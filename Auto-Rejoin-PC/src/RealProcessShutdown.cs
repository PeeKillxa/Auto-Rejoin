using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NexusRobloxManager
{
    public sealed class RealProcessShutdownResult
    {
        public bool Success { get; set; }
        public bool NeedsElevation { get; set; }
        public int ClosedCount { get; set; }
        public int RemainingCount { get; set; }
        public string Message { get; set; }
    }

    public static class RealProcessShutdown
    {
        internal sealed class ProcRecord
        {
            internal int Pid, SessionId;
            internal string ImagePath;
            internal long StartTicks;
            internal bool Unknown, Denied;
        }

        internal enum Outcome { Completed, Gone, IdentityChanged, Denied, Failed }

        internal interface IBackend
        {
            List<ProcRecord> Scan();
            Outcome Gentle(ProcRecord process);
            Outcome Kill(ProcRecord process);
            long NowMilliseconds { get; }
            void Delay(int milliseconds);
        }

        public static RealProcessShutdownResult CloseAll()
        {
            int session;
            using (Process current = Process.GetCurrentProcess()) session = current.SessionId;
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Real");
            return Run(new WindowsBackend(root), root, session);
        }

        internal static bool IsAllowedImage(string imagePath, string installRoot)
        {
            if (String.IsNullOrEmpty(imagePath) || String.IsNullOrEmpty(installRoot)) return false;
            try
            {
                string root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(imagePath);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                string[] parts = full.Substring(root.Length).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if ((parts.Length != 2 && parts.Length != 3) || !parts[0].StartsWith("real-", StringComparison.OrdinalIgnoreCase)) return false;
                Version version;
                if (!Version.TryParse(parts[0].Substring(5), out version)) return false;
                if (parts.Length == 3 && !parts[1].Equals("bin", StringComparison.OrdinalIgnoreCase)) return false;
                return parts[parts.Length - 1].Equals("Real.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        private static string Key(ProcRecord process)
        { return process.Pid.ToString() + ":" + process.StartTicks.ToString(); }

        private static RealProcessShutdownResult Finish(bool success, bool denied, HashSet<string> seen,
            List<ProcRecord> remaining, string message)
        {
            HashSet<string> active = new HashSet<string>();
            foreach (ProcRecord process in remaining) active.Add(Key(process));
            int closed = 0;
            foreach (string key in seen) if (!active.Contains(key)) closed++;
            return new RealProcessShutdownResult
            { Success = success, NeedsElevation = denied, ClosedCount = closed, RemainingCount = remaining.Count, Message = message };
        }

        internal static RealProcessShutdownResult Run(IBackend backend, string installRoot, int sessionId)
        {
            if (backend == null) throw new ArgumentNullException("backend");
            long began = backend.NowMilliseconds;
            HashSet<string> seen = new HashSet<string>(), requested = new HashSet<string>();
            List<ProcRecord> selected = new List<ProcRecord>();
            bool emptyOnce = false;
            while (true)
            {
                List<ProcRecord> scan;
                try { scan = backend.Scan(); }
                catch (Exception ex)
                {
                    if (!(ex is Win32Exception) && !(ex is InvalidOperationException) && !(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                    Win32Exception native = ex as Win32Exception;
                    return Finish(false, ex is UnauthorizedAccessException || (native != null && native.NativeErrorCode == 5), seen, selected,
                        "อ่านรายการโปรเซส Real ไม่สำเร็จ จึงยังยืนยันว่าปิดหมดไม่ได้");
                }
                if (scan == null) return Finish(false, false, seen, selected, "อ่านรายการโปรเซส Real ไม่สำเร็จ");
                selected = new List<ProcRecord>();
                bool unknown = false, denied = false;
                foreach (ProcRecord process in scan)
                {
                    if (process == null || process.SessionId != sessionId) continue;
                    if (process.Unknown || String.IsNullOrEmpty(process.ImagePath))
                    { selected.Add(process); unknown = true; denied |= process.Denied; continue; }
                    if (!IsAllowedImage(process.ImagePath, installRoot)) continue;
                    selected.Add(process); seen.Add(Key(process));
                }
                if (unknown)
                    return Finish(false, denied, seen, selected, denied ?
                        "Real ใช้สิทธิ์แอดมิน ต้องอนุญาตสิทธิ์แอดมินเพื่อปิดโปรเซสที่ค้าง" :
                        "ตรวจสอบตัวตนโปรเซส Real บางตัวไม่ได้ จึงยังไม่สั่งปิดโปรเซสนั้น");
                if (selected.Count == 0)
                {
                    if (emptyOnce) return Finish(true, false, seen, selected, seen.Count == 0 ? "Real ปิดอยู่แล้ว" : "ปิด Real ครบทุกโปรเซสแล้ว");
                    emptyOnce = true;
                }
                else
                {
                    emptyOnce = false;
                    foreach (ProcRecord process in selected)
                    {
                        if (backend.NowMilliseconds - began >= 10000)
                            return Finish(false, false, seen, selected, "Real ยังมีโปรเซสค้างหลังรอปิด กรุณาลองปิดอีกครั้ง");
                        Outcome outcome;
                        if (backend.NowMilliseconds - began < 1500)
                        {
                            if (!requested.Add(Key(process))) continue;
                            outcome = backend.Gentle(process);
                            // A window may reject WM_CLOSE while its background process can still be terminated normally.
                            if (outcome == Outcome.Denied) continue;
                        }
                        else outcome = backend.Kill(process);
                        if (outcome == Outcome.Denied)
                            return Finish(false, true, seen, selected, "Real ใช้สิทธิ์แอดมิน ต้องอนุญาตสิทธิ์แอดมินเพื่อปิดโปรเซสที่ค้าง");
                        if (outcome == Outcome.Failed)
                            return Finish(false, false, seen, selected, "ปิด Real บางโปรเซสไม่สำเร็จ ยังไม่ยืนยันว่าปิดครบ");
                        // Even a successful termination must be confirmed with a fresh process inventory.
                    }
                }
                if (backend.NowMilliseconds - began >= 10000)
                    return Finish(false, false, seen, selected, "Real ยังมีโปรเซสค้างหลังรอปิด กรุณาลองปิดอีกครั้ง");
                backend.Delay(200);
            }
        }

        internal sealed class WindowsBackend : IBackend
        {
            private const uint QueryLimited = 0x1000, Terminate = 1, Synchronize = 0x100000;
            private const uint StillActive = 259, WaitTimeout = 258;
            private readonly Stopwatch timer = Stopwatch.StartNew();
            private readonly string root;
            internal WindowsBackend(string root) { this.root = root; }
            public long NowMilliseconds { get { return timer.ElapsedMilliseconds; } }
            public void Delay(int milliseconds) { Thread.Sleep(milliseconds); }

            public List<ProcRecord> Scan()
            {
                List<ProcRecord> result = new List<ProcRecord>();
                Process[] candidates = Process.GetProcessesByName("Real");
                try
                {
                    foreach (Process candidate in candidates)
                    {
                        int session;
                        if (!ProcessIdToSessionId((uint)candidate.Id, out session))
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error == 87) continue;
                            throw new Win32Exception(error);
                        }
                        ProcRecord record = new ProcRecord { Pid = candidate.Id, SessionId = session };
                        IntPtr handle = OpenProcess(QueryLimited, false, candidate.Id);
                        if (handle == IntPtr.Zero)
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error == 87) continue;
                            record.Unknown = true; record.Denied = error == 5;
                        }
                        else
                        {
                            try
                            {
                                uint state;
                                if (GetExitCodeProcess(handle, out state) && state != StillActive) continue;
                                int error;
                                if (!ReadIdentity(handle, record, out error))
                                { record.Unknown = true; record.Denied = error == 5; }
                            }
                            finally { CloseHandle(handle); }
                        }
                        result.Add(record);
                    }
                }
                finally { foreach (Process candidate in candidates) candidate.Dispose(); }
                return result;
            }

            public Outcome Gentle(ProcRecord expected)
            {
                IntPtr handle;
                Outcome outcome = OpenVerified(expected, QueryLimited, out handle);
                if (outcome != Outcome.Completed) return outcome;
                try
                {
                    bool denied = false;
                    EnumWindows(delegate(IntPtr window, IntPtr unused)
                    {
                        uint owner;
                        GetWindowThreadProcessId(window, out owner);
                        uint state;
                        if (owner == expected.Pid && GetExitCodeProcess(handle, out state) && state == StillActive &&
                            !PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 5) denied = true;
                        return true;
                    }, IntPtr.Zero);
                    return denied ? Outcome.Denied : Outcome.Completed;
                }
                finally { CloseHandle(handle); }
            }

            public Outcome Kill(ProcRecord expected)
            {
                IntPtr handle;
                Outcome outcome = OpenVerified(expected, QueryLimited | Terminate | Synchronize, out handle);
                if (outcome != Outcome.Completed) return outcome;
                try
                {
                    if (!TerminateProcess(handle, 0))
                    {
                        int error = Marshal.GetLastWin32Error();
                        uint state;
                        if (GetExitCodeProcess(handle, out state) && state != StillActive) return Outcome.Gone;
                        return error == 5 ? Outcome.Denied : Outcome.Failed;
                    }
                    uint wait = WaitForSingleObject(handle, 500);
                    return wait == 0 ? Outcome.Completed : wait == WaitTimeout ? Outcome.Completed : Outcome.Failed;
                }
                finally { CloseHandle(handle); }
            }

            private Outcome OpenVerified(ProcRecord expected, uint rights, out IntPtr handle)
            {
                handle = OpenProcess(rights, false, expected.Pid);
                if (handle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    return error == 87 ? Outcome.Gone : error == 5 ? Outcome.Denied : Outcome.Failed;
                }
                ProcRecord actual = new ProcRecord();
                uint state;
                int identityError;
                Outcome result;
                if (GetExitCodeProcess(handle, out state) && state != StillActive) result = Outcome.Gone;
                else if (!ReadIdentity(handle, actual, out identityError)) result = identityError == 5 ? Outcome.Denied : Outcome.Failed;
                else if (actual.StartTicks != expected.StartTicks || !String.Equals(actual.ImagePath, expected.ImagePath, StringComparison.OrdinalIgnoreCase) ||
                    !IsAllowedImage(actual.ImagePath, root)) result = Outcome.IdentityChanged;
                else return Outcome.Completed;
                CloseHandle(handle); handle = IntPtr.Zero;
                return result;
            }

            private static bool ReadIdentity(IntPtr handle, ProcRecord record, out int error)
            {
                StringBuilder name = new StringBuilder(32768);
                int length = name.Capacity;
                FileTime created, exited, kernel, user;
                if (!QueryFullProcessImageName(handle, 0, name, ref length) || !GetProcessTimes(handle, out created, out exited, out kernel, out user))
                { error = Marshal.GetLastWin32Error(); return false; }
                record.ImagePath = name.ToString();
                record.StartTicks = ((long)created.High << 32) | created.Low;
                error = 0; return true;
            }

            [StructLayout(LayoutKind.Sequential)] private struct FileTime { internal uint Low, High; }
            private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder name, ref int length);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr handle, out FileTime created, out FileTime exited, out FileTime kernel, out FileTime user);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(uint pid, out int session);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr handle, uint exitCode);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, int milliseconds);
            [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
            [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        }
    }
}
