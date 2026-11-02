/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    (void)request;
    TEE_GenerateRandom(response->data, GP_OUTPUT_BYTES);
    response->data_size = GP_OUTPUT_BYTES;
    return TEE_SUCCESS;
}
