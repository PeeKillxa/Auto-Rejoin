using System;
using System.IO;
using System.Security.Cryptography;
using NexusRobloxManager;

static class RealLoginTimeoutChecks
{
    static int count;
    static void Assert(bool value, string message)
    { if (!value) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; } catch (IOException) { rejected = true; }
        Assert(rejected, message);
    }
    static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    sealed class Fixture : IDisposable
    {
        internal readonly string Root;
        internal readonly byte[] Original;
        internal readonly RealLoginTimeout.Target Target;
        internal string Backup { get { return Path.Combine(Target.BackupRoot, Target.ExpectedSha256, "Real.exe.original"); } }
        internal Fixture()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NexusLoginTimeoutChecks-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Root);
            Original = new byte[128];
            for (int i = 0; i < Original.Length; i++) Original[i] = (byte)(i * 37 + 11);
            Original[0] = (byte)'M'; Original[1] = (byte)'Z';
            Original[20] = 0x48; Original[21] = 0xb8;
            for (int i = 22; i < 30; i++) Original[i] = 0;
            Original[26] = 240;
            Target = new RealLoginTimeout.Target
            {
                Path = Path.Combine(Root, "Real.exe"), BackupRoot = Path.Combine(Root, "backups"),
                ExpectedSha256 = Hash(Original), Version = "3.1.1", Length = Original.Length, Offset = 26,
                IsRunning = delegate { return false; }
            };
            File.WriteAllBytes(Target.Path, Original);
        }
        public void Dispose()
        {
            string resolved = Path.GetFullPath(Root);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(parent + "NexusLoginTimeoutChecks-", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Refusing cleanup outside the test fixture directory");
            foreach (string file in Directory.GetFiles(resolved, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(resolved, true);
        }
    }
    public static int Run()
    {
        count = 0;
        using (Fixture f = new Fixture())
        {
            RealLoginTimeoutStatus initial = RealLoginTimeout.InspectFile(f.Target);
            Assert(initial.Minutes == 2 && !initial.IsExtended && initial.Version == "3.1.1", "known original login limit is two minutes");
            RealLoginTimeoutStatus extended = RealLoginTimeout.ApplyFile(f.Target, 15);
            byte[] changed = File.ReadAllBytes(f.Target.Path);
            Assert(extended.Minutes == 15 && extended.IsExtended && changed.Length == f.Original.Length, "fifteen-minute patch preserves file size");
            Assert(BitConverter.ToUInt32(changed, f.Target.Offset) == 1800 && BitConverter.ToUInt32(changed, f.Target.Offset - 4) == 0,
                "patch changes only polling limit and leaves initial counter zero");
            bool sameElsewhere = true;
            for (int i = 0; i < changed.Length; i++)
                if ((i < f.Target.Offset || i >= f.Target.Offset + 4) && changed[i] != f.Original[i]) sameElsewhere = false;
            Assert(sameElsewhere, "every byte outside the verified immediate remains unchanged");
            Assert(Hash(File.ReadAllBytes(f.Backup)) == f.Target.ExpectedSha256 && (File.GetAttributes(f.Backup) & FileAttributes.ReadOnly) != 0,
                "immutable local backup contains exact original bytes");
            DateTime backupDate = File.GetLastWriteTimeUtc(f.Backup), targetDate = File.GetLastWriteTimeUtc(f.Target.Path);
            RealLoginTimeout.ApplyFile(f.Target, 15);
            Assert(File.GetLastWriteTimeUtc(f.Target.Path) == targetDate && File.GetLastWriteTimeUtc(f.Backup) == backupDate,
                "repeating the selected timeout does not rewrite files");
            RealLoginTimeoutStatus restored = RealLoginTimeout.ApplyFile(f.Target, 2);
            Assert(restored.Minutes == 2 && Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256, "restore produces the exact original executable");
            Assert(Hash(File.ReadAllBytes(f.Backup)) == f.Target.ExpectedSha256 && File.GetLastWriteTimeUtc(f.Backup) == backupDate,
                "restore preserves the original backup");
        }
        using (Fixture f = new Fixture())
        {
            byte[] tampered = (byte[])f.Original.Clone(); tampered[80] ^= 1; File.WriteAllBytes(f.Target.Path, tampered);
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "unrelated binary tampering is rejected");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == Hash(tampered) && !Directory.Exists(f.Target.BackupRoot), "unsupported binary remains untouched without creating a backup");
        }
        using (Fixture f = new Fixture())
        {
            f.Target.Version = "3.1.2";
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "unknown Real version is rejected");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256, "unknown version is never patched");
        }
        using (Fixture f = new Fixture())
        {
            File.WriteAllBytes(f.Target.Path, new byte[10]);
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "truncated file is rejected before a write");
        }
        using (Fixture f = new Fixture())
        {
            byte[] unexpectedLimit = (byte[])f.Original.Clone(); unexpectedLimit[f.Target.Offset] = 0;
            File.WriteAllBytes(f.Target.Path, unexpectedLimit);
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "unrecognized limit is rejected even if other bytes match");
        }
        using (Fixture f = new Fixture())
        {
            f.Target.IsRunning = delegate { return true; };
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "running Real prevents binary replacement");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256 && !Directory.Exists(f.Target.BackupRoot), "running-process guard acts before any file change");
        }
        using (Fixture f = new Fixture())
        {
            int guards = 0; f.Target.IsRunning = delegate { return ++guards > 1; };
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "process guard is checked again immediately before replacement");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256, "Real starting during staging leaves original executable intact");
        }
        using (Fixture f = new Fixture())
        {
            f.Target.BeforeReplace = delegate { throw new IOException("simulated staging failure"); };
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "staging failure is reported");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256 && Directory.GetFiles(f.Root, "*.tmp").Length == 0,
                "failure before replacement preserves target and removes staging file");
        }
        using (Fixture f = new Fixture())
        {
            FileStream replacementBlocker = null;
            f.Target.BeforeReplace = delegate { replacementBlocker = new FileStream(f.Target.Path, FileMode.Open, FileAccess.Read, FileShare.Read); };
            try
            {
                Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "Windows sharing violation during atomic replacement is reported");
                Assert(Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256 && Directory.GetFiles(f.Root, "*.tmp").Length == 0,
                    "failed atomic replacement leaves original executable complete");
            }
            finally { if (replacementBlocker != null) replacementBlocker.Dispose(); }
        }
        using (Fixture f = new Fixture())
        {
            f.Target.AfterReplace = delegate { throw new IOException("simulated verification failure"); };
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "failure after replacement is reported");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256, "transaction rolls back its replacement after a failure");
            Assert(Directory.GetFiles(f.Root, "*.rollback").Length == 0, "successful rollback consumes transaction backup");
        }
        using (Fixture f = new Fixture())
        {
            byte[] concurrent = (byte[])f.Original.Clone(); concurrent[70] ^= 1;
            f.Target.BeforeReplace = delegate { File.WriteAllBytes(f.Target.Path, concurrent); };
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "file identity is rechecked after staging");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == Hash(concurrent), "concurrent file update is preserved before replacement");
        }
        using (Fixture f = new Fixture())
        {
            byte[] concurrent = (byte[])f.Original.Clone(); concurrent[71] ^= 1;
            f.Target.AfterReplace = delegate { File.WriteAllBytes(f.Target.Path, concurrent); throw new IOException("concurrent update"); };
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 15); }, "concurrent update after replacement aborts recovery");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == Hash(concurrent) && Directory.GetFiles(f.Root, "*.rollback").Length == 1,
                "rollback guard preserves newer bytes and recoverable prior file");
        }
        using (Fixture f = new Fixture())
        {
            RealLoginTimeout.ApplyFile(f.Target, 15);
            byte[] extended = File.ReadAllBytes(f.Target.Path);
            File.SetAttributes(f.Backup, FileAttributes.Normal);
            byte[] badBackup = File.ReadAllBytes(f.Backup); badBackup[60] ^= 1; File.WriteAllBytes(f.Backup, badBackup);
            Reject(delegate { RealLoginTimeout.ApplyFile(f.Target, 2); }, "corrupt existing backup prevents restore");
            Assert(Hash(File.ReadAllBytes(f.Target.Path)) == Hash(extended) && Hash(File.ReadAllBytes(f.Backup)) == Hash(badBackup),
                "backup rejection preserves target and never overwrites corrupt backup");
        }
        using (Fixture f = new Fixture())
        {
            bool rejected = false;
            try { RealLoginTimeout.ApplyFile(f.Target, 30); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected && Hash(File.ReadAllBytes(f.Target.Path)) == f.Target.ExpectedSha256, "only two- and fifteen-minute choices are accepted");
        }
        return count;
    }
}
