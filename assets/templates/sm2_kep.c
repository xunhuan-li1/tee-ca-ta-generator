/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle local = TEE_HANDLE_NULL;
    TEE_ObjectHandle ephemeral = TEE_HANDLE_NULL;
    TEE_ObjectHandle peer = TEE_HANDLE_NULL;
    TEE_ObjectHandle peer_ephemeral = TEE_HANDLE_NULL;
    TEE_ObjectHandle secret = TEE_HANDLE_NULL;
    TEE_OperationHandle operation = TEE_HANDLE_NULL;
    TEE_Attribute parameters[8];
    uint8_t coordinates[4][32] = { { 0 } };
    uint32_t ids[4] = { TEE_ATTR_ECC_PUBLIC_VALUE_X, TEE_ATTR_ECC_PUBLIC_VALUE_Y,
                        TEE_ATTR_ECC_EPHEMERAL_PUBLIC_VALUE_X, TEE_ATTR_ECC_EPHEMERAL_PUBLIC_VALUE_Y };
    const struct gp_attribute *identity = gp_attribute(request, 10, TEE_ATTR_SM2_ID_INITIATOR);
    const struct gp_attribute *peer_identity = gp_attribute(request, 10, TEE_ATTR_SM2_ID_RESPONDER);
    size_t output_size = sizeof(response->data);
    TEE_Result result;
    uint32_t count = 0;

    if (!identity || !peer_identity)
        return TEE_ERROR_BAD_PARAMETERS;
    /* Reject unavailable operations before allocating or generating keys. */
    /* The operation holds two 256-bit keys: long-term and ephemeral. */
    result = TEE_AllocateOperation(&operation, TEE_ALG_SM2_KEP, TEE_MODE_DERIVE, 512);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 0, TEE_TYPE_SM2_KEP_KEYPAIR, 256, 0, GP_GENERATE, &local);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = gp_make_key(request, 1, TEE_TYPE_SM2_KEP_KEYPAIR, 256, 0, GP_GENERATE, &ephemeral);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (GP_GENERATE) {
        result = gp_make_key(request, 0, TEE_TYPE_SM2_KEP_KEYPAIR, 256, 0, true, &peer);
        if (result != TEE_SUCCESS)
            goto cleanup;
        result = gp_make_key(request, 1, TEE_TYPE_SM2_KEP_KEYPAIR, 256, 0, true, &peer_ephemeral);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
    for (uint32_t i = 0; i < 4; ++i) {
        size_t size = 32;
        if (GP_GENERATE) {
            result = TEE_GetObjectBufferAttribute(i < 2 ? peer : peer_ephemeral,
                       i % 2 ? TEE_ATTR_ECC_PUBLIC_VALUE_Y : TEE_ATTR_ECC_PUBLIC_VALUE_X,
                       coordinates[i], &size);
            if (result != TEE_SUCCESS)
                goto cleanup;
            if (size < 32) {
                TEE_MemMove(coordinates[i] + 32 - size, coordinates[i], size);
                TEE_MemFill(coordinates[i], 0, 32 - size);
            }
        } else {
            const struct gp_attribute *input = gp_attribute(request, 2, ids[i]);
            if (!input || input->size != 32) {
                result = TEE_ERROR_BAD_PARAMETERS;
                goto cleanup;
            }
            TEE_MemMove(coordinates[i], input->data, 32);
        }
        TEE_InitRefAttribute(&parameters[count++], ids[i], coordinates[i], 32);
    }
    TEE_InitValueAttribute(&parameters[count++], TEE_ATTR_SM2_KEP_USER, GP_ROLE, 0);
    const struct gp_attribute *initiator = GP_ROLE ? peer_identity : identity;
    const struct gp_attribute *responder = GP_ROLE ? identity : peer_identity;
    TEE_InitRefAttribute(&parameters[count++], TEE_ATTR_SM2_ID_INITIATOR,
                         (void *)initiator->data, initiator->size);
    TEE_InitRefAttribute(&parameters[count++], TEE_ATTR_SM2_ID_RESPONDER,
                         (void *)responder->data, responder->size);
    result = TEE_SetOperationKey2(operation, local, ephemeral);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateTransientObject(TEE_TYPE_GENERIC_SECRET, GP_OUTPUT_BYTES * 8, &secret);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_DeriveKey(operation, parameters, count, secret);
    result = TEE_GetObjectBufferAttribute(secret, TEE_ATTR_SECRET_VALUE, response->data, &output_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (output_size != GP_OUTPUT_BYTES) {
        TEE_MemFill(response->data, 0, sizeof(response->data));
        result = TEE_ERROR_BAD_STATE;
        goto cleanup;
    }
    response->data_size = (uint32_t)output_size;
cleanup:
    if (operation != TEE_HANDLE_NULL)
        TEE_FreeOperation(operation);
    if (secret != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(secret);
    if (peer_ephemeral != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(peer_ephemeral);
    if (peer != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(peer);
    if (ephemeral != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(ephemeral);
    if (local != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(local);
    TEE_MemFill(coordinates, 0, sizeof(coordinates));
    return result;
}
