using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NexusRobloxManager;

static class SecurityBlockChecks
{
    private static int count;
    private const string Sha = "6166e60d3bbd979fe334b9a3d96bbe846f188db1c52e8b40f309879d2276842e";
    private sealed class NativeIOException : IOException
    { internal NativeIOException(int code) { HResult = code; } }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    private static bool Reject(Action action)
    { try { action(); return false; } catch (ArgumentException) { return true; } }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NexusSecurityBlockChecks-" + Guid.NewGuid().ToString("N")));
        internal string Marker { get { return Path.Combine(Root, "security-blocks", Sha + ".txt"); } }
        internal Fixture() { Directory.CreateDirectory(Root); }
        public void Dispose()
        {
            string allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "NexusSecurityBlockChecks-";
            string resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refusing cleanup outside the security-block fixture");
            foreach (string file in Directory.GetFiles(resolved, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(resolved, true);
        }
    }

    public static int Run()
    {
        count = 0;
        Assert(RealLoginSecurity.GetErrorCode(new NativeIOException(unchecked((int)0x800700e1))) == 225,
            "Windows virus-infected HRESULT is identified without parsing localized text");
        Assert(RealLoginSecurity.GetErrorCode(new NativeIOException(unchecked((int)0x800700e2))) == 226,
            "Windows virus-deleted HRESULT is identified");
        Assert(RealLoginSecurity.GetErrorCode(new IOException("wrapper", new Win32Exception(225))) == 225,
            "nested Win32 security denial remains identifiable");
        Assert(RealLoginSecurity.GetErrorCode(new AggregateException(new IOException("ordinary"), new Win32Exception(226))) == 226,
            "an aggregated security denial is identified beyond the first exception");
        Assert(RealLoginSecurity.GetErrorCode(new Win32Exception(5, "file contains a virus")) == 0,
            "access denied and error wording alone cannot be mistaken for a virus block");
        Assert(RealLoginSecurity.GetErrorCode(new Win32Exception(32)) == 0,
            "ordinary sharing violations do not permanently pause timeout changes");
        Assert(RealLoginSecurity.GetErrorCode(new NativeIOException(unchecked((int)0x801300e1))) == 0 &&
            RealLoginSecurity.GetErrorCode(new NativeIOException(225)) == 0,
            "matching low bits without the Windows HRESULT facility do not classify as a security block");
        Assert(RealLoginSecurity.GetErrorCode(new IOException("Operation did not complete successfully because the file contains a virus or potentially unwanted software.")) == 0,
            "English message text without a native error code does not create a persistent block");
        Assert(RealLoginSecurity.GetErrorCode(null) == 0, "a missing failure is not a security block");

        using (Fixture f = new Fixture())
        {
            Assert(!RealLoginSecurity.IsBlocked(f.Root, Sha), "a new build state is not initially blocked");
            Exception native = new NativeIOException(unchecked((int)0x800700e2));
            RealLoginSecurityBlockedException blocked = RealLoginSecurity.RecordIfBlocked(f.Root, Sha, native);
            Assert(blocked != null && blocked.NativeErrorCode == 226 && Object.ReferenceEquals(blocked.InnerException, native),
                "security failure retains its original exception and native code");
            Assert(blocked.Message.Contains("Windows Security") && blocked.Message.Contains("พัก"),
                "the user-facing error explains that timeout changes are paused");
            byte[] marker = File.ReadAllBytes(f.Marker);
            Assert(marker.Length < 128 && File.ReadAllText(f.Marker).StartsWith("226\n", StringComparison.Ordinal),
                "persistent security marker contains bounded diagnostic text rather than executable bytes");
            Assert(RealLoginSecurity.IsBlocked(f.Root, Sha.ToUpperInvariant()),
                "the same build identity remains blocked regardless of hash casing");
            bool stopped = false;
            try { RealLoginSecurity.ThrowIfBlocked(f.Root, Sha); } catch (RealLoginSecurityBlockedException) { stopped = true; }
            Assert(stopped, "a blocked build fails before retrying mutation");

            HashSet<string> memory = (HashSet<string>)typeof(RealLoginSecurity).GetField("Blocked", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            memory.Remove(f.Marker);
            Assert(RealLoginSecurity.IsBlocked(f.Root, Sha), "a fresh state reader recovers the block from its on-disk marker");
            Assert(!RealLoginSecurity.IsBlocked(f.Root, new string('a', 64)), "a security block is scoped to its exact reviewed build");
            Assert(!RealLoginSecurity.IsBlocked(Path.Combine(f.Root, "other-root"), Sha), "another installation state root is independent");
        }

        using (Fixture f = new Fixture())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f.Marker));
            File.WriteAllText(f.Marker, "damaged optional description");
            Assert(RealLoginSecurity.IsBlocked(f.Root, Sha), "damaged marker content cannot silently enable another attempt");
        }

