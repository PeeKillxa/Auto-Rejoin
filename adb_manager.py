"""
ADB Controller & Low-Level Root Shell Interface v3.0
Author: cook45 for clack
Designed for Android Emulators (LDPlayer, BlueStacks, MuMu, Nox, MEmu)
Fixes:
 - Fixed all tuple-unpacking ValueError crashes in shell() callers
 - Added per-instance device_serial support for true Multi-Instance ADB control
 - Added Single-Shot Combined Probe (probe_instance) matching MuMuController
 - Implemented real /proc/net/udp RakNet socket inspection by Roblox UID
 - Added Rokid-Style Two-Stage Launch & Warm Deep-Link Join
"""

import os
import re
import time
import base64
import shutil
import subprocess
from typing import List, Optional, Tuple, Dict, Any

from mumu_controller import (
    IGNORE_PATTERNS,
    TELEPORT_PATTERNS,
    DISCONNECT_PATTERNS,
    build_roblox_intent_uri,
)


COMMON_ADB_PATHS = [
    r"E:\Program Files\Netease\MuMuPlayer\nx_main\adb.exe",
    r"E:\Program Files\Netease\MuMuPlayer\nx_device\15.0\shell\adb.exe",
    r"C:\Program Files\Netease\MuMuPlayer\nx_main\adb.exe",
    r"C:\Program Files\Netease\MuMuPlayer\nx_device\15.0\shell\adb.exe",
    r"E:\LDPlayer\LDPlayer9\adb.exe",
    r"C:\LDPlayer\LDPlayer9\adb.exe",
    r"D:\LDPlayer\LDPlayer9\adb.exe",
    r"C:\Program Files\BlueStacks_nxt\HD-Adb.exe",
    r"C:\Program Files\Nox\bin\nox_adb.exe",
    r"C:\Program Files\Microvirt\MEmu\adb.exe",
]

COMMON_EMU_PORTS = [
    "127.0.0.1:16385",  # MuMu Player 12/15 instance 0
    "127.0.0.1:16416",  # MuMu Player 12/15 instance 1
    "127.0.0.1:16448",  # MuMu Player 12/15 instance 2
    "127.0.0.1:16480",  # MuMu Player 12/15 instance 3
    "127.0.0.1:16512",  # MuMu Player 12/15 instance 4
    "127.0.0.1:16544",  # MuMu Player 12/15 instance 5
    "127.0.0.1:5555",   # LDPlayer / BlueStacks instance 0
    "127.0.0.1:5557",   # LDPlayer instance 1
    "127.0.0.1:5559",   # LDPlayer instance 2
    "127.0.0.1:5561",   # LDPlayer instance 3
    "127.0.0.1:62001",  # Nox Player
    "127.0.0.1:21503",  # MEmu Player
]


