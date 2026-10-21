/* SPDX-License-Identifier: MIT */
#include "gp_support.h"

const struct gp_attribute *gp_attribute(const struct gp_request *request,
                                       uint32_t group, uint32_t id)
{
    for (uint32_t i = 0; i < request->attribute_count; ++i) {
        if (request->attributes[i].group == group &&
            request->attributes[i].id == id)
            return &request->attributes[i];
    }
    return NULL;
}

static bool allowed_attribute(uint32_t type, uint32_t id, bool generate)
{
    switch (type) {
    case TEE_TYPE_RSA_KEYPAIR:
        if (generate)
            return id == TEE_ATTR_RSA_PUBLIC_EXPONENT;
        return id == TEE_ATTR_RSA_MODULUS ||
               id == TEE_ATTR_RSA_PUBLIC_EXPONENT ||
               id == TEE_ATTR_RSA_PRIVATE_EXPONENT ||
               id == TEE_ATTR_RSA_PRIME1 || id == TEE_ATTR_RSA_PRIME2 ||
               id == TEE_ATTR_RSA_EXPONENT1 || id == TEE_ATTR_RSA_EXPONENT2 ||
               id == TEE_ATTR_RSA_COEFFICIENT;
    case TEE_TYPE_DSA_KEYPAIR:
        return id == TEE_ATTR_DSA_PRIME || id == TEE_ATTR_DSA_SUBPRIME ||
               id == TEE_ATTR_DSA_BASE || (!generate &&
               (id == TEE_ATTR_DSA_PRIVATE_VALUE || id == TEE_ATTR_DSA_PUBLIC_VALUE));
    case TEE_TYPE_DH_KEYPAIR:
        return id == TEE_ATTR_DH_PRIME || id == TEE_ATTR_DH_SUBPRIME ||
               id == TEE_ATTR_DH_BASE || (!generate &&
               (id == TEE_ATTR_DH_PRIVATE_VALUE || id == TEE_ATTR_DH_PUBLIC_VALUE));
    case TEE_TYPE_ECDSA_KEYPAIR:
    case TEE_TYPE_ECDH_KEYPAIR:
    case TEE_TYPE_SM2_DSA_KEYPAIR:
    case TEE_TYPE_SM2_PKE_KEYPAIR:
    case TEE_TYPE_SM2_KEP_KEYPAIR:
        return !generate && (id == TEE_ATTR_ECC_PRIVATE_VALUE ||
               id == TEE_ATTR_ECC_PUBLIC_VALUE_X || id == TEE_ATTR_ECC_PUBLIC_VALUE_Y);
    case TEE_TYPE_ED25519_KEYPAIR:
        return !generate && (id == TEE_ATTR_ED25519_PRIVATE_VALUE ||
                              id == TEE_ATTR_ED25519_PUBLIC_VALUE);
    case TEE_TYPE_X25519_KEYPAIR:
        return !generate && (id == TEE_ATTR_X25519_PRIVATE_VALUE ||
                              id == TEE_ATTR_X25519_PUBLIC_VALUE);
    case TEE_TYPE_X448_KEYPAIR:
        return !generate && (id == TEE_ATTR_X448_PRIVATE_VALUE ||
                              id == TEE_ATTR_X448_PUBLIC_VALUE);
#if defined(TEE_ATTR_ED448_PRIVATE_VALUE) && defined(TEE_ATTR_ED448_PUBLIC_VALUE)
    case TEE_TYPE_ED448_KEYPAIR:
        return !generate && (id == TEE_ATTR_ED448_PRIVATE_VALUE ||
                              id == TEE_ATTR_ED448_PUBLIC_VALUE);
#endif
    default:
        return !generate && id == TEE_ATTR_SECRET_VALUE;
    }
}

TEE_Result gp_make_key(const struct gp_request *request, uint32_t group,
                       uint32_t type, uint32_t bits, uint32_t curve,
                       bool generate, TEE_ObjectHandle *key)
{
    TEE_Attribute attributes[GP_MAX_ATTRIBUTES + 1];
    uint32_t count = 0;
    TEE_Result result;

    *key = TEE_HANDLE_NULL;
    for (uint32_t i = 0; i < request->attribute_count; ++i) {
        const struct gp_attribute *input = &request->attributes[i];
        if (input->group != group)
            continue;
        if (!allowed_attribute(type, input->id, generate))
            return TEE_ERROR_BAD_PARAMETERS;
        for (uint32_t j = 0; j < count; ++j)
            if (attributes[j].attributeID == input->id)
                return TEE_ERROR_BAD_PARAMETERS;
        if (input->id & TEE_ATTR_FLAG_VALUE)
            TEE_InitValueAttribute(&attributes[count], input->id, input->a, input->b);
        else
            TEE_InitRefAttribute(&attributes[count], input->id,
                                 (void *)input->data, input->size);
        ++count;
    }
    if (curve) {
        TEE_InitValueAttribute(&attributes[count], TEE_ATTR_ECC_CURVE, curve, 0);
        ++count;
    }
    result = TEE_AllocateTransientObject(type, bits, key);
    if (result != TEE_SUCCESS)
        return result;
    if (generate)
        result = TEE_GenerateKey(*key, bits, attributes, count);
    else
        result = TEE_PopulateTransientObject(*key, attributes, count);
    if (result != TEE_SUCCESS) {
        TEE_FreeTransientObject(*key);
        *key = TEE_HANDLE_NULL;
    }
    TEE_MemFill(attributes, 0, sizeof(attributes));
    return result;
}

TEE_Result gp_read_attribute(TEE_ObjectHandle key, uint32_t id,
                            void *data, size_t *size)
{
    TEE_Result result = TEE_GetObjectBufferAttribute(key, id, data, size);
    if (result != TEE_SUCCESS)
        return result;
    return TEE_SUCCESS;
}