        using (Fixture f = new Fixture())
        {
            File.WriteAllText(Path.Combine(f.Root, "security-blocks"), "fixture prevents directory creation");
            Exception native = new Win32Exception(225);
            RealLoginSecurityBlockedException blocked = RealLoginSecurity.RecordIfBlocked(f.Root, Sha, native);
            Assert(blocked != null && Object.ReferenceEquals(blocked.InnerException, native),
                "failure to persist the marker does not hide the original Windows security denial");
            Assert(RealLoginSecurity.IsBlocked(f.Root, Sha), "a marker persistence failure still pauses retries in the current process");
        }

        using (Fixture f = new Fixture())
        {
            Assert(RealLoginSecurity.RecordIfBlocked(f.Root, Sha, new Win32Exception(5)) == null && !File.Exists(f.Marker),
                "ordinary permission errors do not write a security-block marker");
            Assert(Reject(delegate { RealLoginSecurity.IsBlocked(f.Root, "..\\outside"); }) &&
                Reject(delegate { RealLoginSecurity.RecordIfBlocked(f.Root, new string('g', 64), new Win32Exception(225)); }),
                "invalid build identities cannot choose a marker outside the fixed state directory");
        }
        using (Fixture f = new Fixture())
        {
            byte[] original = new byte[128];
            for (int i = 0; i < original.Length; i++) original[i] = (byte)(i * 13 + 7);
            original[0] = (byte)'M'; original[1] = (byte)'Z';
            original[20] = 0x48; original[21] = 0xb8;
            for (int i = 22; i < 30; i++) original[i] = 0;
            original[26] = 240;
            string digest;
            using (SHA256 hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(original)).Replace("-", "").ToLowerInvariant();
            RealLoginTimeout.Target target = new RealLoginTimeout.Target {
                Path = Path.Combine(f.Root, "fixture.exe"), BackupRoot = Path.Combine(f.Root, "backups"),
                ExpectedSha256 = digest, Version = "3.1.1", Length = original.Length, Offset = 26,
                IsRunning = delegate { return false; }
            };
            File.WriteAllBytes(target.Path, original);
            Exception native = new NativeIOException(unchecked((int)0x800700e1));
            target.BeforeReplace = delegate { throw native; };
            RealLoginSecurityBlockedException blocked = null;
            try { RealLoginTimeout.ApplyFile(target, 15); } catch (RealLoginSecurityBlockedException failure) { blocked = failure; }
            Assert(blocked != null && Object.ReferenceEquals(blocked.InnerException, native),
                "a security failure in the real timeout transaction is wrapped without losing the original cause");
            Assert(RealLoginSecurity.IsBlocked(target.BackupRoot, digest) &&
                Convert.ToBase64String(File.ReadAllBytes(target.Path)) == Convert.ToBase64String(original),
                "a denied timeout transaction pauses retries and leaves its verified original unchanged");
            RealLoginTimeoutStatus status = RealLoginTimeout.InspectFile(target);
            Assert(status.Minutes == 2 && status.SecurityBlocked && !status.IsExtended,
                "read-only inspection still reports the actual two-minute wait with the security-block status");
            int runningChecks = 0;
            target.IsRunning = delegate { runningChecks++; return false; };
            DateTime written = File.GetLastWriteTimeUtc(target.Path);
            try { RealLoginTimeout.ApplyFile(target, 15); } catch (RealLoginSecurityBlockedException) { }
            Assert(runningChecks == 0 && File.GetLastWriteTimeUtc(target.Path) == written,
                "a known blocked build stops before the running-process guard or another file mutation");
            Assert(Directory.GetFiles(f.Root, "*.tmp", SearchOption.AllDirectories).Length == 0,
                "a security-denied timeout transaction removes its accessible staging file");
        }
        using (Fixture f = new Fixture())
        {
            string staging = Path.Combine(f.Root, "locked-staging.tmp");
            Exception primary = new NativeIOException(unchecked((int)0x800700e1)), observed = null;
            using (FileStream locked = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                try { try { throw primary; } finally { RealLoginTimeout.CleanStaging(staging); } }
                catch (Exception error) { observed = error; }
            }
            Assert(Object.ReferenceEquals(primary, observed), "a locked staging cleanup cannot mask the original Windows security failure");
            RealLoginTimeout.CleanStaging(staging);
            Assert(!File.Exists(staging), "accessible staging can be cleaned after its temporary file lock is released");
        }
        return count;
    }
}
