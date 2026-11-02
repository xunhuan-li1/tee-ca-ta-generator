"""Exercise Core 1.4 parameter sets and reject unsafe/ambiguous configurations."""
import argparse
import copy
import json
from pathlib import Path
import subprocess
import uuid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    out = args.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    exe = str(args.exe.resolve())
    def invoke(*args):
        return subprocess.run([exe, *map(str, args)], timeout=120).returncode
    assert invoke('--describe', out/'catalog.json') == 0
    catalog = json.loads((out/'catalog.json').read_text())
    base = dict(FormatVersion=1, Name='gp14_options', Uuid=str(uuid.uuid4()), HeapKiB=2048,
                PanicSafe=True, Features=[])
    def feature(kind, algorithm, **options):
        return dict(Kind=kind, Algorithm='TEE_ALG_'+algorithm, Options=options)
    for name, sizes in [('MLDSA', [44,65,87]), ('FALCON', [512,1024]), ('LMS', [0]),
                        ('MLKEM', [512,768,1024]), ('NTRU', [509,677,701,821])]:
        for size in sizes:
            base['Features'].append(feature('sign' if name in ('MLDSA','FALCON','LMS') else 'kem', name, keyBits=str(size)))
    for size in (640,976,1344):
        for prg in ('SHAKE128','SHAKE256','AES_ECB_NOPAD'):
            for ephemeral in ('false','true'):
                base['Features'].append(feature('kem','FRODOKEM',keyBits=str(size),frodoPrg='TEE_ALG_'+prg,frodoEphemeral=ephemeral))
    for name in ('AES_CBC_PKCS5','DES_CBC_PKCS5','DES3_CBC_PKCS5','CHACHA20'):
        for key in ('input','generate'):
            for streaming in ('false','true'):
                base['Features'].append(feature('cipher',name,keySource=key,streaming=streaming,ivSource='input'))
    base['Features'].append(feature('cipher','CHACHA20',ivSource='input',iv='0001020304050607'))
    for name in ('SHA256_192','SHAKE256_192'):
        base['Features'].append(feature('digest',name,copyReuse='true',streaming='true'))
    for size in (6144,7680,8192):
        base['Features'].append(feature('encrypt','RSAES_PKCS1_OAEP_MGF1_SHA256',keyBits=str(size)))
        base['Features'].append(feature('sign','RSASSA_PKCS1_PSS_MGF1_SHA256',keyBits=str(size)))
    for name,size in [('HMAC_SHA3_384',1088),('HMAC_SHA3_512',1152)]:
        base['Features'].append(feature('mac',name,keyBits=str(size)))
    for preset in catalog['PropertyPresets']:
        if preset['Name'] in ('gpd.client.path','gpd.client.pathHash','gpd.tee.implementationStack','gpd.tee.maskState'):
            base['Features'].append(dict(Kind='property',Options={'set':preset['Set'],'name':preset['Name'],'type':preset['Type']}))
    base['Features'].append(dict(Kind='remote_session',Options=dict(targetUuid=base['Uuid'],remoteTee='remote.tee')))
    path=out/'matrix.json';path.write_text(json.dumps(base))
    assert invoke('--generate',path,out/'matrix',out/'matrix-report.json')==0, (out/'matrix-report.json').read_text()
    sources='\n'.join(f.read_text() for f in (out/'matrix/ta').rglob('*.c'))
    assert 'TEE_DeriveKey(' not in sources
    assert 'TEE_GenerateRandom(' not in sources
    assert 'TEE_MaskPanics(true)' in sources
    assert '#define GP_OUTPUT_BYTES 24' in sources
    cases=[]
    for algorithm in catalog['Algorithms']:
        if algorithm.get('Limitation'):
            cases.append(dict(Kind=algorithm['Family'],Algorithm=algorithm['Id'],Options={}))
    for kind,name,options in [
        ('kem','MLKEM',dict(keyBits='513')),
        ('kem','MLKEM',dict(keySource='input')),
        ('sign','MLDSA',dict(keyBits='88')),
        ('sign','FALCON',dict(keyBits='768')),
        ('cipher','CHACHA20',dict(keyBits='128')),
        ('cipher','CHACHA20',dict(ivSource='input',iv='00'*16)),
        ('ae','AES_GCM',dict(tagBits='64')),
        ('ae','AES_GCM',dict(tagBits='32')),
        ('encrypt','RSA_NOPAD',dict(keyBits='8193')),
        ('mac','HMAC_SHA3_384',dict(keyBits='1096')),
        ('mac','HMAC_SHA3_512',dict(keyBits='1160')),
    ]: cases.append(feature(kind,name,**options))
    for index,f in enumerate(cases):
        config=copy.deepcopy(base);config['Features']=[f]
        path=out/f'invalid-{index}.json';path.write_text(json.dumps(config))
        assert invoke('--validate',path,out/f'invalid-{index}-report.json')==1, f
    report=dict(Passed=True,GeneratedCases=len(base['Features']),RejectedCases=len(cases),
                RuntimeTested=False,Note='Run core14_syntax.py on matrix separately.')
    (out/'summary.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
