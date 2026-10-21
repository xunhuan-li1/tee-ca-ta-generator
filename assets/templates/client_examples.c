/* SPDX-License-Identifier: MIT */
#define _POSIX_C_SOURCE 200809L
#include <pthread.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include "client.h"

TEEC_Result gp_client_shared_example(struct gp_client *client, uint32_t command,
                                    const struct gp_request *request,
                                    struct gp_response *response, uint32_t *origin)
{
    TEEC_Result result = TEEC_SUCCESS;
    const size_t input_offset = 16;
    const size_t output_offset = input_offset + sizeof(*request);
    const size_t capacity = output_offset + sizeof(*response);

    if (origin)
        *origin = TEEC_ORIGIN_API;
    if (!client || client->state != 2 || !request || !response)
        return TEEC_ERROR_BAD_PARAMETERS;
    memset(response, 0, sizeof(*response));
    for (unsigned int registered = 0; registered < 2; ++registered) {
        for (unsigned int partial = 0; partial < 2; ++partial) {
            TEEC_SharedMemory input = { 0 };
            TEEC_SharedMemory output = { 0 };
            TEEC_Operation operation = { 0 };
            bool input_active = false;
            bool output_active = false;
            void *input_buffer = NULL;
            void *output_buffer = NULL;
            input.size = partial ? capacity : sizeof(*request);
            input.flags = partial ? TEEC_MEM_INPUT | TEEC_MEM_OUTPUT : TEEC_MEM_INPUT;
            output.size = sizeof(*response);
            output.flags = TEEC_MEM_OUTPUT;
            if (registered) {
                input_buffer = calloc(1, input.size);
                output_buffer = partial ? NULL : calloc(1, output.size);
                if (!input_buffer || (!partial && !output_buffer)) {
                    result = TEEC_ERROR_OUT_OF_MEMORY;
                    goto cleanup;
                }
                input.buffer = input_buffer;
                result = TEEC_RegisterSharedMemory(&client->context, &input);
            } else {
                result = TEEC_AllocateSharedMemory(&client->context, &input);
            }
            if (result != TEEC_SUCCESS)
                goto cleanup;
            input_active = true;
            if (!partial) {
                if (registered) {
                    output.buffer = output_buffer;
                    result = TEEC_RegisterSharedMemory(&client->context, &output);
                } else {
                    result = TEEC_AllocateSharedMemory(&client->context, &output);
                }
                if (result != TEEC_SUCCESS)
                    goto cleanup;
                output_active = true;
            }
            memcpy((uint8_t *)input.buffer + (partial ? input_offset : 0), request, sizeof(*request));
            operation.paramTypes = partial ?
                TEEC_PARAM_TYPES(TEEC_MEMREF_PARTIAL_INPUT, TEEC_MEMREF_PARTIAL_OUTPUT, TEEC_NONE, TEEC_NONE) :
                TEEC_PARAM_TYPES(TEEC_MEMREF_WHOLE, TEEC_MEMREF_WHOLE, TEEC_NONE, TEEC_NONE);
            operation.params[0].memref.parent = &input;
            operation.params[0].memref.offset = partial ? input_offset : 0;
            operation.params[0].memref.size = sizeof(*request);
            operation.params[1].memref.parent = partial ? &input : &output;
            operation.params[1].memref.offset = partial ? output_offset : 0;
            operation.params[1].memref.size = sizeof(*response);
            result = TEEC_InvokeCommand(&client->session, command, &operation, origin);
            if (result != TEEC_SUCCESS)
                goto cleanup;
            if (operation.params[1].memref.size != sizeof(*response)) {
                result = TEEC_ERROR_COMMUNICATION;
                if (origin)
                    *origin = TEEC_ORIGIN_COMMS;
                goto cleanup;
            }
            memcpy(response, partial ? (uint8_t *)input.buffer + output_offset : output.buffer, sizeof(*response));
            if (response->data_size > sizeof(response->data) ||
                response->iv_size > sizeof(response->iv) ||
                response->tag_size > sizeof(response->tag)) {
                result = TEEC_ERROR_COMMUNICATION;
                if (origin)
                    *origin = TEEC_ORIGIN_COMMS;
            }
cleanup:
            if (output_active)
                memset(output.buffer, 0, output.size);
            if (input_active)
                memset(input.buffer, 0, input.size);
            if (output_active)
                TEEC_ReleaseSharedMemory(&output);
            if (input_active)
                TEEC_ReleaseSharedMemory(&input);
            free(output_buffer);
            if (input_buffer)
                memset(input_buffer, 0, partial ? capacity : sizeof(*request));
            free(input_buffer);
            if (result != TEEC_SUCCESS) {
                memset(response, 0, sizeof(*response));
                return result;
            }
        }
    }
    return result;
}

struct cancel_job {
    struct gp_client *client;
    uint32_t command;
    TEEC_Operation operation;
    TEEC_Result result;
    uint32_t origin;
};

static void *cancel_worker(void *argument)
{
    struct cancel_job *job = argument;
    job->result = TEEC_InvokeCommand(&job->client->session, job->command,
                                     &job->operation, &job->origin);
    return NULL;
}

TEEC_Result gp_client_cancel_example(struct gp_client *client, uint32_t command,
                                    const struct gp_request *request,
                                    struct gp_response *response, uint32_t *origin)
{
    struct cancel_job job = { 0 };
    struct timespec delay = { 0, 20000000 };
    pthread_t thread;

    if (origin)
        *origin = TEEC_ORIGIN_API;
    if (!client || client->state != 2 || !request || !response)
        return TEEC_ERROR_BAD_PARAMETERS;
    memset(response, 0, sizeof(*response));
    job.client = client;
    job.command = command;
    job.operation.paramTypes = TEEC_PARAM_TYPES(TEEC_MEMREF_TEMP_INPUT, TEEC_MEMREF_TEMP_OUTPUT,
                                                TEEC_NONE, TEEC_NONE);
    job.operation.params[0].tmpref.buffer = (void *)request;
    job.operation.params[0].tmpref.size = sizeof(*request);
    job.operation.params[1].tmpref.buffer = response;
    job.operation.params[1].tmpref.size = sizeof(*response);
    if (pthread_create(&thread, NULL, cancel_worker, &job) != 0)
        return TEEC_ERROR_GENERIC;
    (void)nanosleep(&delay, NULL);
    TEEC_RequestCancellation(&job.operation);
    /* A successfully created joinable worker is always joined before stack data expires. */
    if (pthread_join(thread, NULL) != 0)
        abort();
    if (origin)
        *origin = job.origin;
    if (job.result != TEEC_SUCCESS) {
        memset(response, 0, sizeof(*response));
        return job.result;
    }
    if (job.operation.params[1].tmpref.size != sizeof(*response) ||
        response->data_size > sizeof(response->data) || response->iv_size > sizeof(response->iv) || response->tag_size > sizeof(response->tag)) {
        memset(response, 0, sizeof(*response));
        if (origin)
            *origin = TEEC_ORIGIN_COMMS;
        return TEEC_ERROR_COMMUNICATION;
    }
    return TEEC_SUCCESS;
}
