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
}

public sealed class StoreCatalogFile
{
    [JsonPropertyName("apps")]
    public List<StoreApp> Apps { get; set; } = new();
}
