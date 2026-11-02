#!/usr/bin/env python3
"""
Roblox Emulator Watchdog & Rokid-Style Multi-Instance Auto-Rejoin Engine v3.0
Author: cook45 for clack
Supports:
 - MuMu Player 12/15 (Lock-Free Concurrent Native Hypervisor RPC via mumu-cli.exe)
 - LDPlayer 9 / BlueStacks / Nox / MEmu (Multi-Device ADB + ldconsole.exe)
 - Single-Shot Combined Guest Probe (PID, Focus, Heartbeat, Disconnect Flag, Kernel RakNet UDP, Account Info, Logcat)
 - Batched Roblox Presence API Verification (In-Game vs Website/Home Screen vs Offline)
 - Rokid-Style Two-Stage Launch (ActivitySplash -> ActivityProtocolLaunch) & Safe Warm Deep-Link Re-Intent
 - Teleport-Aware State Machine (Ignores Code 285 DisconnectClientInitiated & never wipes cache while running)
 - Account Auto-Discovery, SQLite Cookie Extraction/Injection, & Autoexec Manager across all VMs
 - Flicker-Free ANSI Real-Time Dashboard + Discord Webhook Notifications
"""

import os
import sys
import time
import json
import shutil
import argparse
import threading
import collections
import urllib.request
from datetime import datetime, timezone
from concurrent.futures import ThreadPoolExecutor
from typing import Dict, Any, List, Optional, Tuple

# Force UTF-8 & Enable ANSI Escape Sequences on Windows Console
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
        import ctypes
        kernel32 = ctypes.windll.kernel32
        kernel32.SetConsoleMode(kernel32.GetStdHandle(-11), 7)
    except Exception:
        pass

try:
    import msvcrt
except ImportError:
    msvcrt = None

from adb_manager import ADBManager
from ldconsole import LDConsoleController
from mumu_controller import MuMuController, build_roblox_intent_uri
from autorun_manager import AutorunManager
from rokid_services import RobloxPresenceTracker, CookieToolkit

# ANSI Colors for Terminal UI
RESET = "\033[0m"
BOLD = "\033[1m"
GREEN = "\033[92m"
RED = "\033[91m"
YELLOW = "\033[93m"
CYAN = "\033[96m"
MAGENTA = "\033[95m"
WHITE = "\033[97m"
GRAY = "\033[90m"
PURPLE = "\033[38;5;141m"
PINK = "\033[38;5;207m"
LIGHT_BLUE = "\033[38;5;117m"
CURSOR_HOME = "\033[H"
CLEAR_EOS = "\033[J"


def set_terminal_title(title: str):
    if os.name == "nt":
        os.system(f"title {title}")
    else:
        sys.stdout.write(f"\033]0;{title}\007")
        sys.stdout.flush()


class InstanceState:
    def __init__(self, index: int, name: str = "", port: str = ""):
        self.index = index
        self.name = name or f"VM #{index}"
        self.port = port or "N/A"
        self.status = "INIT"  # Also includes "VERIFYING" while signals are inconclusive.
        self.pid: Optional[int] = None
        self.user_id: Optional[str] = None
        self.username: str = "Detecting..."
        self.presence_type: Optional[int] = None  # 0=Offline, 1=Website/Home, 2=In-Game
        self.presence_label: str = "..."
        self.presence_updated_at: float = 0.0
        self.udp_sockets: int = 0
        self.game_server_ip: Optional[str] = None
        self.reconnect_count = 0
        self.last_reason = "None"
        self.out_of_focus_count = 0
        self.roblox_started_at: float = time.time()
        self.last_intent_sent_at: float = 0.0
        self.last_teleport_at: float = 0.0
        self.last_heartbeat_age: Optional[float] = None
        self.last_heartbeat_at: Optional[float] = None
        self.consecutive_script_failures: int = 0
        self.consecutive_pid_misses: int = 0
        self.consecutive_stale_game_checks: int = 0
        self.probe_failure_count: int = 0
        self.disconnect_candidate_since: Optional[float] = None
        self.disconnect_candidate_reason: str = ""
        self.disconnect_candidate_checks: int = 0
        self.connection_error_since: Optional[float] = None
        self.connection_error_checks: int = 0
        self.is_wait_276: bool = False
        self.name_sync_target: str = ""
        self.last_name_sync_attempt_at: float = 0.0
        self.has_ever_heartbeated: bool = False
        self.anime_ui_watch_started_at: Optional[float] = None
        self.anime_ui_validation_passed: bool = False
        self.anime_ui_last_log_at: float = 0.0
        self.last_force_rejoin_at: float = time.time()
        self.lock = threading.RLock()


