using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NexusRobloxManager
{
    public sealed class RobloxLoginAccount
    {
        public long UserId { get; internal set; }
        public string Username { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Created { get; internal set; }
        public string Cookie { get; internal set; }
        internal void ForgetCookie() { Cookie = null; }
    }

    public static class RobloxLoginApi
    {
        public static RobloxLoginAccount AuthenticateCookie(string cookie)
        {
            if (String.IsNullOrWhiteSpace(cookie) || cookie.Length > 32768)
                throw new InvalidOperationException("ข้อมูลล็อกอินจาก Roblox ไม่ครบ กรุณาลองล็อกอินใหม่");
            foreach (char value in cookie)
                if (value < 33 || value > 126 || value == ';' || value == '"')
                    throw new InvalidOperationException("รูปแบบข้อมูลล็อกอิน Roblox ไม่ถูกต้อง");
            Dictionary<string, object> identity = ReadJson("https://users.roblox.com/v1/users/authenticated", cookie);
            long id; object raw;
            if (!identity.TryGetValue("id", out raw) || !Int64.TryParse(Convert.ToString(raw), out id) || id <= 0)
                throw new InvalidOperationException("Roblox ยังไม่ยืนยันบัญชี กรุณาทำขั้นตอนล็อกอินให้ครบ");
            Dictionary<string, object> profile = ReadJson("https://users.roblox.com/v1/users/" + id, null);
            return ParseConfirmedAccount(cookie, identity, profile);
        }

        internal static RobloxLoginAccount ParseConfirmedAccount(string cookie, Dictionary<string, object> identity, Dictionary<string, object> profile)
        {
            long id, profileId; object raw;
            if (identity == null || profile == null || !identity.TryGetValue("id", out raw) ||
                !Int64.TryParse(Convert.ToString(raw), out id) || id <= 0 || !profile.TryGetValue("id", out raw) ||
                !Int64.TryParse(Convert.ToString(raw), out profileId) || profileId != id)
                throw new InvalidOperationException("Roblox ส่งข้อมูลยืนยันบัญชีไม่ตรงกัน กรุณาล็อกอินใหม่");
            string username = Text(profile, "name"), displayName = Text(profile, "displayName"), created = Text(profile, "created");
            DateTime createdDate;
            if (!ValidName(username) || !ValidName(Text(identity, "name")) ||
                !String.Equals(username, Text(identity, "name"), StringComparison.OrdinalIgnoreCase) ||
                displayName.Length > 128 || HasControl(displayName) ||
                !DateTime.TryParse(created, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out createdDate))
                throw new InvalidOperationException("Roblox ส่งข้อมูลบัญชีกลับมาไม่ครบ กรุณาลองอีกครั้ง");
            return new RobloxLoginAccount { UserId = id, Username = username, DisplayName = displayName, Created = created, Cookie = cookie };
        }

        private static bool ValidName(string name) { return !String.IsNullOrWhiteSpace(name) && name.Length <= 64 && !HasControl(name); }
        private static bool HasControl(string value) { foreach (char character in value) if (Char.IsControl(character)) return true; return false; }

        private static string Text(Dictionary<string, object> json, string key)
        { object value; return json.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }

        private static Dictionary<string, object> ReadJson(string url, string cookie)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET"; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            request.AllowAutoRedirect = false; request.UserAgent = "NexusRobloxManager";
            if (cookie != null) request.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader input = new StreamReader(response.GetResponseStream()))
                {
                    char[] buffer = new char[65537]; int count = 0, read;
                    while (count < buffer.Length && (read = input.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                    if (count >= buffer.Length) throw new InvalidOperationException("ข้อมูลบัญชีจาก Roblox มีขนาดเกินที่รองรับ");
                    Dictionary<string, object> result = new JavaScriptSerializer().DeserializeObject(new String(buffer, 0, count)) as Dictionary<string, object>;
                    if (result == null) throw new InvalidOperationException("อ่านข้อมูลบัญชีจาก Roblox ไม่สำเร็จ");
                    return result;
                }
            }
            catch (WebException error)
            {
                using (HttpWebResponse response = error.Response as HttpWebResponse)
                {
                    if (response == null) throw new InvalidOperationException("ติดต่อ Roblox เพื่อตรวจบัญชีไม่ได้ กรุณาตรวจอินเทอร์เน็ตแล้วลองใหม่");
                    int code = (int)response.StatusCode;
                    throw new InvalidOperationException(code == 401 || code == 403 ?
                        "Roblox ยังไม่ยืนยันบัญชี กรุณาทำ CAPTCHA และรหัสยืนยันให้ครบ" :
                        "Roblox ตรวจบัญชีไม่สำเร็จ (HTTP " + code + ") กรุณารอสักครู่แล้วลองใหม่");
                }
            }
            catch (ArgumentException) { throw new InvalidOperationException("Roblox ส่งข้อมูลบัญชีกลับมาในรูปแบบที่อ่านไม่ได้"); }
        }
    }

    public sealed class RobloxLoginForm : Form
    {
        private readonly Func<RobloxLoginAccount, string> importAccount;
        private readonly Label message = new Label(), countdown = new Label(), identity = new Label();
        private readonly Button import = new Button(), close = new Button();
        private readonly Panel browserHost = new Panel();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private WebView2 browser;
        private CoreWebView2Environment environment;
        private EventHandler<CoreWebView2BrowserProcessExitedEventArgs> browserExitHandler;
        private ManualResetEvent browserExited;
        private string profile, attemptedCookie;
        private RobloxLoginAccount account;
        private DateTime deadline, nextValidation;
        private bool readingCookie, importing, closed, expired, initializing;
        private int generation;
        private readonly bool previewOnly;

        public RobloxLoginForm(Func<RobloxLoginAccount, string> importAccount) : this(importAccount, false) { }

        internal RobloxLoginForm(Func<RobloxLoginAccount, string> importAccount, bool previewOnly)
        {
            if (importAccount == null) throw new ArgumentNullException("importAccount");
            this.importAccount = importAccount; this.previewOnly = previewOnly;
            Text = "ล็อกอิน Roblox ใน Nexus";
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1000, 760); MinimumSize = new Size(800, 650);
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(15, 23, 36);
            ForeColor = Color.White; Font = new Font("Segoe UI", 10);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            message.Dock = DockStyle.Fill; message.Text = "ล็อกอินบนเว็บไซต์ Roblox ตามปกติ\nรอ CAPTCHA และรหัส 2 ชั้นได้สูงสุด 15 นาที";
            countdown.Dock = DockStyle.Fill; countdown.TextAlign = ContentAlignment.TopRight; countdown.ForeColor = Color.FromArgb(125, 211, 252); countdown.Text = "15:00";
            header.Controls.Add(message, 0, 0); header.Controls.Add(countdown, 1, 0); layout.Controls.Add(header, 0, 0);
            browserHost.Dock = DockStyle.Fill; browserHost.BackColor = Color.White; browserHost.Name = "RobloxBrowserHost";
            layout.Controls.Add(browserHost, 0, 1);
            identity.Dock = DockStyle.Fill; identity.TextAlign = ContentAlignment.MiddleLeft; identity.Text = "เมื่อยืนยันบัญชีสำเร็จ ชื่อบัญชีจะแสดงที่นี่ให้ตรวจสอบก่อนเพิ่มเข้า Real";
            layout.Controls.Add(identity, 0, 2);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            LinkLabel license = new LinkLabel { Text = "ใบอนุญาต Microsoft WebView2", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, LinkColor = Color.FromArgb(125, 211, 252) };
            license.LinkClicked += delegate { MessageBox.Show(this, WebViewRuntime.ReadLicense(), "Microsoft WebView2", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            import.Text = "เพิ่มบัญชีนี้เข้า Real"; import.Name = "ImportRobloxAccountButton"; import.Enabled = false;
            close.Text = "ยกเลิก"; close.Name = "CancelRobloxLoginButton";
            foreach (Button button in new Button[] { import, close })
            { button.Dock = DockStyle.Fill; button.FlatStyle = FlatStyle.Flat; button.ForeColor = Color.White; button.BackColor = Color.FromArgb(51, 65, 85); button.Margin = new Padding(4); }
            import.BackColor = Color.FromArgb(2, 132, 199);
            import.Click += ImportClick; close.Click += delegate { Close(); };
            actions.Controls.Add(license, 0, 0); actions.Controls.Add(import, 1, 0); actions.Controls.Add(close, 2, 0); layout.Controls.Add(actions, 0, 3);
            timer.Interval = 1000; timer.Tick += Tick;
            Shown += async delegate { if (!previewOnly) await StartBrowser(); };
        }

        private async Task StartBrowser()
        {
            int currentGeneration = ++generation;
            deadline = DateTime.UtcNow.AddMinutes(15); timer.Start();
            initializing = true;
            WebViewRuntime.BeginProfileInitialization();
            try
            {
                WebViewRuntime.Register();
                WebViewRuntime.ConfigureLoader();
                profile = WebViewRuntime.CreateProfile();
                browserExited = new ManualResetEvent(false);
                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions();
                options.ExclusiveUserDataFolderAccess = true;
                environment = await CoreWebView2Environment.CreateAsync(null, profile, options);
                ManualResetEvent exitSignal = browserExited;
                EventHandler<CoreWebView2BrowserProcessExitedEventArgs> handler = null;
                handler = delegate(object sender, CoreWebView2BrowserProcessExitedEventArgs args)
                {
                    CoreWebView2Environment source = sender as CoreWebView2Environment;
                    try { if (source != null) source.BrowserProcessExited -= handler; }
                    catch (System.Runtime.InteropServices.COMException) { }
                    try { exitSignal.Set(); } catch (ObjectDisposedException) { }
                };
                browserExitHandler = handler;
                environment.BrowserProcessExited += handler;
                if (closed || currentGeneration != generation) { ShutdownBrowser(); return; }
                if (!WebViewRuntime.IsOwnedProfile(WebViewRuntime.LoginProfileRoot, profile) || !WebViewRuntime.IsMatchingProfile(profile, environment.UserDataFolder))
                    throw new InvalidOperationException("Windows กำหนดโฟลเดอร์เบราว์เซอร์ต่างจากหน้าล็อกอินชั่วคราว จึงยังเปิดล็อกอินไม่ได้");
                browser = new WebView2 { Dock = DockStyle.Fill, Name = "RobloxLoginWebView" };
                browserHost.Controls.Add(browser);
                await browser.EnsureCoreWebView2Async(environment);
                if (closed || currentGeneration != generation) { ShutdownBrowser(); return; }
                browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
                {
                    if (!IsRobloxTopLevel(args.Uri)) { args.Cancel = true; message.Text = "หน้านี้รองรับการล็อกอินผ่านเว็บไซต์ Roblox เท่านั้น"; }
                };
                browser.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args)
                { args.Handled = true; if (IsRobloxTopLevel(args.Uri)) browser.CoreWebView2.Navigate(args.Uri); };
                browser.CoreWebView2.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs args) { args.Cancel = true; };
                browser.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs args) { args.State = CoreWebView2PermissionState.Deny; };
                browser.CoreWebView2.Navigate("https://www.roblox.com/login");
            }
            catch (Exception error)
            {
                if (closed) return;
                timer.Stop(); ShutdownBrowser();
                string detail = error is WebView2RuntimeNotFoundException ?
                    "เครื่องนี้ยังไม่มี Microsoft Edge WebView2 Runtime\nติดตั้งจาก https://developer.microsoft.com/microsoft-edge/webview2/ แล้วเปิดหน้าล็อกอินใหม่" :
                    "เปิดหน้าล็อกอินไม่สำเร็จ\n" + SafeBrowserError(error);
                message.Text = detail; identity.Text = "ยังไม่มีการเพิ่มบัญชีเข้า Real";
            }
            finally
            {
                initializing = false;
                try { if (closed || expired || browser == null) ShutdownBrowser(); }
                finally { WebViewRuntime.EndProfileInitialization(); }
            }
        }

        internal static bool IsRobloxTopLevel(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsRobloxCookie(string name, string domain, bool secure)
        {
            string host = (domain ?? "").TrimStart('.');
            return secure && name == ".ROBLOSECURITY" &&
                (host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase));
        }

        private static string SafeBrowserError(Exception error)
        {
            if (error is InvalidOperationException || error is IOException || error is UnauthorizedAccessException) return error.Message;
            return "กรุณาปิดหน้านี้แล้วเปิดใหม่ หากยังไม่ได้ให้ตรวจ Microsoft Edge WebView2 Runtime";
        }

        private async void Tick(object sender, EventArgs args)
        {
            if (closed || expired || account != null) return;
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                expired = true; timer.Stop(); ++generation; ShutdownBrowser();
                message.Text = "ครบเวลารอ 15 นาทีแล้ว กรุณาปิดหน้านี้และเปิดล็อกอินใหม่";
                countdown.Text = "หมดเวลา"; identity.Text = "ยังไม่มีการเพิ่มบัญชีเข้า Real"; return;
            }
            countdown.Text = remaining.Minutes.ToString("00") + ":" + remaining.Seconds.ToString("00");
            if (readingCookie || browser == null || browser.CoreWebView2 == null || DateTime.UtcNow < nextValidation) return;
            readingCookie = true; int currentGeneration = generation;
            try
            {
                List<CoreWebView2Cookie> cookies = await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
                if (closed || expired || currentGeneration != generation || DateTime.UtcNow >= deadline) return;
                foreach (CoreWebView2Cookie candidate in cookies)
                {
                    if (!IsRobloxCookie(candidate.Name, candidate.Domain, candidate.IsSecure) || String.IsNullOrEmpty(candidate.Value)) continue;
                    string cookie = candidate.Value;
                    if (cookie == attemptedCookie && DateTime.UtcNow < nextValidation) return;
                    attemptedCookie = cookie; nextValidation = DateTime.UtcNow.AddSeconds(12);
                    message.Text = "พบการล็อกอินแล้ว กำลังตรวจสอบบัญชีกับ Roblox...";
                    RobloxLoginAccount found = await Task.Run(delegate { return RobloxLoginApi.AuthenticateCookie(cookie); });
                    if (closed || expired || currentGeneration != generation || DateTime.UtcNow >= deadline)
                    { found.ForgetCookie(); return; }
                    account = found; attemptedCookie = null; timer.Stop();
                    countdown.Text = "ยืนยันแล้ว";
                    identity.Text = "บัญชี @" + account.Username + "  ·  " + account.DisplayName + "\nRoblox User ID: " + account.UserId;
                    message.Text = "ตรวจสอบชื่อบัญชีด้านล่าง แล้วกดเพิ่มบัญชีนี้เข้า Real\nNexus จะปิด Real ให้ก่อนนำเข้าบัญชี";
                    import.Enabled = true; ShutdownBrowser();
                    browserHost.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.FromArgb(24, 39, 57), ForeColor = Color.FromArgb(125, 211, 252),
                        Text = "ล็อกอินสำเร็จ\n\n@" + account.Username + "\n\nตรวจชื่อบัญชี แล้วกดเพิ่มบัญชีนี้เข้า Real ด้านล่าง" });
                    return;
                }
            }
            catch (Exception error)
            {
                if (!closed && !expired) message.Text = error is InvalidOperationException ? error.Message : "ยังตรวจบัญชีไม่สำเร็จ จะลองตรวจอีกครั้งในสักครู่";
                nextValidation = DateTime.UtcNow.AddSeconds(12);
            }
            finally { readingCookie = false; }
        }

        private async void ImportClick(object sender, EventArgs args)
        {
            if (importing || closed || account == null || expired) return;
            importing = true; import.Enabled = close.Enabled = false;
            message.Text = "กำลังปิด Real และนำเข้าบัญชีที่เลือก...";
            try
            {
                RobloxLoginAccount selected = account;
                string result = await Task.Run(delegate { return importAccount(selected); });
                selected.ForgetCookie(); account = null;
                message.Text = result; identity.Text += "\nนำเข้าบัญชีแล้ว เปิด Real เพื่อใช้งานได้";
                close.Text = "ปิดหน้านี้";
            }
            catch (Exception error)
            {
                message.Text = "ยังนำเข้าบัญชีไม่ได้\n" + SafeBrowserError(error);
                import.Enabled = account != null;
            }
            finally { importing = false; close.Enabled = true; }
        }

        private void ShutdownBrowser()
        {
            WebView2 old = browser; browser = null;
            if (old != null)
            {
                try { if (old.CoreWebView2 != null) old.CoreWebView2.CookieManager.DeleteAllCookies(); } catch (InvalidOperationException) { }
                browserHost.Controls.Remove(old); old.Dispose();
            }
            if (initializing) return;
            if (profile != null)
            {
                string oldProfile = profile; profile = null;
                ManualResetEvent signal = browserExited; browserExited = null;
                if (signal != null)
                {
                    // CreateAsync can complete without ever starting a browser process.
                    // In that case there will be no BrowserProcessExited event to await.
                    bool noBrowser = environment == null;
                    if (environment != null)
                    {
                        try
                        {
                            noBrowser = !WebViewRuntime.IsMatchingProfile(oldProfile, environment.UserDataFolder) ||
                                environment.GetProcessInfos().Count == 0;
                        }
                        catch (InvalidOperationException) { }
                        catch (System.Runtime.InteropServices.COMException) { }
                    }
                    if (noBrowser)
                    {
                        if (environment != null && browserExitHandler != null)
                        {
                            try { environment.BrowserProcessExited -= browserExitHandler; }
                            catch (System.Runtime.InteropServices.COMException) { }
                        }
                        signal.Set();
                    }
                }
                WebViewRuntime.QueueProfileCleanup(oldProfile, signal);
            }
            browserExitHandler = null;
            environment = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (importing && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; base.OnFormClosing(args); return; }
            closed = true; ++generation; timer.Stop(); attemptedCookie = null;
            if (account != null) { account.ForgetCookie(); account = null; }
            ShutdownBrowser(); base.OnFormClosing(args);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closed = true; ++generation;
                timer.Stop(); timer.Dispose();
                if (account != null) account.ForgetCookie();
                ShutdownBrowser();
            }
            base.Dispose(disposing);
        }
    }
}
