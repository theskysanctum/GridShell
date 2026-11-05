using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Posted to GridShell.Grid by a would-be second instance of itself, right before that
/// second instance exits — see the single-instance guard in GridShell.Grid's App.xaml.cs.
/// Unconditionally shows the grid (unlike <see cref="GridToggleMessage"/>, which could instead
/// hide it if already open), matching the "--show" cold-start intent the second instance was
/// actually launched with.</summary>
public static class ShowGridMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellShowGrid");
}
