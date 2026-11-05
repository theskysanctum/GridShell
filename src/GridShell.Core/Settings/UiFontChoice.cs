namespace GridShell.Core.Settings;

/// <summary>Default = Roboto, Gaming = Exo 2 - names kept as-is (not renamed to match their
/// fonts), and new members always appended at the end, never inserted - this serializes as a
/// plain integer ordinal (no JsonStringEnumConverter configured), so changing any existing
/// member's position would silently reinterpret already-saved settings.json files as a
/// different font.</summary>
public enum UiFontChoice
{
    Default,
    Gaming,
    Custom,
    Rajdhani,
    Orbitron,
}
