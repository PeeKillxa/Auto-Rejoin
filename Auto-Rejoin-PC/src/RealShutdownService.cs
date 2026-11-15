using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace NexusRobloxManager
{
    public static class RealShutdownService
    {
        public static RealProcessShutdownResult CloseWithElevation()
        {
            return CloseWithElevation(RealProcessShutdown.CloseAll, RunElevatedHelper);
        }

        internal static RealProcessShutdownResult CloseWithElevation(Func<RealProcessShutdownResult> closeAll, Func<int> elevate)
        {
            RealProcessShutdownResult initial = closeAll();
            if (initial.Success || !initial.NeedsElevation) return initial;
            int code;
            try { code = elevate(); }
            catch (Win32Exception ex)
            {
                return Failed(ex.NativeErrorCode == 1223
                    ? "ยกเลิกการอนุญาตของ Windows แล้ว Real ที่ใช้สิทธิ์แอดมินจึงยังปิดไม่ครบ"
                    : "เปิดตัวช่วยปิด Real ด้วยสิทธิ์แอดมินไม่ได้: " + ex.Message);
            }
            if (code != 0)
                return Failed(code == 4
                    ? "ต้องอนุญาตด้วยบัญชี Windows เดียวกับที่เปิด Nexus เพื่อปิด Real ของบัญชีนี้"
                    : "ตัวช่วยยังปิด Real ไม่ครบ (รหัส " + code + ") กรุณาลองอีกครั้ง");
            // A helper exit code alone is not evidence that Real stayed closed.
            return closeAll();
        }

        private static RealProcessShutdownResult Failed(string message)
        {
            return new RealProcessShutdownResult { Success = false, NeedsElevation = false, RemainingCount = -1, Message = message };
        }

        private static int RunElevatedHelper()
        {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--close-real " + sid);
            start.UseShellExecute = true;
            start.Verb = "runas";
            start.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("ตัวช่วยปิด Real ไม่เริ่มทำงาน");
                if (!process.WaitForExit(30000)) return 5;
                return process.ExitCode;
            }
        }

        public static int RunHelper(string expectedSid)
        {
            try
            {
                if (!String.Equals(WindowsIdentity.GetCurrent().User.Value, expectedSid, StringComparison.Ordinal)) return 4;
                RealProcessShutdownResult result = RealProcessShutdown.CloseAll();
                return result.Success ? 0 : (result.NeedsElevation ? 2 : 3);
            }
            catch { return 3; }
        }
    }
}
