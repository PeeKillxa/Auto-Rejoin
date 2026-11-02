"""
MuMu Player (mumu-cli.exe) Controller & High-Speed Native Hypervisor Shell v3.0
Author: cook45 for clack
Features:
 - Lock-free concurrent RPC probes across multiple VMs (Zero global lock bottleneck)
 - Single-Shot Combined Shell Probe (PID, Focus, Heartbeat, Disconnect Flag, UDP Sockets, Account Info, Logcat in 1 call)
 - Rokid-Style Two-Stage Launch (ActivitySplash -> ActivityProtocolLaunch) for 100% reliable server joining
 - Safe Warm Deep-Link Intent (re-joins from Home Screen without wiping active cache)
 - Native Root SQLite Cookie Extraction & Injection across all VMs
"""

import os
import re
import json
import time
import base64
import sqlite3
import tempfile
import threading
import subprocess
from typing import Dict, Any, Optional, Tuple, List


COMMON_MUMU_CLI_PATHS = [
    r"E:\Program Files\Netease\MuMuPlayer\nx_main\mumu-cli.exe",
    r"C:\Program Files\Netease\MuMuPlayer\nx_main\mumu-cli.exe",
    r"D:\Program Files\Netease\MuMuPlayer\nx_main\mumu-cli.exe",
]

EXECUTOR_DIRS = [
    "/storage/emulated/0/Delta",
    "/storage/emulated/0/Fluxus",
    "/storage/emulated/0/Codex",
    "/storage/emulated/0/Arceus X",
    "/storage/emulated/0/Cryptic",
    "/storage/emulated/0/VegaX",
    "/storage/emulated/0/Trigon",
]

IGNORE_PATTERNS = [
    re.compile(r"DisconnectClientInitiated", re.IGNORECASE),
    re.compile(r"reason:\s*(Player:\s*)?285\b", re.IGNORECASE),
    re.compile(r"Sending disconnect with reason:\s*285\b", re.IGNORECASE),
    re.compile(r"Code\s*285\b", re.IGNORECASE),
    re.compile(r"Teleporting", re.IGNORECASE),
    re.compile(r"TeleportService", re.IGNORECASE),
    re.compile(r"MegaReplicatorLogDisconnectCleanUpLog", re.IGNORECASE),
    re.compile(r"Time to disconnect replication data", re.IGNORECASE),
    re.compile(r"Client:Disconnect", re.IGNORECASE),
    re.compile(r"Audio.*Disconnect", re.IGNORECASE),
    re.compile(r"Bluetooth.*Disconnect", re.IGNORECASE),
]

TELEPORT_PATTERNS = [
    re.compile(r"DisconnectClientInitiated", re.IGNORECASE),
    re.compile(r"Sending disconnect with reason:\s*285\b", re.IGNORECASE),
    re.compile(r"TeleportService", re.IGNORECASE),
    re.compile(r"Teleporting", re.IGNORECASE),
]

DISCONNECT_PATTERNS = [
    re.compile(r"Error Code:\s*(2|277|268|267|279|524|529|264|273|260|271|272|284|286)\b", re.IGNORECASE),
    re.compile(r"Sending disconnect with reason:\s*(2|277|268|267|279|524|529|264|273|260|271|272|284|286)\b", re.IGNORECASE),
    re.compile(r"Connection Failed.*Error Code:\s*2\b", re.IGNORECASE),
    re.compile(r"idle\s*(for)?\s*20\s*minute", re.IGNORECASE),
    re.compile(r"idle.*(kick|disconnect)", re.IGNORECASE),
    re.compile(r"kicked\s+from\s+this\s+(experience|game)", re.IGNORECASE),
    re.compile(r"You were kicked", re.IGNORECASE),
    re.compile(r"You have been kicked", re.IGNORECASE),
    re.compile(r"Connection to the game has been lost", re.IGNORECASE),
    re.compile(r"Please reconnect and try again", re.IGNORECASE),
    re.compile(r"You have been disconnected", re.IGNORECASE),
    re.compile(r"Lost connection to the game server", re.IGNORECASE),
    re.compile(r"This game has shut down", re.IGNORECASE),
    re.compile(r"The server has closed the game", re.IGNORECASE),
    re.compile(r"RobloxPrompt.*(Disconnected|Error Code|Lost connection|kicked|idle)", re.IGNORECASE),
]

# Error Code 276 = "Same account launched experience from different device"
# Handled manually by the user — watchdog must NOT rejoin on this code.
DISCONNECT_276_PATTERN = re.compile(r"Error Code:\s*276\b", re.IGNORECASE)

