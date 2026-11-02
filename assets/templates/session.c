/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    const TEE_UUID target = GP_TARGET_UUID;
    TEE_TASessionHandle session = TEE_HANDLE_NULL;
    TEE_Param parameters[4] = { 0 };
    uint32_t types = TEE_PARAM_TYPES(TEE_PARAM_TYPE_NONE, TEE_PARAM_TYPE_NONE,
                                    TEE_PARAM_TYPE_NONE, TEE_PARAM_TYPE_NONE);
    uint32_t origin = 0;
    TEE_Result result;

    (void)request;
    result = TEE_OpenTASession(&target, 1000, types, parameters, &session, &origin);
    if (result != TEE_SUCCESS)
        return result;
    /* Adapt the parameter types and data to the target TA's documented protocol. */
    result = TEE_InvokeTACommand(session, 1000, GP_TARGET_COMMAND, types, parameters, &origin);
    TEE_CloseTASession(session);
    if (result != TEE_SUCCESS)
        return result;
    response->value_a = origin;
    return TEE_SUCCESS;
}
