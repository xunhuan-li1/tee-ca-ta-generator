// SPDX-License-Identifier: MIT
using System.Text;
namespace GpStudio;

internal static class ClientCode
{
    private sealed record Field(string Name, byte[] Value, string Destination, string Size, int Limit, InputAttribute? Attribute = null);
    private static List<Field> Fields(Feature feature)
    {
        RequestData request = RequestBuilder.Build(feature); var fields = new List<Field>();
        if (feature.Kind is "digest" or "cipher" or "mac" or "ae" or "encrypt" or "sign" or "storage") fields.Add(new("input", request.Message, "data", "data_size", 2048));
        if (feature.Kind == "ae") fields.Add(new("aad", request.Aad, "aad", "aad_size", 1024));
        if (Scenarios.Get(feature.Kind).Category == "Crypto" && Scenarios.Value(feature, "ivSource") == "input" && FieldRules.Visible(feature, "iv")) fields.Add(new("iv", request.Iv, "iv", "iv_size", 256));
        for (int i = 0; i < request.Attributes.Count; i++)
        {
            var a = request.Attributes[i]; string name = a.Id.Replace("TEE_ATTR_", "").ToLowerInvariant() + "_" + a.Group;
            fields.Add(new(name, a.Bytes, $"attributes[{i}].data", $"attributes[{i}].size", 1024, a));
        }
        return fields;
    }
    public static SortedDictionary<string, string> Render(Project project)
    {
        var files = new SortedDictionary<string, string>(); string[] names = Workflows.CommandNames(project);
        var header = new StringBuilder("/* SPDX-License-Identifier: MIT */\n#ifndef GP_CLIENT_API_H\n#define GP_CLIENT_API_H\n#include <stddef.h>\n#include \"client.h\"\n#ifdef __cplusplus\nextern \"C\" {\n#endif\n\n");
        var source = new StringBuilder("/* SPDX-License-Identifier: MIT */\n#include <stdlib.h>\n#include <string.h>\n#include \"client_api.h\"\n\n");
        source.AppendLine("static void clear_request(struct gp_request *request)\n{\n    volatile unsigned char *bytes = (volatile unsigned char *)request;\n    size_t size = sizeof(*request);\n    while (size--)\n        *bytes++ = 0;\n}\n");
        var examples = new StringBuilder("/* SPDX-License-Identifier: MIT */\n#ifndef GP_EXAMPLES_H\n#define GP_EXAMPLES_H\n#include \"client_api.h\"\n");
        for (int i = 0; i < project.Features.Count; i++)
        {
            Feature feature = project.Features[i]; string name = names[i]; var fields = Fields(feature);
            header.AppendLine($"/* {name}: byte spans are caller-owned; the call copies them before invocation. */\nstruct gp_{name}_request {{");
            if (fields.Count == 0) header.AppendLine("    unsigned int reserved; /* Initialize to zero. */");
            foreach (var f in fields) header.AppendLine($"    const uint8_t *{f.Name};\n    size_t {f.Name}_size;");
            header.AppendLine("};");
            string signature = $"TEEC_Result gp_client_{name}(struct gp_client *client, const struct gp_{name}_request *input, struct gp_response *response, uint32_t *origin)";
            header.AppendLine(signature + ";\n"); source.AppendLine(signature + "\n{");
            source.AppendLine("    struct gp_request *request = NULL;\n    TEEC_Result result;\n\n    if (origin)\n        *origin = TEEC_ORIGIN_API;\n    if (response)\n        memset(response, 0, sizeof(*response));\n    if (!client || client->state != 2 || !input || !response)\n        return TEEC_ERROR_BAD_PARAMETERS;");
            foreach (var f in fields) source.AppendLine($"    if (input->{f.Name}_size > {f.Limit}u || (!input->{f.Name} && input->{f.Name}_size))\n        return TEEC_ERROR_BAD_PARAMETERS;");
            source.AppendLine("    request = calloc(1, sizeof(*request));\n    if (!request)\n        return TEEC_ERROR_OUT_OF_MEMORY;\n    request->version = GP_PROTOCOL_VERSION;");
            int attribute = 0;
            foreach (var f in fields)
            {
                if (f.Attribute is InputAttribute a)
                {
                    source.AppendLine($"    request->attributes[{attribute}].id = {RequestBuilder.AttributeId(a.Id)}; /* {a.Id} */\n    request->attributes[{attribute}].group = {a.Group}u;"); attribute++;
                }
                source.AppendLine($"    if (input->{f.Name}_size)\n        memcpy(request->{f.Destination}, input->{f.Name}, input->{f.Name}_size);\n    request->{f.Size} = (uint32_t)input->{f.Name}_size;");
            }
            source.AppendLine($"    request->attribute_count = {attribute}u;");
            string call = feature.Kind == "shared_memory" ? "gp_client_shared_example" : feature.Kind == "client_cancel" ? "gp_client_cancel_example" : "gp_client_invoke";
            source.AppendLine($"    result = {call}(client, {i + 1}u, request, response, origin);\n    clear_request(request);\n    free(request);\n    return result;\n}}\n");
            if (project.CaOutput != "executable") continue;
            string exampleSignature = $"TEEC_Result gp_example_{name}(struct gp_client *client, struct gp_response *response, uint32_t *origin)";
            examples.AppendLine(exampleSignature + ";");
            var example = new StringBuilder("/* SPDX-License-Identifier: MIT */\n#include \"examples.h\"\n\n" + exampleSignature + "\n{\n");
            foreach (var f in fields.Where(f => f.Value.Length > 0)) example.AppendLine($"    static const uint8_t {f.Name}[] = {RequestBuilder.ByteList(f.Value)};");
            example.AppendLine($"    const struct gp_{name}_request request = {{");
            if (fields.Count == 0 || fields.All(f => f.Value.Length == 0)) example.AppendLine("        0");
            foreach (var f in fields.Where(f => f.Value.Length > 0)) example.AppendLine($"        .{f.Name} = {f.Name},\n        .{f.Name}_size = sizeof({f.Name}),");
            example.AppendLine($"    }};\n    TEEC_Result result;\n\n    result = gp_client_{name}(client, &request, response, origin);\n    if (result != TEEC_SUCCESS)\n        return result;\n    return TEEC_SUCCESS;\n}}\n");
            files.Add("ca/examples/" + name + ".c", example.ToString());
        }
        header.AppendLine("#ifdef __cplusplus\n}\n#endif\n#endif"); examples.AppendLine("#endif");
        files.Add("ca/client_api.h", header.ToString()); files.Add("ca/client_api.c", source.ToString());
        if (project.CaOutput == "executable") files.Add("ca/examples/examples.h", examples.ToString());
        return files;
    }
}
