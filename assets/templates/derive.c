/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_ObjectHandle peer = TEE_HANDLE_NULL;
    TEE_ObjectHandle derived = TEE_HANDLE_NULL;
    TEE_OperationHandle operation = TEE_HANDLE_NULL;
    TEE_Attribute parameters[6];
    uint32_t count = 0;
    uint8_t peer_x[512] = { 0 };
    uint8_t peer_y[80] = { 0 };
    size_t x_size = sizeof(peer_x);
    size_t y_size = sizeof(peer_y);
    size_t output_size = sizeof(response->data);
    TEE_Result result;

    if (GP_CURVE) {
        result = TEE_IsAlgorithmSupported(GP_ALGORITHM, GP_CURVE);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
    /* Reject unavailable operations before allocating or generating keys. */
    result = TEE_AllocateOperation(&operation, GP_ALGORITHM, TEE_MODE_DERIVE, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 0, GP_KEY_TYPE, GP_KEY_BITS, GP_CURVE, GP_GENERATE, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(operation, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
#if GP_ALGORITHM == TEE_ALG_HKDF
    const struct gp_attribute *salt = gp_attribute(request, 10, TEE_ATTR_HKDF_SALT);
    const struct gp_attribute *info = gp_attribute(request, 10, TEE_ATTR_HKDF_INFO);
    TEE_InitValueAttribute(&parameters[count++], TEE_ATTR_HKDF_HASH_ALGORITHM, GP_HASH, 0);
    TEE_InitValueAttribute(&parameters[count++], TEE_ATTR_KDF_KEY_SIZE, GP_OUTPUT_BYTES, 0);
    if (salt)
        TEE_InitRefAttribute(&parameters[count++], TEE_ATTR_HKDF_SALT, (void *)salt->data, salt->size);
    if (info)
        TEE_InitRefAttribute(&parameters[count++], TEE_ATTR_HKDF_INFO, (void *)info->data, info->size);
#else
    if (GP_GENERATE) {
        result = gp_make_key(request, 0, GP_KEY_TYPE, GP_KEY_BITS, GP_CURVE, true, &peer);
        if (result != TEE_SUCCESS)
            goto cleanup;
        result = TEE_GetObjectBufferAttribute(peer, GP_PEER_X, peer_x, &x_size);
        if (result != TEE_SUCCESS)
            goto cleanup;
        if (GP_PEER_Y) {
            result = TEE_GetObjectBufferAttribute(peer, GP_PEER_Y, peer_y, &y_size);
            if (result != TEE_SUCCESS)
                goto cleanup;
        }
    } else {
        const struct gp_attribute *x = gp_attribute(request, 2, GP_PEER_X);
        const struct gp_attribute *y = gp_attribute(request, 2, GP_PEER_Y);
        if (!x || !x->size || (GP_PEER_Y && (!y || !y->size))) {
            result = TEE_ERROR_BAD_PARAMETERS;
            goto cleanup;
        }
        x_size = x->size;
        TEE_MemMove(peer_x, x->data, x_size);
        if (GP_PEER_Y) {
            if (y->size > sizeof(peer_y)) {
                result = TEE_ERROR_BAD_PARAMETERS;
                goto cleanup;
            }
            y_size = y->size;
            TEE_MemMove(peer_y, y->data, y_size);
        }
    }
    TEE_InitRefAttribute(&parameters[count++], GP_PEER_X, peer_x, x_size);
    if (GP_PEER_Y)
        TEE_InitRefAttribute(&parameters[count++], GP_PEER_Y, peer_y, y_size);
#endif
    result = TEE_AllocateTransientObject(TEE_TYPE_GENERIC_SECRET, GP_DERIVED_BITS, &derived);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_DeriveKey(operation, parameters, count, derived);
    result = TEE_GetObjectBufferAttribute(derived, TEE_ATTR_SECRET_VALUE, response->data, &output_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    response->data_size = (uint32_t)output_size;
    if (GP_GENERATE && peer != TEE_HANDLE_NULL) {
        uint8_t secret[GP_RESULT_BYTES];
        size_t secret_size = sizeof(secret);
        x_size = sizeof(peer_x);
        y_size = sizeof(peer_y);
        result = TEE_GetObjectBufferAttribute(key, GP_PEER_X, peer_x, &x_size);
        if (result != TEE_SUCCESS)
            goto cleanup;
        count = 0;
        TEE_InitRefAttribute(&parameters[count++], GP_PEER_X, peer_x, x_size);
        if (GP_PEER_Y) {
            result = TEE_GetObjectBufferAttribute(key, GP_PEER_Y, peer_y, &y_size);
            if (result != TEE_SUCCESS)
                goto cleanup;
            TEE_InitRefAttribute(&parameters[count++], GP_PEER_Y, peer_y, y_size);
        }
        TEE_ResetOperation(operation);
        result = TEE_SetOperationKey(operation, peer);
        if (result != TEE_SUCCESS)
            goto cleanup;
        TEE_ResetTransientObject(derived);
        TEE_DeriveKey(operation, parameters, count, derived);
        result = TEE_GetObjectBufferAttribute(derived, TEE_ATTR_SECRET_VALUE, secret, &secret_size);
        if (result == TEE_SUCCESS && (secret_size != output_size ||
            TEE_MemCompare(secret, response->data, secret_size)))
            result = TEE_ERROR_SECURITY;
        TEE_MemFill(secret, 0, sizeof(secret));
    }
cleanup:
    if (operation != TEE_HANDLE_NULL)
        TEE_FreeOperation(operation);
    if (derived != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(derived);
    if (peer != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(peer);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    TEE_MemFill(peer_x, 0, sizeof(peer_x));
    TEE_MemFill(peer_y, 0, sizeof(peer_y));
    return result;
}
