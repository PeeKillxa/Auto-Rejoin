using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NexusRobloxManager
{
    public static class WebViewRuntime
    {
        public const string SdkVersion = "1.0.4314.35";
        private static readonly object Gate = new object();
        private static bool registered;
        private static bool loaderConfigured;
        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>();
        private static int cleanupCount;
        private static int initializationCount;

        // Keep the bootstrap independent of WebView2 types: its assemblies are embedded.
        public static void Register()
        {
            lock (Gate)
            {
                if (registered) return;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                registered = true;
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            string resource = name == "Microsoft.Web.WebView2.Core" ? "Nexus.WebView2.Core.dll" :
                name == "Microsoft.Web.WebView2.WinForms" ? "Nexus.WebView2.WinForms.dll" : null;
            if (resource == null) return null;
            lock (Gate)
            {
                Assembly assembly;
                if (!Loaded.TryGetValue(resource, out assembly))
                { assembly = Assembly.Load(ReadVerifiedResource(resource)); Loaded.Add(resource, assembly); }
                return assembly;
            }
        }

        internal static string ExpectedResourceHash(string resource)
        {
            switch (resource)
            {
                case "Nexus.WebView2.Core.dll": return "acdd517b951829f763d1255f32df1e6a936cb8623a7ebff6b8b24172dede6bd6";
                case "Nexus.WebView2.WinForms.dll": return "c66437f80886aecf074804b61af4cc10ad90a95310c8e0a717f31ea377c26b03";
                case "Nexus.WebView2.Loader.x64.dll": return "d3fce90bdeecb08596e377dea6a316354dd8949e32ff09b89d51be00c14e21b6";
                case "Nexus.WebView2.Loader.x86.dll": return "1d348d3c0a5cb0d985389b68a0fd9bf966c2aed65f5e44bbc0233739b7a57f9e";
                default: throw new ArgumentException("Unknown WebView2 resource", "resource");
            }
        }

        internal static byte[] ReadVerifiedResource(string resource)
        {
            byte[] bytes;
            using (Stream stream = typeof(WebViewRuntime).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null || stream.Length > 2 * 1024 * 1024)
                    throw new InvalidOperationException("ไฟล์ WebView2 ใน Nexus ไม่ครบ กรุณาอัปเดต Nexus อีกครั้ง");
                using (MemoryStream copy = new MemoryStream()) { stream.CopyTo(copy); bytes = copy.ToArray(); }
            }
            if (!String.Equals(Hash(bytes), ExpectedResourceHash(resource), StringComparison.Ordinal))
                throw new InvalidOperationException("ตรวจสอบไฟล์ WebView2 ใน Nexus ไม่ผ่าน กรุณาอัปเดต Nexus อีกครั้ง");
            return bytes;
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static string ReadLicense()
        {
            using (Stream stream = typeof(WebViewRuntime).Assembly.GetManifestResourceStream("Nexus.WebView2.License.txt"))
            {
                if (stream == null) return "Microsoft.Web.WebView2 " + SdkVersion + "\nhttps://www.nuget.org/packages/Microsoft.Web.WebView2/" + SdkVersion;
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        public static string PrepareLoaderDirectory()
        {
            Register();
            string architecture = Environment.Is64BitProcess ? "x64" : "x86";
            string resource = "Nexus.WebView2.Loader." + architecture + ".dll";
            byte[] bytes = ReadVerifiedResource(resource);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexusRobloxManager", "browser-runtime", SdkVersion, architecture);
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("โฟลเดอร์ WebView2 ไม่ใช่โฟลเดอร์ข้อมูลปกติ");
            string file = Path.Combine(directory, "WebView2Loader.dll");
            lock (Gate)
            {
                if (File.Exists(file))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || Hash(File.ReadAllBytes(file)) != ExpectedResourceHash(resource))
                        throw new InvalidOperationException("ไฟล์ WebView2Loader ในเครื่องไม่ผ่านการตรวจสอบ");
                    return directory;
                }
                string staging = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (FileStream output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                    try { File.Move(staging, file); }
                    catch (IOException) { if (!File.Exists(file)) throw; }
                    if (Hash(File.ReadAllBytes(file)) != ExpectedResourceHash(resource))
                        throw new InvalidOperationException("ตรวจสอบ WebView2Loader หลังเตรียมไฟล์ไม่ผ่าน");
                    return directory;
                }
                finally { try { if (File.Exists(staging)) File.Delete(staging); } catch (IOException) { } }
            }
        }

        public static void ConfigureLoader()
        {
            Register();
            lock (Gate)
            {
                if (loaderConfigured) return;
                ConfigureLoaderCore(PrepareLoaderDirectory());
                loaderConfigured = true;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ConfigureLoaderCore(string directory)
        {
            Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(directory);
        }

        internal static string LoginProfileRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusRobloxManager", "browser-login"); }
        }

        internal static string CreateProfile()
        {
            string root = LoginProfileRoot;
            Directory.CreateDirectory(root);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("โฟลเดอร์ล็อกอินไม่ใช่โฟลเดอร์ข้อมูลปกติ");
            string profile = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            return profile;
        }

        internal static bool IsMatchingProfile(string expected, string actual)
        {
            try
            {
                return !String.IsNullOrWhiteSpace(expected) && !String.IsNullOrWhiteSpace(actual) &&
                    String.Equals(Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        Path.GetFullPath(actual).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        internal static bool IsOwnedProfile(string root, string profile)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullProfile = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Guid id;
                if (!String.Equals(Path.GetDirectoryName(fullProfile), fullRoot, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(Path.GetFileName(fullProfile), "N", out id)) return false;
                if (Directory.Exists(fullRoot) && (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0) return false;
                if (Directory.Exists(fullProfile) && (File.GetAttributes(fullProfile) & FileAttributes.ReparsePoint) != 0) return false;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static void DeleteOwnedProfile(string root, string profile)
        {
            if (!IsOwnedProfile(root, profile) || !Directory.Exists(profile)) return;
            // No recursive deletion is admitted across a junction or symbolic link.
            Stack<string> pending = new Stack<string>(); pending.Push(profile);
            while (pending.Count > 0)
            {
                foreach (string path in Directory.GetFileSystemEntries(pending.Pop()))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
            if (IsOwnedProfile(root, profile)) Directory.Delete(Path.GetFullPath(profile), true);
        }

        internal static void QueueProfileCleanup(string profile, ManualResetEvent browserExited)
        {
            string root = LoginProfileRoot;
            if (!IsOwnedProfile(root, profile)) return;
            Interlocked.Increment(ref cleanupCount);
            Thread cleanup = new Thread(delegate()
            {
                try
                {
                    if (browserExited != null && !browserExited.WaitOne(15000)) return;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        if (attempt == 1 || attempt == 4) GC.Collect();
                        try { DeleteOwnedProfile(root, profile); return; }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        Thread.Sleep(300);
                    }
                }
                finally
                {
                    if (browserExited != null) browserExited.Dispose();
                    Interlocked.Decrement(ref cleanupCount);
                }
            });
            cleanup.Name = "Nexus login profile cleanup";
            cleanup.IsBackground = false;
            cleanup.Start();
        }

        internal static void BeginProfileInitialization() { Interlocked.Increment(ref initializationCount); }
        internal static void EndProfileInitialization() { Interlocked.Decrement(ref initializationCount); }

        public static bool HasPendingProfileCleanup
        { get { return Volatile.Read(ref cleanupCount) != 0 || Volatile.Read(ref initializationCount) != 0; } }

        public static void DrainProfileCleanup()
        {
            // BrowserProcessExited is delivered on the UI apartment that created WebView2.
            // A standalone login host must pump that apartment before returning from Main.
            DateTime until = DateTime.UtcNow.AddSeconds(19);
            while (HasPendingProfileCleanup && DateTime.UtcNow < until)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(20);
            }
        }
    }
}
