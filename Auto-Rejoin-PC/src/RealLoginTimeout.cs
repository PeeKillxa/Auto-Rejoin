using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NexusRejoinChecks")]

namespace NexusRobloxManager
{
    public sealed class RealLoginTimeoutStatus
    {
        public string Path { get; internal set; }
        public int Minutes { get; internal set; }
        public string Version { get; internal set; }
        public bool IsExtended { get { return Minutes == 15; } }
        public string Description { get { return "Real " + Version + " · รอล็อกอิน Roblox " + Minutes + " นาที"; } }
    }

    // This changes one verified browser-login polling limit. It never handles credentials.
    public static class RealLoginTimeout
    {
        private const string OriginalSha256 = "6166e60d3bbd979fe334b9a3d96bbe846f188db1c52e8b40f309879d2276842e";
        private const int OriginalLength = 23497216;
        // High DWORD of the verified poll-counter initializer; each poll waits 500 ms.
        private const int LimitOffset = 0x2812de;
        private static readonly object SyncRoot = new object();

        internal sealed class Target
        {
            internal string Path, BackupRoot, ExpectedSha256, Version;
            internal int Length, Offset;
            internal Func<bool> IsRunning;
            internal Action BeforeReplace, AfterReplace;
        }

        public static RealLoginTimeoutStatus InspectInstalled()
        {
            return InspectFile(FindInstalled());
        }

        public static RealLoginTimeoutStatus ApplyInstalled(int minutes)
        {
            return ApplyFile(FindInstalled(), minutes);
        }

        private static Target FindInstalled()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string root = System.IO.Path.Combine(local, "Real");
            string selected = null;
            Version newest = null;
            if (Directory.Exists(root))
            {
                foreach (string directory in Directory.GetDirectories(root, "real-*"))
                {
                    Version version;
                    string name = System.IO.Path.GetFileName(directory);
                    if (Version.TryParse(name.Substring(5), out version) &&
                        File.Exists(System.IO.Path.Combine(directory, "Real.exe")) && (newest == null || version > newest))
                    { selected = directory; newest = version; }
                }
            }
            if (selected == null) throw new InvalidOperationException("ไม่พบ Real ที่ติดตั้งใน Windows บัญชีนี้");
            if (newest.ToString() != "3.1.1")
                throw new InvalidOperationException("Real รุ่น " + newest + " ยังไม่รองรับการปรับเวลาล็อกอิน กรุณารอ Nexus รุ่นที่รองรับ");
            return new Target
            {
                Path = System.IO.Path.Combine(selected, "Real.exe"), Version = "3.1.1",
                BackupRoot = System.IO.Path.Combine(local, "NexusRobloxManager", "real-login-backups"),
                ExpectedSha256 = OriginalSha256, Length = OriginalLength, Offset = LimitOffset,
                IsRunning = IsRealRunning
            };
        }

        private static bool IsRealRunning()
        {
            Process[] processes = Process.GetProcessesByName("Real");
            try { return processes.Length > 0; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        private static string Hash(byte[] value)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(value)).Replace("-", "").ToLowerInvariant();
        }

        private static uint ReadLimit(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
        }

        private static void WriteLimit(byte[] bytes, int offset, uint limit)
        {
            for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(limit >> (8 * i));
        }

