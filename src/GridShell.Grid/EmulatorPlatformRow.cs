using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>One row in Settings' Emulation tab — a platform, the emulator configured to run it
/// (if any), and how many ROMs were found for it under C:\Games\ROMs\&lt;slug&gt;. RetroArch gets
/// special handling: rather than hand-typing a "-L &lt;core&gt;" argument, its cores folder (a
/// "cores" directory next to the exe, RetroArch's own convention) is scanned so one can just be
/// picked from a dropdown.</summary>
public class EmulatorPlatformRow : INotifyPropertyChanged
{
    public required EmulationPlatform Platform { get; init; }
    public required Action OnChanged { get; init; }

    public string DisplayName => Platform.DisplayName;

    public string Category => Platform.Category;

    public IReadOnlyList<EmulatorDownloadLink> DownloadLinks => EmulatorDownloadCatalog.GetLinksFor(Platform.Slug);

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
            RefreshCoreList();
            TrySelectCoreFromArgumentTemplate();
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
        // Verified against each emulator's own docs/README/source. Most of these install through
        // the Store (store-catalog/catalog.json) now rather than EmulatorDownloadCatalog, which
        // only still covers the handful with no winget package (see its own remarks).
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
            TrySelectCoreFromArgumentTemplate();
            RaiseChanged();
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(EmulatorPath);

    public bool IsRetroArch => !string.IsNullOrWhiteSpace(EmulatorPath)
        && string.Equals(Path.GetFileNameWithoutExtension(EmulatorPath), "retroarch", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<string> AvailableCores { get; } = new();

    private string? _selectedCore;
    public string? SelectedCore
    {
        get => _selectedCore;
        set
        {
            if (_selectedCore == value) return;
            _selectedCore = value;
            OnPropertyChanged();

            if (value is not null)
            {
                var coresDir = Path.Combine(Path.GetDirectoryName(EmulatorPath) ?? "", "cores");
                var corePath = Path.Combine(coresDir, value);
                ArgumentTemplate = $"-f -L \"{corePath}\" \"{{rom}}\"";
            }
        }
    }

    /// <summary>Rescans the cores folder (also called whenever EmulatorPath changes) — exposed
    /// publicly so the Settings UI can offer a manual rescan after installing new cores without
    /// having to re-browse to the exe.</summary>
    public void RefreshCoreList()
    {
        AvailableCores.Clear();
        if (!IsRetroArch)
        {
            return;
        }

        var dir = Path.GetDirectoryName(EmulatorPath);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        var coresDir = Path.Combine(dir, "cores");
        if (!Directory.Exists(coresDir))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(coresDir, "*_libretro.dll")
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                AvailableCores.Add(Path.GetFileName(file));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
        }

        TrySelectCoreFromArgumentTemplate();
    }

    /// <summary>Pre-selects whichever core the current ArgumentTemplate already references
    /// (e.g. on load) without rewriting it through SelectedCore's own setter, which would
    /// otherwise needlessly re-save an unchanged value.</summary>
    private void TrySelectCoreFromArgumentTemplate()
    {
        if (!IsRetroArch || AvailableCores.Count == 0)
        {
            return;
        }

        var match = Regex.Match(_argumentTemplate, "-L\\s+\"([^\"]+)\"");
        if (!match.Success)
        {
            return;
        }

        var fileName = Path.GetFileName(match.Groups[1].Value);
        if (_selectedCore != fileName && AvailableCores.Contains(fileName))
        {
            _selectedCore = fileName;
            OnPropertyChanged(nameof(SelectedCore));
        }
    }

    public void RefreshRomCount(IEnumerable<string>? extraRoots = null)
    {
        RomCount = RomScanner.GetRoms(Platform, extraRoots).Count;
        OnPropertyChanged(nameof(RomCount));
        OnPropertyChanged(nameof(RomCountText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
