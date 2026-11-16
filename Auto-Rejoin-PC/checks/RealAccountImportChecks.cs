using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using NexusRobloxManager;

static class RealAccountImportChecks
{
    private static int count;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private const string OldCookie = "_|WARNING:synthetic-import-fixture-old-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NewCookie = "_|WARNING:synthetic-import-fixture-new-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    private static bool Reject(Action action)
    { try { action(); return false; } catch (InvalidOperationException) { return true; } catch (IOException) { return true; } }
    private static bool Equal(byte[] a, byte[] b) { return Convert.ToBase64String(a) == Convert.ToBase64String(b); }
    private static RealAccountImportIdentity Identity(long uid)
    { return new RealAccountImportIdentity { UserId = uid, Username = "ImportFixture" + uid, DisplayName = "Fixture Display", Created = "2020-01-02T03:04:05Z" }; }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root;
        internal readonly RealAccountImport.Target Target;
        internal readonly byte[] Key = new byte[32];
        internal readonly byte[] Original;
        internal string Database { get { return Path.Combine(Target.Root, "accounts.json"); } }
        internal Fixture()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NexusAccountImportChecks-" + Guid.NewGuid().ToString("N")));
            Target = new RealAccountImport.Target { Root = Path.Combine(Root, "real-data"), BackupRoot = Path.Combine(Root, "backups"),
                IsRunning = delegate { return false; }, NowUtc = delegate { return new DateTime(2026, 11, 15, 14, 0, 0, DateTimeKind.Utc); } };
            Directory.CreateDirectory(Target.Root);
            for (int i = 0; i < Key.Length; i++) Key[i] = (byte)i;
            File.WriteAllBytes(Path.Combine(Target.Root, "account-vault.dpapi"), ProtectedData.Protect(Key, Encoding.UTF8.GetBytes("Real-UI account vault key v1"), DataProtectionScope.CurrentUser));
            File.WriteAllText(Path.Combine(Target.Root, "enc_settings.json"), "{\"initialized\":true,\"enabled\":false,\"has_device_key\":false}");
            var row = new Dictionary<string, object> { {"id", "fixed-original-id"}, {"user_id", 1L}, {"username", "ExistingFixture"},
                {"cookie", OldCookie}, {"cookie_rotated_at", 0}, {"added_at", 1700000000}, {"sort_index", 4}, {"alias", "Personal alias"},
                {"future_field", new Dictionary<string, object> {{"nested", new object[]{"keep", 7, true}}}} };
            Original = Encoding.UTF8.GetBytes(Json.Serialize(new object[] { row }));
            File.WriteAllBytes(Database, Original);
        }
        internal object[] Rows() { return (object[])Json.DeserializeObject(File.ReadAllText(Database)); }
        internal string DecryptCookie(Dictionary<string, object> row)
        {
            string[] parts = ((string)row["cookie"]).Split(':');
            return Encoding.UTF8.GetString(RealAccountLauncher.DecryptGcm(Key, Convert.FromBase64String(parts[2]), Convert.FromBase64String(parts[3])));
        }
        public void Dispose()
        {
            string allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "NexusAccountImportChecks-";
            string resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refusing cleanup outside the import fixture");
            foreach (string file in Directory.GetFiles(resolved, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(resolved, true);
            Array.Clear(Key, 0, Key.Length);
        }
    }

    public static int Run()
    {
        count = 0;
        RealAccountImport.ValidateInstalledVersion(new Version(3, 1, 1));
        Assert(true, "local account integration accepts the reviewed Real 3.1.1 installation version");
        Assert(Reject(delegate { RealAccountImport.ValidateInstalledVersion(new Version(3, 1, 0)); }) &&
            Reject(delegate { RealAccountImport.ValidateInstalledVersion(new Version(3, 2, 0)); }) &&
            Reject(delegate { RealAccountImport.ValidateInstalledVersion(null); }),
            "older, future, and missing Real installation versions cannot use the local integration");
        byte[] key = Convert.FromBase64String("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="), nonce = Convert.FromBase64String("AAECAwQFBgcICQoL");
        byte[] vector = Convert.FromBase64String("CWeubrbFtHr4LeOr0JwMBea4812TGisZXEeB4H4becJ1ecGSj6d74ADRDYjLcXNI5jraLyRappGxk4QK");
        Assert(Equal(RealAccountLauncher.EncryptGcm(key, nonce, Encoding.UTF8.GetBytes("Nexus vault authenticated decryption fixture")), vector),
            "CNG encryption matches an independent AES-256-GCM ciphertext and tag vector");
        using (Fixture f = new Fixture())
        {
            f.Target.IsRunning = delegate { throw new Exception("Preflight must not touch process control"); };
            RealAccountImport.EnsureCanImportFile(f.Target);
            Assert(Equal(File.ReadAllBytes(f.Database), f.Original) && !Directory.Exists(f.Target.BackupRoot),
                "import preflight validates the vault read-only without process control or backup writes");
        }
        using (Fixture f = new Fixture())
        {
            RealAccountImportResult result = RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2));
            object[] rows = f.Rows();
            var original = (Dictionary<string, object>)rows[0]; var added = (Dictionary<string, object>)rows[1];
            Assert(rows.Length == 2 && !result.UpdatedExisting && result.UserId == 2, "a reviewed new account is appended once");
            Assert(Json.Serialize(original) == Json.Serialize(((object[])Json.DeserializeObject(Encoding.UTF8.GetString(f.Original)))[0]),
                "new-account import preserves every existing account field including unknown nested metadata");
            Assert(f.DecryptCookie(added) == NewCookie && ((string)added["cookie"]).StartsWith("dev:v1:"),
                "the imported session is encrypted with the existing Real device-vault key");
            Assert(!File.ReadAllText(f.Database).Contains(NewCookie), "the new session is never serialized as a plaintext account cookie");
            long seconds = (long)(f.Target.NowUtc() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Assert(Convert.ToInt64(added["added_at"]) == seconds && Convert.ToInt64(added["cookie_rotated_at"]) == seconds,
                "new account timestamps use Real-compatible Unix seconds");
            Guid id;
            Assert(Guid.TryParse((string)added["id"], out id) && Convert.ToInt64(added["sort_index"]) == 5 &&
                added.ContainsKey("groups") && added.ContainsKey("region") && (bool)added["from_pool"] == false,
                "new accounts receive the complete compatible metadata and the next sort index");
            byte[] protectedBackup = File.ReadAllBytes(result.BackupPath);
            byte[] unprotected = ProtectedData.Unprotect(protectedBackup, Encoding.UTF8.GetBytes(RealAccountImport.BackupEntropy), DataProtectionScope.CurrentUser);
            Assert(Equal(unprotected, f.Original) && !Encoding.UTF8.GetString(protectedBackup).Contains(OldCookie),
                "the full original JSON backup is protected with Windows DPAPI including legacy plaintext cookie fields");
            Assert((File.GetAttributes(result.BackupPath) & FileAttributes.ReadOnly) != 0, "verified import backups are retained read-only");
            RealAccountImportResult update = RealAccountImport.ImportFile(f.Target, NewCookie, Identity(1));
            rows = f.Rows(); original = (Dictionary<string, object>)rows[0];
            Assert(rows.Length == 2 && update.UpdatedExisting && (string)original["id"] == "fixed-original-id" && (string)original["alias"] == "Personal alias" &&
                (string)original["username"] == "ExistingFixture" && original.ContainsKey("future_field") && Convert.ToInt64(original["added_at"]) == 1700000000,
                "reimporting an existing user updates the session without duplicating or resetting account metadata");
            Assert(f.DecryptCookie(original) == NewCookie && Convert.ToInt64(original["cookie_rotated_at"]) == seconds,
                "existing-user cookie rotation is encrypted and timestamped");
        }
        using (Fixture f = new Fixture())
        {
            f.Target.IsRunning = delegate { return true; };
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), f.Original) && !Directory.Exists(f.Target.BackupRoot),
                "an active Real process blocks import before any file write");
        }
        using (Fixture f = new Fixture())
        {
            int checks = 0; f.Target.IsRunning = delegate { return ++checks > 1; };
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), f.Original),
                "Real starting during staging blocks replacement");
        }
        using (Fixture f = new Fixture())
        {
            File.WriteAllText(Path.Combine(f.Target.Root, "enc_settings.json"), "{\"initialized\":true,\"enabled\":true}");
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && !Directory.Exists(f.Target.BackupRoot),
                "password-protected or unsupported Real vault modes are rejected without writing data");
        }
        using (Fixture f = new Fixture())
        {
            File.WriteAllText(f.Database, "["); byte[] malformed = File.ReadAllBytes(f.Database);
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), malformed),
                "malformed account reads cannot overwrite the existing database");
        }
        using (Fixture f = new Fixture())
        {
            object[] rows = f.Rows(); File.WriteAllText(f.Database, Json.Serialize(new object[] { rows[0], rows[0] }));
            byte[] duplicated = File.ReadAllBytes(f.Database);
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), duplicated),
                "duplicate user IDs in the existing inventory fail closed");
        }
        using (Fixture f = new Fixture())
        {
            byte[] vault = File.ReadAllBytes(Path.Combine(f.Target.Root, "account-vault.dpapi")); vault[vault.Length - 1] ^= 1;
            File.WriteAllBytes(Path.Combine(f.Target.Root, "account-vault.dpapi"), vault);
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), f.Original),
                "an unavailable or corrupt device key cannot produce a new account record");
        }
        using (Fixture f = new Fixture())
        {
            RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2));
            byte[] imported = File.ReadAllBytes(f.Database);
            byte[] wrongKey = new byte[32];
            File.WriteAllBytes(Path.Combine(f.Target.Root, "account-vault.dpapi"), ProtectedData.Protect(wrongKey, Encoding.UTF8.GetBytes("Real-UI account vault key v1"), DataProtectionScope.CurrentUser));
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(3)); }) && Equal(File.ReadAllBytes(f.Database), imported),
                "a device key that does not authenticate existing encrypted accounts is rejected");
        }
        using (Fixture f = new Fixture())
        {
            string unsupported = Encoding.UTF8.GetString(f.Original).Replace(OldCookie, "dev:v2:unsupported");
            File.WriteAllText(f.Database, unsupported);
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && File.ReadAllText(f.Database) == unsupported,
                "future cookie-vault formats are preserved and rejected without migration");
        }
        using (Fixture f = new Fixture())
        {
            byte[] concurrent = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f.Original).Replace("Personal alias", "External edit"));
            f.Target.BeforeReplace = delegate { File.WriteAllBytes(f.Database, concurrent); };
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), concurrent),
                "a concurrent account edit is preserved instead of being overwritten");
        }
        using (Fixture f = new Fixture())
        {
            f.Target.BeforeReplace = delegate { File.AppendAllText(Path.Combine(f.Target.Root, "enc_settings.json"), " "); };
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), f.Original),
                "vault settings are checked again immediately before replacement");
        }
        using (Fixture f = new Fixture())
        {
            f.Target.BeforeReplace = delegate { File.AppendAllText(Path.Combine(f.Target.Root, "account-vault.dpapi"), " "); };
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), f.Original),
                "the vault key file is checked again immediately before replacement");
        }
        using (Fixture f = new Fixture())
        {
            FileStream blocker = null;
            f.Target.BeforeReplace = delegate { blocker = new FileStream(f.Database, FileMode.Open, FileAccess.Read, FileShare.Read); };
            try
            {
                Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), f.Original),
                    "an atomic replacement sharing violation preserves the complete original database");
            }
            finally { if (blocker != null) blocker.Dispose(); }
        }
        using (Fixture f = new Fixture())
        {
            f.Target.AfterReplace = delegate { throw new IOException("synthetic post-replace failure"); };
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), f.Original),
                "post-replace failure restores the exact original JSON when the installed bytes still belong to this transaction");
            Assert(Directory.GetFiles(f.Target.Root, "*.tmp").Length == 0 && Directory.GetFiles(f.Target.BackupRoot, "*.dpapi").Length == 1,
                "transaction staging is cleaned while the encrypted original backup remains available");
        }
        using (Fixture f = new Fixture())
        {
            byte[] newer = null;
            f.Target.AfterReplace = delegate {
                string text = File.ReadAllText(f.Database).Replace("Personal alias", "Later edit"); newer = Encoding.UTF8.GetBytes(text);
                File.WriteAllBytes(f.Database, newer); throw new IOException("synthetic concurrent change");
            };
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, NewCookie, Identity(2)); }) && Equal(File.ReadAllBytes(f.Database), newer),
                "rollback never overwrites a newer account database written after replacement");
        }
        using (Fixture f = new Fixture())
        {
            Assert(Reject(delegate { RealAccountImport.ImportFile(f.Target, "invalid", Identity(2)); }) && !Directory.Exists(f.Target.BackupRoot),
                "an invalid or unreviewed session cannot trigger account writes");
        }
        return count;
    }
}
