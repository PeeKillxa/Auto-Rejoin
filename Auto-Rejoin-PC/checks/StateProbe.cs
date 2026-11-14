using System;
using System.Collections.Generic;
using System.Reflection;
using NexusRobloxManager;
class StateProbe {
 static void Main() {
  WatchdogEngine.DataDirectoryOverride=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"checks","diagnostics");
  WatchdogEngine.DiscoverAccountsFromEnvironment();
  var wins=WindowEngine.ScanRobloxWindows();
  WatchdogEngine.RefreshStateAndMapWindows(wins);
  foreach(var w in wins) Console.WriteLine("WINDOW pid="+w.Pid+" start="+w.StartTime.ToUniversalTime().ToString("O")+" tracker="+w.TrackerId+" user="+w.UserId+" name="+w.Username+" state="+w.BotStatus+" mapped="+(w.AccountRef!=null)+" hwnd="+w.Hwnd.ToInt64());
  foreach(var a in WatchdogEngine.Accounts)Console.WriteLine("ACCOUNT uid="+a.UserId+" name="+a.Username+" pid="+a.CurrentPid+" heartbeat="+a.LastHeartbeatEpoch);
  var logs=(Dictionary<string,LogSessionInfo>)typeof(WatchdogEngine).GetField("logSessionsByPath",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
  foreach(var log in logs.Values)Console.WriteLine("LOG "+System.IO.Path.GetFileName(log.FilePath)+" tracker="+log.TrackerId+" uid="+log.UserId+" name="+log.Username+" stage="+log.Stage);
 }
}
