/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    bool previous_mask = TEE_MaskCancellation();
    bool cancelled = TEE_GetCancellationFlag();
    TEE_Result result;

    (void)request;
    (void)TEE_UnmaskCancellation();
    result = TEE_Wait(GP_WAIT_MS);
    cancelled = cancelled || TEE_GetCancellationFlag();
    if (previous_mask)
        (void)TEE_MaskCancellation();
    response->value_a = cancelled ? 1 : 0;
    response->value_b = result;
    if (result != TEE_SUCCESS && result != TEE_ERROR_CANCEL)
        return result;
    return TEE_SUCCESS;
}
