/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_ObjectHandle public_key = TEE_HANDLE_NULL;
    TEE_OperationHandle sign = TEE_HANDLE_NULL;
    TEE_OperationHandle verify = TEE_HANDLE_NULL;
    TEE_OperationHandle digest = TEE_HANDLE_NULL;
    TEE_Attribute parameters[2];
    uint32_t count = 0;
    uint8_t hash[32] = { 0 };
    size_t hash_size = sizeof(hash);
    size_t signature_size = sizeof(response->data);
    TEE_Result result;

    result = TEE_AllocateOperation(&sign, GP_ALGORITHM, TEE_MODE_SIGN, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateOperation(&verify, GP_ALGORITHM, TEE_MODE_VERIFY, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateTransientObject(GP_KEY_TYPE, GP_KEY_BITS, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
#if GP_ALGORITHM == TEE_ALG_LMS
    /* RFC 8554 / IANA: LMS_SHA256_M32_H5 and LMOTS_SHA256_N32_W8.
     * This example generates a fresh key for one signature, then destroys it.
     * Never clone, restore or reuse a previous stateful private-key state. */
    TEE_InitValueAttribute(&parameters[count++], TEE_ATTR_LMS_PARAMSET_ID, 5, 0);
    TEE_InitValueAttribute(&parameters[count++], TEE_ATTR_LMOTS_PARAMSET_ID, 4, 0);
#endif
    result = TEE_GenerateKey(key, GP_KEY_BITS, parameters, count);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateTransientObject(GP_PUBLIC_KEY_TYPE, GP_KEY_BITS, &public_key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_CopyObjectAttributes1(public_key, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(sign, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(verify, public_key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    /* Core 1.4 section 6.7.2 requires a digest for these GP operations.
     * This is a GP API example, not a standalone protocol encoding. */
    result = TEE_AllocateOperation(&digest, TEE_ALG_SHA256, TEE_MODE_DIGEST, 0);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_DigestDoFinal(digest, request->data, request->data_size, hash, &hash_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AsymmetricSignDigest(sign, NULL, 0, hash, hash_size,
                                      response->data, &signature_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AsymmetricVerifyDigest(verify, NULL, 0, hash, hash_size,
                                        response->data, signature_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    response->data_size = (uint32_t)signature_size;
cleanup:
    if (digest != TEE_HANDLE_NULL)
        TEE_FreeOperation(digest);
    if (verify != TEE_HANDLE_NULL)
        TEE_FreeOperation(verify);
    if (sign != TEE_HANDLE_NULL)
        TEE_FreeOperation(sign);
    if (public_key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(public_key);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    TEE_MemFill(hash, 0, sizeof(hash));
    return result;
}
