/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    TEE_Time system_time;
    TEE_Time ree_time;
    TEE_Result result;

    (void)request;
    TEE_GetSystemTime(&system_time);
    TEE_GetREETime(&ree_time);
    result = TEE_Wait(GP_WAIT_MS);
    if (result != TEE_SUCCESS)
        return result;
    response->value_a = system_time.seconds;
    response->value_b = system_time.millis;
    TEE_MemMove(response->data, &ree_time, sizeof(ree_time));
    response->data_size = sizeof(ree_time);
    return TEE_SUCCESS;
}
