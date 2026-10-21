"""Linux-side isolated QEMU execution of already built generator test projects."""
import gzip
import json
import os
from pathlib import Path
import re
import selectors
import shlex
import shutil
import subprocess
import sys
import time

root = Path(sys.argv[1]).resolve()
work = Path(sys.argv[2]).resolve()
manifest = json.loads((work/'manifest.json').read_text())
boot = work/'boot'
boot.mkdir()
for source in (root/'out/bin').iterdir():
    if source.name not in ('rootfs.cpio.gz','rootfs.cpio.uboot','serial0.log','serial1.log'):
        (boot/source.name).symlink_to(source.resolve())
overlay = work/'overlay'
(overlay/'lib/optee_armtz').mkdir(parents=True)
(overlay/'opt/gp/bin').mkdir(parents=True)
for project in manifest['Projects']:
    directory = work/project['Directory']
    for ta in (directory/'ta').glob('*.ta'):
        shutil.copyfile(ta,overlay/'lib/optee_armtz'/ta.name)
    shutil.copy2(directory/'ca'/project['Executable'],overlay/'opt/gp/bin'/project['Directory'])
for ta in (work/'fixture').glob('*.ta'):
    shutil.copyfile(ta,overlay/'lib/optee_armtz'/ta.name)
shutil.copy2(work/'gp_timeout',overlay/'opt/gp/bin/gp_timeout')
script = ['#!/bin/sh', 'optee_example_hello_world', 'echo __GP_READY']
for case in manifest['Cases']:
    script += ['echo __GP_BEGIN:'+case['Id'],
               '/opt/gp/bin/gp_timeout '+str(manifest['Timeout'])+' /opt/gp/bin/'+shlex.quote(case['Project'])+' '+shlex.quote(case['Command']),
               'rc=$?', 'echo __GP_END:'+case['Id']+':$rc']
script += ['echo __GP_DONE']
(overlay/'opt/gp/run.sh').write_text('\n'.join(script)+'\n')
paths = ['.'] + sorted(str(p.relative_to(overlay)) for p in overlay.rglob('*'))
cpio = subprocess.run(['cpio','--null','-o','--format=newc','--owner=0:0'], cwd=overlay,
                      input=('\0'.join(paths)+'\0').encode(),capture_output=True,check=True).stdout
with gzip.open(root/'out-br/images/rootfs.cpio.gz','rb') as source, gzip.open(boot/'rootfs.cpio.gz','wb') as dest:
    shutil.copyfileobj(source,dest)
    dest.write(cpio)
subprocess.run([str(root/'u-boot/tools/mkimage'),'-A','arm64','-O','linux','-T','ramdisk','-C','gzip','-a','0x45000000','-e','0x45000000','-n','Generator runtime tests','-d',str(boot/'rootfs.cpio.gz'),str(boot/'rootfs.cpio.uboot')],check=True,stdout=subprocess.DEVNULL)
result = subprocess.run(['make','--no-print-directory','-n','-C',str(root/'build'),'-f',str(root/'console.mk'),
                         'RUST_ENABLE=n','MEASURED_BOOT_FTPM=n','tee-console'],capture_output=True,text=True,check=True)
line = next(line for line in result.stdout.splitlines() if 'qemu-system-aarch64' in line and '&&' in line)
command = shlex.split(line.split('&&',1)[1])
(work/'qemu-command.json').write_text(json.dumps(command,indent=2))
process = subprocess.Popen(command,cwd=boot,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,bufsize=0)
selector=selectors.DefaultSelector()
selector.register(process.stdout,selectors.EVENT_READ)
transcript=''
started=time.monotonic()
phase='boot'
reported=set()
try:
    with (work/'normal-world.log').open('wb') as log:
        while time.monotonic()-started < manifest.get('TotalTimeout',3600):
            events=selector.select(1)
            if not events:
                if process.poll() is not None: raise RuntimeError('QEMU exited early: '+str(process.returncode))
                continue
            data=os.read(process.stdout.fileno(),65536)
            if not data: raise RuntimeError('QEMU output closed')
            log.write(data);log.flush()
            transcript+=data.decode(errors='replace').replace('\r','')
            if phase=='boot' and 'login:' in transcript:
                process.stdin.write(b'root\n');process.stdin.flush();phase='login';transcript=''
            elif phase=='login' and re.search(r'# $',transcript):
                process.stdin.write(b'sh /opt/gp/run.sh\n');process.stdin.flush();phase='tests';transcript=''
                print('Guest booted; running '+str(len(manifest['Cases']))+' cases',flush=True)
            if time.monotonic()-started > 180 and not reported and '__GP_READY' not in transcript:
                raise RuntimeError('Guest test script failed to start')
            if phase=='tests' and re.search(r'# $',transcript) and '__GP_READY' not in transcript:
                raise RuntimeError('Guest test script returned before initialization')
            if phase=='tests':
                for match in re.finditer(r'(?:^|\n)__GP_END:([^:\n]+):(\d+)\n',transcript):
                    case_id,code=match.groups()
                    if case_id not in reported:
                        reported.add(case_id)
                        print('case '+case_id+' exit='+code+' ('+str(len(reported))+'/'+str(len(manifest['Cases']))+')',flush=True)
                if '\n__GP_DONE\n' in transcript: break
        else: raise TimeoutError('QEMU runtime suite timed out')
finally:
    if process.poll() is None:
        try:
            process.stdin.write(b'\x01x');process.stdin.flush();process.wait(timeout=10)
        except (OSError,subprocess.TimeoutExpired):
            process.terminate()
            try:process.wait(timeout=5)
            except subprocess.TimeoutExpired:process.kill();process.wait()
    selector.close()
raw=(work/'normal-world.log').read_text(errors='replace').replace('\r','')
results=[]
for case in manifest['Cases']:
    match=re.search(r'(?:^|\n)__GP_BEGIN:'+re.escape(case['Id'])+r'\n(.*?)\n__GP_END:'+re.escape(case['Id'])+r':(\d+)\n',raw,re.S)
    result=dict(case)
    result.update(ExitCode=int(match[2]) if match else None,Output=match[1] if match else '')
    results.append(result)
secure=(boot/'serial1.log').read_text(errors='replace')
observed=sorted(set(re.findall(r'GP(?:CALL|ENTRY):((?:TEE|TEEC|TA)_\w+)',secure+'\n'+raw)))
failures=re.findall(r'GPFAIL:([^\r\n]+)',secure)
report={'Completed':len(reported)==len(results),'ElapsedSeconds':round(time.monotonic()-started,1),'Cases':results,
        'ObservedApis':observed,'GpFailures':failures,'NormalLog':str(work/'normal-world.log'),'SecureLog':str(boot/'serial1.log')}
(work/'runtime.json').write_text(json.dumps(report,indent=2))
print('Completed; successful='+str(sum(r['ExitCode']==0 for r in results))+'/'+str(len(results)),flush=True)
