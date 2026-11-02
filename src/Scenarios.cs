namespace GpStudio;

public static class Scenarios
{
    private static Option Hex(string key, string zh, string en, string value = "", string when = "") => new(key, zh, en, value, null, "hex", when);
    private static Option Number(string key, string zh, string en, string value) => new(key, zh, en, value, null, "number");
    private static Option Choice(string key, string zh, string en, string value, params string[] choices) => new(key, zh, en, value, choices);
    public static readonly Option[] Crypto = [
        Choice("frodoPrg", "FrodoKEM 伪随机生成器", "FrodoKEM PRG", "TEE_ALG_SHAKE128", "TEE_ALG_SHAKE128", "TEE_ALG_SHAKE256", "TEE_ALG_AES_ECB_NOPAD"),
        Choice("frodoEphemeral", "FrodoKEM 临时密钥", "Ephemeral FrodoKEM key", "false", "false", "true"),
        Hex("message", "输入消息（十六进制）", "Message (hex)", "616263"),
        Choice("keySource", "密钥来源", "Key source", "generate", "generate", "input"),
        Number("keyBits", "密钥位数", "Key size (bits)", ""),
        Hex("key", "对称密钥 / IKM", "Secret key / IKM", "000102030405060708090a0b0c0d0e0f", "keySource=input"),
        Hex("key2", "XTS 第二把密钥", "Second XTS key", "101112131415161718191a1b1c1d1e1f", "algorithm=AES_XTS"),
        Choice("ivSource", "IV / nonce 来源", "IV / nonce source", "generate", "generate", "input"),
        Hex("iv", "IV / nonce / tweak", "IV / nonce / tweak", "000102030405060708090a0b0c0d0e0f", "ivSource=input"),
        Hex("aad", "附加认证数据", "Additional authenticated data"),
        Number("tagBits", "认证标签位数", "Authentication tag bits", "128"),
        Choice("curve", "ECC 曲线", "ECC curve", "TEE_ECC_CURVE_NIST_P256", Data.Catalog.Curves.Where(c => c.Generic).Select(c => c.Id).ToArray()),
        Choice("streaming", "数据处理", "Processing", "false", "false", "true"),
        new Option("copyReuse", "复制状态与复用", "Copy state and reuse", "false", ["false", "true"], "toggle"),
        Number("outputBytes", "输出字节数", "Output bytes", "32"),
        Hex("private", "私钥 d / x", "Private key d / x", "", "keySource=input"),
        Hex("publicX", "公钥 X / 编码公钥", "Public X / encoded public key", "", "keySource=input"),
        Hex("publicY", "公钥 Y", "Public Y", "", "keySource=input"),
        Hex("rsaN", "RSA 模数 n", "RSA modulus n", "", "keySource=input"),
        Hex("rsaE", "RSA 公钥指数 e", "RSA public exponent e", "010001"),
        Hex("rsaD", "RSA 私钥指数 d", "RSA private exponent d", "", "keySource=input"),
        Choice("rsaCrt", "RSA CRT 参数", "RSA CRT parameters", "false", "false", "true"),
        Hex("rsaP", "RSA 素数 p", "RSA prime p"),
        Hex("rsaQ", "RSA 素数 q", "RSA prime q"),
        Hex("rsaDp", "RSA CRT 指数 dP", "RSA CRT exponent dP"),
        Hex("rsaDq", "RSA CRT 指数 dQ", "RSA CRT exponent dQ"),
        Hex("rsaQinv", "RSA CRT 系数 qInv", "RSA CRT coefficient qInv"),
        Hex("prime", "DH / DSA 素数 p（空为内置域参数）", "DH / DSA prime p (empty = built-in domain)"),
        Hex("subprime", "DH / DSA 子群阶 q", "DH / DSA subgroup q"),
        Hex("base", "DH / DSA 生成元 g", "DH / DSA generator g"),
        Hex("peerX", "对方公钥 X / 编码公钥", "Peer public X / encoded public key", "", "keySource=input"),
        Hex("peerY", "对方公钥 Y", "Peer public Y", "", "keySource=input"),
        Hex("ephemeralPrivate", "SM2 临时私钥", "SM2 ephemeral private key", "", "keySource=input"),
        Hex("ephemeralX", "SM2 临时公钥 X", "SM2 ephemeral public X", "", "keySource=input"),
        Hex("ephemeralY", "SM2 临时公钥 Y", "SM2 ephemeral public Y", "", "keySource=input"),
        Hex("peerEphemeralX", "对方临时公钥 X", "Peer ephemeral public X", "", "keySource=input"),
        Hex("peerEphemeralY", "对方临时公钥 Y", "Peer ephemeral public Y", "", "keySource=input"),
        Hex("identity", "SM2 用户 ID", "SM2 user ID", "31323334353637383132333435363738"),
        Hex("peerIdentity", "SM2 对方 ID", "SM2 peer ID", "31323334353637383132333435363738"),
        Choice("role", "SM2 密钥交换角色", "SM2 key exchange role", "initiator", "initiator", "responder"),
        Choice("zaSource", "SM2 ZA 处理", "SM2 ZA handling", "compute", "compute", "input"),
        Hex("za", "已有 ZA（32 字节）", "Existing ZA (32 bytes)", "", "zaSource=input"),
        Choice("prehash", "EdDSA 预哈希", "EdDSA prehash", "false", "false", "true"),
        Hex("context", "EdDSA 上下文", "EdDSA context"),
        Choice("mgfHash", "RSA OAEP MGF1 摘要", "RSA OAEP MGF1 hash", "same", "same", "TEE_ALG_SHA1", "TEE_ALG_SHA224", "TEE_ALG_SHA256", "TEE_ALG_SHA384", "TEE_ALG_SHA512", "TEE_ALG_SHA3_224", "TEE_ALG_SHA3_256", "TEE_ALG_SHA3_384", "TEE_ALG_SHA3_512"),
        Hex("label", "RSA OAEP label", "RSA OAEP label"),
        Number("saltLength", "RSA PSS 盐长度（-1 为摘要长度）", "RSA PSS salt length (-1 = digest length)", "-1"),
        Hex("salt", "HKDF salt", "HKDF salt", "000102030405060708090a0b0c"),
        Hex("info", "HKDF info", "HKDF info", "f0f1f2f3f4f5f6f7f8f9"),
        Choice("hash", "HKDF 摘要", "HKDF hash", "TEE_ALG_SHA256", "TEE_ALG_SHA256")
    ];
    private static Scenario S(string id, string zh, string en, string group, string description, string english, params Option[] fields) => new(id, zh, en, group, description, english, fields);
    public static readonly Scenario[] All = [
        S("digest", "摘要计算", "Message digest", "Crypto", "计算摘要，支持分段、复制状态和重置复用。", "Hash a message, copy operation state and reset it.", Crypto),
        S("cipher", "对称加解密", "Symmetric encryption", "Crypto", "加密后解密校验，返回 IV 和密文。", "Encrypt, decrypt and compare; return IV and ciphertext.", Crypto),
        S("mac", "消息认证码", "Message authentication", "Crypto", "计算并验证 MAC。", "Compute and verify a MAC.", Crypto),
        S("ae", "认证加密", "Authenticated encryption", "Crypto", "GCM / CCM 加解密与标签验证。", "GCM / CCM round trip with tag verification.", Crypto),
        S("encrypt", "公钥加解密", "Public-key encryption", "Crypto", "RSA / SM2 加解密，返回密文。", "RSA / SM2 encryption and decryption; return ciphertext.", Crypto),
        S("sign", "签名与验签", "Sign and verify", "Crypto", "RSA、DSA、ECDSA、EdDSA 和 SM2 签名流程。", "RSA, DSA, ECDSA, EdDSA and SM2 signatures.", Crypto),
        S("derive", "密钥协商与派生", "Key agreement and derivation", "Crypto", "DH、ECDH、X25519/X448、SM2 密钥交换和 HKDF。", "DH, ECDH, X25519/X448, SM2 key exchange and HKDF.", Crypto),
        S("kem", "密钥封装与解封装", "Key encapsulation", "Crypto", "生成非对称密钥、封装并解封装 AES 会话密钥；返回封装数据。", "Generate a key pair, encapsulate and decapsulate an AES session key; return encapsulated data.", Crypto),
        S("remote_session", "调用远程 TEE 中的 TA", "Remote TA session", "Communication", "按远程 TEE 名称和 UUID 建立会话并调用命令。", "Open a session in a named remote TEE and invoke a command.",
            new("remoteTee", "远程 TEE 名称", "Remote TEE name", "remote.tee"),
            new("targetUuid", "目标 UUID", "Target UUID", ""), Number("command", "目标命令号", "Target command", "0")),
        S("random", "随机数", "Random bytes", "Core", "在 TA 私有内存生成随机字节后返回。", "Generate random bytes in TA private memory.", Number("bytes", "字节数", "Byte count", "32")),
        S("property", "读取属性", "Read property", "Core", "按名称和类型读取标准或自定义属性。", "Read a named property with its declared type.",
            Choice("set", "属性集", "Property set", "current_ta", "current_ta", "current_client", "implementation"),
            new("name", "属性名称", "Property name", "gpd.ta.description"),
            Choice("type", "属性类型", "Property type", "string", "string", "bool", "u32", "u64", "uuid", "identity", "binary")),
        S("property_enum", "属性枚举", "Enumerate properties", "Core", "枚举、获取名称并重置属性迭代器。", "Enumerate property names and reset the iterator.",
            Choice("set", "属性集", "Property set", "current_ta", "current_ta", "current_client", "implementation")),
        S("storage", "安全存储", "Secure storage", "Storage", "对象创建、读取、写入、定位、截断、重命名与删除。", "Create, read, write, seek, truncate, rename or delete persistent data.",
            Choice("storage", "存储空间", "Storage space", "TEE_STORAGE_PRIVATE", "TEE_STORAGE_PRIVATE", "TEE_STORAGE_PERSO", "TEE_STORAGE_PROTECTED"),
            Choice("action", "操作", "Action", "roundtrip", "roundtrip", "create", "read", "write", "truncate", "rename", "delete"),
            new("objectId", "对象名称", "Object ID", "gp.example"), new("newObjectId", "新名称", "New object ID", "gp.renamed"),
            Hex("message", "数据", "Data", "616263"), Number("offset", "位置", "Offset", "0"), Number("size", "长度", "Length", "3")),
        S("storage_enum", "存储对象枚举", "Enumerate stored objects", "Storage", "列举对象并返回数量。", "Enumerate persistent objects and return the count.",
            Choice("storage", "存储空间", "Storage space", "TEE_STORAGE_PRIVATE", "TEE_STORAGE_PRIVATE", "TEE_STORAGE_PERSO", "TEE_STORAGE_PROTECTED")),
        S("objects", "密钥对象管理", "Key object lifecycle", "Storage", "创建、导入、复制、限制用途、读取属性、重置和释放。", "Allocate, populate, copy, restrict, inspect, reset and free key objects."),
        S("time", "时间与等待", "Time and wait", "Core", "读取 TEE/REE 时间并进行有限等待。", "Read TEE/REE time and perform a bounded wait.", Number("waitMs", "等待毫秒", "Wait milliseconds", "10")),
        S("persistent_time", "TA 持久时间", "TA persistent time", "Core", "设置并读取当前 TA 的持久时间。", "Set and read this TA's persistent time.", Number("seconds", "秒数", "Seconds", "1")),
        S("memory", "内存管理", "Memory management", "Core", "分配、调整大小、检查访问权限、复制、比较与释放。", "Allocate, resize, check access, copy, compare and release memory."),
        S("session", "TA 调用 TA", "TA-to-TA session", "Communication", "调用已有的目标 TA；目标参数类型默认全部为 NONE。", "Call an existing TA with no command parameters by default.",
            new("targetUuid", "目标 UUID", "Target UUID", ""), Number("command", "目标命令号", "Target command", "0")),
        S("cancellation", "取消与等待", "Cancellation and wait", "Communication", "掩蔽、解除掩蔽、读取取消状态并等待。", "Mask, unmask, inspect cancellation and wait.", Number("waitMs", "等待毫秒", "Wait milliseconds", "100")),
        S("shared_memory", "CA 共享内存", "Client shared memory", "Communication", "演示分配、注册、完整及部分共享内存引用。", "Allocated and registered memory, whole and partial references."),
        S("client_cancel", "CA 取消请求", "Client cancellation request", "Communication", "在工作线程执行命令，由另一线程请求取消。", "Invoke on a worker thread and request cancellation from another thread."),
        S("algorithm_support", "算法能力查询", "Algorithm support", "Core", "查询算法与附加条件的支持状态。", "Query an algorithm and its additional element.", Number("element", "附加条件", "Additional element", "0")),
    ];
    public static Scenario Get(string id) => All.SingleOrDefault(s => s.Id == id) ?? throw new ArgumentException("Unknown feature: " + id);
    public static Algorithm? Algorithm(Feature feature) => string.IsNullOrEmpty(feature.Algorithm) ? null :
        Data.Catalog.Algorithms.SingleOrDefault(a => a.Id == feature.Algorithm) ?? throw new ArgumentException("Unknown algorithm: " + feature.Algorithm);
    public static string Value(Feature feature, string key)
    {
        if (feature.Options.TryGetValue(key, out string? value)) return value;
        Algorithm? algorithm = Algorithm(feature);
        if (key == "keyBits") return (algorithm?.KeyBits ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (key == "curve") return algorithm?.Id switch
        {
            "TEE_ALG_ECDSA_SHA1" => "TEE_ECC_CURVE_NIST_P192",
            "TEE_ALG_ECDSA_SHA224" => "TEE_ECC_CURVE_NIST_P224",
            "TEE_ALG_ECDSA_SHA384" => "TEE_ECC_CURVE_NIST_P384",
            "TEE_ALG_ECDSA_SHA512" => "TEE_ECC_CURVE_NIST_P521",
            _ => "TEE_ECC_CURVE_NIST_P256"
        };
        if (key == "message" && (algorithm?.Family == "cipher" || algorithm?.Family == "mac" && algorithm.Id.EndsWith("NOPAD"))) return "00112233445566778899aabbccddeeff" + "00112233445566778899aabbccddeeff";
        if (key == "iv" && algorithm?.Id == "TEE_ALG_CHACHA20") return "000102030405060708090a0b";
        if (key == "key" && algorithm?.Id == "TEE_ALG_CHACHA20") return "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
        if (key == "iv" && algorithm?.Family == "ae") return "000102030405060708090a0b";
        if (key == "iv" && algorithm?.BlockSize == 8) return "0001020304050607";
        if (algorithm?.Id.Contains("SM2") == true && key is "publicX" or "publicY" or "ephemeralX" or "ephemeralY") {
            string scalarKey = key.StartsWith("ephemeral") ? "ephemeralPrivate" : "private";
            if (feature.Options.TryGetValue(scalarKey, out string? scalar) && scalar.Length > 0) {
                var point = CryptoUtilities.Sm2PublicKey(scalar);
                return Convert.ToHexString(key.EndsWith("X") ? point.X : point.Y).ToLowerInvariant();
            }
        }
        if (algorithm != null && Get(feature.Kind).Category == "Crypto" && ExampleInputs.Value(feature, algorithm, key) is string example) return example;
        return Get(feature.Kind).Fields.SingleOrDefault(f => f.Key == key)?.Default ?? "";
    }
}
