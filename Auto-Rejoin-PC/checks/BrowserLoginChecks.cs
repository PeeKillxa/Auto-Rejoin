using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using NexusRobloxManager;

static class BrowserLoginChecks
{
    private static int count;
    private static void Assert(bool value, string message)
    { if (!value) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }

    private static byte[] Resource(string name)
    {
        using (Stream input = typeof(Program).Assembly.GetManifestResourceStream(name))
        {
            if (input == null) throw new Exception("Missing browser resource: " + name);
            using (MemoryStream output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
        }
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static int PeMachine(byte[] bytes)
    {
        if (bytes.Length < 64 || bytes[0] != 77 || bytes[1] != 90) return 0;
        int pe = BitConverter.ToInt32(bytes, 60);
        if (pe < 0 || pe > bytes.Length - 6 || bytes[pe] != 80 || bytes[pe + 1] != 69 || bytes[pe + 2] != 0 || bytes[pe + 3] != 0) return 0;
        return BitConverter.ToUInt16(bytes, pe + 4);
    }

    public static int Run()
    {
        count = 0;
        byte[] core = Resource("Nexus.WebView2.Core.dll"), forms = Resource("Nexus.WebView2.WinForms.dll");
        byte[] x64 = Resource("Nexus.WebView2.Loader.x64.dll"), x86 = Resource("Nexus.WebView2.Loader.x86.dll");
        Assert(Hash(core) == "acdd517b951829f763d1255f32df1e6a936cb8623a7ebff6b8b24172dede6bd6", "embedded browser core matches the reviewed Microsoft SDK payload");
        Assert(Hash(forms) == "c66437f80886aecf074804b61af4cc10ad90a95310c8e0a717f31ea377c26b03", "embedded browser WinForms wrapper matches the reviewed Microsoft SDK payload");
        Assert(Hash(x64) == "d3fce90bdeecb08596e377dea6a316354dd8949e32ff09b89d51be00c14e21b6", "embedded x64 browser loader matches the reviewed Microsoft SDK payload");
        Assert(Hash(x86) == "1d348d3c0a5cb0d985389b68a0fd9bf966c2aed65f5e44bbc0233739b7a57f9e", "embedded x86 browser loader matches the reviewed Microsoft SDK payload");
        Assert(PeMachine(x64) == 0x8664 && PeMachine(x86) == 0x14c, "browser loader resources retain their distinct native architectures");
        string license = Encoding.UTF8.GetString(Resource("Nexus.WebView2.License.txt"));
        Assert(license.Contains("Copyright (C) Microsoft Corporation") && license.Contains("Redistribution and use"), "single-executable browser distribution retains the Microsoft license notice");
        WebViewRuntime.Register();
        Assembly embeddedCore = Assembly.Load("Microsoft.Web.WebView2.Core");
        Assembly embeddedForms = Assembly.Load("Microsoft.Web.WebView2.WinForms");
        Assert(String.IsNullOrEmpty(embeddedCore.Location) && embeddedCore.GetName().Version.ToString() == WebViewRuntime.SdkVersion,
            "browser core resolves from the signed application resources without a sidecar DLL");
        Assert(String.IsNullOrEmpty(embeddedForms.Location), "browser WinForms wrapper resolves from memory without a sidecar DLL");
        ProfileChecks();
        OriginChecks();
        IdentityChecks();
        PreviewChecks();
        return count;
    }

    private static void ProfileChecks()
    {
        string fixture = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "checks", "runtime", "browser-guards-" + Guid.NewGuid().ToString("N")));
        string root = Path.Combine(fixture, "owned"), outside = Path.Combine(fixture, "outside");
        string profile = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile); Directory.CreateDirectory(outside);
        string sentinel = Path.Combine(outside, "preserve.txt"); File.WriteAllText(sentinel, "unrelated fixture");
        try
        {
            Assert(WebViewRuntime.IsMatchingProfile(profile, profile.ToUpperInvariant() + Path.DirectorySeparatorChar), "browser profile matching accepts the same canonical Windows path");
            Assert(!WebViewRuntime.IsMatchingProfile(profile, profile + "-other"), "browser profile matching rejects a similarly named folder");
            Assert(!WebViewRuntime.IsMatchingProfile(profile, outside), "browser refuses a runtime redirect to another profile");
            Assert(WebViewRuntime.IsOwnedProfile(root, profile), "browser cleanup accepts only its immediate GUID session folder");
            Assert(!WebViewRuntime.IsOwnedProfile(root, root), "browser cleanup cannot delete its profile root");
            Assert(!WebViewRuntime.IsOwnedProfile(root, Path.Combine(outside, Guid.NewGuid().ToString("N"))), "browser cleanup rejects a GUID folder outside its root");
            Assert(!WebViewRuntime.IsOwnedProfile(root, Path.Combine(profile, Guid.NewGuid().ToString("N"))), "browser cleanup rejects a nested session path");
            Assert(!WebViewRuntime.IsOwnedProfile(root, Path.Combine(root, "not-a-session")), "browser cleanup rejects an arbitrary folder name");
            WebViewRuntime.DeleteOwnedProfile(root, outside);
            Assert(File.Exists(sentinel), "browser cleanup leaves an unrelated folder intact");
            File.WriteAllText(Path.Combine(profile, "synthetic-cache.txt"), "inert fixture");
            WebViewRuntime.DeleteOwnedProfile(root, profile);
            Assert(!Directory.Exists(profile) && File.Exists(sentinel), "browser cleanup removes its owned fixture and preserves outside data");
        }
        finally
        {
            string expected = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "checks", "runtime")) + Path.DirectorySeparatorChar;
            if (!fixture.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refusing cleanup outside the browser fixture directory");
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
        }
    }

    private static void OriginChecks()
    {
        Assert(RobloxLoginForm.IsRobloxTopLevel("https://www.roblox.com/login") && RobloxLoginForm.IsRobloxTopLevel("https://auth.roblox.com/"), "browser login accepts HTTPS Roblox top-level pages");
        Assert(!RobloxLoginForm.IsRobloxTopLevel("https://roblox.com.example.invalid/login") && !RobloxLoginForm.IsRobloxTopLevel("https://evilroblox.com/login"), "browser login rejects lookalike Roblox host names");
        Assert(!RobloxLoginForm.IsRobloxTopLevel("http://www.roblox.com/login") && !RobloxLoginForm.IsRobloxTopLevel("file:///C:/example.html"), "browser login rejects insecure and local top-level navigation");
        Assert(RobloxLoginForm.IsRobloxCookie(".ROBLOSECURITY", ".roblox.com", true), "browser login accepts its secure first-party session cookie");
        Assert(!RobloxLoginForm.IsRobloxCookie(".ROBLOSECURITY", ".roblox.com.evil.invalid", true) && !RobloxLoginForm.IsRobloxCookie(".ROBLOSECURITY", ".evilroblox.com", true), "browser login cannot select a lookalike domain cookie");
        Assert(!RobloxLoginForm.IsRobloxCookie(".ROBLOSECURITY", ".roblox.com", false) && !RobloxLoginForm.IsRobloxCookie("unrelated", ".roblox.com", true), "browser login ignores insecure and unrelated cookies");
        bool rejected = false;
        try { RobloxLoginApi.AuthenticateCookie("invalid;header"); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "browser authentication rejects a header separator before making a request");
        rejected = false;
        try { RobloxLoginApi.AuthenticateCookie("invalid\r\nheader"); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "browser authentication rejects newline injection before making a request");
        rejected = false;
        try { RobloxLoginApi.AuthenticateCookie(new string('x', 32769)); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "browser authentication bounds session-cookie input size");
    }

    private static void PreviewChecks()
    {
        int imports = 0;
        Application.EnableVisualStyles();
        using (RobloxLoginForm form = new RobloxLoginForm(delegate { imports++; return "fixture"; }, true))
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000);
            form.Size = new Size(800, 650); form.Show(); Application.DoEvents(); form.PerformLayout();
            Control[] import = form.Controls.Find("ImportRobloxAccountButton", true), close = form.Controls.Find("CancelRobloxLoginButton", true);
            Control[] host = form.Controls.Find("RobloxBrowserHost", true);
            Assert(import.Length == 1 && !import[0].Enabled && close.Length == 1 && close[0].Enabled,
                "browser preview requires account verification before enabling import");
            Assert(host.Length == 1 && host[0].Controls.Count == 0 && imports == 0,
                "browser preview neither starts a browser nor imports an account");
            System.Windows.Forms.Timer timer = (System.Windows.Forms.Timer)typeof(RobloxLoginForm).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            Assert(!timer.Enabled, "browser preview does not start its login timer or cookie polling");
            string previews = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "checks", "previews"); Directory.CreateDirectory(previews);
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(previews, "roblox-login-preview.png")); }
            form.Close();
        }
    }

    private static void IdentityChecks()
    {
        Dictionary<string, object> identity = new Dictionary<string, object> { { "id", 123L }, { "name", "Fixture" } };
        Dictionary<string, object> profile = new Dictionary<string, object>
        { { "id", 123L }, { "name", "Fixture" }, { "displayName", "Fixture Display" }, { "created", "2020-01-01T00:00:00Z" } };
        RobloxLoginAccount account = RobloxLoginApi.ParseConfirmedAccount("_|WARNING:synthetic-fixture", identity, profile);
        Assert(account.UserId == 123 && account.Username == "Fixture", "browser review account matches the authenticated user and public profile");
        account.ForgetCookie();
        Assert(account.Cookie == null, "browser review can release its session cookie after import or cancellation");
        profile["id"] = 124L;
        bool rejected = false;
        try { RobloxLoginApi.ParseConfirmedAccount("fixture", identity, profile); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "browser review rejects a public profile belonging to another user");
        profile["id"] = 123L; profile["name"] = "OtherAccount"; rejected = false;
        try { RobloxLoginApi.ParseConfirmedAccount("fixture", identity, profile); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "browser review rejects an authenticated-name mismatch");
        profile["name"] = "Fixture"; profile["displayName"] = "unsafe\nlabel"; rejected = false;
        try { RobloxLoginApi.ParseConfirmedAccount("fixture", identity, profile); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "browser review rejects control characters in account display text");
        profile["displayName"] = "Fixture Display"; profile["created"] = "invalid-date"; rejected = false;
        try { RobloxLoginApi.ParseConfirmedAccount("fixture", identity, profile); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "browser review requires a usable account creation timestamp");
    }
}
