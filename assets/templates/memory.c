/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    uint8_t *buffer = TEE_Malloc(32, TEE_MALLOC_FILL_ZERO);
    uint8_t *resized;
    TEE_Result result;

    (void)request;
    if (!buffer)
        return TEE_ERROR_OUT_OF_MEMORY;
    resized = TEE_Realloc(buffer, 64);
    if (!resized) {
        TEE_Free(buffer);
        return TEE_ERROR_OUT_OF_MEMORY;
    }
    buffer = resized;
    result = TEE_CheckMemoryAccessRights(TEE_MEMORY_ACCESS_READ | TEE_MEMORY_ACCESS_WRITE,
                                          buffer, 64);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_MemFill(buffer, 0x5a, 64);
    TEE_MemMove(response->data, buffer, 64);
    if (TEE_MemCompare(buffer, response->data, 64)) {
        result = TEE_ERROR_SECURITY;
        goto cleanup;
    }
    response->data_size = 64;
cleanup:
    TEE_MemFill(buffer, 0, 64);
    TEE_Free(buffer);
    return result;
}
