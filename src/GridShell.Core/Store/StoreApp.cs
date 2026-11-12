using System.Text.Json.Serialization;

namespace GridShell.Core.Store;

public sealed class StoreApp
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("wingetId")]
    public string WingetId { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>EmulationPlatform slugs this app should be wired to in Settings'
    /// EmulatorConfigs once installed - empty for anything that isn't an emulator, or an
    /// emulator (like RetroArch) that needs a manual per-core setup step it can't do for you.</summary>
    [JsonPropertyName("platformSlugs")]
    public List<string> PlatformSlugs { get; set; } = new();
}

public sealed class StoreCatalogFile
{
    [JsonPropertyName("apps")]
    public List<StoreApp> Apps { get; set; } = new();
}
