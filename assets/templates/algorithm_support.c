/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    TEE_Result result;

    (void)request;
    result = TEE_IsAlgorithmSupported(GP_ALGORITHM, GP_ELEMENT);
    response->value_a = result == TEE_SUCCESS ? 1 : 0;
    response->value_b = result;
    if (result != TEE_SUCCESS && result != TEE_ERROR_NOT_SUPPORTED)
        return result;
    return TEE_SUCCESS;
}
