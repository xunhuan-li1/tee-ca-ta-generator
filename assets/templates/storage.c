/* SPDX-License-Identifier: MIT */
#include "gp_support.h"
@DEFINES@

TEE_Result @FUNCTION@(struct gp_request *request, struct gp_response *response)
{
    const char object_id[] = GP_OBJECT_ID;
    const char new_id[] = GP_NEW_OBJECT_ID;
    TEE_ObjectHandle object = TEE_HANDLE_NULL;
    uint32_t flags = TEE_DATA_FLAG_ACCESS_READ | TEE_DATA_FLAG_ACCESS_WRITE |
                     TEE_DATA_FLAG_ACCESS_WRITE_META;
    size_t count = 0;
    bool keep_corrupt_handle = false;
    TEE_Result result;

    result = TEE_GetPropertyAsBool(TEE_PROPSET_CURRENT_TA,
                                   "gpd.ta.doesNotCloseHandleOnCorruptObject",
                                   &keep_corrupt_handle);
    if (result != TEE_SUCCESS)
        return result;

    if (GP_STORAGE_ACTION <= 1)
        result = TEE_CreatePersistentObject(GP_STORAGE, object_id, sizeof(object_id) - 1,
                                            flags, TEE_HANDLE_NULL, NULL, 0, &object);
    else
        result = TEE_OpenPersistentObject(GP_STORAGE, object_id, sizeof(object_id) - 1,
                                          flags, &object);
    if (result != TEE_SUCCESS)
        return result;
    if (GP_STORAGE_ACTION == 0 || GP_STORAGE_ACTION == 1 || GP_STORAGE_ACTION == 3) {
        result = TEE_SeekObjectData(object, GP_OFFSET, TEE_DATA_SEEK_SET);
        if (result != TEE_SUCCESS)
            goto cleanup;
        result = TEE_WriteObjectData(object, request->data, request->data_size);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
    if (GP_STORAGE_ACTION == 4) {
        result = TEE_TruncateObjectData(object, GP_SIZE);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
    if (GP_STORAGE_ACTION == 5) {
        result = TEE_RenamePersistentObject(object, new_id, sizeof(new_id) - 1);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
    if (GP_STORAGE_ACTION == 0 || GP_STORAGE_ACTION == 2) {
        result = TEE_SeekObjectData(object, 0, TEE_DATA_SEEK_SET);
        if (result != TEE_SUCCESS)
            goto cleanup;
        result = TEE_ReadObjectData(object, response->data, sizeof(response->data), &count);
        if (result != TEE_SUCCESS)
            goto cleanup;
        response->data_size = (uint32_t)count;
        if (GP_STORAGE_ACTION == 0 && (count != request->data_size ||
            TEE_MemCompare(response->data, request->data, count))) {
            result = TEE_ERROR_SECURITY;
            goto cleanup;
        }
    }
    if (GP_STORAGE_ACTION == 0 || GP_STORAGE_ACTION == 6) {
        TEE_ObjectHandle closing = object;
        object = TEE_HANDLE_NULL;
        result = TEE_CloseAndDeletePersistentObject1(closing);
        if (result != TEE_SUCCESS)
            goto cleanup;
    }
cleanup:
    /* Core 1.3.1: a corrupt-object error may already have closed this handle. */
    if (result == TEE_ERROR_CORRUPT_OBJECT && !keep_corrupt_handle)
        object = TEE_HANDLE_NULL;
    if (object != TEE_HANDLE_NULL)
        TEE_CloseObject(object);
    return result;
}
