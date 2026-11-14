"""
LDPlayer Console (ldconsole.exe) Controller
Allows programmatic start, stop, reboot, and instance monitoring.
"""

import os
import subprocess
from typing import List, Dict, Optional


class LDConsoleController:
    def __init__(self, console_path: str = r"E:\LDPlayer\LDPlayer9\ldconsole.exe"):
        self.console_path = console_path if os.path.isfile(console_path) else self._locate_console()

    def _locate_console(self) -> Optional[str]:
        candidates = [
            r"E:\LDPlayer\LDPlayer9\ldconsole.exe",
            r"C:\LDPlayer\LDPlayer9\ldconsole.exe",
            r"D:\LDPlayer\LDPlayer9\ldconsole.exe",
        ]
        for c in candidates:
            if os.path.isfile(c):
                return c
        return None

    def is_available(self) -> bool:
        return self.console_path is not None and os.path.isfile(self.console_path)

    def _run(self, args: List[str], timeout: int = 10) -> str:
        if not self.is_available():
            return ""
        cmd = [self.console_path] + args
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

    def list_instances(self) -> List[Dict[str, str]]:
        """
        Runs 'ldconsole.exe list2'
        Returns list of instance dicts: {index, name, pid, is_running}
        """
        raw = self._run(["list2"], timeout=6)
        instances = []
        for line in raw.splitlines():
            parts = line.strip().split(",")
            if len(parts) >= 6:
                idx = parts[0]
                name = parts[1]
                in_android = parts[4] == "1"
                pid = parts[5]
                instances.append({
                    "index": int(idx),
                    "name": name,
                    "pid": int(pid) if pid.isdigit() else 0,
                    "is_running": in_android or (int(pid) > 0 if pid.isdigit() else False),
                })
        return instances

    def launch(self, index: int = 0) -> bool:
        """Launch LDPlayer instance by index."""
        out = self._run(["launch", "--index", str(index)], timeout=15)
        return True

    def reboot(self, index: int = 0) -> bool:
        """Reboot LDPlayer instance."""
        out = self._run(["reboot", "--index", str(index)], timeout=15)
        return True

    def quit(self, index: int = 0) -> bool:
        """Close LDPlayer instance."""
        out = self._run(["quit", "--index", str(index)], timeout=15)
        return True

    def is_running(self, index: int = 0) -> bool:
        out = self._run(["isrunning", "--index", str(index)], timeout=5).lower()
        return "running" in out
