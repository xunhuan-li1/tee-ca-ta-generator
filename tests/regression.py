"""Exercise the actual published executable, its schema and generated C sources."""
import argparse
import copy
import json
from pathlib import Path
import re
import subprocess
import uuid


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--exe',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    output=args.output.resolve()
    output.mkdir(parents=True,exist_ok=False)
    exe=str(args.exe.resolve())
    def invoke(*values, success=True):
        result=subprocess.run([exe,*map(str,values)],timeout=60)
        assert (result.returncode==0)==success, values
    catalog_path=output/'catalog.json'
    invoke('--describe',catalog_path)
    catalog=json.loads(catalog_path.read_text())
    assert len([a for a in catalog['Algorithms'] if not a['Legacy']])==103
    assert not any(a['Legacy'] for a in catalog['Algorithms'])
    assert not any(a['Deprecated'] or a['Name']=='TEE_Panic' for a in catalog['Apis'])
    assert not {'panic','legacy'} & {s['Id'] for s in catalog['Scenarios']}
    assert 'Event APIs' in str(catalog['Excluded'])
    base={'FormatVersion':1,'Name':'gp_check','Uuid':str(uuid.uuid4()),'Features':[{'Kind':'digest','Algorithm':'TEE_ALG_SHA256','Options':{}}]}
    case=0
    def validate(project, success=True):
        nonlocal case
        case+=1
        path=output/f'case-{case}.json'
        path.write_text(json.dumps(project),encoding='utf-8')
        invoke('--validate',path,output/f'case-{case}-report.json',success=success)
        report=json.loads((output/f'case-{case}-report.json').read_text())
        assert report['Success']==success
    def invalid(change):
        project=copy.deepcopy(base); change(project); validate(project,False)
    assert catalog['ProjectDefaults']['Version'] == '1.0.0'
    for value in ('1.0.1', '1.0.1-rc.1', 'release "A"\\B', 0, 4294967295):
        versioned=copy.deepcopy(base); versioned['Version']=value
        validate(versioned)
    for value in ('', ' ', None, True, -1, 4294967296, 1.5, {}, [], '1\n2', '1\x002', '1\x7f2'):
        invalid(lambda x, v=value:x.update(Version=v))
    invalid(lambda x:x.update(FormatVersion=2))
    invalid(lambda x:x.update(Name='../escape'))
    invalid(lambda x:x.update(Uuid=str(uuid.UUID(int=0))))
    invalid(lambda x:x.update(Features=[]))
    invalid(lambda x:x.update(UnknownSetting=True))
    invalid(lambda x:x.update(KeepAlive=True))
    for control in ('\x00', '\x01', '\x7f'):
        invalid(lambda x, c=control:x.update(Description='before'+c+'after'))
    description=copy.deepcopy(base)
    description['Description']='Quotes: \"GP\"; 中文\r\nLine two\tend'
    validate(description)
    invalid(lambda x:x['Features'][0].update(Algorithm='TEE_ALG_AES_GCM'))
    invalid(lambda x:x['Features'][0].update(Kind='event'))
    invalid(lambda x:x['Features'][0]['Options'].update(message='abc'))
    invalid(lambda x:x['Features'][0]['Options'].update(message='gg'))
    invalid(lambda x:x['Features'][0]['Options'].update(missing='1'))
    invalid(lambda x:x.update(Features=[{'Kind':'derive','Algorithm':'TEE_ALG_HKDF','Options':{'hash':'TEE_ALG_SHA1'}}]))
    invalid(lambda x:x.update(Features=[{'Kind':'panic'}]))
    invalid(lambda x:x.update(Features=[{'Kind':'legacy'}]))
    invalid(lambda x:x['Features'][0].update(Algorithm='TEE_ALG_ECDSA_P256'))
    invalid(lambda x:x.update(Features=[{'Kind':'encrypt','Algorithm':'TEE_ALG_RSAES_PKCS1_OAEP_MGF1_SHA256','Options':{'mgfHash':'TEE_ALG_MD5'}}]))
    invalid(lambda x:x['Features'][0]['Options'].update(copyReuse='invalid'))
    digest_matrix=copy.deepcopy(base); digest_matrix['Features']=[]
    cases=[]
    for algorithm in (a for a in catalog['Algorithms'] if a['Family']=='digest'):
        for streaming in ('false','true'):
            for copy_reuse in ('false','true'):
                options={'streaming':streaming,'copyReuse':copy_reuse}
                digest_matrix['Features'].append({'Kind':'digest','Algorithm':algorithm['Id'],'Options':options})
                cases.append((streaming,copy_reuse))
    matrix_path=output/'digest-matrix.json'; matrix_path.write_text(json.dumps(digest_matrix))
    invoke('--generate',matrix_path,output/'digest-matrix',output/'digest-matrix-report.json')
    for index,(streaming,copy_reuse) in enumerate(cases,1):
        # Command names follow the feature order, with duplicate digest commands numbered.
        filename='gp_digest_'+str(index)+'.c'
        source=(output/'digest-matrix/ta/examples'/filename).read_text()
        assert ('TEE_CopyOperation(' in source)==(copy_reuse=='true')
        assert ('TEE_ResetOperation(' in source)==(copy_reuse=='true')
        assert ('TEE_DigestUpdate(operation, request->data, split)' in source)==(streaming=='true')
        assert not re.search(r'@[A-Z_]+@',source)
    automatic=copy.deepcopy(base); automatic['Features']=[]; automatic['SessionContext']=True; automatic['Version']='1.0.1'
    manual=copy.deepcopy(automatic)
    for algorithm in catalog['Algorithms']:
        feature={'Kind':algorithm['Family'],'Algorithm':algorithm['Id'],'Options':{}}
        if algorithm['Family']=='digest': feature['Options']['copyReuse']='true'
        automatic['Features'].append(feature)
        imported=copy.deepcopy(feature)
        if algorithm['Family']!='digest': imported['Options']['keySource']='input'
        if algorithm['Family'] in ('cipher','ae') and 'ECB' not in algorithm['Id']:
            imported['Options']['ivSource']='input'
        if algorithm['Family'] in ('digest','cipher','mac','ae'): imported['Options']['streaming']='true'
        manual['Features'].append(imported)
    for curve in catalog['Curves']:
        if not curve['Generic']: continue
        for family,algorithm in [('sign','TEE_ALG_ECDSA_SHA256'),('derive','TEE_ALG_ECDH_DERIVE_SHARED_SECRET')]:
            automatic['Features'].append({'Kind':family,'Algorithm':algorithm,'Options':{'curve':curve['Id']}})
    for scenario in catalog['Scenarios']:
        if scenario['Category']=='Crypto': continue
        feature={'Kind':scenario['Id'],'Options':{}}
        if scenario['Id']=='session': feature['Options']['targetUuid']=str(uuid.uuid4())
        if scenario['Id']=='algorithm_support': feature['Algorithm']='TEE_ALG_SHA256'
        automatic['Features'].append(feature)
    validate(automatic); validate(manual)
    for name,project in [('automatic',automatic),('manual',manual)]:
        path=output/(name+'.json'); path.write_text(json.dumps(project,indent=2)+'\n')
        invoke('--generate',path,output/name,output/(name+'-report.json'))
        assert not list((output/name).rglob('*.json'))
        assert '#define TA_VERSION \"1.0.1\"' in (output/name/'ta/user_ta_header_defines.h').read_text()
        ta='\n'.join(f.read_text() for f in (output/name/'ta').rglob('*.c'))
        assert 'TA_CHECK_RESULT' not in ta
        assert not re.search(r'TEE_GenerateRandom\s*\(\s*params',ta)
        assert not re.search(r'@[A-Z_]+@',ta)
    kep=''.join(p.read_text() for p in (output/'automatic/ta/examples').glob('*sm2_kep*.c'))
    assert re.search(r'TEE_AllocateOperation\(\s*&operation,\s*TEE_ALG_SM2_KEP,\s*TEE_MODE_DERIVE,\s*512\)',kep)
    assert 'output_size != GP_OUTPUT_BYTES' in kep
    # Every public interface in the inventory has an actual generated call/definition.
    all_c='\n'.join(f.read_text() for f in (output/'automatic').rglob('*') if f.suffix in ('.c','.h'))
    missing=[a['Name'] for a in catalog['Apis'] if not re.search(r'\b'+a['Name']+r'\s*\(',all_c)]
    assert not missing, missing
    path=output/'automatic.json'
    invoke('--generate',path,output/'automatic',output/'no-overwrite.json',success=False)
    invoke('--generate',path,output/'protected',path,success=False)
    for kind in ('shared','static'):
        library=copy.deepcopy(base); library['Name']='gp_'+kind; library['CaOutput']=kind
        path=output/(kind+'.json');path.write_text(json.dumps(library))
        invoke('--generate',path,output/kind,output/(kind+'-report.json'))
        assert not (output/kind/'ca/main.c').exists()
        assert not (output/kind/'ca/default_inputs.c').exists()
        assert not (output/kind/'ca/examples').exists()
        assert 'gp_client_digest' in (output/kind/'ca/client_api.h').read_text()
        assert 'gp_client_cancel_example' not in (output/kind/'ca/client.h').read_text()
    # Fixed protocol carries only CA inputs; private input values never enter TA sources.
    values=catalog['PublicExampleKeys']['rsa2048']
    assert values['rsaD'] not in (output/'manual/ta/ta_support.c').read_text()
    invoke('--self-test-ui',output/'ui.json')
    summary={'Passed':True,'ValidationCases':case,'CurrentAlgorithms':103,'Aliases':sum(a['Legacy'] for a in catalog['Algorithms']),'ApiCalls':len(catalog['Apis']),
             'AutomaticFeatures':len(automatic['Features']),'ManualFeatures':len(manual['Features']),'Ui':json.loads((output/'ui.json').read_text())}
    (output/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    print(json.dumps(summary,indent=2))


if __name__=='__main__':
    main()
