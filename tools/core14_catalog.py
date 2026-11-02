"""Apply the November 2025 Core 1.4 delta to the public 1.3 API inventory.

References: tables 5-9, 6-3, 6-11, 6-13, 6-15 and sections 4.10, 6.8.
Unresolved definitions are query-only; no implementation-specific IDs are invented.
"""
import json
from pathlib import Path


def upgrade(catalog):
    rows = [
        ('AES_CBC_PKCS5', '10000112', 'cipher', 'AES', 128, 16),
        ('DES_CBC_PKCS5', '10000118', 'cipher', 'DES', 64, 8),
        ('DES3_CBC_PKCS5', '10000119', 'cipher', 'DES3', 192, 8),
        ('CHACHA20', '10000016', 'cipher', 'CHACHA20', 256, 1),
        ('CHACHA20_POLY1305', '10000017', 'ae', '', 256, 1),
        ('POLY1305', '30000020', 'mac', 'POLY1305', 128, 1),
        ('SHA256_192', '5000000C', 'digest', '', 0, 1),
        ('SHAKE256_192', '50000103', 'digest', '', 0, 1),
        ('MLDSA', '50000104', 'sign', 'MLDSA_KEYPAIR', 44, 1),
        ('FALCON', '50000105', 'sign', 'FALCON_KEYPAIR', 512, 1),
        ('FRODOKEM', '50000106', 'kem', 'FRODOKEM_KEYPAIR', 640, 1),
        ('NTRU', '50000107', 'kem', 'NTRU_KEYPAIR', 509, 1),
        ('SLHDSA', '50000108', 'sign', 'SLHDSA_KEYPAIR', 128, 1),
        ('MLKEM', '50000109', 'kem', 'MLKEM_KEYPAIR', 512, 1),
        ('LMS', '5000010A', 'sign', 'LMS_KEYPAIR', 0, 1),
        ('LMOTS', '5000010B', 'sign', '', 0, 1),
        ('XMSS', '5000010C', 'sign', 'XMSS_KEYPAIR', 0, 1),
        ('XMSSMT', '5000010D', 'sign', 'XMSS_KEYPAIR', 0, 1),
        ('WOTS', '5000010E', 'sign', '', 0, 1),
    ]
    limitations = {
        'CHACHA20_POLY1305': 'Query only: table 6-4a names a key type absent from table 6-13.',
        'POLY1305': 'Query only: table 5-9 specifies 128-bit keys, inconsistent with the 256-bit key in RFC 8439.',
        'SLHDSA': 'Query only: section 5.7.3 requires FAST/SMALL values not defined numerically in table 6-15.',
        'LMOTS': 'Query only: section 5.7.3 names an object type absent from table 6-13.',
        'XMSS': 'Query only: section 5.7.3 requires WOTS_PARAMSET_ID, but table 6-15 defines WOTSPLUS_PARAMSET_ID.',
        'XMSSMT': 'Query only: section 5.7.3 requires WOTS_PARAMSET_ID, but table 6-15 defines WOTSPLUS_PARAMSET_ID.',
        'WOTS': 'Query only: section 5.7.3 names an object type absent from table 6-13.',
    }
    algorithms = {a['Id']: a for a in catalog['Algorithms']}
    for name, value, family, key, bits, block in rows:
        id_ = 'TEE_ALG_' + name
        algorithms[id_] = dict(Id=id_, Value='0x'+value, Family=family,
            Digest=id_ if family == 'digest' else 'TEE_ALG_SHA256',
            KeyType='TEE_TYPE_'+key if key else '', KeyBits=bits,
            DigestBytes=24 if family == 'digest' else 32, BlockSize=block,
            Legacy=False, Limitation=limitations.get(name, ''))
    algorithms['TEE_ALG_RSASSA_PKCS1_PSS_MGF1_SHA1']['Value'] = '0x7020B930'
    catalog['Algorithms'] = sorted(algorithms.values(), key=lambda a: a['Id'])
    apis = {a['Name']: a for a in catalog['Apis']
            if not a['Name'].startswith('TEE_BigInt') and a['Name'] != 'TEE_DeriveKey'}
    for name in ['ResetOperation', 'CopyOperation', 'DigestUpdate', 'CipherInit',
                 'MACInit', 'MACUpdate', 'AEUpdateAAD', 'GenerateRandom',
                 'GetSystemTime', 'GetREETime']:
        original = apis['TEE_'+name]
        proto = original['Prototype'].replace('void TEE_', 'TEE_Result TEE_', 1)
        proto = proto.replace('TEE_'+name+'(', 'TEE_'+name+'_PS(')
        apis['TEE_'+name+'_PS'] = dict(original, Name='TEE_'+name+'_PS', Prototype=proto)
    prototypes = [
        'TEE_Result TEE_DeriveKey_PS(TEE_OperationHandle operation, const TEE_Attribute *params, uint32_t paramCount, TEE_ObjectHandle derivedKey);',
        'TEE_Result TEE_MaskPanics(bool mask);',
        'TEE_Result TEE_OpenRemoteTASession(const char *remoteTEE, const TEE_UUID *destination, uint32_t cancellationRequestTimeout, uint32_t paramTypes, TEE_Param params[4], TEE_TASessionHandle *session, uint32_t *returnOrigin);',
        'TEE_Result TEE_EncapsulateKey(TEE_OperationHandle operation, uint32_t keySize, const TEE_Attribute *params, uint32_t paramCount, void *outputBuffer, size_t *bufferLen, TEE_ObjectHandle sessionKey);',
        'TEE_Result TEE_DecapsulateKey(TEE_OperationHandle operation, uint32_t keySize, const TEE_Attribute *params, uint32_t paramCount, const void *encapsulatedKey, size_t bufferLen, TEE_ObjectHandle sessionKey);',
    ]
    for proto in prototypes:
        name = proto.split('(')[0].split()[-1]
        apis[name] = dict(Name=name, Group='Core', Prototype=proto, Optional=False, Deprecated=False)
    apis['TA_InvokeCommandEntryPoint']['Prototype'] = 'TEE_Result TA_InvokeCommandEntryPoint(const void *sessionContext, uint32_t commandID, uint32_t paramTypes, TEE_Param params[4]);'
    # Existing signatures also changed; do not inherit old SDK qualifiers.
    for name, parameter in [('TEE_AEDecryptFinal', 'srcData'), ('TEE_AEEncryptFinal', 'srcData'),
                            ('TEE_AEUpdate', 'srcData'), ('TEE_CipherDoFinal', 'srcData'),
                            ('TEE_CipherUpdate', 'srcData'), ('TEE_DigestDoFinal', 'chunk'),
                            ('TEE_MACComputeFinal', 'message')]:
        apis[name]['Prototype'] = apis[name]['Prototype'].replace('const void *'+parameter, 'void *'+parameter)
    apis['TEE_AllocateTransientObject']['Prototype'] = apis['TEE_AllocateTransientObject']['Prototype'].replace('TEE_ObjectType', 'uint32_t')
    apis['TEE_GetInstanceData']['Prototype'] = 'void *TEE_GetInstanceData(void);'
    apis['TEE_CheckMemoryAccessRights']['Prototype'] = 'TEE_Result TEE_CheckMemoryAccessRights(uint32_t accessFlags, const void *buffer, size_t size);'
    apis['TEE_MemFill']['Prototype'] = 'void TEE_MemFill(void *buffer, uint8_t x, size_t size);'
    apis['TEE_SetTAPersistentTime']['Prototype'] = 'TEE_Result TEE_SetTAPersistentTime(TEE_Time *time);'
    catalog['Apis'] = sorted(apis.values(), key=lambda a: a['Name'])
    if not any(c['Id'] == 'TEE_ECC_CURVE_SEC_P256k1' for c in catalog['Curves']):
        catalog['Curves'].append(dict(Id='TEE_ECC_CURVE_SEC_P256k1', Value='0x00000504', Bits=256, Generic=False))
    catalog['Sources']['Core'] = 'https://globalplatform.org/specs-library/tee-internal-core-api-specification/'
    catalog['Sources']['CoreEdition'] = 'TEE Internal Core API v1.4, Public Release, November 2025'
    catalog['Sources']['LmsParameters'] = 'https://www.iana.org/assignments/leighton-micali-signatures/'
    return catalog


if __name__ == '__main__':
    path = Path(__file__).resolve().parents[1] / 'assets/catalog.json'
    path.write_bytes((json.dumps(upgrade(json.loads(path.read_text())), indent=2)+'\n').encode())
