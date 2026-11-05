using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Watches for top-level window create/destroy/show/hide/title-change/foreground events
/// via two SetWinEventHook ranges and raises a single Changed event, letting the caller
/// re-enumerate rather than trying to track each change incrementally (simpler, and cheap
/// enough given typical open-window counts).</summary>
public sealed class WindowEventWatcher : IDisposable
{
    private const long CHILDID_SELF = 0;

    // Keep delegates alive for the hook's lifetime — otherwise the GC can collect them
    // while Windows still holds native pointers to them.
    private readonly WinEventProc _proc;
    private HWINEVENTHOOK _hookA = HWINEVENTHOOK.NULL;
    private HWINEVENTHOOK _hookB = HWINEVENTHOOK.NULL;

    public event Action? Changed;

    public WindowEventWatcher()
    {
        _proc = WinEventCallback;
    }

    public void Start()
    {
        if (!_hookA.IsNull)
        {
            return;
        }

        const WINEVENT flags = WINEVENT.WINEVENT_OUTOFCONTEXT | WINEVENT.WINEVENT_SKIPOWNPROCESS;

        _hookA = SetWinEventHook(
            EventConstant.EVENT_SYSTEM_FOREGROUND, EventConstant.EVENT_SYSTEM_MINIMIZEEND,
            HINSTANCE.NULL, _proc, 0, 0, flags);

        _hookB = SetWinEventHook(
            EventConstant.EVENT_OBJECT_CREATE, EventConstant.EVENT_OBJECT_NAMECHANGE,
            HINSTANCE.NULL, _proc, 0, 0, flags);
    }

    public void Stop()
    {
        if (!_hookA.IsNull)
        {
            UnhookWinEvent(_hookA);
            _hookA = HWINEVENTHOOK.NULL;
        }

        if (!_hookB.IsNull)
        {
            UnhookWinEvent(_hookB);
            _hookB = HWINEVENTHOOK.NULL;
        }
    }

    private void WinEventCallback(HWINEVENTHOOK hWinEventHook, EventConstant winEvent, HWND hwnd,
        ObjectIdentifier idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (idObject != ObjectIdentifier.OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        Changed?.Invoke();
    }

    public void Dispose() => Stop();
}
