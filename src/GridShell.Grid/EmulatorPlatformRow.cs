using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>One row in Game Manager's platform list - a platform, the emulator configured to run
/// it (if any, read-only here - EmulatorAutoWire/RetroArchCoreAutoWire set it once a matching
/// Store app is installed, there's no manual picker), and how many ROMs were found for it under
/// C:\Games\ROMs\&lt;slug&gt;.</summary>
public class EmulatorPlatformRow : INotifyPropertyChanged
{
    public required EmulationPlatform Platform { get; init; }
    public required Action OnChanged { get; init; }

    public string DisplayName => Platform.DisplayName;

    public string Category => Platform.Category;

    public int RomCount { get; set; }

    public string RomCountText => RomCount switch
    {
        0 => "No ROMs found",
        1 => "1 ROM found",
        _ => $"{RomCount} ROMs found",
    };

    private const string GenericDefaultArgumentTemplate = "\"{rom}\"";

    /// <summary>Set by LoadEmulatorPlatforms while hydrating a row from its already-saved
    /// config, so setting EmulatorPath/ArgumentTemplate back to the exact value they were just
    /// read from doesn't immediately re-save the same data right back - across 187 platforms
    /// that's up to a dozen needless writes on every Settings open, which was enough to
    /// occasionally collide with the taskbar process also touching settings.json at the same
    /// moment.</summary>
    public bool SuppressOnChanged { get; set; }

    private void RaiseChanged()
    {
        if (!SuppressOnChanged)
        {
            OnChanged();
        }
    }

    private string _emulatorPath = "";
    public string EmulatorPath
    {
        get => _emulatorPath;
        set
        {
            if (_emulatorPath == value) return;
            _emulatorPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConfigured));
            OnPropertyChanged(nameof(IsRetroArch));
            OnPropertyChanged(nameof(StatusSummary));
            ApplyKnownEmulatorDefaultsIfUnmodified();
            RaiseChanged();
        }
    }

    /// <summary>Recognized emulators get a launch template that starts them fullscreen - each
    /// flag verified against that emulator's own docs/README/source (not guessed; see the
    /// comment on each entry). Only applied when the template is still the untouched generic
    /// default, so a deliberate customization (including RetroArch's own core-driven template)
    /// is never clobbered. Matched by substring rather than exact filename since real-world
    /// release builds vary (e.g. "PPSSPPWindows64.exe", "Cemu_1.2.3.exe") - first match wins.</summary>
    private static readonly (string ExeNameContains, string ArgumentTemplate)[] KnownEmulatorDefaults =
    {
        // Verified against each emulator's own docs/README/source. All of these install through
        // the Store (store-catalog/catalog.json) now - EmulatorAutoWire (and
        // RetroArchCoreAutoWire for RetroArch) sets EmulatorPath automatically on install.
        ("dolphin", "-b -e \"{rom}\" -C Dolphin.Display.Fullscreen=True"),
        ("pcsx2", "-fullscreen \"{rom}\""),
        ("rpcs3", "--no-gui --fullscreen \"{rom}\""),
        ("shadps4", "--fullscreen true \"{rom}\""),
        ("cemu", "-f -g \"{rom}\""),
        ("vita3k", "--fullscreen \"{rom}\""),
        ("xemu", "-full-screen -dvd_path \"{rom}\""),
        ("xenia", "--fullscreen=true \"{rom}\""),
        ("ruffle", "--fullscreen --scale=show-all --force-scale \"{rom}\""),
        ("azahar", "-f \"{rom}\""),
        ("melonds", "-f \"{rom}\""),
        ("ppsspp", "--fullscreen \"{rom}\""),
        ("mame", "-nowindow \"{rom}\""),
        ("supermodel", "\"{rom}\" -fullscreen"),

        // Fullscreen by default already (confirmed - there's no "turn fullscreen on" flag,
        // only an undocumented/reportedly-broken one to turn it off) - -Play is still needed
        // so it launches the table directly instead of opening the editor.
        ("vpinballx", "-Play \"{rom}\""),

        // Mini vMac has no CLI fullscreen option at all (only a runtime Ctrl+F toggle) -
        // deliberately left out rather than inventing a flag that doesn't exist.
    };

    private void ApplyKnownEmulatorDefaultsIfUnmodified()
    {
        if (_argumentTemplate != GenericDefaultArgumentTemplate)
        {
            return;
        }

        var exeName = Path.GetFileNameWithoutExtension(EmulatorPath);
        foreach (var (contains, template) in KnownEmulatorDefaults)
        {
            if (exeName.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                ArgumentTemplate = template;
                return;
            }
        }
    }

    private string _argumentTemplate = "\"{rom}\"";
    public string ArgumentTemplate
    {
        get => _argumentTemplate;
        set
        {
            if (_argumentTemplate == value) return;
            _argumentTemplate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusSummary));
            RaiseChanged();
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(EmulatorPath);

    public bool IsRetroArch => !string.IsNullOrWhiteSpace(EmulatorPath)
        && string.Equals(Path.GetFileNameWithoutExtension(EmulatorPath), "retroarch", StringComparison.OrdinalIgnoreCase);

    /// <summary>Read-only summary for Game Manager's list - there's no manual picker anymore
    /// (everything here is set by EmulatorAutoWire/RetroArchCoreAutoWire once the matching Store
    /// app is installed), so this is the only thing shown per row besides the ROM count.</summary>
    public string StatusSummary
    {
        get
        {
            if (!IsConfigured)
            {
                return "Not configured - install a matching emulator from the Store.";
            }

            var exeName = Path.GetFileName(EmulatorPath);
            if (!IsRetroArch)
            {
                return exeName;
            }

            var match = Regex.Match(_argumentTemplate, "-L\\s+\"([^\"]+)\"");
            if (!match.Success)
            {
                return exeName;
            }

            var coreName = Path.GetFileNameWithoutExtension(match.Groups[1].Value)
                .Replace("_libretro", "", StringComparison.OrdinalIgnoreCase);
            return $"{exeName} ({coreName})";
        }
    }

    /// <summary>This platform's own subfolder under the default ROMs root, then under each
    /// global extra ROM folder (see Game Manager's "Extra ROM folders" list) in the same order -
    /// the default root is always first/pinned. The only thing left per platform worth showing
    /// once emulator setup is fully automatic (see StatusSummary's own comment) and every
    /// folder is already auto-created (EmulationPlatforms.EnsureSlugFoldersExist) - so showing
    /// where to actually drop ROMs is the one still-useful, actionable thing here.</summary>
    public ObservableCollection<string> RomFolderPaths { get; } = new();

    public void RefreshRomCount(IEnumerable<string>? extraRoots = null)
    {
        var roots = new[] { EmulationPlatforms.RomsRootPath }.Concat(extraRoots ?? Enumerable.Empty<string>()).ToList();

        RomCount = RomScanner.GetRoms(Platform, extraRoots).Count;
        OnPropertyChanged(nameof(RomCount));
        OnPropertyChanged(nameof(RomCountText));

        RomFolderPaths.Clear();
        foreach (var root in roots)
        {
            RomFolderPaths.Add(Path.Combine(root, Platform.Slug));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
