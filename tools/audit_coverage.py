"""Compare the public GP specifications with the catalog and generated sources.

Inputs are developer reference texts extracted from the official PDFs. They are
not redistributed. This audits API/algorithm presence, not GP conformance.
"""
import argparse
import json
from pathlib import Path
import re

EXCLUDED_APIS = {
    'TEE_Panic', 'TEE_GetObjectInfo', 'TEE_RestrictObjectUsage',
    'TEE_CopyObjectAttributes', 'TEE_CloseAndDeletePersistentObject',
    'TEE_BigIntInitFMMContext', 'TEE_DeriveKey',
}
ENTRY_POINTS = {
    'TA_CreateEntryPoint', 'TA_DestroyEntryPoint', 'TA_OpenSessionEntryPoint',
    'TA_CloseSessionEntryPoint', 'TA_InvokeCommandEntryPoint',
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--core-text', type=Path, required=True)
    parser.add_argument('--client-text', type=Path, required=True)
    parser.add_argument('--generated', nargs='+', type=Path, required=True)
    parser.add_argument('--catalog', type=Path, default=Path('assets/catalog.json'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    core = args.core_text.read_text(encoding='utf-8-sig')
    client = args.client_text.read_text(encoding='utf-8-sig')
    catalog = json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    core_names = set(re.findall(r'\b(?:void|TEE_Result|bool|int32_t|uint32_t|size_t|TEE_\w+Handle)\s*\*?\s*((?:TEE|TA)_\w+)\s*\(', core))
    peripheral = {name for name in core_names if name.startswith(('TEE_Peripheral_', 'TEE_Event_'))}
    deprecated_arithmetic = {name for name in core_names if name.startswith('TEE_BigInt')}
    expected_core = (core_names - peripheral - EXCLUDED_APIS - deprecated_arithmetic) | ENTRY_POINTS
    expected_client = set(re.findall(r'\b(?:void|TEEC_Result)\s+(TEEC_\w+)\s*\(', client))
    assert len(expected_client) == 9, sorted(expected_client)
    actual_core = {a['Name'] for a in catalog['Apis'] if a['Group'] == 'Core'}
    actual_client = {a['Name'] for a in catalog['Apis'] if a['Group'] == 'Client'}
    assert actual_core == expected_core, (sorted(expected_core-actual_core), sorted(actual_core-expected_core))
    assert actual_client == expected_client, (expected_client, actual_client)
    # Check current-body prototypes before any backward-compatibility appendix.
    declarations = {}
    pattern = r'\b((?:const\s+)?(?:void|TEE_Result|bool|int32_t|uint32_t|size_t)\s*\*?\s*((?:TEE|TA)_\w+)\s*\([^;{}]+?\)\s*;)'
    for match in re.finditer(pattern, core.replace("TA_EXPORT", "")):
        declarations.setdefault(match[2], ' '.join(match[1].split()))
    # The printed prototype omits this comma; its two parameters are described below it.
    declarations['TEE_IsAlgorithmSupported'] = declarations['TEE_IsAlgorithmSupported'].replace('algId uint32_t', 'algId, uint32_t')
    def signature_types(prototype):
        prototype = re.sub(r'\[\w+\]', '*', prototype)
        result, arguments = prototype.split('(', 1)
        types = [re.sub(r'\b(?:TA|TEE)_\w+\s*$', '', result)]
        for argument in arguments.rstrip(';').rstrip(')').split(','):
            argument = argument.strip()
            if argument != 'void':
                argument = re.sub(r'\b\w+\s*(\*?)$', r'\1', argument)
            types.append(argument)
        return [re.sub(r'\s+', '', value) for value in types]
    for api in catalog['Apis']:
        if api['Group'] != 'Core':
            continue
        assert api['Name'] in declarations, api['Name']
        assert signature_types(api['Prototype']) == signature_types(declarations[api['Name']]), (api['Name'], api['Prototype'], declarations[api['Name']])

    expected_algorithms = dict(re.findall(r'\b(TEE_ALG_\w+)\s*(?:\*|\d+)?\s+(0x[0-9a-fA-F]+)', core))
    expected_algorithms.pop('TEE_ALG_ILLEGAL_VALUE', None)
    expected_algorithms.pop('TEE_ALG_NONE', None)
    actual_algorithms = {a['Id']:a['Value'] for a in catalog['Algorithms']}
    # Numeric identifiers are checked against the formal 1.4 table, not a draft or older SDK.
    assert set(expected_algorithms) == set(actual_algorithms), (sorted(set(expected_algorithms)-set(actual_algorithms)), sorted(set(actual_algorithms)-set(expected_algorithms)))
    assert {k:int(v,16) for k,v in expected_algorithms.items()} == {k:int(v,16) for k,v in actual_algorithms.items()}
    sources = {directory.name+'/'+f.relative_to(directory).as_posix():f.read_text(encoding='utf-8-sig') for directory in args.generated for f in directory.rglob('*') if f.suffix in ('.c','.h')}
    rows = []
    for api in catalog['Apis']:
        files = [name for name,text in sources.items() if re.search(r'\b'+api['Name']+r'\s*\(', text)]
        assert files, api['Name']
        rows.append({'Name':api['Name'], 'Group':api['Group'], 'Files':files})
    forbidden = EXCLUDED_APIS | peripheral | deprecated_arithmetic
    for file,text in sources.items():
        assert not any(re.search(r'\b'+name+r'\s*\(',text) for name in forbidden), file
    report = {'Passed':True, 'CoreVersion':'1.4', 'ClientVersion':'1.0 + Errata 2.0',
              'CurrentAlgorithms':len(actual_algorithms), 'CoreFunctions':len(expected_core)-5,
              'TaEntryPoints':5, 'CoreSizeMacros':0, 'ClientFunctions':len(expected_client),
              'ExcludedApis':sorted(forbidden), 'Apis':rows,
              'Limit':'Identifier and generated-call coverage; does not establish runtime support or conformance.'}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('Apis','ExcludedApis')},indent=2))

if __name__ == '__main__':
    main()
