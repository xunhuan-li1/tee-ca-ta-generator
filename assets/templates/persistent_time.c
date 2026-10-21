/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(const struct gp_request *request, struct gp_response *response)
{
    TEE_Time time = { GP_SECONDS, 0 };
    TEE_Result result;

    (void)request;
    result = TEE_SetTAPersistentTime(&time);
    if (result != TEE_SUCCESS)
        return result;
    result = TEE_GetTAPersistentTime(&time);
    if (result != TEE_SUCCESS)
        return result;
    response->value_a = time.seconds;
    response->value_b = time.millis;
    return TEE_SUCCESS;
}
