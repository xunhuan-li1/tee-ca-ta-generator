namespace GpStudio;

public sealed record PropertyPreset(string Set, string Name, string Type);

public static class PropertyPresets
{
    public static readonly PropertyPreset[] All = [
        new("current_ta", "gpd.ta.appID", "uuid"),
        new("current_ta", "gpd.ta.singleInstance", "bool"),
        new("current_ta", "gpd.ta.multiSession", "bool"),
        new("current_ta", "gpd.ta.instanceKeepAlive", "bool"),
        new("current_ta", "gpd.ta.dataSize", "u32"),
        new("current_ta", "gpd.ta.stackSize", "u32"),
        new("current_ta", "gpd.ta.version", "string"),
        new("current_ta", "gpd.ta.description", "string"),
        new("current_ta", "gpd.ta.endian", "u32"),
        new("current_ta", "gpd.ta.doesNotCloseHandleOnCorruptObject", "bool"),
        new("current_client", "gpd.client.identity", "identity"),
        new("current_client", "gpd.client.endian", "u32"),
        new("implementation", "gpd.tee.internalCore.version", "u32"),
        new("implementation", "gpd.tee.description", "string"),
        new("implementation", "gpd.tee.deviceID", "uuid"),
        new("implementation", "gpd.tee.systemTime.protectionLevel", "u32"),
        new("implementation", "gpd.tee.TAPersistentTime.protectionLevel", "u32"),
        new("implementation", "gpd.tee.arith.maxBigIntSize", "u32"),
        new("implementation", "gpd.tee.cryptography.nist", "bool"),
        new("implementation", "gpd.tee.cryptography.bsi-r", "bool"),
        new("implementation", "gpd.tee.cryptography.bsi-t", "bool"),
        new("implementation", "gpd.tee.cryptography.ietf", "bool"),
        new("implementation", "gpd.tee.cryptography.octa", "bool"),
        new("implementation", "gpd.tee.trustedStorage.private.rollbackProtection", "u32"),
        new("implementation", "gpd.tee.trustedStorage.perso.rollbackProtection", "u32"),
        new("implementation", "gpd.tee.trustedStorage.protected.rollbackProtection", "u32"),
        new("implementation", "gpd.tee.trustedos.implementation.version", "string"),
        new("implementation", "gpd.tee.trustedos.implementation.binaryversion", "binary"),
        new("implementation", "gpd.tee.trustedos.manufacturer", "string"),
        new("implementation", "gpd.tee.firmware.implementation.version", "string"),
        new("implementation", "gpd.tee.firmware.implementation.binaryversion", "binary"),
        new("implementation", "gpd.tee.firmware.manufacturer", "string")
    ];
}
