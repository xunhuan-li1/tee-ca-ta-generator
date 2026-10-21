/* SPDX-License-Identifier: MIT */
#ifndef GP_DEMO_CLIENT_H
#define GP_DEMO_CLIENT_H

#include <tee_client_api.h>
#include "gp_protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

struct gp_client {
    TEEC_Context context;
    TEEC_Session session;
    unsigned int state;
};

/* Initialize gp_client to zero before the first open. */
TEEC_Result gp_client_open(struct gp_client *client, uint32_t *origin);
TEEC_Result gp_client_open_with_login(struct gp_client *client, uint32_t method,
                                      const void *connection_data, uint32_t *origin);
void gp_client_close(struct gp_client *client);
TEEC_Result gp_client_invoke(struct gp_client *client, uint32_t command,
                            const struct gp_request *request,
                            struct gp_response *response, uint32_t *origin);
@CLIENT_EXAMPLE_PROTOTYPES@

#ifdef __cplusplus
}
#endif
#endif
