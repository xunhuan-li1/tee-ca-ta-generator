"""Compile a generated project using an existing upstream OP-TEE WSL checkout.

Builds in a new Linux temporary directory without changing the toolchain,
OP-TEE configuration, signing keys or original generated source directory.
"""
import argparse
import io
import json
import re
from pathlib import Path
import shlex
import subprocess
import tarfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', required=True, type=Path)
    parser.add_argument('--optee-root', required=True)
    parser.add_argument('--distribution', default='Ubuntu')
    parser.add_argument('--report', required=True, type=Path)
    args = parser.parse_args()
    args.report.parent.mkdir(parents=True, exist_ok=True)

    def run(*command, **kwargs):
        return subprocess.run(['wsl.exe', '-d', args.distribution, '--', *command],
                              capture_output=True, **kwargs)

    result = run('mktemp', '-d', '-t', 'gp-generator-XXXXXXXX', timeout=30)
    result.check_returncode()
    directory = result.stdout.decode().strip()
    archive = io.BytesIO()
    with tarfile.open(fileobj=archive, mode='w') as bundle:
        for path in sorted(args.project.rglob('*')):
            if path.is_file():
                bundle.add(path, arcname=path.relative_to(args.project).as_posix())
    result = run('tar', '-xf', '-', '-C', directory, input=archive.getvalue(), timeout=60)
    result.check_returncode()
    base = args.optee_root.rstrip('/')
    kit = base + '/optee_os/out/arm/export-ta_arm64'
    ta_compiler = base + '/toolchains/aarch64/bin/aarch64-linux-gnu-'
    ca_compiler = base + '/out-br/host/bin/aarch64-linux-gnu-'
    export = base + '/out-br/staging/usr'
    reports = []
    for kind, command in [('ta', ['make', '-C', directory+'/ta', '-j4', 'TA_DEV_KIT_DIR='+kit, 'CROSS_COMPILE='+ta_compiler]),
                          ('ca', ['make', '-C', directory+'/ca', '-j4', 'TEEC_EXPORT='+export, 'CROSS_COMPILE='+ca_compiler])]:
        if not (args.project/kind).is_dir():
            continue
        result = run(*command, timeout=300)
        log = args.report.with_name(args.report.stem+'-'+kind+'.log')
        log.write_bytes(result.stdout + result.stderr)
        reports.append({'Kind':kind,'ExitCode':result.returncode,'Log':str(log),'Command':shlex.join(command)})
        print(kind, result.returncode, str(log), flush=True)
    checks = []
    if all(r['ExitCode'] == 0 for r in reports):
        makefile = (args.project/'ta/Makefile').read_text()
        ta_uuid = re.search(r'^BINARY := (.+)$', makefile, re.M)[1]
        result = run('test','-s',directory+'/ta/'+ta_uuid+'.ta',timeout=30)
        checks.append({'Kind':'signed-ta-exists','Passed':result.returncode == 0})
        if (args.project/'ca').is_dir():
            makefile = (args.project/'ca/Makefile').read_text()
            target = re.search(r'^TARGET := (.+)$',makefile,re.M)[1]
            artifact = directory+'/ca/'+target
            if target.endswith(('.a','.so')):
                header=(args.project/'ca/client_api.h').read_text()
                named=re.search(r'TEEC_Result\s+(gp_client_\w+)\s*\(',header)[1]
                structure=re.search(r'struct (gp_\w+_request)\s*\{',header)[1]
                consumer = ('#include "client_api.h"\nint main(void) { struct gp_client c = {0}; '
                            'struct '+structure+' q = {0}; struct gp_response r; uint32_t o = 0; '
                            'if (gp_client_open(&c,&o) != TEEC_SUCCESS) return 2; '
                            'TEEC_Result s = '+named+'(&c,&q,&r,&o); '
                            'gp_client_close(&c); return s != TEEC_SUCCESS; }\n')
                command = [ca_compiler+'gcc','-x','c','-','-x','none','-I'+directory+'/ca',
                           '-I'+directory+'/common','-I'+export+'/include',artifact,
                           '-L'+export+'/lib','-lteec','-pthread','-Wl,--no-undefined',
                           '-o',directory+'/ca/consumer']
                result = run(*command,input=consumer.encode(),timeout=60)
                log=args.report.with_name(args.report.stem+'-consumer.log')
                log.write_bytes(result.stdout+result.stderr)
                checks.append({'Kind':'library-consumer-link','Passed':result.returncode == 0,'Log':str(log)})
            result = run(ca_compiler+'readelf','-h',artifact,timeout=30)
            checks.append({'Kind':'aarch64-output','Passed':result.returncode == 0 and b'AArch64' in result.stdout})
            if target.endswith('.so'):
                result=run(ca_compiler+'readelf','-d',artifact,timeout=30)
                checks.append({'Kind':'separate-libteec-dependency','Passed':result.returncode == 0 and b'libteec.so' in result.stdout})
            elif target.endswith('.a'):
                result=run(ca_compiler+'nm','-u',artifact,timeout=30)
                checks.append({'Kind':'libteec-not-merged','Passed':result.returncode == 0 and b'TEEC_InvokeCommand' in result.stdout})
    result = {'Passed':all(r['ExitCode']==0 for r in reports) and all(c['Passed'] for c in checks),
              'LinuxDirectory':directory,'Builds':reports,'Checks':checks}
    args.report.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    raise SystemExit(0 if result['Passed'] else 1)


if __name__ == '__main__':
    main()
