using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GridShell.Core.Tiles;

public class Tile : INotifyPropertyChanged
{
    private string _name = "";
    public required string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public required string TargetPath { get; init; }
    public required TileKind Kind { get; init; }

    /// <summary>True for fixed built-in website shortcuts, which are shown in the Web tab but
    /// are not user-managed or removable tiles.</summary>
    public bool IsBuiltInWebsite { get; init; }

    /// <summary>Lets XAML show management actions only for user-added Website tiles.</summary>
    public bool IsWebsiteKind => Kind == TileKind.Website && !IsBuiltInWebsite;

    /// <summary>Path whose shell icon represents this tile (the shortcut file itself for App tiles, so icon overrides are honored).</summary>
    public required string IconSourcePath { get; init; }

    /// <summary>For Website tiles: the Chromium browser executable to launch with --app=. Null for other kinds.</summary>
    public string? BrowserExecutablePath { get; init; }

    /// <summary>For Game tiles: command-line arguments to launch TargetPath with (e.g. an
    /// emulator's core + ROM path). Null for other kinds, or for a game that's a bare .exe.</summary>
    public string? Arguments { get; init; }

    /// <summary>For emulated Game tiles: the EmulationPlatform slug it was added under (e.g.
    /// "snes"). Null for a native (bare .exe) game or any other kind - grouped under
    /// PlatformGroupName's "PC" fallback.</summary>
    public string? Platform { get; init; }

    /// <summary>Display-friendly grouping key for the Games grid - the platform's real display
    /// name ("Super Nintendo (SNES)") when known, "PC" for a native game, or the raw slug
    /// itself as a last resort if a platform was since removed from EmulationPlatforms.All.</summary>
    public string PlatformGroupName =>
        Platform is null
            ? "PC"
            : EmulationPlatforms.All.FirstOrDefault(p => p.Slug == Platform)?.DisplayName ?? Platform;

    /// <summary>User-chosen image file that overrides the auto-extracted/fetched icon, if set.</summary>
    public string? CustomIconPath { get; set; }

    /// <summary>Whether the desktop wallpaper's video should pause while this tile's process
    /// has foreground focus - opt-in per tile (defaults true for new Game tiles in
    /// AddTileWindow, false otherwise) rather than a blanket rule, since not every game needs
    /// it (e.g. a windowed indie game).</summary>
    public bool PauseWallpaperWhileRunning { get; init; }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
