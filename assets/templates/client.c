/* SPDX-License-Identifier: MIT */
#include <string.h>
#include "client.h"

TEEC_Result gp_client_open_with_login(struct gp_client *client, uint32_t method,
                                      const void *connection_data, uint32_t *origin)
{
    const TEEC_UUID uuid = GP_DEMO_UUID;
    TEEC_Result result;

    if (origin)
        *origin = TEEC_ORIGIN_API;
    if (!client || client->state)
        return TEEC_ERROR_BAD_PARAMETERS;
    result = TEEC_InitializeContext(NULL, &client->context);
    if (result != TEEC_SUCCESS)
        return result;
    client->state = 1;
    result = TEEC_OpenSession(&client->context, &client->session, &uuid,
                              method, connection_data, NULL, origin);
    if (result != TEEC_SUCCESS) {
        TEEC_FinalizeContext(&client->context);
        memset(client, 0, sizeof(*client));
        return result;
    }
    client->state = 2;
    return TEEC_SUCCESS;
}

TEEC_Result gp_client_open(struct gp_client *client, uint32_t *origin)
{
    @DEFAULT_LOGIN@
}

void gp_client_close(struct gp_client *client)
{
    if (!client)
        return;
    if (client->state == 2)
        TEEC_CloseSession(&client->session);
    if (client->state)
        TEEC_FinalizeContext(&client->context);
    memset(client, 0, sizeof(*client));
}

TEEC_Result gp_client_invoke(struct gp_client *client, uint32_t command,
                            const struct gp_request *request,
                            struct gp_response *response, uint32_t *origin)
{
    TEEC_Operation operation = { 0 };
    TEEC_Result result;

    if (origin)
        *origin = TEEC_ORIGIN_API;
    if (!client || client->state != 2 || !request || !response)
        return TEEC_ERROR_BAD_PARAMETERS;
    memset(response, 0, sizeof(*response));
    operation.paramTypes = TEEC_PARAM_TYPES(TEEC_MEMREF_TEMP_INPUT, TEEC_MEMREF_TEMP_OUTPUT,
                                             TEEC_NONE, TEEC_NONE);
    operation.params[0].tmpref.buffer = (void *)request;
    operation.params[0].tmpref.size = sizeof(*request);
    operation.params[1].tmpref.buffer = response;
    operation.params[1].tmpref.size = sizeof(*response);
    result = TEEC_InvokeCommand(&client->session, command, &operation, origin);
    if (result != TEEC_SUCCESS) {
        memset(response, 0, sizeof(*response));
        return result;
    }
    if (operation.params[1].tmpref.size != sizeof(*response) ||
        response->data_size > sizeof(response->data) ||
        response->iv_size > sizeof(response->iv) || response->tag_size > sizeof(response->tag)) {
        memset(response, 0, sizeof(*response));
        if (origin)
            *origin = TEEC_ORIGIN_COMMS;
        return TEEC_ERROR_COMMUNICATION;
    }
    return TEEC_SUCCESS;
}
