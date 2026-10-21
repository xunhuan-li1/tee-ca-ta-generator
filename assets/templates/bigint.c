/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_BigInt a[TEE_BigIntSizeInU32(128)];
    TEE_BigInt b[TEE_BigIntSizeInU32(128)];
    TEE_BigInt n[TEE_BigIntSizeInU32(128)];
    TEE_BigInt out[TEE_BigIntSizeInU32(256)];
    TEE_BigInt remainder[TEE_BigIntSizeInU32(128)];
    TEE_BigInt u[TEE_BigIntSizeInU32(128)];
    TEE_BigInt v[TEE_BigIntSizeInU32(128)];
    const uint8_t encoded[] = { 5 };
    int32_t value = 0;
    size_t size = sizeof(response->data);
    TEE_Result result;

    (void)request;
    TEE_BigIntInit(a, sizeof(a) / sizeof(a[0]));
    TEE_BigIntInit(b, sizeof(b) / sizeof(b[0]));
    TEE_BigIntInit(n, sizeof(n) / sizeof(n[0]));
    TEE_BigIntInit(out, sizeof(out) / sizeof(out[0]));
    TEE_BigIntInit(remainder, sizeof(remainder) / sizeof(remainder[0]));
    TEE_BigIntInit(u, sizeof(u) / sizeof(u[0]));
    TEE_BigIntInit(v, sizeof(v) / sizeof(v[0]));
    result = TEE_BigIntConvertFromOctetString(a, encoded, sizeof(encoded), 1);
    if (result != TEE_SUCCESS)
        return result;
    TEE_BigIntConvertFromS32(b, 7);
    TEE_BigIntConvertFromS32(n, 23);
    result = TEE_BigIntAssign(out, a);
    if (result != TEE_SUCCESS)
        return result;
    if (TEE_BigIntCmp(out, a) != 0 || TEE_BigIntCmpS32(a, 5) != 0)
        return TEE_ERROR_SECURITY;
    result = TEE_BigIntSetBit(out, 3, true);
    if (result != TEE_SUCCESS)
        return result;
    if (!TEE_BigIntGetBit(out, 3) || TEE_BigIntGetBitCount(out) != 4)
        return TEE_ERROR_SECURITY;
    TEE_BigIntShiftRight(out, out, 1);
    TEE_BigIntNeg(out, a);
    result = TEE_BigIntAbs(out, out);
    if (result != TEE_SUCCESS)
        return result;
    TEE_BigIntAdd(out, a, b);
    TEE_BigIntSub(out, out, a);
    TEE_BigIntMul(out, a, b);
    TEE_BigIntSquare(out, a);
    TEE_BigIntDiv(out, remainder, b, a);
    TEE_BigIntMod(out, b, a);
    TEE_BigIntAddMod(out, a, b, n);
    TEE_BigIntSubMod(out, a, b, n);
    TEE_BigIntMulMod(out, a, b, n);
    TEE_BigIntSquareMod(out, a, n);
    if (!TEE_BigIntRelativePrime(a, n))
        return TEE_ERROR_SECURITY;
    TEE_BigIntInvMod(out, a, n);
    TEE_BigIntComputeExtendedGcd(out, u, v, a, b);
    if (TEE_BigIntIsProbablePrime(n, 80) == 0)
        return TEE_ERROR_SECURITY;
    result = TEE_BigIntExpMod(out, a, b, n, NULL);
    if (result != TEE_SUCCESS)
        return result;
    result = TEE_BigIntConvertToS32(&value, out);
    if (result != TEE_SUCCESS)
        return result;
    if (value != 17)
        return TEE_ERROR_SECURITY;
    result = TEE_BigIntConvertToOctetString(response->data, &size, out);
    if (result != TEE_SUCCESS)
        return result;
    response->data_size = (uint32_t)size;
    response->value_a = (uint32_t)value;
    return TEE_SUCCESS;
}