# Connection Error dialog patterns — Roblox shows a popup saying
# "Connection error / Unable to contact server" or
# "Connection Error / Sorry, there was a problem reaching our servers".
# These mean the Android network stack is in a broken state; a simple
# Roblox restart won't fix it — the entire VM must be rebooted.
CONNECTION_ERROR_PATTERNS = [
    re.compile(r"Unable to contact server", re.IGNORECASE),
    re.compile(r"problem reaching our servers", re.IGNORECASE),
    re.compile(r"ConnectionFailed.*Unable to contact", re.IGNORECASE),
    re.compile(r"Please check your internet connection", re.IGNORECASE),
    re.compile(r"Sorry.*there was a problem.*servers", re.IGNORECASE),
]


def build_roblox_intent_uri(place_id: str = "8304191830", vip_link: str = "", job_id: str = "") -> str:
    """Format Place ID, Private Server URL, or Job ID into a deep link URI."""
    vip_clean = (vip_link or "").strip()
    if vip_clean and ("share?code=" in vip_clean or "privateServerLinkCode" in vip_clean or "roblox.com" in vip_clean or vip_clean.startswith("roblox://")):
        return vip_clean
    place_clean = (place_id or "8304191830").strip()
    job_clean = (job_id or "").strip()
    if job_clean:
        return f"roblox://placeID={place_clean}&gameInstanceId={job_clean}"
    return f"roblox://placeID={place_clean}"


