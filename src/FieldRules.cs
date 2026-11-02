namespace GpStudio;

public static class FieldRules
{
    public static string[]? Choices(Feature feature, Option field)
    {
        Algorithm? algorithm = Scenarios.Algorithm(feature);
        if (field.Key == "tagBits")
            return algorithm?.Id == "TEE_ALG_AES_CCM" ? ["32", "48", "64", "80", "96", "112", "128"] : ["96", "104", "112", "120", "128"];
        if (field.Key == "keyBits")
            return algorithm?.KeyType switch
            {
                "TEE_TYPE_AES" => algorithm.Id == "TEE_ALG_AES_XTS" ? ["128", "256"] : ["128", "192", "256"],
                "TEE_TYPE_MLDSA_KEYPAIR" => ["44", "65", "87"],
                "TEE_TYPE_FALCON_KEYPAIR" => ["512", "1024"],
                "TEE_TYPE_MLKEM_KEYPAIR" => ["512", "768", "1024"],
                "TEE_TYPE_FRODOKEM_KEYPAIR" => ["640", "976", "1344"],
                "TEE_TYPE_NTRU_KEYPAIR" => ["509", "677", "701", "821"],
                "TEE_TYPE_LMS_KEYPAIR" => ["0"],
                "TEE_TYPE_CHACHA20" => ["256"],
                "TEE_TYPE_SM4" => ["128"],
                "TEE_TYPE_DES" => ["64"],
                "TEE_TYPE_DES3" => ["128", "192"],
                "TEE_TYPE_RSA_KEYPAIR" => ["256", "512", "768", "1024", "1536", "2048", "3072", "4096", "6144", "7680", "8192"],
                "TEE_TYPE_DSA_KEYPAIR" when algorithm.Digest == "TEE_ALG_SHA224" => ["2048"],
                "TEE_TYPE_DSA_KEYPAIR" when algorithm.Digest != "TEE_ALG_SHA1" => ["2048", "3072"],
                _ => field.Choices
            };
        return field.Choices;
    }

    public static bool Visible(Feature feature, string key)
    {
        if (feature.Kind == "storage")
        {
            string action = Scenarios.Value(feature, "action");
            return key switch
            {
                "newObjectId" => action == "rename",
                "message" => action is "roundtrip" or "create" or "write",
                "offset" => action is "read" or "write",
                "size" => action is "read" or "truncate",
                _ => true
            };
        }
        Algorithm? a = Scenarios.Algorithm(feature);
        if (a == null || feature.Kind == "algorithm_support") return true;
        if (Core14.IsPqc(a)) return key switch {
            "message" => a.Family == "sign",
            "keyBits" => a.Id != "TEE_ALG_LMS",
            "frodoPrg" or "frodoEphemeral" => a.Id == "TEE_ALG_FRODOKEM",
            _ => false
        };
        string family = a.Family;
        string id = a.Id;
        string type = a.KeyType;
        bool input = Scenarios.Value(feature, "keySource") == "input";
        bool secret = family is "cipher" or "mac" or "ae" || id == "TEE_ALG_HKDF";
        bool rsa = type == "TEE_TYPE_RSA_KEYPAIR";
        bool dsa = type == "TEE_TYPE_DSA_KEYPAIR";
        bool dh = type == "TEE_TYPE_DH_KEYPAIR";
        bool ecc = type is "TEE_TYPE_ECDSA_KEYPAIR" or "TEE_TYPE_ECDH_KEYPAIR";
        bool sm2 = id.Contains("SM2");
        bool kep = id == "TEE_ALG_SM2_KEP";
        return key switch
        {
            "message" => family != "derive",
            "keySource" => family != "digest",
            "keyBits" => !input && secret || rsa || dsa || dh,
            "key" => secret && input,
            "key2" => id == "TEE_ALG_AES_XTS" && input,
            "ivSource" => family == "ae" || family == "cipher" && !id.Contains("ECB"),
            "iv" => (family == "ae" || family == "cipher" && !id.Contains("ECB")) && Scenarios.Value(feature, "ivSource") == "input",
            "aad" or "tagBits" => family == "ae",
            "curve" => ecc,
            "streaming" => family is "digest" or "cipher" or "mac" or "ae",
            "copyReuse" => family == "digest",
            "outputBytes" => id is "TEE_ALG_SHAKE128" or "TEE_ALG_SHAKE256" || id is "TEE_ALG_HKDF" or "TEE_ALG_SM2_KEP",
            "private" or "publicX" => input && !secret && !rsa,
            "publicY" => input && (ecc || sm2),
            "rsaN" or "rsaD" => rsa && input,
            "rsaE" => rsa,
            "rsaCrt" => rsa && input,
            "rsaP" or "rsaQ" or "rsaDp" or "rsaDq" or "rsaQinv" => rsa && input && Scenarios.Value(feature, "rsaCrt") == "true",
            "prime" or "base" => dh || dsa,
            "subprime" => dh || dsa,
            "peerX" => input && family == "derive" && !secret,
            "peerY" => input && family == "derive" && (ecc || sm2),
            "ephemeralPrivate" or "ephemeralX" or "ephemeralY" or "peerEphemeralX" or "peerEphemeralY" => kep && input,
            "identity" => kep || id == "TEE_ALG_SM2_DSA_SM3" && Scenarios.Value(feature, "zaSource") == "compute",
            "peerIdentity" or "role" => kep,
            "zaSource" => id == "TEE_ALG_SM2_DSA_SM3",
            "za" => id == "TEE_ALG_SM2_DSA_SM3" && Scenarios.Value(feature, "zaSource") == "input",
            "prehash" or "context" => id is "TEE_ALG_ED25519" or "TEE_ALG_ED448",
            "label" or "mgfHash" => id.Contains("OAEP"),
            "saltLength" => id.Contains("PSS"),
            "salt" or "info" or "hash" => id == "TEE_ALG_HKDF",
            _ => false
        };
    }
}
