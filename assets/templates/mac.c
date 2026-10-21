/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_OperationHandle operation = TEE_HANDLE_NULL;
    size_t size = sizeof(response->data);
    size_t split = GP_STREAM ? request->data_size / 2 : 0;
    TEE_Result result;

    if (GP_NO_PADDING && request->data_size % GP_BLOCK_SIZE)
        return TEE_ERROR_BAD_PARAMETERS;
    /* Reject unavailable operations before allocating or generating keys. */
    result = TEE_AllocateOperation(&operation, GP_ALGORITHM, TEE_MODE_MAC, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 0, GP_KEY_TYPE, GP_KEY_BITS, 0, GP_GENERATE, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(operation, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_MACInit(operation, NULL, 0);
    TEE_MACUpdate(operation, request->data, split);
    result = TEE_MACComputeFinal(operation, request->data + split,
                                 request->data_size - split, response->data, &size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    response->data_size = (uint32_t)size;
    TEE_MACInit(operation, NULL, 0);
    result = TEE_MACCompareFinal(operation, request->data, request->data_size, response->data, size);
    if (result != TEE_SUCCESS)
        goto cleanup;
cleanup:
    if (operation != TEE_HANDLE_NULL)
        TEE_FreeOperation(operation);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    return result;
}
