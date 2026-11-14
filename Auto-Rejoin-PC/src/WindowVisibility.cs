using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NexusRobloxManager
{
    public static class WindowVisibility
    {
        private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);
        private static readonly WinEventCallback callback = OnWindowShown;
        private static IntPtr hook;
        private static bool applying;

        [ComImport, Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
        private class TaskbarObject { }
        [ComImport, Guid("56FDF342-FD6D-11D0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
        }

        public static int HiddenStyles(int styles)
        {
            return (styles | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT) & ~Native.WS_EX_APPWINDOW;
        }

        public static bool IsStealthStyleApplied(IntPtr hwnd)
        {
            int styles = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            uint color, flags;
            byte alpha;
            return (styles & Native.WS_EX_TOOLWINDOW) != 0 && (styles & Native.WS_EX_APPWINDOW) == 0 &&
                (styles & Native.WS_EX_TRANSPARENT) != 0 && (styles & Native.WS_EX_LAYERED) != 0 &&
                Native.GetLayeredWindowAttributes(hwnd, out color, out alpha, out flags) && alpha <= 1;
        }

        public static void UpdateTaskbar(IntPtr hwnd, bool hidden)
        {
            ITaskbarList taskbar = null;
            try
            {
                taskbar = (ITaskbarList)new TaskbarObject();
                taskbar.HrInit();
                if (hidden) taskbar.DeleteTab(hwnd); else taskbar.AddTab(hwnd);
            }
            catch { } // Tool-window styles still exclude the window if Explorer is unavailable.
            finally { if (taskbar != null) Marshal.FinalReleaseComObject(taskbar); }
        }

        public static void Start()
        {
            if (hook != IntPtr.Zero) return;
            // EVENT_OBJECT_SHOW, out of context. Notifications are delivered to the manager's UI message loop.
            hook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, callback, 0, 0, 2);
        }

        public static void Stop()
        {
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }

        private static void OnWindowShown(IntPtr eventHook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time)
        {
            if (applying || hwnd == IntPtr.Zero || objectId != 0 || childId != 0 || !Native.IsWindow(hwnd)) return;
            if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return;
            string cls = Native.GetClass(hwnd);
            if (!cls.Equals("WINDOWSCLIENT", StringComparison.OrdinalIgnoreCase) &&
                Native.GetTitle(hwnd).IndexOf("Roblox", StringComparison.OrdinalIgnoreCase) < 0) return;
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                using (Process process = Process.GetProcessById((int)pid))
                {
                    if (!process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase) ||
                        !WatchdogEngine.ShouldHideProcess((int)pid, process.StartTime.ToUniversalTime()) ||
                        IsStealthStyleApplied(hwnd)) return;
                    applying = true;
                    try
                    {
                        WindowEngine.SetStealthHide(new RobloxWin { Pid = (int)pid, Hwnd = hwnd, StartTime = process.StartTime }, true, false);
                    }
                    finally { applying = false; }
                }
            }
            catch { } // The window/process can disappear while a queued show event is delivered.
        }
    }
}
