using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace NexusRobloxManager
{
    public sealed class RealLoginSecurityBlockedException : IOException
    {
        public int NativeErrorCode { get; private set; }

        internal RealLoginSecurityBlockedException(int code, Exception inner)
            : base("Windows Security บล็อกไฟล์ Real จึงพักการปรับเวลาล็อกอินรุ่นนี้ไว้ เพื่อไม่สร้างไฟล์ที่ถูกบล็อกซ้ำ", inner)
        {
            NativeErrorCode = code;
        }
    }

    internal static class RealLoginSecurity
    {
        private static readonly object SyncRoot = new object();
        private static readonly HashSet<string> Blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static int GetErrorCode(Exception error)
        {
            if (error == null) return 0;
            Queue<Exception> pending = new Queue<Exception>();
            HashSet<Exception> visited = new HashSet<Exception>();
            pending.Enqueue(error);
            while (pending.Count > 0)
            {
                Exception current = pending.Dequeue();
                if (!visited.Add(current)) continue;
                Win32Exception native = current as Win32Exception;
                if (native != null && (native.NativeErrorCode == 225 || native.NativeErrorCode == 226))
                    return native.NativeErrorCode;
                if (current.HResult == unchecked((int)0x800700e1)) return 225;
                if (current.HResult == unchecked((int)0x800700e2)) return 226;
                if (current.InnerException != null) pending.Enqueue(current.InnerException);
                AggregateException aggregate = current as AggregateException;
                if (aggregate != null)
                    foreach (Exception child in aggregate.InnerExceptions) pending.Enqueue(child);
            }
            return 0;
        }

        private static string MarkerPath(string backupRoot, string buildSha)
        {
            if (String.IsNullOrWhiteSpace(backupRoot)) throw new ArgumentException("Backup root is required", "backupRoot");
            if (buildSha == null || buildSha.Length != 64) throw new ArgumentException("Expected a SHA-256 build identity", "buildSha");
            foreach (char value in buildSha)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F')))
                    throw new ArgumentException("Expected a SHA-256 build identity", "buildSha");
            return Path.Combine(Path.GetFullPath(backupRoot), "security-blocks", buildSha.ToLowerInvariant() + ".txt");
        }

        internal static bool IsBlocked(string backupRoot, string buildSha)
        {
            string marker = MarkerPath(backupRoot, buildSha);
            lock (SyncRoot)
            {
                if (Blocked.Contains(marker)) return true;
                // A marker pauses retries even if its optional explanatory content is damaged.
                if (File.Exists(marker)) { Blocked.Add(marker); return true; }
                return false;
            }
        }

        internal static void ThrowIfBlocked(string backupRoot, string buildSha)
        {
            if (IsBlocked(backupRoot, buildSha)) throw new RealLoginSecurityBlockedException(225, null);
        }

        internal static RealLoginSecurityBlockedException RecordIfBlocked(string backupRoot, string buildSha, Exception failure)
        {
            int code = GetErrorCode(failure);
            if (code == 0) return null;
            string marker = MarkerPath(backupRoot, buildSha);
            lock (SyncRoot)
            {
                Blocked.Add(marker);
                string staging = null;
                try
                {
                    if (!File.Exists(marker))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(marker));
                        staging = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        string text = code.ToString(CultureInfo.InvariantCulture) + "\n" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\n";
                        byte[] bytes = Encoding.UTF8.GetBytes(text);
                        using (FileStream stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                        try { File.Move(staging, marker); }
                        catch (IOException) { if (!File.Exists(marker)) throw; }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally
                {
                    if (staging != null)
                    {
                        try { if (File.Exists(staging)) File.Delete(staging); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
            return new RealLoginSecurityBlockedException(code, failure);
        }
    }
}
