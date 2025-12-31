using System.Runtime.InteropServices;

namespace ClipboardGuardian.Win.Hooks;

internal static class NativeMethods
{
    public delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern nint GetModuleHandle(nint lpModuleName);

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern nint GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint GetClipboardOwner();

    [DllImport("user32.dll")]
    public static extern nint GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    private const int InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkVKey = 0x56;

    public static void SendCtrlV()
    {
        var inputs = new[]
        {
            Input.KeyDown(VkControl),
            Input.KeyDown(VkVKey),
            Input.KeyUp(VkVKey),
            Input.KeyUp(VkControl),
        };

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int type;
        public InputUnion u;

        public static Input KeyDown(ushort vk) => new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = nint.Zero
                }
            }
        };

        public static Input KeyUp(ushort vk) => new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = KeyeventfKeyup,
                    time = 0,
                    dwExtraInfo = nint.Zero
                }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CwpStruct
{
    public nint lParam;
    public nint wParam;
    public uint message;
    public nint hwnd;
    public nint result;
}
