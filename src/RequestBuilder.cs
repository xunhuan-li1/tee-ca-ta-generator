using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace GpStudio;

public sealed record InputAttribute(string Id, uint Group, byte[] Bytes);
public sealed class RequestData
{
    public byte[] Message { get; set; } = [];
    public byte[] Aad { get; set; } = [];
    public byte[] Iv { get; set; } = [];
    public List<InputAttribute> Attributes { get; } = [];
}

public static class RequestBuilder
{
    private static readonly Dictionary<string, string> Ids = JsonSerializer.Deserialize<Dictionary<string, string>>(Data.Asset("attributes.json"))!;
    private static readonly Dictionary<string, Dictionary<string, string>> Domains = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(Data.Asset("domains.json"))!;
    public static string AttributeId(string name) => Ids[name];
    public static byte[] Hex(string value, string name, int maximum = 1024)
    {
        string text = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        if (text.Length % 2 != 0 || text.Length > maximum * 2 || text.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException(name + ": expected hexadecimal bytes (maximum " + maximum + ").");
        return Convert.FromHexString(text);
    }
    public static int Number(Feature feature, string key, int minimum, int maximum)
    {
        if (!int.TryParse(Scenarios.Value(feature, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < minimum || n > maximum)
            throw new ArgumentException(key + $": expected {minimum}..{maximum}.");
        return n;
    }
    public static int KeyBits(Feature f, Algorithm a)
    {
        int bits = Number(f, "keyBits", a.Family == "digest" || a.Id == "TEE_ALG_LMS" ? 0 : 1, 8192);
        if (a.KeyType is "TEE_TYPE_ECDSA_KEYPAIR" or "TEE_TYPE_ECDH_KEYPAIR")
            return Data.Catalog.Curves.Single(c => c.Id == Scenarios.Value(f, "curve")).Bits;
        if (Scenarios.Value(f, "keySource") == "input" &&
            (a.Family is "cipher" or "mac" or "ae" || a.Id == "TEE_ALG_HKDF"))
            return Hex(Scenarios.Value(f, "key"), "key").Length * 8;
        return bits;
    }
    private static byte[] Power(byte[] value, byte[] exponent, byte[] modulus)
    {
        var p = new BigInteger(modulus, true, true);
        var x = new BigInteger(exponent, true, true);
        if (p <= 3 || x < 1 || x >= p - 1) throw new ArgumentException("Invalid domain or private scalar.");
        return BigInteger.ModPow(new BigInteger(value, true, true), x, p).ToByteArray(true, true);
    }
    public static RequestData Build(Feature f)
    {
        var output = new RequestData();
        Algorithm? a = Scenarios.Algorithm(f);
        string V(string key) => Scenarios.Value(f, key);
        byte[] H(string key, int maximum = 1024) => Hex(V(key), key, maximum);
        void Add(string id, uint group, byte[] bytes, bool required = false)
        {
            if (required && bytes.Length == 0) throw new ArgumentException("Required input: " + id);
            if (bytes.Length == 0 && !required) return;
            if (bytes.Length > 1024) throw new ArgumentException("Attribute exceeds 1024 bytes: " + id);
            if (!Ids.ContainsKey(id)) throw new ArgumentException("Unknown GP attribute: " + id);
            output.Attributes.Add(new(id, group, bytes));
        }
        output.Message = H("message", 2048);
        if (a == null || f.Kind == "algorithm_support" || a.Family == "digest") return output;
        output.Aad = H("aad", 1024);
        if (FieldRules.Visible(f, "iv") && V("ivSource") == "input") output.Iv = H("iv", 256);
        bool manual = V("keySource") == "input";
        int bits = KeyBits(f, a);
        if (a.Family is "cipher" or "mac" or "ae" || a.Id == "TEE_ALG_HKDF")
        {
            if (manual)
            {
                Add("TEE_ATTR_SECRET_VALUE", 0, H("key"), true);
                if (a.Id == "TEE_ALG_AES_XTS") Add("TEE_ATTR_SECRET_VALUE", 1, H("key2"), true);
            }
        }
        else if (a.KeyType == "TEE_TYPE_RSA_KEYPAIR")
        {
            Add("TEE_ATTR_RSA_PUBLIC_EXPONENT", 0, H("rsaE"), true);
            if (manual)
            {
                Add("TEE_ATTR_RSA_MODULUS", 0, H("rsaN"), true);
                Add("TEE_ATTR_RSA_PRIVATE_EXPONENT", 0, H("rsaD"), true);
                if (V("rsaCrt") == "true")
                {
                    Add("TEE_ATTR_RSA_PRIME1", 0, H("rsaP"), true);
                    Add("TEE_ATTR_RSA_PRIME2", 0, H("rsaQ"), true);
                    Add("TEE_ATTR_RSA_EXPONENT1", 0, H("rsaDp"), true);
                    Add("TEE_ATTR_RSA_EXPONENT2", 0, H("rsaDq"), true);
                    Add("TEE_ATTR_RSA_COEFFICIENT", 0, H("rsaQinv"), true);
                }
            }
        }
        else if (a.KeyType is "TEE_TYPE_DH_KEYPAIR" or "TEE_TYPE_DSA_KEYPAIR")
        {
            string type = a.KeyType == "TEE_TYPE_DH_KEYPAIR" ? "DH" : "DSA";
            if (!Domains.TryGetValue(type.ToLowerInvariant() + bits, out var domain))
            {
                if (V("prime") == "" || V("base") == "" || type == "DSA" && V("subprime") == "")
                    throw new ArgumentException("Supply p, g and (for DSA) q for this key size.");
                domain = new() { ["p"] = V("prime"), ["g"] = V("base"), ["q"] = V("subprime") };
            }
            byte[] p = V("prime") == "" ? Hex(domain["p"], "prime") : H("prime");
            byte[] g = V("base") == "" ? Hex(domain["g"], "base") : H("base");
            byte[] q = V("subprime") == "" ? domain.TryGetValue("q", out string? qHex) ? Hex(qHex, "subprime") : [] : H("subprime");
            Add("TEE_ATTR_" + type + "_PRIME", 0, p, true);
            Add("TEE_ATTR_" + type + "_BASE", 0, g, true);
            Add("TEE_ATTR_" + type + "_SUBPRIME", 0, q, type == "DSA");
            if (manual)
            {
                byte[] x = H("private");
                Add("TEE_ATTR_" + type + "_PRIVATE_VALUE", 0, x, true);
                Add("TEE_ATTR_" + type + "_PUBLIC_VALUE", 0, V("publicX") == "" ? Power(g, x, p) : H("publicX"), true);
            }
        }
        else if (manual)
        {
            bool ecc = a.KeyType.Contains("ECD") || a.KeyType.Contains("SM2");
            string prefix = ecc ? "ECC" : a.KeyType.Replace("TEE_TYPE_", "").Replace("_KEYPAIR", "");
            Add("TEE_ATTR_" + prefix + "_PRIVATE_VALUE", 0, H("private"), true);
            Add("TEE_ATTR_" + prefix + "_PUBLIC_VALUE" + (ecc ? "_X" : ""), 0, H("publicX"), true);
            if (ecc) Add("TEE_ATTR_ECC_PUBLIC_VALUE_Y", 0, H("publicY"), true);
            if (a.Id == "TEE_ALG_SM2_KEP")
            {
                Add("TEE_ATTR_ECC_PRIVATE_VALUE", 1, H("ephemeralPrivate"), true);
                Add("TEE_ATTR_ECC_PUBLIC_VALUE_X", 1, H("ephemeralX"), true);
                Add("TEE_ATTR_ECC_PUBLIC_VALUE_Y", 1, H("ephemeralY"), true);
            }
        }
        if (a.Family == "derive" && manual && a.Id != "TEE_ALG_HKDF")
        {
            string peerId = a.KeyType switch
            {
                "TEE_TYPE_DH_KEYPAIR" => "TEE_ATTR_DH_PUBLIC_VALUE",
                "TEE_TYPE_X25519_KEYPAIR" => "TEE_ATTR_X25519_PUBLIC_VALUE",
                "TEE_TYPE_X448_KEYPAIR" => "TEE_ATTR_X448_PUBLIC_VALUE",
                _ => "TEE_ATTR_ECC_PUBLIC_VALUE_X"
            };
            Add(peerId, 2, H("peerX"), true);
            if (a.KeyType.Contains("ECDH") || a.KeyType.Contains("SM2"))
                Add("TEE_ATTR_ECC_PUBLIC_VALUE_Y", 2, H("peerY"), true);
            if (a.Id == "TEE_ALG_SM2_KEP")
            {
                Add("TEE_ATTR_ECC_EPHEMERAL_PUBLIC_VALUE_X", 2, H("peerEphemeralX"), true);
                Add("TEE_ATTR_ECC_EPHEMERAL_PUBLIC_VALUE_Y", 2, H("peerEphemeralY"), true);
            }
        }
        if (a.Id == "TEE_ALG_SM2_DSA_SM3")
        {
            Add("TEE_ATTR_SM2_ID_INITIATOR", 10, H("identity"));
            if (V("zaSource") == "input") Add("TEE_ATTR_SECRET_VALUE", 11, H("za"), true);
        }
        if (a.Id == "TEE_ALG_SM2_KEP")
        {
            Add("TEE_ATTR_SM2_ID_INITIATOR", 10, H("identity"), true);
            Add("TEE_ATTR_SM2_ID_RESPONDER", 10, H("peerIdentity"), true);
        }
        if (a.Id.Contains("OAEP")) Add("TEE_ATTR_RSA_OAEP_LABEL", 10, H("label"));
        if (a.Id is "TEE_ALG_ED25519" or "TEE_ALG_ED448") Add("TEE_ATTR_EDDSA_CTX", 10, H("context", 255));
        if (a.Id == "TEE_ALG_HKDF")
        {
            Add("TEE_ATTR_HKDF_SALT", 10, H("salt"));
            Add("TEE_ATTR_HKDF_INFO", 10, H("info"));
        }
        if (output.Attributes.Count > 24) throw new ArgumentException("Too many input attributes.");
        return output;
    }

    public static string ByteList(byte[] bytes) => bytes.Length <= 16 ?
        "{ " + string.Join(", ", bytes.Select(b => "0x" + b.ToString("x2"))) + " }" :
        "{\n" + string.Join(",\n", bytes.Chunk(16).Select(chunk => "            " + string.Join(", ", chunk.Select(b => "0x" + b.ToString("x2"))))) + "\n        }";
}
