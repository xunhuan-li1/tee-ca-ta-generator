/* SPDX-License-Identifier: MIT */
#include "gp_support.h"

@SESSION_TYPE@

TEE_Result TA_CreateEntryPoint(void)
{
    @MASK_PANICS@
    uint32_t *instance = TEE_Malloc(sizeof(*instance), TEE_MALLOC_FILL_ZERO);
    if (!instance)
        return TEE_ERROR_OUT_OF_MEMORY;
    *instance = 0x47504750;
    TEE_SetInstanceData(instance);
    return TEE_SUCCESS;
}

void TA_DestroyEntryPoint(void)
{
    const void *data = TEE_GetInstanceData();
    if (data)
        TEE_Free((void *)data);
    TEE_SetInstanceData(NULL);
}

TEE_Result TA_OpenSessionEntryPoint(uint32_t types, TEE_Param params[4], void **context)
{
    (void)params;
    if (types != TEE_PARAM_TYPES(TEE_PARAM_TYPE_NONE, TEE_PARAM_TYPE_NONE,
                                 TEE_PARAM_TYPE_NONE, TEE_PARAM_TYPE_NONE))
        return TEE_ERROR_BAD_PARAMETERS;
    @SESSION_OPEN@
    return TEE_SUCCESS;
}

void TA_CloseSessionEntryPoint(void *context)
{
    @SESSION_CLOSE@
}

#if TEE_CORE_API_MAJOR_VERSION > 1 || TEE_CORE_API_MINOR_VERSION >= 4
TEE_Result TA_InvokeCommandEntryPoint(const void *context, uint32_t command,
#else
TEE_Result TA_InvokeCommandEntryPoint(void *context, uint32_t command,
#endif
                                      uint32_t types, TEE_Param params[4])
{
    struct gp_request *request = NULL;
    struct gp_response *response = NULL;
    TEE_Result result = TEE_ERROR_BAD_PARAMETERS;
    size_t output_capacity;

    (void)context;
    if (types != TEE_PARAM_TYPES(TEE_PARAM_TYPE_MEMREF_INPUT, TEE_PARAM_TYPE_MEMREF_OUTPUT,
                                 TEE_PARAM_TYPE_NONE, TEE_PARAM_TYPE_NONE))
        return TEE_ERROR_BAD_PARAMETERS;
    output_capacity = params[1].memref.size;
    params[1].memref.size = 0;
    if (!params[0].memref.buffer || params[0].memref.size != sizeof(*request))
        return TEE_ERROR_BAD_PARAMETERS;
    if (!params[1].memref.buffer || output_capacity < sizeof(*response)) {
        params[1].memref.size = sizeof(*response);
        return TEE_ERROR_SHORT_BUFFER;
    }
    request = TEE_Malloc(sizeof(*request), TEE_MALLOC_FILL_ZERO);
    response = TEE_Malloc(sizeof(*response), TEE_MALLOC_FILL_ZERO);
    if (!request || !response) {
        result = TEE_ERROR_OUT_OF_MEMORY;
        goto cleanup;
    }
    /* Snapshot client-controlled data before validating or calling GP APIs. */
    TEE_MemMove(request, params[0].memref.buffer, sizeof(*request));
    if (request->version != GP_PROTOCOL_VERSION ||
        request->attribute_count > GP_MAX_ATTRIBUTES ||
        request->data_size > sizeof(request->data) ||
        request->aad_size > sizeof(request->aad) || request->iv_size > sizeof(request->iv))
        goto cleanup;
    for (uint32_t i = 0; i < request->attribute_count; ++i)
        if (request->attributes[i].size > GP_ATTRIBUTE_BYTES)
            goto cleanup;
    @SESSION_INVOKE@
    switch (command) {
@DISPATCH@
    default:
        result = TEE_ERROR_NOT_SUPPORTED;
        break;
    }
    if (result == TEE_SUCCESS) {
        if (response->data_size > sizeof(response->data) ||
            response->iv_size > sizeof(response->iv) || response->tag_size > sizeof(response->tag)) {
            result = TEE_ERROR_BAD_STATE;
            goto cleanup;
        }
        TEE_MemMove(params[1].memref.buffer, response, sizeof(*response));
        params[1].memref.size = sizeof(*response);
    }
cleanup:
    if (response) {
        TEE_MemFill(response, 0, sizeof(*response));
        TEE_Free(response);
    }
    if (request) {
        TEE_MemFill(request, 0, sizeof(*request));
        TEE_Free(request);
    }
    return result;
}
