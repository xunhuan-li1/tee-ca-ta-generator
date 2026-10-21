/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_BigInt a[TEE_BigIntSizeInU32(64)];
    TEE_BigInt b[TEE_BigIntSizeInU32(64)];
    TEE_BigInt n[TEE_BigIntSizeInU32(64)];
    TEE_BigInt out[TEE_BigIntSizeInU32(64)];
    size_t context_words = TEE_BigIntFMMContextSizeInU32(64);
    size_t fmm_words = TEE_BigIntFMMSizeInU32(64);
    TEE_BigIntFMMContext *context = TEE_Malloc(context_words * sizeof(uint32_t), TEE_MALLOC_FILL_ZERO);
    TEE_BigIntFMM *fa = TEE_Malloc(fmm_words * sizeof(uint32_t), TEE_MALLOC_FILL_ZERO);
    TEE_BigIntFMM *fb = TEE_Malloc(fmm_words * sizeof(uint32_t), TEE_MALLOC_FILL_ZERO);
    TEE_BigIntFMM *fc = TEE_Malloc(fmm_words * sizeof(uint32_t), TEE_MALLOC_FILL_ZERO);
    TEE_Result result = TEE_ERROR_OUT_OF_MEMORY;
    int32_t value = 0;

    (void)request;
    if (!context || !fa || !fb || !fc)
        goto cleanup;
    TEE_BigIntInit(a, sizeof(a) / sizeof(a[0]));
    TEE_BigIntInit(b, sizeof(b) / sizeof(b[0]));
    TEE_BigIntInit(n, sizeof(n) / sizeof(n[0]));
    TEE_BigIntInit(out, sizeof(out) / sizeof(out[0]));
    TEE_BigIntConvertFromS32(a, 5);
    TEE_BigIntConvertFromS32(b, 7);
    TEE_BigIntConvertFromS32(n, 23);
    result = TEE_BigIntInitFMMContext1(context, context_words, n);
    if (result != TEE_SUCCESS)
        goto cleanup;
    TEE_BigIntInitFMM(fa, fmm_words);
    TEE_BigIntInitFMM(fb, fmm_words);
    TEE_BigIntInitFMM(fc, fmm_words);
    TEE_BigIntConvertToFMM(fa, a, n, context);
    TEE_BigIntConvertToFMM(fb, b, n, context);
    TEE_BigIntComputeFMM(fc, fa, fb, n, context);
    TEE_BigIntConvertFromFMM(out, fc, n, context);
    result = TEE_BigIntConvertToS32(&value, out);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (value != 12) {
        result = TEE_ERROR_SECURITY;
        goto cleanup;
    }
    response->value_a = (uint32_t)value;
cleanup:
    TEE_Free(fc);
    TEE_Free(fb);
    TEE_Free(fa);
    TEE_Free(context);
    return result;
}