class MuMuController:
    def __init__(self, cli_path: str = ""):
        self.cli_path = cli_path if (cli_path and os.path.isfile(cli_path)) else self._locate_cli()
        self._inst_locks: Dict[int, threading.Lock] = {}
        self._locks_guard = threading.Lock()
        self._last_logcat_line: Dict[int, str] = {}

    def _get_inst_lock(self, index: int) -> threading.Lock:
        with self._locks_guard:
            if index not in self._inst_locks:
                self._inst_locks[index] = threading.Lock()
            return self._inst_locks[index]

    def _locate_cli(self) -> Optional[str]:
        for p in COMMON_MUMU_CLI_PATHS:
            if os.path.isfile(p):
                return p
        return None

    def is_available(self) -> bool:
        return self.cli_path is not None and os.path.isfile(self.cli_path)

    def _run(self, args: list, timeout: int = 10) -> str:
        """Execute mumu-cli.exe without any global lock so all VMs run in parallel."""
        if not self.is_available():
            return ""
        cmd = [self.cli_path] + args
        try:
            startupinfo = None
            if os.name == "nt":
                startupinfo = subprocess.STARTUPINFO()
                startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                startupinfo.wShowWindow = subprocess.SW_HIDE

            proc = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                timeout=timeout,
                encoding="utf-8",
                errors="replace",
                startupinfo=startupinfo
            )
            return proc.stdout.strip()
        except Exception:
            return ""

    def get_instance_info(self, index: int = 0) -> Dict[str, Any]:
        """Runs 'mumu-cli.exe info -v <index>'"""
        raw = self._run(["info", "-v", str(index)], timeout=5)
        if raw:
            try:
                return json.loads(raw)
            except Exception:
                pass
        return {}

    def rename_instance(self, index: int, name: str) -> bool:
        """Rename a MuMu player through the supported mumu-cli command."""
        clean_name = str(name or "").replace("\r", " ").replace("\n", " ").strip()
        if not clean_name or not self.is_available():
            return False

        self._run(["rename", "--vmindex", str(index), "--name", clean_name], timeout=10)
        for _ in range(3):
            if self.get_instance_info(index).get("name", "").strip() == clean_name:
                return True
            time.sleep(0.25)
        return False

    def sh(self, command: str, index: int = 0, timeout: int = 10) -> str:
        """Run a shell command inside the Android guest as native root (UID=0) via RPC."""
        return self._run(["sh", "-v", str(index), "-c", command], timeout=timeout)

    def probe_instance(self, index: int = 0, include_logcat: bool = True) -> Dict[str, Any]:
        """
        Single-Shot Combined Probe:
        Fetches PID, Focus, Heartbeat age, Disconnect flag, Teleport signal,
        Kernel RakNet UDP game socket state, UserId/Username, and Logcat in ONE single RPC call!
        """
        cmd_parts = [
            "echo __NOW__:$(date +%s)",
            "echo __PID__:$(pidof com.roblox.client)",
            "echo __UID__:$(stat -c %u /data/data/com.roblox.client 2>/dev/null)",
            "echo __FOC__:$(dumpsys activity activities 2>/dev/null | grep -E 'mResumedActivity|topResumedActivity' | head -n 2 | tr '\\n' ' ')",
            "echo __DISC__:$(head -n 1 /storage/emulated/0/Delta/Workspace/watchdog_disconnected.txt /storage/emulated/0/Fluxus/Workspace/watchdog_disconnected.txt 2>/dev/null | grep -v '^==>' | head -n 1)",
            "rm -f /storage/emulated/0/Delta/Workspace/watchdog_disconnected.txt /storage/emulated/0/Fluxus/Workspace/watchdog_disconnected.txt 2>/dev/null",
            "echo __HBV__:$(head -n 1 /storage/emulated/0/Delta/Workspace/watchdog_heartbeat.txt /storage/emulated/0/Fluxus/Workspace/watchdog_heartbeat.txt 2>/dev/null | grep -E '^[0-9]+$' | head -n 1)",
            "echo __HBM__:$(stat -c %Y /storage/emulated/0/Delta/Workspace/watchdog_heartbeat.txt /storage/emulated/0/Delta/Workspace/executor_check_*.txt /storage/emulated/0/Fluxus/Workspace/watchdog_heartbeat.txt 2>/dev/null | sort -rn | head -n 1)",
            "echo __UI_STATUS__:$(for f in /storage/emulated/0/Delta/Workspace/watchdog_ui_status.txt /storage/emulated/0/Fluxus/Workspace/watchdog_ui_status.txt; do if [ -s \"$f\" ]; then head -n 1 \"$f\"; break; fi; done)",
            "echo __SESS__:$(ls /storage/emulated/0/Delta/Workspace/bot_session_*.json 2>/dev/null | head -n 1)",
            "echo __APP_START__",
            "cat /data/data/com.roblox.client/files/appData/LocalStorage/appStorage.json 2>/dev/null",
            "echo",
            "echo __APP_END__",
            "echo __UDP_START__",
            "cat /proc/net/udp 2>/dev/null",
            "echo __UDP_END__",
        ]
        if include_logcat:
            cmd_parts.append(
                'echo __LOGCAT_START__; logcat -d -t 90 --pid="$(pidof com.roblox.client)" 2>/dev/null'
            )
        cmd_parts.append("echo __PROBE_END__")

        probe_script = "; ".join(cmd_parts)
        raw = self.sh(probe_script, index=index, timeout=7)
        result: Dict[str, Any] = {
            "probe_ok": False,
            "now": time.time(),
            "pid": None,
            "roblox_uid": None,
            "in_foreground": False,
            "focused_window": "",
            "hb_age": None,
            "is_disconnected": False,
            "disconnect_reason": "",
            "is_teleporting": False,
            "is_connection_error": False,
            "is_disconnect_276": False,
            "udp_sockets": 0,
            "game_server_ip": None,
            "user_id": None,
            "username": None,
            "anime_ui_game_id": None,
            "anime_ui_exists": None,
            "anime_ui_enabled": None,
            "anime_ui_status_age": None,
        }
        if not raw:
            return result

        logcat_lines: List[str] = []
        udp_lines: List[str] = []
        app_lines: List[str] = []
        section = "MAIN"
        now_epoch = time.time()
        hb_val_str = ""
        hb_mtime_str = ""
        probe_complete = False

        for line in raw.splitlines():
            if line.startswith("__PROBE_END__"):
                probe_complete = True
                section = "MAIN"
                continue
            if line.startswith("__APP_START__"):
                section = "APP"
                continue
            elif line.startswith("__APP_END__"):
                section = "MAIN"
                continue
            elif line.startswith("__UDP_START__"):
                section = "UDP"
                continue
            elif line.startswith("__UDP_END__"):
                section = "MAIN"
                continue
            elif line.startswith("__LOGCAT_START__"):
                section = "LOGCAT"
                continue

            if section == "APP":
                app_lines.append(line)
                continue
            elif section == "UDP":
                udp_lines.append(line)
                continue
            elif section == "LOGCAT":
                logcat_lines.append(line)
                continue

            if line.startswith("__NOW__:"):
                val = line.split(":", 1)[1].strip()
                if val.isdigit():
                    now_epoch = float(val)
                    result["now"] = now_epoch
            elif line.startswith("__PID__:"):
                val = line.split(":", 1)[1].strip()
                pids = [int(p) for p in re.findall(r"\b\d+\b", val)]
                if pids:
                    result["pid"] = pids[0]
            elif line.startswith("__UID__:"):
                val = line.split(":", 1)[1].strip()
                if val.isdigit():
                    result["roblox_uid"] = val
            elif line.startswith("__FOC__:"):
                val = line.split(":", 1)[1].strip()
                result["focused_window"] = val
                if "com.roblox.client" in val:
                    result["in_foreground"] = True
            elif line.startswith("__DISC__:"):
                flag = line.split(":", 1)[1].strip()
                if flag:
                    if any(p.search(flag) for p in IGNORE_PATTERNS):
                        result["is_teleporting"] = True
                    else:
                        result["is_disconnected"] = True
                        result["disconnect_reason"] = f"Game Kicked / Disconnected: {flag[:90]}"
            elif line.startswith("__HBV__:"):
                hb_val_str = line.split(":", 1)[1].strip()
            elif line.startswith("__HBM__:"):
                hb_mtime_str = line.split(":", 1)[1].strip()
            elif line.startswith("__UI_STATUS__:"):
                ui_fields = line.split(":", 1)[1].strip().split("|")
                if len(ui_fields) == 4 and ui_fields[0].isdigit():
                    result["anime_ui_game_id"] = ui_fields[1].strip()
                    result["anime_ui_exists"] = ui_fields[2].strip() == "1"
                    result["anime_ui_enabled"] = ui_fields[3].strip() == "1"
                    result["anime_ui_status_age"] = max(0.0, now_epoch - float(ui_fields[0]))
            elif line.startswith("__SESS__:"):
                val = line.split(":", 1)[1].strip()
                sess_m = re.search(r"bot_session_(\d+)\.json", val)
                if sess_m and not result["user_id"]:
                    result["user_id"] = sess_m.group(1)

        if app_lines:
            app_raw = "\n".join(app_lines)
            uid_m = re.search(r'"UserId"\s*:\s*"(\d+)"', app_raw)
            uname_m = re.search(r'"Username"\s*:\s*"([^"]+)"', app_raw)
            if uid_m:
                result["user_id"] = uid_m.group(1)
            if uname_m:
                result["username"] = uname_m.group(1)

        # Compute heartbeat age accurately
        ages = []
        if hb_val_str and hb_val_str.isdigit():
            ages.append(max(0.0, now_epoch - float(hb_val_str)))
        if hb_mtime_str and hb_mtime_str.isdigit():
            ages.append(max(0.0, now_epoch - float(hb_mtime_str)))
        if ages:
            result["hb_age"] = min(ages)

        # Parse /proc/net/udp for Roblox RakNet game server sockets
        r_uid = result.get("roblox_uid")
        if r_uid and udp_lines:
            game_sockets = 0
            for uline in udp_lines:
                cols = uline.strip().split()
                if len(cols) >= 8 and cols[7] == r_uid:
                    rem = cols[2]
                    if ":" in rem:
                        rem_ip_hex, rem_port_hex = rem.split(":", 1)
                        # Ignore unconnected (0000), DNS (0035), HTTPS/QUIC (01BB)
                        if rem_ip_hex != "00000000" and rem_port_hex not in ("0000", "0035", "01BB"):
                            game_sockets += 1
                            try:
                                port_num = int(rem_port_hex, 16)
                                ip_int = int(rem_ip_hex, 16)
                                ip_str = f"{ip_int & 0xFF}.{(ip_int >> 8) & 0xFF}.{(ip_int >> 16) & 0xFF}.{(ip_int >> 24) & 0xFF}:{port_num}"
                                result["game_server_ip"] = ip_str
                            except Exception:
                                pass
            result["udp_sockets"] = game_sockets

        # Parse logcat if not already disconnected
        if include_logcat and logcat_lines and not result["is_disconnected"] and not result["is_connection_error"] and not result["is_disconnect_276"]:
            last_seen_marker = self._last_logcat_line.get(index, "")
            newest_marker = None
            for line in reversed(logcat_lines):
                clean = line.strip()
                if not clean:
                    continue
                if newest_marker is None:
                    newest_marker = clean
                if clean == last_seen_marker:
                    break
                if any(p.search(clean) for p in TELEPORT_PATTERNS):
                    result["is_teleporting"] = True
                    continue
                if any(p.search(clean) for p in IGNORE_PATTERNS):
                    continue
                # Check Error Code 276 first — suppress automatic rejoin
                if DISCONNECT_276_PATTERN.search(clean):
                    result["is_disconnect_276"] = True
                    break
                # Check connection error dialog — needs full VM reboot
                if any(p.search(clean) for p in CONNECTION_ERROR_PATTERNS):
                    result["is_connection_error"] = True
                    break
                for pattern in DISCONNECT_PATTERNS:
                    if pattern.search(clean):
                        result["is_disconnected"] = True
                        result["disconnect_reason"] = clean[:120] + ("..." if len(clean) > 120 else "")
                        break
                if result["is_disconnected"]:
                    break
            if newest_marker:
                self._last_logcat_line[index] = newest_marker

        result["probe_ok"] = probe_complete
        return result

    def is_roblox_running(self, index: int = 0) -> Optional[int]:
        """Check if com.roblox.client process is alive. Returns PID if alive, None otherwise."""
        out = self.sh("pidof com.roblox.client", index=index, timeout=5)
        pids = [int(p) for p in re.findall(r"\b\d+\b", out)]
        if pids:
            return pids[0]
        raw = self._run(["control", "-v", str(index), "app", "info", "-pkg", "com.roblox.client"], timeout=5)
        if '"state": "running"' in raw:
            return 9999
        return None

    def get_focused_window(self, index: int = 0) -> str:
        """Get the current focused package/activity in Android."""
        out = self.sh("dumpsys activity activities | grep -E 'mResumedActivity|topResumedActivity'", index=index, timeout=4)
        for line in out.splitlines():
            match = re.search(r"([a-zA-Z0-9_\.]+/\.?[a-zA-Z0-9_\.]+)", line)
            if match:
                return match.group(1)
        return ""

    def is_roblox_in_foreground(self, index: int = 0) -> bool:
        focused = self.get_focused_window(index=index)
        return "com.roblox.client" in focused

    def check_disconnect_state(self, index: int = 0) -> Tuple[bool, str]:
        probe = self.probe_instance(index=index, include_logcat=True)
        return probe["is_disconnected"], probe["disconnect_reason"]

    def clear_logcat(self, index: int = 0):
        self._last_logcat_line.pop(index, None)
        self.sh("logcat -c 2>/dev/null", index=index, timeout=4)

    def kill_roblox(self, index: int = 0):
        """Force-kill Roblox cleanly via native RPC."""
        with self._get_inst_lock(index):
            self._run(["control", "-v", str(index), "app", "close", "-pkg", "com.roblox.client"], timeout=6)
            self.sh("am force-stop com.roblox.client; pkill -9 -f com.roblox.client 2>/dev/null", index=index, timeout=6)
            time.sleep(1.0)

    def clear_script_heartbeat(self, index: int = 0):
        """Remove old heartbeat and disconnect flags across all executor workspaces when starting a new session."""
        cmd = (
            "for d in /storage/emulated/0/Delta/Workspace /storage/emulated/0/Fluxus/Workspace "
            "/storage/emulated/0/Codex/Workspace /storage/emulated/0/Arceus\\ X/Workspace; do "
            "rm -f \"$d/watchdog_heartbeat.txt\" \"$d/watchdog_ui_status.txt\" \"$d/watchdog_disconnected.txt\" \"$d\"/executor_check_*.txt 2>/dev/null; "
            "done"
        )
        self.sh(cmd, index=index, timeout=4)

    def get_script_heartbeat_age(self, index: int = 0) -> Optional[float]:
        probe = self.probe_instance(index=index, include_logcat=False)
        return probe["hb_age"]

    def ensure_autoexecute_script(self, index: int = 0, script_urls: Optional[List[str]] = None):
        """Ensure autoexecute script exists in Delta / Executor directory across all VMs with non-blocking task.spawn & watchdog bridge."""
        if script_urls is None:
            script_urls = [
                "https://raw.githubusercontent.com/weziiotez-dev/xt-hub/main/crazy.lua",
                "https://cdn.jsdelivr.net/gh/PeeKillxa/AA_Macro@main/tracker.lua",
            ]

        bridge_lua = """-- [Rokid/Cook45 Native Watchdog Bridge v3.0]
task.spawn(function()
    pcall(function()
        local Players = game:GetService("Players")
        local GuiService = game:GetService("GuiService")
        local lp = Players.LocalPlayer or Players.PlayerAdded:Wait()
        local hbFile = "watchdog_heartbeat.txt"
        local rkFile = "executor_check_" .. tostring(lp.Name) .. ".txt"
        local dcFile = "watchdog_disconnected.txt"
        local isDisc = false

        if delfile and isfile and isfile(dcFile) then
            pcall(delfile, dcFile)
        end

        local function pulse()
            if isDisc or not writefile then return end
            local now = tostring(os.time())
            pcall(writefile, hbFile, now .. "\\n")
            pcall(writefile, rkFile, "working:" .. now .. "\\n")
        end
        pulse()

        lp.OnTeleport:Connect(function()
            isDisc = false
            if delfile and isfile and isfile(dcFile) then pcall(delfile, dcFile) end
            pulse()
        end)

        GuiService.ErrorMessageChanged:Connect(function(msg)
            pcall(function()
                local code = GuiService:GetErrorCode().Value
                local m = tostring(msg or ""):lower()
                if code == 285 or m:find("disconnectclientinitiated") or m:find("teleport") or m:find("joining") then
                    return
                end
                if code >= Enum.ConnectionError.DisconnectErrors.Value then
                    isDisc = true
                    if delfile and isfile and isfile(hbFile) then pcall(delfile, hbFile) end
                    if writefile then pcall(writefile, dcFile, string.format("GuiService Error [Code %d]: %s\\n", code, tostring(msg))) end
                end
            end)
        end)

        while task.wait(5) do
            if not isDisc then pulse() end
        end
    end)
end)
"""
        party_file = r"c:\Users\PC\Documents\roblox\Party_Autoexec_PC.lua"
        if index in (0, 4, 5) and os.path.exists(party_file):
            with open(party_file, "r", encoding="utf-8") as pf:
                script_content = pf.read()
        else:
            spawn_lines = [bridge_lua]
            for u in (script_urls or []):
                u_clean = u.strip()
                if u_clean:
                    spawn_lines.append(f"task.spawn(function() pcall(function() loadstring(game:HttpGet('{u_clean}'))() end) end)")
            script_content = "\n".join(spawn_lines) + "\n"
        b64 = base64.b64encode(script_content.encode("utf-8")).decode("ascii")
        dev_label = "Android Device" if index == 0 else f"Android Device-{index}"
        self.sh(
            f"mkdir -p /storage/emulated/0/Delta/Workspace 2>/dev/null; echo -n '{dev_label}' > /storage/emulated/0/Delta/Workspace/device_name.txt; "
            f"mkdir -p /storage/emulated/0/Delta/Autoexecute 2>/dev/null; "
            f"rm -f /storage/emulated/0/Delta/Autoexecute/text.txt /storage/emulated/0/Delta/Autoexec/AA_Autoexec.lua 2>/dev/null; "
            f"echo '{b64}' | base64 -d > /storage/emulated/0/Delta/Autoexecute/AA_Autoexec.lua",
            index=index,
            timeout=5
        )

    def clean_roblox_cache(self, index: int = 0) -> bool:
        """Clear Roblox cache directory ONLY when Roblox is stopped to prevent memory bloat."""
        try:
            self.sh(
                "rm -rf /data/data/com.roblox.client/cache/* /data/user/0/com.roblox.client/cache/* "
                "/data/data/com.roblox.client/code_cache/* 2>/dev/null",
                index=index,
                timeout=5
            )
            return True
        except Exception:
            return False

    def send_join_intent(self, place_id: str = "8304191830", vip_link: str = "", job_id: str = "", index: int = 0) -> bool:
        """
        Warm Deep-Link Join:
        Sends ActivityProtocolLaunch deep link to an ALREADY running Roblox process
        WITHOUT wiping cache or killing the app.
        """
        intent_uri = build_roblox_intent_uri(place_id=place_id, vip_link=vip_link, job_id=job_id)
        cmd1 = f"am start -n com.roblox.client/com.roblox.client.ActivityProtocolLaunch -d '{intent_uri}'"
        out1 = self.sh(cmd1, index=index, timeout=6)
        if "error" in out1.lower() or "unable" in out1.lower() or "does not exist" in out1.lower():
            cmd2 = f"am start -a android.intent.action.VIEW -d '{intent_uri}' com.roblox.client"
            self.sh(cmd2, index=index, timeout=6)
        return True

    def launch_roblox(
        self,
        place_id: str = "8304191830",
        vip_link: str = "",
        job_id: str = "",
        index: int = 0,
        autoexec_urls: Optional[List[str]] = None,
        two_stage: bool = True
    ) -> bool:
        """
        Rokid-Style Two-Stage Cold Launch:
        1. Clean logcat, heartbeat, and cache while Roblox is stopped.
        2. Stage 1: Launch ActivitySplash to initialize Roblox WebView/Auth & Executor injection.
        3. Stage 2: Fire ActivityProtocolLaunch with deep link to guarantee joining the server instead of sitting on Home Screen.
        """
        with self._get_inst_lock(index):
            intent_uri = build_roblox_intent_uri(place_id=place_id, vip_link=vip_link, job_id=job_id)

            self.clear_logcat(index=index)
            self.clear_script_heartbeat(index=index)
            self.clean_roblox_cache(index=index)
            self.ensure_autoexecute_script(index=index, script_urls=autoexec_urls)

            if two_stage:
                # Stage 1: Start Splash Activity so engine & session warm up cleanly
                cmd_splash = f"am start -n com.roblox.client/com.roblox.client.startup.ActivitySplash -d '{intent_uri}'"
                out_splash = self.sh(cmd_splash, index=index, timeout=8)
                if "error" in out_splash.lower() or "does not exist" in out_splash.lower():
                    self._run(["control", "-v", str(index), "app", "launch", "-pkg", "com.roblox.client"], timeout=8)

                # Wait for process to initialize before firing protocol deep link (Rokid technique)
                time.sleep(6.0)

            # Stage 2: Fire ActivityProtocolLaunch directly with deep link
            cmd_proto = f"am start -n com.roblox.client/com.roblox.client.ActivityProtocolLaunch -d '{intent_uri}'"
            out_proto = self.sh(cmd_proto, index=index, timeout=8)
            if "error" in out_proto.lower() or "unable" in out_proto.lower() or "does not exist" in out_proto.lower():
                cmd_view = f"am start -a android.intent.action.VIEW -d '{intent_uri}' com.roblox.client"
                self.sh(cmd_view, index=index, timeout=8)

            return True

    def extract_account_and_cookie(self, index: int = 0) -> Dict[str, Optional[str]]:
        """
        Extract UserId, Username, and .ROBLOSECURITY cookie from VM's Roblox installation via native root RPC.
        """
        info: Dict[str, Optional[str]] = {"user_id": None, "username": None, "cookie": None}
        probe = self.probe_instance(index=index, include_logcat=False)
        info["user_id"] = probe.get("user_id")
        info["username"] = probe.get("username")

        # Pull Cookies SQLite DB as base64 over RPC (Android toybox base64 does not take -w 0; Python b64decode ignores newlines)
        b64_db = self.sh("base64 /data/data/com.roblox.client/app_webview/Default/Cookies 2>/dev/null", index=index, timeout=8).strip()
        if b64_db:
            tmp_path = None
            try:
                raw_bytes = base64.b64decode(b64_db)
                fd, tmp_path = tempfile.mkstemp(suffix=f"_vm{index}_Cookies.db")
                os.close(fd)
                with open(tmp_path, "wb") as f:
                    f.write(raw_bytes)
                conn = sqlite3.connect(tmp_path)
                cur = conn.cursor()
                cur.execute("SELECT value FROM cookies WHERE host_key LIKE '%roblox.com%' AND name = '.ROBLOSECURITY'")
                rows = cur.fetchall()
                if not rows:
                    cur.execute("SELECT value FROM cookies WHERE host_key LIKE '%roblox.com%'")
                    rows = cur.fetchall()
                cookie_pattern = re.compile(r"(_\|WARNING:-DO-NOT-SHARE-THIS\.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items\.\|_[A-Za-z0-9\.\-_]+)")
                for (val,) in rows:
                    if isinstance(val, bytes):
                        val = val.decode("utf-8", errors="replace")
                    m = cookie_pattern.search(str(val))
                    if m:
                        info["cookie"] = m.group(1)
                        break
                    elif str(val).startswith("_|WARNING:"):
                        info["cookie"] = str(val).strip()
                        break
                cur.close()
                conn.close()
            except Exception:
                pass
            finally:
                if tmp_path and os.path.exists(tmp_path):
                    try:
                        os.remove(tmp_path)
                    except Exception:
                        pass
        return info

    def inject_cookie(self, index: int, cookie_value: str, user_id: str = "", username: str = "") -> bool:
        """
        Inject .ROBLOSECURITY cookie (and optional UserId/Username into appStorage.json) into VM's Roblox data.
        """
        self.kill_roblox(index=index)
        tmp_path = None
        try:
            # Pull existing Cookies DB if available, otherwise create standard Chromium cookies table
            b64_db = self.sh("base64 /data/data/com.roblox.client/app_webview/Default/Cookies 2>/dev/null", index=index, timeout=6).strip()
            fd, tmp_path = tempfile.mkstemp(suffix=f"_inject_vm{index}.db")
            os.close(fd)
            if b64_db:
                try:
                    with open(tmp_path, "wb") as f:
                        f.write(base64.b64decode(b64_db))
                except Exception:
                    pass

            conn = sqlite3.connect(tmp_path)
            cur = conn.cursor()
            cur.execute("""
                CREATE TABLE IF NOT EXISTS cookies (
                    creation_utc INTEGER NOT NULL,
                    host_key TEXT NOT NULL,
                    top_frame_site_key TEXT NOT NULL DEFAULT '',
                    name TEXT NOT NULL,
                    value TEXT NOT NULL,
                    encrypted_value BLOB NOT NULL DEFAULT X'',
                    path TEXT NOT NULL,
                    expires_utc INTEGER NOT NULL,
                    is_secure INTEGER NOT NULL,
                    is_httponly INTEGER NOT NULL,
                    last_access_utc INTEGER NOT NULL,
                    has_expires INTEGER NOT NULL DEFAULT 1,
                    is_persistent INTEGER NOT NULL DEFAULT 1,
                    priority INTEGER NOT NULL DEFAULT 1,
                    samesite INTEGER NOT NULL DEFAULT -1,
                    source_scheme INTEGER NOT NULL DEFAULT 2,
                    source_port INTEGER NOT NULL DEFAULT 443,
                    last_update_utc INTEGER NOT NULL DEFAULT 0
                )
            """)
            now_us = int(time.time() * 1_000_000)
            exp_us = 99999999999999999
            cur.execute("SELECT COUNT(*) FROM cookies WHERE host_key = '.roblox.com' AND name = '.ROBLOSECURITY'")
            exists = cur.fetchone()[0]
            if exists:
                cur.execute(
                    "UPDATE cookies SET value = ?, last_access_utc = ?, expires_utc = ? WHERE host_key = '.roblox.com' AND name = '.ROBLOSECURITY'",
                    (cookie_value, now_us, exp_us)
                )
            else:
                cur.execute(
                    "INSERT INTO cookies (creation_utc, host_key, name, value, path, expires_utc, is_secure, is_httponly, last_access_utc) "
                    "VALUES (?, '.roblox.com', '.ROBLOSECURITY', ?, '/', ?, 1, 1, ?)",
                    (now_us, cookie_value, exp_us, now_us)
                )
            conn.commit()
            cur.close()
            conn.close()

            with open(tmp_path, "rb") as f:
                new_b64 = base64.b64encode(f.read()).decode("ascii")

            inject_cmd = (
                "R_UID=$(stat -c %u:%g /data/data/com.roblox.client 2>/dev/null); "
                "mkdir -p /data/data/com.roblox.client/app_webview/Default 2>/dev/null; "
                "rm -f /data/data/com.roblox.client/app_webview/Default/Cookies-journal 2>/dev/null; "
                f"echo '{new_b64}' | base64 -d > /data/data/com.roblox.client/app_webview/Default/Cookies; "
                "if [ -n \"$R_UID\" ]; then chown -R $R_UID /data/data/com.roblox.client/app_webview; fi; "
                "chmod 660 /data/data/com.roblox.client/app_webview/Default/Cookies"
            )
            self.sh(inject_cmd, index=index, timeout=10)
            return True
        except Exception:
            return False
        finally:
            if tmp_path and os.path.exists(tmp_path):
                try:
                    os.remove(tmp_path)
                except Exception:
                    pass

    def get_all_instances(self) -> Dict[int, Dict[str, Any]]:
        """Query all MuMu instances via 'mumu-cli.exe info -v all'"""
        raw = self._run(["info", "-v", "all"], timeout=6)
        instances = {}
        if raw:
            try:
                data = json.loads(raw)
                if isinstance(data, dict) and "index" in data:
                    data = {str(data["index"]): data}
                for k, v in data.items():
                    if str(k).isdigit() and isinstance(v, dict):
                        idx = int(k)
                        is_started = bool(v.get("is_android_started") and v.get("is_process_started"))
                        instances[idx] = {
                            "index": idx,
                            "name": v.get("name", f"Android Device-{idx}"),
                            "is_running": is_started,
                            "player_state": v.get("player_state", "stopped" if not is_started else "running"),
                            "pid": v.get("pid", None),
                            "adb_port": v.get("adb_port", None),
                            "android_version": v.get("android_version", "15.0")
                        }
            except Exception:
                pass
        return instances

    def is_instance_running(self, index: int = 0) -> bool:
        info = self.get_instance_info(index=index)
        return bool(info.get("is_process_started") and info.get("is_android_started"))

    def is_instance_booted(self, index: int = 0) -> bool:
        info = self.get_instance_info(index=index)
        if info.get("player_state") == "start_finished" and info.get("is_android_started"):
            boot = self.sh("getprop sys.boot_completed", index=index, timeout=3)
            return "1" in boot
        return False

    def wait_for_instance_ready(self, index: int = 0, timeout: int = 70, progress_cb=None) -> bool:
        """Wait until Android VM is fully booted and accepting shell commands."""
        start_t = time.time()
        while (time.time() - start_t) < timeout:
            if self.is_instance_booted(index=index):
                return True
            if progress_cb:
                elapsed = int(time.time() - start_t)
                progress_cb(index, elapsed)
            time.sleep(2.5)
        return False

    def launch_main_app(self) -> bool:
        out = self._run(["main", "launch"], timeout=10)
        return "error" not in out.lower()

    def launch_player(self, index: int = 0) -> bool:
        out = self._run(["control", "-v", str(index), "launch"], timeout=15)
        return "error" not in out.lower()

    def shutdown_player(self, index: int = 0) -> bool:
        out = self._run(["control", "-v", str(index), "shutdown"], timeout=15)
        return "error" not in out.lower()

    def restart_player(self, index: int = 0) -> bool:
        out = self._run(["control", "-v", str(index), "restart"], timeout=15)
        return "error" not in out.lower()

    def sort_windows(self) -> bool:
        """Automatically arrange/tile all player windows across screen."""
        out = self._run(["sort"], timeout=5)
        return "error" not in out.lower()
