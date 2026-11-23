using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TruckSidecar;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT r, int size);

    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);

    // ── AppBar: จองแถบขวาของจอ เพื่อให้หน้าต่างที่ขยายเต็มจอหดหลบ sidecar เอง ──
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABE_RIGHT = 2;

    /// <summary>ลงทะเบียนเป็น AppBar ชิดขวาของจอ คืนค่าพื้นที่ที่ Windows อนุญาต</summary>
    public static Rectangle AppBarDockRight(IntPtr hWnd, Rectangle screen, int width, bool alreadyRegistered)
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hWnd, uCallbackMessage = 0x8000 + 77 };
        if (!alreadyRegistered) SHAppBarMessage(ABM_NEW, ref d);
        d.uEdge = ABE_RIGHT;
        d.rc = new RECT { Left = screen.Right - width, Top = screen.Top, Right = screen.Right, Bottom = screen.Bottom };
        SHAppBarMessage(ABM_QUERYPOS, ref d);
        d.rc.Left = d.rc.Right - width;
        SHAppBarMessage(ABM_SETPOS, ref d);
        return Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
    }

    public static void AppBarRemove(IntPtr hWnd)
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hWnd };
        SHAppBarMessage(ABM_REMOVE, ref d);
    }

    // ── จอภาพ (แทน WinForms Screen) ──
    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

    /// <summary>(ขอบเขตจอ, พื้นที่ทำงาน) ของจอที่ rect อยู่มากที่สุด — หน่วยพิกเซล</summary>
    public static (Rectangle Bounds, Rectangle WorkArea) MonitorOf(Rectangle rect)
    {
        var r = new RECT { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromRect(ref r, 2 /* MONITOR_DEFAULTTONEAREST */), ref mi);
        static Rectangle R(RECT x) => Rectangle.FromLTRB(x.Left, x.Top, x.Right, x.Bottom);
        return (R(mi.rcMonitor), R(mi.rcWork));
    }

    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    public const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_TOPMOST = 0x8;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;
    public static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);

    public const uint GW_HWNDPREV = 3;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;

    /// <summary>
    /// หน้าต่างหลักของโปรแกรมเป้าหมาย = หน้าต่างที่มองเห็นและใหญ่ที่สุดของ process นั้น
    /// (VB6 มีหน้าต่าง ThunderRT6Main ซ่อนอยู่เป็น owner จึงไม่กรองด้วย owner)
    /// </summary>
    public static IntPtr FindMainWindow(string processName, IntPtr exclude)
    {
        var pids = Process.GetProcessesByName(processName).Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return IntPtr.Zero;

        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((h, _) =>
        {
            if (h == exclude || !IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid)) return true;
            GetWindowRect(h, out var r);
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if ((r.Right - r.Left) >= 200 && (r.Bottom - r.Top) >= 150 && area > bestArea)
            {
                best = h;
                bestArea = area;
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    // ── ดึง sidecar ขึ้นมา active เมื่อรถชั่งเข้า/ออก ──
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO li);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder s, int n);

    /// <summary>"0x1234 cmd#5678 ConsoleWindowClass" — ใช้ใน log</summary>
    public static string Describe(IntPtr h)
    {
        if (h == IntPtr.Zero) return "(none)";
        GetWindowThreadProcessId(h, out var pid);
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { name = "?"; }
        var sb = new System.Text.StringBuilder(128);
        GetClassName(h, sb, sb.Capacity);
        return $"0x{h.ToInt64():X} {name}#{pid} {sb}";
    }

    /// <summary>มิลลิวินาทีนับจากที่ผู้ใช้กดแป้น/ขยับเมาส์ครั้งล่าสุด (ทั้งเครื่อง)</summary>
    public static uint IdleMs()
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref li) ? unchecked((uint)Environment.TickCount - li.dwTime) : 0;
    }

    /// <summary>
    /// ทำให้ hWnd เป็นหน้าต่าง active แม้โปรแกรมเราอยู่เบื้องหลัง
    /// Windows กันโปรแกรมเบื้องหลังแย่ง foreground (SetForegroundWindow เฉยๆ จะแค่กะพริบ) จึงผูกคิว input
    /// เข้ากับ thread ของหน้าต่างที่ active อยู่ชั่วคราว — ไม่ส่งแป้น/คลิกใดๆ เข้าโปรแกรมนั้น
    /// </summary>
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);
    [DllImport("user32.dll", EntryPoint = "AttachThreadInput", SetLastError = true)] static extern bool AttachThreadInputE(uint idAttach, uint idAttachTo, bool attach);

    // ── สิทธิ์ขึ้นหน้าผ่าน hotkey ──
    // Windows ยอมให้โปรเซสที่ "ได้รับ input ล่าสุด" ขึ้นหน้าได้ — hotkey ที่ลงทะเบียนไว้ถูกส่งตรงถึงหน้าต่างเจ้าของ
    // ไม่ผ่านหน้าต่างอื่น จึงใช้ F24 (ไม่มีบนคีย์บอร์ดจริง) เป็นตัวกระตุ้นโดยไม่ส่งแป้นใดเข้าโปรแกรมชั่ง
    // keydown → ถูกกินเป็น hotkey; keyup ส่งหลังเราขึ้นหน้าแล้ว (ตกที่ sidecar เอง)
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public const uint VK_F24 = 0x87, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public static void F24Down() => keybd_event((byte)VK_F24, 0, 0, UIntPtr.Zero);
    public static void F24Up() => keybd_event((byte)VK_F24, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
    public static bool SetForeground(IntPtr hWnd) => SetForegroundWindow(hWnd);

    public static bool ForceForeground(IntPtr hWnd) => ForceForeground(hWnd, out _);

    public static bool ForceForeground(IntPtr hWnd, out string diag)
    {
        var steps = new List<string>();
        var fg = GetForegroundWindow();
        diag = "";
        if (fg == hWnd) return true;
        uint me = GetCurrentThreadId();
        uint other = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        bool attached = other != 0 && other != me && AttachThreadInputE(me, other, true);
        steps.Add(attached ? $"attach→{other} ok" : $"attach→{other} FAIL err={Marshal.GetLastWin32Error()}");
        try
        {
            BringWindowToTop(hWnd);
            bool sf = SetForegroundWindow(hWnd);
            steps.Add($"SetForegroundWindow={sf}");
        }
        finally
        {
            if (attached) AttachThreadInput(me, other, false);
        }
        bool ok = GetForegroundWindow() == hWnd;
        if (!ok)
        {
            SwitchToThisWindow(hWnd, true);
            ok = GetForegroundWindow() == hWnd;
            steps.Add($"SwitchToThisWindow→{ok}");
        }
        diag = string.Join(", ", steps);
        return ok;
    }

    /// <summary>ขอบเขตที่มองเห็นจริง (ไม่รวมขอบโปร่งใสของ Windows 10/11)</summary>
    public static Rectangle VisibleBounds(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var r, Marshal.SizeOf<RECT>()) != 0)
            GetWindowRect(hWnd, out r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }
}
