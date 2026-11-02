"""Syntax-check generated CA/TA code in WSL using formal Core 1.4 declarations.

The installed OP-TEE 4.10 kit lacks these APIs. This fixture supplies declarations
and published constants ONLY. It cannot link or run, and is never exported by
the generator. A passing result is not an implementation or runtime test.
"""
import argparse
import io
import json
from pathlib import Path
import subprocess
import tarfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--projects', nargs='+', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--optee-root', default='/home/company_desktop/optee')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    catalog = json.loads((root/'assets/catalog.json').read_text())
    changed = ['TA_InvokeCommandEntryPoint', 'TEE_AEDecryptFinal', 'TEE_AEEncryptFinal',
               'TEE_AEUpdate', 'TEE_CipherDoFinal', 'TEE_CipherUpdate', 'TEE_DigestDoFinal',
               'TEE_MACComputeFinal', 'TEE_AllocateTransientObject', 'TEE_GetInstanceData',
               'TEE_CheckMemoryAccessRights', 'TEE_MemFill', 'TEE_SetTAPersistentTime']
    fixture = ''.join(f'#define {name} gp13_{name}\n' for name in changed)
    fixture += '#include <tee_internal_api.h>\n'
    fixture += ''.join(f'#undef {name}\n' for name in changed)
    fixture += '#undef TEE_CORE_API_MINOR_VERSION\n#define TEE_CORE_API_MINOR_VERSION 4\n'
    for algorithm in catalog['Algorithms']:
        fixture += f"#undef {algorithm['Id']}\n#define {algorithm['Id']} {algorithm['Value']}\n"
    constants = dict(TEE_MODE_ENCAPSULATE='0x00000007', TEE_MODE_DECAPSULATE='0x00000008',
                     TEE_TYPE_CHACHA20='0xA0000015', TEE_ATTR_LMOTS_PARAMSET_ID='0xF0000D04',
                     TEE_ATTR_LMS_PARAMSET_ID='0xF0000D05', TEE_ATTR_FRODOKEM_PRG='0xF0000D0A',
                     TEE_ATTR_FRODOKEM_EPHEMERAL='0xF0000D0B')
    for offset, name in enumerate(['LMS', 'XMSS', 'SLHDSA', 'MLDSA', 'FALCON', 'MLKEM', 'FRODOKEM', 'NTRU'], 0x50):
        constants['TEE_TYPE_'+name+'_PUBLIC_KEY'] = f'0x{0xA0000000+offset:08X}'
        constants['TEE_TYPE_'+name+'_KEYPAIR'] = f'0x{0xA1000000+offset:08X}'
    for name, value in constants.items():
        fixture += f'#ifndef {name}\n#define {name} {value}\n#endif\n'
    fixture += '#undef TEE_ATTR_RSA_OAEP_MGF_HASH\n#define TEE_ATTR_RSA_OAEP_MGF_HASH 0xF0000931\n'
    for api in catalog['Apis']:
        if api['Name'].endswith('_PS') or api['Name'] in changed or api['Name'] in ('TEE_MaskPanics', 'TEE_OpenRemoteTASession', 'TEE_EncapsulateKey', 'TEE_DecapsulateKey', 'TA_InvokeCommandEntryPoint'):
            fixture += api['Prototype']+'\n'
    payload = {'core14_declarations.h': fixture.encode()}
    for project in args.projects:
        for file in project.rglob('*'):
            if file.is_file():
                payload[project.name+'/'+file.relative_to(project).as_posix()] = file.read_bytes()
    script = '''import json, pathlib, subprocess, sys
root=pathlib.Path.cwd();base=sys.argv[1];rows=[]
for project in sorted(p for p in root.iterdir() if p.is_dir()):
 for kind in ('ta','ca'):
  for source in sorted((project/kind).rglob('*.c')):
   kit=base+'/optee_os/out/arm/export-ta_arm64'
   compiler=base+('/toolchains/aarch64/bin/aarch64-linux-gnu-gcc' if kind=='ta' else '/out-br/host/bin/aarch64-linux-gnu-gcc')
   includes=[project/'common',project/kind,project/kind/'include']
   includes += [kit+'/include'] if kind=='ta' else [base+'/out-br/staging/usr/include']
   cmd=[compiler,'-std=c11','-fsyntax-only','-Wall','-Wextra','-Werror']+['-I'+str(i) for i in includes]
   if kind=='ta':cmd+=['-DARM64=1','-include',str(root/'core14_declarations.h')]
   r=subprocess.run(cmd+[str(source)],capture_output=True,text=True)
   rows.append(dict(File=str(source.relative_to(root)),ExitCode=r.returncode,Errors=r.stderr))
report=dict(Passed=all(r['ExitCode']==0 for r in rows),Validation='declarations and syntax only; no implementation, linking or runtime',TranslationUnits=len(rows),Results=rows)
print(json.dumps(report,indent=2))
sys.exit(0 if report['Passed'] else 1)
'''
    payload['check.py'] = script.encode()
    archive = io.BytesIO()
    with tarfile.open(fileobj=archive, mode='w') as tar:
        for name, data in payload.items():
            item = tarfile.TarInfo(name); item.size = len(data)
            tar.addfile(item, io.BytesIO(data))
    def wsl(*cmd, **kwargs):
        return subprocess.run(['wsl', '-d', 'Ubuntu', '--exec', *cmd], capture_output=True, **kwargs)
    result = wsl('mktemp', '-d', '-t', 'gp14-syntax-XXXXXXXX', timeout=30); result.check_returncode()
    directory = result.stdout.decode().strip()
    wsl('tar', '-xf', '-', '-C', directory, input=archive.getvalue(), timeout=60).check_returncode()
    result = wsl('sh', '-c', 'cd "$1" && python3 check.py "$2"', 'sh', directory, args.optee_root, timeout=600)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_bytes(result.stdout)
    report = json.loads(result.stdout)
    print(json.dumps({k: v for k, v in report.items() if k != 'Results'}))
    for row in report['Results']:
        if row['ExitCode']: print(row['File'], row['Errors'])
    raise SystemExit(result.returncode)


if __name__ == '__main__':
    main()
