"""Build a public API inventory from GP text and pristine OP-TEE headers.

The inputs are developer references; PDFs and upstream source trees are not
distributed with the application. Only identifiers and API signatures are stored.
"""
import argparse
import json
from pathlib import Path
import re


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--core-text', required=True, type=Path)
    parser.add_argument('--headers', required=True, type=Path)
    parser.add_argument('--output', default='assets/catalog.json', type=Path)
    args = parser.parse_args()
    core = args.core_text.read_text(encoding='utf-8')
    defines = (args.headers/'tee_api_defines.h').read_text(encoding='utf-8')
    ids = dict(re.findall(r'^#define\s+(TEE_ALG_\w+)\s+(0x[0-9A-Fa-f]+)', defines, re.M))
    ids = {name: value for name, value in ids.items() if name in core and name != 'TEE_ALG_ILLEGAL_VALUE'}
    sizes = {'MD5': 16, 'SHA1': 20, 'SHA224': 28, 'SHA256': 32, 'SHA384': 48, 'SHA512': 64,
             'SHA3_224': 28, 'SHA3_256': 32, 'SHA3_384': 48, 'SHA3_512': 64, 'SM3': 32}

    def describe(name, value):
        base = name.removeprefix('TEE_ALG_')
        digest = next((s for s in sorted(sizes, key=len, reverse=True) if base.endswith(s)), 'SHA256')
        block = 16
        bits = 128
        key = ''
        if base in sizes or base.startswith('SHAKE'):
            family = 'digest'; bits = 0
        elif base.startswith('HMAC_'):
            family = 'mac'; key = 'TEE_TYPE_HMAC_' + digest; bits = 256; block = 1
        elif '_MAC_' in base or base.endswith('CMAC'):
            family = 'mac'; key = 'TEE_TYPE_' + base.split('_')[0]
        elif base in ('AES_GCM', 'AES_CCM'):
            family = 'ae'; key = 'TEE_TYPE_AES'
        elif base.startswith(('AES_', 'DES_', 'DES3_', 'SM4_')):
            family = 'cipher'; key = 'TEE_TYPE_' + base.split('_')[0]
        elif base.startswith(('RSAES_', 'RSA_NOPAD')) or base == 'SM2_PKE':
            family = 'encrypt'; key = 'TEE_TYPE_RSA_KEYPAIR' if base.startswith('RSA') else 'TEE_TYPE_SM2_PKE_KEYPAIR'
            bits = 2048 if base.startswith('RSA') else 256
        elif base.startswith(('RSASSA_', 'ECDSA_', 'DSA_', 'ED')) or base == 'SM2_DSA_SM3':
            family = 'sign'
            stem = 'RSA' if base.startswith('RSA') else base.split('_')[0]
            if base == 'SM2_DSA_SM3': stem = 'SM2_DSA'
            key = 'TEE_TYPE_' + stem + '_KEYPAIR'
            bits = 2048 if stem in ('RSA', 'DSA') else 448 if stem == 'ED448' else 256
            if base == 'DSA_SHA1': bits = 1024
        else:
            family = 'derive'
            stem = base.split('_')[0]
            key = 'TEE_TYPE_' + (stem if stem == 'HKDF' else ('SM2_KEP' if stem == 'SM2' else stem) + '_KEYPAIR')
            bits = 2048 if stem == 'DH' else 448 if stem == 'X448' else 256
        if base.startswith('DES3_'): bits = 192; block = 8
        elif base.startswith('DES_'): bits = 64; block = 8
        return dict(Id=name, Value=value, Family=family, Digest='TEE_ALG_'+digest, KeyType=key,
                    KeyBits=bits, DigestBytes=sizes[digest], BlockSize=block, Legacy=False)

    algorithms = [describe(name, value) for name, value in sorted(ids.items())]
    apis = []
    for filename, group in [('tee_internal_api.h', 'Core'), ('tee_client_api.h', 'Client')]:
        text = (args.headers/filename).read_text(encoding='utf-8')
        text = re.sub(r'/\*.*?\*/', '', text, flags=re.S)
        text = re.sub(r'\bTA_EXPORT\b', '', text)
        for match in re.finditer(r'\b(?:(?:const\s+)?void\s*\*?|TEE_Result|TEEC_Result|bool|int32_t|uint32_t|size_t)\s*\b((?:TA|TEE|TEEC)_\w+)\s*\([^;{}]*\)\s*;', text):
            name = match[1]
            if group == 'Core' and name not in core: continue
            if group == 'Client' and name not in {'TEEC_InitializeContext','TEEC_FinalizeContext','TEEC_OpenSession','TEEC_CloseSession','TEEC_InvokeCommand','TEEC_RequestCancellation','TEEC_RegisterSharedMemory','TEEC_AllocateSharedMemory','TEEC_ReleaseSharedMemory'}: continue
            if name in {'TEE_Panic', 'TEE_GetObjectInfo', 'TEE_RestrictObjectUsage', 'TEE_CopyObjectAttributes', 'TEE_CloseAndDeletePersistentObject', 'TEE_BigIntInitFMMContext'}: continue
            if any(api['Name'] == name for api in apis): continue
            apis.append(dict(Name=name, Group=group, Prototype=' '.join(match[0].split()), Optional=False,
                             Deprecated=name in {'TEE_GetObjectInfo','TEE_RestrictObjectUsage','TEE_CopyObjectAttributes','TEE_CloseAndDeletePersistentObject','TEE_BigIntInitFMMContext'}))
    apis.append(dict(Name='TEE_BigIntSizeInU32', Group='Core', Prototype='TEE_BigIntSizeInU32(n)', Optional=False, Deprecated=False))
    curves = [dict(Id=name, Value=value, Bits=int(bits), Generic=generic == 'Y')
              for name, generic, value, bits in re.findall(r'(TEE_ECC_CURVE_\w+)\s+(?:NIST|BSI-R|BSI-T|IETF|OCTA)\s+([YN])\s+(0x[0-9a-fA-F]+)\s+(\d+)\s+bits', core)]
    result = dict(Algorithms=algorithms, Curves=curves, Apis=sorted(apis,key=lambda a:a['Name']), Sources={
        'Core':'https://globalplatform.org/wp-content/uploads/2021/03/GPD_TEE_Internal_Core_API_Specification_v1.3.1_PublicRelease_CC.pdf',
        'Client':'https://globalplatform.org/wp-content/uploads/2010/07/TEE_Client_API_Specification-V1.0.pdf',
        'Errata':'https://globalplatform.org/specs-library/tee-client-api-specification-v1-0-errata-and-precisions-v2/',
        'Headers':'https://github.com/OP-TEE/optee_os/tree/4.10.0/lib/libutee/include',
        'ClientHeaders':'https://github.com/OP-TEE/optee_client/tree/4.10.0/libteec/include'})
    from core14_catalog import upgrade
    result = upgrade(result)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n',encoding='utf-8')
    print(f'{len(algorithms)} current algorithms, {len(apis)} API functions/macros')


if __name__ == '__main__':
    main()
