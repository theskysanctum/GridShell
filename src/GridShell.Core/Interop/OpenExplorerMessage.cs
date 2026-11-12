using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Posted to GridShell.Grid's pre-warmed instance by a second invocation started with
/// "--explorer &lt;path&gt;" (e.g. from the default-file-manager registry override) - the actual
/// path travels via <see cref="ExplorerHandoff"/> since a window message alone can't carry a
/// string across processes.</summary>
public static class OpenExplorerMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellOpenExplorer");
}
