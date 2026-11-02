/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_OperationHandle operation = TEE_HANDLE_NULL;
    TEE_OperationHandle decrypt = TEE_HANDLE_NULL;
    uint8_t *plain = NULL;
    size_t prefix_size = sizeof(response->data);
    size_t last_size;
    size_t tag_size = GP_TAG_BITS / 8;
    size_t plain_size = GP_DATA_BYTES;
    size_t split = GP_STREAM ? request->data_size / 2 : 0;
    TEE_Result result;

    if (!GP_IV_GENERATE && request->iv_size != GP_IV_BYTES)
        return TEE_ERROR_BAD_PARAMETERS;
    response->iv_size = GP_IV_BYTES;
    if (GP_IV_GENERATE)
        TEE_GenerateRandom(response->iv, GP_IV_BYTES);
    else
        TEE_MemMove(response->iv, request->iv, GP_IV_BYTES);
    plain = TEE_Malloc(GP_DATA_BYTES, TEE_MALLOC_FILL_ZERO);
    if (!plain)
        return TEE_ERROR_OUT_OF_MEMORY;
    /* Reject unavailable operations before allocating or generating keys. */
    result = TEE_AllocateOperation(&operation, GP_ALGORITHM, TEE_MODE_ENCRYPT, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateOperation(&decrypt, GP_ALGORITHM, TEE_MODE_DECRYPT, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 0, TEE_TYPE_AES, GP_KEY_BITS, 0, GP_GENERATE, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(operation, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(decrypt, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AEInit(operation, response->iv, GP_IV_BYTES, GP_TAG_BITS,
                        request->aad_size, request->data_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_AEUpdateAAD(operation, request->aad, request->aad_size);
    if (split) {
        result = TEE_AEUpdate(operation, request->data, split, response->data, &prefix_size);
        if (result != TEE_SUCCESS)
            goto cleanup;
    } else {
        prefix_size = 0;
    }
    last_size = sizeof(response->data) - prefix_size;
    result = TEE_AEEncryptFinal(operation, request->data + split, request->data_size - split,
                                response->data + prefix_size, &last_size, response->tag, &tag_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    response->data_size = (uint32_t)(prefix_size + last_size);
    response->tag_size = (uint32_t)tag_size;
    result = TEE_AEInit(decrypt, response->iv, GP_IV_BYTES, GP_TAG_BITS,
                        request->aad_size, response->data_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_AEUpdateAAD(decrypt, request->aad, request->aad_size);
    result = TEE_AEDecryptFinal(decrypt, response->data, response->data_size,
                                plain, &plain_size, response->tag, tag_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (plain_size != request->data_size || TEE_MemCompare(plain, request->data, plain_size))
        result = TEE_ERROR_SECURITY;
cleanup:
    if (operation != TEE_HANDLE_NULL)
        TEE_FreeOperation(operation);
    if (decrypt != TEE_HANDLE_NULL)
        TEE_FreeOperation(decrypt);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    if (plain)
        TEE_MemFill(plain, 0, GP_DATA_BYTES);
    TEE_Free(plain);
    return result;
}
