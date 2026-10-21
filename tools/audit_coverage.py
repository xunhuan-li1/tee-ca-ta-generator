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
    'TEE_BigIntInitFMMContext',
}
ENTRY_POINTS = {
    'TA_CreateEntryPoint', 'TA_DestroyEntryPoint', 'TA_OpenSessionEntryPoint',
    'TA_CloseSessionEntryPoint', 'TA_InvokeCommandEntryPoint',
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--core-text', type=Path, required=True)
    parser.add_argument('--client-text', type=Path, required=True)
    parser.add_argument('--generated', type=Path, required=True)
    parser.add_argument('--catalog', type=Path, default=Path('assets/catalog.json'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    core = args.core_text.read_text(encoding='utf-8-sig')
    client = args.client_text.read_text(encoding='utf-8-sig')
    catalog = json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    core_names = set(re.findall(r'\b(?:void|TEE_Result|bool|int32_t|uint32_t|size_t|TEE_\w+Handle)\s*\*?\s*((?:TEE|TA)_\w+)\s*\(', core))
    peripheral = {name for name in core_names if name.startswith(('TEE_Peripheral_', 'TEE_Event_'))}
    expected_core = (core_names - peripheral - EXCLUDED_APIS) | ENTRY_POINTS | {'TEE_BigIntSizeInU32'}
    expected_client = set(re.findall(r'\b(?:void|TEEC_Result)\s+(TEEC_\w+)\s*\(', client))
    assert len(expected_client) == 9, sorted(expected_client)
    actual_core = {a['Name'] for a in catalog['Apis'] if a['Group'] == 'Core'}
    actual_client = {a['Name'] for a in catalog['Apis'] if a['Group'] == 'Client'}
    assert actual_core == expected_core, (sorted(expected_core-actual_core), sorted(actual_core-expected_core))
    assert actual_client == expected_client, (expected_client, actual_client)
    expected_algorithms = dict(re.findall(r'\b(TEE_ALG_\w+)\s*(?:\*|\d+)?\s+(0x[0-9a-fA-F]+)', core))
    expected_algorithms.pop('TEE_ALG_ILLEGAL_VALUE', None)
    actual_algorithms = {a['Id']:a['Value'] for a in catalog['Algorithms']}
    # The change-marked PDF contains revised/struck text; numeric constants use the target public headers.
    assert set(expected_algorithms) == set(actual_algorithms), (sorted(set(expected_algorithms)-set(actual_algorithms)), sorted(set(actual_algorithms)-set(expected_algorithms)))
    sources = {f.relative_to(args.generated).as_posix():f.read_text(encoding='utf-8-sig') for f in args.generated.rglob('*') if f.suffix in ('.c','.h')}
    rows = []
    for api in catalog['Apis']:
        files = [name for name,text in sources.items() if re.search(r'\b'+api['Name']+r'\s*\(', text)]
        assert files, api['Name']
        rows.append({'Name':api['Name'], 'Group':api['Group'], 'Files':files})
    forbidden = EXCLUDED_APIS | peripheral
    for file,text in sources.items():
        assert not any(re.search(r'\b'+name+r'\s*\(',text) for name in forbidden), file
    report = {'Passed':True, 'CoreVersion':'1.3.1', 'ClientVersion':'1.0 + Errata 2.0',
              'CurrentAlgorithms':len(actual_algorithms), 'CoreFunctions':len(expected_core)-6,
              'TaEntryPoints':5, 'CoreSizeMacros':1, 'ClientFunctions':len(expected_client),
              'ExcludedApis':sorted(forbidden), 'Apis':rows,
              'Limit':'Identifier and generated-call coverage; does not establish runtime support or conformance.'}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('Apis','ExcludedApis')},indent=2))

if __name__ == '__main__':
    main()
