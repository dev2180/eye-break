using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EyeBreak;

/// <summary>
/// While a break runs, swallows only the keys that would pull something over the overlay or close an app:
/// the Windows key (Start menu), Alt+Tab, Alt+Esc, Ctrl+Esc and Alt+F4. Everything else, including normal
/// typing, still reaches the app you're working in.
///
/// The hook lives on its own thread with its own message loop. Windows silently skips a keyboard hook that
/// answers too slowly, and the UI thread is busy animating, so sharing it let Alt+Tab slip through.
/// Ctrl+Alt+Del can't be blocked by any app, which keeps Task Manager available as a safety net.
/// </summary>
public static class KeyBlocker
{
    const int WH_KEYBOARD_LL = 13, WM_QUIT = 0x12;
    const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_F4 = 0x73, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_CONTROL = 0x11;
    const int LLKHF_ALTDOWN = 0x20;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    static readonly HookProc Proc = Callback; // kept alive for the lifetime of the hook
    static IntPtr _hook;

    /// <summary>Set during infinite mode: Esc is caught (from any app) and ends the session.</summary>
    public static volatile Action? OnEscape;
    /// <summary>Set during every break: Num Lock on + numpad 8 stops the break at once.</summary>
    public static volatile Action? OnAbort;
    const int VK_NUMPAD8 = 0x68;   // only sent while Num Lock is on
    const int WM_KEYDOWN = 0x100;
    static Thread? _thread;
    static uint _threadId;

    public static void Start()
    {
        if (_thread != null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            using (var module = Process.GetCurrentProcess().MainModule!)
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(module.ModuleName), 0);
            ready.Set();
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }) { IsBackground = true, Name = "EyeBreak key blocker" };
        _thread.Start();
        ready.Wait(2000);
    }

    public static void Stop()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }

    static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            int flags = Marshal.ReadInt32(lParam, 8);
            bool alt = (flags & LLKHF_ALTDOWN) != 0;
            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

            if (vk == VK_ESCAPE && !alt && !ctrl && OnEscape is { } esc)
            {
                if ((int)wParam == WM_KEYDOWN) ThreadPool.QueueUserWorkItem(_ => esc());
                return 1; // swallow, so Esc doesn't also close something in the app underneath
            }

            if (vk == VK_NUMPAD8 && OnAbort is { } abort)
            {
                if ((int)wParam == WM_KEYDOWN) ThreadPool.QueueUserWorkItem(_ => abort());
                return 1;
            }

            if (vk is VK_LWIN or VK_RWIN
                || (alt && vk is VK_TAB or VK_ESCAPE or VK_F4)
                || (ctrl && vk == VK_ESCAPE))
                return 1; // swallow
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
}
