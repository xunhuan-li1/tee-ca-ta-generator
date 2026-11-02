using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GpStudio;

public sealed record Generated(SortedDictionary<string, string> Files, List<string> Warnings);

public static class Generator
{
    public static readonly string[] LoginMethods = ["TEEC_LOGIN_PUBLIC", "TEEC_LOGIN_USER", "TEEC_LOGIN_GROUP", "TEEC_LOGIN_APPLICATION", "TEEC_LOGIN_USER_APPLICATION", "TEEC_LOGIN_GROUP_APPLICATION"];
    public static string Uuid(string value)
    {
        string s = Guid.Parse(value).ToString("N");
        return "{ 0x" + s[..8] + ", 0x" + s[8..12] + ", 0x" + s[12..16] + ", { " +
            string.Join(", ", Enumerable.Range(0, 8).Select(i => "0x" + s.Substring(16 + 2 * i, 2))) + " } }";
    }
    private static bool ValidUuid(string value) => Regex.IsMatch(value ?? "", "^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$") && Guid.TryParse(value, out Guid uuid) && uuid != Guid.Empty;
    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    public static void Validate(Project project)
    {
        if (project.FormatVersion != 1) throw new ArgumentException("FormatVersion must be 1.");
        if (!Regex.IsMatch(project.Name ?? "", "^[a-z][a-z0-9_]{0,47}$") || Regex.IsMatch(project.Name!, "^(con|prn|aux|nul|com[1-9]|lpt[1-9])$"))
            throw new ArgumentException("Invalid project name.");
        if (project.Description == null || project.Description.Length > 512 ||
            project.Description.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new ArgumentException("Description must contain at most 512 characters and no unsupported control characters.");
        if (string.IsNullOrWhiteSpace(project.Version) || project.Version.Any(char.IsControl))
            throw new ArgumentException("Version must be a nonempty string without control characters (for example, 1.0.1).");
        if (!ValidUuid(project.Uuid)) throw new ArgumentException("Invalid nonzero project UUID.");
        if (project.CaOutput is not ("executable" or "shared" or "static")) throw new ArgumentException("Invalid CaOutput.");
        if (!LoginMethods.Contains(project.Login)) throw new ArgumentException("Invalid GP login method.");
        if (project.GroupId is < 0 or > uint.MaxValue) throw new ArgumentException("GroupId must be uint32.");
        if (project.KeepAlive && !project.SingleInstance) throw new ArgumentException("KeepAlive requires SingleInstance.");
        if (project.StackKiB is < 16 or > 1024 || project.HeapKiB is < 128 or > 65536) throw new ArgumentException("StackKiB: 16..1024; HeapKiB: 128..65536.");
        if (project.Features is null || project.Features.Count is < 1 or > 256) throw new ArgumentException("Select 1..256 features.");
        foreach (Feature f in project.Features)
        {
            if (f is null || f.Options is null || f.Kind is null || f.Scenario is null || f.Algorithm is null)
                throw new ArgumentException("Feature, Kind, Scenario, Algorithm and Options must not be null.");
            if (f.Options.Any(item => item.Value == null))
                throw new ArgumentException("Option values must not be null.");
            Scenario scenario = Scenarios.Get(f.Kind);
            if (f.Scenario.Length > 0 && (Workflows.For(f).Kind != f.Kind || f.Algorithm.Length > 0 && !Workflows.For(f).Accepts(Scenarios.Algorithm(f)!))) throw new ArgumentException("Scenario does not match feature or algorithm.");
            if (!project.GenerateCa && f.Kind is "shared_memory" or "client_cancel") continue;
            Algorithm? a = Scenarios.Algorithm(f);
            bool crypto = scenario.Category == "Crypto";
            if (a != null) Core14.Validate(f, a);
            if (crypto && (a == null || a.Family != f.Kind)) throw new ArgumentException("Choose an algorithm belonging to " + f.Kind + ".");
            if (!crypto && f.Kind != "algorithm_support" && a != null) throw new ArgumentException("Algorithm is not applicable to " + f.Kind + ".");
            if (f.Kind == "algorithm_support" && a == null) throw new ArgumentException("Select an algorithm to query.");
            foreach (var item in f.Options)
            {
                Option? field = scenario.Fields.SingleOrDefault(o => o.Key == item.Key);
                if (field == null || item.Value == null) throw new ArgumentException("Unknown option: " + item.Key);
                if (field.Choices != null && !field.Choices.Contains(item.Value)) throw new ArgumentException("Invalid choice: " + item.Key);
            }
            string V(string key) => Scenarios.Value(f, key);
            int N(string key, int low, int high) => RequestBuilder.Number(f, key, low, high);
            if (f.Kind is "session" or "remote_session")
            {
                if (!ValidUuid(V("targetUuid")) || f.Kind == "session" && Guid.Parse(V("targetUuid")) == Guid.Parse(project.Uuid)) throw new ArgumentException("Enter a different, existing target TA UUID.");
                if (!uint.TryParse(V("command"), out _)) throw new ArgumentException("Target command must be uint32.");
            }
            if (f.Kind is "time" or "cancellation") N("waitMs", 0, 60000);
            if (f.Kind == "persistent_time") N("seconds", 0, int.MaxValue);
            if (f.Kind == "random") N("bytes", 1, 4096);
            if (f.Kind == "algorithm_support") N("element", 0, int.MaxValue);
            if (f.Kind == "property" && (!Regex.IsMatch(V("name"), "^[A-Za-z0-9_.-]{1,128}$"))) throw new ArgumentException("Invalid property name.");
            if (f.Kind == "storage")
            {
                if (!Regex.IsMatch(V("objectId"), "^[A-Za-z0-9_.-]{1,64}$") || !Regex.IsMatch(V("newObjectId"), "^[A-Za-z0-9_.-]{1,64}$")) throw new ArgumentException("Invalid storage object ID.");
                N("offset", 0, int.MaxValue); N("size", 0, 4096);
                if (V("action") == "roundtrip" && N("offset", 0, int.MaxValue) != 0) throw new ArgumentException("Roundtrip starts at offset 0.");
                if (V("action") == "rename" && V("objectId") == V("newObjectId")) throw new ArgumentException("Rename requires a different object ID.");
            }
            if (f.Kind == "remote_session" && !Regex.IsMatch(V("remoteTee"), "^[A-Za-z0-9_.:/-]{1,128}$"))
                throw new ArgumentException("Enter a remote TEE name (1..128 ASCII letters, digits, _, ., :, / or -). ");
            RequestData request = RequestBuilder.Build(f);
            if (!crypto) continue;
            int bits = RequestBuilder.KeyBits(f, a!);
            if (a!.KeyType is "TEE_TYPE_AES" or "TEE_TYPE_SM4")
            {
                if (!(a.KeyType == "TEE_TYPE_SM4" ? bits == 128 : bits is 128 or 192 or 256)) throw new ArgumentException("Invalid symmetric key length.");
                if (a.Id == "TEE_ALG_AES_XTS" && bits == 192) throw new ArgumentException("XTS requires 128 or 256 bits per key.");
            }
            if (a.KeyType == "TEE_TYPE_CHACHA20" && bits != 256) throw new ArgumentException("ChaCha20 requires a 256-bit key in this example.");
            if (a.KeyType == "TEE_TYPE_HKDF" && (bits < 8 || bits > 1024 || bits % 8 != 0)) throw new ArgumentException("HKDF-SHA256 key: 8..1024 bits, multiple of 8.");
            if (a.KeyType == "TEE_TYPE_DES" && bits != 64 || a.KeyType == "TEE_TYPE_DES3" && bits is not (128 or 192)) throw new ArgumentException("Invalid DES / 3DES key length.");
            if (a.KeyType == "TEE_TYPE_RSA_KEYPAIR" && bits is not (256 or 512 or 768 or 1024 or 1536 or 2048 or 3072 or 4096 or 6144 or 7680 or 8192)) throw new ArgumentException("RSA key bits: 256, 512, 768, 1024, 1536, 2048, 3072, 4096, 6144, 7680, 8192.");
            if (a.KeyType.StartsWith("TEE_TYPE_HMAC_"))
            {
                int minimum = a.KeyType switch { "TEE_TYPE_HMAC_MD5" => 64, "TEE_TYPE_HMAC_SHA1" or "TEE_TYPE_HMAC_SM3" => 80, "TEE_TYPE_HMAC_SHA224" => 112, "TEE_TYPE_HMAC_SHA256" or "TEE_TYPE_HMAC_SHA3_224" => 192, _ => 256 };
                int maximum = a.KeyType == "TEE_TYPE_HMAC_SHA3_384" ? 1088 : a.KeyType == "TEE_TYPE_HMAC_SHA3_512" ? 1152 : a.KeyType is "TEE_TYPE_HMAC_MD5" or "TEE_TYPE_HMAC_SHA1" or "TEE_TYPE_HMAC_SHA224" ? 512 : 1024;
                if (bits % 8 != 0 || bits < minimum || bits > maximum) throw new ArgumentException($"{a.KeyType}: {minimum}..{maximum} bits, multiple of 8.");
            }
            if (a.KeyType == "TEE_TYPE_DH_KEYPAIR" && (bits is < 256 or > 4096 || bits % 8 != 0)) throw new ArgumentException("DH example: 256..4096 bits, multiple of 8; the returned generic secret is limited to 4096 bits by Core 1.4 table 5-9.");
            if (a.KeyType == "TEE_TYPE_DSA_KEYPAIR" && !(a.Digest == "TEE_ALG_SHA1" ? bits is >= 512 and <= 1024 && bits % 64 == 0 : a.Digest == "TEE_ALG_SHA224" ? bits == 2048 : bits is 2048 or 3072)) throw new ArgumentException("Invalid DSA key size for the selected digest.");
            if (a.KeyType == "TEE_TYPE_RSA_KEYPAIR" && a.Family == "sign" && !a.Id.Contains("PSS") && bits / 8 < a.DigestBytes + 30) throw new ArgumentException("RSA modulus too short for the selected signature digest.");
            if (a.Id.EndsWith("NOPAD") && a.Family is "cipher" or "mac" && request.Message.Length % a.BlockSize != 0) throw new ArgumentException("No-padding input must be block-aligned.");
            if (a.Id == "TEE_ALG_AES_CTS" && request.Message.Length < 17) throw new ArgumentException("CTS requires at least 17 bytes.");
            if (a.Id == "TEE_ALG_AES_XTS" && request.Message.Length < 16) throw new ArgumentException("XTS requires at least 16 bytes.");
            if (V("keySource") == "input" && a.Id == "TEE_ALG_AES_XTS")
            {
                byte[] first = RequestBuilder.Hex(V("key"), "key");
                byte[] second = RequestBuilder.Hex(V("key2"), "key2");
                if (first.Length != second.Length || first.SequenceEqual(second)) throw new ArgumentException("XTS requires two distinct keys of equal size.");
            }
            if (a.Id == "TEE_ALG_CHACHA20" && V("ivSource") == "input" && request.Iv.Length is not (8 or 12)) throw new ArgumentException("ChaCha20 nonce must be 8 or 12 bytes.");
            if (a.Family == "cipher" && V("ivSource") == "input" && request.Iv.Length != IvBytes(f, a)) throw new ArgumentException("Incorrect IV / tweak size.");
            if (a.Family == "ae")
            {
                int tag = N("tagBits", 32, 128);
                if (a.Id == "TEE_ALG_AES_CCM" ? tag % 16 != 0 : tag is not (96 or 104 or 112 or 120 or 128)) throw new ArgumentException("Invalid AE tag length.");
                int nonce = V("ivSource") == "input" ? request.Iv.Length : 12;
                if (a.Id == "TEE_ALG_AES_CCM" ? nonce is < 7 or > 13 : nonce is < 1 or > 256) throw new ArgumentException("Invalid nonce size.");
            }
            if (a.Family is "digest" or "derive") N("outputBytes", 1, a.Family == "derive" ? 512 : 4096);
            if (a.Id.Contains("OAEP") && request.Message.Length > bits / 8 - 2 * a.DigestBytes - 2) throw new ArgumentException("Message too long for this OAEP hash and key.");
            if (a.Id == "TEE_ALG_RSAES_PKCS1_V1_5" && request.Message.Length > bits / 8 - 11) throw new ArgumentException("Message too long for RSAES-PKCS1-v1_5.");
            if (a.Id == "TEE_ALG_RSA_NOPAD" && request.Message.Length >= bits / 8) throw new ArgumentException("Raw RSA example requires an integer smaller than the modulus width.");
            if (a.Id.Contains("PSS"))
            {
                int salt = N("saltLength", -1, 512);
                if ((salt < 0 ? a.DigestBytes : salt) > (bits - 1 + 7) / 8 - a.DigestBytes - 2) throw new ArgumentException("PSS salt does not fit the RSA modulus.");
            }
            if (a.Id == "TEE_ALG_SM2_DSA_SM3" && V("zaSource") == "input" &&
                (V("keySource") != "input" || RequestBuilder.Hex(V("za"), "za").Length != 32)) throw new ArgumentException("Supplied ZA requires an imported matching key and 32-byte ZA.");
        }
    }

    private static int IvBytes(Feature f, Algorithm a) => a.Family == "ae" ?
        Scenarios.Value(f, "ivSource") == "input" ? RequestBuilder.Hex(Scenarios.Value(f, "iv"), "iv").Length : 12 :
        a.Id == "TEE_ALG_CHACHA20" ? Scenarios.Value(f, "ivSource") == "input" ? RequestBuilder.Hex(Scenarios.Value(f, "iv"), "iv").Length : 12 :
        a.Id.Contains("ECB") ? 0 : a.BlockSize;
    private static string Defines(Feature f, string template)
    {
        Algorithm? a = Scenarios.Algorithm(f);
        string V(string key) => Scenarios.Value(f, key);
        string B(bool value) => value ? "1" : "0";
        string N(string key, string fallback = "0") =>
            long.TryParse(V(key), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long number)
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : V(key) == "" ? fallback : V(key);
        bool crypto = Scenarios.Get(f.Kind).Category == "Crypto";
        int bits = crypto ? RequestBuilder.KeyBits(f, a!) : 0;
        string curve = a?.KeyType is "TEE_TYPE_ECDSA_KEYPAIR" or "TEE_TYPE_ECDH_KEYPAIR" ?
            V("curve") : "0";
        bool ed = a?.Id is "TEE_ALG_ED25519" or "TEE_ALG_ED448";
        string digest = ed ? a!.Id == "TEE_ALG_ED448" ? "TEE_ALG_SHAKE256" : "TEE_ALG_SHA512" : a?.Digest ?? "TEE_ALG_SHA256";
        int outputBytes = f.Kind == "random" ? int.Parse(N("bytes")) :
            crypto ? a!.Family == "digest" && a.Id is not ("TEE_ALG_SHAKE128" or "TEE_ALG_SHAKE256") ? a.DigestBytes : int.Parse(N("outputBytes", "32")) : 32;
        string peerX = a?.KeyType switch { "TEE_TYPE_DH_KEYPAIR" => "TEE_ATTR_DH_PUBLIC_VALUE", "TEE_TYPE_X25519_KEYPAIR" => "TEE_ATTR_X25519_PUBLIC_VALUE", "TEE_TYPE_X448_KEYPAIR" => "TEE_ATTR_X448_PUBLIC_VALUE", _ => "TEE_ATTR_ECC_PUBLIC_VALUE_X" };
        string peerY = a?.KeyType == "TEE_TYPE_ECDH_KEYPAIR" ? "TEE_ATTR_ECC_PUBLIC_VALUE_Y" : "0";
        var macros = new Dictionary<string, string>
        {
            ["SESSION_BITS"] = (a?.Id == "TEE_ALG_FRODOKEM" ? bits == 640 ? 128 : bits == 976 ? 192 : 256 : 256).ToString(),
            ["FRODO_PRG"] = V("frodoPrg"), ["FRODO_EPHEMERAL"] = B(V("frodoEphemeral") == "true"),
            ["PUBLIC_KEY_TYPE"] = a?.KeyType.Replace("_KEYPAIR", "_PUBLIC_KEY") ?? "0",
            ["ALGORITHM"] = a?.Id ?? "0", ["KEY_TYPE"] = a?.KeyType is { Length: > 0 } type ? type : "TEE_TYPE_GENERIC_SECRET",
            ["KEY_BITS"] = bits.ToString(), ["CURVE"] = curve, ["GENERATE"] = B(V("keySource") != "input"),
            ["STREAM"] = B(V("streaming") == "true"), ["IV_GENERATE"] = B(V("ivSource") != "input"),
            ["IV_BYTES"] = crypto ? IvBytes(f, a!).ToString() : "0", ["BLOCK_SIZE"] = (a?.BlockSize ?? 16).ToString(),
            ["OUTPUT_BYTES"] = outputBytes.ToString(), ["NO_PADDING"] = B(a?.Id.EndsWith("NOPAD") == true),
            ["XTS"] = B(a?.Id == "TEE_ALG_AES_XTS"), ["CTS"] = B(a?.Id == "TEE_ALG_AES_CTS"),
            ["RAW_RSA"] = B(a?.Id == "TEE_ALG_RSA_NOPAD"), ["OAEP"] = B(a?.Id.Contains("OAEP") == true),
            ["MGF_HASH"] = V("mgfHash") is "" or "same" ? a?.Digest ?? "TEE_ALG_SHA256" : V("mgfHash"),
            ["PSS"] = B(a?.Id.Contains("PSS") == true), ["SM2"] = B(a?.Id == "TEE_ALG_SM2_DSA_SM3"),
            ["EDDSA"] = B(ed), ["PREHASH"] = B(V("prehash") == "true"), ["DIGEST"] = digest,
            ["DIGEST_BYTES"] = (ed ? 64 : a?.DigestBytes ?? 32).ToString(),
            ["SALT_LENGTH"] = N("saltLength", "-1") == "-1" ? (a?.DigestBytes ?? 32).ToString() : N("saltLength"),
            ["TAG_BITS"] = N("tagBits", "128"), ["ZA_INPUT"] = B(V("zaSource") == "input"),
            ["ROLE"] = B(V("role") == "responder"), ["HASH"] = V("hash") == "" ? "TEE_ALG_SHA256" : V("hash"),
            ["DERIVED_BITS"] = (a?.Id == "TEE_ALG_HKDF" ? outputBytes * 8 : (bits + 7) / 8 * 8).ToString(),
            ["PEER_X"] = peerX, ["PEER_Y"] = peerY,
            ["WAIT_MS"] = N("waitMs", "100"), ["SECONDS"] = N("seconds"), ["ELEMENT"] = N("element"),
            ["OBJECT_ID"] = Quote(V("objectId")), ["NEW_OBJECT_ID"] = Quote(V("newObjectId")), ["OFFSET"] = N("offset"), ["SIZE"] = N("size"),
            ["STORAGE"] = V("storage") == "" ? "TEE_STORAGE_PRIVATE" : V("storage"),
            ["STORAGE_ACTION"] = Array.IndexOf(new[] { "roundtrip", "create", "read", "write", "truncate", "rename", "delete" }, V("action")).ToString(),
            ["PROPERTY_NAME"] = Quote(V("name")),
            ["PROPERTY_SET"] = V("set") switch { "current_client" => "TEE_PROPSET_CURRENT_CLIENT", "implementation" => "TEE_PROPSET_TEE_IMPLEMENTATION", _ => "TEE_PROPSET_CURRENT_TA" },
            ["PROPERTY_TYPE"] = Array.IndexOf(new[] { "string", "bool", "u32", "u64", "uuid", "identity", "binary" }, V("type")).ToString()
        };
        if (f.Kind is "session" or "remote_session") { macros["TARGET_UUID"] = Uuid(V("targetUuid")); macros["TARGET_COMMAND"] = N("command"); }
        if (f.Kind == "remote_session") macros["REMOTE_TEE"] = Quote(V("remoteTee"));
        return string.Join("\n", macros.Where(pair => Regex.IsMatch(template, @"\bGP_" + pair.Key + @"\b"))
            .Select(pair => "#define GP_" + pair.Key + " " + pair.Value));
    }

    internal static string TemplateName(Feature feature) => feature.Kind switch { "sign" when Core14.IsPqc(Scenarios.Algorithm(feature)!) => "pqc_sign", "client_cancel" => "cancellation", "shared_memory" => "memory", "derive" when feature.Algorithm == "TEE_ALG_SM2_KEP" => "sm2_kep", _ => feature.Kind };

    internal static string SelectTemplate(Feature feature, string template)
    {
        if (feature.Kind != "digest") return template;
        foreach (var (section, enabled) in new[] {
            ("COPY", Scenarios.Value(feature, "copyReuse") == "true"),
            ("STREAM", Scenarios.Value(feature, "streaming") == "true")
        }) {
            template = Regex.Replace(template, "@" + section + "_BEGIN@\\r?\\n(.*?)@" + section + "_END@\\r?\\n",
                match => enabled ? match.Groups[1].Value : "", RegexOptions.Singleline);
        }
        return template;
    }

    public static Generated Generate(Project project)
    {
        Validate(project);
        if (!project.GenerateCa) {
            project = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project, Data.Json), Data.Json)!;
            project.Features.RemoveAll(f => f.Kind is "shared_memory" or "client_cancel");
        }
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        string Template(string name) => Data.Asset("templates/" + name);
        void Add(string path, string text)
        {
            if (Regex.IsMatch(text, "@[A-Z_]+@")) throw new InvalidDataException("Unresolved template field in " + path);
            if (path.StartsWith("ta/") && path.EndsWith(".c")) {
                if (path == "ta/sm2_za.c" && !project.PanicSafe) text = text.Replace("cleanup:\n", "");
                text = Core14.CheckedCalls(text, project.PanicSafe);
            }
            if (path.EndsWith(".c") || path.EndsWith(".h")) text = CSourceFormat.Format(text);
            files.Add(path, text.Replace("\r\n", "\n").TrimEnd() + "\n");
        }
        var declarations = new StringBuilder();
        var commands = new StringBuilder();
        var dispatch = new StringBuilder();
        var sources = new List<string> { "ta_entry.c", "ta_support.c" };
        for (int i = 0; i < project.Features.Count; ++i)
        {
            Feature feature = project.Features[i];
            string function = "gp_" + Workflows.CommandNames(project)[i];
            string template = TemplateName(feature);
            string file = "examples/" + function + ".c";
            sources.Add(file);
            string body = SelectTemplate(feature, Template(template + ".c"));
            Add("ta/" + file, body.Replace("@FUNCTION@", function).Replace("@DEFINES@", Defines(feature, body)));
            declarations.AppendLine($"TEE_Result {function}(struct gp_request *request, struct gp_response *response);");
            commands.AppendLine($"#define GP_CMD_{feature.Kind.ToUpperInvariant()}_{i + 1} {i + 1}u");
            dispatch.AppendLine($"    case {i + 1}:\n        result = {function}(request, response);\n        break;");
        }
        bool za = project.Features.Any(f => f.Algorithm == "TEE_ALG_SM2_DSA_SM3" && Scenarios.Value(f, "zaSource") != "input");
        if (za) { Add("ta/sm2_za.c", Template("sm2_za.c")); sources.Add("sm2_za.c"); }
        Add("common/gp_protocol.h", Template("protocol.h").Replace("@RESULT_CAPACITY@", project.Features.Any(f => Scenarios.Algorithm(f) is Algorithm a && Core14.IsPqc(a)) ? "32768" : "4096").Replace("@UUID@", Uuid(project.Uuid)).Replace("@COMMANDS@", commands.ToString()));
        string curveDefines = string.Join("\n", Data.Catalog.Curves.Select(c => $"#ifndef {c.Id}\n#define {c.Id} {c.Value}\n#endif"));
        Add("ta/include/gp_support.h", Template("ta_support.h").Replace("@PROTOTYPES@", declarations.ToString()).Replace("@CURVE_DEFINES@", curveDefines));
        Add("ta/ta_support.c", Template("ta_support.c"));
        string entry = Template("ta_entry.c").Replace("@MASK_PANICS@", project.PanicSafe ? "TEE_Result result = TEE_MaskPanics(true);\n    if (result != TEE_SUCCESS)\n        return result;" : "").Replace("@DISPATCH@", dispatch.ToString());
        entry = entry.Replace("@SESSION_TYPE@", project.SessionContext ? "struct gp_session { uint32_t calls; };" : "")
            .Replace("@SESSION_OPEN@", project.SessionContext ? "*context = TEE_Malloc(sizeof(struct gp_session), TEE_MALLOC_FILL_ZERO);\n    if (!*context)\n        return TEE_ERROR_OUT_OF_MEMORY;" : "*context = NULL;")
            .Replace("@SESSION_CLOSE@", project.SessionContext ? "if (context) {\n        TEE_MemFill(context, 0, sizeof(struct gp_session));\n        TEE_Free(context);\n    }" : "(void)context;")
            .Replace("@SESSION_INVOKE@", project.SessionContext ? "if (!context)\n        goto cleanup;\n    ++((struct gp_session *)context)->calls;" : "");
        Add("ta/ta_entry.c", entry);
        var flags = new List<string>();
        if (project.SingleInstance) flags.Add("TA_FLAG_SINGLE_INSTANCE");
        if (project.MultiSession) flags.Add("TA_FLAG_MULTI_SESSION");
        if (project.KeepAlive) flags.Add("TA_FLAG_INSTANCE_KEEP_ALIVE");
        Add("ta/user_ta_header_defines.h", "/* SPDX-License-Identifier: MIT */\n#ifndef USER_TA_HEADER_DEFINES_H\n#define USER_TA_HEADER_DEFINES_H\n#include \"gp_protocol.h\"\n#define TA_UUID GP_DEMO_UUID\n#define TA_FLAGS (" + (flags.Count == 0 ? "0" : string.Join(" | ", flags)) + ")\n#define TA_STACK_SIZE (" + project.StackKiB + " * 1024)\n#define TA_DATA_SIZE (" + project.HeapKiB + " * 1024)\n#define TA_VERSION " + Quote(project.Version) + "\n#define TA_DESCRIPTION " + Quote(project.Description) + "\n#endif\n");
        Add("ta/Makefile", "BINARY := " + project.Uuid + "\nCFG_TA_OPTEE_CORE_API_COMPAT_1_1 := n\nifeq ($(wildcard $(TA_DEV_KIT_DIR)/mk/ta_dev_kit.mk),)\n$(error Set TA_DEV_KIT_DIR to the OP-TEE exported TA development kit)\nendif\ninclude $(TA_DEV_KIT_DIR)/mk/ta_dev_kit.mk\n");
        Add("ta/sub.mk", "global-incdirs-y += include ../common\ncflags-y += -Wall -Wextra -Werror\n" + string.Join("\n", sources.Select(s => "srcs-y += " + s)));
        if (project.GenerateCa)
        {
            bool clientExamples = project.Features.Any(f => f.Kind is "shared_memory" or "client_cancel");
            string extraPrototypes = clientExamples ?
                "TEEC_Result gp_client_shared_example(struct gp_client *client, uint32_t command,\n" +
                "                                    const struct gp_request *request,\n" +
                "                                    struct gp_response *response, uint32_t *origin);\n" +
                "TEEC_Result gp_client_cancel_example(struct gp_client *client, uint32_t command,\n" +
                "                                    const struct gp_request *request,\n" +
                "                                    struct gp_response *response, uint32_t *origin);" : "";
            Add("ca/client.h", Template("client.h").Replace("@CLIENT_EXAMPLE_PROTOTYPES@", extraPrototypes));
            string login = project.Login.Contains("GROUP") ?
                $"const uint32_t group = {project.GroupId}u;\n    return gp_client_open_with_login(client, {project.Login}, &group, origin);" :
                $"return gp_client_open_with_login(client, {project.Login}, NULL, origin);";
            Add("ca/client.c", Template("client.c").Replace("@DEFAULT_LOGIN@", login));
            foreach (var file in ClientCode.Render(project)) Add(file.Key, file.Value);
            if (project.Features.Any(f => f.Kind is "shared_memory" or "client_cancel")) Add("ca/client_examples.c", Template("client_examples.c"));
            if (project.CaOutput == "executable") Add("ca/main.c", HostMain(project, Template("main.c")));
            Add("ca/Makefile", HostMakefile(project));
            Add("ca/Android.mk", AndroidMakefile(project));
        }
        Add("Makefile", ".PHONY: all ta ca clean\nall: ta" + (project.GenerateCa ? " ca" : "") + "\nta:\n\t$(MAKE) -C ta\n" + (project.GenerateCa ? "ca:\n\t$(MAKE) -C ca\n" : "") + "clean:\n\t$(MAKE) -C ta clean\n" + (project.GenerateCa ? "\t$(MAKE) -C ca clean\n" : ""));
        Add("README.md", ProjectReadme(project));
        Add("LICENSE", Data.Asset("project_license.txt"));
        if (project.Features.Any(f => f.Algorithm.Contains("448") || f.Algorithm.Contains("SM4") && f.Algorithm.Contains("PKCS5")))
            warnings.Add("Some optional algorithms may be declared but not implemented by the target OP-TEE build. Compilation does not establish runtime support.");
        if (project.Features.Any(f => f.Kind is "session" or "remote_session")) warnings.Add("The target TA and its command protocol must exist on the device.");
        return new(files, warnings);
    }

    private static string HostMain(Project project, string template)
    {
        string[] names = Workflows.CommandNames(project);
        string rows = string.Join("\n", project.Features.Select((f, i) => $"    {{ \"{names[i]}\", gp_example_{names[i]} }},"));
        return template.Replace("@COMMAND_TABLE@", rows);
    }

    private static string HostMakefile(Project project)
    {
        string sources = "client.c client_api.c" + (project.CaOutput == "executable" ? " main.c " + string.Join(" ", Workflows.CommandNames(project).Select(n => "examples/" + n + ".c")) : "") + (project.Features.Any(f => f.Kind is "shared_memory" or "client_cancel") ? " client_examples.c" : "");
        string target = project.CaOutput switch { "shared" => "lib" + project.Name + "_ca.so", "static" => "lib" + project.Name + "_ca.a", _ => project.Name + "_ca" };
        return "CC := $(CROSS_COMPILE)gcc\nAR := $(CROSS_COMPILE)ar\nTEEC_LIB_DIR ?= $(TEEC_EXPORT)/lib\nCPPFLAGS += -I../common -I. -I$(TEEC_EXPORT)/include\nCFLAGS += -std=c11 -O2 -Wall -Wextra -Werror -fPIC -pthread\nLDLIBS += -L$(TEEC_LIB_DIR) -lteec -pthread\nSOURCES := " + sources + "\nOBJECTS := $(SOURCES:.c=.o)\nTARGET := " + target + "\n.PHONY: all clean\nall: $(TARGET)\n$(TARGET): $(OBJECTS)\n\t" + (project.CaOutput == "static" ? "$(AR) rcs $@ $^" : "$(CC) " + (project.CaOutput == "shared" ? "-shared -Wl,-soname,$(TARGET) " : "") + "$(LDFLAGS) -o $@ $^ $(LDLIBS)") + "\n%.o: %.c client.h client_api.h ../common/gp_protocol.h\n\t$(CC) $(CPPFLAGS) $(CFLAGS) -c -o $@ $<\nclean:\n\t$(RM) $(OBJECTS) $(TARGET)\n";
    }
    private static string AndroidMakefile(Project project)
    {
        string build = project.CaOutput switch { "shared" => "BUILD_SHARED_LIBRARY", "static" => "BUILD_STATIC_LIBRARY", _ => "BUILD_EXECUTABLE" };
        return "LOCAL_PATH := $(call my-dir)\ninclude $(CLEAR_VARS)\nLOCAL_MODULE := " + project.Name + "_ca\nLOCAL_SRC_FILES := client.c client_api.c" + (project.CaOutput == "executable" ? " main.c " + string.Join(" ", Workflows.CommandNames(project).Select(n => "examples/" + n + ".c")) : "") + (project.Features.Any(f => f.Kind is "shared_memory" or "client_cancel") ? " client_examples.c" : "") + "\nLOCAL_C_INCLUDES := $(LOCAL_PATH)/../common $(TEEC_INCLUDE_DIR)\nLOCAL_CFLAGS := -std=c11 -Wall -Wextra -Werror\nLOCAL_SHARED_LIBRARIES := libteec\ninclude $(" + build + ")\n";
    }
    private static string ProjectReadme(Project project) => "# " + project.Name + "\n\nGenerated for GlobalPlatform Core API 1.4 and Client API 1.0 with Errata 2.0; chapter 9 is excluded.\n\n" +
        "Build the TA with `make -C ta TA_DEV_KIT_DIR=/path/to/export-ta_arm64 CROSS_COMPILE=/path/to/aarch64-linux-gnu-`. Build the CA with `make -C ca TEEC_EXPORT=/path/to/staging/usr CROSS_COMPILE=/path/to/aarch64-linux-gnu-`.\n\n" +
        "The signed TA is `ta/" + project.Uuid + ".ta`. It must use the key trusted by your target system. Install it through your platform's normal TA deployment process.\n\n" +
        "CA output: `" + project.CaOutput + "`. Dynamic libraries and executables require a separate libteec.so; static library consumers link libteec separately. Android.mk expects the application to supply a libteec module and TEEC_INCLUDE_DIR.\n\n" +
        "Use `gp_client_open`, the named functions in `ca/client_api.h`, then `gp_client_close`. Fill the per-feature input structure with caller-owned byte spans. `ca/examples/` contains runnable examples for executable outputs; library outputs contain no default input keys. Generic invocation remains in `ca/client.h`. A zero-initialized gp_client is required. Request attributes and buffers are copied into TA private memory before use. Example keys and parameters are public data.\n\n" +
        "The executable runs all selected commands by default, each in a separate session. Use --help or --all. Pass a command name to run one command. Results include the actual data length, IV, tag, GP error and origin. Empty outputs carry no hex dump.\n\n" +
        "An algorithm identifier does not guarantee target support. Optional algorithms can return NOT_SUPPORTED. Compilation is separate from runtime testing and GP conformance certification. Storage actions can modify the selected object. Session calls need a separately deployed target TA.\n\n" +
        string.Join("\n", project.Features.Select((f, i) => $"- `{Workflows.CommandNames(project)[i]}`: {f.Algorithm}")) + "\n";
    public static void Export(Generated project, string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any()) throw new IOException("Output directory must be empty.");
        Directory.CreateDirectory(fullPath);
        foreach (var file in project.Files)
        {
            string path = Path.Combine(fullPath, file.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Value, new UTF8Encoding(false));
        }
    }
}
