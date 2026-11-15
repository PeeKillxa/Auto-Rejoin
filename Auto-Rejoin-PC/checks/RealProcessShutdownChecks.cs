using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NexusRobloxManager;

static class RealProcessShutdownChecks
{
    private static int count;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        count++; Console.WriteLine("PASS " + message);
    }

    private sealed class FakeBackend : RealProcessShutdown.IBackend
    {
        internal readonly List<RealProcessShutdown.ProcRecord> Processes = new List<RealProcessShutdown.ProcRecord>();
        internal readonly List<string> Events = new List<string>();
        internal Func<List<RealProcessShutdown.ProcRecord>> ScanAction;
        internal Func<RealProcessShutdown.ProcRecord, RealProcessShutdown.Outcome> GentleAction, KillAction;
        internal long Clock;
        internal int Scans;
        public long NowMilliseconds { get { return Clock; } }
        public void Delay(int milliseconds) { Clock += milliseconds; Events.Add("delay:" + milliseconds); }
        public List<RealProcessShutdown.ProcRecord> Scan()
        {
            Scans++;
            return ScanAction == null ? new List<RealProcessShutdown.ProcRecord>(Processes) : ScanAction();
        }
        public RealProcessShutdown.Outcome Gentle(RealProcessShutdown.ProcRecord process)
        {
            Events.Add("gentle:" + process.Pid + ":" + process.StartTicks);
            return GentleAction == null ? RealProcessShutdown.Outcome.Completed : GentleAction(process);
        }
        public RealProcessShutdown.Outcome Kill(RealProcessShutdown.ProcRecord process)
        {
            Events.Add("kill:" + process.Pid + ":" + process.StartTicks);
            if (KillAction != null) return KillAction(process);
            Processes.Remove(process);
            return RealProcessShutdown.Outcome.Completed;
        }
    }

    private static string InstallRoot { get { return Path.Combine(Path.GetTempPath(), "NexusShutdownChecksRoot", "Real"); } }
    private const int Session = 7;
    private static RealProcessShutdown.ProcRecord Process(int pid, string relativePath)
    {
        return new RealProcessShutdown.ProcRecord
        {
            Pid = pid, SessionId = Session, StartTicks = pid * 100L,
            ImagePath = Path.Combine(InstallRoot, relativePath)
        };
    }
    private static RealProcessShutdownResult Run(FakeBackend backend)
    { return RealProcessShutdown.Run(backend, InstallRoot, Session); }
    private static int EventCount(FakeBackend backend, string prefix)
    {
        int found = 0;
        foreach (string value in backend.Events) if (value.StartsWith(prefix, StringComparison.Ordinal)) found++;
        return found;
    }

    public static int Run()
    {
        count = 0;
        Assert(RealProcessShutdown.IsAllowedImage(Path.Combine(InstallRoot, @"real-3.1.1\Real.exe"), InstallRoot),
            "Real close accepts the installed desktop executable");
        Assert(RealProcessShutdown.IsAllowedImage(Path.Combine(InstallRoot, @"real-3.1.1\bin\Real.exe"), InstallRoot),
            "Real close accepts the installed background executable");
        Assert(!RealProcessShutdown.IsAllowedImage(Path.Combine(InstallRoot + "-other", @"real-3.1.1\Real.exe"), InstallRoot),
            "Real close cannot match a similarly named installation prefix");
        Assert(!RealProcessShutdown.IsAllowedImage(Path.Combine(InstallRoot, @"real-3.1.1\RobloxPlayerBeta.exe"), InstallRoot),
            "Real close cannot target a Roblox executable");
        Assert(!RealProcessShutdown.IsAllowedImage(Path.Combine(InstallRoot, @"real-3.1.1\other\Real.exe"), InstallRoot),
            "Real close rejects unrecognized executable subdirectories");
        Assert(!RealProcessShutdown.IsAllowedImage(Path.Combine(InstallRoot, @"arbitrary\Real.exe"), InstallRoot),
            "Real close requires a versioned Real installation directory");
        Assert(!RealProcessShutdown.IsAllowedImage(Path.Combine(InstallRoot, @"..\other\real-3.1.1\Real.exe"), InstallRoot),
            "Real close resolves path traversal before checking its installation scope");
        Assert(!RealProcessShutdown.IsAllowedImage(null, InstallRoot), "Real close rejects unknown executable paths");

        var backend = new FakeBackend();
        var result = Run(backend);
        Assert(result.Success && result.RemainingCount == 0 && EventCount(backend, "kill:") == 0,
            "already closed Real completes without terminating any process");
        Assert(backend.Scans >= 2 && backend.Clock >= 200, "Real close confirms an empty inventory after a quiet interval");

        backend = new FakeBackend();
        var outer = Process(11, @"real-3.1.1\Real.exe");
        var engine = Process(12, @"real-3.1.1\bin\Real.exe");
        backend.Processes.Add(outer); backend.Processes.Add(engine);
        backend.GentleAction = delegate(RealProcessShutdown.ProcRecord p)
        {
            if (p == outer) backend.Processes.Remove(p);
            return RealProcessShutdown.Outcome.Completed;
        };
        result = Run(backend);
        Assert(result.Success && backend.Processes.Count == 0, "Real close drains the window and its leftover background process");
        Assert(EventCount(backend, "gentle:11:") == 1 && EventCount(backend, "gentle:12:") == 1,
            "Real requests graceful closure once per process identity");
        Assert(EventCount(backend, "kill:11:") == 0 && EventCount(backend, "kill:12:") == 1 && backend.Clock >= 1500,
            "only the surviving Real backend is forced closed after the grace period");

        backend = new FakeBackend();
        var unrelated = Process(20, @"real-3.1.1\Real.exe"); unrelated.ImagePath = Path.Combine(InstallRoot + "-other", @"real-3.1.1\Real.exe");
        var otherSession = Process(21, @"real-3.1.1\Real.exe"); otherSession.SessionId = Session + 1;
        var roblox = Process(22, @"real-3.1.1\RobloxPlayerBeta.exe");
        backend.Processes.Add(unrelated); backend.Processes.Add(otherSession); backend.Processes.Add(roblox);
        result = Run(backend);
        Assert(result.Success && backend.Processes.Count == 3 && EventCount(backend, "gentle:") == 0 && EventCount(backend, "kill:") == 0,
            "Real close preserves other installations, user sessions, and Roblox processes");

        backend = new FakeBackend();
        outer = Process(31, @"real-3.1.1\Real.exe");
        var respawn = Process(32, @"real-3.1.1\bin\Real.exe");
        backend.Processes.Add(outer);
        backend.KillAction = delegate(RealProcessShutdown.ProcRecord p)
        {
            backend.Processes.Remove(p);
            if (p == outer) backend.Processes.Add(respawn);
            return RealProcessShutdown.Outcome.Completed;
        };
        result = Run(backend);
        Assert(result.Success && EventCount(backend, "kill:31:") == 1 && EventCount(backend, "kill:32:") == 1,
            "fresh Real inventory detects and closes a respawned background process");

        backend = new FakeBackend();
        outer = Process(41, @"real-3.1.1\Real.exe"); backend.Processes.Add(outer);
        backend.KillAction = delegate { return RealProcessShutdown.Outcome.Completed; };
        result = Run(backend);
        Assert(!result.Success && result.RemainingCount > 0 && backend.Clock >= 10000 && backend.Clock <= 11000,
            "termination return alone cannot report success while Real remains alive");

        backend = new FakeBackend();
        outer = Process(51, @"real-3.1.1\Real.exe"); backend.Processes.Add(outer);
        backend.GentleAction = delegate { return RealProcessShutdown.Outcome.Denied; };
        backend.KillAction = delegate { return RealProcessShutdown.Outcome.Denied; };
        result = Run(backend);
        Assert(!result.Success && result.NeedsElevation && result.RemainingCount > 0,
            "access denied requests elevation instead of pretending Real exited");

        backend = new FakeBackend();
        backend.Processes.Add(new RealProcessShutdown.ProcRecord { Pid = 61, SessionId = Session, Unknown = true, Denied = true });
        result = Run(backend);
        Assert(!result.Success && result.NeedsElevation && EventCount(backend, "kill:") == 0,
            "unreadable elevated Real identity is never terminated without verification");

        backend = new FakeBackend();
        backend.Processes.Add(new RealProcessShutdown.ProcRecord { Pid = 62, SessionId = Session, Unknown = true });
        result = Run(backend);
        Assert(!result.Success && EventCount(backend, "kill:") == 0,
            "unknown Real identity prevents a false all-closed result");

        backend = new FakeBackend();
        outer = Process(71, @"real-3.1.1\Real.exe"); backend.Processes.Add(outer);
        var reused = Process(71, @"real-3.1.1\RobloxPlayerBeta.exe"); reused.StartTicks = outer.StartTicks + 1;
        backend.KillAction = delegate(RealProcessShutdown.ProcRecord p)
        {
            backend.Processes.Remove(p); backend.Processes.Add(reused);
            return RealProcessShutdown.Outcome.IdentityChanged;
        };
        result = Run(backend);
        Assert(result.Success && backend.Processes.Contains(reused) && EventCount(backend, "kill:71:") == 1,
            "PID reuse preserves the unrelated replacement after an identity change");

        backend = new FakeBackend();
        backend.ScanAction = delegate { throw new IOException("fixture process inventory unavailable"); };
        result = Run(backend);
        Assert(!result.Success && EventCount(backend, "kill:") == 0,
            "an unreadable process inventory cannot be treated as an empty one");
        backend = new FakeBackend();
        backend.ScanAction = delegate { throw new UnauthorizedAccessException("fixture process access denied"); };
        result = Run(backend);
        Assert(!result.Success && result.NeedsElevation && EventCount(backend, "kill:") == 0,
            "unauthorized process inventory requests elevation without closing unverified targets");

        int closeCalls = 0, elevateCalls = 0;
        result = RealShutdownService.CloseWithElevation(delegate { closeCalls++; return new RealProcessShutdownResult { Success = true }; },
            delegate { elevateCalls++; return 0; });
        Assert(result.Success && closeCalls == 1 && elevateCalls == 0, "closed Real does not request unnecessary administrator approval");
        closeCalls = elevateCalls = 0;
        result = RealShutdownService.CloseWithElevation(delegate { closeCalls++; return new RealProcessShutdownResult { Success = false, NeedsElevation = false }; },
            delegate { elevateCalls++; return 0; });
        Assert(!result.Success && closeCalls == 1 && elevateCalls == 0, "ordinary close failures do not trigger administrator escalation");
        closeCalls = elevateCalls = 0;
        result = RealShutdownService.CloseWithElevation(delegate { closeCalls++; return new RealProcessShutdownResult { NeedsElevation = true }; },
            delegate { elevateCalls++; throw new Win32Exception(1223); });
        Assert(!result.Success && closeCalls == 1 && elevateCalls == 1 && result.Message.Contains("Windows"),
            "cancelling Windows approval reports incomplete Real closure");
        closeCalls = elevateCalls = 0;
        result = RealShutdownService.CloseWithElevation(delegate { closeCalls++; return new RealProcessShutdownResult { NeedsElevation = true }; },
            delegate { elevateCalls++; return 3; });
        Assert(!result.Success && closeCalls == 1 && elevateCalls == 1, "failed elevated helper cannot report Real as closed");
        closeCalls = elevateCalls = 0;
        result = RealShutdownService.CloseWithElevation(delegate
        {
            closeCalls++;
            return closeCalls == 1 ? new RealProcessShutdownResult { NeedsElevation = true } : new RealProcessShutdownResult { Success = true };
        }, delegate { elevateCalls++; return 0; });
        Assert(result.Success && closeCalls == 2 && elevateCalls == 1, "successful elevated helper is followed by fresh Real verification");
        closeCalls = elevateCalls = 0;
        result = RealShutdownService.CloseWithElevation(delegate
        {
            closeCalls++;
            return closeCalls == 1 ? new RealProcessShutdownResult { NeedsElevation = true } : new RealProcessShutdownResult { RemainingCount = 1 };
        }, delegate { elevateCalls++; return 0; });
        Assert(!result.Success && closeCalls == 2 && result.RemainingCount == 1, "Real respawning after the helper prevents a false success result");
        Assert(RealShutdownService.RunHelper("S-1-0-0") == 4, "elevated helper rejects a different Windows identity before touching Real");

        NativeFixture();
        return count;
    }

    private static void NativeFixture()
    {
        int session;
        using (var current = System.Diagnostics.Process.GetCurrentProcess()) session = current.SessionId;
        var preflight = new RealProcessShutdown.WindowsBackend(InstallRoot).Scan();
        foreach (var candidate in preflight)
            if (candidate.SessionId == session && candidate.Unknown)
            {
                Console.WriteLine("SKIP native Real shutdown fixture: existing named process identity is unreadable");
                return;
            }
        string fixture = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NexusShutdownNative-" + Guid.NewGuid().ToString("N")));
        string root = Path.Combine(fixture, "Real"), version = Path.Combine(root, "real-99.0.0");
        Directory.CreateDirectory(Path.Combine(version, "bin"));
        string source = Path.Combine(fixture, "fixture.cs"), outerPath = Path.Combine(version, "Real.exe"), backendPath = Path.Combine(version, "bin", "Real.exe");
        File.WriteAllText(source, "using System.Threading;class R{static void Main(){while(true)Thread.Sleep(100);}}");
        string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
        if (!File.Exists(compiler)) compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
        var compile = new ProcessStartInfo(compiler, "/nologo /target:winexe /out:\"" + outerPath + "\" \"" + source + "\"");
        compile.UseShellExecute = false; compile.CreateNoWindow = true; compile.RedirectStandardOutput = true; compile.RedirectStandardError = true;
        using (var process = System.Diagnostics.Process.Start(compile))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("Benign Real shutdown fixture compilation failed: " + output);
        }
        File.Copy(outerPath, backendPath);
        string guardPath = Path.Combine(version, "RobloxPlayerBeta.exe");
        File.Copy(outerPath, guardPath);
        System.Diagnostics.Process outer = null, engine = null, guard = null;
        try
        {
            outer = System.Diagnostics.Process.Start(new ProcessStartInfo(outerPath) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            engine = System.Diagnostics.Process.Start(new ProcessStartInfo(backendPath) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            guard = System.Diagnostics.Process.Start(new ProcessStartInfo(guardPath) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            Thread.Sleep(150);
            var result = RealProcessShutdown.Run(new RealProcessShutdown.WindowsBackend(root), root, session);
            Assert(result.Success && result.RemainingCount == 0 && outer.WaitForExit(1000) && engine.WaitForExit(1000),
                "native Real close terminates both benign installed-like executable copies and confirms their exit");
            Assert(!guard.HasExited, "native Real closure preserves a concurrently running benign Roblox-named process");
            var after = new RealProcessShutdown.WindowsBackend(root).Scan();
            bool preserved = true;
            foreach (var earlier in preflight)
            {
                if (earlier.SessionId != session || earlier.Unknown || RealProcessShutdown.IsAllowedImage(earlier.ImagePath, root)) continue;
                bool same = false;
                foreach (var later in after)
                    if (later.Pid == earlier.Pid && later.StartTicks == earlier.StartTicks) same = true;
                if (!same) preserved = false;
            }
            Assert(preserved, "native fixture closure leaves existing Real processes outside its test installation unchanged");
        }
        finally
        {
            foreach (var process in new System.Diagnostics.Process[] { outer, engine, guard })
                if (process != null)
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                    process.Dispose();
                }
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fixture.StartsWith(temp + "NexusShutdownNative-", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Refusing cleanup outside the native shutdown fixture directory");
            Directory.Delete(fixture, true);
        }
    }
}
