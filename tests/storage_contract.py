"""Fault-inject the GP storage handle contract for both corruption policies."""
import argparse
import json
from pathlib import Path
import subprocess

HARNESS = r"""
#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
typedef uint32_t TEE_Result;
typedef uintptr_t TEE_ObjectHandle;
#define TEE_HANDLE_NULL 0
#define TEE_SUCCESS 0
#define TEE_ERROR_GENERIC 0xffff0000u
#define TEE_ERROR_CORRUPT_OBJECT 0xf0100001u
#define TEE_ERROR_STORAGE_NOT_AVAILABLE 0xf0100003u
#define TEE_ERROR_SECURITY 0xffff000fu
#define TEE_PROPSET_CURRENT_TA 1
#define TEE_DATA_FLAG_ACCESS_READ 1
#define TEE_DATA_FLAG_ACCESS_WRITE 2
#define TEE_DATA_FLAG_ACCESS_WRITE_META 4
#define TEE_DATA_SEEK_SET 0
#define GP_OBJECT_ID "contract"
#define GP_NEW_OBJECT_ID "renamed"
#define GP_STORAGE 1
#define GP_STORAGE_ACTION action
#define GP_OFFSET 0
#define GP_SIZE 3
struct gp_request { uint8_t data[8]; uint32_t data_size; };
struct gp_response { uint8_t data[8]; uint32_t data_size; };
static int action, policy, fault, hit, closed, live, queries;
static TEE_Result fault_result;
enum { QUERY=1, OPEN, SEEK, WRITE, READ, TRUNCATE, RENAME, DELETE_OBJECT };
static TEE_Result step(int stage) {
    if (fault != stage) return TEE_SUCCESS;
    ++hit;
    if (stage != QUERY && stage != OPEN && fault_result == TEE_ERROR_CORRUPT_OBJECT && !policy)
        live = 0; /* The implementation has already closed the object. */
    return fault_result;
}
static TEE_Result TEE_GetPropertyAsBool(uintptr_t set, const char *name, bool *value) {
    assert(set == TEE_PROPSET_CURRENT_TA);
    assert(!strcmp(name, "gpd.ta.doesNotCloseHandleOnCorruptObject"));
    ++queries; *value = policy; return step(QUERY);
}
static TEE_Result TEE_OpenPersistentObject(uint32_t space, const void *id, size_t len,
                                          uint32_t flags, TEE_ObjectHandle *object) {
    (void)space; (void)id; (void)len; (void)flags;
    assert(queries == 1 && !live);
    TEE_Result r = step(OPEN);
    if (!r) { live = 1; *object = 1; }
    else *object = TEE_HANDLE_NULL;
    return r;
}
static TEE_Result TEE_CreatePersistentObject(uint32_t space, const void *id, size_t len,
             uint32_t flags, TEE_ObjectHandle attrs, const void *data, size_t size,
             TEE_ObjectHandle *object) {
    (void)attrs; (void)data; (void)size;
    return TEE_OpenPersistentObject(space,id,len,flags,object);
}
static TEE_Result TEE_SeekObjectData(TEE_ObjectHandle h, int64_t off, uint32_t whence) {
    (void)off; (void)whence; assert(h && live); return step(SEEK);
}
static TEE_Result TEE_WriteObjectData(TEE_ObjectHandle h, const void *p, size_t n) {
    (void)p; (void)n; assert(h && live); return step(WRITE);
}
static TEE_Result TEE_ReadObjectData(TEE_ObjectHandle h, void *p, size_t n, size_t *count) {
    assert(h && live && n >= 3); TEE_Result r=step(READ);
    if (!r) { memcpy(p,"abc",3); *count=3; } return r;
}
static TEE_Result TEE_TruncateObjectData(TEE_ObjectHandle h, size_t n) {
    (void)n; assert(h && live); return step(TRUNCATE);
}
static TEE_Result TEE_RenamePersistentObject(TEE_ObjectHandle h, const void *p, size_t n) {
    (void)p; (void)n; assert(h && live); return step(RENAME);
}
static TEE_Result TEE_CloseAndDeletePersistentObject1(TEE_ObjectHandle h) {
    assert(h && live); TEE_Result r=step(DELETE_OBJECT); live=0; ++closed; return r;
}
static void TEE_CloseObject(TEE_ObjectHandle h) {
    assert(h && live); live=0; ++closed;
}
#define TEE_MemCompare memcmp
@FUNCTION_SOURCE@
int main(void) {
    unsigned cases=0;
    struct gp_request request = {{'a','b','c'},3};
    struct gp_response response = {0};
    const TEE_Result errors[] = {TEE_ERROR_CORRUPT_OBJECT, TEE_ERROR_STORAGE_NOT_AVAILABLE};
    for (policy=0; policy<=1; ++policy) {
        for (action=0; action<=6; ++action) {
            for (unsigned e=0; e<2; ++e) {
                fault_result=errors[e];
                for (fault=0; fault<=DELETE_OBJECT; ++fault) {
                    hit=closed=live=queries=0;
                    TEE_Result result=gp_storage_contract(&request,&response);
                    assert(result == (hit ? fault_result : TEE_SUCCESS));
                    assert(!live && closed <= 1 && queries == 1);
                    ++cases;
                }
            }
        }
    }
    printf("{\"Passed\":true,\"Cases\":%u}\n", cases);
    return 0;
}
"""

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--template',type=Path,default=Path('assets/templates/storage.c'))
    args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=False)
    source=args.template.read_text(encoding='utf-8-sig').replace('#include "gp_support.h"','').replace('@DEFINES@','').replace('@FUNCTION@','gp_storage_contract')
    c=HARNESS.replace('@FUNCTION_SOURCE@',source)
    (args.output/'contract.c').write_text(c,encoding='utf-8')
    prefix=['wsl.exe','-d','Ubuntu','--']
    remote=subprocess.run(prefix+['mktemp','-d','-t','gp-storage-contract-XXXXXXXX'],check=True,capture_output=True).stdout.decode().strip()
    subprocess.run(prefix+['tee',remote+'/contract.c'],input=c.encode(),check=True,capture_output=True)
    compile_result=subprocess.run(prefix+['gcc','-std=c11','-Wall','-Wextra','-Werror',remote+'/contract.c','-o',remote+'/contract'],capture_output=True)
    (args.output/'compile.log').write_bytes(compile_result.stdout+compile_result.stderr)
    compile_result.check_returncode()
    result=subprocess.run(prefix+[remote+'/contract'],capture_output=True)
    (args.output/'run.log').write_bytes(result.stdout+result.stderr)
    result.check_returncode()
    report=json.loads(result.stdout)
    (args.output/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report))

if __name__=='__main__':main()
