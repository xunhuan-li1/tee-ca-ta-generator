/* SPDX-License-Identifier: MIT */
#ifndef GP_DEMO_PROTOCOL_H
#define GP_DEMO_PROTOCOL_H

#include <stdint.h>

#define GP_DEMO_UUID @UUID@
#define GP_PROTOCOL_VERSION 1u
#define GP_MAX_ATTRIBUTES 24u
#define GP_ATTRIBUTE_BYTES 512u
#define GP_DATA_BYTES 2048u
#define GP_AAD_BYTES 1024u
#define GP_RESULT_BYTES 4096u

struct gp_attribute {
    uint32_t id;
    uint32_t group;
    uint32_t a;
    uint32_t b;
    uint32_t size;
    uint8_t data[GP_ATTRIBUTE_BYTES];
};

/* All inputs, including imported keys, travel from CA to TA through this object. */
struct gp_request {
    uint32_t version;
    uint32_t attribute_count;
    uint32_t data_size;
    uint32_t aad_size;
    uint32_t iv_size;
    struct gp_attribute attributes[GP_MAX_ATTRIBUTES];
    uint8_t data[GP_DATA_BYTES];
    uint8_t aad[GP_AAD_BYTES];
    uint8_t iv[256];
};

/* Byte counts describe meaningful data only; reserved capacity is never printed. */
struct gp_response {
    uint32_t data_size;
    uint32_t value_a;
    uint32_t value_b;
    uint32_t iv_size;
    uint32_t tag_size;
    uint8_t iv[256];
    uint8_t tag[16];
    uint8_t data[GP_RESULT_BYTES];
};

@COMMANDS@

#endif