        private static byte[] ReadVerified(Target target, out int minutes)
        {
            if (target.Version != "3.1.1" || target.Length < 16 || target.Offset < 6 || target.Offset > target.Length - 4)
                throw new InvalidOperationException("Real รุ่นนี้ยังไม่รองรับการปรับเวลาล็อกอิน");
            FileInfo info = new FileInfo(target.Path);
            if (!info.Exists || info.Length != target.Length || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("ไฟล์ Real ไม่ตรงกับรุ่นที่รองรับ จะไม่แก้ไขไฟล์");
            byte[] bytes = File.ReadAllBytes(target.Path);
            if (bytes.Length != target.Length || bytes[0] != 'M' || bytes[1] != 'Z')
                throw new InvalidOperationException("ไฟล์ Real ไม่ตรงกับรุ่นที่รองรับ จะไม่แก้ไขไฟล์");
            uint limit = ReadLimit(bytes, target.Offset);
            if (limit != 240 && limit != 1800)
                throw new InvalidOperationException("ค่ารอล็อกอินใน Real ไม่ใช่ค่าที่ Nexus รองรับ จะไม่แก้ไขไฟล์");
            byte[] original = (byte[])bytes.Clone();
            WriteLimit(original, target.Offset, 240);
            if (!String.Equals(Hash(original), target.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ไฟล์ Real ถูกอัปเดตหรือเปลี่ยนแปลง จะไม่แก้ไขไฟล์รุ่นที่ไม่รู้จัก");
            minutes = limit == 240 ? 2 : 15;
            return bytes;
        }

        private static RealLoginTimeoutStatus Status(Target target, int minutes)
        {
            return new RealLoginTimeoutStatus { Path = target.Path, Minutes = minutes, Version = target.Version };
        }

        internal static RealLoginTimeoutStatus InspectFile(Target target)
        {
            int minutes;
            ReadVerified(target, out minutes);
            return Status(target, minutes);
        }

        private static string BackupPath(Target target)
        {
            return System.IO.Path.Combine(target.BackupRoot, target.ExpectedSha256.ToLowerInvariant(), "Real.exe.original");
        }

        private static void VerifyBackup(Target target, string backup)
        {
            FileInfo file = new FileInfo(backup);
            if (!file.Exists || file.Length != target.Length || (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !String.Equals(Hash(File.ReadAllBytes(backup)), target.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ไฟล์สำรอง Real ไม่ผ่านการตรวจสอบ จะไม่เขียนทับไฟล์สำรองหรือ Real");
        }

        private static void WriteDurable(string path, byte[] bytes)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }

        private static void EnsureBackup(Target target, byte[] original)
        {
            string backup = BackupPath(target);
            if (File.Exists(backup)) { VerifyBackup(target, backup); return; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));
            string staging = backup + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteDurable(staging, original);
                if (!String.Equals(Hash(File.ReadAllBytes(staging)), target.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("ตรวจสอบไฟล์สำรอง Real ไม่สำเร็จ");
                try { File.Move(staging, backup); }
                catch (IOException) { if (!File.Exists(backup)) throw; }
                VerifyBackup(target, backup);
                File.SetAttributes(backup, File.GetAttributes(backup) | FileAttributes.ReadOnly);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }

        private static void RequireStopped(Target target)
        {
            if (target.IsRunning == null || target.IsRunning())
                throw new InvalidOperationException("กรุณาปิด Real ให้หมดก่อนปรับเวลาล็อกอิน แล้วเปิด Real ใหม่หลังปรับเสร็จ");
        }

        internal static RealLoginTimeoutStatus ApplyFile(Target target, int minutes)
        {
            if (minutes != 2 && minutes != 15) throw new ArgumentOutOfRangeException("minutes", "รองรับเวลารอ 2 หรือ 15 นาทีเท่านั้น");
            lock (SyncRoot)
            {
                using (Mutex mutex = new Mutex(false, "Local\\NexusRealLoginTimeoutPatch"))
                {
                    bool owns;
                    try { owns = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { owns = true; }
                    if (!owns) throw new InvalidOperationException("มีการปรับเวลาล็อกอิน Real อยู่แล้ว กรุณารอสักครู่");
                    try { return ApplyLocked(target, minutes); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
        }

        private static RealLoginTimeoutStatus ApplyLocked(Target target, int minutes)
        {
            RequireStopped(target);
            int oldMinutes;
            byte[] before = ReadVerified(target, out oldMinutes);
            string backup = BackupPath(target);
            if (File.Exists(backup)) VerifyBackup(target, backup);
            if (oldMinutes == minutes) return Status(target, minutes);
            byte[] original = (byte[])before.Clone();
            WriteLimit(original, target.Offset, 240);
            EnsureBackup(target, original);
            byte[] after = (byte[])original.Clone();
            WriteLimit(after, target.Offset, minutes == 15 ? 1800U : 240U);
            string beforeHash = Hash(before), afterHash = Hash(after);
            string suffix = ".nexus-" + Guid.NewGuid().ToString("N");
            string staging = target.Path + suffix + ".tmp";
            string rollback = target.Path + suffix + ".rollback";
            bool replaced = false;
            try
            {
                WriteDurable(staging, after);
                if (Hash(File.ReadAllBytes(staging)) != afterHash) throw new IOException("ตรวจสอบไฟล์ Real ที่เตรียมไว้ไม่สำเร็จ");
                if (target.BeforeReplace != null) target.BeforeReplace();
                RequireStopped(target);
                if (Hash(File.ReadAllBytes(target.Path)) != beforeHash)
                    throw new IOException("Real เปลี่ยนแปลงระหว่างเตรียมไฟล์ ยกเลิกการปรับเวลาแล้ว");
                VerifyBackup(target, backup);
                File.Replace(staging, target.Path, rollback, false);
                replaced = true;
                if (Hash(File.ReadAllBytes(rollback)) != beforeHash)
                    throw new IOException("Real เปลี่ยนแปลงระหว่างแทนไฟล์ กำลังคืนไฟล์ก่อนหน้า");
                if (target.AfterReplace != null) target.AfterReplace();
                if (Hash(File.ReadAllBytes(target.Path)) != afterHash)
                    throw new IOException("ตรวจสอบ Real หลังแทนไฟล์ไม่สำเร็จ");
                File.Delete(rollback);
                return Status(target, minutes);
            }
            catch (Exception failure)
            {
                if (replaced)
                {
                    try
                    {
                        // Preserve concurrent updates: only roll back our exact replacement bytes.
                        if (!File.Exists(rollback) || Hash(File.ReadAllBytes(target.Path)) != afterHash)
                            throw new IOException("ไฟล์ Real เปลี่ยนไปแล้ว จึงไม่เขียนทับการเปลี่ยนแปลงนั้น");
                        string recoveryHash = Hash(File.ReadAllBytes(rollback));
                        File.Replace(rollback, target.Path, null, false);
                        if (Hash(File.ReadAllBytes(target.Path)) != recoveryHash)
                            throw new IOException("ตรวจสอบไฟล์ Real ที่คืนค่าไม่สำเร็จ");
                    }
                    catch (Exception recovery)
                    {
                        throw new IOException("ปรับเวลาไม่สำเร็จ ไฟล์ก่อนหน้ายังเก็บไว้ที่ " + rollback + " · " + recovery.Message, failure);
                    }
                }
                throw;
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
    }
}
