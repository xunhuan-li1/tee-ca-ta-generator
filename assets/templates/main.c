/* SPDX-License-Identifier: MIT */
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "examples/examples.h"

struct command_entry {
    const char *name;
    TEEC_Result (*run)(struct gp_client *, struct gp_response *, uint32_t *);
};
static const struct command_entry commands[] = {
@COMMAND_TABLE@
};

static void print_bytes(const char *label, const uint8_t *data, uint32_t length)
{
    if (!length)
        return;
    printf("%s (%" PRIu32 " bytes): ", label, length);
    for (uint32_t i = 0; i < length; ++i)
        printf("%02x", data[i]);
    putchar('\n');
}

static int run_command(const struct command_entry *command)
{
    struct gp_client client = { 0 };
    struct gp_response *response = calloc(1, sizeof(*response));
    uint32_t origin = TEEC_ORIGIN_API;
    TEEC_Result result;
    int failed = 1;

    if (!response)
        return 1;
    result = gp_client_open(&client, &origin);
    if (result != TEEC_SUCCESS) {
        fprintf(stderr, "%s: open failed: result=0x%08" PRIx32 " origin=%" PRIu32 "\n", command->name, result, origin);
        goto cleanup;
    }
    result = command->run(&client, response, &origin);
    printf("%s: result=0x%08" PRIx32 " origin=%" PRIu32 " value=(%" PRIu32 ",%" PRIu32 ") bytes=%" PRIu32 "\n",
           command->name, result, origin, response->value_a, response->value_b, response->data_size);
    if (result != TEEC_SUCCESS)
        goto cleanup;
    print_bytes("data", response->data, response->data_size);
    print_bytes("iv", response->iv, response->iv_size);
    print_bytes("tag", response->tag, response->tag_size);
    failed = 0;
cleanup:
    gp_client_close(&client);
    memset(response, 0, sizeof(*response));
    free(response);
    return failed;
}

int main(int argc, char **argv)
{
    unsigned int failed = 0, executed = 0;
    int all = argc == 1 || (argc == 2 && strcmp(argv[1], "--all") == 0);

    if (argc > 2) {
        fprintf(stderr, "Usage: %s [command | --all | --help]\n", argv[0]);
        return 2;
    }
    if (argc == 2 && (!strcmp(argv[1], "--help") || !strcmp(argv[1], "--list"))) {
        printf("Usage: %s [command | --all | --help]\n", argv[0]);
        for (size_t i = 0; i < sizeof(commands) / sizeof(commands[0]); ++i)
            puts(commands[i].name);
        return 0;
    }
    for (size_t i = 0; i < sizeof(commands) / sizeof(commands[0]); ++i) {
        if (!all && strcmp(argv[1], commands[i].name))
            continue;
        ++executed;
        failed += run_command(&commands[i]);
    }
    if (!executed) {
        fprintf(stderr, "Unknown command. Use --help.\n");
        return 2;
    }
    printf("Completed: %u commands, %u failures.\n", executed, failed);
    return failed ? 1 : 0;
}
