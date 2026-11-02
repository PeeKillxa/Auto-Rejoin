"""
Autorun Manager for Windows
Handles automatic startup after Windows boot / power outage recovery.
Uses Windows Startup folder with startup delay to allow services (network/hypervisor) to initialize.
"""

import os
import sys
from typing import Optional


class AutorunManager:
    def __init__(self, target_script_dir: Optional[str] = None):
        self.workspace_dir = target_script_dir or os.path.dirname(os.path.abspath(__file__))
        self.startup_folder = os.path.join(
            os.environ.get("APPDATA", ""),
            r"Microsoft\Windows\Start Menu\Programs\Startup"
        )
        self.bat_filename = "Roblox_Emu_Watchdog.bat"
        self.bat_path = os.path.join(self.startup_folder, self.bat_filename)

    def is_enabled(self) -> bool:
        return os.path.isfile(self.bat_path)

    def enable(self) -> bool:
        """Create a startup launcher batch script in the user's Startup folder."""
        try:
            os.makedirs(self.startup_folder, exist_ok=True)
            run_bat = os.path.join(self.workspace_dir, "run.bat")

            content = f"""@echo off
chcp 65001 >nul
title Roblox Emu Watchdog - Auto Startup (cook45 for clack)
echo ======================================================================
echo  [+] Windows Boot / Power Restored: Starting Roblox Watchdog in 8s...
echo  [+] Allowing network and hypervisor services to initialize...
echo ======================================================================
timeout /t 8 /nobreak >nul
cd /d "{self.workspace_dir}"
start "Roblox Emu Watchdog" cmd /k "run.bat --autorun"
"""
            with open(self.bat_path, "w", encoding="utf-8") as f:
                f.write(content)
            return True
        except Exception as e:
            print(f"[!] Error enabling autorun: {e}")
            return False

    def disable(self) -> bool:
        """Remove startup script from Startup folder."""
        try:
            if os.path.isfile(self.bat_path):
                os.remove(self.bat_path)
            return True
        except Exception as e:
            print(f"[!] Error disabling autorun: {e}")
            return False

    def get_status_str(self) -> str:
        return "ENABLED" if self.is_enabled() else "DISABLED"
