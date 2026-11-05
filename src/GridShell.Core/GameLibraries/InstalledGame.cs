namespace GridShell.Core.GameLibraries;

/// <summary>One game found already installed via a third-party launcher - TargetPath/Arguments
/// are launched the same way TileLauncher launches anything else (Process.Start with
/// UseShellExecute, a protocol URL for Source and Epic, a real exe for GOG).</summary>
public sealed record InstalledGame(string Source, string Name, string TargetPath, string Arguments);
