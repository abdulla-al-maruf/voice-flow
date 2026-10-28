using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed class Hotkeys : IMessageFilter, IDisposable
{
    internal event Action<int> Pressed;
    internal IntPtr Handle { get { return IntPtr.Zero; } }
    internal Hotkeys() { Application.AddMessageFilter(this); }
    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg != 0x0312) return false;
        if (Pressed != null) Pressed(message.WParam.ToInt32());
        return true;
    }
    public void Dispose() { Application.RemoveMessageFilter(this); }
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO
    { public int cbSize; public uint flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public INPUTUNION data; }
    [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION
    { [FieldOffset(0)] public KEYBDINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    internal static INPUT Key(ushort key, bool up) { return new INPUT { type = 1, data = new INPUTUNION { keyboard = new KEYBDINPUT { wVk = key, dwFlags = up ? 2u : 0u } } }; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    internal static bool OriginalVoiceBusy()
    {
        if (Edition.Public) return false; // the old prototype app is not part of the public edition
        bool busy = false;
        EnumWindows(delegate(IntPtr window, IntPtr unused)
        {
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            string value = title.ToString();
            if (value == "AntigravityVoice.State:recording" || value == "AntigravityVoice.State:finishing") busy = true;
            return !busy;
        }, IntPtr.Zero);
        return busy;
    }
}