class EmuWatchdog:
    def __init__(self, config_path: str = "config.json"):
        self.config_path = config_path
        self.config = self._load_config()
        self.adb: Optional[ADBManager] = None
        self.ldconsole: Optional[LDConsoleController] = None
        self.mumu: Optional[MuMuController] = None
        self.autorun_mgr = AutorunManager()
        self.presence_tracker = RobloxPresenceTracker()
        self.start_time = time.time()
        self.event_logs = collections.deque(maxlen=8)
        self.instances: Dict[int, InstanceState] = {}
        self.stop_event = threading.Event()
        self.threads: List[threading.Thread] = []
        self._dashboard_first_draw = True
        self._init_controllers()

    def _init_controllers(self):
        emu_cfg = self.config.get("emulator", {})
        custom_adb = emu_cfg.get("custom_adb_path", "")
        serial = emu_cfg.get("device_serial", "")
        self.adb = ADBManager(custom_adb_path=custom_adb, device_serial=serial)
        ld_path = emu_cfg.get("ldplayer_console_path", "")
        self.ldconsole = LDConsoleController(console_path=ld_path)
        mumu_path = emu_cfg.get("mumu_cli_path", "")
        self.mumu = MuMuController(cli_path=mumu_path)

    def _load_config(self) -> dict:
        default_cfg = {
            "target": {
                "place_id": "8304191830",
                "private_server_link": "",
                "job_id": "",
                "per_instance_links": {},
                "autoexec_urls": [
                    "https://raw.githubusercontent.com/weziiotez-dev/xt-hub/main/crazy.lua",
                    "https://cdn.jsdelivr.net/gh/PeeKillxa/AA_Macro@main/tracker.lua"
                ]
            },
            "watchdog": {
                "check_interval_seconds": 5,
                "reconnect_cooldown_seconds": 4,
                "max_load_wait_seconds": 65,
                "auto_reset_wifi_on_fail": False,
                "enable_logcat_detection": True,
                "enable_focus_detection": True,
                "enable_socket_detection": True,
                "enable_script_watchdog": True,
                "enable_presence_api": True,
                "loading_grace_seconds": 90,
                "home_screen_max_wait_seconds": 160,
                "script_timeout_seconds": 210,
                "teleport_grace_seconds": 95,
                "heartbeat_timeout_seconds": 300,
                "enable_anime_ui_check": True,
                "anime_ui_universe_id": "3183403065",
                "anime_ui_start_delay_seconds": 15,
                "anime_ui_timeout_seconds": 60,
                "anime_ui_status_fresh_seconds": 15,
                "pid_missing_confirmations": 3,
                "stale_game_confirmations": 3,
                "disconnect_confirm_seconds": 15,
                "disconnect_confirmations": 3,
                "presence_fresh_seconds": 45,
                "force_rejoin_interval_minutes": 0,
                "auto_restart_vm_on_stuck": True,
                "vm_boot_timeout_seconds": 120
            },
            "multi_instance": {
                "enabled": True,
                "target_instances": [0, 1, 2, 3, 4, 5],
                "auto_launch_on_start": True,
                "auto_arrange_windows": True,
                "stagger_delay_seconds": 6
            },
            "autorun": {
                "enabled": True,
                "countdown_seconds": 5
            },
            "emulator": {
                "type": "MuMu Player",
                "custom_adb_path": r"E:\Program Files\Netease\MuMuPlayer\nx_main\adb.exe",
                "device_serial": "127.0.0.1:16385",
                "mumu_cli_path": r"E:\Program Files\Netease\MuMuPlayer\nx_main\mumu-cli.exe",
                "mumu_instance_index": 0,
                "ldplayer_console_path": r"E:\LDPlayer\LDPlayer9\ldconsole.exe",
                "ldplayer_instance_index": 0,
                "auto_launch_emulator": True
            },
            "notification": {
                "discord_webhook_url": "",
                "notify_on_disconnect": True,
                "notify_on_rejoin": True,
                "periodic_report_minutes": 0
            }
        }
        if os.path.isfile(self.config_path):
            try:
                with open(self.config_path, "r", encoding="utf-8") as f:
                    user_cfg = json.load(f)
                    for k, v in user_cfg.items():
                        if isinstance(v, dict) and k in default_cfg:
                            default_cfg[k].update(v)
                        else:
                            default_cfg[k] = v
            except Exception:
                pass
        else:
            self._save_config(default_cfg)
        return default_cfg

    def _save_config(self, cfg: Optional[dict] = None):
        if cfg is None:
            cfg = self.config
        try:
            with open(self.config_path, "w", encoding="utf-8") as f:
                json.dump(cfg, f, indent=2, ensure_ascii=False)
        except Exception as e:
            self.log(f"[!] Failed to save config: {e}", RED)

    def log(self, msg: str, color: str = WHITE):
        ts = datetime.now().strftime("%H:%M:%S")
        print(f"{GRAY}[{ts}]{RESET} {color}{msg}{RESET}")

    def add_event_log(self, text: str):
        ts = datetime.now().strftime("%H:%M:%S")
        self.event_logs.append(f"[{ts}] {text}")

    def send_discord_webhook_async(self, title: str, description: str, color_hex: int = 0x00FF88):
        """Non-blocking Discord webhook sender so worker threads never stall on HTTP timeouts."""
        webhook_url = self.config.get("notification", {}).get("discord_webhook_url", "").strip()
        if not webhook_url:
            return

        def _worker():
            payload = {
                "username": "Rokid x Cook45 Multi-Emu Watchdog v3.0",
                "avatar_url": "https://images.rbxcdn.com/2b354316d8a436ccf2d93e507b9a2128.png",
                "embeds": [{
                    "title": title,
                    "description": description,
                    "color": color_hex,
                    "footer": {"text": "Rokid-Engine Multi-Watchdog v3.0 • cook45 for clack"},
                    "timestamp": datetime.now(timezone.utc).isoformat()
                }]
            }
            try:
                req = urllib.request.Request(
                    webhook_url,
                    data=json.dumps(payload).encode("utf-8"),
                    headers={"Content-Type": "application/json", "User-Agent": "EmuWatchdog/3.0"}
                )
                urllib.request.urlopen(req, timeout=6).close()
            except Exception:
                pass

        threading.Thread(target=_worker, daemon=True).start()

    def is_mumu_mode(self) -> bool:
        return bool(self.mumu and self.mumu.is_available() and (
            self.config.get("emulator", {}).get("type") == "MuMu Player" or
            "1638" in str(self.config.get("emulator", {}).get("device_serial", ""))
        ))

    def is_ldplayer_mode(self) -> bool:
        return bool(
            not self.is_mumu_mode() and
            self.ldconsole and self.ldconsole.is_available() and
            self.config.get("emulator", {}).get("type") == "LDPlayer 9"
        )

    def get_target_for_instance(self, index: int) -> Tuple[str, str, str]:
        """Return (place_id, vip_link, job_id) for a specific VM, supporting per-VM overrides."""
        target_cfg = self.config.get("target", {})
        place_id = str(target_cfg.get("place_id", "8304191830")).strip()
        vip_link = str(target_cfg.get("private_server_link", "")).strip()
        job_id = str(target_cfg.get("job_id", "")).strip()

        per_inst = target_cfg.get("per_instance_links", {})
        override = str(per_inst.get(str(index), "")).strip()
        if override:
            if override.isdigit():
                return override, "", ""
            else:
                return place_id, override, ""
        return place_id, vip_link, job_id

    def sync_emulator_name_with_roblox_account(self, inst: InstanceState):
        """Use the logged-in Roblox username as the persistent MuMu instance name."""
        if not self.is_mumu_mode():
            return

        now = time.time()
        with inst.lock:
            desired_name = (inst.username or "").strip()
            current_name = (inst.name or "").strip()
            if (
                not desired_name
                or desired_name in ("Detecting...", "Unknown")
                or desired_name.startswith("roblox_user_")
                or current_name.casefold() == desired_name.casefold()
            ):
                return
            if (
                inst.name_sync_target == desired_name
                and now - inst.last_name_sync_attempt_at < 60.0
            ):
                return
            inst.name_sync_target = desired_name
            inst.last_name_sync_attempt_at = now

        if self.mumu.rename_instance(inst.index, desired_name):
            with inst.lock:
                inst.name = desired_name
            self.add_event_log(
                f"[VM #{inst.index}] MuMu instance renamed to Roblox account '{desired_name}'"
            )
        else:
            self.add_event_log(
                f"[VM #{inst.index}] Could not rename MuMu instance to '{desired_name}'; will retry later"
            )

    def get_adb_serial_for_instance(self, index: int, fallback_port: str = "") -> str:
        if fallback_port and ":" in fallback_port:
            return fallback_port
        if fallback_port and fallback_port.isdigit():
            return f"127.0.0.1:{fallback_port}"
        if self.is_ldplayer_mode():
            return f"127.0.0.1:{5555 + index * 2}"
        return self.config.get("emulator", {}).get("device_serial", "127.0.0.1:5555")

    def print_ascii_header(self):
        os.system("cls" if os.name == "nt" else "clear")
        banner = r"""
   ______  __  __   _    _      __          __     _______ _____ _    _ _____   ____   _____ 
  |  ____|/ / / /  | |  | |   /\\ \        / /\   |__   __/ ____| |  | |  __ \ / __ \ / ____|
  | |__  / / / /   | |  | |  /  \\ \  /\  / /  \     | | | |    | |__| | |  | | |  | | |  __ 
  |  __|/ / / /    | |/\| | / /\ \\ \/  \/ / /\ \    | | | |    |  __  | |  | | |  | | | |_ |
  | |__/ / / /     \  /\  // ____ \\  /\  // ____ \   | | | |____| |  | | |__| | |__| | |__| |
  |_____/_/_/_/     \/  \//_/    \_\\/  \/ /_/    \_ \ |_|  \_____|_|  |_|_____/ \____/ \_____|
        """
        print(f"{PURPLE}{banner}{RESET}")
        print(f"{PINK}{BOLD}     [ ROKID-ENGINE x MULTI-EMULATOR WATCHDOG v3.0 (ZERO-BUG EDITION) - cook45 for clack ]{RESET}\n")

    def print_menu_box(self):
        target_place = self.config.get("target", {}).get("place_id", "8304191830")
        target_vip = self.config.get("target", {}).get("private_server_link", "")
        per_links = self.config.get("target", {}).get("per_instance_links", {})
        if per_links:
            target_display = f"Per-VM Links ({len(per_links)} custom)"
        elif target_vip:
            target_display = "VIP Private Server"
        else:
            target_display = f"Place: {target_place}"

        target_insts = self.config.get("multi_instance", {}).get("target_instances", [0])
        inst_count = len(target_insts)
        auto_start = "ENABLED" if self.autorun_mgr.is_enabled() else "DISABLED"

        box = f"""{LIGHT_BLUE}╔═══════════════╦════════════════════════════════════════════════════════════════════════╗
║ {BOLD}Engine Core{RESET}{LIGHT_BLUE}   ║ {WHITE}> Rokid-Rejoin Core + Native MuMu Hypervisor RPC + Kernel UDP Probe{RESET}{LIGHT_BLUE}    ║
║ {BOLD}SYSTEM STATUS{RESET}{LIGHT_BLUE} ║ {WHITE}> VMs: {CYAN}{inst_count} Active {target_insts}{WHITE} | Boot-Autorun: {GREEN if auto_start=='ENABLED' else RED}{auto_start}{WHITE} | Target: {MAGENTA}{target_display}{LIGHT_BLUE} ║
║ {BOLD}MAIN MENU{RESET}{LIGHT_BLUE}     ║ {YELLOW}> 1-Start Watchdog      2-Multi-Instance Mgr    3-Target & VIP Setup   {RESET}{LIGHT_BLUE}║
║               ║ {YELLOW}> 4-Rokid Cookie/Accs   5-Autoexec Script Mgr   6-Windows Auto-Start   {RESET}{LIGHT_BLUE}║
║               ║ {YELLOW}> 7-Tile All Windows    8-Discord Webhook       9-Rejoin Test / Kill   {RESET}{LIGHT_BLUE}║
║               ║ {YELLOW}> 0-Exit Program{RESET}{LIGHT_BLUE}                                                       ║
╚═══════════════╩════════════════════════════════════════════════════════════════════════╝{RESET}"""
        print(box)

    def ensure_instance_ready(self, idx: int, name: str) -> bool:
        """Check if an instance is running; if not, launch it and wait until booted."""
        if self.is_mumu_mode():
            if not self.mumu.is_instance_running(idx):
                self.log(f"🔌 VM #{idx} ({name}) ยังไม่ได้เปิด -> กำลังบูต MuMu Player อัตโนมัติ...", YELLOW)
                self.mumu.launch_player(idx)

                def on_progress(i, sec):
                    sys.stdout.write(f"\r  ⏳ กำลังบูต Android Engine (VM #{i})... {sec}s ")
                    sys.stdout.flush()

                vm_boot_timeout = self.config.get("watchdog", {}).get("vm_boot_timeout_seconds", 120)
                booted = self.mumu.wait_for_instance_ready(index=idx, timeout=vm_boot_timeout, progress_cb=on_progress)
                print()
                if booted:
                    self.log(f"[+] VM #{idx} ({name}) บูตเสร็จสมบูรณ์ พร้อมทำงาน!", GREEN)
                    stagger = self.config.get("multi_instance", {}).get("stagger_delay_seconds", 6)
                    time.sleep(stagger)
                    return True
                else:
                    self.log(f"⚠️ VM #{idx} บูตเกินเวลาที่กำหนด ({vm_boot_timeout}s)", RED)
                    return False
            return True
        elif self.is_ldplayer_mode():
            if not self.ldconsole.is_running(idx):
                self.log(f"🔌 LDPlayer VM #{idx} ยังไม่ได้เปิด -> กำลังสั่ง Launch...", YELLOW)
                self.ldconsole.launch(idx)
                time.sleep(15)
            return True
        return True

    def trigger_instance_rejoin(self, inst: InstanceState, reason: str):
        with inst.lock:
            inst.status = "REJOINING"
            inst.reconnect_count += 1
            inst.last_reason = reason
            inst.consecutive_pid_misses = 0
            inst.consecutive_stale_game_checks = 0
            inst.disconnect_candidate_since = None
            inst.disconnect_candidate_reason = ""
            inst.disconnect_candidate_checks = 0
            inst.connection_error_since = None
            inst.connection_error_checks = 0
            inst.is_wait_276 = False
            inst.last_heartbeat_at = None
            inst.anime_ui_watch_started_at = None
            inst.anime_ui_validation_passed = False
            inst.anime_ui_last_log_at = 0.0
            self.add_event_log(f"[VM #{inst.index} | {inst.username[:12]}] 🚨 Rejoin: {reason[:38]}")

            if self.config.get("notification", {}).get("notify_on_disconnect", True):
                self.send_discord_webhook_async(
                    title=f"⚠️ Roblox Rejoining (VM #{inst.index} - {inst.username})",
                    description=(
                        f"**Instance:** `{inst.name}` (VM #{inst.index})\n"
                        f"**Account:** `{inst.username}` (`{inst.user_id or 'N/A'}`)\n"
                        f"**Reason:** `{reason}`\n"
                        f"**Rejoin Count:** `#{inst.reconnect_count}`"
                    ),
                    color_hex=0xFF3333
                )

            serial = self.get_adb_serial_for_instance(inst.index, inst.port)

            # 1. Force-kill Roblox cleanly
            if self.is_mumu_mode():
                self.mumu.kill_roblox(index=inst.index)
            else:
                self.adb.kill_roblox(serial=serial)
                if self.config.get("watchdog", {}).get("auto_reset_wifi_on_fail", False):
                    self.adb.restart_wifi(serial=serial)

            # 2. Cooldown before relaunch
            cooldown = self.config.get("watchdog", {}).get("reconnect_cooldown_seconds", 4)
            time.sleep(cooldown)

            # 3. Rokid Two-Stage Launch (ActivitySplash -> ActivityProtocolLaunch)
            place_id, vip_link, job_id = self.get_target_for_instance(inst.index)
            autoexec_urls = self.config.get("target", {}).get("autoexec_urls", None)

            if self.is_mumu_mode():
                self.mumu.launch_roblox(
                    place_id=place_id,
                    vip_link=vip_link,
                    job_id=job_id,
                    index=inst.index,
                    autoexec_urls=autoexec_urls,
                    two_stage=True
                )
            else:
                self.adb.launch_roblox(
                    place_id=place_id,
                    vip_link=vip_link,
                    job_id=job_id,
                    serial=serial,
                    two_stage=True
                )

            # 4. Wait for Roblox process & foreground
            max_wait = self.config.get("watchdog", {}).get("max_load_wait_seconds", 65)
            start_wait = time.time()
            in_game = False

            while (time.time() - start_wait) < max_wait and not self.stop_event.is_set():
                time.sleep(2.5)
                probe = (
                    self.mumu.probe_instance(inst.index, include_logcat=False)
                    if self.is_mumu_mode()
                    else self.adb.probe_instance(serial=serial, include_logcat=False)
                )
                if probe.get("probe_ok") and probe["pid"] and probe["in_foreground"]:
                    in_game = True
                    inst.pid = probe["pid"]
                    break

            now = time.time()
            inst.roblox_started_at = now
            inst.last_intent_sent_at = now
            inst.last_heartbeat_age = None
            inst.has_ever_heartbeated = False
            inst.out_of_focus_count = 0
            inst.anime_ui_watch_started_at = None
            inst.anime_ui_validation_passed = False
            inst.anime_ui_last_log_at = 0.0

            if in_game:
                inst.status = "LOADING..."
                if self.is_mumu_mode():
                    self.mumu.clear_logcat(inst.index)
                else:
                    self.adb.clear_logcat(serial=serial)
                self.add_event_log(f"[VM #{inst.index} | {inst.username[:12]}] ✅ Rejoin #{inst.reconnect_count} Launched (PID: {inst.pid})")
                if self.config.get("notification", {}).get("notify_on_rejoin", True):
                    self.send_discord_webhook_async(
                        title=f"✅ Rejoined Successfully (VM #{inst.index} - {inst.username})",
                        description=(
                            f"**Instance:** `{inst.name}` (VM #{inst.index})\n"
                            f"**Account:** `{inst.username}` (`{inst.user_id or 'N/A'}`)\n"
                            f"**Roblox PID:** `{inst.pid}`\n"
                            f"**Rejoin Count:** `#{inst.reconnect_count}`"
                        ),
                        color_hex=0x00FF88
                    )
            else:
                inst.status = "TIMEOUT"
                self.add_event_log(f"[VM #{inst.index}] ⚠️ Launch timeout ({max_wait}s). Retrying next cycle.")

    def _presence_poller_worker(self):
        """
        Background thread that batch-queries Roblox Presence API every 15s for all active VMs
        and resolves real Roblox Usernames from UserIds.
        """
        while not self.stop_event.is_set():
            try:
                if self.config.get("watchdog", {}).get("enable_presence_api", True):
                    uid_map: Dict[str, List[InstanceState]] = {}
                    for inst in list(self.instances.values()):
                        with inst.lock:
                            uid = inst.user_id
                            uname = inst.username
                        if uid:
                            uid_map.setdefault(uid, []).append(inst)
                            if uname in ("Detecting...", "Unknown") or uname.startswith("roblox_user_"):
                                resolved = self.presence_tracker.resolve_username(uid, uname)
                                with inst.lock:
                                    inst.username = resolved

                    if uid_map:
                        presences = self.presence_tracker.query_batch_presence(list(uid_map.keys()))
                        for uid, pinfo in presences.items():
                            for inst in uid_map.get(uid, []):
                                with inst.lock:
                                    inst.presence_type = pinfo.get("type")
                                    inst.presence_label = pinfo.get("label", "...")
                                    inst.presence_updated_at = time.time()
            except Exception:
                pass

            for _ in range(30):
                if self.stop_event.is_set():
                    break
                time.sleep(0.5)

    def _check_anime_ui_validation(
        self,
        inst: InstanceState,
        probe: Dict[str, Any],
        now: float,
        heartbeat_fresh: bool,
        map_active: bool,
        in_teleport_window: bool,
    ) -> str:
        """One-shot startup validation: wait 15s, then allow 60s for AA's UI to open."""
        cfg = self.config.get("watchdog", {})
        if not cfg.get("enable_anime_ui_check", True):
            return "inactive"

        expected_game_id = str(cfg.get("anime_ui_universe_id", "3183403065")).strip()
        observed_game_id = str(probe.get("anime_ui_game_id") or "").strip()
        target_place_id = str(self.config.get("target", {}).get("place_id", "8304191830")).strip()
        is_anime_adventures = (
            observed_game_id == expected_game_id
            or (not observed_game_id and target_place_id == "8304191830")
        )
        if not is_anime_adventures:
            with inst.lock:
                inst.anime_ui_watch_started_at = None
                inst.anime_ui_validation_passed = False
                inst.anime_ui_last_log_at = 0.0
            return "inactive"

        if in_teleport_window:
            with inst.lock:
                inst.anime_ui_watch_started_at = None
                inst.anime_ui_validation_passed = False
                inst.anime_ui_last_log_at = 0.0
            return "inactive"

        # Do not let a stale tracker or a missing game connection trigger this check.
        if not heartbeat_fresh:
            with inst.lock:
                inst.anime_ui_watch_started_at = None
                inst.anime_ui_validation_passed = False
                inst.anime_ui_last_log_at = 0.0
            return "inactive"

        with inst.lock:
            if inst.anime_ui_validation_passed:
                return "passed"
            if inst.anime_ui_watch_started_at is None:
                if not map_active:
                    return "inactive"
                inst.anime_ui_watch_started_at = now
                inst.anime_ui_last_log_at = now
                started_at = now
                self.add_event_log(
                    f"[VM #{inst.index}] Tracker is live in Anime Adventures; UI check starts in 15s"
                )
            else:
                started_at = inst.anime_ui_watch_started_at

        delay_seconds = max(0, int(cfg.get("anime_ui_start_delay_seconds", 15)))
        timeout_seconds = max(1, int(cfg.get("anime_ui_timeout_seconds", 60)))
        status_fresh_seconds = max(5, int(cfg.get("anime_ui_status_fresh_seconds", 15)))
        check_started_at = started_at + delay_seconds
        check_deadline = check_started_at + timeout_seconds
        if now < check_started_at:
            return "checking"

        status_age = probe.get("anime_ui_status_age")
        status_is_fresh = status_age is not None and status_age <= status_fresh_seconds
        ui_is_open = (
            status_is_fresh
            and probe.get("anime_ui_game_id") == expected_game_id
            and probe.get("anime_ui_exists") is True
            and probe.get("anime_ui_enabled") is True
        )
        if ui_is_open:
            with inst.lock:
                inst.anime_ui_validation_passed = True
            self.add_event_log(f"[VM #{inst.index}] XT_AnimeAdventuresUI found and enabled")
            return "passed"

        if now >= check_deadline:
            return "timeout"

        with inst.lock:
            if (now - inst.anime_ui_last_log_at) >= 15:
                remaining = max(0, int(check_deadline - now))
                self.add_event_log(
                    f"[VM #{inst.index}] XT_AnimeAdventuresUI not open yet; {remaining}s left"
                )
                inst.anime_ui_last_log_at = now
        return "checking"

    def _instance_monitor_worker(self, inst: InstanceState):
        interval = self.config.get("watchdog", {}).get("check_interval_seconds", 5)
        enable_focus = self.config.get("watchdog", {}).get("enable_focus_detection", True)
        enable_logcat = self.config.get("watchdog", {}).get("enable_logcat_detection", True)
        enable_socket = self.config.get("watchdog", {}).get("enable_socket_detection", True)
        enable_script_check = self.config.get("watchdog", {}).get("enable_script_watchdog", True)
        loading_grace = self.config.get("watchdog", {}).get("loading_grace_seconds", 90)
        home_max_wait = self.config.get("watchdog", {}).get("home_screen_max_wait_seconds", 160)
        heartbeat_timeout = max(
            60,
            int(self.config.get("watchdog", {}).get("heartbeat_timeout_seconds", 300))
        )
        script_timeout = max(
            home_max_wait + 30,
            self.config.get("watchdog", {}).get("script_timeout_seconds", 210),
            heartbeat_timeout
        )
        teleport_grace = self.config.get("watchdog", {}).get("teleport_grace_seconds", 95)
        vm_boot_timeout = self.config.get("watchdog", {}).get("vm_boot_timeout_seconds", 120)
        force_rejoin_mins = self.config.get("watchdog", {}).get("force_rejoin_interval_minutes", 0)
        pid_missing_confirmations = max(2, int(self.config.get("watchdog", {}).get("pid_missing_confirmations", 3)))
        stale_game_confirmations = max(2, int(self.config.get("watchdog", {}).get("stale_game_confirmations", 3)))
        disconnect_confirmations = max(2, int(self.config.get("watchdog", {}).get("disconnect_confirmations", 3)))
        disconnect_confirm_seconds = max(
            interval * 2,
            float(self.config.get("watchdog", {}).get("disconnect_confirm_seconds", 15))
        )
        presence_fresh_seconds = max(
            interval * 3,
            float(self.config.get("watchdog", {}).get("presence_fresh_seconds", 45))
        )

        while not self.stop_event.is_set():
            try:
                now = time.time()

                # 0. Optional Scheduled Force Rejoin (Rokid style)
                if force_rejoin_mins and force_rejoin_mins > 0:
                    if (now - inst.last_force_rejoin_at) >= (force_rejoin_mins * 60):
                        inst.last_force_rejoin_at = now
                        self.trigger_instance_rejoin(inst, f"Scheduled Force Rejoin ({force_rejoin_mins}m interval)")
                        continue

                # 1. VM Running Check. Confirm once more to ride out a transient MuMu CLI miss.
                if self.is_mumu_mode() and not self.mumu.is_instance_running(inst.index):
                    time.sleep(1.0)
                    if self.mumu.is_instance_running(inst.index):
                        time.sleep(interval)
                        continue
                    with inst.lock:
                        inst.status = "OFFLINE"
                        inst.pid = None
                    auto_boot = self.config.get("multi_instance", {}).get("auto_launch_on_start", True) or self.config.get("watchdog", {}).get("auto_restart_vm_on_stuck", True)
                    if auto_boot:
                        self.add_event_log(f"[VM #{inst.index}] ⚠️ VM stopped. Auto-booting MuMu VM #{inst.index}...")
                        with inst.lock:
                            inst.status = "BOOTING"
                        self.mumu.launch_player(inst.index)
                        self.mumu.wait_for_instance_ready(inst.index, timeout=vm_boot_timeout)
                        place_id, vip_link, job_id = self.get_target_for_instance(inst.index)
                        autoexec_urls = self.config.get("target", {}).get("autoexec_urls", None)
                        self.mumu.launch_roblox(
                            place_id=place_id,
                            vip_link=vip_link,
                            job_id=job_id,
                            index=inst.index,
                            autoexec_urls=autoexec_urls,
                            two_stage=True
                        )
                        with inst.lock:
                            inst.roblox_started_at = time.time()
                    time.sleep(interval)
                    continue

                # 2. Single-Shot Combined Guest Probe (1 subprocess call per cycle!)
                serial = self.get_adb_serial_for_instance(inst.index, inst.port)
                probe = (
                    self.mumu.probe_instance(inst.index, include_logcat=enable_logcat)
                    if self.is_mumu_mode()
                    else self.adb.probe_instance(serial=serial, include_logcat=enable_logcat)
                )

                # An incomplete ADB/RPC response is UNKNOWN, not proof that Roblox exited.
                if not probe.get("probe_ok"):
                    with inst.lock:
                        inst.probe_failure_count += 1
                        probe_failures = inst.probe_failure_count
                        inst.status = "VERIFYING"
                        inst.consecutive_pid_misses = 0
                        inst.consecutive_stale_game_checks = 0
                        inst.disconnect_candidate_checks = 0
                    if probe_failures == 1 or probe_failures % 6 == 0:
                        self.add_event_log(
                            f"[VM #{inst.index}] Probe incomplete ({probe_failures}x); holding rejoin"
                        )
                    time.sleep(interval)
                    continue

                with inst.lock:
                    inst.probe_failure_count = 0

                # Update account identity if discovered
                if probe.get("user_id") or probe.get("username"):
                    with inst.lock:
                        incoming_uid = str(probe.get("user_id") or "")
                        current_uid = str(inst.user_id or "")
                        uid_changed = bool(incoming_uid and incoming_uid != current_uid)
                        if incoming_uid:
                            inst.user_id = incoming_uid
                        raw_uname = probe.get("username")
                        username_unknown = (
                            inst.username in ("Detecting...", "Unknown")
                            or inst.username.startswith("roblox_user_")
                        )
                        if uid_changed or (raw_uname and (username_unknown or raw_uname != inst.username)):
                            resolved_uid = incoming_uid or current_uid
                            inst.username = (
                                self.presence_tracker.resolve_username(resolved_uid, raw_uname)
                                if resolved_uid else (raw_uname or inst.username)
                            )
                self.sync_emulator_name_with_roblox_account(inst)

                # Update UDP socket info
                with inst.lock:
                    inst.udp_sockets = probe.get("udp_sockets", 0)
                    inst.game_server_ip = probe.get("game_server_ip")

                # 4. Teleport Signal Tracking (Code 285 / TeleportService)
                if probe.get("is_teleporting"):
                    with inst.lock:
                        # Logcat buffers can repeat old teleport lines after rotation;
                        # do not restart the grace timer on every repeated probe.
                        if (now - inst.last_teleport_at) >= teleport_grace:
                            inst.last_teleport_at = now

                in_teleport_window = (now - inst.last_teleport_at) < teleport_grace
                if in_teleport_window:
                    with inst.lock:
                        inst.anime_ui_watch_started_at = None
                        inst.anime_ui_validation_passed = False
                        inst.anime_ui_last_log_at = 0.0
                hb_age = probe.get("hb_age")
                udp_active = (probe.get("udp_sockets", 0) > 0) if enable_socket else False
                with inst.lock:
                    presence_type = inst.presence_type
                    presence_updated_at = inst.presence_updated_at
                    presence_label = inst.presence_label
                presence_fresh = (
                    self.config.get("watchdog", {}).get("enable_presence_api", True)
                    and presence_updated_at > 0
                    and (now - presence_updated_at) <= presence_fresh_seconds
                )
                presence_in_game = presence_fresh and presence_type == 2
                presence_on_web = presence_fresh and presence_type == 1
                heartbeat_fresh = hb_age is not None and hb_age <= 55.0
                with inst.lock:
                    if hb_age is not None:
                        inst.has_ever_heartbeated = True
                        inst.last_heartbeat_at = now - max(0.0, float(hb_age))
                    has_ever_heartbeated = inst.has_ever_heartbeated
                    last_heartbeat_at = inst.last_heartbeat_at
                effective_heartbeat_age = (
                    max(0.0, now - last_heartbeat_at)
                    if last_heartbeat_at is not None else None
                )
                heartbeat_stale_for_session = (
                    has_ever_heartbeated
                    and (effective_heartbeat_age is None or effective_heartbeat_age > heartbeat_timeout)
                )
                heartbeat_grace_active = (
                    has_ever_heartbeated
                    and effective_heartbeat_age is not None
                    and effective_heartbeat_age <= heartbeat_timeout
                    and not heartbeat_fresh
                )
                # UDP sockets and Presence can linger after Roblox shows a failed-join
                # dialog. Once this session has heartbeated, an old heartbeat wins.
                alive_evidence = heartbeat_fresh or (
                    (udp_active or presence_in_game) and not heartbeat_stale_for_session
                )
                time_in_roblox = now - inst.roblox_started_at
                hb_summary = f"{int(hb_age)}s" if hb_age is not None else "lost"
                presence_summary = presence_label if presence_fresh else "stale/unknown"
                focus_summary = "yes" if probe.get("in_foreground") else "no"
                signal_summary = (
                    f"PID={probe.get('pid') or '-'} HB={hb_summary} "
                    f"UDP={probe.get('udp_sockets', 0)} Presence={presence_summary} Focus={focus_summary}"
                )

                with inst.lock:
                    inst.last_heartbeat_age = hb_age
                    if enable_focus and not probe.get("in_foreground"):
                        # Focus is useful diagnostic context, but overlays and Android UI
                        # must never be enough by themselves to kill an active game.
                        inst.out_of_focus_count = min(inst.out_of_focus_count + 1, 999)
                    else:
                        inst.out_of_focus_count = 0

                # Confirm explicit disconnects before considering weaker UDP/Presence
                # signals; those can remain visible after Roblox has failed to join.

                # --- Error Code 276: Same account on another device ---
                # Do NOT rejoin. User handles manually. Log once + hold.
                if enable_logcat and probe.get("is_disconnect_276"):
                    with inst.lock:
                        if not inst.is_wait_276:
                            inst.is_wait_276 = True
                            inst.disconnect_candidate_since = None
                            inst.disconnect_candidate_checks = 0
                            self.add_event_log(
                                f"[VM #{inst.index} | {inst.username[:12]}] [276] Same account on another device - waiting for manual action"
                            )
                        inst.status = "ERR 276"
                    time.sleep(interval)
                    continue
                else:
                    with inst.lock:
                        inst.is_wait_276 = False

                # --- Connection Error dialog: full VM reboot required ---
                # Roblox shows "Connection error / Unable to contact server" or
                # "Connection Error / Sorry there was a problem reaching our servers".
                # Android network stack is broken; rejoin alone won't fix it.
                if enable_logcat and probe.get("is_connection_error"):
                    with inst.lock:
                        if inst.connection_error_since is None:
                            inst.connection_error_since = now
                            inst.connection_error_checks = 0
                            self.add_event_log(
                                f"[VM #{inst.index} | {inst.username[:12]}] [CONN ERR] Connection Error dialog - confirming..."
                            )
                        inst.connection_error_checks += 1
                        inst.status = "CONN ERR"
                        conn_err_since = inst.connection_error_since
                        conn_err_checks = inst.connection_error_checks
                    if conn_err_checks >= 2 and (now - conn_err_since) >= (interval * 2):
                        self.add_event_log(
                            f"[VM #{inst.index} | {inst.username[:12]}] [CONN ERR] Confirmed -> REBOOTING VM..."
                        )
                        with inst.lock:
                            inst.status = "REBOOTING VM"
                            inst.connection_error_since = None
                            inst.connection_error_checks = 0
                        if self.config.get("notification", {}).get("notify_on_disconnect", True):
                            self.send_discord_webhook_async(
                                title=f"VM #{inst.index} Rebooting (Connection Error)",
                                description=(
                                    f"**Instance:** `{inst.name}` (VM #{inst.index})\n"
                                    f"**Account:** `{inst.username}` (`{inst.user_id or 'N/A'}`)\n"
                                    f"**Reason:** Connection Error dialog - VM reboot required"
                                ),
                                color_hex=0xFF8800
                            )
                        self.mumu.shutdown_player(inst.index)
                        time.sleep(3)
                        self.mumu.launch_player(inst.index)
                        self.mumu.wait_for_instance_ready(inst.index, timeout=vm_boot_timeout)
                        place_id, vip_link, job_id = self.get_target_for_instance(inst.index)
                        autoexec_urls = self.config.get("target", {}).get("autoexec_urls", None)
                        self.mumu.launch_roblox(
                            place_id=place_id,
                            vip_link=vip_link,
                            job_id=job_id,
                            index=inst.index,
                            autoexec_urls=autoexec_urls,
                            two_stage=True
                        )
                        with inst.lock:
                            inst.roblox_started_at = time.time()
                            inst.consecutive_script_failures = 0
                            inst.has_ever_heartbeated = False
                        continue
                    time.sleep(interval)
                    continue
                else:
                    with inst.lock:
                        inst.connection_error_since = None
                        inst.connection_error_checks = 0

                # --- Normal disconnect handling ---
                if enable_logcat and probe.get("is_disconnected"):
                    with inst.lock:
                        if inst.disconnect_candidate_since is None:
                            inst.disconnect_candidate_since = now
                            inst.disconnect_candidate_reason = (
                                probe.get("disconnect_reason") or "Disconnect signal detected"
                            )
                            inst.disconnect_candidate_checks = 0
                            self.add_event_log(
                                f"[VM #{inst.index}] Disconnect signal seen; waiting for confirmation"
                            )

                with inst.lock:
                    disconnect_since = inst.disconnect_candidate_since
                    disconnect_reason = inst.disconnect_candidate_reason
                    if disconnect_since is not None:
                        inst.disconnect_candidate_checks += 1
                    disconnect_checks = inst.disconnect_candidate_checks
                if disconnect_since is not None:
                    if (
                        (now - disconnect_since) >= disconnect_confirm_seconds
                        and disconnect_checks >= disconnect_confirmations
                    ):
                        self.trigger_instance_rejoin(
                            inst,
                            f"Confirmed disconnect: {disconnect_reason} ({signal_summary})"
                        )
                        continue
                    with inst.lock:
                        inst.status = "VERIFYING"
                    time.sleep(interval)
                    continue

                # Positive gameplay signals always beat noisy focus/logcat signals.
                if alive_evidence:
                    observed_pid = probe.get("pid")
                    with inst.lock:
                        if observed_pid and inst.pid != observed_pid:
                            inst.pid = observed_pid
                            inst.roblox_started_at = now
                            inst.anime_ui_watch_started_at = None
                            inst.anime_ui_validation_passed = False
                            inst.anime_ui_last_log_at = 0.0
                    ui_validation_state = self._check_anime_ui_validation(
                        inst=inst,
                        probe=probe,
                        now=now,
                        heartbeat_fresh=heartbeat_fresh,
                        map_active=(probe.get("udp_sockets", 0) > 0 or presence_in_game),
                        in_teleport_window=in_teleport_window,
                    )
                    if ui_validation_state == "timeout":
                        self.trigger_instance_rejoin(
                            inst,
                            "XT_AnimeAdventuresUI missing/disabled after 60s startup check",
                        )
                        continue
                    with inst.lock:
                        if heartbeat_fresh:
                            inst.has_ever_heartbeated = True
                        if ui_validation_state == "checking":
                            inst.status = "CHECKING UI"
                        else:
                            inst.status = (
                                "IN-GAME"
                                if heartbeat_fresh or not has_ever_heartbeated
                                else "VERIFYING"
                            )
                        inst.consecutive_pid_misses = 0
                        inst.consecutive_stale_game_checks = 0
                        inst.consecutive_script_failures = 0
                        inst.disconnect_candidate_since = None
                        inst.disconnect_candidate_reason = ""
                        inst.disconnect_candidate_checks = 0
                    time.sleep(interval)
                    continue

                if in_teleport_window:
                    with inst.lock:
                        inst.status = "TELEPORTING"
                        inst.consecutive_pid_misses = 0
                        inst.consecutive_stale_game_checks = 0
                        inst.disconnect_candidate_since = None
                        inst.disconnect_candidate_reason = ""
                        inst.disconnect_candidate_checks = 0
                    time.sleep(interval)
                    continue

                # 3. Roblox Process Alive Check. Only count a missing PID from complete probes.
                pid = probe.get("pid")
                if not pid:
                    with inst.lock:
                        inst.consecutive_pid_misses += 1
                        missing_count = inst.consecutive_pid_misses
                        inst.consecutive_stale_game_checks = 0
                        inst.status = "VERIFYING"
                        inst.pid = None
                    if missing_count >= pid_missing_confirmations:
                        self.trigger_instance_rejoin(
                            inst,
                            f"Roblox PID missing on {missing_count} complete probes ({signal_summary})"
                        )
                    elif missing_count == 1:
                        self.add_event_log(
                            f"[VM #{inst.index}] PID missing; confirming before rejoin"
                        )
                    time.sleep(interval)
                    continue

                with inst.lock:
                    inst.consecutive_pid_misses = 0
                    if inst.pid != pid:
                        inst.pid = pid
                        inst.roblox_started_at = now
                        inst.last_heartbeat_age = None
                        # A stale timestamp still proves this session's tracker ran before.
                        inst.has_ever_heartbeated = hb_age is not None
                        inst.anime_ui_watch_started_at = None
                        inst.anime_ui_validation_passed = False
                        inst.anime_ui_last_log_at = 0.0

                if enable_script_check:
                    # Suspect a frozen/disconnected session only after repeated complete probes.
                    if has_ever_heartbeated and (
                        effective_heartbeat_age is None
                        or effective_heartbeat_age > heartbeat_timeout
                    ):
                        age_str = (
                            f"{int(effective_heartbeat_age)}s"
                            if effective_heartbeat_age is not None else "Lost"
                        )
                        with inst.lock:
                            inst.consecutive_stale_game_checks += 1
                            stale_count = inst.consecutive_stale_game_checks
                            inst.status = "VERIFYING"
                        if stale_count >= stale_game_confirmations:
                            self.trigger_instance_rejoin(
                                inst,
                                f"Confirmed disconnected/frozen after {stale_count} probes ({signal_summary})"
                            )
                            continue
                        self.add_event_log(
                            f"[VM #{inst.index}] Stale heartbeat ({age_str}); confirming ({stale_count}/{stale_game_confirmations})"
                        )
                        time.sleep(interval)
                        continue
                    elif heartbeat_grace_active:
                        with inst.lock:
                            inst.consecutive_stale_game_checks = 0
                            inst.status = "VERIFYING"
                        time.sleep(interval)
                        continue
                    else:
                        with inst.lock:
                            inst.consecutive_stale_game_checks = 0

                    # Freshly launched Roblox waiting to load map & execute script.
                    if time_in_roblox < loading_grace and not presence_on_web:
                        with inst.lock:
                            inst.status = "LOADING..."

                    elif time_in_roblox < script_timeout:
                        with inst.lock:
                            inst.status = "JOINING MAP"
                        # Warm-join without wiping cache while Roblox is at Home/Website.
                        if (now - inst.last_intent_sent_at) >= 30.0:
                            inst.last_intent_sent_at = now
                            place_id, vip_link, job_id = self.get_target_for_instance(inst.index)
                            self.add_event_log(f"[VM #{inst.index} | {inst.username[:12]}] 🏠 Home Screen detected -> Warm Join ({place_id})")
                            if self.is_mumu_mode():
                                self.mumu.send_join_intent(place_id=place_id, vip_link=vip_link, job_id=job_id, index=inst.index)
                            else:
                                self.adb.send_join_intent(place_id=place_id, vip_link=vip_link, job_id=job_id, serial=serial)

                    else:
                        # Exceeded the full load/script timeout without any live signal.
                        inst.consecutive_script_failures += 1
                        age_str = f"{int(hb_age)}s" if hb_age is not None else "Never"

                        if (
                            inst.consecutive_script_failures >= 2
                            and self.is_mumu_mode()
                            and self.config.get("watchdog", {}).get("auto_restart_vm_on_stuck", True)
                        ):
                            self.add_event_log(f"[VM #{inst.index}] 🔄 Stuck 2x ({age_str}) -> REBOOTING VM #{inst.index}...")
                            with inst.lock:
                                inst.status = "REBOOTING VM"
                            self.mumu.shutdown_player(inst.index)
                            time.sleep(3)
                            self.mumu.launch_player(inst.index)
                            self.mumu.wait_for_instance_ready(inst.index, timeout=vm_boot_timeout)
                            place_id, vip_link, job_id = self.get_target_for_instance(inst.index)
                            autoexec_urls = self.config.get("target", {}).get("autoexec_urls", None)
                            self.mumu.launch_roblox(
                                place_id=place_id,
                                vip_link=vip_link,
                                job_id=job_id,
                                index=inst.index,
                                autoexec_urls=autoexec_urls,
                                two_stage=True
                            )
                            with inst.lock:
                                inst.roblox_started_at = time.time()
                                inst.consecutive_script_failures = 0
                                inst.has_ever_heartbeated = False
                            continue
                        else:
                            self.trigger_instance_rejoin(
                                inst,
                                f"Load/Script timeout ({int(time_in_roblox)}s > {script_timeout}s; {signal_summary})"
                            )
                            continue
                else:
                    with inst.lock:
                        inst.status = "IN-GAME"

            except Exception as e:
                self.add_event_log(f"[VM #{inst.index}] Worker error: {e}")

            time.sleep(interval)

    def print_multi_dashboard(self):
        if self._dashboard_first_draw:
            os.system("cls" if os.name == "nt" else "clear")
            self._dashboard_first_draw = False
        else:
            sys.stdout.write(CURSOR_HOME)

        uptime_secs = int(time.time() - self.start_time)
        hrs = uptime_secs // 3600
        mins = (uptime_secs % 3600) // 60
        secs = uptime_secs % 60
        uptime_str = f"{hrs:02d}h {mins:02d}m {secs:02d}s"

        target_place = self.config.get("target", {}).get("place_id", "8304191830")
        target_vip = self.config.get("target", {}).get("private_server_link", "")
        target_display = "VIP Private Server" if target_vip else f"Place ID: {target_place}"
        auto_start = "ENABLED" if self.autorun_mgr.is_enabled() else "DISABLED"

        lines = []
        lines.append(f"{CYAN}{BOLD}╔══════════════════════════════════════════════════════════════════════════════════════════════╗{RESET}")
        lines.append(f"{CYAN}{BOLD}║          🤖 ROKID-ENGINE x ROBLOX MULTI-EMULATOR WATCHDOG v3.0 (cook45 for clack)            ║{RESET}")
        lines.append(f"{CYAN}{BOLD}╠══════════════════════════════════════════════════════════════════════════════════════════════╣{RESET}")
        lines.append(f"║ {BOLD}Target Game:{RESET}   {WHITE}{target_display[:28]:<28}{RESET} │ {BOLD}Auto-Start:{RESET}  {GREEN if auto_start=='ENABLED' else RED}{auto_start:<12}{RESET} │ {BOLD}Uptime:{RESET} {WHITE}{uptime_str:<10}{RESET} ║")
        lines.append(f"{CYAN}{BOLD}╠═════╤════════════════════╤═════════════╤═════════╤═══════════╤══════════════════╤════════╤═══╣{RESET}")
        lines.append(f"║ {BOLD}VM# {RESET}│ {BOLD}Roblox Account     {RESET}│ {BOLD}State       {RESET}│ {BOLD}PID     {RESET}│ {BOLD}Heartbeat {RESET}│ {BOLD}Server / Presence{RESET}│ {BOLD}Rejoin {RESET}│{BOLD}St{RESET} ║")
        lines.append(f"{CYAN}{BOLD}╟─────┼────────────────────┼─────────────┼─────────┼───────────┼──────────────────┼────────┼───╢{RESET}")

        for idx in sorted(self.instances.keys()):
            inst = self.instances[idx]
            with inst.lock:
                acc_display = (inst.username if inst.username != "Detecting..." else inst.name)[:18]
                pid_display = str(inst.pid) if inst.pid else "-----"
                hb_age = inst.last_heartbeat_age
                hb_display = f"{int(hb_age)}s ago" if hb_age is not None else "---"
                if inst.game_server_ip:
                    net_display = f"UDP:{inst.game_server_ip.split(':')[0]}"[:16]
                elif inst.presence_label and inst.presence_label != "...":
                    net_display = f"API:{inst.presence_label}"[:16]
                else:
                    net_display = f"Port:{inst.port}"[:16]
                status = inst.status
                rejoins = inst.reconnect_count

            if status == "IN-GAME":
                state_colored = f"{GREEN}{BOLD}{'IN-GAME':<11}{RESET}"
                st_icon = f"{GREEN}●{RESET}"
            elif status == "CHECKING UI":
                state_colored = f"{CYAN}{BOLD}{'CHECKING UI':<11}{RESET}"
                st_icon = f"{CYAN}?{RESET}"
            elif status == "TELEPORTING":
                state_colored = f"{MAGENTA}{BOLD}{'TELEPORTING':<11}{RESET}"
                st_icon = f"{MAGENTA}⇄{RESET}"
            elif status == "JOINING MAP":
                state_colored = f"{CYAN}{BOLD}{'JOINING MAP':<11}{RESET}"
                st_icon = f"{CYAN}→{RESET}"
            elif status == "LOADING...":
                state_colored = f"{LIGHT_BLUE}{BOLD}{'LOADING...':<11}{RESET}"
                st_icon = f"{LIGHT_BLUE}…{RESET}"
            elif status == "VERIFYING":
                state_colored = f"{YELLOW}{BOLD}{'VERIFYING':<11}{RESET}"
                st_icon = f"{YELLOW}?{RESET}"
            elif "REBOOT" in status:
                state_colored = f"{MAGENTA}{BOLD}{'REBOOT VM':<11}{RESET}"
                st_icon = f"{MAGENTA}↻{RESET}"
            elif "REJOIN" in status:
                state_colored = f"{YELLOW}{BOLD}{'REJOINING':<11}{RESET}"
                st_icon = f"{YELLOW}↻{RESET}"
            elif status == "BOOTING":
                state_colored = f"{CYAN}{BOLD}{'BOOTING':<11}{RESET}"
                st_icon = f"{CYAN}↑{RESET}"
            else:
                state_colored = f"{RED}{BOLD}{'OFFLINE':<11}{RESET}"
                st_icon = f"{RED}×{RESET}"

            lines.append(
                f"║ {idx:<3} │ {WHITE}{acc_display:<18}{RESET} │ {state_colored} │ {pid_display:<7} │ {hb_display:<9} │ {GRAY}{net_display:<16}{RESET} │ {rejoins:<6} │ {st_icon} ║"
            )

        lines.append(f"{CYAN}{BOLD}╠═════╧════════════════════╧═════════════╧═════════╧═══════════╧══════════════════╧════════╧═══╣{RESET}")
        lines.append(f"║ {BOLD}📜 RECENT ACTIVITY LOG (บันทึกเหตุการณ์ล่าสุด):{RESET}{' '*46}║")
        lines.append(f"{CYAN}{BOLD}╟──────────────────────────────────────────────────────────────────────────────────────────────╢{RESET}")

        recent = list(self.event_logs)[-6:]
        if not recent:
            lines.append(f"║   {GRAY}(ระบบกำลังเฝ้าระวังทุกจอแบบ Real-Time...){RESET}{' '*50}║")
        for i in range(6):
            if i < len(recent):
                clean_log = recent[i][:88]
                pad = " " * max(0, 90 - len(clean_log))
                lines.append(f"║ {clean_log}{pad}║")
            elif len(recent) > 0:
                lines.append(f"║ {' '*90}║")

        lines.append(f"{CYAN}{BOLD}╚══════════════════════════════════════════════════════════════════════════════════════════════╝{RESET}")
        lines.append(f" {GRAY}กด {BOLD}Ctrl+C{RESET}{GRAY} เพื่อหยุดระบบ Watchdog และกลับสู่เมนูหลัก{RESET}{CLEAR_EOS}")

        sys.stdout.write("\n".join(lines) + "\n")
        sys.stdout.flush()

    def run_watchdog_loop(self):
        set_terminal_title("== Rokid x Cook45 Multi-Watchdog v3.0 Active == | Monitoring All VMs")
        self.stop_event.clear()
        self.threads.clear()
        self.instances.clear()
        self._dashboard_first_draw = True

        target_insts = self.config.get("multi_instance", {}).get("target_instances", [0])
        if not target_insts:
            target_insts = [0]

        auto_launch = self.config.get("multi_instance", {}).get("auto_launch_on_start", True)
        auto_tile = self.config.get("multi_instance", {}).get("auto_arrange_windows", True)
        autoexec_urls = self.config.get("target", {}).get("autoexec_urls", None)

        self.print_ascii_header()
        self.log(f"🚀 กำลังเตรียมระบบ Rokid-Engine สำหรับ {len(target_insts)} จอ (VMs: {target_insts})...", CYAN)

        all_info = self.mumu.get_all_instances() if self.is_mumu_mode() else {}

        # 1. Initialize & Boot offline VMs
        for idx in target_insts:
            inst_info = all_info.get(idx, {})
            inst_name = inst_info.get("name", f"Android Device-{idx}")
            inst_port = str(inst_info.get("adb_port", 16385 + idx * 32 if idx > 0 else 16385))

            inst_state = InstanceState(index=idx, name=inst_name, port=inst_port)
            self.instances[idx] = inst_state

            if auto_launch:
                self.ensure_instance_ready(idx, inst_name)

        # 2. Auto-tile windows if enabled
        if auto_tile and self.is_mumu_mode():
            self.log("[+] จัดเรียงหน้าต่าง MuMu ทุกจอบน Desktop...", CYAN)
            self.mumu.sort_windows()
            time.sleep(0.8)

        # 3. Parallel initial probe of all VMs to attach or launch Roblox
        self.log("🔍 กำลังสแกนสถานะ Roblox, ไอดีผู้เล่น, และพอร์ตเชื่อมต่อของทุกจอพร้อมกัน...", CYAN)
        for idx, inst in self.instances.items():
            if self.is_mumu_mode():
                self.mumu.ensure_autoexecute_script(index=idx, script_urls=autoexec_urls)

            serial = self.get_adb_serial_for_instance(idx, inst.port)
            probe = (
                self.mumu.probe_instance(idx, include_logcat=False)
                if self.is_mumu_mode()
                else self.adb.probe_instance(serial=serial, include_logcat=False)
            )
            if not probe.get("probe_ok"):
                inst.status = "VERIFYING"
                self.add_event_log(
                    f"[VM #{idx}] Initial probe incomplete; leaving Roblox untouched"
                )
                continue

            if probe.get("user_id"):
                inst.user_id = probe["user_id"]
                inst.username = self.presence_tracker.resolve_username(inst.user_id, probe.get("username"))
            elif probe.get("username"):
                inst.username = probe["username"]
            self.sync_emulator_name_with_roblox_account(inst)

            pid = probe.get("pid")
            if not pid:
                hb_age = probe.get("hb_age")
                udp_active = (
                    self.config.get("watchdog", {}).get("enable_socket_detection", True)
                    and probe.get("udp_sockets", 0) > 0
                )
                if (hb_age is not None and hb_age <= 55.0) or udp_active:
                    inst.udp_sockets = probe.get("udp_sockets", 0)
                    inst.game_server_ip = probe.get("game_server_ip")
                    inst.last_heartbeat_age = hb_age
                    inst.has_ever_heartbeated = hb_age is not None
                    if hb_age is not None:
                        inst.last_heartbeat_at = time.time() - max(0.0, float(hb_age))
                    inst.status = "VERIFYING"
                    self.add_event_log(
                        f"[VM #{idx}] PID missing but live game signal exists; leaving Roblox untouched"
                    )
                else:
                    inst.status = "VERIFYING"
                    self.add_event_log(
                        f"[VM #{idx}] PID absent at startup; confirming before auto-launch"
                    )
                continue
            else:
                inst.pid = pid
                inst.udp_sockets = probe.get("udp_sockets", 0)
                inst.game_server_ip = probe.get("game_server_ip")
                inst.last_heartbeat_age = probe.get("hb_age")
                inst.has_ever_heartbeated = inst.last_heartbeat_age is not None
                if inst.last_heartbeat_age is not None:
                    inst.last_heartbeat_at = time.time() - max(0.0, float(inst.last_heartbeat_age))
                heartbeat_timeout = max(
                    60,
                    int(self.config.get("watchdog", {}).get("heartbeat_timeout_seconds", 300))
                )
                heartbeat_is_fresh = (
                    inst.last_heartbeat_age is not None and inst.last_heartbeat_age <= 55.0
                )
                heartbeat_within_grace = (
                    inst.last_heartbeat_age is not None
                    and inst.last_heartbeat_age <= heartbeat_timeout
                )
                if heartbeat_is_fresh or (inst.udp_sockets > 0 and not inst.has_ever_heartbeated):
                    inst.status = "IN-GAME"
                elif heartbeat_within_grace:
                    inst.status = "VERIFYING"
                else:
                    inst.status = "LOADING..."
                self.add_event_log(f"[VM #{idx} | {inst.username[:12]}] Attached (PID: {pid}, UDP: {inst.udp_sockets})")

        # 4. Start Presence Poller + Per-VM Monitor Threads
        tp_thread = threading.Thread(target=self._presence_poller_worker, daemon=True)
        tp_thread.start()
        self.threads.append(tp_thread)

        for idx, inst in self.instances.items():
            t = threading.Thread(target=self._instance_monitor_worker, args=(inst,), daemon=True)
            t.start()
            self.threads.append(t)

        self.log("🎉 ระบบเฝ้าระวังเริ่มทำงานเต็มรูปแบบ! กำลังเปิดหน้าต่าง Real-Time Dashboard...", GREEN)
        time.sleep(1.2)

        try:
            while not self.stop_event.is_set():
                self.print_multi_dashboard()
                time.sleep(2.0)
        except KeyboardInterrupt:
            print(f"\n{YELLOW}[!] ได้รับคำสั่งหยุดระบบ (Ctrl+C)... กำลังบันทึก Cache และกลับสู่เมนูหลัก...{RESET}")
            self.stop_event.set()
            self.presence_tracker.save_cache()
            time.sleep(1.0)

    # =========================================================================
    # INTERACTIVE MENUS
    # =========================================================================

    def multi_instance_menu(self):
        while True:
            self.print_ascii_header()
            print(f"{CYAN}{BOLD}🖥️  MULTI-INSTANCE (MULTI-WINDOW) MANAGER (จัดการระบบหลายจอ){RESET}\n")

            instances = self.mumu.get_all_instances() if self.is_mumu_mode() else {}
            target_insts = set(self.config.get("multi_instance", {}).get("target_instances", [0]))
            display_names: Dict[int, str] = {}

            running_instances = [
                (idx, info) for idx, info in instances.items() if info.get("is_running")
            ]
            if running_instances and self.is_mumu_mode():
                def read_instance_identity(item):
                    idx, _info = item
                    return idx, self.mumu.probe_instance(idx, include_logcat=False)

                try:
                    with ThreadPoolExecutor(max_workers=min(4, len(running_instances))) as executor:
                        for idx, probe in executor.map(read_instance_identity, running_instances):
                            if not probe.get("probe_ok"):
                                continue
                            uid = probe.get("user_id")
                            username = probe.get("username")
                            if uid:
                                username = self.presence_tracker.resolve_username(uid, username)
                            if username and username not in ("Detecting...", "Unknown") and not username.startswith("roblox_user_"):
                                display_names[idx] = username
                                current_name = str(instances[idx].get("name") or "").strip()
                                if current_name.casefold() != username.casefold():
                                    if self.mumu.rename_instance(idx, username):
                                        instances[idx]["name"] = username
                                        self.log(
                                            f"[+] MuMu VM #{idx} renamed: {current_name} -> {username}",
                                            GREEN
                                        )
                except Exception:
                    pass

            print(f"{WHITE}{BOLD}รายการจอทั้งหมดใน MuMu Player:{RESET}")
            if not instances:
                print(f"  {YELLOW}(ไม่พบ instance จาก mumu-cli หรืออยู่ในโหมด ADB){RESET}")
            else:
                for idx in sorted(instances.keys()):
                    info = instances[idx]
                    is_monitored = idx in target_insts
                    check_mark = f"{GREEN}[*]{RESET}" if is_monitored else f"{GRAY}[ ]{RESET}"
                    run_status = f"{GREEN}[RUNNING - Port {info['adb_port']}]{RESET}" if info["is_running"] else f"{GRAY}[STOPPED]{RESET}"
                    display_name = display_names.get(idx, info["name"])
                    print(f"  {check_mark} [{idx}] {WHITE}{display_name[:20]:<20}{RESET} {run_status}")

            auto_launch = self.config.get("multi_instance", {}).get("auto_launch_on_start", True)
            auto_tile = self.config.get("multi_instance", {}).get("auto_arrange_windows", True)
            stagger = self.config.get("multi_instance", {}).get("stagger_delay_seconds", 6)

            print(f"\n{WHITE}{BOLD}การตั้งค่าระบบ Multi-Instance:{RESET}")
            print(f"  - Auto-Launch MuMu on Start  : {GREEN if auto_launch else RED}{'[ENABLED]' if auto_launch else '[DISABLED]'}{RESET}")
            print(f"  - Auto-Tile / Sort Windows   : {GREEN if auto_tile else RED}{'[ENABLED]' if auto_tile else '[DISABLED]'}{RESET}")
            print(f"  - Stagger Boot Delay         : {CYAN}{stagger} วินาที{RESET}")

            print(f"\n{YELLOW}{BOLD}คำสั่งการทำงาน:{RESET}")
            print("  [0-9] สลับเลือก/ยกเลิก จอที่ต้องการฟาร์ม (เช่น กด 6 เพื่อเพิ่ม/เอาออก จอ 6)")
            print("  [A]   เลือกทุกจอที่เปิดอยู่ (Select All Running VMs)")
            print("  [C]   รีเซ็ตเหลือเฉพาะจอ 0 (Reset to VM 0)")
            print("  [L]   สั่งเปิดจอที่เลือกไว้ทั้งหมดทันที (Launch Selected VMs)")
            print("  [K]   สั่งปิดจอที่เลือกไว้ทั้งหมด (Shutdown Selected VMs)")
            print("  [T]   จัดเรียงหน้าต่างทุกจอให้พอดีหน้าจอ (Tile / Sort Windows)")
            print("  [S]   เปิด/ปิด Auto-Launch MuMu อัตโนมัติ")
            print("  [W]   เปิด/ปิด Auto-Tile จัดเรียงหน้าต่างอัตโนมัติ")
            print("  [B]   กลับเมนูหลัก (Back to Main Menu)\n")

            choice = input(f"{PINK}cook45@multi_inst:~? {RESET}").strip()
            if not choice or choice.upper() == "B":
                break
            elif choice.isdigit():
                val = int(choice)
                if val in target_insts:
                    target_insts.remove(val)
                else:
                    target_insts.add(val)
                self.config["multi_instance"]["target_instances"] = sorted(list(target_insts))
                self._save_config()
            elif choice.upper() == "A":
                running_indices = [k for k, v in instances.items() if v.get("is_running")]
                self.config["multi_instance"]["target_instances"] = sorted(running_indices or list(instances.keys()) or [0])
                self._save_config()
            elif choice.upper() == "C":
                self.config["multi_instance"]["target_instances"] = [0]
                self._save_config()
            elif choice.upper() == "L":
                print(f"\n{CYAN}[+] กำลังสั่งเปิดทุกจอที่เลือก...{RESET}")
                for idx in sorted(target_insts):
                    if not self.mumu.is_instance_running(idx):
                        print(f"  -> สั่ง Launch MuMu VM #{idx}...")
                        self.mumu.launch_player(idx)
                        time.sleep(3)
                print(f"\n{GREEN}[+] ส่งคำสั่ง Launch ครบทุกจอแล้ว!{RESET}")
                time.sleep(1.5)
            elif choice.upper() == "K":
                print(f"\n{YELLOW}[!] กำลังสั่ง Shutdown ทุกจอที่เลือก...{RESET}")
                for idx in sorted(target_insts):
                    self.mumu.shutdown_player(idx)
                print(f"\n{GREEN}[+] สั่ง Shutdown เรียบร้อย!{RESET}")
                time.sleep(1.5)
            elif choice.upper() == "T":
                self.mumu.sort_windows()
                print(f"\n{GREEN}[+] จัดเรียงหน้าต่างทุกจอบน Desktop เรียบร้อย!{RESET}")
                time.sleep(1.2)
            elif choice.upper() == "S":
                cur = self.config.get("multi_instance", {}).get("auto_launch_on_start", True)
                self.config["multi_instance"]["auto_launch_on_start"] = not cur
                self._save_config()
            elif choice.upper() == "W":
                cur = self.config.get("multi_instance", {}).get("auto_arrange_windows", True)
                self.config["multi_instance"]["auto_arrange_windows"] = not cur
                self._save_config()

    def target_game_menu(self):
        while True:
            self.print_ascii_header()
            print(f"{CYAN}{BOLD}🎯 TARGET GAME & PER-VM SERVER SETUP (ตั้งค่าแมพ / ลิงก์เซิร์ฟเวอร์แบบ Rokid){RESET}\n")

            target_cfg = self.config.get("target", {})
            cur_place = target_cfg.get("place_id", "8304191830")
            cur_vip = target_cfg.get("private_server_link", "")
            per_links = target_cfg.get("per_instance_links", {})
            force_mins = self.config.get("watchdog", {}).get("force_rejoin_interval_minutes", 0)

            print(f"  1. Global Place ID          : {MAGENTA}{cur_place}{RESET}")
            print(f"  2. Global VIP Server Link   : {MAGENTA}{cur_vip or '(ไม่ได้ตั้ง - เข้าเซิร์ฟปกติตาม Place ID)'}{RESET}")
            print(f"  3. Force Rejoin Interval    : {CYAN}{f'ทุกๆ {force_mins} นาที' if force_mins > 0 else 'ปิด (รีจอยเฉพาะตอนหลุด/ค้าง)'}{RESET}")
            print(f"  4. Per-VM Custom Links      : {YELLOW}{len(per_links)} จอที่ตั้งแยกพิเศษ{RESET}")
            for k, v in sorted(per_links.items(), key=lambda x: int(x[0]) if str(x[0]).isdigit() else 99):
                print(f"     - VM #{k} -> {WHITE}{v[:65]}{RESET}")

            print("\nเลือกคำสั่ง:")
            print("  [1] ตั้งค่า Global Place ID (ใช้ร่วมกันทุกจอ)")
            print("  [2] ตั้งค่า Global Private / VIP Server Link (ใช้ร่วมกันทุกจอ)")
            print("  [3] ล้างค่า Global VIP Server Link (กลับไปใช้ Place ID)")
            print("  [4] ตั้งค่าแยกเซิร์ฟเวอร์ / แยก Place ID รายจอ (Different Server per VM - Rokid Mode)")
            print("  [5] ล้างค่าแยกเซิร์ฟเวอร์รายจอทั้งหมด")
            print("  [6] ตั้งเวลา Force Rejoin รีเฟรชเกมอัตโนมัติทุก X นาที (0 = ปิด)")
            print("  [0] ย้อนกลับเมนูหลัก\n")

            c = input(f"{PINK}cook45@target_cfg:~? {RESET}").strip()
            if not c or c == "0":
                break
            elif c == "1":
                new_id = input(f"{YELLOW}ใส่ Place ID ใหม่: {RESET}").strip()
                if new_id:
                    self.config["target"]["place_id"] = new_id
                    self._save_config()
            elif c == "2":
                new_vip = input(f"{YELLOW}วางลิงก์ Private Server URL: {RESET}").strip()
                if new_vip:
                    self.config["target"]["private_server_link"] = new_vip
                    self._save_config()
            elif c == "3":
                self.config["target"]["private_server_link"] = ""
                self._save_config()
            elif c == "4":
                vm_idx = input(f"{YELLOW}ระบุหมายเลขจอ VM (เช่น 0, 1, 2...): {RESET}").strip()
                if vm_idx.isdigit():
                    val = input(f"{YELLOW}ใส่ Place ID หรือ VIP Server URL สำหรับ VM #{vm_idx} (เว้นว่างเพื่อลบ): {RESET}").strip()
                    self.config["target"].setdefault("per_instance_links", {})
                    if val:
                        self.config["target"]["per_instance_links"][vm_idx] = val
                    else:
                        self.config["target"]["per_instance_links"].pop(vm_idx, None)
                    self._save_config()
            elif c == "5":
                self.config["target"]["per_instance_links"] = {}
                self._save_config()
            elif c == "6":
                mins_str = input(f"{YELLOW}ใส่จำนวนนาทีที่ต้องการ Force Rejoin (ใส่ 0 เพื่อปิด): {RESET}").strip()
                if mins_str.isdigit():
                    self.config["watchdog"]["force_rejoin_interval_minutes"] = int(mins_str)
                    self._save_config()

    def rokid_account_cookie_menu(self):
        while True:
            self.print_ascii_header()
            print(f"{CYAN}{BOLD}🍪 ROKID ACCOUNT & COOKIE TOOLKIT (จัดการไอดี & Cookie ทุกจอผ่าน Root RPC){RESET}\n")
            target_insts = self.config.get("multi_instance", {}).get("target_instances", [0])
            print(f"  จอที่ควบคุมปัจจุบัน: {WHITE}VMs {target_insts}{RESET}\n")

            print("เลือกฟังก์ชัน (เทียบเท่า Rokid-Manager ทุกฟีเจอร์ แต่ทำงานข้ามทุกจอ MuMu พร้อมกัน):")
            print("  [1] 🔍 สแกนรายชื่อไอดี Roblox + เช็คสถานะ Online/In-Game ของทุกจอทันที")
            print("  [2] 📥 ดึง Cookie (.ROBLOSECURITY) ออกจากทุกจอที่ล็อกอินอยู่ -> บันทึกลง Cookies Storage/cookies-data.txt")
            print("  [3] 💉 ยัด Cookie จากไฟล์ cookie.txt เข้าไปล็อกอินในแต่ละจออัตโนมัติ (Auto-Login via Cookie)")
            print("  [4] ✅ ตรวจสอบ Cookie ดี/เสีย จากไฟล์ cookie.txt (แยกไฟล์ live.txt / dead.txt)")
            print("  [0] ย้อนกลับเมนูหลัก\n")

            c = input(f"{PINK}cook45@rokid_toolkit:~? {RESET}").strip()
            if not c or c == "0":
                break
            elif c == "1":
                print(f"\n{CYAN}[+] กำลังสแกนข้อมูลบัญชีและเช็ค Roblox Presence API จากทั้ง {len(target_insts)} จอ...{RESET}\n")
                with ThreadPoolExecutor(max_workers=max(1, len(target_insts))) as ex:
                    probes = list(ex.map(lambda i: (i, self.mumu.probe_instance(i, include_logcat=False)), target_insts))
                uids = [p["user_id"] for _, p in probes if p.get("user_id")]
                presences = self.presence_tracker.query_batch_presence(uids)
                print(f"  {'VM#':<5} {'Username':<22} {'UserId':<14} {'PID':<8} {'Presence':<12} {'Game Server UDP'}")
                print("  " + "-" * 80)
                for idx, p in probes:
                    uid = p.get("user_id") or "N/A"
                    uname = self.presence_tracker.resolve_username(uid, p.get("username")) if uid != "N/A" else "Not Logged In"
                    p_label = presences.get(uid, {}).get("label", "UNKNOWN")
                    pid = str(p.get("pid") or "---")
                    srv = p.get("game_server_ip") or f"UDP:{p.get('udp_sockets', 0)}"
                    print(f"  #{idx:<4} {WHITE}{uname:<22}{RESET} {uid:<14} {pid:<8} {GREEN if p_label=='IN-GAME' else YELLOW}{p_label:<12}{RESET} {srv}")
                input(f"\n{GREEN}[+] สแกนเสร็จสมบูรณ์! (กด Enter เพื่อดำเนินการต่อ){RESET}")

            elif c == "2":
                print(f"\n{CYAN}[+] กำลังดึงฐานข้อมูล Cookies SQLite จากทุกจอผ่าน Root RPC...{RESET}\n")
                os.makedirs("Cookies Storage", exist_ok=True)
                out_file = os.path.join("Cookies Storage", "cookies-data.txt")
                saved_lines = []
                for idx in target_insts:
                    info = self.mumu.extract_account_and_cookie(idx)
                    uid = info.get("user_id") or ""
                    uname = self.presence_tracker.resolve_username(uid, info.get("username")) if uid else (info.get("username") or f"VM{idx}")
                    cookie = info.get("cookie")
                    if cookie:
                        saved_lines.append(f"{uname}:{uid}:{cookie}")
                        print(f"  {GREEN}✓ [VM #{idx}]{RESET} {uname} ({uid}) -> ดึง Cookie สำเร็จ! ({cookie[:35]}...)")
                    else:
                        print(f"  {RED}✗ [VM #{idx}]{RESET} ไม่พบ Cookie ในระบบ (อาจจะยังไม่ได้ล็อกอิน)")
                if saved_lines:
                    with open(out_file, "w", encoding="utf-8") as f:
                        f.write("\n".join(saved_lines) + "\n")
                    print(f"\n{GREEN}[+] บันทึก Cookie ทั้งหมด {len(saved_lines)} บัญชีไว้ที่: {out_file}{RESET}")
                input(f"\n{GRAY}(กด Enter เพื่อกลับ){RESET}")

            elif c == "3":
                cookie_file = "cookie.txt"
                if not os.path.isfile(cookie_file):
                    with open(cookie_file, "w", encoding="utf-8") as f:
                        f.write("")
                    input(f"\n{YELLOW}[!] สร้างไฟล์ cookie.txt ให้แล้ว! กรุณานำ Cookie มาวางบรรทัดละ 1 จอแล้วลองใหม่ (กด Enter){RESET}")
                    continue
                with open(cookie_file, "r", encoding="utf-8", errors="replace") as f:
                    raw_lines = [ln.strip() for ln in f.readlines() if ln.strip()]
                if not raw_lines:
                    input(f"\n{RED}[!] ไฟล์ cookie.txt ว่างเปล่า กรุณาใส่ Cookie ก่อน (กด Enter){RESET}")
                    continue
                print(f"\n{CYAN}[+] กำลังตรวจสอบและฉีด Cookie เข้าสู่ VM...{RESET}")
                for i, idx in enumerate(target_insts):
                    if i >= len(raw_lines):
                        break
                    _, _, cookie = CookieToolkit.parse_cookie_line(raw_lines[i])
                    ok, uid, uname = CookieToolkit.verify_cookie(cookie)
                    if not ok:
                        print(f"  {RED}✗ [VM #{idx}] Cookie บรรทัดที่ {i+1} ตายหรือไม่ถูกต้อง ข้าม...{RESET}")
                        continue
                    print(f"  {CYAN}→ [VM #{idx}] กำลังยัด Cookie ของ {uname} ({uid})...{RESET}")
                    if self.mumu.inject_cookie(idx, cookie, user_id=uid or "", username=uname or ""):
                        print(f"  {GREEN}✓ [VM #{idx}] Inject สำเร็จ! ({uname}){RESET}")
                    else:
                        print(f"  {RED}✗ [VM #{idx}] Inject ล้มเหลว{RESET}")
                input(f"\n{GREEN}[+] ดำเนินการเสร็จสิ้น! (กด Enter){RESET}")

            elif c == "4":
                print(f"\n{CYAN}[+] กำลังตรวจสอบ Cookie ในไฟล์ cookie.txt...{RESET}")
                res = CookieToolkit.check_cookies_file("cookie.txt")
                if res["created_empty"]:
                    print(f"{YELLOW}[!] ไม่พบไฟล์ cookie.txt -> สร้างไฟล์เปล่าให้แล้ว กรุณาใส่ Cookie แล้วลองใหม่{RESET}")
                else:
                    for i, d in enumerate(res["details"], 1):
                        if d["ok"]:
                            print(f"  {GREEN}[LIVE #{i}]{RESET} {d['username']} (UserId: {d['user_id']})")
                        else:
                            print(f"  {RED}[DEAD #{i}]{RESET} {d['username']}")
                    print(f"\n{CYAN}สรุปผล:{RESET} ทั้งหมด {res['total']} | {GREEN}ใช้ได้ (Live): {res['live']}{RESET} | {RED}เสีย (Dead): {res['dead']}{RESET}")
                    print(f"ไฟล์ถูกแยกเก็บไว้ในโฟลเดอร์ {WHITE}Cookies Storage/{RESET}")
                input(f"\n{GRAY}(กด Enter เพื่อกลับ){RESET}")

    def autoexec_manager_menu(self):
        while True:
            self.print_ascii_header()
            print(f"{CYAN}{BOLD}📜 AUTOEXECUTE SCRIPT MANAGER (จัดการสคริปต์รันอัตโนมัติใน Delta / Fluxus / Codex){RESET}\n")

            urls = self.config.get("target", {}).get("autoexec_urls", [])
            print(f"{WHITE}{BOLD}รายการ URL สคริปต์ที่จะถูกรันแบบขนาน (Non-Blocking task.spawn) ทุกครั้งที่เข้าเกม:{RESET}")
            if not urls:
                print(f"  {GRAY}(ไม่มี URL สคริปต์เสริม - จะรันเฉพาะ Watchdog Heartbeat Bridge ในตัว){RESET}")
            else:
                for i, u in enumerate(urls, 1):
                    print(f"  [{i}] {GREEN}{u}{RESET}")

            print("\nเลือกคำสั่ง:")
            print("  [1] เพิ่ม URL สคริปต์ Autoexec ใหม่")
            print("  [2] ลบ URL สคริปต์ตามหมายเลข")
            print("  [3] รีเซ็ตกลับเป็นค่าเริ่มต้น (xt-hub crazy.lua + AA_Macro tracker.lua)")
            print("  [4] 🚀 อัปเดตไฟล์ AA_Autoexec.lua เข้าสู่ทุกจอทันที (Push to All VMs Now)")
            print("  [0] ย้อนกลับเมนูหลัก\n")

            c = input(f"{PINK}cook45@autoexec:~? {RESET}").strip()
            if not c or c == "0":
                break
            elif c == "1":
                u = input(f"{YELLOW}วาง Raw Script URL ที่ต้องการเพิ่ม: {RESET}").strip()
                if u:
                    urls.append(u)
                    self.config["target"]["autoexec_urls"] = urls
                    self._save_config()
            elif c == "2":
                idx_s = input(f"{YELLOW}ใส่หมายเลขสคริปต์ที่ต้องการลบ: {RESET}").strip()
                if idx_s.isdigit() and 1 <= int(idx_s) <= len(urls):
                    urls.pop(int(idx_s) - 1)
                    self.config["target"]["autoexec_urls"] = urls
                    self._save_config()
            elif c == "3":
                self.config["target"]["autoexec_urls"] = [
                    "https://raw.githubusercontent.com/weziiotez-dev/xt-hub/main/crazy.lua",
                    "https://cdn.jsdelivr.net/gh/PeeKillxa/AA_Macro@main/tracker.lua"
                ]
                self._save_config()
            elif c == "4":
                target_insts = self.config.get("multi_instance", {}).get("target_instances", [0])
                print(f"\n{CYAN}[+] กำลังฉีด AA_Autoexec.lua (พร้อมระบบกันบั๊ก Code 285) เข้าสู่ VM {target_insts}...{RESET}")
                for idx in target_insts:
                    self.mumu.ensure_autoexecute_script(index=idx, script_urls=urls)
                    print(f"  {GREEN}✓ อัปเดต VM #{idx} สำเร็จ!{RESET}")
                input(f"\n{GREEN}[+] อัปเดตครบทุกจอเรียบร้อย! (กด Enter){RESET}")

    def windows_autostart_menu(self):
        self.print_ascii_header()
        print(f"{CYAN}{BOLD}⚡ WINDOWS AUTO-START SETUP (รันอัตโนมัติตอนเปิดเครื่อง / ไฟตก){RESET}\n")

        is_on = self.autorun_mgr.is_enabled()
        status_text = f"{GREEN}ENABLED (เปิดใช้งานอยู่ - ระบบจะเริ่มเองทันทีเมื่อคอมเปิด){RESET}" if is_on else f"{RED}DISABLED (ปิดอยู่){RESET}"

        print(f"  สถานะปัจจุบัน     : {status_text}")
        print(f"  โฟลเดอร์ Startup  : {WHITE}{self.autorun_mgr.startup_folder}{RESET}")
        print(f"  ไฟล์สคริปต์บูต    : {GRAY}{self.autorun_mgr.bat_path}{RESET}\n")

        print("เลือกคำสั่ง:")
        print("  [1] เปิดใช้งาน Auto-Start (สร้างตัวรันใน Windows Startup เผื่อไฟตก/รีสตาร์ท)")
        print("  [2] ปิดการใช้งาน Auto-Start (ลบออกจาก Startup)")
        print("  [0] ย้อนกลับไปเมนูหลัก\n")

        c = input(f"{PINK}cook45@autostart:~? {RESET}").strip()
        if c == "1":
            if self.autorun_mgr.enable():
                self.config["autorun"]["enabled"] = True
                self._save_config()
                input(f"\n{GREEN}[+] เปิดใช้งาน Auto-Start สำเร็จ! (กด Enter){RESET}")
            else:
                input(f"\n{RED}❌ ไม่สามารถเขียนไฟล์ใน Startup ได้ (กด Enter){RESET}")
        elif c == "2":
            self.autorun_mgr.disable()
            self.config["autorun"]["enabled"] = False
            self._save_config()
            input(f"\n{GREEN}[+] ปิดการทำงาน Auto-Start เรียบร้อยแล้ว (กด Enter){RESET}")

    def discord_webhook_menu(self):
        self.print_ascii_header()
        print(f"{CYAN}{BOLD}📡 DISCORD WEBHOOK SETUP (ตั้งค่าการแจ้งเตือน){RESET}\n")
        cur_url = self.config.get("notification", {}).get("discord_webhook_url", "")
        masked_url = (cur_url[:45] + "..." + cur_url[-8:]) if len(cur_url) > 55 else (cur_url or "(ยังไม่ได้ตั้งค่า)")
        print(f"  Webhook URL ปัจจุบัน: {MAGENTA}{masked_url}{RESET}\n")
        print("  [1] ใส่ / เปลี่ยน Webhook URL")
        print("  [2] ส่งข้อความทดสอบเข้า Discord (Test Ping)")
        print("  [3] ปิดการแจ้งเตือน Webhook")
        print("  [0] ย้อนกลับ\n")

        c = input(f"{PINK}cook45@webhook:~? {RESET}").strip()
        if c == "1":
            u = input(f"{YELLOW}วาง Discord Webhook URL: {RESET}").strip()
            if u:
                self.config["notification"]["discord_webhook_url"] = u
                self._save_config()
                input(f"\n{GREEN}[+] บันทึก Webhook เรียบร้อย! (กด Enter){RESET}")
        elif c == "2":
            if cur_url:
                self.log("กำลังส่งการแจ้งเตือนทดสอบเข้า Discord...", CYAN)
                self.send_discord_webhook_async("🧪 Rokid x Cook45 Watchdog v3.0 Test", "การเชื่อมต่อระบบแจ้งเตือนของ Roblox Multi-Watchdog v3.0 สมบูรณ์แบบ!", 0x9B59B6)
                time.sleep(1.5)
                input(f"\n{GREEN}[+] ส่งข้อมูลเข้า Discord แล้ว! (กด Enter){RESET}")
            else:
                input(f"\n{RED}❌ ยังไม่ได้ตั้งค่า Webhook URL (กด Enter){RESET}")
        elif c == "3":
            self.config["notification"]["discord_webhook_url"] = ""
            self._save_config()
            input(f"\n{GREEN}[+] ลบการแจ้งเตือน Webhook แล้ว (กด Enter){RESET}")

    def tile_windows_cmd(self, interactive: bool = True):
        self.print_ascii_header()
        self.log("🪟 กำลังจัดเรียงหน้าต่างทุกจอให้พอดีหน้าจอ (Tile Windows)...", CYAN)
        if self.is_mumu_mode():
            self.mumu.sort_windows()
            if interactive:
                input(f"\n{GREEN}[+] จัดเรียงหน้าต่าง MuMu ทุกจอบน Desktop สำเร็จ! (กด Enter){RESET}")
        else:
            if interactive:
                input(f"\n{YELLOW}[!] ฟังก์ชันนี้รองรับบน MuMu Player (กด Enter){RESET}")

    def rejoin_or_kill_menu(self):
        self.print_ascii_header()
        print(f"{CYAN}{BOLD}🛠️  REJOIN TEST & PROCESS CONTROL (ทดสอบรีจอย / สั่งปิดเกม){RESET}\n")
        target_insts = self.config.get("multi_instance", {}).get("target_instances", [0])
        print(f"  จอที่ควบคุมปัจจุบัน: {WHITE}{target_insts}{RESET}\n")
        print("  [1] 🚀 ทดสอบสั่ง Rejoin เฉพาะจอที่ระบุ (Two-Stage Launch)")
        print("  [2] 🔄 สั่ง Rejoin ใหม่ทุกจอพร้อมกัน")
        print("  [3] 🛑 สั่งบังคับปิด Roblox (Force Kill) ทุกจอ")
        print("  [0] ย้อนกลับ\n")

        c = input(f"{PINK}cook45@control:~? {RESET}").strip()
        if c == "1":
            idx_s = input(f"{YELLOW}ระบุหมายเลข VM ที่ต้องการทดสอบ Rejoin (เช่น 0): {RESET}").strip()
            if idx_s.isdigit():
                idx = int(idx_s)
                inst = InstanceState(idx, name=f"Device #{idx}")
                self.trigger_instance_rejoin(inst, "Manual Rejoin Test")
                input(f"\n{GREEN}[+] ทดสอบเสร็จสิ้น! สถานะ: {inst.status} (กด Enter){RESET}")
        elif c == "2":
            for idx in target_insts:
                inst = InstanceState(idx, name=f"Device #{idx}")
                self.log(f"กำลังสั่ง Rejoin VM #{idx}...", CYAN)
                threading.Thread(target=self.trigger_instance_rejoin, args=(inst, "Manual Batch Rejoin"), daemon=True).start()
                time.sleep(2)
            input(f"\n{GREEN}[+] ส่งคำสั่ง Rejoin ครบทุกจอแล้ว! (กด Enter){RESET}")
        elif c == "3":
            self.kill_all_roblox_cmd(interactive=True)

    def kill_all_roblox_cmd(self, interactive: bool = True):
        self.print_ascii_header()
        self.log("🛑 กำลังสั่งปิดและเคลียร์ Roblox ทุกจอ...", YELLOW)
        target_insts = self.config.get("multi_instance", {}).get("target_instances", [0])
        for idx in target_insts:
            if self.is_mumu_mode():
                self.mumu.kill_roblox(index=idx)
            else:
                self.adb.kill_roblox(serial=self.get_adb_serial_for_instance(idx))
        if interactive:
            input(f"\n{GREEN}[+] สั่งปิดและเคลียร์ Process Roblox ทุกจอเรียบร้อย! (กด Enter){RESET}")

    def test_rejoin_cmd(self, interactive: bool = True):
        self.print_ascii_header()
        target_insts = self.config.get("multi_instance", {}).get("target_instances", [0])
        target_idx = target_insts[0] if target_insts else 0
        self.log(f"🚀 กำลังทดสอบกระบวนการ Rejoin บน VM #{target_idx}...", CYAN)
        inst = InstanceState(target_idx, name=f"Device #{target_idx}")
        self.trigger_instance_rejoin(inst, "Manual CLI Rejoin Test")
        if interactive:
            input(f"\n{GREEN}[+] ทดสอบกระบวนการเสร็จสิ้น! สถานะ: {inst.status} (กด Enter){RESET}")

    def handle_autorun_boot(self):
        set_terminal_title("== Cook45 Auto-Boot Manager v3.0 == | System Started")
        self.print_ascii_header()
        print(f"{YELLOW}{BOLD}⚡ ตรวจพบการเปิดเครื่อง / กู้คืนจากไฟตก (WINDOWS AUTORUN DETECTED){RESET}")
        print(f"{WHITE}ระบบกำลังจะบูต MuMu Player ทุกจอ และเริ่มฟาร์ม Roblox อัตโนมัติใน 5 วินาที...{RESET}\n")

        for sec in range(5, 0, -1):
            sys.stdout.write(f"\r{MAGENTA}⏰ เริ่มต้นระบบใน {sec} วินาที... (กดปุ่มใดๆ เพื่อยกเลิกแล้วเข้าหน้าเมนูตั้งค่า) {RESET}")
            sys.stdout.flush()
            start_t = time.time()
            while time.time() - start_t < 1.0:
                if msvcrt and msvcrt.kbhit():
                    msvcrt.getch()
                    print(f"\n\n{GREEN}[+] ยกเลิกการรันอัตโนมัติแล้ว! กำลังเปิดเมนูหลัก...{RESET}\n")
                    time.sleep(1.0)
                    self.start_interactive_menu()
                    return
                time.sleep(0.05)

        print(f"\n\n{GREEN}[+] นับเวลาเสร็จสิ้น! บูตระบบ Multi-Instance ทันที!{RESET}\n")
        self.run_watchdog_loop()

    def start_interactive_menu(self):
        set_terminal_title("== Rokid x Cook45 Multi-Emu Manager v3.0 == | cook45 for clack")

        while True:
            self.print_ascii_header()
            self.print_menu_box()
            prompt = f"\n{PINK}cook45@clack:~? {RESET}"
            try:
                choice = input(prompt).strip()
            except (KeyboardInterrupt, EOFError):
                break

            if choice == "1":
                self.run_watchdog_loop()
            elif choice == "2":
                self.multi_instance_menu()
            elif choice == "3":
                self.target_game_menu()
            elif choice == "4":
                self.rokid_account_cookie_menu()
            elif choice == "5":
                self.autoexec_manager_menu()
            elif choice == "6":
                self.windows_autostart_menu()
            elif choice == "7":
                self.tile_windows_cmd()
            elif choice == "8":
                self.discord_webhook_menu()
            elif choice == "9":
                self.rejoin_or_kill_menu()
            elif choice == "0" or choice.lower() == "exit":
                self.presence_tracker.save_cache()
                print(f"\n{YELLOW}👋 ปิดโปรแกรมแล้ว เจอกันเพื่อนรัก clack!{RESET}")
                break


def main():
    parser = argparse.ArgumentParser(description="Rokid-Engine x Roblox Emulator Multi-Watchdog v3.0")
    parser.add_argument("--config", default="config.json", help="Path to config.json")
    parser.add_argument("--autorun", action="store_true", help="Started by Windows boot/startup")
    parser.add_argument("--start", action="store_true", help="Start watchdog immediately without menu")
    parser.add_argument("--rejoin-now", action="store_true", help="Trigger immediate rejoin test")
    parser.add_argument("--kill", action="store_true", help="Force stop Roblox and exit")
    parser.add_argument("--tile", action="store_true", help="Tile windows and exit")
    args = parser.parse_args()

    watchdog = EmuWatchdog(config_path=args.config)

    if args.autorun:
        watchdog.handle_autorun_boot()
        return

    if args.tile:
        watchdog.tile_windows_cmd(interactive=False)
        return

    if args.kill:
        watchdog.kill_all_roblox_cmd(interactive=False)
        return

    if args.rejoin_now:
        watchdog.test_rejoin_cmd(interactive=False)
        return

    if args.start:
        watchdog.run_watchdog_loop()
        return

    watchdog.start_interactive_menu()


if __name__ == "__main__":
    main()
