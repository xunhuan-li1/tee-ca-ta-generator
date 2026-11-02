using System.Text.RegularExpressions;

namespace GpStudio;

// Rules taken from the November 2025 Public Release, not the 2023 draft.
public static class Core14
{
    public static bool IsPqc(Algorithm a) => a.KeyType is
        "TEE_TYPE_MLDSA_KEYPAIR" or "TEE_TYPE_FALCON_KEYPAIR" or
        "TEE_TYPE_LMS_KEYPAIR" or "TEE_TYPE_MLKEM_KEYPAIR" or
        "TEE_TYPE_FRODOKEM_KEYPAIR" or "TEE_TYPE_NTRU_KEYPAIR";

    public static void Validate(Feature feature, Algorithm algorithm)
    {
        if (feature.Kind == "algorithm_support") return;
        if (algorithm.Limitation.Length > 0)
            throw new ArgumentException(algorithm.Id + ": " + algorithm.Limitation);
        if (!IsPqc(algorithm)) return;
        if (Scenarios.Value(feature, "keySource") != "generate")
            throw new ArgumentException("This PQC example generates its key in the TA. Imported-key attributes require clarification of Core 1.4 tables 5-9 and 6-15.");
        string[]? sizes = FieldRules.Choices(feature, new Option("keyBits", "", "", ""));
        if (sizes != null && !sizes.Contains(Scenarios.Value(feature, "keyBits")))
            throw new ArgumentException("Invalid parameter set for " + algorithm.Id + ".");
    }

    // Use blocks so replacing a void call also preserves an unbraced if body.
    // No wrapper hides a GP error: every generated Result is checked at its call site.
    public static string CheckedCalls(string source, bool maskPanics)
    {
        string[] names = maskPanics
            ? ["ResetOperation", "CopyOperation", "DigestUpdate", "CipherInit", "MACInit",
               "MACUpdate", "AEUpdateAAD", "DeriveKey", "GenerateRandom", "GetSystemTime", "GetREETime"]
            : ["DeriveKey"];
        bool cleanup = Regex.IsMatch(source, @"(?m)^cleanup:");
        foreach (string name in names)
        {
            source = Regex.Replace(source, @"(?m)^(\s*)TEE_" + name + @"\(([^;]*)\);", match =>
                "{\n" + (cleanup ? "result" : "TEE_Result status") + " = TEE_" + name + "_PS(" + match.Groups[2].Value + ");\n" +
                (cleanup ? "if (result != TEE_SUCCESS)\n    goto cleanup;" : "if (status != TEE_SUCCESS)\n    return status;") + "\n}");
        }
        return source;
    }
}
