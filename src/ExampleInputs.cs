using System.Text.Json;

namespace GpStudio;

public static class ExampleInputs
{
    public static readonly Dictionary<string, Dictionary<string, string>> Keys =
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(Data.Asset("example_keys.json"))!;

    public static string? Value(Feature feature, Algorithm algorithm, string key)
    {
        if (key == "key")
        {
            if (algorithm.KeyType.StartsWith("TEE_TYPE_HMAC_")) return "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
            if (algorithm.KeyType == "TEE_TYPE_DES") return "133457799bbcdff1";
            if (algorithm.KeyType == "TEE_TYPE_DES3") return "0123456789abcdeffedcba98765432100011223344556677";
            if (algorithm.Id == "TEE_ALG_HKDF") return string.Concat(Enumerable.Repeat("0b", 22));
        }
        string type = algorithm.KeyType;
        int bits = algorithm.KeyBits;
        if (feature.Options.TryGetValue("keyBits", out string? size) && int.TryParse(size, out int n) && n > 0) bits = n;
        if (type is "TEE_TYPE_ECDSA_KEYPAIR" or "TEE_TYPE_ECDH_KEYPAIR")
        {
            string curve = Scenarios.Value(feature, "curve");
            if (curve.Contains("BSI")) return null;
            bits = Data.Catalog.Curves.Single(c => c.Id == curve).Bits;
        }
        string set = type switch
        {
            "TEE_TYPE_ECDSA_KEYPAIR" or "TEE_TYPE_ECDH_KEYPAIR" => "ecc" + bits,
            "TEE_TYPE_RSA_KEYPAIR" => "rsa" + bits,
            "TEE_TYPE_DSA_KEYPAIR" => "dsa" + bits,
            "TEE_TYPE_DH_KEYPAIR" => "dh" + bits,
            "TEE_TYPE_ED25519_KEYPAIR" => "ed25519", "TEE_TYPE_ED448_KEYPAIR" => "ed448",
            "TEE_TYPE_X25519_KEYPAIR" => "x25519", "TEE_TYPE_X448_KEYPAIR" => "x448",
            "TEE_TYPE_SM2_DSA_KEYPAIR" or "TEE_TYPE_SM2_PKE_KEYPAIR" or "TEE_TYPE_SM2_KEP_KEYPAIR" => "sm2",
            _ => ""
        };
        return Keys.TryGetValue(set, out var values) ? values.GetValueOrDefault(key) : null;
    }
}
