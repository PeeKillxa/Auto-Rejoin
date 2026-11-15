using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NexusRobloxManager
{
    // =========================================================================
    // WIN32 NATIVE P/INVOKE
    // =========================================================================
    public static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        public static readonly uint ShowManagerMessage = RegisterWindowMessage("NexusRobloxManager.ShowExisting.v1");
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const int WS_OVERLAPPED = 0x00000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_BORDER = 0x00800000;
        public const int WS_THICKFRAME = 0x00040000;
        public const int WS_SYSMENU = 0x00080000;
        public const int WS_MINIMIZEBOX = 0x00020000;
        public const int WS_MAXIMIZEBOX = 0x00010000;

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_LAYERED = 0x00080000;

        public const uint LWA_ALPHA = 0x00000002;

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_MAXIMIZE = 3;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_SHOWMINNOACTIVE = 7;
        public const int SW_SHOWNA = 8;
        public const int SW_RESTORE = 9;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_NOSENDCHANGING = 0x0400;

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_ERASE = 0x0004;
        public const uint RDW_ALLCHILDREN = 0x0080;
        public const uint RDW_UPDATENOW = 0x0100;
        public const uint RDW_FRAME = 0x0400;

        public const uint GW_OWNER = 4;
        public const uint WM_SETTEXT = 0x000C;
        public const int WM_HOTKEY = 0x0312;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;

        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT rect, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static string GetTitle(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetClass(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static void SetDarkTitleBar(IntPtr hWnd)
        {
            try
            {
                int val = 1;
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int));
            }
            catch { }
        }
    }

    public class DpiScope : IDisposable
    {
        private IntPtr oldCtx = IntPtr.Zero;
        private bool active;

        public DpiScope()
        {
            try
            {
                oldCtx = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
                active = oldCtx != IntPtr.Zero;
            }
            catch { active = false; }
        }

        public void Dispose()
        {
            if (!active) return;
            try { Native.SetThreadDpiAwarenessContext(oldCtx); }
            catch { }
        }
    }

    // =========================================================================
    // ROBLOX MULTI-INSTANCE MUTEX HOLDER (Allows opening unlimited Roblox windows)
    // =========================================================================
    public static class MultiInstanceUnlocker
    {
        private static Thread worker;
        private static volatile bool running;
        public static bool IsLockedByUs { get; private set; }

        public static void Start()
        {
            if (worker != null) return;
            running = true;
            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "RobloxMultiMutex";
            worker.Start();
        }

        public static void Stop()
        {
            running = false;
        }

        private static void Loop()
        {
            Mutex mutex = null;
            EventWaitHandle ev = null;
            bool owned = false;
            try
            {
                bool created;
                ev = new EventWaitHandle(false, EventResetMode.ManualReset, "ROBLOX_singletonEvent", out created);
                mutex = new Mutex(false, "ROBLOX_singletonMutex");
                while (running)
                {
                    bool got = false;
                    try { got = mutex.WaitOne(500); }
                    catch (AbandonedMutexException) { got = true; }
                    if (got)
                    {
                        owned = true;
                        IsLockedByUs = true;
                        while (running) Thread.Sleep(300);
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }
            }
            catch { }
            finally
            {
                IsLockedByUs = false;
                try { if (owned && mutex != null) mutex.ReleaseMutex(); } catch { }
                try { if (mutex != null) mutex.Close(); } catch { }
                try { if (ev != null) ev.Close(); } catch { }
            }
        }
    }

    // =========================================================================
    // DATA MODELS & WINDOW ENGINE
    // =========================================================================
    public class MonitorItem
    {
        public string Device = "";
        public string Label = "";
        public Rectangle Bounds;
        public Rectangle WorkArea;
        public bool IsPrimary;
    }

    public class RobloxWin
    {
        public int Index;
        public IntPtr Hwnd;
        public int Pid;
        public string Title = "";
        public Rectangle Rect;
        public bool IsHidden;
        public bool IsBorderless;
        public bool IsMinimized;
        public DateTime StartTime;

        // Account & Watchdog Metadata
        public string Username = "";
        public long UserId = 0;
        public long TrackerId = 0;
        public int HeartbeatAgeSec = -1;
        public int RejoinCount = 0;
        public bool HasCookie = false;
        public string BotStatus = "LOADING";
        public int AbnormalAgeSec = 0;
        public AccountSlot AccountRef = null;
        public string SessionLogPath = "";
    }

    public class SavedWinState
    {
        public int OrigStyle;
        public int OrigExStyle;
        public Rectangle LastVisibleRect;
        public bool WasHiddenByUs;
    }

    public static class WindowEngine
    {
        public static readonly Dictionary<IntPtr, SavedWinState> States = new Dictionary<IntPtr, SavedWinState>();

        public static List<MonitorItem> GetMonitors()
        {
            List<MonitorItem> list = new List<MonitorItem>();
            using (new DpiScope())
            {
                Native.MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, IntPtr rc, IntPtr data)
                {
                    Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                    if (Native.GetMonitorInfo(hMon, ref mi))
                    {
                        MonitorItem m = new MonitorItem();
                        m.Device = mi.szDevice ?? "";
                        m.Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                        m.WorkArea = Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
                        m.IsPrimary = (mi.dwFlags & 1) != 0;
                        list.Add(m);
                    }
                    return true;
                };
                Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            }

            if (list.Count == 0)
            {
                MonitorItem m = new MonitorItem();
                m.Device = "PRIMARY";
                m.Bounds = Screen.PrimaryScreen.Bounds;
                m.WorkArea = Screen.PrimaryScreen.WorkingArea;
                m.IsPrimary = true;
                list.Add(m);
            }

            list.Sort(delegate(MonitorItem a, MonitorItem b)
            {
                if (a.IsPrimary != b.IsPrimary) return a.IsPrimary ? -1 : 1;
                return a.Bounds.Left.CompareTo(b.Bounds.Left);
            });

            for (int i = 0; i < list.Count; i++)
            {
                list[i].Label = string.Format("จอที่ {0} ({1}x{2}){3}",
                    i + 1, list[i].Bounds.Width, list[i].Bounds.Height,
                    list[i].IsPrimary ? " [จอหลัก]" : "");
            }
            return list;
        }

        public static List<RobloxWin> ScanRobloxWindows()
        {
            List<RobloxWin> result = new List<RobloxWin>();
            Process[] procs = Process.GetProcessesByName("RobloxPlayerBeta");
            if (procs == null || procs.Length == 0) return result;

            Dictionary<uint, DateTime> startTimes = new Dictionary<uint, DateTime>();
            HashSet<uint> pids = new HashSet<uint>();
            foreach (Process p in procs)
            {
                uint pid = (uint)p.Id;
                pids.Add(pid);
                DateTime st = DateTime.MaxValue;
                try { st = p.StartTime; } catch { }
                startTimes[pid] = st;
                p.Dispose();
            }

            Dictionary<uint, RobloxWin> byPid = new Dictionary<uint, RobloxWin>();

            using (new DpiScope())
            {
                Native.EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    if (!pids.Contains(pid)) return true;
                    if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;

                    string cls = Native.GetClass(h);
                    string title = Native.GetTitle(h);

                    // Skip splash screen, IME, or helper windows
                    if (cls.IndexOf("IME", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cls.IndexOf("Splash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cls.IndexOf("MSCTFIME", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    bool isRobloxMain = cls.Equals("WINDOWSCLIENT", StringComparison.OrdinalIgnoreCase) ||
                                        title.IndexOf("Roblox", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool visible = Native.IsWindowVisible(h);
                    SavedWinState saved;
                    bool knownToUs = States.TryGetValue(h, out saved);

                    // Allow finding windows even if hidden or stealth-hidden, as long as they are the main Roblox window
                    if (!visible && !isRobloxMain && !knownToUs) return true;

                    Native.RECT r;
                    if (!Native.GetWindowRect(h, out r)) return true;

                    bool iconic = Native.IsIconic(h);
                    if (!iconic && !isRobloxMain && (r.Width < 100 || r.Height < 80)) return true;

                    int exStyle = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    int style = Native.GetWindowLong(h, Native.GWL_STYLE);

                    // Detect if currently stealth-hidden (Layered + Transparent with low alpha, or SW_HIDE)
                    bool isStealthHidden = false;
                    if (!visible)
                    {
                        isStealthHidden = true;
                    }
                    else if ((exStyle & Native.WS_EX_LAYERED) != 0 && (exStyle & Native.WS_EX_TRANSPARENT) != 0)
                    {
                        uint crKey, dwFlags;
                        byte alpha;
                        if (Native.GetLayeredWindowAttributes(h, out crKey, out alpha, out dwFlags))
                        {
                            if ((dwFlags & Native.LWA_ALPHA) != 0 && alpha <= 5)
                                isStealthHidden = true;
                        }
                        else
                        {
                            isStealthHidden = true;
                        }
                    }

                    // Ensure we record original style if not yet saved
                    if (!knownToUs)
                    {
                        SavedWinState init = new SavedWinState();
                        init.OrigStyle = style | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
                        init.OrigExStyle = exStyle & ~(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW);
                        init.LastVisibleRect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                        init.WasHiddenByUs = isStealthHidden;
                        States[h] = init;
                    }

                    RobloxWin existing;
                    if (byPid.TryGetValue(pid, out existing))
                    {
                        bool existingIsClient = Native.GetClass(existing.Hwnd).Equals("WINDOWSCLIENT", StringComparison.OrdinalIgnoreCase);
                        if (existingIsClient && !cls.Equals("WINDOWSCLIENT", StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (existing.Rect.Width * existing.Rect.Height >= r.Width * r.Height && existingIsClient == isRobloxMain)
                            return true;
                    }

                    RobloxWin w = new RobloxWin();
                    w.Hwnd = h;
                    w.Pid = (int)pid;
                    w.Title = string.IsNullOrEmpty(title) ? "Roblox" : title;
                    w.Rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    w.IsHidden = isStealthHidden;
                    w.IsBorderless = (style & Native.WS_CAPTION) == 0;
                    w.IsMinimized = iconic;
                    DateTime st;
                    w.StartTime = startTimes.TryGetValue(pid, out st) ? st : DateTime.MaxValue;
                    byPid[pid] = w;
                    return true;
                }, IntPtr.Zero);
            }

            foreach (KeyValuePair<uint, RobloxWin> kv in byPid)
            {
                result.Add(kv.Value);
            }

            result.Sort(delegate(RobloxWin a, RobloxWin b)
            {
                int cmp = a.StartTime.CompareTo(b.StartTime);
                if (cmp != 0) return cmp;
                return a.Pid.CompareTo(b.Pid);
            });

            for (int i = 0; i < result.Count; i++)
            {
                result[i].Index = i + 1;
            }

            return result;
        }

        // Keep the client running transparently, with no desktop interaction or taskbar/Alt+Tab entry.
        public static void SetStealthHide(RobloxWin w, bool hide, bool rememberPreference = true)
        {
            if (w == null || w.Hwnd == IntPtr.Zero || !Native.IsWindow(w.Hwnd)) return;
            IntPtr h = w.Hwnd;
            if (rememberPreference) WatchdogEngine.RememberWindowVisibility(w, hide);

            using (new DpiScope())
            {
                SavedWinState st;
                if (!States.TryGetValue(h, out st))
                {
                    st = new SavedWinState();
                    st.OrigStyle = Native.GetWindowLong(h, Native.GWL_STYLE);
                    st.OrigExStyle = Native.GetWindowLong(h, Native.GWL_EXSTYLE) & ~(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW);
                    Native.RECT cr;
                    if (Native.GetWindowRect(h, out cr))
                        st.LastVisibleRect = Rectangle.FromLTRB(cr.Left, cr.Top, cr.Right, cr.Bottom);
                    States[h] = st;
                }

                if (hide)
                {
                    // If minimized, restore first so Roblox render loop doesn't throttle while hidden
                    if (Native.IsIconic(h))
                    {
                        Native.ShowWindow(h, Native.SW_RESTORE);
                        Thread.Sleep(25);
                    }

                    Native.RECT cur;
                    if (Native.GetWindowRect(h, out cur) && cur.Width > 50 && cur.Height > 50)
                    {
                        st.LastVisibleRect = Rectangle.FromLTRB(cur.Left, cur.Top, cur.Right, cur.Bottom);
                    }

                    int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    // Hide briefly when changing shell styles so Explorer drops an existing taskbar button.
                    Native.ShowWindow(h, Native.SW_HIDE);
                    int newEx = WindowVisibility.HiddenStyles(ex);
                    Native.SetWindowLong(h, Native.GWL_EXSTYLE, newEx);
                    Native.SetLayeredWindowAttributes(h, 0, 1, Native.LWA_ALPHA);
                    Native.ShowWindow(h, Native.SW_SHOWNOACTIVATE);
                    Native.SetWindowPos(h, Native.HWND_BOTTOM, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOSENDCHANGING | Native.SWP_FRAMECHANGED);
                    st.WasHiddenByUs = true;
                    w.IsHidden = true;
                    WindowVisibility.UpdateTaskbar(h, true);
                }
                else
                {
                    // Unhide: Restore full opacity, remove click-through, and bring in front of desktop clutter
                    if (!Native.IsWindowVisible(h))
                    {
                        Native.ShowWindow(h, Native.SW_SHOWNOACTIVATE);
                    }
                    if (Native.IsIconic(h))
                    {
                        Native.ShowWindow(h, Native.SW_RESTORE);
                    }

                    Native.SetLayeredWindowAttributes(h, 0, 255, Native.LWA_ALPHA);
                    int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    int restoredEx = (ex & ~(Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | Native.WS_EX_APPWINDOW)) |
                        (st.OrigExStyle & Native.WS_EX_APPWINDOW);
                    Native.SetWindowLong(h, Native.GWL_EXSTYLE, restoredEx);

                    // Promote Z-order cleanly above normal background windows
                    Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED | Native.SWP_NOSENDCHANGING);
                    Native.SetWindowPos(h, Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOSENDCHANGING);

                    Native.RedrawWindow(h, IntPtr.Zero, IntPtr.Zero,
                        Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_FRAME | Native.RDW_ALLCHILDREN | Native.RDW_UPDATENOW);

                    st.WasHiddenByUs = false;
                    w.IsHidden = false;
                    WindowVisibility.UpdateTaskbar(h, false);
                }
            }
        }

        private static void GetFrameDelta(IntPtr h, out int dl, out int dt, out int dr, out int db)
        {
            dl = 0; dt = 0; dr = 0; db = 0;
            try
            {
                Native.RECT wr;
                Native.RECT fr;
                if (!Native.GetWindowRect(h, out wr)) return;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out fr, Marshal.SizeOf(typeof(Native.RECT))) != 0) return;
                dl = fr.Left - wr.Left;
                dt = fr.Top - wr.Top;
                dr = wr.Right - fr.Right;
                db = wr.Bottom - fr.Bottom;
                if (dl < 0 || dt < 0 || dr < 0 || db < 0 || dl > 32 || dt > 32 || dr > 32 || db > 32)
                {
                    dl = 0; dt = 0; dr = 0; db = 0;
                }
            }
            catch { dl = 0; dt = 0; dr = 0; db = 0; }
        }

        public static void CalculateGridColsRows(int count, int forcedCols, Rectangle area, out int cols, out int rows)
        {
            if (count <= 0) { cols = 1; rows = 1; return; }
            if (forcedCols > 0)
            {
                cols = forcedCols;
                rows = (count + cols - 1) / cols;
                return;
            }

            // MuMu-style smart grid calculation
            if (count == 1) { cols = 1; rows = 1; return; }
            if (count == 2) { cols = 2; rows = 1; return; }
            if (count <= 4) { cols = 2; rows = 2; return; }
            if (count <= 6) { cols = 3; rows = 2; return; }
            if (count <= 9) { cols = 3; rows = 3; return; }
            if (count <= 12) { cols = 4; rows = 3; return; }
            if (count <= 16) { cols = 4; rows = 4; return; }
            if (count <= 20) { cols = 5; rows = 4; return; }
            if (count <= 25) { cols = 5; rows = 5; return; }

            double aspect = (double)area.Width / Math.Max(1, area.Height);
            cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * aspect / 1.33)));
            rows = (count + cols - 1) / cols;
        }

        public static List<Rectangle> ComputeLayoutRects(
            int count,
            Rectangle area,
            int forcedCols,
            bool cascadeMode,
            int gap,
            bool stretchLastRow)
        {
            List<Rectangle> rects = new List<Rectangle>();
            if (count <= 0) return rects;

            if (cascadeMode)
            {
                int cw = Math.Min(Math.Max(640, area.Width * 55 / 100), area.Width - 40);
                int ch = Math.Min(Math.Max(420, area.Height * 60 / 100), area.Height - 40);
                int stepX = count > 1 ? Math.Min(48, Math.Max(20, (area.Width - cw) / (count - 1))) : 0;
                int stepY = count > 1 ? Math.Min(36, Math.Max(18, (area.Height - ch) / (count - 1))) : 0;

                for (int i = 0; i < count; i++)
                {
                    int x = area.Left + i * stepX;
                    int y = area.Top + i * stepY;
                    if (x + cw > area.Right) x = Math.Max(area.Left, area.Right - cw);
                    if (y + ch > area.Bottom) y = Math.Max(area.Top, area.Bottom - ch);
                    rects.Add(new Rectangle(x, y, cw, ch));
                }
                return rects;
            }

            int cols, rows;
            CalculateGridColsRows(count, forcedCols, area, out cols, out rows);

            for (int i = 0; i < count; i++)
            {
                int r = i / cols;
                int c = i % cols;
                int colsInThisRow = cols;
                if (stretchLastRow && r == rows - 1)
                {
                    int remaining = count - r * cols;
                    if (remaining > 0 && remaining < cols)
                        colsInThisRow = remaining;
                }

                int top = area.Top + (int)Math.Round(r * (area.Height + gap) / (double)rows);
                int bottom = area.Top + (int)Math.Round((r + 1) * (area.Height + gap) / (double)rows) - gap;
                int left = area.Left + (int)Math.Round(c * (area.Width + gap) / (double)colsInThisRow);
                int right = area.Left + (int)Math.Round((c + 1) * (area.Width + gap) / (double)colsInThisRow) - gap;

                rects.Add(Rectangle.FromLTRB(left, top, Math.Max(left + 60, right), Math.Max(top + 50, bottom)));
            }

            return rects;
        }

        public static void PlaceWindow(RobloxWin w, Rectangle target, bool borderless, bool labelTitle)
        {
            if (w == null || w.Hwnd == IntPtr.Zero || !Native.IsWindow(w.Hwnd)) return;
            IntPtr h = w.Hwnd;

            using (new DpiScope())
            {
                // If window was stealth-hidden, unhide it before placing
                // Arranging a hidden client must preserve its visibility preference.

                if (Native.IsIconic(h) || Native.IsZoomed(h))
                {
                    Native.ShowWindow(h, Native.SW_RESTORE);
                    Thread.Sleep(30);
                }

                SavedWinState st;
                if (!States.TryGetValue(h, out st))
                {
                    st = new SavedWinState();
                    st.OrigStyle = Native.GetWindowLong(h, Native.GWL_STYLE) | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
                    st.OrigExStyle = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    States[h] = st;
                }

                // Force borderless if requested OR if target cell is smaller than Roblox's framed minimum (816x638)
                bool needBorderless = borderless || target.Width < 816 || target.Height < 638;
                int curStyle = Native.GetWindowLong(h, Native.GWL_STYLE);

                if (needBorderless)
                {
                    int ns = curStyle & ~(Native.WS_CAPTION | Native.WS_THICKFRAME);
                    if (ns != curStyle)
                    {
                        Native.SetWindowLong(h, Native.GWL_STYLE, ns);
                        Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                    }
                }

                int dl, dt, dr, db;
                GetFrameDelta(h, out dl, out dt, out dr, out db);

                int x = target.X - dl;
                int y = target.Y - dt;
                int cw = target.Width + dl + dr;
                int ch = target.Height + dt + db;

                uint flags = Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOSENDCHANGING;

                // Promote window above background windows via TOPMOST -> NOTOPMOST without stealing focus
                Native.SetWindowPos(h, Native.HWND_TOPMOST, x, y, cw, ch, flags);
                Native.SetWindowPos(h, Native.HWND_NOTOPMOST, x, y, cw, ch, flags);

                // Verify if Roblox still clamped the window size; if so, strip border and re-apply
                Native.RECT actual;
                if (Native.GetWindowRect(h, out actual))
                {
                    int visW = actual.Width - dl - dr;
                    int visH = actual.Height - dt - db;
                    if (visW > target.Width + 10 || visH > target.Height + 10)
                    {
                        int ns = Native.GetWindowLong(h, Native.GWL_STYLE) & ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_BORDER);
                        Native.SetWindowLong(h, Native.GWL_STYLE, ns);
                        Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                        Native.SetWindowPos(h, Native.HWND_TOPMOST, target.X, target.Y, target.Width, target.Height, flags);
                        Native.SetWindowPos(h, Native.HWND_NOTOPMOST, target.X, target.Y, target.Width, target.Height, flags);
                    }
                }

                if (labelTitle)
                {
                    try
                    {
                        IntPtr res;
                        string accPart = !string.IsNullOrEmpty(w.Username) ? ("@" + w.Username + " — ") : "";
                        string newTitle = string.Format("[จอ #{0}] {1}Roblox (PID: {2})", w.Index, accPart, w.Pid);
                        Native.SendMessageTimeout(h, Native.WM_SETTEXT, IntPtr.Zero, newTitle, Native.SMTO_ABORTIFHUNG, 300, out res);
                    }
                    catch { }
                }
            }
        }

        public static void RestoreAllBorders(List<RobloxWin> wins)
        {
            using (new DpiScope())
            {
                foreach (RobloxWin w in wins)
                {
                    if (w == null || w.Hwnd == IntPtr.Zero || !Native.IsWindow(w.Hwnd)) continue;
                    SavedWinState st;
                    int restoreStyle;
                    if (States.TryGetValue(w.Hwnd, out st) && st.OrigStyle != 0)
                    {
                        restoreStyle = st.OrigStyle | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
                    }
                    else
                    {
                        restoreStyle = Native.GetWindowLong(w.Hwnd, Native.GWL_STYLE) | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
                    }
                    Native.SetWindowLong(w.Hwnd, Native.GWL_STYLE, restoreStyle);
                    Native.SetWindowPos(w.Hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                }
            }
        }
    }

    // =========================================================================
    // CUSTOM CYBERPUNK / MUMU-STYLE UI CONTROLS
    // =========================================================================
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }

    public class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.EnableNotifyMessage, true);
        }

        protected override void OnNotifyMessage(Message m)
        {
            // Filter out WM_ERASEBKGND (0x0014) so the ListView never flashes/flickers on updates
            if (m.Msg != 0x0014)
            {
                base.OnNotifyMessage(m);
            }
        }
    }

    public class CyberButton : Button
    {
        public Color BaseColor = Color.FromArgb(14, 165, 233);
        public Color HoverColor = Color.FromArgb(56, 189, 248);
        public Color BorderAccent = Color.FromArgb(125, 211, 252);
        public Color TextCol = Color.White;
        public string SubText = "";
        private bool hovered;
        private bool pressed;

        public CyberButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            if (Width < 4 || Height < 4) return;
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.FromArgb(11, 15, 23));

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (pressed) r.Offset(0, 1);

            using (GraphicsPath path = MainForm.RoundedRect(r, 10))
            {
                Color fill = pressed ? ControlPaint.Dark(BaseColor, 0.08f) : (hovered ? HoverColor : BaseColor);
                using (LinearGradientBrush br = new LinearGradientBrush(r, fill, ControlPaint.Dark(fill, 0.16f), LinearGradientMode.Vertical))
                {
                    g.FillPath(br, path);
                }
                using (Pen p = new Pen(hovered ? Color.White : BorderAccent, hovered ? 1.6f : 1.1f))
                {
                    g.DrawPath(p, path);
                }
            }

            if (string.IsNullOrEmpty(SubText))
            {
                TextRenderer.DrawText(g, Text, Font, r, TextCol,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            else
            {
                using (Font subF = new Font("Segoe UI", 8.2f, FontStyle.Regular))
                {
                    int gap = Math.Max(4, Font.Height / 4);
                    int total = Font.Height + subF.Height + gap;
                    int top = r.Top + Math.Max(2, (r.Height - total) / 2);
                    Rectangle topRect = new Rectangle(r.X + 8, top, r.Width - 16, Font.Height);
                    Rectangle botRect = new Rectangle(r.X + 8, top + Font.Height + gap, r.Width - 16, subF.Height);
                    TextRenderer.DrawText(g, Text, Font, topRect, TextCol,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, SubText, subF, botRect, Color.FromArgb(225, 240, 255),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }
            }
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -5, -5), TextCol, BaseColor);
        }
    }

    // =========================================================================
    // MAIN APPLICATION WINDOW
    // =========================================================================
    public class MainForm : Form
    {
        private const int HK_ARRANGE = 9101;
        private const int HK_TOGGLE_HIDE = 9102;
        private const int HK_SHOW_ALL = 9103;

        private List<RobloxWin> windows = new List<RobloxWin>();
        private List<MonitorItem> monitors = new List<MonitorItem>();

        // Layout Settings
        private int forcedCols = 0; // 0 = MuMu Auto Grid, -1 = Cascade
        private bool borderlessMode = true;
        private bool stretchLastRow = true;
        private bool useWorkArea = true;
        private bool labelTitles = true;
        private bool autoArrangeNew = false;
        private int gapPixels = 2;

        // UI Controls
        private Label lblHeaderStatus;
        private Label lblBannerInfo;
        private ComboBox cbMonitors;
        private CheckBox chkBorderless;
        private CheckBox chkStretchLast;
        private CheckBox chkUseWorkArea;
        private CheckBox chkLabelTitles;
        private CheckBox chkAutoArrange;
        private NumericUpDown numGap;
        private BufferedPanel previewBox;
        private ListView lvWindows;
        private NotifyIcon trayIcon;
        private System.Windows.Forms.Timer scanTimer;
        private CyberButton btnWatchdogToggle;
        private TextBox txtTargetPlaceId;
        private CyberButton btnUpdate;
        private System.Windows.Forms.Timer updateTimer;
        private bool updateBusy, closingForUpdate;
        private UpdateManifest availableUpdate;

        private List<CyberButton> modeButtons = new List<CyberButton>();
        private HashSet<IntPtr> knownAutoArrangedSet = new HashSet<IntPtr>();

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            if (d <= 0) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private readonly bool previewOnly;

        protected override bool ShowWithoutActivation
        {
            get { return previewOnly || base.ShowWithoutActivation; }
        }

        public MainForm() : this(false) { }

        public MainForm(bool previewOnly)
        {
            this.previewOnly = previewOnly;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "NEXUS ROBLOX MANAGER — Window Manager & Auto-Rejoin";
            Size = new Size(1100, 830);
            MinimumSize = new Size(940, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 14, 22);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            DoubleBuffered = true;

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(icoPath)) Icon = new Icon(icoPath);
                else Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            if (!previewOnly)
            {
                MultiInstanceUnlocker.Start();
                WatchdogEngine.Init();
            }

            WatchdogEngine.OnBannerLog = delegate(string msg)
            {
                try
                {
                    if (IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate { SetBanner(msg); });
                }
                catch { }
            };

            BuildUi();
            if (!previewOnly) { SetupTray(); WindowVisibility.Start(); }
            UpdateWatchdogButtonStyle();

            scanTimer = new System.Windows.Forms.Timer();
            scanTimer.Interval = 1500;
            scanTimer.Tick += delegate { RefreshWindows(false); };
            if (!previewOnly) scanTimer.Start();

            RefreshMonitors();
            if (!previewOnly) RefreshWindows(true);
            if (!previewOnly)
            {
                updateTimer = new System.Windows.Forms.Timer();
                updateTimer.Interval = 30 * 60 * 1000;
                updateTimer.Tick += delegate { CheckForUpdates(false); };
                updateTimer.Start();
                Shown += delegate { CheckForUpdates(false); };
            }
        }

        private void CheckForUpdates(bool manual)
        {
            if (updateBusy) return;
            if (manual && availableUpdate != null) { OfferUpdate(); return; }
            updateBusy = true; btnUpdate.Enabled = false; btnUpdate.Text = "กำลังตรวจอัปเดต...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateManifest found = null; Exception failure = null;
                try { found = UpdateService.Check(); } catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        updateBusy = false; btnUpdate.Enabled = true; availableUpdate = found;
                        btnUpdate.Text = found == null ? "ตรวจอัปเดต · v" + AppVersion.Value : "อัปเดตเป็น v" + found.version;
                        if (manual)
                        {
                            if (failure != null) MessageBox.Show(this, "ตรวจอัปเดตไม่ได้ ใช้รุ่นเดิมต่อได้\n" + failure.Message, "Nexus Update");
                            else if (found == null) SetBanner("Nexus เป็นเวอร์ชันล่าสุดแล้ว: " + AppVersion.Value);
                            else OfferUpdate();
                        }
                        else if (found != null) SetBanner("มี Nexus เวอร์ชันใหม่ " + found.version + " • กดปุ่มอัปเดตด้านบน");
                    });
                }
                catch { }
            });
        }

        private void OfferUpdate()
        {
            UpdateManifest manifest = availableUpdate;
            if (manifest == null || updateBusy) return;
            if (MessageBox.Show(this, "อัปเดต Nexus เป็น v" + manifest.version + " และเปิดโปรแกรมใหม่?\nเกมที่เปิดอยู่จะคงอยู่ และการตั้งค่าจะเก็บไว้\n\n" + manifest.notes,
                "Nexus Update", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            updateBusy = true; btnUpdate.Enabled = false; btnUpdate.Text = "กำลังดาวน์โหลด...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string staged = null; Exception failure = null;
                try { staged = UpdateService.Stage(manifest); } catch (Exception ex) { failure = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        updateBusy = false; btnUpdate.Enabled = true; btnUpdate.Text = "อัปเดตเป็น v" + manifest.version;
                        if (failure != null) { MessageBox.Show(this, "ดาวน์โหลดอัปเดตไม่สำเร็จ\n" + failure.Message, "Nexus Update"); return; }
                        if (!WatchdogEngine.TryPrepareForUpdate())
                        { SetBanner("รอให้การตรวจสถานะหรือ Rejoin ที่กำลังทำอยู่จบ แล้วกดอัปเดตอีกครั้ง"); return; }
                        try { UpdateService.StartInstaller(staged); closingForUpdate = true; Close(); }
                        catch (Exception ex) { WatchdogEngine.CancelPrepareForUpdate(); MessageBox.Show(this, ex.Message, "Nexus Update"); }
                    });
                }
                catch { }
            });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkTitleBar(Handle);
            if (!previewOnly) RegisterGlobalHotkeys();
        }

        private void RegisterGlobalHotkeys()
        {
            try
            {
                Native.UnregisterHotKey(Handle, HK_ARRANGE);
                Native.UnregisterHotKey(Handle, HK_TOGGLE_HIDE);
                Native.UnregisterHotKey(Handle, HK_SHOW_ALL);

                // F9 = Arrange All, F10 = Toggle Hide/Show All, Ctrl+Alt+F11 = Force Show All
                Native.RegisterHotKey(Handle, HK_ARRANGE, Native.MOD_NOREPEAT, 0x78); // F9
                Native.RegisterHotKey(Handle, HK_TOGGLE_HIDE, Native.MOD_NOREPEAT, 0x79); // F10
                Native.RegisterHotKey(Handle, HK_SHOW_ALL, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x7A); // Ctrl+Alt+F11
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (!previewOnly && Native.ShowManagerMessage != 0 && (uint)m.Msg == Native.ShowManagerMessage)
            {
                RestoreFromTray();
                return;
            }
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HK_ARRANGE) ArrangeAllNow();
                else if (id == HK_TOGGLE_HIDE) ToggleHideAllNow();
                else if (id == HK_SHOW_ALL) ShowAllNow();
            }
            base.WndProc(ref m);
        }

        private void SetupTray()
        {
            ContextMenu trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("⚡ เรียงจอแบบ MuMu ทันที (F9)", delegate { ArrangeAllNow(); });
            trayMenu.MenuItems.Add("🙈 ซ่อนทุกจอ - ฟาร์มต่อปกติ (F10)", delegate { HideAllNow(); });
            trayMenu.MenuItems.Add("👁️ ยกเลิกซ่อนทั้งหมด", delegate { ShowAllNow(); });
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("เปิดหน้าต่างโปรแกรม", delegate { RestoreFromTray(); });
            trayMenu.MenuItems.Add("ออกจากโปรแกรม", delegate { Close(); });

            trayIcon = new NotifyIcon();
            trayIcon.Text = "Nexuse Roblox Window Manager (F9 เรียงจอ / F10 ซ่อนจอ)";
            trayIcon.Icon = Icon ?? SystemIcons.Application;
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private TableLayoutPanel MakeTable(int columns, params float[] widths)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Margin = Padding.Empty;
            table.ColumnCount = columns;
            table.RowCount = 1;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < columns; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, widths[i]));
            return table;
        }

        private Label MakeLabel(string text, bool bold)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            label.Font = new Font("Segoe UI", bold ? 9.5f : 9f, bold ? FontStyle.Bold : FontStyle.Regular);
            label.ForeColor = bold ? Color.FromArgb(125, 211, 252) : Color.FromArgb(203, 213, 225);
            return label;
        }

        private CyberButton LayoutButton(string text, Color color, EventHandler click)
        {
            CyberButton button = CreateSmallButton(text, color, 0, 0, 180);
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4);
            button.Click += click;
            return button;
        }

        private void BuildUi()
        {
            SuspendLayout();
            TableLayoutPanel root = MakeTable(1, 100);
            root.RowStyles.Clear();
            root.Name = "MainLayout";
            root.Padding = new Padding(14, 8, 14, 10);
            root.RowCount = 6;
            foreach (int height in new int[] { 64, 360, 36, 28 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Controls.Add(root);

            TableLayoutPanel header = MakeTable(2, 72, 28);
            header.BackColor = Color.FromArgb(15, 23, 36);
            header.Padding = new Padding(12, 4, 12, 4);
            TableLayoutPanel title = MakeTable(1, 100);
            title.RowStyles.Clear();
            title.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            title.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            Label name = MakeLabel("NEXUS  /  ROBLOX WINDOW MANAGER", true);
            name.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            name.ForeColor = Color.White;
            title.Controls.Add(name, 0, 0);
            title.Controls.Add(MakeLabel("จัดเรียงจอ • ซ่อน / แสดง • Nexus Auto-Rejoin", false), 0, 1);
            header.Controls.Add(title, 0, 0);
            lblHeaderStatus = MakeLabel("กำลังสแกน Roblox...", true);
            lblHeaderStatus.TextAlign = ContentAlignment.MiddleRight;
            lblHeaderStatus.ForeColor = Color.FromArgb(52, 211, 153);
            TableLayoutPanel headerRight = MakeTable(1, 100);
            headerRight.RowStyles.Clear(); headerRight.RowCount = 2;
            headerRight.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            headerRight.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            headerRight.Controls.Add(lblHeaderStatus, 0, 0);
            btnUpdate = LayoutButton("ตรวจอัปเดต · v" + AppVersion.Value, Color.FromArgb(51,65,85), delegate { CheckForUpdates(true); });
            btnUpdate.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnUpdate.Margin = new Padding(4,0,4,0);
            headerRight.Controls.Add(btnUpdate, 0, 1);
            header.Controls.Add(headerRight, 1, 0);
            root.Controls.Add(header, 0, 0);

            Panel scroll = new Panel();
            scroll.Name = "SettingsScroll";
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.Margin = Padding.Empty;
            TableLayoutPanel tools = MakeTable(1, 100);
            tools.RowStyles.Clear();
            tools.Dock = DockStyle.Top;
            tools.Height = 356;
            tools.MinimumSize = new Size(880, 356);
            tools.RowCount = 3;
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 224));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            scroll.Controls.Add(tools);
            root.Controls.Add(scroll, 0, 1);

            TableLayoutPanel hero = MakeTable(5, 25, 24, 20, 15, 16);
            CyberButton arrange = LayoutButton("เรียงจอทันที  (F9)", Color.FromArgb(2, 132, 199), delegate { ArrangeAllNow(); });
            arrange.SubText = "วางทุกจอในตารางอัตโนมัติ";
            hero.Controls.Add(arrange, 0, 0);
            CyberButton hide = LayoutButton("ซ่อนทุกจอ  (F10)", Color.FromArgb(180, 83, 9), delegate { HideAllNow(); });
            hide.SubText = "ซ่อนหน้าจอ / Taskbar / Alt+Tab";
            hero.Controls.Add(hide, 1, 0);
            CyberButton show = LayoutButton("แสดงทุกจอ", Color.FromArgb(5, 150, 105), delegate { ShowAllNow(); });
            show.SubText = "เรียกหน้าต่างที่ซ่อนกลับมา";
            hero.Controls.Add(show, 2, 0);
            hero.Controls.Add(LayoutButton("คืนขอบหน้าต่าง", Color.FromArgb(51, 65, 85), delegate
            {
                WindowEngine.RestoreAllBorders(windows);
                SetBanner("คืนขอบหน้าต่างให้ทุกจอแล้ว");
                RefreshWindows(true);
            }), 3, 0);
            CyberButton loginWait = LayoutButton("เวลาล็อกอิน Real", Color.FromArgb(51, 65, 85), delegate
            {
                using (RealLoginTimeoutForm dialog = new RealLoginTimeoutForm()) dialog.ShowDialog(this);
            });
            loginWait.Name = "RealLoginTimeoutButton";
            loginWait.SubText = "ขยายเวลารอเป็น 15 นาที";
            hero.Controls.Add(loginWait, 4, 0);
            tools.Controls.Add(hero, 0, 0);

            TableLayoutPanel middle = MakeTable(2, 65, 35);
            middle.BackColor = Color.FromArgb(15, 22, 34);
            middle.Padding = new Padding(10, 6, 10, 6);
            middle.Margin = new Padding(4);
            TableLayoutPanel settings = MakeTable(1, 100);
            settings.RowStyles.Clear();
            settings.RowCount = 4;
            foreach (int height in new int[] { 24, 70, 34 }) settings.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settings.Controls.Add(MakeLabel("รูปแบบการจัดเรียง", true), 0, 0);
            FlowLayoutPanel presets = new FlowLayoutPanel();
            presets.Dock = DockStyle.Fill;
            presets.Margin = Padding.Empty;
            string[] labels = { "อัตโนมัติ", "2 คอลัมน์", "3 คอลัมน์", "4 คอลัมน์", "5 คอลัมน์", "ซ้อนเฉียง" };
            int[] values = { 0, 2, 3, 4, 5, -1 };
            for (int i = 0; i < values.Length; i++)
            {
                int value = values[i];
                CyberButton button = CreateSmallButton(labels[i], Color.FromArgb(30, 41, 59), 0, 0, 88);
                button.Size = new Size(88, 30);
                button.Margin = new Padding(0, 2, 5, 2);
                button.Tag = value;
                button.Click += delegate { forcedCols = value; UpdateModeButtonStyles(); ArrangeAllNow(); };
                modeButtons.Add(button);
                presets.Controls.Add(button);
            }
            settings.Controls.Add(presets, 0, 1);
            TableLayoutPanel monitorRow = MakeTable(4, 12, 60, 16, 12);
            monitorRow.Controls.Add(MakeLabel("หน้าจอ", false), 0, 0);
            cbMonitors = new ComboBox();
            cbMonitors.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMonitors.BackColor = Color.FromArgb(30, 41, 59);
            cbMonitors.ForeColor = Color.White;
            cbMonitors.FlatStyle = FlatStyle.Flat;
            cbMonitors.DrawMode = DrawMode.OwnerDrawFixed;
            cbMonitors.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                using (SolidBrush background = new SolidBrush(cbMonitors.BackColor)) e.Graphics.FillRectangle(background, e.Bounds);
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(cbMonitors.Items[e.Index]), cbMonitors.Font, e.Bounds,
                        cbMonitors.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            cbMonitors.Dock = DockStyle.Fill;
            cbMonitors.SelectedIndexChanged += delegate { if (previewBox != null) previewBox.Invalidate(); };
            monitorRow.Controls.Add(cbMonitors, 1, 0);
            monitorRow.Controls.Add(MakeLabel("ระยะ (px)", false), 2, 0);
            numGap = new NumericUpDown();
            numGap.Minimum = 0; numGap.Maximum = 24; numGap.Value = gapPixels;
            numGap.BackColor = Color.FromArgb(30, 41, 59); numGap.ForeColor = Color.White;
            numGap.Dock = DockStyle.Fill;
            numGap.ValueChanged += delegate { gapPixels = (int)numGap.Value; previewBox.Invalidate(); };
            monitorRow.Controls.Add(numGap, 3, 0);
            settings.Controls.Add(monitorRow, 0, 2);
            TableLayoutPanel options = MakeTable(2, 50, 50);
            options.RowStyles.Clear();
            options.RowCount = 3;
            for (int i = 0; i < 3; i++) options.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
            chkBorderless = CreateOptionCheck("ไร้ขอบหน้าต่าง", borderlessMode, 0, 0, delegate { borderlessMode = chkBorderless.Checked; previewBox.Invalidate(); });
            chkStretchLast = CreateOptionCheck("ขยายแถวสุดท้ายให้เต็ม", stretchLastRow, 0, 0, delegate { stretchLastRow = chkStretchLast.Checked; previewBox.Invalidate(); });
            chkUseWorkArea = CreateOptionCheck("เว้นพื้นที่ Taskbar", useWorkArea, 0, 0, delegate { useWorkArea = chkUseWorkArea.Checked; previewBox.Invalidate(); });
            chkLabelTitles = CreateOptionCheck("แสดงหมายเลขบนหัวจอ", labelTitles, 0, 0, delegate { labelTitles = chkLabelTitles.Checked; });
            chkAutoArrange = CreateOptionCheck("เรียงอัตโนมัติเมื่อมีจอใหม่", autoArrangeNew, 0, 0, delegate { autoArrangeNew = chkAutoArrange.Checked; });
            CheckBox[] checks = { chkBorderless, chkStretchLast, chkUseWorkArea, chkLabelTitles, chkAutoArrange };
            for (int i = 0; i < checks.Length; i++)
            {
                checks[i].Dock = DockStyle.Fill;
                checks[i].AutoSize = false;
                checks[i].AutoEllipsis = true;
                checks[i].Margin = Padding.Empty;
                options.Controls.Add(checks[i], i % 2, i / 2);
            }
            settings.Controls.Add(options, 0, 3);
            middle.Controls.Add(settings, 0, 0);
            previewBox = new BufferedPanel();
            previewBox.Dock = DockStyle.Fill;
            previewBox.Margin = new Padding(12, 0, 0, 0);
            previewBox.BackColor = Color.FromArgb(9, 13, 20);
            previewBox.Cursor = Cursors.Hand;
            previewBox.Paint += PaintMonitorPreview;
            previewBox.MouseClick += OnPreviewClick;
            middle.Controls.Add(previewBox, 1, 0);
            tools.Controls.Add(middle, 0, 1);
            UpdateModeButtonStyles();

            TableLayoutPanel rejoin = MakeTable(4, 32, 25, 24, 19);
            rejoin.BackColor = Color.FromArgb(14, 22, 36);
            rejoin.Margin = new Padding(4, 4, 4, 2);
            btnWatchdogToggle = LayoutButton("Auto-Rejoin", Color.FromArgb(5, 150, 105), delegate
            {
                WatchdogEngine.WatchdogEnabled = !WatchdogEngine.WatchdogEnabled;
                WatchdogEngine.SaveSettings(); UpdateWatchdogButtonStyle();
                SetBanner(WatchdogEngine.WatchdogEnabled ? "เปิด Nexus Auto-Rejoin แล้ว • ปิด Auto Rejoin ใน Real เพื่อกันเปิดซ้ำ" : "พัก Nexus Auto-Rejoin แล้ว");
            });
            rejoin.Controls.Add(btnWatchdogToggle, 0, 0);
            TableLayoutPanel target = MakeTable(1, 100);
            target.RowStyles.Clear();
            target.Margin = new Padding(8, 3, 8, 3);
            target.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            target.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            target.Controls.Add(MakeLabel("แมพจาก Real (รายการแรก)", false), 0, 0);
            txtTargetPlaceId = new TextBox();
            txtTargetPlaceId.ReadOnly = true;
            txtTargetPlaceId.Dock = DockStyle.Fill;
            txtTargetPlaceId.Margin = Padding.Empty;
            txtTargetPlaceId.Text = WatchdogEngine.TargetPlaceId.ToString();
            txtTargetPlaceId.BackColor = Color.FromArgb(30, 41, 59);
            txtTargetPlaceId.ForeColor = Color.White;
            txtTargetPlaceId.BorderStyle = BorderStyle.FixedSingle;
            target.Controls.Add(txtTargetPlaceId, 0, 1);
            rejoin.Controls.Add(target, 1, 0);
            rejoin.Controls.Add(LayoutButton("Rejoin จอที่เลือก", Color.FromArgb(2, 132, 199), delegate { ForceRestartSelected(); }), 2, 0);
            rejoin.Controls.Add(LayoutButton("Rejoin ทุกจอ", Color.FromArgb(180, 83, 9), delegate { WatchdogEngine.KillRobloxAll(windows); }), 3, 0);
            tools.Controls.Add(rejoin, 0, 2);

            lblBannerInfo = MakeLabel("เปิด Real ค้างไว้สำหรับ Auto Inject / Execute • บัญชีที่ลบจาก Real จะหยุดติดตามอัตโนมัติ", false);
            lblBannerInfo.BackColor = Color.FromArgb(14, 34, 53);
            lblBannerInfo.ForeColor = Color.FromArgb(165, 243, 252);
            lblBannerInfo.Padding = new Padding(10, 0, 10, 0);
            lblBannerInfo.Margin = new Padding(4, 2, 4, 2);
            root.Controls.Add(lblBannerInfo, 0, 2);
            root.Controls.Add(MakeLabel("หน้าต่าง Roblox  /  คลิก Auto Rejoin เพื่อเปิด-ปิดรายบัญชี • ดับเบิลคลิกเพื่อซ่อนจอ", true), 0, 3);
            lvWindows = new BufferedListView();
            lvWindows.Name = "WindowsList";
            lvWindows.Dock = DockStyle.Fill;
            lvWindows.Margin = new Padding(4, 0, 4, 4);
            lvWindows.View = View.Details;
            lvWindows.FullRowSelect = true;
            lvWindows.HideSelection = false;
            lvWindows.MultiSelect = true;
            lvWindows.OwnerDraw = true;
            lvWindows.BackColor = Color.FromArgb(13, 19, 30);
            lvWindows.ForeColor = Color.FromArgb(241, 245, 249);
            lvWindows.BorderStyle = BorderStyle.None;
            lvWindows.Font = new Font("Segoe UI", 9.5f);
            string[] columns = { "จอ", "บัญชี Roblox", "PID", "สถานะ", "Heartbeat / Rejoin", "ตำแหน่ง / ขนาด", "Auto Rejoin" };
            foreach (string column in columns) lvWindows.Columns.Add(column, 100);
            lvWindows.Resize += delegate
            {
                int available = Math.Max(840, lvWindows.ClientSize.Width - 1);
                int[] weights = { 5, 18, 7, 22, 17, 18, 13 };
                int used = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    int width = i == weights.Length - 1 ? available - used : available * weights[i] / 100;
                    lvWindows.Columns[i].Width = width;
                    used += width;
                }
            };
            lvWindows.DrawColumnHeader += OnDrawListColumnHeader;
            lvWindows.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            lvWindows.DrawSubItem += OnDrawListSubItem;
            lvWindows.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ListViewHitTestInfo hit = lvWindows.HitTest(e.Location);
                if (hit.Item == null || hit.SubItem == null || hit.Item.SubItems.IndexOf(hit.SubItem) != 6) return;
                RobloxWin window = hit.Item.Tag as RobloxWin;
                if (window == null || window.AccountRef == null) { SetBanner("รอให้ระบุบัญชีของจอนี้ก่อน"); return; }
                WatchdogEngine.SetAccountRejoinEnabled(window.AccountRef, !window.AccountRef.RejoinEnabled);
                UpdateListView();
                lvWindows.Invalidate(hit.Item.Bounds);
            };
            lvWindows.DoubleClick += delegate
            {
                ListViewHitTestInfo hit = lvWindows.HitTest(lvWindows.PointToClient(Cursor.Position));
                if (hit.Item != null && hit.SubItem != null && hit.Item.SubItems.IndexOf(hit.SubItem) == 6) return;
                ToggleHideSelected();
            };
            ContextMenu accountMenu = new ContextMenu();
            accountMenu.MenuItems.Add("ปิด Auto Rejoin บัญชีที่เลือก", delegate { SetSelectedRejoinEnabled(false); });
            accountMenu.MenuItems.Add("เปิด Auto Rejoin บัญชีที่เลือก", delegate { SetSelectedRejoinEnabled(true); });
            lvWindows.ContextMenu = accountMenu;
            root.Controls.Add(lvWindows, 0, 4);

            TableLayoutPanel footer = MakeTable(5, 24, 23, 14, 20, 19);
            footer.Name = "WindowActions";
            footer.Padding = new Padding(0, 4, 0, 2);
            footer.Controls.Add(LayoutButton("ซ่อน / แสดงจอที่เลือก", Color.FromArgb(14, 116, 144), delegate { ToggleHideSelected(); }), 0, 0);
            footer.Controls.Add(LayoutButton("ดึงจอขึ้นหน้าสุด", Color.FromArgb(30, 64, 175), delegate { FocusSelected(); }), 1, 0);
            footer.Controls.Add(LayoutButton("สแกนใหม่", Color.FromArgb(51, 65, 85), delegate { RefreshWindows(true); SetBanner("อัปเดตรายการจอแล้ว"); }), 2, 0);
            footer.Controls.Add(LayoutButton("ปิดจอที่เลือก", Color.FromArgb(159, 18, 57), delegate { KillSelected(); }), 3, 0);
            footer.Controls.Add(LayoutButton("เก็บลง Tray", Color.FromArgb(71, 85, 105), delegate { Hide(); if (trayIcon != null) trayIcon.ShowBalloonTip(2000, "Nexus", "ยังทำงานอยู่เบื้องหลัง • F9 เรียงจอ / F10 ซ่อนจอ", ToolTipIcon.Info); }), 4, 0);
            root.Controls.Add(footer, 0, 5);
            ResumeLayout(true);
        }

        private void OnDrawListColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(20, 30, 48)))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }
            using (Pen borderPen = new Pen(Color.FromArgb(40, 56, 82)))
            {
                e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
            }
            using (Font hf = new Font("Segoe UI", 9.0f, FontStyle.Bold))
            {
                Rectangle textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, hf, textRect, Color.FromArgb(125, 211, 252),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        private void OnDrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            RobloxWin rw = e.Item.Tag as RobloxWin;
            bool isHidden = rw != null && rw.IsHidden;
            string status = rw != null ? (rw.BotStatus ?? "") : "";

            Color rowBg = selected
                ? Color.FromArgb(26, 54, 93)
                : (e.ItemIndex % 2 == 0 ? Color.FromArgb(13, 19, 30) : Color.FromArgb(17, 25, 40));

            using (SolidBrush bg = new SolidBrush(rowBg))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            Color textCol = Color.FromArgb(226, 232, 240);
            if (e.ColumnIndex == 0)
                textCol = Color.FromArgb(56, 189, 248);
            else if (e.ColumnIndex == 3)
            {
                if (status == "DISCONNECTED" || status == "LEFT_MAP" || status == "ERR 276")
                    textCol = Color.FromArgb(248, 113, 113);
                else if (status == "RESTARTING" || status == "LOADING")
                    textCol = Color.FromArgb(251, 191, 36);
                else if (status == "TELEPORTING")
                    textCol = Color.FromArgb(56, 189, 248);
                else if (isHidden)
                    textCol = Color.FromArgb(251, 191, 36);
                else
                    textCol = Color.FromArgb(52, 211, 153);
            }

            if (e.ColumnIndex == 6 && rw != null && rw.AccountRef != null)
                textCol = rw.AccountRef.RejoinEnabled ? Color.FromArgb(52,211,153) : Color.FromArgb(248,113,113);
            Rectangle r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            using (Font f = new Font("Segoe UI", 9.2f, (e.ColumnIndex == 0 || e.ColumnIndex == 3) ? FontStyle.Bold : FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, f, r, textCol,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        private CheckBox CreateOptionCheck(string text, bool initial, int x, int y, EventHandler onChange)
        {
            CheckBox chk = new CheckBox();
            chk.Text = text;
            chk.Checked = initial;
            chk.ForeColor = Color.FromArgb(226, 232, 240);
            chk.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            chk.AutoSize = true;
            chk.Location = new Point(x, y);
            chk.Cursor = Cursors.Hand;
            if (onChange != null) chk.CheckedChanged += onChange;
            return chk;
        }

        private CyberButton CreateSmallButton(string text, Color col, int x, int y, int w)
        {
            CyberButton b = new CyberButton();
            b.Text = text;
            b.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            b.BaseColor = col;
            b.HoverColor = ControlPaint.Light(col, 0.15f);
            b.BorderAccent = ControlPaint.Light(col, 0.35f);
            b.Bounds = new Rectangle(x, y, w, 38);
            return b;
        }

        private void UpdateModeButtonStyles()
        {
            foreach (CyberButton b in modeButtons)
            {
                int val = (int)b.Tag;
                bool active = (val == forcedCols);
                if (active)
                {
                    b.BaseColor = Color.FromArgb(2, 132, 199);
                    b.HoverColor = Color.FromArgb(14, 165, 233);
                    b.BorderAccent = Color.FromArgb(125, 211, 252);
                }
                else
                {
                    b.BaseColor = Color.FromArgb(30, 41, 59);
                    b.HoverColor = Color.FromArgb(51, 65, 85);
                    b.BorderAccent = Color.FromArgb(71, 85, 105);
                }
                b.Invalidate();
            }
        }

        private void SetBanner(string msg)
        {
            if (lblBannerInfo != null) lblBannerInfo.Text = msg;
        }

        private void RefreshMonitors()
        {
            monitors = WindowEngine.GetMonitors();
            cbMonitors.Items.Clear();
            foreach (MonitorItem m in monitors)
            {
                cbMonitors.Items.Add(m.Label);
            }
            if (cbMonitors.Items.Count > 0) cbMonitors.SelectedIndex = 0;
        }

        private MonitorItem SelectedMonitor()
        {
            if (monitors.Count == 0) RefreshMonitors();
            int idx = cbMonitors.SelectedIndex;
            if (idx >= 0 && idx < monitors.Count) return monitors[idx];
            return monitors[0];
        }

        private void UpdateWatchdogButtonStyle()
        {
            if (btnWatchdogToggle == null) return;
            if (WatchdogEngine.WatchdogEnabled)
            {
                btnWatchdogToggle.Text = "Auto-Rejoin  เปิด (ON)";
                btnWatchdogToggle.BaseColor = Color.FromArgb(5, 150, 105);
                btnWatchdogToggle.HoverColor = Color.FromArgb(16, 185, 129);
                btnWatchdogToggle.BorderAccent = Color.FromArgb(52, 211, 153);
            }
            else
            {
                btnWatchdogToggle.Text = "Auto-Rejoin  พัก (OFF)";
                btnWatchdogToggle.BaseColor = Color.FromArgb(30, 41, 59);
                btnWatchdogToggle.HoverColor = Color.FromArgb(51, 65, 85);
                btnWatchdogToggle.BorderAccent = Color.FromArgb(100, 116, 139);
            }
            btnWatchdogToggle.Invalidate();
        }

        private string lastPreviewSignature = "";

        private void SetSelectedRejoinEnabled(bool enabled)
        {
            foreach (ListViewItem item in lvWindows.SelectedItems)
            {
                RobloxWin window = item.Tag as RobloxWin;
                if (window != null && window.AccountRef != null) WatchdogEngine.SetAccountRejoinEnabled(window.AccountRef, enabled);
            }
            UpdateListView(); lvWindows.Invalidate();
            SetBanner(enabled ? "เปิด Auto Rejoin บัญชีที่เลือกแล้ว" : "ปิด Auto Rejoin บัญชีที่เลือกแล้ว • จออื่นทำงานต่อปกติ");
        }

        private void ForceRestartSelected()
        {
            if (lvWindows.SelectedItems.Count == 0)
            {
                int autoClosed = 0;
                foreach (RobloxWin w in windows)
                {
                    if (w.BotStatus == "LEFT_MAP" || w.BotStatus == "DISCONNECTED")
                    {
                        WatchdogEngine.KillRobloxWindow(w, "Nexus rejoin disconnected client");
                        autoClosed++;
                    }
                }
                if (autoClosed > 0)
                {
                    RefreshWindows(true);
                    return;
                }
                SetBanner("⚠️ กรุณาเลือกจอในตารางก่อนกด Rejoin (หรือใช้ปุ่ม Rejoin ทุกจอ)");
                return;
            }

            foreach (ListViewItem it in lvWindows.SelectedItems)
            {
                RobloxWin rw = it.Tag as RobloxWin;
                if (rw != null)
                {
                    WatchdogEngine.KillRobloxWindow(rw, "Manual Nexus rejoin");
                }
            }
            RefreshWindows(true);
        }

        private void RefreshWindows(bool forceUiUpdate)
        {
            WatchdogEngine.RefreshRealTarget();
            if (txtTargetPlaceId != null) txtTargetPlaceId.Text = WatchdogEngine.TargetPlaceId.ToString();
            List<RobloxWin> scanned = WindowEngine.ScanRobloxWindows();
            WatchdogEngine.RefreshStateAndMapWindows(scanned);
            WatchdogEngine.ApplyVisibilityPreferences(scanned);
            windows = scanned;

            int total = windows.Count;
            int hiddenCount = 0;
            StringBuilder sigSb = new StringBuilder();
            foreach (RobloxWin w in windows)
            {
                if (w.IsHidden) hiddenCount++;
                sigSb.Append(w.Pid).Append(':').Append(w.IsHidden ? '1' : '0').Append(':').Append(w.Username ?? "").Append('|');
            }
            int visibleCount = total - hiddenCount;

            string newHeader = string.Format("🟢 พบ Roblox {0} จอ (แสดง {1} | ซ่อน {2})", total, visibleCount, hiddenCount);
            if (lblHeaderStatus.Text != newHeader)
                lblHeaderStatus.Text = newHeader;

            if (hiddenCount > 0)
            {
                lblHeaderStatus.ForeColor = Color.FromArgb(251, 191, 36);
                lblHeaderStatus.BackColor = Color.FromArgb(48, 32, 12);
            }
            else
            {
                lblHeaderStatus.ForeColor = Color.FromArgb(52, 211, 153);
                lblHeaderStatus.BackColor = Color.FromArgb(16, 42, 38);
            }

            // Check auto-arrange on newly opened Roblox windows
            if (autoArrangeNew && total > 0)
            {
                bool hasNew = false;
                foreach (RobloxWin w in windows)
                {
                    if (!knownAutoArrangedSet.Contains(w.Hwnd))
                    {
                        hasNew = true;
                        break;
                    }
                }
                if (hasNew)
                {
                    knownAutoArrangedSet.Clear();
                    foreach (RobloxWin w in windows) knownAutoArrangedSet.Add(w.Hwnd);
                    ArrangeAllNow();
                    return;
                }
            }
            else
            {
                knownAutoArrangedSet.Clear();
                foreach (RobloxWin w in windows) knownAutoArrangedSet.Add(w.Hwnd);
            }

            UpdateListView();

            string curSig = sigSb.ToString();
            if (forceUiUpdate || curSig != lastPreviewSignature)
            {
                lastPreviewSignature = curSig;
                previewBox.Invalidate();
            }
        }

        private void UpdateListView()
        {
            // Adjust row count only when windows are opened or closed (never Clear() every tick!)
            bool countChanged = (lvWindows.Items.Count != windows.Count);
            if (countChanged)
            {
                lvWindows.BeginUpdate();
                while (lvWindows.Items.Count > windows.Count)
                {
                    lvWindows.Items.RemoveAt(lvWindows.Items.Count - 1);
                }
                while (lvWindows.Items.Count < windows.Count)
                {
                    ListViewItem emptyItem = new ListViewItem(new string[] { "", "", "", "", "", "", "" });
                    lvWindows.Items.Add(emptyItem);
                }
            }

            for (int i = 0; i < windows.Count; i++)
            {
                RobloxWin w = windows[i];
                ListViewItem item = lvWindows.Items[i];
                RobloxWin prevWin = item.Tag as RobloxWin;
                bool styleChanged = (prevWin == null || prevWin.IsHidden != w.IsHidden || prevWin.BotStatus != w.BotStatus);
                item.Tag = w;

                string unameDisplay = !string.IsNullOrEmpty(w.Username)
                    ? ("@" + w.Username)
                    : w.Title;

                string statusStr;
                if (w.AccountRef != null && !w.AccountRef.RejoinEnabled)
                    statusStr = "พัก Auto Rejoin บัญชีนี้";
                else if (w.BotStatus == "LEFT_MAP")
                {
                    int remainSec = Math.Max(0, 15 - w.AbnormalAgeSec);
                    statusStr = string.Format("🚨 อยู่หน้าหลัก Roblox (รอโหลด/ปิดใน {0}s)", remainSec);
                }
                else if (w.BotStatus == "DISCONNECTED")
                {
                    int remainSec = Math.Max(0, 3 - w.AbnormalAgeSec);
                    statusStr = string.Format("🚨 หลุดจากแมพ (กำลังปิดใน {0}s)", remainSec);
                }
                else if (w.BotStatus == "RESTARTING")
                    statusStr = "🔄 Nexus กำลังเปิดเกมใหม่...";
                else if (w.BotStatus == "LOADING")
                    statusStr = "⏳ กำลังเข้าแมพ / รอสคริปต์...";
                else if (w.BotStatus == "TELEPORTING")
                    statusStr = "⚡ กำลังวาร์ปเข้าแมพใหม่...";
                else if (w.BotStatus == "ERR 276")
                    statusStr = "⛔ ซ้อนเครื่องอื่น (Code 276)";
                else if (w.IsHidden)
                    statusStr = "🙈 ซ่อนอยู่ (กำลังฟาร์มปกติ)";
                else if (w.IsMinimized)
                    statusStr = "🔽 ย่ออยู่ (Minimized)";
                else
                    statusStr = "🟢 กำลังฟาร์มปกติ";

                string hbPart = w.HeartbeatAgeSec >= 0 ? (w.HeartbeatAgeSec + "s") : "รอสคริปต์";
                string watchdogCol = string.Format("HB: {0} | รีสตาร์ท: #{1}", hbPart, w.RejoinCount);
                string rectStr = string.Format("{0}, {1}  ({2} x {3})", w.Rect.X, w.Rect.Y, w.Rect.Width, w.Rect.Height);

                string[] cols = new string[]
                {
                    "#" + w.Index,
                    unameDisplay,
                    w.Pid.ToString(),
                    statusStr,
                    watchdogCol,
                    rectStr,
                    w.AccountRef == null ? "—" : (w.AccountRef.RejoinEnabled ? "เปิด (ON)" : "ปิด (OFF)")
                };

                for (int c = 0; c < cols.Length; c++)
                {
                    if (item.SubItems[c].Text != cols[c])
                    {
                        item.SubItems[c].Text = cols[c];
                    }
                }

                if (styleChanged && !countChanged)
                {
                    lvWindows.Invalidate(item.Bounds);
                }
            }

            if (countChanged)
            {
                lvWindows.EndUpdate();
            }
        }

        // =====================================================================
        // CORE ACTIONS: ARRANGE ALL, HIDE ALL, SHOW ALL
        // =====================================================================
        public void ArrangeAllNow()
        {
            windows = WindowEngine.ScanRobloxWindows();
            if (windows.Count == 0)
            {
                SetBanner("⚠️ ยังไม่พบหน้าต่าง Roblox ที่เปิดอยู่ กรุณาเปิดเกมก่อน");
                return;
            }

            MonitorItem mon = SelectedMonitor();
            Rectangle area = useWorkArea ? mon.WorkArea : mon.Bounds;
            bool isCascade = (forcedCols == -1);
            int colsParam = isCascade ? 0 : forcedCols;

            List<Rectangle> rects = WindowEngine.ComputeLayoutRects(
                windows.Count, area, colsParam, isCascade, gapPixels, stretchLastRow);

            for (int i = 0; i < windows.Count && i < rects.Count; i++)
            {
                WindowEngine.PlaceWindow(windows[i], rects[i], borderlessMode, labelTitles);
            }

            if (Visible && WindowState != FormWindowState.Minimized)
            {
                BringToFront();
                Activate();
            }

            RefreshWindows(true);
            int cols, rows;
            WindowEngine.CalculateGridColsRows(windows.Count, colsParam, area, out cols, out rows);
            SetBanner(string.Format("✅ เรียงหน้าต่าง Roblox ทั้งหมด {0} จอ ({1}) บน {2} เรียบร้อย!",
                windows.Count,
                isCascade ? "ซ้อนเฉียง Cascade" : string.Format("ตาราง {0}x{1} ขนาดจอละ {2}x{3} px", cols, rows, rects[0].Width, rects[0].Height),
                mon.Label));
        }

        public void HideAllNow()
        {
            WatchdogEngine.SetAllHidden(true);
            RefreshWindows(true);
            if (windows.Count == 0)
            {
                SetBanner("เปิดโหมดซ่อนจอใหม่อัตโนมัติแล้ว • จอที่เปิดเพิ่มหรือ Rejoin จะซ่อนจาก Taskbar ด้วย");
                return;
            }

            foreach (RobloxWin w in windows)
            {
                WindowEngine.SetStealthHide(w, true);
            }

            RefreshWindows(true);
            SetBanner(string.Format("ซ่อน Roblox {0} จอจากหน้าจอ / Taskbar / Alt+Tab แล้ว • จอที่ Rejoin จะซ่อนให้อัตโนมัติ • F10 แสดงกลับ", windows.Count));
        }

        public void ShowAllNow()
        {
            WatchdogEngine.SetAllHidden(false);
            RefreshWindows(true);
            if (windows.Count == 0)
            {
                SetBanner("⚠️ ไม่พบหน้าต่าง Roblox");
                return;
            }

            foreach (RobloxWin w in windows)
            {
                WindowEngine.SetStealthHide(w, false);
            }

            if (Visible && WindowState != FormWindowState.Minimized)
            {
                BringToFront();
                Activate();
            }

            RefreshWindows(true);
            SetBanner(string.Format("👁️ ยกเลิกซ่อนทั้งหมดแล้ว! แสดงหน้าต่าง Roblox ครบ {0} จอ", windows.Count));
        }

        public void ToggleHideAllNow()
        {
            windows = WindowEngine.ScanRobloxWindows();
            if (windows.Count == 0) return;

            bool anyVisible = false;
            foreach (RobloxWin w in windows)
            {
                if (!w.IsHidden) { anyVisible = true; break; }
            }

            if (anyVisible) HideAllNow();
            else ShowAllNow();
        }

        private void ToggleHideSelected()
        {
            if (lvWindows.SelectedItems.Count == 0)
            {
                SetBanner("⚠️ กรุณาคลิกเลือกจอในตารางด้านล่างก่อน (หรือกดปุ่มด้านบนเพื่อซ่อน/แสดงทุกจอ)");
                return;
            }

            foreach (ListViewItem it in lvWindows.SelectedItems)
            {
                RobloxWin w = it.Tag as RobloxWin;
                if (w != null)
                {
                    WindowEngine.SetStealthHide(w, !w.IsHidden);
                }
            }
            RefreshWindows(true);
        }

        private void FocusSelected()
        {
            if (lvWindows.SelectedItems.Count == 0) return;
            RobloxWin w = lvWindows.SelectedItems[0].Tag as RobloxWin;
            if (w == null) return;
            WindowEngine.SetStealthHide(w, false);
            if (Native.IsIconic(w.Hwnd)) Native.ShowWindow(w.Hwnd, Native.SW_RESTORE);
            Native.SetForegroundWindow(w.Hwnd);
            RefreshWindows(true);
        }

        private void KillSelected()
        {
            if (lvWindows.SelectedItems.Count == 0) return;
            int cnt = lvWindows.SelectedItems.Count;
            if (MessageBox.Show(this,
                string.Format("ต้องการปิดหน้าต่าง Roblox ที่เลือกจำนวน {0} จอ ใช่หรือไม่?", cnt),
                "ยืนยันการปิด Roblox", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            foreach (ListViewItem it in lvWindows.SelectedItems)
            {
                RobloxWin w = it.Tag as RobloxWin;
                if (w != null)
                {
                    try
                    {
                        using (Process p = Process.GetProcessById(w.Pid))
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }
            Thread.Sleep(250);
            RefreshWindows(true);
            SetBanner(string.Format("❌ ปิดหน้าต่าง Roblox ที่เลือก {0} จอเรียบร้อย", cnt));
        }

        // =====================================================================
        // LIVE MONITOR PREVIEW RENDERER
        // =====================================================================
        private void PaintMonitorPreview(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(9, 13, 20));

            Rectangle pad = new Rectangle(12, 28, previewBox.Width - 24, previewBox.Height - 40);
            using (Font tf = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "ตัวอย่างการจัดวาง • คลิกเพื่อเลือกจอ",
                    tf, new Point(10, 6), Color.FromArgb(148, 163, 184));
            }

            MonitorItem mon = SelectedMonitor();
            Rectangle full = mon.Bounds;
            Rectangle area = useWorkArea ? mon.WorkArea : mon.Bounds;

            double scale = Math.Min((double)pad.Width / Math.Max(1, full.Width), (double)pad.Height / Math.Max(1, full.Height));
            int sw = (int)(full.Width * scale);
            int sh = (int)(full.Height * scale);
            int ox = pad.Left + (pad.Width - sw) / 2;
            int oy = pad.Top + (pad.Height - sh) / 2;

            Rectangle screenRect = new Rectangle(ox, oy, sw, sh);
            using (GraphicsPath sp = RoundedRect(screenRect, 8))
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(15, 23, 42)))
                    g.FillPath(sb, sp);
                using (Pen pen = new Pen(Color.FromArgb(51, 65, 85), 1.5f))
                    g.DrawPath(pen, sp);
            }

            int n = windows.Count > 0 ? windows.Count : 7;
            bool isSample = (windows.Count == 0);
            bool isCascade = (forcedCols == -1);
            int colsParam = isCascade ? 0 : forcedCols;

            List<Rectangle> rects = WindowEngine.ComputeLayoutRects(n, area, colsParam, isCascade, gapPixels, stretchLastRow);

            for (int i = 0; i < rects.Count; i++)
            {
                Rectangle r = rects[i];
                int cx = ox + (int)((r.X - full.X) * scale);
                int cy = oy + (int)((r.Y - full.Y) * scale);
                int cw = Math.Max(12, (int)(r.Width * scale) - 2);
                int ch = Math.Max(10, (int)(r.Height * scale) - 2);

                Rectangle cell = new Rectangle(cx + 1, cy + 1, cw, ch);
                bool hidden = !isSample && i < windows.Count && windows[i].IsHidden;

                Color fillCol = isSample
                    ? Color.FromArgb(45, 56, 189, 248)
                    : (hidden ? Color.FromArgb(85, 245, 158, 11) : Color.FromArgb(95, 14, 165, 233));
                Color borderCol = isSample
                    ? Color.FromArgb(120, 56, 189, 248)
                    : (hidden ? Color.FromArgb(251, 191, 36) : Color.FromArgb(56, 189, 248));

                using (GraphicsPath cp = RoundedRect(cell, 5))
                {
                    using (SolidBrush cb = new SolidBrush(fillCol))
                        g.FillPath(cb, cp);
                    using (Pen cpen = new Pen(borderCol, 1.3f))
                        g.DrawPath(cpen, cp);
                }

                string unameShort = (!isSample && i < windows.Count && !string.IsNullOrEmpty(windows[i].Username))
                    ? (" " + windows[i].Username)
                    : "";
                string label = string.Format("#{0}{1}{2}", i + 1, unameShort, hidden ? " (ซ่อน)" : "");
                using (Font cf = new Font("Segoe UI", 8.0f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, label, cf, cell, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private void OnPreviewClick(object sender, MouseEventArgs e)
        {
            if (windows.Count == 0) return;
            MonitorItem mon = SelectedMonitor();
            Rectangle full = mon.Bounds;
            Rectangle area = useWorkArea ? mon.WorkArea : mon.Bounds;
            Rectangle pad = new Rectangle(12, 28, previewBox.Width - 24, previewBox.Height - 40);

            double scale = Math.Min((double)pad.Width / Math.Max(1, full.Width), (double)pad.Height / Math.Max(1, full.Height));
            int sw = (int)(full.Width * scale);
            int sh = (int)(full.Height * scale);
            int ox = pad.Left + (pad.Width - sw) / 2;
            int oy = pad.Top + (pad.Height - sh) / 2;

            bool isCascade = (forcedCols == -1);
            int colsParam = isCascade ? 0 : forcedCols;
            List<Rectangle> rects = WindowEngine.ComputeLayoutRects(windows.Count, area, colsParam, isCascade, gapPixels, stretchLastRow);

            for (int i = rects.Count - 1; i >= 0 && i < windows.Count; i--)
            {
                Rectangle r = rects[i];
                Rectangle cell = new Rectangle(
                    ox + (int)((r.X - full.X) * scale),
                    oy + (int)((r.Y - full.Y) * scale),
                    Math.Max(12, (int)(r.Width * scale)),
                    Math.Max(10, (int)(r.Height * scale)));
                if (cell.Contains(e.Location))
                {
                    RobloxWin w = windows[i];
                    WindowEngine.SetStealthHide(w, false);
                    if (Native.IsIconic(w.Hwnd)) Native.ShowWindow(w.Hwnd, Native.SW_RESTORE);
                    Native.SetForegroundWindow(w.Hwnd);
                    SetBanner(string.Format("🎯 ดึงจอ #{0} (PID: {1}) ขึ้นมาด้านหน้าสุดแล้ว", w.Index, w.Pid));
                    RefreshWindows(true);
                    break;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (previewOnly) { base.OnFormClosing(e); return; }
            if (closingForUpdate)
            {
                if (scanTimer != null) scanTimer.Stop();
                if (updateTimer != null) updateTimer.Stop();
                if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
                WindowVisibility.Stop(); MultiInstanceUnlocker.Stop(); base.OnFormClosing(e); return;
            }
            // Check if any Roblox windows are still stealth-hidden before exiting
            List<RobloxWin> current = WindowEngine.ScanRobloxWindows();
            int hiddenCount = 0;
            foreach (RobloxWin w in current)
            {
                if (w.IsHidden) hiddenCount++;
            }

            if (hiddenCount > 0)
            {
                DialogResult dr = MessageBox.Show(
                    this,
                    string.Format("ตอนนี้ยังมีหน้าต่าง Roblox ที่ซ่อนอยู่ {0} จอ\n\nกด [Yes] เพื่อแสดงหน้าต่างทั้งหมดกลับมาก่อนปิดโปรแกรม\nกด [No] เพื่อซ่อนต่อไป (เปิดโปรแกรมใหม่มายกเลิกซ่อนทีหลังได้)\nกด [Cancel] เพื่อยกเลิกการปิด", hiddenCount),
                    "มีหน้าต่าง Roblox ซ่อนอยู่",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (dr == DialogResult.Yes)
                {
                    WatchdogEngine.SetAllHidden(false);
                    foreach (RobloxWin w in current)
                    {
                        WindowEngine.SetStealthHide(w, false);
                    }
                }
            }

            try
            {
                Native.UnregisterHotKey(Handle, HK_ARRANGE);
                Native.UnregisterHotKey(Handle, HK_TOGGLE_HIDE);
                Native.UnregisterHotKey(Handle, HK_SHOW_ALL);
            }
            catch { }

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            try { Program.WriteDiagnostic("[Closing] " + DateTime.Now.ToString("HH:mm:ss") + " Reason: " + e.CloseReason.ToString() + "\r\n"); } catch { }
            WindowVisibility.Stop();
            MultiInstanceUnlocker.Stop();
            base.OnFormClosing(e);
        }
    }

    public static class Program
    {
        public static void WriteDiagnostic(string message)
        {
            // Each Windows user writes to their own AppData. Logging must never prevent startup.
            try { File.AppendAllText(Path.Combine(WatchdogEngine.DataDir, "crash.log"), message); }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--apply-update") { Environment.ExitCode = UpdateService.Apply(args[1]); return; }
            InstanceGate gate;
            if (!InstanceGate.TryAcquire(@"Local\NexusRobloxManager.Singleton.v1", out gate))
            {
                Native.PostMessage(new IntPtr(0xffff), Native.ShowManagerMessage, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            using (gate) RunApplication(args);
        }

        private static void RunApplication(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                try { WriteDiagnostic("[ThreadException] " + e.Exception.ToString() + "\r\n"); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try { WriteDiagnostic("[UnhandledException] " + Convert.ToString(e.ExceptionObject) + "\r\n"); } catch { }
            };

            try
            {
                try
                {
                    Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
                }
                catch { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm form = new MainForm();
                if (args != null && args.Length > 0)
                {
                    string cmd = args[0].ToLowerInvariant();
                    if (cmd == "--arrange") form.Shown += delegate { form.ArrangeAllNow(); };
                    else if (cmd == "--hide") form.Shown += delegate { form.HideAllNow(); };
                    else if (cmd == "--show") form.Shown += delegate { form.ShowAllNow(); };
                    else if (cmd == "--repair-duplicates") form.Shown += delegate { WatchdogEngine.RecoverLocalDuplicateAccounts(); };
                }
                WriteDiagnostic("[Main] " + DateTime.Now.ToString("HH:mm:ss") + " Starting form...\r\n");
                Application.Run(form);
                WriteDiagnostic("[Exit] " + DateTime.Now.ToString("HH:mm:ss") + " Application.Run returned normally\r\n");
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                try { WriteDiagnostic("[MainCatch] " + ex.ToString() + "\r\n"); } catch { }
            }
        }
    }
}
