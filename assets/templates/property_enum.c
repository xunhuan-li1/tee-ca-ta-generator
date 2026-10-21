/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_PropSetHandle enumerator = TEE_HANDLE_NULL;
    char name[256];
    uint32_t count = 0;
    TEE_Result result;

    (void)request;
    result = TEE_AllocatePropertyEnumerator(&enumerator);
    if (result != TEE_SUCCESS)
        return result;
    TEE_StartPropertyEnumerator(enumerator, GP_PROPERTY_SET);
    for (;;) {
        size_t size = sizeof(name);
        result = TEE_GetPropertyName(enumerator, name, &size);
        if (result == TEE_ERROR_ITEM_NOT_FOUND) {
            result = TEE_SUCCESS;
            break;
        }
        if (result != TEE_SUCCESS)
            break;
        ++count;
        result = TEE_GetNextProperty(enumerator);
        if (result == TEE_ERROR_ITEM_NOT_FOUND) {
            result = TEE_SUCCESS;
            break;
        }
        if (result != TEE_SUCCESS)
            break;
    }
    TEE_ResetPropertyEnumerator(enumerator);
    TEE_FreePropertyEnumerator(enumerator);
    if (result != TEE_SUCCESS)
        return result;
    response->value_a = count;
    return TEE_SUCCESS;
}
