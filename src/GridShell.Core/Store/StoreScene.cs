using System.Text.Json.Serialization;

namespace GridShell.Core.Store;

/// <summary>A Store "Scene" - a self-contained animated wallpaper (plain HTML/CSS/JS, no
/// external network resources) synced the same way the app catalog is (StoreCatalogSync's
/// cloned repo, store-catalog/scenes/{EntryFile}). Applying one just points AppSettings.
/// WallpaperUrl at its local synced file path - the same mechanism Settings' own "Appearance"
/// wallpaper picker already uses for a custom .html wallpaper, so WallpaperWindow needs no
/// changes to render it.</summary>
public sealed class StoreScene
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Filename under store-catalog/scenes/ in the synced clone (e.g. "aurora-waves.html").</summary>
    [JsonPropertyName("entryFile")]
    public string EntryFile { get; set; } = "";
}

public sealed class StoreSceneCatalogFile
{
    [JsonPropertyName("scenes")]
    public List<StoreScene> Scenes { get; set; } = new();
}
