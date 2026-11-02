/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    TEE_ObjectHandle source = TEE_HANDLE_NULL;
    TEE_ObjectHandle copy = TEE_HANDLE_NULL;
    TEE_ObjectHandle ec = TEE_HANDLE_NULL;
    TEE_ObjectInfo info;
    TEE_Attribute attribute;
    uint8_t key[16];
    uint32_t curve = 0;
    uint32_t unused = 0;
    size_t size = sizeof(response->data);
    TEE_Result result;

    (void)request;
    TEE_GenerateRandom(key, sizeof(key));
    result = TEE_AllocateTransientObject(TEE_TYPE_AES, 128, &source);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_AllocateTransientObject(TEE_TYPE_AES, 128, &copy);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_InitRefAttribute(&attribute, TEE_ATTR_SECRET_VALUE, key, sizeof(key));
    result = TEE_PopulateTransientObject(source, &attribute, 1);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_CopyObjectAttributes1(copy, source);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_RestrictObjectUsage1(copy, TEE_USAGE_ENCRYPT | TEE_USAGE_DECRYPT | TEE_USAGE_EXTRACTABLE);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_GetObjectInfo1(copy, &info);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_GetObjectBufferAttribute(copy, TEE_ATTR_SECRET_VALUE, response->data, &size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (size != sizeof(key) || TEE_MemCompare(key, response->data, sizeof(key))) {
        result = TEE_ERROR_SECURITY;
        goto cleanup;
    }
    response->data_size = (uint32_t)size;
    TEE_ResetTransientObject(copy);
    result = TEE_AllocateTransientObject(TEE_TYPE_ECDSA_KEYPAIR, 256, &ec);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_InitValueAttribute(&attribute, TEE_ATTR_ECC_CURVE, TEE_ECC_CURVE_NIST_P256, 0);
    result = TEE_GenerateKey(ec, 256, &attribute, 1);
    if (result != TEE_SUCCESS)
        goto cleanup;
    result = TEE_GetObjectValueAttribute(ec, TEE_ATTR_ECC_CURVE, &curve, &unused);
    if (result != TEE_SUCCESS)
        goto cleanup;
    response->value_a = info.objectSize;
    response->value_b = curve;
cleanup:
    if (ec != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(ec);
    if (copy != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(copy);
    if (source != TEE_HANDLE_NULL)
        TEE_FreeTransientObject(source);
    TEE_MemFill(key, 0, sizeof(key));
    return result;
}
