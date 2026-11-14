using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NexusRobloxManager
{
    // Reads Real's vault in memory only. Never copies cookies or tickets to disk/logs.
    public static class RealAccountLauncher
    {
        private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Real", "data");

        public static string ReadCookie(long userId)
        {
            object[] accounts = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Root, "accounts.json"))) as object[];
            if (accounts == null) throw new InvalidOperationException("Real accounts.json format is unsupported");
            foreach (object item in accounts)
            {
                Dictionary<string, object> account = item as Dictionary<string, object>;
                if (account == null || !account.ContainsKey("user_id") || Convert.ToInt64(account["user_id"]) != userId) continue;
                string value = Convert.ToString(account["cookie"]);
                if (value.StartsWith("_|WARNING:", StringComparison.Ordinal)) return value;
                if (!value.StartsWith("dev:v1:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unlock the account in Real; this vault format is unsupported");
                byte[] key = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Root, "account-vault.dpapi")),
                    Encoding.UTF8.GetBytes("Real-UI account vault key v1"), DataProtectionScope.CurrentUser);
                byte[] plain = null;
                try
                {
                    string[] parts = value.Split(':');
                    if (parts.Length != 4) throw new InvalidOperationException("Invalid Real vault entry");
                    plain = DecryptGcm(key, Convert.FromBase64String(parts[2]), Convert.FromBase64String(parts[3]));
                    string cookie = Encoding.UTF8.GetString(plain);
                    if (!cookie.StartsWith("_|WARNING:", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid Real account cookie");
                    return cookie;
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                    if (plain != null) Array.Clear(plain, 0, plain.Length);
                }
            }
            throw new InvalidOperationException("Account is missing from Real");
        }

        public static void ReadTarget(out long placeId, out string server)
        {
            placeId = 0;
            server = "";
            string file = Path.Combine(Root, "config", "settings.json");
            Dictionary<string, object> settings = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            if (settings == null || !settings.ContainsKey("behavior")) return;
            Dictionary<string, object> behavior = settings["behavior"] as Dictionary<string, object>;
            if (behavior == null || !behavior.ContainsKey("savedLaunchTargets")) return;
            object[] targets = behavior["savedLaunchTargets"] as object[];
            if (targets == null || targets.Length == 0) return;
            Dictionary<string, object> target = targets[0] as Dictionary<string, object>;
            if (target == null) return;
            object game;
            if (target.TryGetValue("game", out game)) long.TryParse(Convert.ToString(game), out placeId);
            object value;
            if (target.TryGetValue("server", out value)) server = Convert.ToString(value).Trim();
        }

        public static string BuildJoinUrl(long placeId, long tracker, string server)
        {
            if (placeId <= 0) throw new InvalidOperationException("Set a saved launch target in Real first");
            string url = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=";
            if (string.IsNullOrEmpty(server)) url += "RequestGame";
            else
            {
                Guid job;
                if (!Guid.TryParse(server, out job))
                    throw new InvalidOperationException("Real's saved server must be empty or a server Job ID; private invite links need launching in Real first");
                url += "RequestGameJob&gameId=" + Uri.EscapeDataString(job.ToString());
            }
            return url + "&placeId=" + placeId + "&isPlayTogetherGame=false&browserTrackerId=" + tracker;
        }

        public static void ValidateExecutable(string executable)
        {
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable) ||
                !Path.GetFileName(executable).Equals("RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Original Roblox client is unavailable; launch this account from Real once");
        }

        public static int Launch(AccountSlot account, Action verifyNoOldClient)
        {
            if (verifyNoOldClient == null) throw new ArgumentNullException("verifyNoOldClient");
            if (!WatchdogEngine.SyncRealAccounts() || !WatchdogEngine.IsTrackedAccount(account))
                throw new InvalidOperationException("Account is no longer available in Real");
            long placeId;
            string server;
            ReadTarget(out placeId, out server);
            long tracker = account.TrackerId > 0 ? account.TrackerId : DateTime.UtcNow.Ticks / 10000;
            string joinUrl = BuildJoinUrl(placeId, tracker, server);
            string executable = account.ExecutablePath;
            ValidateExecutable(executable);
            string cookie = ReadCookie(account.UserId);
            string ticket = RequestTicket(cookie, placeId);
            // Same installed client as Real: retains its ClientSettings and global graphics/FPS settings.
            ProcessStartInfo start = new ProcessStartInfo(executable,
                "--app -t " + ticket + " -b " + tracker + " -j " + joinUrl);
            start.UseShellExecute = false;
            if (WatchdogEngine.WantsHidden(account)) start.WindowStyle = ProcessWindowStyle.Hidden;
            start.WorkingDirectory = Path.GetDirectoryName(executable);
            if (!WatchdogEngine.SyncRealAccounts() || !WatchdogEngine.IsTrackedAccount(account))
                throw new InvalidOperationException("Account was removed from Real; launch cancelled");
            // Authentication is a network request; another launcher could have opened a client while it was in flight.
            verifyNoOldClient();
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Roblox did not start");
                return process.Id;
            }
        }

        private static string RequestTicket(string cookie, long placeId)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string csrf = "";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://auth.roblox.com/v1/authentication-ticket/");
                request.Method = "POST";
                request.ContentLength = 0;
                request.ContentType = "application/json";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0.0.0 Safari/537.36";
                request.AllowAutoRedirect = false;
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.Referer = "https://www.roblox.com/games/" + placeId;
                request.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;
                request.Headers["RBXAuthenticationNegotiation"] = "1";
                if (csrf.Length > 0) request.Headers["X-CSRF-TOKEN"] = csrf;
                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        string ticket = response.Headers["rbx-authentication-ticket"];
                        if (string.IsNullOrEmpty(ticket) || !System.Text.RegularExpressions.Regex.IsMatch(ticket, @"^[!-~]+$") || ticket.IndexOf('"') >= 0)
                            throw new InvalidOperationException("Roblox returned no usable authentication ticket");
                        return ticket;
                    }
                }
                catch (WebException ex)
                {
                    using (HttpWebResponse response = ex.Response as HttpWebResponse)
                    {
                        if (response == null) throw new InvalidOperationException("Cannot reach Roblox authentication service");
                        string token = response.Headers["x-csrf-token"];
                        if (attempt == 0 && response.StatusCode == HttpStatusCode.Forbidden && !string.IsNullOrEmpty(token))
                        { csrf = token; continue; }
                        throw new InvalidOperationException("Roblox authentication HTTP " + (int)response.StatusCode + "; check the account in Real");
                    }
                }
            }
            throw new InvalidOperationException("Roblox authentication failed");
        }

        // Windows CNG AES-GCM, supported by the .NET Framework compiler used by Build.bat.
        [StructLayout(LayoutKind.Sequential)]
        private struct AuthInfo
        {
            public int Size, Version;
            public IntPtr Nonce;
            public int NonceLength;
            public IntPtr AuthData;
            public int AuthDataLength;
            public IntPtr Tag;
            public int TagLength;
            public IntPtr MacContext;
            public int MacContextLength, AadLength;
            public long DataLength;
            public int Flags;
        }
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm, string id, string implementation, int flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptSetProperty(IntPtr algorithm, string property, byte[] input, int length, int flags);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptGenerateSymmetricKey(IntPtr algorithm, out IntPtr key, IntPtr keyObject, int keyObjectLength, byte[] secret, int secretLength, int flags);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptDecrypt(IntPtr key, byte[] input, int inputLength, ref AuthInfo padding, IntPtr iv, int ivLength, byte[] output, int outputLength, out int resultLength, int flags);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptDestroyKey(IntPtr key);
        [DllImport("bcrypt.dll")]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, int flags);
        private static void Check(int status) { if (status != 0) throw new CryptographicException("Real vault authentication failed"); }

        public static byte[] DecryptGcm(byte[] secret, byte[] nonce, byte[] encrypted)
        {
            if (secret.Length != 32 || nonce.Length != 12 || encrypted.Length < 16) throw new CryptographicException("Invalid Real vault entry");
            IntPtr algorithm = IntPtr.Zero, key = IntPtr.Zero;
            byte[] tag = new byte[16];
            Buffer.BlockCopy(encrypted, encrypted.Length - 16, tag, 0, 16);
            GCHandle nonceHandle = GCHandle.Alloc(nonce, GCHandleType.Pinned);
            GCHandle tagHandle = GCHandle.Alloc(tag, GCHandleType.Pinned);
            byte[] output = new byte[encrypted.Length - 16];
            try
            {
                Check(BCryptOpenAlgorithmProvider(out algorithm, "AES", null, 0));
                byte[] mode = Encoding.Unicode.GetBytes("ChainingModeGCM\0");
                Check(BCryptSetProperty(algorithm, "ChainingMode", mode, mode.Length, 0));
                Check(BCryptGenerateSymmetricKey(algorithm, out key, IntPtr.Zero, 0, secret, secret.Length, 0));
                AuthInfo info = new AuthInfo();
                info.Size = Marshal.SizeOf(typeof(AuthInfo)); info.Version = 1;
                info.Nonce = nonceHandle.AddrOfPinnedObject(); info.NonceLength = nonce.Length;
                info.Tag = tagHandle.AddrOfPinnedObject(); info.TagLength = tag.Length;
                int length;
                Check(BCryptDecrypt(key, encrypted, output.Length, ref info, IntPtr.Zero, 0, output, output.Length, out length, 0));
                if (length != output.Length) throw new CryptographicException("Invalid Real vault payload");
                return output;
            }
            catch { Array.Clear(output, 0, output.Length); throw; }
            finally
            {
                if (key != IntPtr.Zero) BCryptDestroyKey(key);
                if (algorithm != IntPtr.Zero) BCryptCloseAlgorithmProvider(algorithm, 0);
                tagHandle.Free(); nonceHandle.Free();
            }
        }
    }
}
