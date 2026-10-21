/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_OperationHandle encrypt = TEE_HANDLE_NULL;
    TEE_OperationHandle decrypt = TEE_HANDLE_NULL;
    TEE_Attribute parameters[2];
    uint32_t mgf_hash = GP_MGF_HASH;
    uint32_t parameter_count = 0;
    const struct gp_attribute *label = gp_attribute(request, 10, TEE_ATTR_RSA_OAEP_LABEL);
    uint8_t *plain = NULL;
    const uint8_t *decoded = NULL;
    uint8_t padded[512] = { 0 };
    const uint8_t *input = request->data;
    size_t input_size = request->data_size;
    size_t output_size = sizeof(response->data);
    size_t plain_size = GP_DATA_BYTES;
    TEE_Result result;

    if (GP_RAW_RSA) {
        if (input_size >= GP_KEY_BITS / 8)
            return TEE_ERROR_BAD_PARAMETERS;
        /* Raw RSA treats input as an unsigned integer; left-pad its encoding. */
        TEE_MemMove(padded + GP_KEY_BITS / 8 - input_size, input, input_size);
        input = padded;
        input_size = GP_KEY_BITS / 8;
    }
    if (GP_OAEP) {
        TEE_InitRefAttribute(&parameters[parameter_count], TEE_ATTR_RSA_OAEP_MGF_HASH,
                             &mgf_hash, sizeof(mgf_hash));
        ++parameter_count;
        if (label) {
            TEE_InitRefAttribute(&parameters[parameter_count], TEE_ATTR_RSA_OAEP_LABEL,
                                 (void *)label->data, label->size);
            ++parameter_count;
        }
    }
    plain = TEE_Malloc(GP_DATA_BYTES, TEE_MALLOC_FILL_ZERO);
    if (!plain)
        return TEE_ERROR_OUT_OF_MEMORY;
    /* Reject unavailable operations before allocating or generating keys. */
    result = TEE_AllocateOperation(&encrypt, GP_ALGORITHM, TEE_MODE_ENCRYPT, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateOperation(&decrypt, GP_ALGORITHM, TEE_MODE_DECRYPT, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 0, GP_KEY_TYPE, GP_KEY_BITS, GP_CURVE, GP_GENERATE, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(encrypt, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(decrypt, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AsymmetricEncrypt(encrypt, parameters, parameter_count,
                                    input, input_size, response->data, &output_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AsymmetricDecrypt(decrypt, parameters, parameter_count,
                                    response->data, output_size, plain, &plain_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    decoded = plain;
    if (GP_RAW_RSA) {
        /* Raw RSA returns an integer; implementations may omit leading zeros. */
        while (input_size && *input == 0) {
            ++input;
            --input_size;
        }
        while (plain_size && *decoded == 0) {
            ++decoded;
            --plain_size;
        }
    }
    if (plain_size != input_size || TEE_MemCompare(decoded, input, plain_size)) {
        result = TEE_ERROR_SECURITY;
        goto cleanup;
    }
    response->data_size = (uint32_t)output_size;
cleanup:
    if (encrypt != TEE_HANDLE_NULL)
        TEE_FreeOperation(encrypt);
    if (decrypt != TEE_HANDLE_NULL)
        TEE_FreeOperation(decrypt);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    TEE_MemFill(plain, 0, GP_DATA_BYTES);
    TEE_Free(plain);
    TEE_MemFill(padded, 0, sizeof(padded));
    return result;
}
