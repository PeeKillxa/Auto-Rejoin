using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NexusRobloxManager
{
    public sealed class RealAccountImportIdentity
    {
        public long UserId { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Created { get; set; }
        public string AvatarUrl { get; set; }
    }

    public sealed class RealAccountImportResult
    {
        public long UserId { get; internal set; }
        public string Username { get; internal set; }
        public bool UpdatedExisting { get; internal set; }
        public string BackupPath { get; internal set; }
    }

    // Compatible local-data integration, not an official Real import API.
    // The caller validates the session with Roblox and obtains explicit account review before importing.
    public static class RealAccountImport
    {
        private const int MaxAccountsBytes = 16 * 1024 * 1024;
        private const string VaultEntropy = "Real-UI account vault key v1";
        internal const string BackupEntropy = "Nexus Real account import backup v1";
        private static readonly object SyncRoot = new object();

        internal sealed class Target
        {
            internal string Root, BackupRoot;
            internal Func<bool> IsRunning;
            internal Func<DateTime> NowUtc;
            internal Action BeforeReplace, AfterReplace;
        }

        private sealed class Snapshot : IDisposable
        {
            internal byte[] AccountsBytes, SettingsBytes, VaultBytes, Key;
            internal List<Dictionary<string, object>> Accounts;
            public void Dispose()
            {
                if (Key != null) Array.Clear(Key, 0, Key.Length);
                if (AccountsBytes != null) Array.Clear(AccountsBytes, 0, AccountsBytes.Length);
            }
        }

        private static Target Installed()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string installRoot = Path.Combine(local, "Real");
            Version latest = null;
            if (Directory.Exists(installRoot))
            {
                foreach (string directory in Directory.GetDirectories(installRoot, "real-*"))
                {
                    Version version;
                    string name = Path.GetFileName(directory);
                    if (Version.TryParse(name.Substring(5), out version) && (latest == null || version > latest)) latest = version;
                }
            }
            ValidateInstalledVersion(latest);
            return new Target {
                Root = Path.Combine(local, "Real", "data"),
                BackupRoot = Path.Combine(local, "NexusRobloxManager", "real-account-import-backups"),
                IsRunning = delegate {
                    Process[] processes = Process.GetProcessesByName("Real");
                    try { return processes.Length != 0; }
                    finally { foreach (Process process in processes) process.Dispose(); }
                },
                NowUtc = delegate { return DateTime.UtcNow; }
            };
        }

        internal static void ValidateInstalledVersion(Version version)
        {
            if (version == null) throw new InvalidOperationException("ไม่พบรุ่น Real ที่ติดตั้ง จึงยังนำเข้าบัญชีไม่ได้");
            if (version != new Version(3, 1, 1) && version != new Version(3, 2, 1))
                throw new InvalidOperationException("การนำเข้าบัญชีรองรับ Real 3.1.1 และ 3.2.1 · พบรุ่น " + version + " กรุณารอ Nexus รุ่นที่รองรับ");
        }

        public static void EnsureCanImport()
        { EnsureCanImportFile(Installed()); }

        internal static void EnsureCanImportFile(Target target)
        { using (Snapshot ignored = Load(target)) { } }

        public static RealAccountImportResult Import(string cookie, RealAccountImportIdentity identity)
        { return ImportFile(Installed(), cookie, identity); }

        private static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = MaxAccountsBytes * 2, RecursionLimit = 100 }; }

        private static byte[] ReadBounded(string path, int maximum)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length > maximum || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("ข้อมูล Real ไม่พร้อมหรือเป็นรูปแบบที่ยังไม่รองรับ กรุณาตั้งค่า Real ให้เสร็จก่อน");
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length > maximum) throw new InvalidOperationException("ข้อมูล Real มีขนาดเกินที่รองรับ");
            return bytes;
        }

        private static object Parse(byte[] bytes)
        {
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes);
                if (json.Length > 0 && json[0] == '\ufeff') json = json.Substring(1);
                return Serializer().DeserializeObject(json);
            }
            catch (ArgumentException) { throw new InvalidOperationException("อ่านรูปแบบข้อมูล Real ไม่สำเร็จ จะไม่เขียนทับข้อมูลเดิม"); }
            catch (InvalidOperationException) { throw new InvalidOperationException("อ่านรูปแบบข้อมูล Real ไม่สำเร็จ จะไม่เขียนทับข้อมูลเดิม"); }
        }

        private static long Number(object value)
        {
            if (!(value is int) && !(value is long)) throw new InvalidOperationException("รูปแบบตัวเลขในบัญชี Real ยังไม่รองรับ");
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static Snapshot Load(Target target)
        {
            Snapshot snapshot = new Snapshot();
            try
            {
                snapshot.SettingsBytes = ReadBounded(Path.Combine(target.Root, "enc_settings.json"), 1024 * 1024);
                Dictionary<string, object> settings = Parse(snapshot.SettingsBytes) as Dictionary<string, object>;
                object initialized, enabled;
                if (settings == null || !settings.TryGetValue("initialized", out initialized) || !(initialized is bool) || !(bool)initialized ||
                    !settings.TryGetValue("enabled", out enabled) || !(enabled is bool) || (bool)enabled)
                    throw new InvalidOperationException("รูปแบบคลังบัญชี Real นี้ยังไม่รองรับการนำเข้า กรุณาเพิ่มบัญชีผ่าน Real");
                snapshot.VaultBytes = ReadBounded(Path.Combine(target.Root, "account-vault.dpapi"), 64 * 1024);
                try { snapshot.Key = ProtectedData.Unprotect(snapshot.VaultBytes, Encoding.UTF8.GetBytes(VaultEntropy), DataProtectionScope.CurrentUser); }
                catch (CryptographicException) { throw new InvalidOperationException("เปิดคลังบัญชี Real ของ Windows บัญชีนี้ไม่ได้ จะไม่เปลี่ยนข้อมูลเดิม"); }
                if (snapshot.Key.Length != 32) throw new InvalidOperationException("รูปแบบกุญแจคลัง Real ยังไม่รองรับ");
                snapshot.AccountsBytes = ReadBounded(Path.Combine(target.Root, "accounts.json"), MaxAccountsBytes);
                object[] array = Parse(snapshot.AccountsBytes) as object[];
                if (array == null) throw new InvalidOperationException("รูปแบบรายการบัญชี Real ยังไม่รองรับ จะไม่เปลี่ยนข้อมูลเดิม");
                snapshot.Accounts = new List<Dictionary<string, object>>();
                HashSet<long> ids = new HashSet<long>();
                foreach (object item in array)
                {
                    Dictionary<string, object> row = item as Dictionary<string, object>;
                    object user, cookie, id, sort;
                    if (row == null || !row.TryGetValue("user_id", out user) || Number(user) <= 0 || !ids.Add(Number(user)) ||
                        !row.TryGetValue("cookie", out cookie) || !(cookie is string) ||
                        !row.TryGetValue("id", out id) || !(id is string) || String.IsNullOrWhiteSpace((string)id) ||
                        !row.TryGetValue("sort_index", out sort) || Number(sort) < 0)
                        throw new InvalidOperationException("ข้อมูลบัญชี Real ไม่สมบูรณ์หรือมีบัญชีซ้ำ จะไม่เปลี่ยนข้อมูลเดิม");
                    string stored = (string)cookie;
                    if (!stored.StartsWith("dev:v1:", StringComparison.Ordinal) && !stored.StartsWith("_|WARNING:", StringComparison.Ordinal))
                        throw new InvalidOperationException("พบรูปแบบคลังบัญชี Real ที่ยังไม่รองรับ จะไม่เปลี่ยนข้อมูลเดิม");
                    if (stored.StartsWith("dev:v1:", StringComparison.Ordinal))
                    {
                        byte[] plain = null;
                        try
                        {
                            string[] parts = stored.Split(':');
                            if (parts.Length != 4) throw new FormatException();
                            plain = RealAccountLauncher.DecryptGcm(snapshot.Key, Convert.FromBase64String(parts[2]), Convert.FromBase64String(parts[3]));
                            if (!Encoding.UTF8.GetString(plain).StartsWith("_|WARNING:", StringComparison.Ordinal)) throw new CryptographicException();
                        }
                        catch (FormatException) { throw new InvalidOperationException("ข้อมูลคลังบัญชี Real เสียหาย จะไม่เปลี่ยนข้อมูลเดิม"); }
                        catch (CryptographicException) { throw new InvalidOperationException("ตรวจสอบคลังบัญชี Real ไม่สำเร็จ จะไม่เปลี่ยนข้อมูลเดิม"); }
                        finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
                    }
                    snapshot.Accounts.Add(row);
                }
                return snapshot;
            }
            catch { snapshot.Dispose(); throw; }
        }

        private static void Validate(string cookie, RealAccountImportIdentity identity)
        {
            if (String.IsNullOrEmpty(cookie) || cookie.Length < 64 || cookie.Length > 20000 || !cookie.StartsWith("_|WARNING:", StringComparison.Ordinal))
                throw new InvalidOperationException("เซสชัน Roblox ไม่สมบูรณ์ กรุณาล็อกอินใหม่");
            foreach (char value in cookie) if (value < 0x21 || value > 0x7e) throw new InvalidOperationException("รูปแบบเซสชัน Roblox ยังไม่รองรับ");
            if (identity == null || identity.UserId <= 0 || String.IsNullOrWhiteSpace(identity.Username) || identity.Username.Length > 100)
                throw new InvalidOperationException("ข้อมูลบัญชี Roblox ยังไม่ได้รับการยืนยัน");
            foreach (char value in identity.Username) if (Char.IsControl(value)) throw new InvalidOperationException("ชื่อบัญชี Roblox ไม่ถูกต้อง");
            if (identity.DisplayName != null && identity.DisplayName.Length > 200) throw new InvalidOperationException("ข้อมูลชื่อแสดง Roblox ไม่ถูกต้อง");
            DateTimeOffset created;
            if (identity.Created == null || identity.Created.Length > 100 || !DateTimeOffset.TryParse(identity.Created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out created))
                throw new InvalidOperationException("ข้อมูลวันสร้างบัญชี Roblox ไม่ถูกต้อง");
            Uri avatar;
            if (!String.IsNullOrEmpty(identity.AvatarUrl) && (identity.AvatarUrl.Length > 2048 || !Uri.TryCreate(identity.AvatarUrl, UriKind.Absolute, out avatar) || avatar.Scheme != "https"))
                throw new InvalidOperationException("ข้อมูลรูปบัญชี Roblox ไม่ถูกต้อง");
        }

        private static void RequireStopped(Target target)
        { if (target.IsRunning == null || target.IsRunning()) throw new InvalidOperationException("กรุณาปิด Real ให้ครบก่อนนำเข้าบัญชี"); }

        private static string Hash(byte[] bytes)
        { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        private static void WriteDurable(string path, byte[] bytes)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }

        private static void Clean(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Backup(Target target, byte[] original)
        {
            Directory.CreateDirectory(target.BackupRoot);
            string path = Path.Combine(target.BackupRoot, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".dpapi");
            byte[] protectedBytes = ProtectedData.Protect(original, Encoding.UTF8.GetBytes(BackupEntropy), DataProtectionScope.CurrentUser);
            byte[] verified = null;
            try
            {
                WriteDurable(path, protectedBytes);
                verified = ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes(BackupEntropy), DataProtectionScope.CurrentUser);
                if (Hash(verified) != Hash(original)) throw new IOException("ตรวจสอบไฟล์สำรองบัญชีไม่สำเร็จ");
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                return path;
            }
            catch { Clean(path); throw; }
            finally { Array.Clear(protectedBytes, 0, protectedBytes.Length); if (verified != null) Array.Clear(verified, 0, verified.Length); }
        }

        private static void VerifySources(Target target, Snapshot snapshot)
        {
            if (Hash(ReadBounded(Path.Combine(target.Root, "accounts.json"), MaxAccountsBytes)) != Hash(snapshot.AccountsBytes) ||
                Hash(ReadBounded(Path.Combine(target.Root, "enc_settings.json"), 1024 * 1024)) != Hash(snapshot.SettingsBytes) ||
                Hash(ReadBounded(Path.Combine(target.Root, "account-vault.dpapi"), 64 * 1024)) != Hash(snapshot.VaultBytes))
                throw new IOException("ข้อมูล Real เปลี่ยนระหว่างนำเข้า ยกเลิกเพื่อรักษาข้อมูลล่าสุดแล้ว");
        }

        internal static RealAccountImportResult ImportFile(Target target, string cookie, RealAccountImportIdentity identity)
        {
            Validate(cookie, identity);
            lock (SyncRoot)
            using (Mutex mutex = new Mutex(false, "Local\\NexusRealAccountImport"))
            {
                bool owns;
                try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
                if (!owns) throw new InvalidOperationException("มีการนำเข้าบัญชี Real อยู่แล้ว กรุณารอสักครู่");
                try { return ImportLocked(target, cookie, identity); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static RealAccountImportResult ImportLocked(Target target, string cookie, RealAccountImportIdentity identity)
        {
            RequireStopped(target);
            using (Snapshot snapshot = Load(target))
            {
                long now = (long)((target.NowUtc == null ? DateTime.UtcNow : target.NowUtc()).ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                if (now <= 0) throw new InvalidOperationException("เวลา Windows ไม่ถูกต้อง จึงยังนำเข้าบัญชีไม่ได้");
                byte[] nonce = new byte[12], plain = Encoding.UTF8.GetBytes(cookie), encrypted = null;
                string encoded;
                try
                {
                    using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
                    encrypted = RealAccountLauncher.EncryptGcm(snapshot.Key, nonce, plain);
                    encoded = "dev:v1:" + Convert.ToBase64String(nonce) + ":" + Convert.ToBase64String(encrypted);
                }
                finally { Array.Clear(plain, 0, plain.Length); if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length); }
                Dictionary<string, object> matched = null;
                long maxSort = -1;
                foreach (Dictionary<string, object> row in snapshot.Accounts)
                { if (Number(row["user_id"]) == identity.UserId) matched = row; maxSort = Math.Max(maxSort, Number(row["sort_index"])); }
                bool existing = matched != null;
                if (!existing)
                {
                    if (maxSort == Int64.MaxValue) throw new InvalidOperationException("ลำดับบัญชี Real เกินที่รองรับ");
                    matched = new Dictionary<string, object> {
                        {"id", Guid.NewGuid().ToString()}, {"username", identity.Username}, {"user_id", identity.UserId},
                        {"display_name", String.IsNullOrEmpty(identity.DisplayName) ? identity.Username : identity.DisplayName},
                        {"avatar_url", identity.AvatarUrl ?? ""}, {"created", identity.Created}, {"added_at", now},
                        {"alias", ""}, {"description", ""}, {"group", ""}, {"groups", new object[0]},
                        {"sort_index", maxSort + 1}, {"region", ""}, {"from_pool", false}
                    };
                    snapshot.Accounts.Add(matched);
                }
                matched["cookie"] = encoded;
                matched["cookie_rotated_at"] = now;
                byte[] after = new UTF8Encoding(false).GetBytes(Serializer().Serialize(snapshot.Accounts));
                if (after.Length > MaxAccountsBytes) throw new InvalidOperationException("ข้อมูลบัญชี Real หลังนำเข้ามีขนาดเกินที่รองรับ");
                string afterHash = Hash(after), path = Path.Combine(target.Root, "accounts.json");
                string backup = Backup(target, snapshot.AccountsBytes);
                string staging = path + ".nexus-import-" + Guid.NewGuid().ToString("N") + ".tmp";
                bool replaced = false;
                try
                {
                    WriteDurable(staging, after);
                    if (Hash(File.ReadAllBytes(staging)) != afterHash) throw new IOException("ตรวจสอบข้อมูลบัญชีที่เตรียมไว้ไม่สำเร็จ");
                    if (target.BeforeReplace != null) target.BeforeReplace();
                    RequireStopped(target);
                    VerifySources(target, snapshot);
                    File.Replace(staging, path, null, false);
                    replaced = true;
                    if (target.AfterReplace != null) target.AfterReplace();
                    if (Hash(ReadBounded(path, MaxAccountsBytes)) != afterHash) throw new IOException("ตรวจสอบข้อมูลบัญชีหลังนำเข้าไม่สำเร็จ");
                    return new RealAccountImportResult { UserId = identity.UserId, Username = identity.Username, UpdatedExisting = existing, BackupPath = backup };
                }
                catch (Exception failure)
                {
                    if (replaced)
                    {
                        string recovery = path + ".nexus-recover-" + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            if (Hash(ReadBounded(path, MaxAccountsBytes)) != afterHash)
                                throw new IOException("ข้อมูล Real เปลี่ยนหลังนำเข้า จึงรักษาข้อมูลล่าสุดไว้");
                            WriteDurable(recovery, snapshot.AccountsBytes);
                            RequireStopped(target);
                            if (Hash(ReadBounded(path, MaxAccountsBytes)) != afterHash) throw new IOException("ข้อมูล Real เปลี่ยนระหว่างคืนค่า");
                            File.Replace(recovery, path, null, false);
                            if (Hash(ReadBounded(path, MaxAccountsBytes)) != Hash(snapshot.AccountsBytes)) throw new IOException("ตรวจสอบข้อมูลบัญชีที่คืนค่าไม่สำเร็จ");
                        }
                        catch (Exception recoveryFailure)
                        { throw new IOException("นำเข้าไม่สำเร็จ ข้อมูลสำรองถูกเข้ารหัสไว้ที่ " + backup + " · " + recoveryFailure.Message, failure); }
                        finally { Clean(recovery); }
                    }
                    throw;
                }
                finally { Clean(staging); Array.Clear(after, 0, after.Length); }
            }
        }
    }
}
