// SPDX-License-Identifier: MIT
namespace GpStudio;

public sealed record UseCase(string Id, string Kind, string Chinese, string English,
    string Category, string DefaultAlgorithm = "", string AlgorithmPrefix = "", bool Advanced = false)
{
    public bool Accepts(Algorithm a) => Kind == "algorithm_support" || a.Family == Kind &&
        (AlgorithmPrefix.Length == 0 || (a.Id.StartsWith("ALG_") ? "TEE_" + a.Id : a.Id).StartsWith(AlgorithmPrefix, StringComparison.Ordinal));
    public Feature Create() => new() { Kind = Kind, Scenario = Id, Algorithm = DefaultAlgorithm };
}

public static class Workflows
{
    public static readonly UseCase[] All = [
        new("sm2_encrypt", "encrypt", "SM2 加密 / 解密", "SM2 encryption / decryption", "SM", "TEE_ALG_SM2_PKE", "TEE_ALG_SM2_PKE"),
        new("sm2_sign", "sign", "SM2 签名 / 验签", "SM2 signatures", "SM", "TEE_ALG_SM2_DSA_SM3", "TEE_ALG_SM2_DSA_SM3"),
        new("sm2_kep", "derive", "SM2 密钥交换", "SM2 key exchange", "SM", "TEE_ALG_SM2_KEP", "TEE_ALG_SM2_KEP"),
        new("cipher", "cipher", "对称加密 / 解密", "Symmetric encryption / decryption", "Crypto", "TEE_ALG_AES_CBC_NOPAD"),
        new("digest", "digest", "计算消息摘要", "Compute message digests", "Crypto", "TEE_ALG_SHA256"),
        new("mac", "mac", "消息认证码", "Message authentication codes", "Crypto", "TEE_ALG_HMAC_SHA256"),
        new("ae", "ae", "认证加密", "Authenticated encryption", "Crypto", "TEE_ALG_AES_GCM"),
        new("rsa_encrypt", "encrypt", "RSA 加密 / 解密", "RSA encryption / decryption", "Crypto", "TEE_ALG_RSAES_PKCS1_OAEP_MGF1_SHA256", "TEE_ALG_RSA"),
        new("rsa_sign", "sign", "RSA 签名 / 验签", "RSA signatures", "Crypto", "TEE_ALG_RSASSA_PKCS1_PSS_MGF1_SHA256", "TEE_ALG_RSA"),
        new("ecdsa", "sign", "ECDSA 签名 / 验签", "ECDSA signatures", "Crypto", "TEE_ALG_ECDSA_SHA256", "TEE_ALG_ECDSA"),
        new("derive", "derive", "ECDH 密钥协商", "ECDH key agreement", "Crypto", "TEE_ALG_ECDH_DERIVE_SHARED_SECRET", "TEE_ALG_ECDH"),
        new("hkdf", "derive", "HKDF 密钥派生", "HKDF key derivation", "Crypto", "TEE_ALG_HKDF", "TEE_ALG_HKDF"),
        new("xts", "cipher", "AES-XTS 数据加密", "AES-XTS data encryption", "Crypto", "TEE_ALG_AES_XTS", "TEE_ALG_AES_XTS", true),
        new("dsa", "sign", "DSA 签名 / 验签", "DSA signatures", "Crypto", "TEE_ALG_DSA_SHA256", "TEE_ALG_DSA", true),
        new("dh", "derive", "DH 密钥协商", "DH key agreement", "Crypto", "TEE_ALG_DH_DERIVE_SHARED_SECRET", "TEE_ALG_DH", true),
        new("eddsa", "sign", "EdDSA 签名 / 验签", "EdDSA signatures", "Crypto", "TEE_ALG_ED25519", "TEE_ALG_ED", true),
        new("xdh", "derive", "XDH 密钥协商", "XDH key agreement", "Crypto", "TEE_ALG_X25519", "TEE_ALG_X", true),
        new("random", "random", "生成随机数", "Generate random bytes", "Core"),
        new("time", "time", "时间与等待", "Time and wait", "Core"),
        new("persistent_time", "persistent_time", "TA 持久时间", "TA persistent time", "Core", Advanced:true),
        new("property", "property", "读取属性", "Read properties", "Core"),
        new("property_enum", "property_enum", "属性枚举", "Enumerate properties", "Core", Advanced:true),
        new("custom_property", "property", "查询自定义属性", "Query custom properties", "Core", Advanced:true),
        new("storage", "storage", "安全存储", "Secure storage", "Storage"),
        new("storage_enum", "storage_enum", "存储对象枚举", "Enumerate stored objects", "Storage", Advanced:true),
        new("objects", "objects", "临时密钥对象", "Transient key objects", "Storage", Advanced:true),
        new("key_attributes", "objects", "密钥属性与用途", "Key attributes and usage", "Storage", Advanced:true),
        new("memory", "memory", "内存管理", "Memory management", "Core", Advanced:true),
        new("bigint", "bigint", "大整数运算", "Big integer arithmetic", "Arithmetic", Advanced:true),
        new("fmm", "fmm", "快速模乘", "Fast modular multiplication", "Arithmetic", Advanced:true),
        new("internal_client", "session", "TA 调用另一个 TA", "Call another TA", "Communication"),
        new("shared_memory", "shared_memory", "CA 共享内存", "Client shared memory", "Communication"),
        new("cancel_client", "client_cancel", "CA 取消请求", "Client cancellation", "Communication", Advanced:true),
        new("cancellation", "cancellation", "TA 取消状态", "TA cancellation state", "Communication", Advanced:true),
        new("algorithm_support", "algorithm_support", "算法支持查询", "Query algorithm support", "Core", "TEE_ALG_SHA256", Advanced:true),
    ];
    public static string Description(UseCase w, bool english) => w.Id switch {
        "sm2_encrypt" => english ? "Encrypt with SM2, decrypt and verify, then return the ciphertext." : "使用 SM2 加密、解密并校验，返回密文。",
        "sm2_sign" => english ? "Choose a key and ZA source; sign a message and verify the signature." : "选择密钥和 ZA 来源，对消息签名并验签。",
        "sm2_kep" => english ? "Configure identities, role, long-term and ephemeral keys for SM2 agreement." : "配置身份、角色、长期与临时密钥，进行 SM2 密钥交换。",
        "rsa_encrypt" => english ? "RSA encryption and decryption with selectable padding and keys." : "配置填充方式与密钥，进行 RSA 加解密。",
        "rsa_sign" => english ? "RSA signatures with selectable digest, padding and PSS salt length." : "配置摘要、填充和 PSS 盐长度，进行 RSA 签名验签。",
        "ecdsa" => english ? "Sign and verify with the selected ECC curve and digest." : "选择 ECC 曲线和摘要，进行签名验签。",
        "derive" => english ? "Use your private key and a peer public key to derive an ECDH secret." : "使用本方私钥与对方公钥协商 ECDH 共享秘密。",
        "hkdf" => english ? "Derive key material from IKM, salt and info with HKDF-SHA256." : "使用 IKM、salt 和 info 进行 HKDF-SHA256 密钥派生。",
        "xts" => english ? "Encrypt data with two AES keys and a tweak, then verify decryption." : "使用两把 AES 密钥及 tweak 加密数据并验证解密结果。",
        "dsa" => english ? "Configure DSA domain parameters, key and digest; sign and verify." : "配置 DSA 域参数、密钥和摘要，进行签名验签。",
        "dh" => english ? "Configure DH domain parameters and peer key to agree a shared secret." : "配置 DH 域参数和对方公钥，协商共享秘密。",
        "eddsa" => english ? "EdDSA signatures with optional prehash and context." : "配置预哈希与上下文，进行 EdDSA 签名验签。",
        "xdh" => english ? "X25519 / X448 key agreement with generated or imported keys." : "使用生成或输入的密钥进行 X25519 / X448 密钥协商。",
        "custom_property" => english ? "Query an existing custom property by set, name and type." : "按属性集、名称和类型查询已有自定义属性。",
        _ => english ? Scenarios.Get(w.Kind).DescriptionEn : Scenarios.Get(w.Kind).Description
    };
    public static UseCase For(Feature f)
    {
        if (f.Scenario.Length > 0) return All.Single(w => w.Id == f.Scenario);
        var choices = All.Where(w => w.Kind == f.Kind);
        return choices.FirstOrDefault(w => w.AlgorithmPrefix.Length > 0 && (f.Algorithm.StartsWith("ALG_") ? "TEE_" + f.Algorithm : f.Algorithm).StartsWith(w.AlgorithmPrefix))
            ?? choices.First(w => w.AlgorithmPrefix.Length == 0);
    }
    public static string Name(Feature f) => For(f).Id;
    public static string[] CommandNames(Project p)
    {
        string[] names = p.Features.Select(Name).ToArray();
        return names.Select((name, i) => names.Count(n => n == name) == 1 ? name : name + "_" + (i + 1)).ToArray();
    }
    public static IEnumerable<Algorithm> Algorithms(UseCase useCase) => Data.Catalog.Algorithms.Where(a => useCase.Accepts(a) &&
        (useCase.Id != "cipher" || a.Id != "TEE_ALG_AES_XTS"));
    public static bool NeedsCa(UseCase useCase) => useCase.Kind is "shared_memory" or "client_cancel";
}
