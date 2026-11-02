/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_ObjectHandle key2 = TEE_HANDLE_NULL;
    TEE_OperationHandle encrypt = TEE_HANDLE_NULL;
    TEE_OperationHandle decrypt = TEE_HANDLE_NULL;
    uint8_t *plain = NULL;
    size_t first_size = sizeof(response->data);
    size_t final_size;
    size_t plain_size = GP_DATA_BYTES + 32;
    size_t split = GP_STREAM ? (request->data_size / GP_BLOCK_SIZE / 2) * GP_BLOCK_SIZE : 0;
    TEE_Result result;

    if (GP_NO_PADDING && request->data_size % GP_BLOCK_SIZE)
        return TEE_ERROR_BAD_PARAMETERS;
    if (GP_CTS && request->data_size < 17)
        return TEE_ERROR_BAD_PARAMETERS;
    if (GP_XTS && request->data_size < 16)
        return TEE_ERROR_BAD_PARAMETERS;
    if (!GP_IV_GENERATE && request->iv_size != GP_IV_BYTES)
        return TEE_ERROR_BAD_PARAMETERS;
    response->iv_size = GP_IV_BYTES;
    if (GP_IV_GENERATE && GP_IV_BYTES)
        TEE_GenerateRandom(response->iv, GP_IV_BYTES);
    else if (GP_IV_BYTES)
        TEE_MemMove(response->iv, request->iv, GP_IV_BYTES);
    plain = TEE_Malloc(plain_size, TEE_MALLOC_FILL_ZERO);
    if (!plain)
        return TEE_ERROR_OUT_OF_MEMORY;
    /* Reject unavailable operations before allocating or generating keys. */
    result = TEE_AllocateOperation(&encrypt, GP_ALGORITHM, TEE_MODE_ENCRYPT,
                                   GP_KEY_BITS * (GP_XTS ? 2 : 1));
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateOperation(&decrypt, GP_ALGORITHM, TEE_MODE_DECRYPT,
                                   GP_KEY_BITS * (GP_XTS ? 2 : 1));
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 0, GP_KEY_TYPE, GP_KEY_BITS, 0, GP_GENERATE, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (GP_XTS) {
        result = gp_make_key(request, 1, GP_KEY_TYPE, GP_KEY_BITS, 0, GP_GENERATE, &key2);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
    result = GP_XTS ? TEE_SetOperationKey2(encrypt, key, key2) : TEE_SetOperationKey(encrypt, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = GP_XTS ? TEE_SetOperationKey2(decrypt, key, key2) : TEE_SetOperationKey(decrypt, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_CipherInit(encrypt, GP_IV_BYTES ? response->iv : NULL, GP_IV_BYTES);
    if (split) {
        result = TEE_CipherUpdate(encrypt, request->data, split, response->data, &first_size);
        if (result != TEE_SUCCESS)
            goto cleanup;
    } else {
        first_size = 0;
    }
    final_size = sizeof(response->data) - first_size;
    result = TEE_CipherDoFinal(encrypt, request->data + split, request->data_size - split,
                               response->data + first_size, &final_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    response->data_size = (uint32_t)(first_size + final_size);
    TEE_CipherInit(decrypt, GP_IV_BYTES ? response->iv : NULL, GP_IV_BYTES);
    result = TEE_CipherDoFinal(decrypt, response->data, response->data_size, plain, &plain_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (plain_size != request->data_size || TEE_MemCompare(plain, request->data, plain_size))
        result = TEE_ERROR_SECURITY;
cleanup:
    if (encrypt != TEE_HANDLE_NULL)
        TEE_FreeOperation(encrypt);
    if (decrypt != TEE_HANDLE_NULL)
        TEE_FreeOperation(decrypt);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    if (key2 != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key2);
    if (plain)
        TEE_MemFill(plain, 0, GP_DATA_BYTES + 32);
    TEE_Free(plain);
    return result;
}