class ADBManager:
    def __init__(self, custom_adb_path: str = "", device_serial: str = ""):
        self.adb_path = self._locate_adb(custom_adb_path)
        self.device_serial = device_serial
        self.is_root_available: Optional[bool] = None
        self._last_logcat_line: Dict[str, str] = {}

    def _locate_adb(self, custom_path: str) -> str:
        if custom_path and os.path.isfile(custom_path):
            return custom_path

        path_in_env = shutil.which("adb")
        if path_in_env:
            return path_in_env

        for path in COMMON_ADB_PATHS:
            if os.path.isfile(path):
                return path

        return "adb"

    def _run_cmd(self, args: List[str], timeout: int = 15, serial: str = "") -> Tuple[int, str, str]:
        cmd = [self.adb_path]
        target_serial = serial or self.device_serial
        if target_serial:
            cmd.extend(["-s", target_serial])
        cmd.extend(args)

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
                startupinfo=startupinfo,
            )
            return proc.returncode, proc.stdout.strip(), proc.stderr.strip()
        except subprocess.TimeoutExpired:
            return -1, "", "Command timed out"
        except Exception as e:
            return -2, "", str(e)

    def connect_all_emulators(self, force: bool = False) -> List[str]:
        """Try connecting to standard emulator ADB ports fast."""
        if not force:
            existing = self.list_devices()
            if any(d.get("state") == "device" for d in existing):
                return [d["serial"] for d in existing if d.get("state") == "device"]

        connected = []
        for port in COMMON_EMU_PORTS:
            code, out, _ = self._run_cmd(["connect", port], timeout=2, serial="")
            out_l = out.lower()
            if code == 0 and "connected" in out_l and "unable" not in out_l:
                connected.append(port)
        return connected

    def list_devices(self) -> List[Dict[str, str]]:
        """List all attached ADB devices."""
        code, out, _ = self._run_cmd(["devices", "-l"], timeout=5, serial="")
        if code != 0:
            return []
        lines = out.splitlines()
        devices = []
        for line in lines[1:]:
            line = line.strip()
            if not line or line.startswith("*"):
                continue
            parts = line.split()
            if len(parts) >= 2:
                devices.append({
                    "serial": parts[0],
                    "state": parts[1],
                    "extra": " ".join(parts[2:])
                })
        return devices

    def set_device(self, serial: str):
        self.device_serial = serial
        self.is_root_available = None

    def shell(self, command: str, use_root: bool = True, timeout: int = 15, serial: str = "") -> str:
        """Run a shell command on device, adapting to adbd root or su wrapper. Always returns str."""
        code, out, err = self._run_cmd(["shell", command], timeout=timeout, serial=serial)
        if code == 0 and "permission denied" not in err.lower():
            return out

        if use_root:
            escaped_cmd = command.replace('"', '\\"')
            code_su, out_su, _ = self._run_cmd(["shell", f'su -c "{escaped_cmd}"'], timeout=timeout, serial=serial)
            if code_su == 0:
                return out_su

        return out

    def check_root(self, serial: str = "") -> bool:
        """Check if root privileges are available, attempting adb root then su."""
        self._run_cmd(["root"], timeout=6, serial=serial)
        time.sleep(0.3)

        _, out, _ = self._run_cmd(["shell", "id"], timeout=5, serial=serial)
        if "uid=0" in out or "root" in out:
            self.is_root_available = True
            return True

        _, out_su, _ = self._run_cmd(["shell", "su -c id"], timeout=5, serial=serial)
        if "uid=0" in out_su or "root" in out_su:
            self.is_root_available = True
            return True

        self.is_root_available = False
        return False

    def probe_instance(self, serial: str = "", include_logcat: bool = True) -> Dict[str, Any]:
        """Single-Shot Combined Probe over ADB matching MuMuController.probe_instance."""
        target_serial = serial or self.device_serial
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

        raw = self.shell("; ".join(cmd_parts), use_root=True, timeout=8, serial=target_serial)
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

        ages = []
        if hb_val_str and hb_val_str.isdigit():
            ages.append(max(0.0, now_epoch - float(hb_val_str)))
        if hb_mtime_str and hb_mtime_str.isdigit():
            ages.append(max(0.0, now_epoch - float(hb_mtime_str)))
        if ages:
            result["hb_age"] = min(ages)

        r_uid = result.get("roblox_uid")
        if r_uid and udp_lines:
            game_sockets = 0
            for uline in udp_lines:
                cols = uline.strip().split()
                if len(cols) >= 8 and cols[7] == r_uid:
                    rem = cols[2]
                    if ":" in rem:
                        rem_ip_hex, rem_port_hex = rem.split(":", 1)
                        if rem_ip_hex != "00000000" and rem_port_hex not in ("0000", "0035", "01BB"):
                            game_sockets += 1
            result["udp_sockets"] = game_sockets

        if include_logcat and logcat_lines and not result["is_disconnected"]:
            last_seen_marker = self._last_logcat_line.get(target_serial, "")
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
                for pattern in DISCONNECT_PATTERNS:
                    if pattern.search(clean):
                        result["is_disconnected"] = True
                        result["disconnect_reason"] = clean[:120] + ("..." if len(clean) > 120 else "")
                        break
                if result["is_disconnected"]:
                    break
            if newest_marker:
                self._last_logcat_line[target_serial] = newest_marker

        result["probe_ok"] = probe_complete
        return result

    def is_roblox_running(self, serial: str = "") -> Optional[int]:
        out = self.shell("pidof com.roblox.client", use_root=True, timeout=5, serial=serial)
        pids = [int(p) for p in re.findall(r"\b\d+\b", out)]
        if pids:
            return pids[0]
        return None

    def get_focused_window(self, serial: str = "") -> str:
        out = self.shell("dumpsys activity activities | grep -E 'mResumedActivity|topResumedActivity'", use_root=False, timeout=5, serial=serial)
        for line in out.splitlines():
            match = re.search(r"([a-zA-Z0-9_\.]+/\.?[a-zA-Z0-9_\.]+)", line)
            if match:
                return match.group(1)
        return ""

    def is_roblox_in_foreground(self, serial: str = "") -> bool:
        return "com.roblox.client" in self.get_focused_window(serial=serial)

    def check_disconnect_state(self, serial: str = "") -> Tuple[bool, str]:
        probe = self.probe_instance(serial=serial, include_logcat=True)
        return probe["is_disconnected"], probe["disconnect_reason"]

    def check_roblox_udp_sockets(self, serial: str = "") -> bool:
        """Real UDP socket check filtered by com.roblox.client UID."""
        probe = self.probe_instance(serial=serial, include_logcat=False)
        return probe["udp_sockets"] > 0

    def clear_script_heartbeat(self, serial: str = ""):
        self.shell(
            "rm -f /storage/emulated/0/Delta/Workspace/watchdog_heartbeat.txt "
            "/storage/emulated/0/Delta/Workspace/watchdog_ui_status.txt "
            "/storage/emulated/0/Delta/Workspace/watchdog_disconnected.txt "
            "/storage/emulated/0/Fluxus/Workspace/watchdog_ui_status.txt "
            "/storage/emulated/0/Delta/Workspace/executor_check_*.txt 2>/dev/null",
            use_root=False,
            timeout=4,
            serial=serial
        )

    def get_script_heartbeat_age(self, serial: str = "") -> Optional[float]:
        probe = self.probe_instance(serial=serial, include_logcat=False)
        return probe["hb_age"]

    def clear_logcat(self, serial: str = ""):
        self._last_logcat_line.pop(serial or self.device_serial, None)
        self.shell("logcat -c 2>/dev/null", use_root=False, timeout=5, serial=serial)

    def install_apk(self, apk_path: str, serial: str = "", timeout: int = 180) -> Tuple[bool, str]:
        """Install an APK package into Android emulator via ADB."""
        clean_path = os.path.abspath(str(apk_path or "").strip().strip('"').strip("'"))
        if not os.path.isfile(clean_path):
            return False, f"File not found: {clean_path}"
        code, out, err = self._run_cmd(["install", "-r", "-d", clean_path], timeout=timeout, serial=serial)
        combined = (out + "\n" + err).strip()
        if code == 0 and "Success" in combined:
            return True, "Success"
        return False, combined or f"Exit code {code}"

    def kill_roblox(self, serial: str = ""):
        self.shell("am force-stop com.roblox.client; pkill -9 -f com.roblox.client 2>/dev/null", use_root=True, timeout=6, serial=serial)
        time.sleep(1.0)

    def clean_roblox_cache(self, serial: str = "") -> bool:
        try:
            self.shell("rm -rf /data/data/com.roblox.client/cache/* /data/user/0/com.roblox.client/cache/* 2>/dev/null", use_root=True, timeout=5, serial=serial)
            return True
        except Exception:
            return False

    def send_join_intent(self, place_id: str = "8304191830", vip_link: str = "", job_id: str = "", serial: str = "") -> bool:
        intent_uri = build_roblox_intent_uri(place_id=place_id, vip_link=vip_link, job_id=job_id)
        cmd1 = f'am start -n com.roblox.client/com.roblox.client.ActivityProtocolLaunch -d "{intent_uri}"'
        out1 = self.shell(cmd1, use_root=True, timeout=8, serial=serial)
        if "error" in out1.lower() or "unable" in out1.lower():
            cmd2 = f'am start -a android.intent.action.VIEW -d "{intent_uri}" com.roblox.client'
            self.shell(cmd2, use_root=True, timeout=8, serial=serial)
        return True

    def launch_roblox(self, place_id: str = "8304191830", vip_link: str = "", job_id: str = "", serial: str = "", two_stage: bool = True) -> bool:
        intent_uri = build_roblox_intent_uri(place_id=place_id, vip_link=vip_link, job_id=job_id)
        self.clear_logcat(serial=serial)
        self.clear_script_heartbeat(serial=serial)
        self.clean_roblox_cache(serial=serial)

        if two_stage:
            cmd_splash = f'am start -n com.roblox.client/com.roblox.client.startup.ActivitySplash -d "{intent_uri}"'
            self.shell(cmd_splash, use_root=True, timeout=8, serial=serial)
            time.sleep(6.0)

        cmd_proto = f'am start -n com.roblox.client/com.roblox.client.ActivityProtocolLaunch -d "{intent_uri}"'
        out_proto = self.shell(cmd_proto, use_root=True, timeout=8, serial=serial)
        if "error" in out_proto.lower() or "unable" in out_proto.lower():
            cmd_view = f'am start -a android.intent.action.VIEW -d "{intent_uri}" com.roblox.client'
            self.shell(cmd_view, use_root=True, timeout=8, serial=serial)
        return True

    def restart_wifi(self, serial: str = ""):
        self.shell("svc wifi disable; sleep 1.5; svc wifi enable", use_root=True, timeout=8, serial=serial)
        time.sleep(2.0)
