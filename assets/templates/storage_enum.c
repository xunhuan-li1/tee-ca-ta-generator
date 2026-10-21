/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectEnumHandle enumerator = TEE_HANDLE_NULL;
    TEE_ObjectInfo info;
    uint8_t id[TEE_OBJECT_ID_MAX_LEN];
    uint32_t count = 0;
    TEE_Result result;

    (void)request;
    result = TEE_AllocatePersistentObjectEnumerator(&enumerator);
    if (result != TEE_SUCCESS)
        return result;
    result = TEE_StartPersistentObjectEnumerator(enumerator, GP_STORAGE);
    if (result == TEE_ERROR_ITEM_NOT_FOUND)
        result = TEE_SUCCESS;
    else if (result == TEE_SUCCESS) {
        for (;;) {
            size_t size = sizeof(id);
            result = TEE_GetNextPersistentObject(enumerator, &info, id, &size);
            if (result == TEE_ERROR_ITEM_NOT_FOUND) {
                result = TEE_SUCCESS;
                break;
            }
            if (result != TEE_SUCCESS)
                break;
            ++count;
        }
    }
    TEE_ResetPersistentObjectEnumerator(enumerator);
    TEE_FreePersistentObjectEnumerator(enumerator);
    if (result != TEE_SUCCESS)
        return result;
    response->value_a = count;
    return TEE_SUCCESS;
}
