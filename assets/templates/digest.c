/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_OperationHandle operation = TEE_HANDLE_NULL;
@COPY_BEGIN@
    TEE_OperationHandle copy = TEE_HANDLE_NULL;
@COPY_END@
    TEE_OperationInfo info;
    TEE_OperationInfoMultiple *multiple = NULL;
    size_t multiple_size = sizeof(*multiple) + sizeof(TEE_OperationInfoKey);
@COPY_BEGIN@
    uint8_t *comparison = NULL;
@COPY_END@
    size_t length = GP_OUTPUT_BYTES;
@COPY_BEGIN@
    size_t comparison_size = GP_OUTPUT_BYTES;
@COPY_END@
    size_t split = GP_STREAM ? request->data_size / 2 : 0;
    TEE_Result result;

    multiple = TEE_Malloc(multiple_size, TEE_MALLOC_FILL_ZERO);
    if (!multiple) {
        result = TEE_ERROR_OUT_OF_MEMORY;
        goto cleanup;
    }
@COPY_BEGIN@
    comparison = TEE_Malloc(GP_OUTPUT_BYTES, TEE_MALLOC_FILL_ZERO);
    if (!comparison) {
        result = TEE_ERROR_OUT_OF_MEMORY;
        goto cleanup;
    }
@COPY_END@
    result = TEE_AllocateOperation(&operation, GP_ALGORITHM, TEE_MODE_DIGEST, 0);
    if (result != TEE_SUCCESS)
        goto cleanup;
@COPY_BEGIN@
    result = TEE_AllocateOperation(&copy, GP_ALGORITHM, TEE_MODE_DIGEST, 0);
    if (result != TEE_SUCCESS)
        goto cleanup;
@COPY_END@
    TEE_GetOperationInfo(operation, &info);
    result = TEE_GetOperationInfoMultiple(operation, multiple, &multiple_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
@STREAM_BEGIN@
    TEE_DigestUpdate(operation, request->data, split);
@STREAM_END@
@COPY_BEGIN@
    TEE_CopyOperation(copy, operation);
@COPY_END@
#if GP_ALGORITHM == TEE_ALG_SHAKE128 || GP_ALGORITHM == TEE_ALG_SHAKE256
    TEE_DigestUpdate(operation, request->data + split, request->data_size - split);
    size_t extracted = length / 2;
    result = TEE_DigestExtract(operation, response->data, &extracted);
    if (result != TEE_SUCCESS)
        goto cleanup;
    length -= extracted;
    result = TEE_DigestDoFinal(operation, NULL, 0, response->data + extracted, &length);
    length += extracted;
#else
    result = TEE_DigestDoFinal(operation, request->data + split,
                               request->data_size - split, response->data, &length);
#endif
    if (result != TEE_SUCCESS)
        goto cleanup;
@COPY_BEGIN@
    result = TEE_DigestDoFinal(copy, request->data + split,
                               request->data_size - split, comparison, &comparison_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (length != comparison_size || TEE_MemCompare(response->data, comparison, length)) {
        result = TEE_ERROR_SECURITY;
        goto cleanup;
    }
    TEE_ResetOperation(copy);
    comparison_size = GP_OUTPUT_BYTES;
@STREAM_BEGIN@
    TEE_DigestUpdate(copy, request->data, split);
@STREAM_END@
    result = TEE_DigestDoFinal(copy, request->data + split,
                               request->data_size - split, comparison, &comparison_size);
    if (result != TEE_SUCCESS)
        goto cleanup;
    if (length != comparison_size || TEE_MemCompare(response->data, comparison, length)) {
        result = TEE_ERROR_SECURITY;
        goto cleanup;
    }
@COPY_END@
    response->data_size = (uint32_t)length;
cleanup:
@COPY_BEGIN@
    if (copy != TEE_HANDLE_NULL)
        TEE_FreeOperation(copy);
@COPY_END@
    if (operation != TEE_HANDLE_NULL)
        TEE_FreeOperation(operation);
@COPY_BEGIN@
    if (comparison) {
        TEE_MemFill(comparison, 0, GP_OUTPUT_BYTES);
        TEE_Free(comparison);
    }
@COPY_END@
    TEE_Free(multiple);
    return result;
}
