"""Validate generated client interfaces and executable dispatch against a host TEEC stub."""
import argparse, io, json, subprocess, tarfile, uuid
from pathlib import Path

def main():
    p=argparse.ArgumentParser();p.add_argument('--exe',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--optee-root',required=True);args=p.parse_args()
    out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
    project={'FormatVersion':1,'Name':'contract','Uuid':str(uuid.uuid4()),'Features':[
        {'Kind':'digest','Algorithm':'TEE_ALG_SHA256'},
        {'Kind':'cipher','Algorithm':'TEE_ALG_AES_CBC_NOPAD','Options':{'keySource':'input','ivSource':'input'}},
        {'Kind':'random'}]}
    (out/'project.json').write_text(json.dumps(project),encoding='utf-8')
    subprocess.run([str(args.exe.resolve()),'--generate',str(out/'project.json'),str(out/'project'),str(out/'generate.json')],check=True,timeout=60)
    generated=out/'project'
    stub=r'''
#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include "client_api.h"
static unsigned int opened, closed;
TEEC_Result TEEC_InitializeContext(const char *n, TEEC_Context *c) { (void)n; (void)c; return TEEC_SUCCESS; }
void TEEC_FinalizeContext(TEEC_Context *c) { (void)c; }
TEEC_Result TEEC_OpenSession(TEEC_Context *c, TEEC_Session *s, const TEEC_UUID *u, uint32_t m, const void *d, TEEC_Operation *o, uint32_t *origin) {
    (void)c;(void)s;(void)u;(void)m;(void)d;(void)o; assert(opened==closed); *origin=TEEC_ORIGIN_TEE;
    if (getenv("OPEN_FAIL")) return TEEC_ERROR_TARGET_DEAD;
    ++opened; return TEEC_SUCCESS;
}
void TEEC_CloseSession(TEEC_Session *s) { (void)s; ++closed; }
TEEC_Result TEEC_InvokeCommand(TEEC_Session *s, uint32_t cmd, TEEC_Operation *o, uint32_t *origin) {
    (void)s; assert(opened==closed+1); *origin=TEEC_ORIGIN_TRUSTED_APP;
    assert(o->paramTypes==TEEC_PARAM_TYPES(TEEC_MEMREF_TEMP_INPUT,TEEC_MEMREF_TEMP_OUTPUT,TEEC_NONE,TEEC_NONE));
    const struct gp_request *q=o->params[0].tmpref.buffer;
    struct gp_response *r=o->params[1].tmpref.buffer;
    assert(q->version==GP_PROTOCOL_VERSION);
    if (cmd==3 && getenv("INVOKE_FAIL")) return TEEC_ERROR_TARGET_DEAD;
    if (cmd==1) { assert(q->data_size==3); assert(!memcmp(q->data,"abc",3)); }
    if (cmd==2) { assert(q->iv_size==16); assert(q->attribute_count==1); assert(q->attributes[0].size==16); }
    memset(r,0,sizeof(*r)); r->data_size=3; memcpy(r->data,"abc",3);
    if (getenv("BAD_REPLY")) r->data_size=GP_RESULT_BYTES+1;
    o->params[1].tmpref.size=sizeof(*r); return TEEC_SUCCESS;
}
'''
    consumer=r'''
#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include "client_api.h"
int main(void) {
    struct gp_client client={0}; struct gp_response response; uint32_t origin=0;
    const uint8_t bytes[]={0x61,0x62,0x63};
    struct gp_digest_request request={.input=bytes,.input_size=sizeof(bytes)};
    assert(gp_client_digest(&client,&request,&response,&origin)==TEEC_ERROR_BAD_PARAMETERS);
    assert(gp_client_open(&client,&origin)==TEEC_SUCCESS);
    assert(gp_client_digest(&client,&request,&response,&origin)==TEEC_SUCCESS);
    assert(response.data_size==3 && !memcmp(response.data,"abc",3));
    request.input_size=GP_DATA_BYTES+1;
    assert(gp_client_digest(&client,&request,&response,&origin)==TEEC_ERROR_BAD_PARAMETERS && response.data_size==0);
    request.input=NULL; request.input_size=3;
    assert(gp_client_digest(&client,&request,&response,&origin)==TEEC_ERROR_BAD_PARAMETERS);
    request.input=bytes;
    assert(!setenv("BAD_REPLY","1",1));
    assert(gp_client_digest(&client,&request,&response,&origin)==TEEC_ERROR_COMMUNICATION);
    assert(response.data_size==0 && origin==TEEC_ORIGIN_COMMS);
    gp_client_close(&client); gp_client_close(&client); return 0;
}
'''
    (generated/'stub.c').write_text(stub);(generated/'consumer.c').write_text(consumer)
    def run(*cmd,**kw):return subprocess.run(['wsl.exe','-d','Ubuntu','--',*cmd],capture_output=True,timeout=90,**kw)
    r=run('mktemp','-d','-t','gp-contract-XXXXXXXX');r.check_returncode();remote=r.stdout.decode().strip()
    archive=io.BytesIO()
    with tarfile.open(fileobj=archive,mode='w') as t:
        for f in generated.rglob('*'):
            if f.is_file():t.add(f,arcname=f.relative_to(generated).as_posix())
    run('tar','-xf','-','-C',remote,input=archive.getvalue()).check_returncode()
    import shlex
    include=args.optee_root.rstrip('/')+'/out-br/staging/usr/include'
    flags=['cc','-std=c11','-D_POSIX_C_SOURCE=200809L','-Wall','-Wextra','-Werror','-I'+remote+'/ca','-I'+remote+'/common','-I'+include]
    r=run(*flags,remote+'/ca/client.c',remote+'/ca/client_api.c',remote+'/stub.c',remote+'/consumer.c','-o',remote+'/contract');(out/'build.log').write_bytes(r.stdout+r.stderr);r.check_returncode()
    run(remote+'/contract').check_returncode()
    sources=[remote+'/ca/client.c',remote+'/ca/client_api.c',remote+'/ca/main.c',remote+'/stub.c']+[remote+'/'+f.relative_to(generated).as_posix() for f in (generated/'ca/examples').glob('*.c')]
    r=run(*flags,*sources,'-o',remote+'/demo');(out/'executable-build.log').write_bytes(r.stdout+r.stderr);r.check_returncode()
    checks=[]
    for name,cmd,expected in [('all',[remote+'/demo'],0),('explicit-all',[remote+'/demo','--all'],0),('help',[remote+'/demo','--help'],0),('single',[remote+'/demo','digest'],0),('unknown',[remote+'/demo','missing'],2),('open-error',['env','OPEN_FAIL=1',remote+'/demo','random'],1),('invoke-error',['env','INVOKE_FAIL=1',remote+'/demo','random'],1),('all-with-failure',['env','INVOKE_FAIL=1',remote+'/demo'],1)]:
        r=run(*cmd);(out/(name+'.log')).write_bytes(r.stdout+r.stderr);assert r.returncode==expected,(name,r.returncode);checks.append(name)
    text=(out/'all.log').read_text(encoding='utf-8',errors='replace');assert '3 commands, 0 failures' in text and text.index('cipher:')<text.index('random:')
    report={'Passed':True,'Checks':['typed-inputs','capacity-validation','null-inputs','malformed-reply','per-command-sessions',*checks],'LinuxDirectory':remote};(out/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':main()
