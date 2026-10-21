/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_PropSetHandle set = GP_PROPERTY_SET;
    const char *name = GP_PROPERTY_NAME;
    size_t size = sizeof(response->data);
    TEE_Result result;
    bool boolean = false;
    uint32_t number = 0;
    uint64_t wide = 0;
    TEE_UUID uuid;
    TEE_Identity identity;

    (void)request;
    switch (GP_PROPERTY_TYPE) {
    case 0:
        result = TEE_GetPropertyAsString(set, name, (char *)response->data, &size);
        break;
    case 1:
        result = TEE_GetPropertyAsBool(set, name, &boolean);
        response->value_a = boolean ? 1 : 0;
        size = 0;
        break;
    case 2:
        result = TEE_GetPropertyAsU32(set, name, &number);
        response->value_a = number;
        size = 0;
        break;
    case 3:
        result = TEE_GetPropertyAsU64(set, name, &wide);
        response->value_a = (uint32_t)wide;
        response->value_b = (uint32_t)(wide >> 32);
        size = 0;
        break;
    case 4:
        result = TEE_GetPropertyAsUUID(set, name, &uuid);
        if (result == TEE_SUCCESS)
            TEE_MemMove(response->data, &uuid, sizeof(uuid));
        size = sizeof(uuid);
        break;
    case 5:
        result = TEE_GetPropertyAsIdentity(set, name, &identity);
        if (result == TEE_SUCCESS)
            TEE_MemMove(response->data, &identity, sizeof(identity));
        size = sizeof(identity);
        break;
    case 6:
        result = TEE_GetPropertyAsBinaryBlock(set, name, response->data, &size);
        break;
    default:
        return TEE_ERROR_BAD_PARAMETERS;
    }
    if (result != TEE_SUCCESS)
        return result;
    response->data_size = (uint32_t)size;
    return TEE_SUCCESS;
}
