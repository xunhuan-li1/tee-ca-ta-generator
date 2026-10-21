"""Compile and execute generated projects in an isolated WSL/QEMU OP-TEE guest.

Only temporary copies are instrumented. Existing firmware, rootfs, keys and
project files are left untouched. GP calls are traced to distinguish runtime
coverage from static identifier coverage.
"""
import argparse
import io
import json
from pathlib import Path
import re
import subprocess
import tarfile


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--projects',nargs='+',type=Path,required=True)
    parser.add_argument('--optee-root',required=True)
    parser.add_argument('--fixture-uuid',required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--no-trace',action='store_true',help='Run generated sources without instrumentation')
    parser.add_argument('--timeout',type=int,default=45)
    args=parser.parse_args()
    out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
    base=args.optee_root.rstrip('/')
    def run(*cmd,**kw):
        return subprocess.run(['wsl.exe','-d','Ubuntu','--',*cmd],capture_output=True,**kw)
    r=run('mktemp','-d','-t','gp-runtime-XXXXXXXX',timeout=30);r.check_returncode();remote=r.stdout.decode().strip()
    (out/'linux-directory.txt').write_text(remote)
    catalog=json.loads((Path(__file__).resolve().parents[1]/'assets/catalog.json').read_text(encoding='utf-8-sig'))
    def header(group):
        apis=[a for a in catalog['Apis'] if a['Group']==group and a['Name'].startswith('TEE_' if group=='Core' else 'TEEC_') and a['Name']!='TEE_BigIntSizeInU32']
        result='#ifndef GP_RUNTIME_TRACE_H\n#define GP_RUNTIME_TRACE_H\n'
        result+='#include <trace.h>\n' if group=='Core' else '#include <stdio.h>\n'
        for a in apis:
            name=a['Name']
            mark='EMSG("GPCALL:'+name+'");' if group=='Core' else 'fprintf(stderr,"GPCALL:'+name+'\\n");'
            fail='EMSG("GPFAIL:'+name+':0x%08x",gp_runtime_result_);' if group=='Core' else 'fprintf(stderr,"GPFAIL:'+name+':0x%08x\\n",gp_runtime_result_);'
            if a['Prototype'].startswith(('TEE_Result','TEEC_Result')):
                typ='TEE_Result' if group=='Core' else 'TEEC_Result'
                result+='#define '+name+'(...) ({ '+mark+' '+typ+' gp_runtime_result_ = '+name+'(__VA_ARGS__); if (gp_runtime_result_) { '+fail+' } gp_runtime_result_; })\n'
            else:
                marker='EMSG("GPCALL:'+name+'")' if group=='Core' else 'fprintf(stderr,"GPCALL:'+name+'\\n")'
                # EMSG is a statement macro; wrap it in a GNU statement expression for void/non-Result APIs.
                result+='#define '+name+'(...) ({ '+marker+'; '+name+'(__VA_ARGS__); })\n'
        return result+'#endif\n'
    payload={};manifest={'Projects':[],'Cases':[],'Timeout':args.timeout}
    for project in args.projects:
        name=project.name
        config=json.loads(project.with_suffix('.json').read_text(encoding='utf-8-sig'))
        executable=re.search(r'^TARGET := (.+)$',(project/'ca/Makefile').read_text(encoding='utf-8-sig'),re.M)[1]
        main=(project/'ca/main.c').read_text(encoding='utf-8-sig')
        commands=re.findall(r'\{\s*"([^"\n]+)"\s*,\s*gp_example_',main)
        assert len(commands)==len(config['Features']),(name,len(commands),len(config['Features']))
        manifest['Projects'].append({'Directory':name,'Executable':executable})
        for index,(command,feature) in enumerate(zip(commands,config['Features']),1):
            manifest['Cases'].append({'Id':name+'-'+str(index),'Project':name,'Command':command,'Feature':feature})
        for file in project.rglob('*'):
            if not file.is_file():continue
            rel=file.relative_to(project).as_posix();data=file.read_bytes()
            if not args.no_trace and rel in ('ta/include/gp_support.h','ca/client.h'):
                text=file.read_text(encoding='utf-8-sig');pos=text.rfind('#endif');data=(text[:pos]+'#include "gp_runtime_trace.h"\n'+text[pos:]).encode()
            elif not args.no_trace and rel=='ta/ta_entry.c':
                text=file.read_text(encoding='utf-8-sig')
                for a in catalog['Apis']:
                    if a['Name'].startswith('TA_'):
                        text=re.sub(r'(\b'+a['Name']+r'\([^;]*?\)\s*\{\n)',r'\1    EMSG("GPENTRY:'+a['Name']+r'");\n',text)
                data=text.encode()
            payload[name+'/'+rel]=data
        payload[name+'/ta/include/gp_runtime_trace.h']=header('Core').encode()
        payload[name+'/ca/gp_runtime_trace.h']=header('Client').encode()
    uid=args.fixture_uuid.replace('-','')
    uuid_c='{ 0x'+uid[:8]+', 0x'+uid[8:12]+', 0x'+uid[12:16]+', { '+', '.join('0x'+uid[i:i+2] for i in range(16,32,2))+' } }'
    payload['fixture/Makefile']=('BINARY := '+args.fixture_uuid+'\ninclude $(TA_DEV_KIT_DIR)/mk/ta_dev_kit.mk\n').encode()
    payload['fixture/sub.mk']=b'global-incdirs-y += .\nsrcs-y += entry.c\n'
    payload['fixture/user_ta_header_defines.h']=('#include <tee_internal_api.h>\n#define TA_UUID '+uuid_c+'\n#define TA_FLAGS 0\n#define TA_STACK_SIZE (16 * 1024)\n#define TA_DATA_SIZE (32 * 1024)\n#define TA_VERSION "1"\n#define TA_DESCRIPTION "Runtime session fixture"\n').encode()
    payload['fixture/entry.c']=b"""#include <tee_internal_api.h>
TEE_Result TA_CreateEntryPoint(void) { return TEE_SUCCESS; }
void TA_DestroyEntryPoint(void) {}
TEE_Result TA_OpenSessionEntryPoint(uint32_t t, TEE_Param p[4], void **c) { (void)p; *c=0; return t == 0 ? TEE_SUCCESS : TEE_ERROR_BAD_PARAMETERS; }
void TA_CloseSessionEntryPoint(void *c) { (void)c; }
TEE_Result TA_InvokeCommandEntryPoint(void *c, uint32_t id, uint32_t t, TEE_Param p[4]) { (void)c;(void)p; return id == 0 && t == 0 ? TEE_SUCCESS : TEE_ERROR_BAD_PARAMETERS; }
"""
    payload['timeout.c']=b"""#define _POSIX_C_SOURCE 200809L
#include <errno.h>
#include <signal.h>
#include <stdlib.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
static volatile sig_atomic_t expired;
static pid_t child_pid;
static void deadline(int signum) { (void)signum; expired=1; if(child_pid>0) kill(-child_pid,SIGKILL); }
int main(int argc,char **argv) {
    int status; struct sigaction action={0};
    if(argc<3) return 125;
    action.sa_handler=deadline; sigemptyset(&action.sa_mask); sigaction(SIGALRM,&action,NULL);
    child_pid=fork();
    if(child_pid<0) return 125;
    if(child_pid==0) { setpgid(0,0); execv(argv[2],argv+2); _exit(127); }
    setpgid(child_pid,child_pid); alarm((unsigned int)strtoul(argv[1],NULL,10));
    while(waitpid(child_pid,&status,0)<0) if(errno!=EINTR) return 125;
    alarm(0); if(expired) return 124;
    return WIFEXITED(status)?WEXITSTATUS(status):128+WTERMSIG(status);
}
"""
    payload['runner.py']=Path(__file__).with_name('qemu_guest_runner.py').read_bytes()
    payload['manifest.json']=json.dumps(manifest).encode()
    archive=io.BytesIO()
    with tarfile.open(fileobj=archive,mode='w') as bundle:
        for name,data in payload.items():
            info=tarfile.TarInfo(name);info.size=len(data);info.mode=0o644;bundle.addfile(info,io.BytesIO(data))
    run('tar','-xf','-','-C',remote,input=archive.getvalue(),timeout=60).check_returncode()
    for name in [p.name for p in args.projects]+['fixture']:
        for kind in (['ta','ca'] if name!='fixture' else ['ta']):
            directory=remote+'/'+name+('/'+kind if name!='fixture' else '')
            cmd=['make','-C',directory,'-j4']
            if kind=='ta':cmd+=['TA_DEV_KIT_DIR='+base+'/optee_os/out/arm/export-ta_arm64','CROSS_COMPILE='+base+'/toolchains/aarch64/bin/aarch64-linux-gnu-']
            else:cmd+=['TEEC_EXPORT='+base+'/out-br/staging/usr','CROSS_COMPILE='+base+'/out-br/host/bin/aarch64-linux-gnu-']
            r=run(*cmd,timeout=300);(out/(name+'-'+kind+'.log')).write_bytes(r.stdout+r.stderr);r.check_returncode();print('Built '+name+' '+kind,flush=True)
    run(base+'/out-br/host/bin/aarch64-linux-gnu-gcc','-std=c11','-Wall','-Wextra','-Werror',remote+'/timeout.c','-o',remote+'/gp_timeout',timeout=60).check_returncode()
    command=['wsl.exe','-d','Ubuntu','--','python3',remote+'/runner.py',base,remote]
    r=subprocess.run(command,timeout=3800)
    for name,source in [('runtime.json','runtime.json'),('normal-world.log','normal-world.log'),('secure-world.log','boot/serial1.log')]:
        result=run('cat',remote+'/'+source,timeout=60)
        if result.returncode==0:(out/name).write_bytes(result.stdout)
    r.check_returncode()
    report=json.loads((out/'runtime.json').read_text())
    expected={a['Name'] for a in catalog['Apis'] if a['Name']!='TEE_BigIntSizeInU32'}
    report['Instrumented']=not args.no_trace
    report['MissingApis']=sorted(expected-set(report['ObservedApis'])) if not args.no_trace else []
    report['LinuxDirectory']=remote
    (out/'runtime.json').write_text(json.dumps(report,indent=2))
    print(json.dumps({'Cases':len(report['Cases']),'Success':sum(c['ExitCode']==0 for c in report['Cases']),'MissingApis':report['MissingApis'],'LinuxDirectory':remote},indent=2))

if __name__=='__main__':main()
