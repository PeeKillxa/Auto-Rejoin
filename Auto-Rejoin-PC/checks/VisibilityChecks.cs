using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using NexusRobloxManager;

static class VisibilityChecks
{
    private static int count;
    private static void Assert(bool ok, string label)
    { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    private sealed class TestWindow : Form
    { protected override bool ShowWithoutActivation { get { return true; } } }

    public static int Run()
    {
        WatchdogEngine.WatchdogEnabled = false;
        WatchdogEngine.SetAllHidden(true);
        AccountSlot account = WatchdogEngine.Accounts[0];
        Assert(WatchdogEngine.WantsHidden(account) && WatchdogEngine.ShouldHideProcess(999999, DateTime.UtcNow),
            "Hide All applies to current accounts and future replacement processes");
        Assert(!WatchdogEngine.WatchdogEnabled, "changing visibility preserves a paused watchdog");
        account.VisibilityPreferenceSet = true; account.HiddenByUser = false;
        account.KnownProcesses[777] = DateTime.UtcNow;
        Assert(!WatchdogEngine.ShouldHideProcess(777, account.KnownProcesses[777]), "explicitly shown account overrides Hide New Clients");
        var pending = new RobloxWin { Pid = 555, StartTime = DateTime.Now };
        WatchdogEngine.RememberWindowVisibility(pending, false);
        Assert(!WatchdogEngine.ShouldHideProcess(555, pending.StartTime.ToUniversalTime()), "showing an unidentified client survives later show events");
        Assert(WatchdogEngine.ShouldHideProcess(555, pending.StartTime.AddMinutes(2).ToUniversalTime()), "PID reuse does not inherit an old visibility override");
        WatchdogEngine.SetAllHidden(true);
        var saved = new JavaScriptSerializer().Deserialize<ManagedSession[]>(File.ReadAllText(WatchdogEngine.SessionFilePath));
        Assert(Array.TrueForAll(saved, delegate(ManagedSession x) { return x.VisibilityPreferenceSet && x.HiddenByUser; }),
            "hidden preference is persisted per account for rejoin and manager restart");
        account.HiddenByUser = false; account.VisibilityPreferenceSet = false;
        typeof(WatchdogEngine).GetMethod("LoadManagedSessions", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Assert(account.VisibilityPreferenceSet && account.HiddenByUser, "manager reload restores saved visibility before trying to resume a client");
        var other = WatchdogEngine.Accounts[1];
        account.LastSeenRunningUtc = DateTime.UtcNow; account.ExecutablePath = "removed-version";
        other.LastSeenRunningUtc = DateTime.UtcNow; other.ExecutablePath = "removed-version";
        other.VisibilityPreferenceSet = true; other.HiddenByUser = false;
        typeof(WatchdogEngine).GetMethod("SaveManagedSessions", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        account.VisibilityPreferenceSet = false; other.VisibilityPreferenceSet = false; other.HiddenByUser = true;
        typeof(WatchdogEngine).GetMethod("LoadManagedSessions", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Assert(account.VisibilityPreferenceSet && account.HiddenByUser && other.VisibilityPreferenceSet && !other.HiddenByUser,
            "missing installed versions do not lose subsequent accounts' hidden/visible preferences");
        WatchdogEngine.SetAllHidden(false);
        Assert(!WatchdogEngine.WantsHidden(account) && !WatchdogEngine.HideNewClients, "Show All disables future automatic hiding");

        using (var form = new TestWindow())
        {
            form.Text = "Nexus visibility check";
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000);
            form.Size = new Size(300, 200); form.ShowInTaskbar = true; form.Show();
            Application.DoEvents();
            var window = new RobloxWin { Hwnd = form.Handle, Pid = Process.GetCurrentProcess().Id, StartTime = Process.GetCurrentProcess().StartTime };
            int originalAppStyle = Native.GetWindowLong(form.Handle, Native.GWL_EXSTYLE) & Native.WS_EX_APPWINDOW;
            WindowEngine.SetStealthHide(window, true, false);
            Assert(WindowVisibility.IsStealthStyleApplied(form.Handle), "real native window is transparent, click-through and excluded from Taskbar/Alt+Tab");
            Assert(Native.IsWindowVisible(form.Handle) && !Native.IsIconic(form.Handle), "hidden client remains running as a non-minimized window");
            int ticks = 0;
            using (var timer = new System.Windows.Forms.Timer())
            {
                timer.Interval = 10; timer.Tick += delegate { ticks++; }; timer.Start();
                Stopwatch wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 120) { Application.DoEvents(); Thread.Sleep(5); }
            }
            Assert(ticks > 0, "window message loop continues while hidden");
            int resetStyle = (Native.GetWindowLong(form.Handle, Native.GWL_EXSTYLE) | Native.WS_EX_APPWINDOW) & ~Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLong(form.Handle, Native.GWL_EXSTYLE, resetStyle);
            Assert(!WindowVisibility.IsStealthStyleApplied(form.Handle), "shell style reset can be detected");
            account.HiddenByUser = true; account.VisibilityPreferenceSet = true; window.AccountRef = account;
            WatchdogEngine.ApplyVisibilityPreferences(new List<RobloxWin> { window });
            Assert(WindowVisibility.IsStealthStyleApplied(form.Handle), "reconciliation reapplies hidden account preference after a style reset");
            WindowEngine.SetStealthHide(window, false, false);
            int restored = Native.GetWindowLong(form.Handle, Native.GWL_EXSTYLE);
            Assert((restored & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED)) == 0 &&
                (restored & Native.WS_EX_APPWINDOW) == originalAppStyle, "showing restores opacity, interaction and original taskbar style");
        }
        using (var replacement = new TestWindow())
        {
            replacement.StartPosition = FormStartPosition.Manual; replacement.Location = new Point(-30000, -30000); replacement.Show();
            var next = new RobloxWin { Hwnd = replacement.Handle, Pid = Process.GetCurrentProcess().Id,
                StartTime = Process.GetCurrentProcess().StartTime, AccountRef = account };
            WatchdogEngine.ApplyVisibilityPreferences(new List<RobloxWin> { next });
            Assert(WindowVisibility.IsStealthStyleApplied(replacement.Handle), "new replacement HWND inherits hidden account preference");
            WindowEngine.SetStealthHide(next, false, false);
        }
        WatchdogEngine.SetAllHidden(false);
        return count;
    }
}
