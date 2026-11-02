/* SPDX-License-Identifier: MIT */
#include "gp_support.h"

/* Standard SM2 curve parameters a, b, Gx and Gy, in big-endian order. */
static const uint8_t curve_parameters[128] = {
    0xff,0xff,0xff,0xfe,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
    0xff,0xff,0xff,0xff,0x00,0x00,0x00,0x00,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xfc,
    0x28,0xe9,0xfa,0x9e,0x9d,0x9f,0x5e,0x34,0x4d,0x5a,0x9e,0x4b,0xcf,0x65,0x09,0xa7,
    0xf3,0x97,0x89,0xf5,0x15,0xab,0x8f,0x92,0xdd,0xbc,0xbd,0x41,0x4d,0x94,0x0e,0x93,
    0x32,0xc4,0xae,0x2c,0x1f,0x19,0x81,0x19,0x5f,0x99,0x04,0x46,0x6a,0x39,0xc9,0x94,
    0x8f,0xe3,0x0b,0xbf,0xf2,0x66,0x0b,0xe1,0x71,0x5a,0x45,0x89,0x33,0x4c,0x74,0xc7,
    0xbc,0x37,0x36,0xa2,0xf4,0xf6,0x77,0x9c,0x59,0xbd,0xce,0xe3,0x6b,0x69,0x21,0x53,
    0xd0,0xa9,0x87,0x7c,0xc6,0x2a,0x47,0x40,0x02,0xdf,0x32,0xe5,0x21,0x39,0xf0,0xa0
};

TEE_Result gp_sm2_za(TEE_ObjectHandle key, const uint8_t *identity,
                     size_t identity_size, uint8_t output[32])
{
    TEE_OperationHandle operation = TEE_HANDLE_NULL;
    uint8_t coordinates[64] = { 0 };
    uint8_t entl[2];
    size_t size = 32;
    size_t output_size = 32;
    TEE_Result result;

    if (identity_size > 8191 || (!identity && identity_size) || !output)
        return TEE_ERROR_BAD_PARAMETERS;
    result = TEE_GetObjectBufferAttribute(key, TEE_ATTR_ECC_PUBLIC_VALUE_X, coordinates, &size);
    if (result != TEE_SUCCESS)
        return result;
    if (size < 32) {
        TEE_MemMove(coordinates + 32 - size, coordinates, size);
        TEE_MemFill(coordinates, 0, 32 - size);
    }
    size = 32;
    result = TEE_GetObjectBufferAttribute(key, TEE_ATTR_ECC_PUBLIC_VALUE_Y, coordinates + 32, &size);
    if (result != TEE_SUCCESS)
        return result;
    if (size < 32) {
        TEE_MemMove(coordinates + 64 - size, coordinates + 32, size);
        TEE_MemFill(coordinates + 32, 0, 32 - size);
    }
    entl[0] = (uint8_t)((identity_size * 8) >> 8);
    entl[1] = (uint8_t)(identity_size * 8);
    result = TEE_AllocateOperation(&operation, TEE_ALG_SM3, TEE_MODE_DIGEST, 0);
    if (result != TEE_SUCCESS)
        return result;
    TEE_DigestUpdate(operation, entl, sizeof(entl));
    TEE_DigestUpdate(operation, identity, identity_size);
    TEE_DigestUpdate(operation, curve_parameters, sizeof(curve_parameters));
    result = TEE_DigestDoFinal(operation, coordinates, sizeof(coordinates), output, &output_size);
cleanup:
    TEE_FreeOperation(operation);
    TEE_MemFill(coordinates, 0, sizeof(coordinates));
    if (result != TEE_SUCCESS)
        return result;
    return output_size == 32 ? TEE_SUCCESS : TEE_ERROR_BAD_STATE;
}
