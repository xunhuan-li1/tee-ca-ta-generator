/* SPDX-License-Identifier: MIT */
#ifndef GP_DEMO_SUPPORT_H
#define GP_DEMO_SUPPORT_H

#include <tee_internal_api.h>
#include "gp_protocol.h"

/* GP 1.3.1 identifiers absent from some OP-TEE header releases. */
#ifndef TEE_ATTR_ED448_PUBLIC_VALUE
#define TEE_ATTR_ED448_PUBLIC_VALUE 0xD0000002
#endif
#ifndef TEE_ATTR_ED448_PRIVATE_VALUE
#define TEE_ATTR_ED448_PRIVATE_VALUE 0xC0000003
#endif
#ifndef TEE_STORAGE_PERSO
#define TEE_STORAGE_PERSO 0x00000002
#endif
#ifndef TEE_STORAGE_PROTECTED
#define TEE_STORAGE_PROTECTED 0x00000003
#endif
@CURVE_DEFINES@

TEE_Result gp_make_key(const struct gp_request *request, uint32_t group,
                       uint32_t type, uint32_t bits, uint32_t curve,
                       bool generate, TEE_ObjectHandle *key);
TEE_Result gp_read_attribute(TEE_ObjectHandle key, uint32_t id,
                            void *data, size_t *size);
TEE_Result gp_sm2_za(TEE_ObjectHandle key, const uint8_t *identity,
                     size_t identity_size, uint8_t output[32]);
const struct gp_attribute *gp_attribute(const struct gp_request *request,
                                       uint32_t group, uint32_t id);
@PROTOTYPES@

#endif
