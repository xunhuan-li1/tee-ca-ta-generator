"""Generate a reproducible option matrix for the published OP-TEE generator.
Covers every algorithm and UI choice family plus representative input boundaries.
Use qemu_runtime.py on generated executable projects and compile_wsl.py for libraries.
"""
import argparse,copy,json,subprocess,uuid
from pathlib import Path


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--exe',type=Path,required=True)
    ap.add_argument('--output',type=Path,required=True)
    args=ap.parse_args();out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
    exe=str(args.exe.resolve())
    def invoke(*values):
        return subprocess.run([exe,*map(str,values)],timeout=180).returncode
    assert invoke('--describe',out/'catalog.json')==0
    cat=json.loads((out/'catalog.json').read_text());cases=[];rejected=[];counter=0
    def add(label,kind,alg='',**options):
        cases.append({'Label':label,'Feature':{'Kind':kind,'Algorithm':alg,'Options':{k:str(v) for k,v in options.items()}}})
    for a in cat['Algorithms']:
        alg=a['Id'];fam=a['Family']
        if fam in ('cipher','mac','ae'):
            for key,stream in [('generate','true'),('input','false')]:
                add(alg+' '+key+' streaming='+stream,fam,alg,keySource=key,streaming=stream,ivSource='input')
        if a['KeyType'] in ('TEE_TYPE_AES','TEE_TYPE_SM4','TEE_TYPE_DES','TEE_TYPE_DES3'):
            sizes={'TEE_TYPE_AES':[128,192,256],'TEE_TYPE_SM4':[128],'TEE_TYPE_DES':[64],'TEE_TYPE_DES3':[128,192]}[a['KeyType']]
            if alg=='TEE_ALG_AES_XTS':sizes=[128,256]
            for bits in sizes:
                for key in ('generate','input'):
                    opts={'keySource':key,'keyBits':bits}
                    if key=='input':opts.update(key=bytes(range(bits//8)).hex(),key2=bytes(range(32,32+bits//8)).hex())
                    add(alg+' '+key+' bits='+str(bits),fam,alg,**opts)
        if a['KeyType'].startswith('TEE_TYPE_HMAC_'):
            typ=a['KeyType'];minimum=64 if typ.endswith('MD5') else 80 if typ in ('TEE_TYPE_HMAC_SHA1','TEE_TYPE_HMAC_SM3') else 112 if typ.endswith('SHA224') else 192 if typ in ('TEE_TYPE_HMAC_SHA256','TEE_TYPE_HMAC_SHA3_224') else 256
            maximum=512 if typ in ('TEE_TYPE_HMAC_MD5','TEE_TYPE_HMAC_SHA1','TEE_TYPE_HMAC_SHA224') else 1024
            for bits in (minimum,maximum):
                add(alg+' key boundary '+str(bits),fam,alg,keySource='input',key=bytes(range(bits//8)).hex(),message='')
    for alg in ('TEE_ALG_AES_GCM','TEE_ALG_AES_CCM'):
        tags=[32,48,64,80,96,112,128] if alg.endswith('CCM') else [32,64,96,104,112,120,128]
        for tag in tags:
            for stream in ('false','true'):
                add(alg+' tag='+str(tag)+' stream='+stream,'ae',alg,keySource='input',ivSource='input',tagBits=tag,streaming=stream,aad='00010203')
        for nonce in ([7,13] if alg.endswith('CCM') else [1,16,256]):
            add(alg+' nonce bytes='+str(nonce),'ae',alg,ivSource='input',iv=bytes(range(nonce)).hex(),message='',aad='')
    for bits in [256,512,768,1024,1536,2048,3072,4096]:
        add('RSA generated bits='+str(bits),'encrypt','TEE_ALG_RSA_NOPAD',keyBits=bits,message='01')
    for salt in [-1,0,1,32,222]:
        add('PSS salt='+str(salt),'sign','TEE_ALG_RSASSA_PKCS1_PSS_MGF1_SHA256',keySource='input',saltLength=salt)
    for alg,sizes in [('TEE_ALG_DSA_SHA1',[512,1024]),('TEE_ALG_DSA_SHA224',[2048]),('TEE_ALG_DSA_SHA256',[2048,3072])]:
        for bits in sizes:
            add(alg+' bits='+str(bits),'sign',alg,keyBits=bits)
    for c in cat['Curves']:
        if not c['Generic']:continue
        for kind,alg in [('sign',{'192':'TEE_ALG_ECDSA_SHA1','224':'TEE_ALG_ECDSA_SHA224','256':'TEE_ALG_ECDSA_SHA256','384':'TEE_ALG_ECDSA_SHA384','521':'TEE_ALG_ECDSA_SHA512'}.get(str(c['Bits']),'TEE_ALG_ECDSA_SHA256')),('derive','TEE_ALG_ECDH_DERIVE_SHARED_SECRET')]:
            add(alg+' matched curve '+c['Id'],kind,alg,curve=c['Id'])
            if 'NIST' in c['Id']:add(alg+' imported curve '+c['Id'],kind,alg,curve=c['Id'],keySource='input')
    for alg in ('TEE_ALG_ED25519','TEE_ALG_ED448'):
        for prehash in ('false','true'):
            for context in ('','616263'):
                for key in ('generate','input'):
                    add(alg+' prehash='+prehash+' context='+context+' '+key,'sign',alg,prehash=prehash,context=context,keySource=key)
    for role in ('initiator','responder'):
        for key in ('generate','input'):
            add('SM2 KEP '+role+' '+key,'derive','TEE_ALG_SM2_KEP',role=role,keySource=key)
    # Public example ZA is a 32-byte supplied input; signing and verification use the same value.
    add('SM2 externally supplied ZA','sign','TEE_ALG_SM2_DSA_SM3',keySource='input',zaSource='input',za='00'*32)
    for size in (1,512):
        add('HKDF output='+str(size),'derive','TEE_ALG_HKDF',keySource='input',outputBytes=size,salt='',info='')
        add('SM2 KEP output='+str(size),'derive','TEE_ALG_SM2_KEP',keySource='input',outputBytes=size)
    for alg in ('TEE_ALG_SHAKE128','TEE_ALG_SHAKE256'):
        for size in (1,4096):
            for copy_reuse in ('false','true'):
                add(alg+' output='+str(size)+' copy='+copy_reuse,'digest',alg,outputBytes=size,copyReuse=copy_reuse,message='')
    for size in [1,16,32,64,128,256,512,4096]:add('Random bytes='+str(size),'random',bytes=size)
    for wait in [0,100]:
        add('Time wait='+str(wait),'time',waitMs=wait)
        add('Cancellation wait='+str(wait),'cancellation',waitMs=wait)
    for seconds in [0,2147483647]:add('Persistent time seconds='+str(seconds),'persistent_time',seconds=seconds)
    for prop in cat['PropertyPresets']:add('Property '+prop['Name'],'property',set=prop['Set'],name=prop['Name'],type=prop['Type'])
    for space in ['TEE_STORAGE_PRIVATE','TEE_STORAGE_PERSO','TEE_STORAGE_PROTECTED']:
        add('Storage roundtrip '+space,'storage',storage=space,objectId='matrix.'+space.lower())
        add('Storage enumeration '+space,'storage_enum',storage=space)
    # Sequential commands share persistent objects but each has a new session.
    for action,opts in [('create',{}),('write',{'offset':'2','message':'646566'}),('read',{'offset':'1','size':'4'}),('truncate',{'size':'8'}),('read',{'size':'8'}),('rename',{}),('delete',{'objectId':'matrix.renamed'})]:
        add('Storage sequence '+action,'storage',action=action,objectId=opts.pop('objectId','matrix.sequence'),newObjectId='matrix.renamed',**opts)
    for alg in cat['Algorithms']:
        add('Capability '+alg['Id'],'algorithm_support',alg['Id'])
    for curve in cat['Curves']:
        if curve['Generic']:
            add('ECDSA capability '+curve['Id'],'algorithm_support','TEE_ALG_ECDSA_SHA256',element=int(curve['Value'],0))
    def project(features,name):return {'FormatVersion':1,'Name':name,'Uuid':str(uuid.uuid4()),'Features':features}
    def accepted(rows):
        nonlocal counter
        counter+=1;path=out/('validate-'+str(counter)+'.json');report=out/('validate-'+str(counter)+'-report.json')
        path.write_text(json.dumps(project([r['Feature'] for r in rows],'option_check')))
        if invoke('--validate',path,report)==0:return rows
        if len(rows)==1:
            rejected.append(dict(rows[0],Errors=json.loads(report.read_text()).get('Errors',[])));return []
        m=len(rows)//2;return accepted(rows[:m])+accepted(rows[m:])
    valid=[]
    for start in range(0,len(cases),128):valid+=accepted(cases[start:start+128])
    projects=[]
    for start in range(0,len(valid),100):
        name='options_'+str(start//100+1);rows=valid[start:start+100];p=project([r['Feature'] for r in rows],name)
        (out/(name+'.json')).write_text(json.dumps(p,indent=2));assert invoke('--generate',out/(name+'.json'),out/name,out/(name+'-report.json'))==0
        projects.append({'Name':name,'Cases':rows});print(name,len(rows),flush=True)
    # Exercise all project switches and all six login modes without expensive crypto.
    for index,login in enumerate(cat['LoginMethods']):
        name='login_'+str(index);p=project([{'Kind':'random'}],name);p.update(Login=login,GroupId=0)
        (out/(name+'.json')).write_text(json.dumps(p));assert invoke('--generate',out/(name+'.json'),out/name,out/(name+'-report.json'))==0
        projects.append({'Name':name,'Cases':[{'Label':login,'Feature':p['Features'][0]}]})
    for index,(single,multi,alive,ctx) in enumerate((s,m,a,c) for s in [False,True] for m in [False,True] for a in [False,True] for c in [False,True] if s or not a):
        name='flags_'+str(index);p=project([{'Kind':'random'},{'Kind':'shared_memory'},{'Kind':'client_cancel'}],name)
        p.update(SingleInstance=single,MultiSession=multi,KeepAlive=alive,SessionContext=ctx)
        (out/(name+'.json')).write_text(json.dumps(p));assert invoke('--generate',out/(name+'.json'),out/name,out/(name+'-report.json'))==0
        projects.append({'Name':name,'Cases':[{'Label':name+' '+f['Kind'],'Feature':f} for f in p['Features']]})
    for kind in ('shared','static','ta_only'):
        name='artifact_'+kind;p=project([{'Kind':'digest','Algorithm':'TEE_ALG_SHA256'}],name)
        if kind=='ta_only':p['GenerateCa']=False
        else:p['CaOutput']=kind
        (out/(name+'.json')).write_text(json.dumps(p));assert invoke('--generate',out/(name+'.json'),out/name,out/(name+'-report.json'))==0
    summary={'CandidateOptions':len(cases),'ValidOptions':len(valid),'RejectedConfigurations':rejected,'Projects':projects,
             'Scope':'All catalog algorithms and discrete choice families; boundary inputs; not every combination of arbitrary user data.'}
    (out/'matrix.json').write_text(json.dumps(summary,indent=2));print('valid',len(valid),'rejected',len(rejected),flush=True)

if __name__=='__main__':main()
