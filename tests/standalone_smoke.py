"""Check the single EXE without adjacent runtime, catalog or template files."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--exe',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    destination=args.output.resolve()
    destination.mkdir(parents=True,exist_ok=False)
    exe=destination/'GP-Project-Generator.exe'
    shutil.copyfile(args.exe,exe)
    env=os.environ.copy()
    env.update(DOTNET_ROOT=str(destination/'no-runtime'),DOTNET_ROOT_X64=str(destination/'no-runtime'),
               DOTNET_MULTILEVEL_LOOKUP='0',DOTNET_BUNDLE_EXTRACT_BASE_DIR=str(destination/'extracted'))
    def run(*arguments):
        subprocess.run([str(exe),*map(str,arguments)],env=env,cwd=destination,check=True,timeout=60)
    run('--describe','catalog.json')
    guide=(Path(__file__).resolve().parents[1]/'AI_GUIDE.md').read_text(encoding='utf-8')
    example=json.loads(re.search(r'```json\n(.*?)\n```',guide,re.S)[1])
    example['Uuid']=str(uuid.uuid4())
    (destination/'ai-example.json').write_text(json.dumps(example),encoding='utf-8')
    run('--validate','ai-example.json','ai-validate.json')
    run('--generate','ai-example.json','ai-example','ai-generate.json')
    project={'FormatVersion':1,'Name':'gp_smoke','Uuid':str(uuid.uuid4()),'Login':'TEEC_LOGIN_GROUP_APPLICATION','GroupId':65534,
             'Features':[{'Kind':'ae','Algorithm':'TEE_ALG_AES_GCM','Options':{'tagBits':'32','keySource':'input','ivSource':'input','streaming':'true'}},
                         {'Kind':'shared_memory'},{'Kind':'client_cancel'},
                         {'Kind':'property','Options':{'set':'current_client','name':'gpd.client.identity','type':'identity'}}]}
    (destination/'smoke.json').write_text(json.dumps(project),encoding='utf-8')
    run('--generate','smoke.json','project','generate.json')
    run('--self-test-ui','ui.json')
    summary={'Passed':True,'IsolatedExe':True,'GuideExampleValidated':True,'GroupLoginAndGcm32Generated':True,
             'Ui':json.loads((destination/'ui.json').read_text())}
    (destination/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(summary,indent=2))


if __name__=='__main__':
    main()
