using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NexusRobloxManager
{
    public class UpdateEnvelope { public string payload; public string signature; }
    public class UpdateManifest
    {
        public string channel, version, url, sha256, notes;
        public long size;
        public string EnvelopeJson;
    }
    public class UpdateApplyRequest
    {
        public int ParentPid;
        public long ParentStartTicks;
        public string TargetPath, OldSha256, EnvelopeJson;
    }

    public static class UpdateService
    {
        public const string FeedUrl = "https://raw.githubusercontent.com/PeeKillxa/Auto-Rejoin/main/Auto-Rejoin-PC/update.json";
        public const long MaxBinaryBytes = 64 * 1024 * 1024;
        private static string UpdatesRoot { get { return Path.Combine(WatchdogEngine.DataDir, "updates"); } }

        public static UpdateManifest VerifyManifest(string json, string publicKeyXml)
        {
            if (json == null || json.Length > 256 * 1024) throw new InvalidOperationException("Update manifest is too large");
            UpdateEnvelope envelope = new JavaScriptSerializer().Deserialize<UpdateEnvelope>(json);
            if (envelope == null) throw new InvalidOperationException("Invalid update manifest");
            byte[] payload = Convert.FromBase64String(envelope.payload);
            if (payload.Length > 64 * 1024) throw new InvalidOperationException("Update payload is too large");
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(publicKeyXml);
                if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(envelope.signature)))
                    throw new InvalidOperationException("Update signature verification failed");
            }
            UpdateManifest manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(Encoding.UTF8.GetString(payload));
            Version version;
            if (manifest == null || manifest.channel != "Auto-Rejoin-PC" || !Version.TryParse(manifest.version, out version) ||
                !Regex.IsMatch(manifest.sha256 ?? "", "^[0-9a-f]{64}$") || manifest.size < 1024 || manifest.size > MaxBinaryBytes)
                throw new InvalidOperationException("Invalid PC update metadata");
            RequireHttps(manifest.url);
            manifest.EnvelopeJson = json;
            return manifest;
        }

        private static Uri RequireHttps(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
                throw new InvalidOperationException("Updates require an HTTPS download URL");
            return uri;
        }

        private static byte[] Download(string url, long limit)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            for (int redirect = 0; redirect < 5; redirect++)
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(RequireHttps(url));
                request.AllowAutoRedirect = false; request.Timeout = 20000; request.ReadWriteTimeout = 20000;
                request.UserAgent = "NexusRobloxManager/" + AppVersion.Value;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    {
                        url = new Uri(new Uri(url), response.Headers["Location"]).AbsoluteUri;
                        continue;
                    }
                    if (response.ContentLength > limit) throw new InvalidOperationException("Update download exceeds size limit");
                    using (Stream input = response.GetResponseStream())
                    using (MemoryStream output = new MemoryStream())
                    {
                        byte[] buffer = new byte[32768]; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + count > limit) throw new InvalidOperationException("Update download exceeds size limit");
                            output.Write(buffer, 0, count);
                        }
                        return output.ToArray();
                    }
                }
            }
            throw new InvalidOperationException("Too many update redirects");
        }

        public static UpdateManifest Check()
        {
            string json = Encoding.UTF8.GetString(Download(FeedUrl + "?check=" + DateTime.UtcNow.Ticks, 256 * 1024));
            UpdateManifest manifest = VerifyManifest(json, UpdateTrust.PublicKeyXml);
            return new Version(manifest.version) > new Version(AppVersion.Value) ? manifest : null;
        }

        public static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static void VerifyBinary(byte[] bytes, UpdateManifest manifest)
        {
            if (bytes.LongLength != manifest.size || Hash(bytes) != manifest.sha256 || bytes.Length < 2 || bytes[0] != 77 || bytes[1] != 90)
                throw new InvalidOperationException("Update executable size/hash validation failed");
        }

        public static string Stage(UpdateManifest manifest)
        {
            // Verify again even when called with an object prepared by the UI.
            manifest = VerifyManifest(manifest.EnvelopeJson, UpdateTrust.PublicKeyXml);
            if (new Version(manifest.version) <= new Version(AppVersion.Value)) throw new InvalidOperationException("Update is not newer");
            byte[] bytes = Download(manifest.url, MaxBinaryBytes);
            VerifyBinary(bytes, manifest);
            string directory = Path.Combine(UpdatesRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "new.exe"), bytes);
            string current = Application.ExecutablePath;
            File.Copy(current, Path.Combine(directory, "helper.exe"));
            using (Process parent = Process.GetCurrentProcess())
            {
                UpdateApplyRequest request = new UpdateApplyRequest { ParentPid = parent.Id,
                    ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks, TargetPath = current,
                    OldSha256 = Hash(File.ReadAllBytes(current)), EnvelopeJson = manifest.EnvelopeJson };
                File.WriteAllText(Path.Combine(directory, "request.json"), new JavaScriptSerializer().Serialize(request));
            }
            return Path.Combine(directory, "request.json");
        }

        public static void StartInstaller(string requestPath)
        {
            string helper = Path.Combine(Path.GetDirectoryName(requestPath), "helper.exe");
            Process.Start(new ProcessStartInfo(helper, "--apply-update \"" + requestPath + "\"")
            { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(helper) });
        }

        public static void ReplaceVerifiedFile(string target, string staged, string expectedOldHash, string backup)
        {
            if (Hash(File.ReadAllBytes(target)) != expectedOldHash) throw new InvalidOperationException("Installed executable changed; update cancelled");
            string temporary = target + ".update-" + Guid.NewGuid().ToString("N");
            File.Copy(staged, temporary);
            try { File.Replace(temporary, target, backup); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static int Apply(string requestPath, bool showErrors = true)
        {
            string target = null, backup = null;
            bool replaced = false;
            try
            {
                string path = Path.GetFullPath(requestPath);
                string root = Path.GetFullPath(UpdatesRoot) + Path.DirectorySeparatorChar;
                string directory = Path.GetDirectoryName(path);
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) != "request.json" ||
                    !Application.ExecutablePath.Equals(Path.Combine(directory, "helper.exe"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid updater helper path");
                UpdateApplyRequest request = new JavaScriptSerializer().Deserialize<UpdateApplyRequest>(File.ReadAllText(path));
                if (Hash(File.ReadAllBytes(Application.ExecutablePath)) != request.OldSha256)
                    throw new InvalidOperationException("Updater helper does not match the installed version");
                UpdateManifest manifest = VerifyManifest(request.EnvelopeJson, UpdateTrust.PublicKeyXml);
                if (new Version(manifest.version) <= new Version(AppVersion.Value)) throw new InvalidOperationException("Update is not newer");
                string staged = Path.Combine(directory, "new.exe");
                VerifyBinary(File.ReadAllBytes(staged), manifest);
                target = Path.GetFullPath(request.TargetPath);
                if (!Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) || target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid update target");
                try
                {
                    using (Process parent = Process.GetProcessById(request.ParentPid))
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTicks ||
                            !parent.MainModule.FileName.Equals(target, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Update parent identity changed");
                        if (!parent.WaitForExit(30000)) throw new InvalidOperationException("Manager has not exited; no files replaced");
                    }
                }
                catch (ArgumentException) { }
                backup = target + ".previous";
                VerifyBinary(File.ReadAllBytes(staged), manifest);
                ReplaceVerifiedFile(target, staged, request.OldSha256, backup);
                replaced = true;
                using (Process next = Process.Start(new ProcessStartInfo(target, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) }))
                    if (next == null || (next.WaitForExit(5000) && next.ExitCode != 0)) throw new InvalidOperationException("Updated manager could not start");
                return 0;
            }
            catch (Exception ex)
            {
                if (replaced && backup != null && File.Exists(backup))
                {
                    try { File.Replace(backup, target, null); Process.Start(target); } catch { }
                }
                Program.WriteDiagnostic("[Updater] " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine);
                if (showErrors) MessageBox.Show("อัปเดตไม่สำเร็จ ใช้เวอร์ชันเดิมต่อได้\n" + ex.Message, "Nexus Update");
                return 1;
            }
        }
    }
}
