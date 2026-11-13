using System.Text.Json.Serialization;

namespace GridShell.Core.Store;

/// <summary>A Store "Scene" - a self-contained animated wallpaper (plain HTML/CSS/JS, no
/// external network resources) fetched directly over HTTP (see StoreSceneSync), not through
/// StoreCatalogSync's full repo clone - a genuinely separate, on-demand download per scene.
/// Applying one downloads its entry file (if not already cached) and points AppSettings.
/// WallpaperUrl at the local cached path - the same mechanism Settings' own "Appearance"
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

    /// <summary>Filename under store-catalog/scenes/ in the repo (e.g. "aurora-waves.html") -
    /// resolved against StoreSceneSync's own raw-content base URL, not a local path.</summary>
    [JsonPropertyName("entryFile")]
    public string EntryFile { get; set; } = "";
}

public sealed class StoreSceneCatalogFile
{
    [JsonPropertyName("scenes")]
    public List<StoreScene> Scenes { get; set; } = new();
}
