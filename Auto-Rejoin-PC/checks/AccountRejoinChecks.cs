using System;
using System.Reflection;
using System.Web.Script.Serialization;
using System.IO;
using NexusRobloxManager;
static class AccountRejoinChecks
{
    static int count;
    static void Assert(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
    public static int Run()
    {
        WatchdogEngine.ApplyRealAccountSnapshot("[{\"user_id\":1,\"username\":\"Fixture\"},{\"user_id\":2,\"username\":\"Other\"}]");
        var first=WatchdogEngine.FindOrCreateAccount("",1);var other=WatchdogEngine.FindOrCreateAccount("",2);
        WatchdogEngine.WatchdogEnabled=true;
        first.ExecutablePath="fixture";first.RejoinPending=true;first.ManualRejoin=true;
        WatchdogEngine.SetAccountRejoinEnabled(first,false);
        Assert(!first.RejoinPending&&!first.ManualRejoin&&!first.RejoinEnabled,"pausing one account cancels its queued launch");
        Assert(other.RejoinEnabled&&WatchdogEngine.WatchdogEnabled,"other accounts and global watchdog remain enabled");
        var queue=typeof(WatchdogEngine).GetMethod("QueueRejoin",BindingFlags.Static|BindingFlags.NonPublic);
        queue.Invoke(null,new object[]{first,"manual",true});Assert(!first.RejoinPending,"paused account is excluded from manual/all-account rejoin requests");
        bool cancelled=false;try{WatchdogEngine.EnsureRejoinAllowed(first);}catch(OperationCanceledException){cancelled=true;}
        Assert(cancelled,"pause is checked again before process close or replacement launch");
        var saved=new JavaScriptSerializer().Deserialize<ManagedSession[]>(File.ReadAllText(WatchdogEngine.SessionFilePath));
        Assert(Array.Exists(saved,delegate(ManagedSession x){return x.UserId==1&&!x.RejoinEnabled;}),"paused account persists even without a currently observed client");
        first.RejoinEnabled=true;
        typeof(WatchdogEngine).GetMethod("LoadManagedSessions",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        Assert(!first.RejoinEnabled,"restart restores per-account pause");
        var old=new JavaScriptSerializer().Deserialize<ManagedSession>("{\"UserId\":1}");
        Assert(old.RejoinEnabled,"older settings without rejoin field default to enabled");
        WatchdogEngine.SetAccountRejoinEnabled(first,true);WatchdogEngine.EnsureRejoinAllowed(first);
        Assert(first.RejoinEnabled,"account can be enabled again independently");
        WatchdogEngine.ApplyRealAccountSnapshot("[{\"user_id\":2,\"username\":\"Other\"}]");
        WatchdogEngine.ApplyRealAccountSnapshot("[{\"user_id\":1,\"username\":\"Fixture\"},{\"user_id\":2,\"username\":\"Other\"}]");
        Assert(WatchdogEngine.FindOrCreateAccount("",1).RejoinEnabled,"removed and re-added account starts with fresh preferences");
        return count;
    }
}
