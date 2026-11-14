using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace NexusRobloxManager
{
    public class AccountSlot
    {
        public bool RemovedFromReal = false;
        public string Username = "";
        public long UserId = 0;
        public long TrackerId = 0;

        // Runtime state
        public int CurrentPid = 0;
        public IntPtr CurrentHwnd = IntPtr.Zero;
        public string Status = "OFFLINE";
        public long PlaceId = 0;
        public long LastHeartbeatEpoch = 0;
        public int HeartbeatAgeSec = -1;
        public bool UiExists = false;
        public bool UiEnabled = false;
        public bool IsDisconnectedFlag = false;
        public int DisconnectCode = 0;
        public string DisconnectReason = "";
        public bool IsTeleportingFlag = false;

        // Auto-Restart counters
        public int RestartCount = 0;
        public string LastReason = "-";
        public DateTime LastRestartSentAt = DateTime.MinValue;
        public string ExecutablePath = "";
        public bool WasObserved = false;
        public int LastObservedPid = 0;
        public readonly Dictionary<int, DateTime> KnownProcesses = new Dictionary<int, DateTime>();
        public DateTime LastObservedStartUtc = DateTime.MinValue;
        public DateTime MissingSinceUtc = DateTime.MinValue;
        public bool RejoinPending = false;
        public bool ManualRejoin = false;
        public bool RejoinBusy = false;
        public bool StopForConflict = false;
        public DateTime NextRejoinUtc = DateTime.MinValue;
        public DateTime LaunchDeadlineUtc = DateTime.MinValue;
        public int LaunchPid = 0;
        public int Failures = 0;
        public DateTime LastSeenRunningUtc = DateTime.MinValue;
        public bool VisibilityPreferenceSet;
        public bool HiddenByUser;
        public bool RejoinEnabled = true;
    }

    public class LogSessionInfo
    {
        public string FilePath = "";
        public DateTime FileCreatedUtc = DateTime.MinValue;
        public long TrackerId = 0;
        public long UserId = 0;
        public string Username = "";
        public bool HeaderParsed = false;
        public long LastTailOffset = -1;

        public bool HasEverEnteredGame = false;
        public string Stage = "INIT"; // INIT, APP_BOOT, IN_GAME, TELEPORTING, LEFT_MAP, DISCONNECTED, ERR 276
        public DateTime StageUpdatedAtUtc = DateTime.MinValue;
        public DateTime LastTeleportAtUtc = DateTime.MinValue;
        public int DisconnectCode = 0;
        public string DisconnectReason = "";
    }

    public class PersistedConfig
    {
        public bool WatchdogEnabled = true;
        public long TargetPlaceId = 8304191830;
        public int UiTimeoutSec = 90;
        public bool HideNewClients;
    }

    public class ManagedSession
    {
        public long UserId;
        public long TrackerId;
        public string ExecutablePath;
        public DateTime LastSeenUtc;
        public bool VisibilityPreferenceSet;
        public bool HiddenByUser;
        public bool RejoinEnabled = true;
    }

    public static class WatchdogEngine
    {
        public static readonly object SyncRoot = new object();
        public static readonly List<AccountSlot> Accounts = new List<AccountSlot>();
        private static bool realAccountsKnown;
        private static Dictionary<long, string> realAccountNames = new Dictionary<long, string>();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static bool WatchdogEnabled = true;
        public static long TargetPlaceId = 8304191830;
        public static int UiTimeoutSec = 90;
        public static bool HideNewClients;
        private static readonly Dictionary<int, ManagedSession> pendingVisibility = new Dictionary<int, ManagedSession>();

        public static Action<string> OnBannerLog;
        public static string DataDirectoryOverride;

        private static readonly Dictionary<int, long> pidToTrackerCache = new Dictionary<int, long>();
        private static readonly Dictionary<string, LogSessionInfo> logSessionsByPath = new Dictionary<string, LogSessionInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, DateTime> pidAbnormalSinceUtc = new Dictionary<int, DateTime>();
        private static readonly Dictionary<int, DateTime> pidKilledAtUtc = new Dictionary<int, DateTime>();
        private static readonly Dictionary<int, DateTime> pidUnresponsiveSinceUtc = new Dictionary<int, DateTime>();
        private static readonly HashSet<int> manualHomeCloseRequests = new HashSet<int>();
        private static List<RobloxWin> latestWindowsSnapshot = new List<RobloxWin>();

        private static DateTime lastLogDirScan = DateTime.MinValue;
        private static Thread watchdogThread;
        private static volatile bool running;
        private static bool updatePreparing, watchdogCycleBusy;
        private static DateTime startedUtc = DateTime.UtcNow;
        private static DateTime lastSessionSaveUtc = DateTime.MinValue;
        public static string SessionFilePath { get { return Path.Combine(DataDir, "managed_sessions.json"); } }

        public static string DataDir
        {
            get
            {
                string dir = string.IsNullOrEmpty(DataDirectoryOverride)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusRobloxManager")
                    : DataDirectoryOverride;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsFilePath
        {
            get { return Path.Combine(DataDir, "in_game_rejoin_settings.json"); }
        }

        public static string RealWorkspaceDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Real", "workspace");
            }
        }

        public static string RealAccountsJsonPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Real", "data", "accounts.json");
            }
        }

        public static string RobloxLogsDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
            }
        }

        public static void Init()
        {
            LoadSettings();
            RefreshRealTarget();
            DiscoverAccountsFromEnvironment();
            LoadManagedSessions();
            SyncWorkspaceConfigFile();

            if (watchdogThread == null)
            {
                running = true;
                watchdogThread = new Thread(WatchdogLoop);
                watchdogThread.IsBackground = true;
                watchdogThread.Name = "NexusProcessWatchdog";
                watchdogThread.Start();
            }
        }

        private static void LoadManagedSessions()
        {
            try
            {
                if (!File.Exists(SessionFilePath)) return;
                ManagedSession[] sessions = new JavaScriptSerializer().Deserialize<ManagedSession[]>(File.ReadAllText(SessionFilePath));
                lock (SyncRoot)
                {
                    foreach (ManagedSession session in sessions)
                    {
                        AccountSlot account = FindOrCreateAccount("", session.UserId);
                        if (account == null) continue;
                        account.VisibilityPreferenceSet = session.VisibilityPreferenceSet;
                        account.HiddenByUser = session.HiddenByUser;
                        account.RejoinEnabled = session.RejoinEnabled;
                        if ((DateTime.UtcNow - session.LastSeenUtc.ToUniversalTime()).TotalHours > 24) continue;
                        try { RealAccountLauncher.ValidateExecutable(session.ExecutablePath); }
                        catch { continue; } // A removed Roblox version must not discard other accounts' saved visibility.
                        account.WasObserved = true;
                        account.TrackerId = session.TrackerId;
                        account.ExecutablePath = session.ExecutablePath;
                        account.LastSeenRunningUtc = session.LastSeenUtc.ToUniversalTime();
                    }
                }
            }
            catch { }
        }

        private static void SaveManagedSessions()
        {
            try
            {
                List<ManagedSession> sessions = new List<ManagedSession>();
                foreach (AccountSlot account in Accounts)
                    if (!account.RejoinEnabled || account.VisibilityPreferenceSet || (account.WasObserved && account.LastSeenRunningUtc != DateTime.MinValue))
                        sessions.Add(new ManagedSession { UserId = account.UserId, TrackerId = account.TrackerId,
                            ExecutablePath = account.ExecutablePath, LastSeenUtc = account.LastSeenRunningUtc,
                            VisibilityPreferenceSet = account.VisibilityPreferenceSet, HiddenByUser = account.HiddenByUser, RejoinEnabled = account.RejoinEnabled });
                string temporary = SessionFilePath + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(sessions), Utf8NoBom);
                if (File.Exists(SessionFilePath)) File.Replace(temporary, SessionFilePath, null);
                else File.Move(temporary, SessionFilePath);
                lastSessionSaveUtc = DateTime.UtcNow;
            }
            catch { }
        }

        private static void RecoverRecentOfflineAccounts(List<RobloxWin> windows)
        {
            // Existing Real telemetry proves a previous active session, even if Nexus was restarted after it crashed.
            string executable = "";
            foreach (RobloxWin window in windows)
            {
                if (window.AccountRef == null && window.TrackerId > 0) return; // Wait for identity instead of opening a duplicate.
                if (window.AccountRef != null && !string.IsNullOrEmpty(window.AccountRef.ExecutablePath))
                    executable = window.AccountRef.ExecutablePath;
            }
            if (executable.Length == 0) return;
            long now = ToUnixEpoch(DateTime.UtcNow);
            foreach (AccountSlot account in Accounts)
            {
                if (account.WasObserved || account.LastHeartbeatEpoch <= 0 || now - account.LastHeartbeatEpoch > 86400) continue;
                account.WasObserved = true;
                account.ExecutablePath = executable;
                account.LastSeenRunningUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(account.LastHeartbeatEpoch);
                LogEvent("Nexus restored recent Real session @" + account.Username + "; waiting to confirm offline");
            }
        }

        public static void RefreshRealTarget()
        {
            try
            {
                long place;
                string server;
                RealAccountLauncher.ReadTarget(out place, out server);
                if (place > 0) TargetPlaceId = place;
            }
            catch { }
        }

        public static void LogEvent(string msg)
        {
            try
            {
                lock (SyncRoot)
                {
                    string path = Path.Combine(DataDir, "rejoin.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    {
                        string previous = path + ".previous";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine, Utf8NoBom);
                }
            }
            catch { }
            if (OnBannerLog != null)
            {
                try { OnBannerLog(msg); } catch { }
            }
        }

        public static void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;
                string json = File.ReadAllText(SettingsFilePath, Utf8NoBom);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                PersistedConfig cfg = ser.Deserialize<PersistedConfig>(json);
                if (cfg == null) return;
                WatchdogEnabled = cfg.WatchdogEnabled;
                HideNewClients = cfg.HideNewClients;
                if (cfg.TargetPlaceId > 0) TargetPlaceId = cfg.TargetPlaceId;
                if (cfg.UiTimeoutSec >= 45) UiTimeoutSec = cfg.UiTimeoutSec;
            }
            catch { }
        }

        public static void SaveSettings()
        {
            try
            {
                PersistedConfig cfg = new PersistedConfig();
                cfg.WatchdogEnabled = WatchdogEnabled;
                cfg.TargetPlaceId = TargetPlaceId;
                cfg.UiTimeoutSec = UiTimeoutSec;
                cfg.HideNewClients = HideNewClients;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(SettingsFilePath, ser.Serialize(cfg), Utf8NoBom);
                SyncWorkspaceConfigFile();
            }
            catch { }
        }

        public static void SyncWorkspaceConfigFile()
        {
            try
            {
                if (!Directory.Exists(RealWorkspaceDir)) Directory.CreateDirectory(RealWorkspaceDir);
                string cfgPath = Path.Combine(RealWorkspaceDir, "nexus_autorejoin_config.json");
                string json = string.Format("{{\"enabled\":{0},\"place_id\":{1},\"ui_timeout_sec\":{2},\"auto_close_on_disconnect\":false}}\n",
                    WatchdogEnabled ? "true" : "false",
                    TargetPlaceId,
                    UiTimeoutSec);
                File.WriteAllText(cfgPath, json, Utf8NoBom);
            }
            catch { }
        }

        // Only a complete, valid Real inventory can add/remove tracked accounts.
        public static bool ApplyRealAccountSnapshot(string json)
        {
            Dictionary<long, string> names = new Dictionary<long, string>();
            try
            {
                object[] rows = new JavaScriptSerializer().DeserializeObject(json) as object[];
                if (rows == null) return false;
                foreach (object row in rows)
                {
                    Dictionary<string, object> item = row as Dictionary<string, object>;
                    long uid = GetLong(item, "user_id");
                    if (item == null || uid <= 0 || names.ContainsKey(uid)) return false;
                    names.Add(uid, GetStr(item, "username"));
                }
            }
            catch { return false; }
            lock (SyncRoot)
            {
                realAccountNames = names;
                realAccountsKnown = true;
                foreach (AccountSlot account in new List<AccountSlot>(Accounts))
                {
                    if (names.ContainsKey(account.UserId)) continue;
                    account.RemovedFromReal = true;
                    account.RejoinPending = false;
                    account.ManualRejoin = false;
                    account.WasObserved = false;
                    account.LaunchPid = 0;
                    account.NextRejoinUtc = DateTime.MaxValue;
                    int pid = account.LastObservedPid;
                    pidToTrackerCache.Remove(pid);
                    pidAbnormalSinceUtc.Remove(pid);
                    pidKilledAtUtc.Remove(pid);
                    pidUnresponsiveSinceUtc.Remove(pid);
                    foreach (RobloxWin window in latestWindowsSnapshot)
                    {
                        if (window.AccountRef != account) continue;
                        window.AccountRef = null;
                        window.Username = "";
                        window.UserId = 0;
                        window.RejoinCount = 0;
                        window.BotStatus = "UNMANAGED";
                    }
                    List<string> oldLogs = new List<string>();
                    foreach (KeyValuePair<string, LogSessionInfo> log in logSessionsByPath)
                        if (log.Value.UserId == account.UserId) oldLogs.Add(log.Key);
                    foreach (string path in oldLogs) logSessionsByPath.Remove(path);
                    Accounts.Remove(account);
                    account.KnownProcesses.Clear();
                    account.CurrentPid = 0;
                    account.LastObservedPid = 0;
                    account.CurrentHwnd = IntPtr.Zero;
                    account.Status = "REMOVED";
                    account.RestartCount = 0;
                    account.Failures = 0;
                    account.TrackerId = 0;
                    account.ExecutablePath = "";
                    account.LastHeartbeatEpoch = 0;
                    account.LastSeenRunningUtc = DateTime.MinValue;
                    account.VisibilityPreferenceSet = false;
                    account.HiddenByUser = false;
                    LogEvent("Nexus stopped tracking an account removed from Real");
                    account.Username = "";
                    account.UserId = 0;
                    SaveManagedSessions();
                }
                foreach (KeyValuePair<long, string> item in names)
                {
                    AccountSlot existing = Accounts.Find(delegate(AccountSlot a) { return a.UserId == item.Key; });
                    if (existing == null) Accounts.Add(new AccountSlot { UserId = item.Key, Username = item.Value });
                    else existing.Username = item.Value;
                }
            }
            return true;
        }

        public static bool SyncRealAccounts()
        {
            try { return ApplyRealAccountSnapshot(File.ReadAllText(RealAccountsJsonPath, Utf8NoBom)); }
            catch { return false; } // A missing/half-written file is not a confirmed deletion.
        }

        public static bool IsTrackedAccount(AccountSlot account)
        {
            lock (SyncRoot)
                return realAccountsKnown && account != null && !account.RemovedFromReal &&
                    realAccountNames.ContainsKey(account.UserId) && Accounts.Contains(account);
        }

        public static void SetAccountRejoinEnabled(AccountSlot account, bool enabled)
        {
            lock (SyncRoot)
            {
                if (!IsTrackedAccount(account)) return;
                account.RejoinEnabled = enabled;
                if (!enabled)
                {
                    account.RejoinPending = false;
                    account.ManualRejoin = false;
                    account.NextRejoinUtc = DateTime.MaxValue;
                    account.MissingSinceUtc = DateTime.MinValue;
                }
                SaveManagedSessions();
            }
            LogEvent("Nexus Auto Rejoin @" + account.Username + (enabled ? " enabled" : " disabled"));
        }

        public static void EnsureRejoinAllowed(AccountSlot account)
        {
            lock (SyncRoot)
                if (updatePreparing || !IsTrackedAccount(account) || !account.RejoinEnabled || (!WatchdogEnabled && !account.ManualRejoin))
                    throw new OperationCanceledException("Rejoin is paused");
        }

        public static bool TryPrepareForUpdate()
        {
            lock (SyncRoot)
            {
                if (watchdogCycleBusy || Accounts.Exists(delegate(AccountSlot account) { return account.RejoinBusy; })) return false;
                updatePreparing = true;
                return true;
            }
        }

        public static void CancelPrepareForUpdate() { lock (SyncRoot) updatePreparing = false; }

        public static bool WantsHidden(AccountSlot account)
        {
            lock (SyncRoot) return account != null && account.VisibilityPreferenceSet ? account.HiddenByUser : HideNewClients;
        }

        public static bool ShouldHideProcess(int pid, DateTime startedUtc)
        {
            lock (SyncRoot)
            {
                ManagedSession preference;
                if (pendingVisibility.TryGetValue(pid, out preference) &&
                    Math.Abs((preference.LastSeenUtc - startedUtc).TotalSeconds) < 1) return preference.HiddenByUser;
                foreach (AccountSlot account in Accounts)
                {
                    DateTime knownStart;
                    if (account.KnownProcesses.TryGetValue(pid, out knownStart) && Math.Abs((knownStart - startedUtc).TotalSeconds) < 1)
                        return WantsHidden(account);
                    if (account.LaunchPid == pid) return WantsHidden(account);
                }
                return HideNewClients;
            }
        }

        public static void RememberWindowVisibility(RobloxWin window, bool hidden)
        {
            lock (SyncRoot)
            {
                if (window.AccountRef != null && IsTrackedAccount(window.AccountRef))
                {
                    window.AccountRef.VisibilityPreferenceSet = true;
                    window.AccountRef.HiddenByUser = hidden;
                }
                else
                    pendingVisibility[window.Pid] = new ManagedSession { HiddenByUser = hidden,
                        LastSeenUtc = window.StartTime.ToUniversalTime() };
                SaveManagedSessions();
            }
        }

        public static void SetAllHidden(bool hidden)
        {
            lock (SyncRoot)
            {
                HideNewClients = hidden;
                pendingVisibility.Clear();
                foreach (AccountSlot account in Accounts)
                {
                    account.VisibilityPreferenceSet = true;
                    account.HiddenByUser = hidden;
                }
                SaveSettings();
                SaveManagedSessions();
            }
        }

        public static void ApplyVisibilityPreferences(List<RobloxWin> windows)
        {
            foreach (RobloxWin window in windows)
            {
                bool hidden = window.AccountRef != null ? WantsHidden(window.AccountRef) : ShouldHideProcess(window.Pid, window.StartTime.ToUniversalTime());
                if (!hidden) continue;
                if (!WindowVisibility.IsStealthStyleApplied(window.Hwnd)) WindowEngine.SetStealthHide(window, true, false);
                window.IsHidden = true;
            }
        }

        public static AccountSlot FindOrCreateAccount(string username, long userId)
        {
            lock (SyncRoot)
            {
                if (!realAccountsKnown) return null;
                foreach (AccountSlot account in Accounts)
                {
                    if (userId > 0 ? account.UserId == userId :
                        !string.IsNullOrEmpty(username) && account.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                        return account;
                }
                // Logs and stale telemetry never resurrect an account removed in Real.
                return null;
            }
        }

        public static void DiscoverAccountsFromEnvironment()
        {
            SyncRealAccounts();
        }

        private static long ToUnixEpoch(DateTime utc)
        {
            if (utc == DateTime.MinValue || utc == DateTime.MaxValue) return 0;
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        // =====================================================================
        // REAL-TIME TELEMETRY, LOG SESSION ANALYZER & PID-TO-ACCOUNT MAPPER
        // =====================================================================
        public static void RefreshStateAndMapWindows(List<RobloxWin> currentWindows)
        {
            lock (SyncRoot)
            {
                SyncRealAccounts();
                DateTime nowUtc = DateTime.UtcNow;
                long nowEpoch = ToUnixEpoch(nowUtc);

                UpdatePidToTrackerMap(currentWindows);
                UpdateRobloxLogSessions();
                UpdateWorkspaceTelemetry(nowEpoch);

                foreach (AccountSlot acc in Accounts)
                {
                    acc.CurrentPid = 0;
                    acc.CurrentHwnd = IntPtr.Zero;
                }

                HashSet<string> claimedLogPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (RobloxWin w in currentWindows)
                {
                    long tracker;
                    if (pidToTrackerCache.TryGetValue(w.Pid, out tracker) && tracker > 0)
                    {
                        w.TrackerId = tracker;
                    }

                    // 1. Find exact LogSessionInfo for this window (by TrackerId first, then by Process StartTime)
                    LogSessionInfo logInfo = FindLogSessionForWindow(w, claimedLogPaths);
                    if (logInfo != null)
                    {
                        w.SessionLogPath = logInfo.FilePath;
                        claimedLogPaths.Add(logInfo.FilePath);
                        if (w.TrackerId <= 0 && logInfo.TrackerId > 0)
                            w.TrackerId = logInfo.TrackerId;
                    }

                    long uid = (logInfo != null && logInfo.UserId > 0) ? logInfo.UserId : 0;
                    string logUname = (logInfo != null) ? logInfo.Username : "";

                    AccountSlot matched = null;
                    foreach (AccountSlot candidate in Accounts)
                    {
                        if ((candidate.LaunchPid == w.Pid) || (candidate.WasObserved && candidate.TrackerId > 0 && candidate.TrackerId == w.TrackerId))
                        { matched = candidate; break; }
                    }
                    if (uid > 0 || !string.IsNullOrEmpty(logUname))
                    {
                        if (matched == null) matched = FindOrCreateAccount(logUname, uid);
                    }

                    DateTime winStartUtc = (w.StartTime != DateTime.MinValue && w.StartTime != DateTime.MaxValue)
                        ? w.StartTime.ToUniversalTime()
                        : nowUtc;
                    double procAgeSec = Math.Max(0.0, (nowUtc - winStartUtc).TotalSeconds);
                    long winStartEpoch = ToUnixEpoch(winStartUtc) - 4;

                    bool hasFreshSessionHb = false;
                    int hbAge = -1;
                    bool hbDisconnected = false;
                    bool hbTeleporting = false;
                    int hbDiscCode = 0;
                    string hbDiscReason = "";

                    if (matched != null)
                    {
                        matched.CurrentPid = w.Pid;
                        matched.CurrentHwnd = w.Hwnd;
                        matched.WasObserved = true;
                        ManagedSession visibility;
                        if (pendingVisibility.TryGetValue(w.Pid, out visibility) &&
                            Math.Abs((visibility.LastSeenUtc - winStartUtc).TotalSeconds) < 1)
                        {
                            matched.VisibilityPreferenceSet = true;
                            matched.HiddenByUser = visibility.HiddenByUser;
                            pendingVisibility.Remove(w.Pid);
                        }
                        if (!matched.VisibilityPreferenceSet)
                        {
                            matched.VisibilityPreferenceSet = true;
                            matched.HiddenByUser = HideNewClients || w.IsHidden;
                        }
                        matched.LastSeenRunningUtc = nowUtc;
                        bool newProcess = matched.LastObservedPid != w.Pid;
                        matched.LastObservedPid = w.Pid;
                        matched.MissingSinceUtc = DateTime.MinValue;
                        matched.LastObservedStartUtc = w.StartTime == DateTime.MaxValue ? nowUtc : w.StartTime.ToUniversalTime();
                        if (w.StartTime != DateTime.MinValue && w.StartTime != DateTime.MaxValue)
                            matched.KnownProcesses[w.Pid] = w.StartTime.ToUniversalTime();
                        if (newProcess || string.IsNullOrEmpty(matched.ExecutablePath))
                        {
                            try { using (Process p = Process.GetProcessById(w.Pid)) matched.ExecutablePath = p.MainModule.FileName; }
                            catch { }
                        }
                        if (w.TrackerId > 0) matched.TrackerId = w.TrackerId;

                        w.Username = !string.IsNullOrEmpty(matched.Username) ? matched.Username : ("UID:" + matched.UserId);
                        w.UserId = matched.UserId;
                        w.RejoinCount = matched.RestartCount;
                        w.AccountRef = matched;

                        // Ignore stale heartbeat/disconnect files left over from a previous process before this PID started!
                        if (matched.LastHeartbeatEpoch >= winStartEpoch && matched.LastHeartbeatEpoch > 0)
                        {
                            hasFreshSessionHb = true;
                            hbAge = (int)Math.Max(0, nowEpoch - matched.LastHeartbeatEpoch);
                            hbDisconnected = matched.IsDisconnectedFlag;
                            hbTeleporting = matched.IsTeleportingFlag;
                            hbDiscCode = matched.DisconnectCode;
                            hbDiscReason = matched.DisconnectReason;
                        }
                    }

                    w.HeartbeatAgeSec = hasFreshSessionHb ? hbAge : -1;

                    // Determine real-time Emu-grade status combining Log Session State + In-Game Heartbeat
                    DateTime killedAt;
                    bool recentlyKilled = pidKilledAtUtc.TryGetValue(w.Pid, out killedAt) && (nowUtc - killedAt).TotalSeconds < 12.0;
                    if (!recentlyKilled && matched != null && (nowUtc - matched.LastRestartSentAt).TotalSeconds < 12.0 && procAgeSec < 12.0)
                    {
                        recentlyKilled = true;
                    }

                    string logStage = (logInfo != null) ? logInfo.Stage : "INIT";
                    bool isErr276 = (logStage == "ERR 276" || hbDiscCode == 276);
                    bool isLeftMapInLog = (logStage == "LEFT_MAP") ||
                                          (logStage == "APP_BOOT" && procAgeSec >= 45.0);
                    bool isDiscInLog = (logStage == "DISCONNECTED");
                    bool isTeleportingNow = (logStage == "TELEPORTING" && (nowUtc - logInfo.LastTeleportAtUtc).TotalSeconds < 35.0) ||
                                            (hbTeleporting && hbAge >= 0 && hbAge < 35 && !isLeftMapInLog && !isDiscInLog);

                    // Heartbeat timeout: 20s during normal play (tracker pulses every 2s), 35s during teleport, 60s max initial load
                    bool staleInGameHb = hasFreshSessionHb && hbAge >= Math.Max(90, UiTimeoutSec);
                    bool stuckLoadingNoHb = !hasFreshSessionHb && logStage != "IN_GAME" &&
                        !isTeleportingNow && procAgeSec >= Math.Max(120, UiTimeoutSec);
                    bool hungWindow = false;
                    if (procAgeSec >= 120 && w.Hwnd != IntPtr.Zero && Native.IsWindow(w.Hwnd))
                    {
                        IntPtr reply;
                        bool responds = Native.SendMessageTimeout(w.Hwnd, 0, IntPtr.Zero, null,
                            Native.SMTO_ABORTIFHUNG, 250, out reply) != IntPtr.Zero;
                        hungWindow = ConfirmUnresponsiveWindow(w.Pid, responds, nowUtc);
                    }

                    string status;
                    string abnormalReason = "";

                    if (isErr276)
                    {
                        status = "ERR 276";
                    }
                    else if (recentlyKilled)
                    {
                        status = "RESTARTING";
                    }
                    else if (isLeftMapInLog || (hbDisconnected && hbDiscCode == 2850))
                    {
                        status = "LEFT_MAP";
                        abnormalReason = (logInfo != null && !string.IsNullOrEmpty(logInfo.DisconnectReason))
                            ? logInfo.DisconnectReason
                            : "ออกจากแมพกลับหน้าหลัก (Left Game)";
                    }
                    else if (hungWindow || isDiscInLog || hbDisconnected || staleInGameHb || stuckLoadingNoHb)
                    {
                        status = "DISCONNECTED";
                        if (hungWindow)
                            abnormalReason = "Roblox window Not Responding for at least 30 seconds";
                        else if (isDiscInLog && logInfo != null && !string.IsNullOrEmpty(logInfo.DisconnectReason))
                            abnormalReason = logInfo.DisconnectReason;
                        else if (hbDisconnected && !string.IsNullOrEmpty(hbDiscReason))
                            abnormalReason = hbDiscReason;
                        else if (staleInGameHb)
                            abnormalReason = string.Format("สคริปต์หยุดส่งสัญญาณ (HB ค้าง {0}s)", hbAge);
                        else
                            abnormalReason = string.Format("ค้างหน้าโหลดเข้าแมพเกิน {0}s", (int)procAgeSec);
                    }
                    else if (isTeleportingNow)
                    {
                        status = "TELEPORTING";
                    }
                    else if (hasFreshSessionHb && hbAge >= 0 && hbAge <= 22)
                    {
                        status = "FARMING";
                    }
                    else if (logStage == "IN_GAME")
                    {
                        status = "IN_GAME";
                    }
                    else
                    {
                        status = "LOADING";
                    }

                    w.BotStatus = status;
                    if (matched != null)
                    {
                        matched.Status = status;
                        if (status == "ERR 276") matched.StopForConflict = true;
                        ObserveAccountHealth(matched, status, procAgeSec);
                        matched.HeartbeatAgeSec = w.HeartbeatAgeSec;
                        if (!string.IsNullOrEmpty(abnormalReason))
                            matched.DisconnectReason = abnormalReason;
                    }

                    // Track confirmation timer for LEFT_MAP / DISCONNECTED so WatchdogLoop can close cleanly
                    if (status == "LEFT_MAP" || status == "DISCONNECTED")
                    {
                        DateTime sinceUtc;
                        if (!pidAbnormalSinceUtc.TryGetValue(w.Pid, out sinceUtc))
                        {
                            sinceUtc = nowUtc;
                            pidAbnormalSinceUtc[w.Pid] = sinceUtc;
                        }
                        w.AbnormalAgeSec = (int)Math.Max(0, (nowUtc - sinceUtc).TotalSeconds);
                    }
                    else
                    {
                        pidAbnormalSinceUtc.Remove(w.Pid);
                        w.AbnormalAgeSec = 0;
                    }
                }

                latestWindowsSnapshot = new List<RobloxWin>(currentWindows);
                RecoverRecentOfflineAccounts(currentWindows);
                if ((nowUtc - lastSessionSaveUtc).TotalSeconds >= 15) SaveManagedSessions();
            }
        }

        private static LogSessionInfo FindLogSessionForWindow(RobloxWin w, HashSet<string> claimedPaths)
        {
            // 1. Exact match by BrowserTrackerId
            if (w.TrackerId > 0)
            {
                LogSessionInfo exact = null;
                foreach (LogSessionInfo info in logSessionsByPath.Values)
                {
                    if (info.TrackerId == w.TrackerId && !claimedPaths.Contains(info.FilePath) &&
                        w.StartTime != DateTime.MinValue && w.StartTime != DateTime.MaxValue &&
                        (info.FileCreatedUtc - w.StartTime.ToUniversalTime()).TotalSeconds >= -5 &&
                        (info.FileCreatedUtc - w.StartTime.ToUniversalTime()).TotalMinutes <= 10 &&
                        (exact == null || info.FileCreatedUtc > exact.FileCreatedUtc))
                        exact = info;
                }
                if (exact != null) return exact;
            }

            // 2. Fallback match by Process StartTime vs Log File CreationTimeUtc (within 25 seconds)
            if (w.StartTime != DateTime.MinValue && w.StartTime != DateTime.MaxValue)
            {
                DateTime winUtc = w.StartTime.ToUniversalTime();
                LogSessionInfo best = null;
                double bestDiff = 25.0;
                int candidates = 0;
                foreach (LogSessionInfo info in logSessionsByPath.Values)
                {
                    if (claimedPaths.Contains(info.FilePath)) continue;
                    if (w.TrackerId > 0 && info.TrackerId > 0 && w.TrackerId != info.TrackerId) continue;
                    if (info.FileCreatedUtc == DateTime.MinValue) continue;
                    double diff = Math.Abs((info.FileCreatedUtc - winUtc).TotalSeconds);
                    if (diff <= 25.0) candidates++;
                    if (diff <= bestDiff)
                    {
                        bestDiff = diff;
                        best = info;
                    }
                }
                if (best != null && candidates == 1) return best;
            }

            return null;
        }

        private static void UpdatePidToTrackerMap(List<RobloxWin> wins)
        {
            bool needWmi = false;
            foreach (RobloxWin w in wins)
            {
                if (!pidToTrackerCache.ContainsKey(w.Pid))
                {
                    needWmi = true;
                    break;
                }
            }
            if (!needWmi) return;

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'RobloxPlayerBeta.exe'"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject obj in results)
                    {
                        try
                        {
                            int pid = Convert.ToInt32(obj["ProcessId"]);
                            string cmd = Convert.ToString(obj["CommandLine"] ?? "");
                            long tracker = 0;
                            Match m = Regex.Match(cmd, @"(?:-b\s+|browserTrackerId=|browsertrackerid:)(\d+)", RegexOptions.IgnoreCase);
                            if (m.Success) long.TryParse(m.Groups[1].Value, out tracker);
                            pidToTrackerCache[pid] = tracker;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static DateTime ParseLogFilenameTimestampUtc(FileInfo fi)
        {
            try
            {
                Match m = Regex.Match(fi.Name, @"_(\d{8}T\d{6}Z)_Player_", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    DateTime dt;
                    if (DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss'Z'",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
                    {
                        return dt;
                    }
                }
            }
            catch { }
            try { return fi.CreationTimeUtc; } catch { return DateTime.MinValue; }
        }

        private static DateTime ParseLogLineTimestampUtc(string line, DateTime fallbackUtc)
        {
            if (!string.IsNullOrEmpty(line) && line.Length >= 24 && line[4] == '-' && line[10] == 'T')
            {
                DateTime dt;
                if (DateTime.TryParse(line.Substring(0, 24), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
                {
                    return dt;
                }
            }
            return fallbackUtc;
        }

        private static void UpdateRobloxLogSessions()
        {
            try
            {
                if (!Directory.Exists(RobloxLogsDir)) return;
                DirectoryInfo di = new DirectoryInfo(RobloxLogsDir);
                FileInfo[] files = di.GetFiles("*_Player_*_last.log");
                Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });

                int parsed = 0;
                int limit = Math.Max(64, latestWindowsSnapshot.Count * 4);
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo fi = files[i];
                    if (fi.Name.IndexOf("CrashHandler", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (parsed++ >= limit) break;

                    LogSessionInfo info;
                    if (!logSessionsByPath.TryGetValue(fi.FullName, out info))
                    {
                        info = new LogSessionInfo();
                        info.FilePath = fi.FullName;
                        info.FileCreatedUtc = ParseLogFilenameTimestampUtc(fi);
                        logSessionsByPath[fi.FullName] = info;
                    }

                    // 1. Parse header (up to 2 MB, skipping WndProcessCheck spam) until TrackerId & UserId are discovered
                    if (!info.HeaderParsed)
                    {
                        ScanLogHeader(info, fi);
                    }

                    // 2. Incrementally parse appended tail lines for real-time session transitions (IN_GAME vs LEFT_MAP vs DISCONNECTED)
                    ScanLogTailIncremental(info, fi);

                    if (info.UserId > 0 || !string.IsNullOrEmpty(info.Username))
                    {
                        FindOrCreateAccount(info.Username, info.UserId);
                    }
                }
            }
            catch { }
        }

        private static void ScanLogHeader(LogSessionInfo info, FileInfo fi)
        {
            try
            {
                using (FileStream fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Utf8NoBom))
                {
                    long maxHeaderBytes = Math.Min(fs.Length, 2 * 1024 * 1024L);
                    while (!sr.EndOfStream && fs.Position < maxHeaderBytes)
                    {
                        string line = sr.ReadLine();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (line.IndexOf("WndProcessCheck", StringComparison.Ordinal) >= 0 ||
                            line.IndexOf("memoryPrioritizationCallback", StringComparison.Ordinal) >= 0)
                            continue;

                        ExtractIdentityFromLogLine(info, line);
                        ParseSessionEventFromLogLine(info, line);

                        if (info.TrackerId > 0 && info.UserId > 0 && !string.IsNullOrEmpty(info.Username))
                        {
                            break;
                        }
                    }
                }

                if (info.TrackerId > 0 && info.UserId > 0)
                {
                    info.HeaderParsed = true;
                }
            }
            catch { }
        }

        private static void ScanLogTailIncremental(LogSessionInfo info, FileInfo fi)
        {
            try
            {
                using (FileStream fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    if (info.LastTailOffset < 0 || info.LastTailOffset > len)
                    {
                        // First tail attach: scan last 384 KB to catch the most recent session state
                        info.LastTailOffset = Math.Max(0L, len - 384 * 1024L);
                    }

                    if (len <= info.LastTailOffset) return;

                    fs.Seek(info.LastTailOffset, SeekOrigin.Begin);
                    using (StreamReader sr = new StreamReader(fs, Utf8NoBom))
                    {
                        while (!sr.EndOfStream)
                        {
                            string line = sr.ReadLine();
                            if (string.IsNullOrEmpty(line)) continue;
                            if (line.IndexOf("WndProcessCheck", StringComparison.Ordinal) >= 0 ||
                                line.IndexOf("memoryPrioritizationCallback", StringComparison.Ordinal) >= 0 ||
                                line.IndexOf("Calling mi_collect", StringComparison.Ordinal) >= 0)
                                continue;

                            ExtractIdentityFromLogLine(info, line);
                            ParseSessionEventFromLogLine(info, line);
                        }
                        info.LastTailOffset = fs.Position;
                    }
                }
            }
            catch { }
        }

        private static void ExtractIdentityFromLogLine(LogSessionInfo info, string line)
        {
            if (info.TrackerId <= 0 &&
                (line.IndexOf("BrowserTrackerId=", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Match mt = Regex.Match(line, @"(?:suggestedBrowserTrackerId|browserTrackerId)=(\d+)", RegexOptions.IgnoreCase);
                if (mt.Success) long.TryParse(mt.Groups[1].Value, out info.TrackerId);
            }

            if (info.UserId <= 0 && line.IndexOf("userid:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Match mu = Regex.Match(line, @"userid:(\d+)", RegexOptions.IgnoreCase);
                if (mu.Success) long.TryParse(mu.Groups[1].Value, out info.UserId);
            }

            if ((info.UserId <= 0 || string.IsNullOrEmpty(info.Username)) &&
                line.IndexOf("UserName", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Match mj = Regex.Match(line, @"""UserId""(?:%3a|:)(\d+).*?""UserName""(?:%3a|:)""([^""%]+)""", RegexOptions.IgnoreCase);
                if (mj.Success)
                {
                    if (info.UserId <= 0) long.TryParse(mj.Groups[1].Value, out info.UserId);
                    if (string.IsNullOrEmpty(info.Username)) info.Username = mj.Groups[2].Value;
                }
            }

            // Players.<name> in a script error may name another player, never use it as account identity.
        }

        private static void ParseSessionEventFromLogLine(LogSessionInfo info, string line)
        {
            DateTime nowUtc = DateTime.UtcNow;

            // 1. Entering / Playing in a Map (UGCGame / Play Session)
            if (line.IndexOf("Entered play session.", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("! Joining game", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("setStage: (stage:UGCGame)", StringComparison.Ordinal) >= 0)
            {
                info.HasEverEnteredGame = true;
                info.Stage = "IN_GAME";
                info.StageUpdatedAtUtc = ParseLogLineTimestampUtc(line, nowUtc);
                info.DisconnectCode = 0;
                info.DisconnectReason = "";
                return;
            }

            // 2. Teleporting between places (Lobby <-> Match)
            if (line.IndexOf("UgcExperienceController: doTeleport:", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("DisconnectClientInitiated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Sending disconnect with reason: 285", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DateTime ts = ParseLogLineTimestampUtc(line, nowUtc);
                info.Stage = "TELEPORTING";
                info.StageUpdatedAtUtc = ts;
                info.LastTeleportAtUtc = ts;
                return;
            }

            // 3. Exited Map back to Roblox Desktop App Home Screen (LuaApp)
            if (line.IndexOf("returnToLuaApp: (stage:UGCGame)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("returning from game", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                info.HasEverEnteredGame = true;
                info.Stage = "LEFT_MAP";
                info.StageUpdatedAtUtc = ParseLogLineTimestampUtc(line, nowUtc);
                if (string.IsNullOrEmpty(info.DisconnectReason))
                    info.DisconnectReason = "ออกจากแมพกลับหน้าหลัก (LuaApp)";
                return;
            }

            if (line.IndexOf("Entered app session.", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("setStage: (stage:LuaApp)", StringComparison.Ordinal) >= 0)
            {
                DateTime ts = ParseLogLineTimestampUtc(line, nowUtc);
                info.StageUpdatedAtUtc = ts;
                if (info.HasEverEnteredGame)
                {
                    info.Stage = "LEFT_MAP";
                    if (string.IsNullOrEmpty(info.DisconnectReason))
                        info.DisconnectReason = "ออกจากแมพกลับหน้าหลัก (LuaApp)";
                }
                else if (info.Stage == "INIT")
                {
                    info.Stage = "APP_BOOT";
                }
                return;
            }

            // 4. Explicit Game Server Disconnect / Error Code (256, 524, 277, 267, etc.)
            if (line.IndexOf("Client has been disconnected with reason:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Sending disconnect with reason:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Lost connection with reason", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Connection lost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ID_CONNECTION_LOST", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ID_DISCONNECTION_NOTIFICATION", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Session Transition FSM: Error Occurred", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ConnectionError", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Error Code:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Ignore normal client-initiated teleport disconnect (285)
                Match disconnectMatch = Regex.Match(line, @"(?:reason:|Error Code:)\s*(\d+)", RegexOptions.IgnoreCase);
                int disconnectCode = disconnectMatch.Success ? int.Parse(disconnectMatch.Groups[1].Value) : 0;
                if (disconnectCode == 285 ||
                    line.IndexOf("DisconnectClientInitiated", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }

                DateTime ts = ParseLogLineTimestampUtc(line, nowUtc);
                if (disconnectCode == 276 ||
                    line.IndexOf("same account", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("different device", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    info.Stage = "ERR 276";
                    info.StageUpdatedAtUtc = ts;
                    info.DisconnectCode = 276;
                    info.DisconnectReason = "บัญชีถูกเปิดซ้อนจากเครื่องอื่น (Code 276)";
                    return;
                }

                info.Stage = "DISCONNECTED";
                info.DisconnectCode = disconnectCode;
                info.StageUpdatedAtUtc = ts;
                int idx = line.IndexOf("reason:", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    info.DisconnectReason = line.Substring(idx).Trim();
                else if (line.IndexOf("ID_CONNECTION_LOST", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Lost connection", StringComparison.OrdinalIgnoreCase) >= 0)
                    info.DisconnectReason = "หลุดการเชื่อมต่อจากเซิร์ฟเวอร์ (Error Code 256)";
                else
                    info.DisconnectReason = "หลุดการเชื่อมต่อจากเซิร์ฟเวอร์ (Disconnected)";
            }
        }

        private static void UpdateWorkspaceTelemetry(long nowEpoch)
        {
            if (!Directory.Exists(RealWorkspaceDir)) return;
            JavaScriptSerializer ser = new JavaScriptSerializer();

            try
            {
                foreach (string f in Directory.GetFiles(RealWorkspaceDir, "bot_session_*.json"))
                {
                    try
                    {
                        string txt = File.ReadAllText(f, Utf8NoBom);
                        Dictionary<string, object> d = ser.DeserializeObject(txt) as Dictionary<string, object>;
                        if (d == null) continue;
                        long uid = GetLong(d, "user_id");
                        string uname = GetStr(d, "username");
                        long lastSeen = GetLong(d, "last_seen");
                        if (uid > 0 || !string.IsNullOrEmpty(uname))
                        {
                            AccountSlot acc = FindOrCreateAccount(uname, uid);
                            if (acc != null && lastSeen > acc.LastHeartbeatEpoch)
                                acc.LastHeartbeatEpoch = lastSeen;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                foreach (string f in Directory.GetFiles(RealWorkspaceDir, "executor_check_*.txt"))
                {
                    try
                    {
                        string fn = Path.GetFileNameWithoutExtension(f);
                        string uname = fn.Substring("executor_check_".Length);
                        string txt = File.ReadAllText(f, Utf8NoBom).Trim();
                        Match m = Regex.Match(txt, @"working:(\d+)");
                        if (m.Success)
                        {
                            long ts;
                            if (long.TryParse(m.Groups[1].Value, out ts))
                            {
                                AccountSlot acc = FindOrCreateAccount(uname, 0);
                                if (acc != null && ts > acc.LastHeartbeatEpoch)
                                    acc.LastHeartbeatEpoch = ts;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                foreach (string f in Directory.GetFiles(RealWorkspaceDir, "watchdog_hb_*.json"))
                {
                    try
                    {
                        string txt = File.ReadAllText(f, Utf8NoBom);
                        Dictionary<string, object> d = ser.DeserializeObject(txt) as Dictionary<string, object>;
                        if (d == null) continue;
                        long uid = GetLong(d, "user_id");
                        string uname = GetStr(d, "username");
                        long lastSeen = GetLong(d, "last_seen");
                        AccountSlot acc = FindOrCreateAccount(uname, uid);
                        if (acc == null || lastSeen <= 0 || lastSeen < acc.LastHeartbeatEpoch) continue;
                        if (lastSeen > acc.LastHeartbeatEpoch)
                            acc.LastHeartbeatEpoch = lastSeen;
                        acc.PlaceId = GetLong(d, "place_id");
                        acc.UiExists = GetBool(d, "ui_exists");
                        acc.UiEnabled = GetBool(d, "ui_enabled");
                        acc.IsTeleportingFlag = GetBool(d, "teleporting");
                        acc.IsDisconnectedFlag = GetBool(d, "disconnected");
                        acc.DisconnectCode = (int)GetLong(d, "disconnect_code");
                        acc.DisconnectReason = GetStr(d, "disconnect_reason");
                    }
                    catch { }
                }
            }
            catch { }

            lock (SyncRoot)
            {
                foreach (AccountSlot acc in Accounts)
                {
                    if (acc.LastHeartbeatEpoch > 0)
                        acc.HeartbeatAgeSec = (int)Math.Max(0, nowEpoch - acc.LastHeartbeatEpoch);
                    else
                        acc.HeartbeatAgeSec = -1;
                }
            }
        }

        // One worker owns the launch queue. UI requests only enqueue work.
        public static void KillRobloxProcess(AccountSlot acc, string reason)
        {
            QueueRejoin(acc, reason, true);
        }

        public static void KillRobloxWindow(RobloxWin w, string reason)
        {
            if (w == null) return;
            if (w.AccountRef == null)
            {
                lock (SyncRoot)
                {
                    LogSessionInfo log;
                    if (!string.IsNullOrEmpty(w.SessionLogPath) && logSessionsByPath.TryGetValue(w.SessionLogPath, out log) &&
                        IsUnmappedHomeWindow(w, log, DateTime.UtcNow, true))
                    {
                        manualHomeCloseRequests.Add(w.Pid);
                        LogEvent("Nexus queued unidentified Home window for close (PID " + w.Pid + ")");
                        return;
                    }
                }
                LogEvent("Cannot rejoin an unidentified window. Wait for its Roblox account log.");
                return;
            }
            QueueRejoin(w.AccountRef, reason, true);
        }

        public static void KillRobloxAll(List<RobloxWin> wins)
        {
            foreach (RobloxWin w in new List<RobloxWin>(wins)) KillRobloxWindow(w, "Manual rejoin all");
        }

        private static void QueueRejoin(AccountSlot acc, string reason, bool manual)
        {
            if (acc == null) return;
            lock (SyncRoot)
            {
                if (!IsTrackedAccount(acc) || !acc.RejoinEnabled || acc.RejoinBusy || acc.RejoinPending) return;
                if (!manual && acc.StopForConflict) return;
                if (acc.UserId <= 0 || string.IsNullOrEmpty(acc.ExecutablePath)) return;
                acc.RejoinPending = true;
                acc.ManualRejoin = manual;
                if (manual) { acc.StopForConflict = false; acc.Failures = 0; }
                acc.LastReason = reason;
                acc.NextRejoinUtc = DateTime.UtcNow.AddSeconds(manual ? 0 : 10);
                acc.LaunchPid = 0;
            }
            LogEvent("Nexus queued @" + acc.Username + ": " + reason);
        }

        private static bool IsClientAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return !p.HasExited && p.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch { return true; } // Lack of access is not proof of a crash.
        }

        private static List<ClientProcessIdentity> FindAccountProcesses(AccountSlot account)
        {
            Dictionary<int, DateTime> known;
            long uid, tracker;
            Dictionary<long, long> trackerUsers = new Dictionary<long, long>();
            lock (SyncRoot)
            {
                if (!IsTrackedAccount(account)) throw new InvalidOperationException("Account removed from Real");
                uid = account.UserId; tracker = account.TrackerId;
                known = new Dictionary<int, DateTime>(account.KnownProcesses);
                if (account.LastObservedPid > 0 && account.LastObservedStartUtc != DateTime.MinValue)
                    known[account.LastObservedPid] = account.LastObservedStartUtc;
                foreach (LogSessionInfo log in logSessionsByPath.Values)
                    if (log.TrackerId > 0 && log.UserId > 0) trackerUsers[log.TrackerId] = log.UserId;
            }
            List<ClientProcessIdentity> clients = new List<ClientProcessIdentity>();
            // A fresh OS inventory is mandatory, independent of the UI's most recent scan.
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CreationDate, CommandLine FROM Win32_Process WHERE Name = 'RobloxPlayerBeta.exe'"))
            using (ManagementObjectCollection rows = searcher.Get())
            {
                foreach (ManagementBaseObject row in rows)
                {
                    int pid = Convert.ToInt32(row["ProcessId"]);
                    string creation = Convert.ToString(row["CreationDate"]);
                    DateTime started = string.IsNullOrEmpty(creation) ? DateTime.MinValue : ManagementDateTimeConverter.ToDateTime(creation).ToUniversalTime();
                    Match match = Regex.Match(Convert.ToString(row["CommandLine"] ?? ""), @"(?:-b\s+|browserTrackerId=|browsertrackerid:)(\d+)", RegexOptions.IgnoreCase);
                    long processTracker = 0;
                    if (match.Success) long.TryParse(match.Groups[1].Value, out processTracker);
                    DateTime expected;
                    bool knownPid = known.TryGetValue(pid, out expected) && started != DateTime.MinValue && Math.Abs((started - expected).TotalSeconds) < 1;
                    long logUid;
                    bool sameTracker = processTracker > 0 && ((tracker > 0 && processTracker == tracker) ||
                        (trackerUsers.TryGetValue(processTracker, out logUid) && logUid == uid));
                    if (!knownPid && !sameTracker) continue;
                    if (started == DateTime.MinValue) throw new InvalidOperationException("Cannot verify old process start time; launch blocked");
                    clients.Add(new ClientProcessIdentity { Pid = pid, StartedUtc = started });
                }
            }
            return clients;
        }

        private static void TerminateAndConfirm(ClientProcessIdentity identity)
        {
            try
            {
                using (Process process = Process.GetProcessById(identity.Pid))
                {
                    if (process.HasExited) return;
                    if (!process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase) ||
                        Math.Abs((process.StartTime.ToUniversalTime() - identity.StartedUtc).TotalSeconds) >= 1)
                        throw new InvalidOperationException("Old PID identity changed; replacement launch blocked");
                    process.Kill();
                    if (!process.WaitForExit(15000)) throw new InvalidOperationException("Old Roblox has not exited; no replacement launched");
                }
            }
            catch (ArgumentException) { } // Already gone; the fresh inventory must still confirm this.
        }

        private static void CloseObservedClient(AccountSlot account)
        {
            EnsureRejoinAllowed(account);
            CloseUnmappedHomeWindows(true);
            List<int> closed = new List<int>();
            RejoinExitBarrier.CloseAndConfirm(
                delegate { return FindAccountProcesses(account); },
                delegate(ClientProcessIdentity identity)
                {
                    LogEvent("Nexus closing @" + account.Username + " old PID " + identity.Pid);
                    EnsureRejoinAllowed(account);
                    TerminateAndConfirm(identity);
                    closed.Add(identity.Pid);
                    LogEvent("Nexus exit confirmed @" + account.Username + " old PID " + identity.Pid);
                },
                delegate
                {
                    LogEvent("Nexus all old clients exited @" + account.Username + "; waiting 10s for session release");
                    Thread.Sleep(10000);
                });
            lock (SyncRoot)
            {
                foreach (int pid in closed)
                {
                    pidKilledAtUtc[pid] = DateTime.UtcNow;
                    pidAbnormalSinceUtc.Remove(pid);
                    pidUnresponsiveSinceUtc.Remove(pid);
                }
                account.KnownProcesses.Clear();
                account.CurrentPid = 0;
                account.LastObservedPid = 0;
                account.LastHeartbeatEpoch = 0;
                account.IsDisconnectedFlag = false;
                account.IsTeleportingFlag = false;
            }
        }

        private static bool IsUnmappedHomeWindow(RobloxWin window, LogSessionInfo log, DateTime now, bool force)
        {
            if (window == null || log == null || window.AccountRef != null || window.UserId > 0 || log.UserId > 0 ||
                window.Pid <= 0 || window.Hwnd == IntPtr.Zero || window.TrackerId <= 0 || log.TrackerId != window.TrackerId ||
                window.StartTime == DateTime.MinValue || window.StartTime == DateTime.MaxValue) return false;
            if (log.Stage != "LEFT_MAP" && log.Stage != "APP_BOOT") return false;
            // Positive Home/app-session evidence is mandatory. A loading or unknown window is not enough.
            if (log.StageUpdatedAtUtc == DateTime.MinValue) return false;
            return force || ((now - window.StartTime.ToUniversalTime()).TotalSeconds >= 120 &&
                (now - log.StageUpdatedAtUtc).TotalSeconds >= 15);
        }

        private static List<RobloxWin> FindUnmappedHomeWindows(bool force)
        {
            List<RobloxWin> homes = new List<RobloxWin>();
            lock (SyncRoot)
            {
                foreach (RobloxWin window in latestWindowsSnapshot)
                {
                    LogSessionInfo log;
                    if (string.IsNullOrEmpty(window.SessionLogPath) || !logSessionsByPath.TryGetValue(window.SessionLogPath, out log)) continue;
                    FileInfo file = new FileInfo(window.SessionLogPath);
                    if (!file.Exists) continue;
                    // Re-read the tail so an old Home snapshot cannot close a client that has entered a game since then.
                    ScanLogTailIncremental(log, file);
                    bool manual = manualHomeCloseRequests.Contains(window.Pid);
                    if (IsUnmappedHomeWindow(window, log, DateTime.UtcNow, force || manual)) homes.Add(window);
                }
            }
            return homes;
        }

        private static void CloseUnmappedHomeWindows(bool force)
        {
            foreach (RobloxWin window in FindUnmappedHomeWindows(force))
            {
                try
                {
                    LogEvent("Nexus closing unidentified Home window PID " + window.Pid);
                    TerminateAndConfirm(new ClientProcessIdentity { Pid = window.Pid, StartedUtc = window.StartTime.ToUniversalTime() });
                    lock (SyncRoot)
                    {
                        manualHomeCloseRequests.Remove(window.Pid);
                        pidKilledAtUtc[window.Pid] = DateTime.UtcNow;
                        pidAbnormalSinceUtc.Remove(window.Pid);
                    }
                    LogEvent("Nexus Home window exit confirmed PID " + window.Pid);
                }
                catch
                {
                    // A failed Home close blocks the following account launch too.
                    LogEvent("Nexus Home window did not exit PID " + window.Pid + "; replacement launch blocked");
                    throw;
                }
            }
        }

        public static void RecoverLocalDuplicateAccounts()
        {
            List<AccountSlot> accounts;
            lock (SyncRoot) accounts = new List<AccountSlot>(Accounts);
            foreach (AccountSlot account in accounts)
            {
                try
                {
                    bool localConflict;
                    lock (SyncRoot) localConflict = latestWindowsSnapshot.Exists(delegate(RobloxWin window)
                    { return window.AccountRef == account && window.BotStatus == "ERR 276"; });
                    if (account.RejoinEnabled && (FindAccountProcesses(account).Count > 1 || localConflict))
                        QueueRejoin(account, "Repair duplicate local account sessions", true);
                }
                catch (Exception ex) { LogEvent("Nexus duplicate inventory: " + ex.GetType().Name); }
            }
        }

        public static int RetryDelaySeconds(int failures)
        {
            return (int)Math.Min(300, 15 * Math.Pow(2, Math.Min(5, Math.Max(0, failures - 1))));
        }

        private static void ObserveAccountHealth(AccountSlot account, string status, double processAgeSeconds)
        {
            if (status != "FARMING" && status != "IN_GAME") return;
            // Only a confirmed replacement completes a launch. Do not cancel a manual request on the old healthy client.
            if (account.RejoinPending && account.LaunchPid > 0 && account.CurrentPid == account.LaunchPid)
            {
                account.RejoinPending = false;
                account.ManualRejoin = false;
                account.LaunchPid = 0;
                LogEvent("Nexus rejoin confirmed @" + account.Username + " (PID " + account.CurrentPid + ")");
            }
            if (processAgeSeconds >= 120)
            {
                account.Failures = 0;
                account.StopForConflict = false;
            }
        }

        private static bool ConfirmUnresponsiveWindow(int pid, bool responds, DateTime now)
        {
            if (responds) { pidUnresponsiveSinceUtc.Remove(pid); return false; }
            DateTime since;
            if (!pidUnresponsiveSinceUtc.TryGetValue(pid, out since))
            { pidUnresponsiveSinceUtc[pid] = now; return false; }
            return (now - since).TotalSeconds >= 30;
        }

        private static void WatchdogLoop()
        {
            DateTime nextLaunchUtc = DateTime.MinValue;
            while (running)
            {
                Thread.Sleep(1800);
                lock (SyncRoot) { if (updatePreparing) continue; watchdogCycleBusy = true; }
                try
                {
                    if (!SyncRealAccounts()) continue;
                    if (WatchdogEnabled || manualHomeCloseRequests.Count > 0) CloseUnmappedHomeWindows(false);
                    AccountSlot launch = null;
                    DateTime now = DateTime.UtcNow;
                    lock (SyncRoot)
                    {
                        foreach (RobloxWin w in latestWindowsSnapshot)
                        {
                            if (!WatchdogEnabled || !IsTrackedAccount(w.AccountRef) || !w.AccountRef.RejoinEnabled || w.BotStatus == "ERR 276") continue;
                            if (w.BotStatus != "LEFT_MAP" && w.BotStatus != "DISCONNECTED") continue;
                            DateTime since;
                            if (pidAbnormalSinceUtc.TryGetValue(w.Pid, out since) &&
                                (now - since).TotalSeconds >= (w.BotStatus == "LEFT_MAP" ? 15 : 3))
                                QueueRejoin(w.AccountRef, w.AccountRef.DisconnectReason.Length > 0 ? w.AccountRef.DisconnectReason : w.BotStatus, false);
                        }
                        foreach (AccountSlot acc in Accounts)
                        {
                            if (!IsTrackedAccount(acc) || !acc.RejoinEnabled || !acc.WasObserved || acc.StopForConflict || acc.RejoinBusy) continue;
                            if (!WatchdogEnabled && !acc.ManualRejoin) continue;
                            bool alive = IsClientAlive(acc.LastObservedPid);
                            if (!alive && !acc.RejoinPending)
                            {
                                if ((now - startedUtc).TotalSeconds < 45) continue;
                                // An unmapped but fresh session is still running; wait for its log identity.
                                if (acc.LastHeartbeatEpoch > 0 && ToUnixEpoch(now) - acc.LastHeartbeatEpoch < Math.Max(90, UiTimeoutSec)) continue;
                                if (acc.MissingSinceUtc == DateTime.MinValue) acc.MissingSinceUtc = now;
                                if ((now - acc.MissingSinceUtc).TotalSeconds >= 10)
                                    QueueRejoin(acc, "Roblox process exited", false);
                            }
                            if (!acc.RejoinPending) continue;
                            if (acc.LaunchPid > 0)
                            {
                                bool launchedAlive = IsClientAlive(acc.LaunchPid);
                                if (launchedAlive && (acc.Status == "IN_GAME" || acc.Status == "FARMING")) continue;
                                if (now < acc.LaunchDeadlineUtc) continue;
                                acc.LaunchPid = 0;
                                acc.NextRejoinUtc = now.AddSeconds(RetryDelaySeconds(++acc.Failures));
                                LogEvent("Nexus retry @" + acc.Username + " in " + RetryDelaySeconds(acc.Failures) + "s (launch did not become healthy)");
                                continue;
                            }
                            // Cancel a delayed automatic request if the original client recovered.
                            if (!acc.ManualRejoin && alive && (acc.Status == "FARMING" || acc.Status == "IN_GAME"))
                            { acc.RejoinPending = false; continue; }
                            if (launch == null && now >= acc.NextRejoinUtc && now >= nextLaunchUtc)
                            { acc.RejoinBusy = true; launch = acc; }
                        }
                    }
                    if (launch == null) continue;
                    try
                    {
                        if (!SyncRealAccounts() || !IsTrackedAccount(launch)) continue;
                        // Preflight before closing any client; unsupported vault/target leaves it running.
                        long target;
                        string server;
                        RealAccountLauncher.ReadTarget(out target, out server);
                        RealAccountLauncher.BuildJoinUrl(target, launch.TrackerId, server);
                        RealAccountLauncher.ValidateExecutable(launch.ExecutablePath);
                        RealAccountLauncher.ReadCookie(launch.UserId);
                        if (!WatchdogEnabled && !launch.ManualRejoin) continue;
                        if (!SyncRealAccounts() || !IsTrackedAccount(launch)) continue;
                        EnsureRejoinAllowed(launch);
                        CloseObservedClient(launch);
                        int pid = RealAccountLauncher.Launch(launch, delegate
                        {
                            EnsureRejoinAllowed(launch);
                            foreach (RobloxWin home in FindUnmappedHomeWindows(true))
                                if (IsClientAlive(home.Pid)) throw new InvalidOperationException("Unidentified Home window is still running; replacement launch blocked");
                            RejoinExitBarrier.AssertClear(delegate { return FindAccountProcesses(launch); });
                        });
                        lock (SyncRoot)
                        {
                            launch.LaunchPid = pid;
                            launch.LastObservedPid = pid;
                            using (Process p = Process.GetProcessById(pid)) launch.LastObservedStartUtc = p.StartTime.ToUniversalTime();
                            launch.KnownProcesses[pid] = launch.LastObservedStartUtc;
                            launch.Status = "RESTARTING";
                            launch.LastRestartSentAt = DateTime.UtcNow;
                            launch.LaunchDeadlineUtc = DateTime.UtcNow.AddSeconds(Math.Max(120, UiTimeoutSec));
                            launch.RestartCount++;
                        }
                        LogEvent("Nexus launched @" + launch.Username + " (PID " + pid + "); waiting for game / Real auto inject");
                    }
                    catch (OperationCanceledException)
                    {
                        lock (SyncRoot) { launch.RejoinPending = false; launch.ManualRejoin = false; }
                        LogEvent("Nexus rejoin cancelled for paused account");
                    }
                    catch (Exception ex)
                    {
                        if (!IsTrackedAccount(launch) || !launch.RejoinEnabled) continue;
                        lock (SyncRoot)
                        {
                            launch.NextRejoinUtc = DateTime.UtcNow.AddSeconds(RetryDelaySeconds(++launch.Failures));
                            launch.Status = "REJOIN_WAIT";
                        }
                        // Only controlled error messages are emitted; no URLs, cookies or tickets.
                        string error = ex is InvalidOperationException ? ex.Message : ex.GetType().Name;
                        LogEvent("Nexus rejoin @" + launch.Username + " failed: " + error + "; retry in " + RetryDelaySeconds(launch.Failures) + "s");
                    }
                    finally
                    {
                        lock (SyncRoot) launch.RejoinBusy = false;
                        nextLaunchUtc = DateTime.UtcNow.AddSeconds(8);
                    }
                }
                catch (Exception ex) { LogEvent("Nexus watchdog: " + ex.GetType().Name); }
                finally { lock (SyncRoot) watchdogCycleBusy = false; }
            }
        }

        private static string GetStr(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
                return Convert.ToString(v);
            return "";
        }

        private static long GetLong(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                long res;
                if (long.TryParse(Convert.ToString(v), out res)) return res;
            }
            return 0;
        }

        private static bool GetBool(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                if (v is bool) return (bool)v;
                string s = Convert.ToString(v);
                return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
            }
            return false;
        }
    }
}
