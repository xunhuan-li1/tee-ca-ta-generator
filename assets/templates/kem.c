/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle key = TEE_HANDLE_NULL;
    TEE_ObjectHandle public_key = TEE_HANDLE_NULL;
    TEE_ObjectHandle sender_key = TEE_HANDLE_NULL;
    TEE_ObjectHandle receiver_key = TEE_HANDLE_NULL;
    TEE_OperationHandle encapsulate = TEE_HANDLE_NULL;
    TEE_OperationHandle decapsulate = TEE_HANDLE_NULL;
    TEE_Attribute parameters[2];
    uint32_t count = 0;
    uint8_t sender_secret[32] = { 0 };
    uint8_t receiver_secret[32] = { 0 };
    size_t sender_size = sizeof(sender_secret);
    size_t receiver_size = sizeof(receiver_secret);
    size_t ciphertext_size = sizeof(response->data);
    TEE_Result result;

    (void)request;
    result = TEE_AllocateOperation(&encapsulate, GP_ALGORITHM, TEE_MODE_ENCAPSULATE, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateOperation(&decapsulate, GP_ALGORITHM, TEE_MODE_DECAPSULATE, GP_KEY_BITS);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateTransientObject(GP_KEY_TYPE, GP_KEY_BITS, &key);
    if (result != TEE_SUCCESS)
        goto cleanup;
#if GP_ALGORITHM == TEE_ALG_FRODOKEM
    TEE_InitValueAttribute(&parameters[count++], TEE_ATTR_FRODOKEM_PRG, GP_FRODO_PRG, 0);
    TEE_InitValueAttribute(&parameters[count++], TEE_ATTR_FRODOKEM_EPHEMERAL, GP_FRODO_EPHEMERAL, 0);
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
    result = TEE_SetOperationKey(encapsulate, public_key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_SetOperationKey(decapsulate, key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    /* Core 1.4 requires a cipher, HMAC or AE key object, not GENERIC_SECRET. */
    result = TEE_AllocateTransientObject(TEE_TYPE_AES, GP_SESSION_BITS, &sender_key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateTransientObject(TEE_TYPE_AES, GP_SESSION_BITS, &receiver_key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_EncapsulateKey(encapsulate, GP_SESSION_BITS, NULL, 0,
                                response->data, &ciphertext_size, sender_key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_DecapsulateKey(decapsulate, GP_SESSION_BITS, NULL, 0,
                                response->data, ciphertext_size, receiver_key);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_GetObjectBufferAttribute(sender_key, TEE_ATTR_SECRET_VALUE, sender_secret, &sender_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_GetObjectBufferAttribute(receiver_key, TEE_ATTR_SECRET_VALUE, receiver_secret, &receiver_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (sender_size != GP_SESSION_BITS / 8 || receiver_size != sender_size ||
        TEE_MemCompare(sender_secret, receiver_secret, sender_size)) {
        result = TEE_ERROR_SECURITY;
        goto cleanup;
    }
    response->data_size = (uint32_t)ciphertext_size;
    response->value_a = GP_SESSION_BITS;
    response->value_b = 1;
cleanup:
    if (encapsulate != TEE_HANDLE_NULL)
        TEE_FreeOperation(encapsulate);
    if (decapsulate != TEE_HANDLE_NULL)
        TEE_FreeOperation(decapsulate);
    if (sender_key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(sender_key);
    if (receiver_key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(receiver_key);
    if (public_key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(public_key);
    if (key != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(key);
    TEE_MemFill(sender_secret, 0, sizeof(sender_secret));
    TEE_MemFill(receiver_secret, 0, sizeof(receiver_secret));
    return result;
}
