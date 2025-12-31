using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipboardGuardian.Win.Hooks;

internal sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int VkInsert = 0x2D;
    private const int VkV = 0x56;

    private nint _hookId = nint.Zero;
    private NativeMethods.HookProc? _proc;
    private volatile int _suppressNextPaste;

    public Func<bool>? IsProtectionEnabled { get; set; }

    public event Action? PasteRequested;

    public void Start()
    {
        _proc = HookCallback;
        _hookId = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _proc, NativeMethods.GetModuleHandle(nint.Zero), 0);
    }

    public void Dispose()
    {
        if (_hookId != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = nint.Zero;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && wParam == (nint)WmKeydown)
        {
            var hookStruct = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var isCtrlPressed = (NativeMethods.GetKeyState((int)Keys.LControlKey) & 0x8000) != 0 ||
                                (NativeMethods.GetKeyState((int)Keys.RControlKey) & 0x8000) != 0;
            var isShiftPressed = (NativeMethods.GetKeyState((int)Keys.LShiftKey) & 0x8000) != 0 ||
                                 (NativeMethods.GetKeyState((int)Keys.RShiftKey) & 0x8000) != 0;

            var isPasteHotkey = (hookStruct.vkCode == VkV && isCtrlPressed) ||
                                (hookStruct.vkCode == VkInsert && isShiftPressed);

            if (isPasteHotkey)
            {
                if (_suppressNextPaste > 0)
                {
                    _suppressNextPaste = 0;
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (IsProtectionEnabled?.Invoke() == true)
                {
                    PasteRequested?.Invoke();
                    return (nint)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void SimulatePaste()
    {
        try
        {
            _suppressNextPaste = 1;
            NativeMethods.SendCtrlV();
        }
        catch
        {
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct KbdLlHookStruct
{
    public int vkCode;
    public int scanCode;
    public int flags;
    public int time;
    public nint dwExtraInfo;
}
