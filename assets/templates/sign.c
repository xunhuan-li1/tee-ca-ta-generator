/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_OperationHandle sign = TEE_HANDLE_NULL;
    TEE_OperationHandle verify = TEE_HANDLE_NULL;
    TEE_OperationHandle digest = TEE_HANDLE_NULL;
    TEE_Attribute parameters[3];
    uint32_t parameter_count = 0;
    uint8_t hash[64] = { 0 };
    uint8_t za[32] = { 0 };
    const uint8_t *input = request->data;
    size_t input_size = request->data_size;
    size_t hash_size = GP_DIGEST_BYTES;
    size_t signature_size = sizeof(response->data);
    TEE_Result result;

    if (GP_CURVE) {
        result = TEE_IsAlgorithmSupported(GP_ALGORITHM, GP_CURVE);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
    /* Reject unavailable operations before allocating or generating keys. */
    result = TEE_AllocateOperation(&sign, GP_ALGORITHM, TEE_MODE_SIGN, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateOperation(&verify, GP_ALGORITHM, TEE_MODE_VERIFY, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 0, GP_KEY_TYPE, GP_KEY_BITS, GP_CURVE, GP_GENERATE, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (GP_SM2) {
        const struct gp_attribute *identity = gp_attribute(request, 10, TEE_ATTR_SM2_ID_INITIATOR);
        if (GP_ZA_INPUT) {
            const struct gp_attribute *supplied = gp_attribute(request, 11, TEE_ATTR_SECRET_VALUE);
            if (!supplied || supplied->size != sizeof(za)) {
                result = TEE_ERROR_BAD_PARAMETERS;
                goto cleanup;
            }
            TEE_MemMove(za, supplied->data, sizeof(za));
        } else {
            result = gp_sm2_za(key, identity ? identity->data : NULL,
                               identity ? identity->size : 0, za);
            if (result != TEE_SUCCESS)
                goto cleanup;
        }
    }
    if (!GP_EDDSA || GP_PREHASH) {
        result = TEE_AllocateOperation(&digest, GP_DIGEST, TEE_MODE_DIGEST, 0);
        if (result != TEE_SUCCESS)
            goto cleanup;
        if (GP_SM2)
            TEE_DigestUpdate(digest, za, sizeof(za));
        result = TEE_DigestDoFinal(digest, request->data, request->data_size, hash, &hash_size);
        if (result != TEE_SUCCESS)
            goto cleanup;
        input = hash;
        input_size = hash_size;
    }
    if (GP_PSS) {
        TEE_InitValueAttribute(&parameters[parameter_count++], TEE_ATTR_RSA_PSS_SALT_LENGTH,
                               GP_SALT_LENGTH, 0);
    }
    if (GP_EDDSA) {
        const struct gp_attribute *context = gp_attribute(request, 10, TEE_ATTR_EDDSA_CTX);
        TEE_InitValueAttribute(&parameters[parameter_count++], TEE_ATTR_EDDSA_PREHASH, GP_PREHASH, 0);
        if (context) {
            TEE_InitRefAttribute(&parameters[parameter_count++], TEE_ATTR_EDDSA_CTX,
                                 (void *)context->data, context->size);
        }
    }
    result = TEE_SetOperationKey(sign, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(verify, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AsymmetricSignDigest(sign, parameters, parameter_count,
                                      input, input_size, response->data, &signature_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AsymmetricVerifyDigest(verify, parameters, parameter_count,
                                        input, input_size, response->data, signature_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    response->data_size = (uint32_t)signature_size;
cleanup:
    if (digest != TEE_HANDLE_NULL)
        TEE_FreeOperation(digest);
    if (sign != TEE_HANDLE_NULL)
        TEE_FreeOperation(sign);
    if (verify != TEE_HANDLE_NULL)
        TEE_FreeOperation(verify);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    TEE_MemFill(hash, 0, sizeof(hash));
    TEE_MemFill(za, 0, sizeof(za));
    return result;
}
