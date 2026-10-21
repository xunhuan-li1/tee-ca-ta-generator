"""Exercise OAEP dual hashes, imported RSA CRT fields and property-set choices."""
import argparse
import json
import math
import re
from pathlib import Path
import subprocess
import uuid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    exe = str(args.exe.resolve())
    subprocess.run([exe, '--describe', str(output/'catalog.json')], check=True, timeout=60)
    catalog = json.loads((output/'catalog.json').read_text())
    keys = catalog['PublicExampleKeys']['rsa2048']
    n, e, d = (int(keys[k], 16) for k in ('rsaN','rsaE','rsaD'))
    # Recover CRT components of this public example vector without extra packages.
    odd = e*d-1
    powers = 0
    while odd % 2 == 0:
        powers += 1
        odd //= 2
    prime = 0
    for base in range(2,100):
        value = pow(base,odd,n)
        for _ in range(powers):
            squared = pow(value,2,n)
            if squared == 1 and value not in (1,n-1):
                prime = math.gcd(value-1,n)
                break
            value = squared
        if 1 < prime < n:
            break
    assert 1 < prime < n and n % prime == 0
    q = n//prime
    def encoded(value):
        return value.to_bytes((value.bit_length()+7)//8,'big').hex()
    crt = dict(keys, keySource='input', rsaCrt='true',
               rsaP=encoded(prime), rsaQ=encoded(q), rsaDp=encoded(d%(prime-1)),
               rsaDq=encoded(d%(q-1)), rsaQinv=encoded(pow(q,-1,prime)))
    hashes = ['same','TEE_ALG_SHA1','TEE_ALG_SHA224','TEE_ALG_SHA256','TEE_ALG_SHA384','TEE_ALG_SHA512',
              'TEE_ALG_SHA3_224','TEE_ALG_SHA3_256','TEE_ALG_SHA3_384','TEE_ALG_SHA3_512']
    features = [{'Kind':'encrypt','Algorithm':'TEE_ALG_RSAES_PKCS1_OAEP_MGF1_SHA256',
                 'Options':dict(crt,mgfHash=h,label='00010203')} for h in hashes]
    features += [{'Kind':'property_enum','Options':{'set':s}} for s in ('current_ta','current_client','implementation')]
    project = {'FormatVersion':1,'Name':'parameters','Uuid':str(uuid.uuid4()),'Features':features}
    config = output/'project.json'
    config.write_text(json.dumps(project),encoding='utf-8')
    subprocess.run([exe,'--generate',str(config),str(output/'project'),str(output/'generate.json')],check=True,timeout=90)
    for index,h in enumerate(hashes,1):
        ta = (output/f'project/ta/examples/gp_rsa_encrypt_{index}.c').read_text()
        assert 'TEE_ATTR_RSA_OAEP_MGF_HASH' in ta
        assert '#define GP_MGF_HASH '+('TEE_ALG_SHA256' if h=='same' else h) in ta
        # Some target implementations stop scanning attributes when they find label.
        compact = re.sub(r'\s+', '', ta)
        assert compact.index('TEE_InitRefAttribute(&parameters[parameter_count],TEE_ATTR_RSA_OAEP_MGF_HASH') < compact.index('TEE_InitRefAttribute(&parameters[parameter_count],TEE_ATTR_RSA_OAEP_LABEL')
        assert keys['rsaD'].lower() not in ta.lower()
    source=(output/'project/ca/client_api.c').read_text()
    for suffix in ('prime1','prime2','exponent1','exponent2','coefficient'):
        assert 'rsa_'+suffix in source
    for index,prop in enumerate(('CURRENT_TA','CURRENT_CLIENT','TEE_IMPLEMENTATION'),11):
        ta=(output/f'project/ta/examples/gp_property_enum_{index}.c').read_text()
        assert '#define GP_PROPERTY_SET TEE_PROPSET_'+prop in ta
    project['Features']=[features[0]]
    project['Features'][0]['Options']['rsaQinv']=''
    config = output/'incomplete-crt.json'
    config.write_text(json.dumps(project),encoding='utf-8')
    result=subprocess.run([exe,'--validate',str(config),str(output/'missing-crt.json')],timeout=60)
    assert result.returncode != 0
    (output/'summary.json').write_text(json.dumps({'Passed':True,'OaepHashChoices':len(hashes),'CrtComponents':5,'PropertySets':3,'IncompleteCrtRejected':True},indent=2))
    print((output/'summary.json').read_text())

if __name__ == '__main__':
    main()
