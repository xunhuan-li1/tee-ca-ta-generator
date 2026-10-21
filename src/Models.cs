using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GpStudio;

public sealed class Project
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "demo";
    public string Description { get; set; } = "GP portable TA examples";
    [JsonConverter(typeof(TaVersionConverter))]
    public string Version { get; set; } = "1.0.0";
    public string Uuid { get; set; } = Guid.NewGuid().ToString();
    public string CaOutput { get; set; } = "executable";
    public bool GenerateCa { get; set; } = true;
    public string Login { get; set; } = "TEEC_LOGIN_PUBLIC";
    public long GroupId { get; set; }
    public bool SessionContext { get; set; }
    public bool SingleInstance { get; set; }
    public bool MultiSession { get; set; }
    public bool KeepAlive { get; set; }
    public int StackKiB { get; set; } = 16;
    public int HeapKiB { get; set; } = 256;
    public List<Feature> Features { get; set; } = [new() { Kind = "random" }, new() { Kind = "time" }];
}

// Read integer versions from older project files; always save a string.
public sealed class TaVersionConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString()!;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out uint version))
            return version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        throw new JsonException("Version must be a string (for example, 1.0.1) or a legacy uint32 integer.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

public sealed class Feature
{
    public string Scenario { get; set; } = "";
    public string Kind { get; set; } = "digest";
    public string Algorithm { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.Ordinal);
}

public sealed record Algorithm(string Id, string Value, string Family, string Digest,
    string KeyType, int KeyBits, int DigestBytes, int BlockSize, bool Legacy);
public sealed record Api(string Name, string Group, string Prototype, bool Optional, bool Deprecated);
public sealed record Curve(string Id, string Value, int Bits, bool Generic);
public sealed record Option(string Key, string Label, string English, string Default,
    string[]? Choices = null, string Type = "text", string When = "");
public sealed record Scenario(string Id, string Title, string English, string Category,
    string Description, string DescriptionEn, Option[] Fields);
public sealed class Catalog
{
    public List<Algorithm> Algorithms { get; set; } = [];
    public List<Api> Apis { get; set; } = [];
    public List<Curve> Curves { get; set; } = [];
    public Dictionary<string, string> Sources { get; set; } = [];
}

public static class Data
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static string Asset(string name)
    {
        Assembly assembly = typeof(Data).Assembly;
        string suffix = ".Assets." + name.Replace('/', '.');
        string key = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(key)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
    public static Catalog Catalog { get; } = JsonSerializer.Deserialize<Catalog>(Asset("catalog.json"), Json)!;
    public static Project Read(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        void Duplicates(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new ArgumentException("Duplicate property: " + property.Name);
                    Duplicates(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in element.EnumerateArray()) Duplicates(item);
        }
        Duplicates(document.RootElement);
        foreach (string field in new[] { "FormatVersion", "Name", "Uuid", "Features" })
            if (!document.RootElement.TryGetProperty(field, out _)) throw new ArgumentException("Missing field: " + field);
        Project project = document.RootElement.Deserialize<Project>(Json) ?? throw new ArgumentException("Project is required.");
        // Older configurations used zero as an algorithm-default marker.
        if (project.Features != null) {
            foreach (Feature? feature in project.Features) {
                if (feature?.Options != null &&
                    Scenarios.All.Any(scenario => scenario.Id == feature.Kind && scenario.Category == "Crypto") &&
                    feature.Options.TryGetValue("keyBits", out string? bits) && bits == "0")
                    feature.Options.Remove("keyBits");
            }
        }
        return project;
    }
    public static void Save(string path, object value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, Json) + "\n", new System.Text.UTF8Encoding(false));
}
