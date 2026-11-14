using System;
using System.Collections.Generic;
using System.Diagnostics;
using NexusRobloxManager;

static class GuardChecks
{
    static int count;
    static void Assert(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        count++; Console.WriteLine("PASS " + label);
    }
    static int Probe(string name)
    {
        var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--instancegate-probe " + name);
        start.UseShellExecute = false;
        using (var process = Process.Start(start))
        {
            if (!process.WaitForExit(10000)) throw new Exception("Instance gate child timed out");
            return process.ExitCode;
        }
    }
    public static int Run()
    {
        string name = @"Local\NexusChecks." + Guid.NewGuid().ToString("N");
        InstanceGate owner;
        Assert(InstanceGate.TryAcquire(name, out owner), "first manager acquires instance ownership");
        using (owner) Assert(Probe(name) == 1, "second process is blocked by the same named instance gate");
        Assert(Probe(name) == 0, "new manager can acquire ownership after the old process exits");

        var old = new List<ClientProcessIdentity> { new ClientProcessIdentity { Pid = 11 }, new ClientProcessIdentity { Pid = 22 } };
        var events = new List<string>();
        RejoinExitBarrier.CloseAndConfirm(delegate { return new List<ClientProcessIdentity>(old); }, delegate(ClientProcessIdentity p)
        { events.Add("close:" + p.Pid); old.Remove(p); }, delegate { events.Add("release"); });
        events.Add("launch");
        Assert(string.Join(",", events.ToArray()) == "close:11,close:22,release,launch", "all duplicate clients close before session delay and replacement launch");

        bool blocked = false, waited = false;
        old.Add(new ClientProcessIdentity { Pid = 33 });
        try
        {
            RejoinExitBarrier.CloseAndConfirm(delegate { return new List<ClientProcessIdentity>(old); }, delegate { }, delegate { waited = true; });
        }
        catch (InvalidOperationException) { blocked = true; }
        Assert(blocked && !waited, "old process surviving termination blocks replacement");

        blocked = false;
        old.Clear();
        try
        {
            RejoinExitBarrier.CloseAndConfirm(delegate { return new List<ClientProcessIdentity>(old); }, delegate { }, delegate { old.Add(new ClientProcessIdentity { Pid = 44 }); });
        }
        catch (InvalidOperationException) { blocked = true; }
        Assert(blocked, "client appearing during session-release delay blocks replacement");

        blocked = false;
        try { RejoinExitBarrier.AssertClear(delegate { return new List<ClientProcessIdentity>(old); }); }
        catch (InvalidOperationException) { blocked = true; }
        Assert(blocked, "final check after authentication blocks a newly appeared client");

        blocked = false;
        try
        {
            RejoinExitBarrier.CloseAndConfirm(delegate { throw new InvalidOperationException("inventory unavailable"); }, delegate { }, delegate { });
        }
        catch (InvalidOperationException) { blocked = true; }
        Assert(blocked, "unreadable process inventory blocks replacement instead of assuming exit");

        blocked = false;
        try
        {
            RejoinExitBarrier.CloseAndConfirm(delegate { return new List<ClientProcessIdentity>(old); }, delegate { throw new InvalidOperationException("exit timed out"); }, delegate { });
        }
        catch (InvalidOperationException) { blocked = true; }
        Assert(blocked, "exit timeout cannot fall through to launch");
        return count;
    }
}
