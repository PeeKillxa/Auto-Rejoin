"""
Rokid-Manager Services & Roblox Cloud API Engine for Multi-Instance Emulators v3.0
Author: cook45 for clack
Provides:
 - Batched Roblox Presence API tracking (In-Game vs Website/Home Screen vs Offline)
 - Automatic UserId <-> Username resolution & persistent caching (username_cache.json)
 - Multi-VM Cookie Extraction (SQLite over Root RPC -> Cookies Storage/cookies-data.txt)
 - Multi-VM Cookie Auto-Login / Injection (cookie.txt -> each VM's Chromium Cookies DB)
 - Bulk Cookie Alive/Dead Checker (live.txt / dead.txt)
"""

import os
import json
import time
import threading
import urllib.request
import urllib.error
from typing import Dict, Any, List, Optional, Tuple


class RobloxPresenceTracker:
    def __init__(self, cache_file: str = "username_cache.json"):
        self.cache_file = cache_file
        self.username_cache: Dict[str, str] = {}
        self.game_name_cache: Dict[str, str] = {}
        self._lock = threading.RLock()
        self._load_cache()

    def _load_cache(self):
        if os.path.isfile(self.cache_file):
            try:
                with open(self.cache_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    if isinstance(data, dict):
                        self.username_cache = {str(k): str(v) for k, v in data.items()}
            except Exception:
                pass

    def save_cache(self):
        with self._lock:
            try:
                with open(self.cache_file, "w", encoding="utf-8") as f:
                    json.dump(self.username_cache, f, indent=2, ensure_ascii=False)
            except Exception:
                pass

    def resolve_username(self, user_id: str, current_hint: Optional[str] = None) -> str:
        """
        Resolve real Roblox Username from UserId.
        Automatically replaces placeholder names like 'roblox_user_9875878167'.
        """
        uid_str = str(user_id or "").strip()
        if not uid_str or not uid_str.isdigit():
            return current_hint or "Unknown"

        if current_hint and not current_hint.startswith("roblox_user_") and current_hint != "Unknown":
            with self._lock:
                if self.username_cache.get(uid_str) != current_hint:
                    self.username_cache[uid_str] = current_hint
                    self.save_cache()
            return current_hint

        with self._lock:
            cached = self.username_cache.get(uid_str)
            if cached and not cached.startswith("roblox_user_"):
                return cached

        for base_url in ("https://users.roblox.com/v1/users/", "https://users.roproxy.com/v1/users/"):
            try:
                req = urllib.request.Request(
                    f"{base_url}{uid_str}",
                    headers={"User-Agent": "Mozilla/5.0", "Accept": "application/json"}
                )
                with urllib.request.urlopen(req, timeout=5) as resp:
                    data = json.loads(resp.read().decode("utf-8", errors="replace"))
                    name = data.get("name")
                    if name:
                        with self._lock:
                            self.username_cache[uid_str] = name
                        self.save_cache()
                        return name
            except Exception:
                continue

        return current_hint or f"User_{uid_str}"

    def resolve_user_id_from_username(self, username: str) -> Optional[str]:
        uname = (username or "").strip()
        if not uname:
            return None
        if uname.isdigit():
            return uname

        payload = json.dumps({"usernames": [uname], "excludeBannedUsers": False}).encode("utf-8")
        for url in ("https://users.roblox.com/v1/usernames/users", "https://users.roproxy.com/v1/usernames/users"):
            try:
                req = urllib.request.Request(
                    url,
                    data=payload,
                    headers={"Content-Type": "application/json", "User-Agent": "Mozilla/5.0"}
                )
                with urllib.request.urlopen(req, timeout=6) as resp:
                    data = json.loads(resp.read().decode("utf-8", errors="replace"))
                    items = data.get("data", [])
                    if items and items[0].get("id"):
                        uid = str(items[0]["id"])
                        real_name = items[0].get("name", uname)
                        with self._lock:
                            self.username_cache[uid] = real_name
                        self.save_cache()
                        return uid
            except Exception:
                continue
        return None

    def query_batch_presence(self, user_ids: List[str]) -> Dict[str, Dict[str, Any]]:
        """
        Query Roblox Presence API for all VM UserIds in a SINGLE HTTP POST request!
        userPresenceType:
          0 = Offline
          1 = Online (Website / Home Screen)
          2 = In-Game
          3 = In-Studio
        """
        valid_ids = [int(u) for u in set(user_ids) if str(u).isdigit()]
        if not valid_ids:
            return {}

        payload = json.dumps({"userIds": valid_ids}).encode("utf-8")
        result: Dict[str, Dict[str, Any]] = {}

        for url in ("https://presence.roblox.com/v1/presence/users", "https://presence.roproxy.com/v1/presence/users"):
            try:
                req = urllib.request.Request(
                    url,
                    data=payload,
                    headers={"Content-Type": "application/json", "User-Agent": "Mozilla/5.0"}
                )
                with urllib.request.urlopen(req, timeout=6) as resp:
                    data = json.loads(resp.read().decode("utf-8", errors="replace"))
                    for item in data.get("userPresences", []):
                        uid_str = str(item.get("userId", ""))
                        ptype = int(item.get("userPresenceType", 0))
                        loc = str(item.get("lastLocation", "") or "")
                        if loc == "Website" and ptype == 1:
                            label = "HOME/WEB"
                        elif ptype == 2:
                            label = "IN-GAME"
                        elif ptype == 1:
                            label = "HOME/APP"
                        else:
                            label = "OFFLINE"
                        result[uid_str] = {
                            "type": ptype,
                            "label": label,
                            "location": loc,
                            "place_id": item.get("placeId"),
                            "job_id": item.get("gameId"),
                        }
                    return result
            except Exception:
                continue

        return result


class CookieToolkit:
    @staticmethod
    def parse_cookie_line(raw_line: str) -> Tuple[Optional[str], Optional[str], str]:
        """Parse 'user:pass:cookie' or raw '_|WARNING:...' cookie string."""
        line = raw_line.strip()
        if not line:
            return None, None, ""
        if "_|WARNING:" in line:
            idx = line.find("_|WARNING:")
            prefix = line[:idx].rstrip(":")
            cookie = line[idx:].strip()
            if ":" in prefix:
                parts = prefix.split(":", 1)
                return parts[0], parts[1], cookie
            elif prefix:
                return prefix, None, cookie
            return None, None, cookie
        if line.count(":") >= 2:
            parts = line.split(":", 2)
            return parts[0], parts[1], parts[2]
        return None, None, line

    @staticmethod
    def verify_cookie(cookie: str) -> Tuple[bool, Optional[str], Optional[str]]:
        """
        Verify a .ROBLOSECURITY cookie against Roblox Authenticated User API.
        Returns: (is_valid, user_id, username)
        """
        clean_cookie = (cookie or "").strip()
        if not clean_cookie:
            return False, None, None

        req = urllib.request.Request(
            "https://users.roblox.com/v1/users/authenticated",
            headers={
                "Cookie": f".ROBLOSECURITY={clean_cookie}",
                "User-Agent": "Mozilla/5.0",
                "Accept": "application/json",
            }
        )
        try:
            with urllib.request.urlopen(req, timeout=7) as resp:
                if resp.status == 200:
                    data = json.loads(resp.read().decode("utf-8", errors="replace"))
                    return True, str(data.get("id", "")), str(data.get("name", ""))
        except urllib.error.HTTPError:
            return False, None, None
        except Exception:
            return False, None, None
        return False, None, None

    @classmethod
    def check_cookies_file(cls, file_path: str = "cookie.txt", storage_dir: str = "Cookies Storage") -> Dict[str, Any]:
        os.makedirs(storage_dir, exist_ok=True)
        live_path = os.path.join(storage_dir, "live.txt")
        dead_path = os.path.join(storage_dir, "dead.txt")

        if not os.path.isfile(file_path):
            with open(file_path, "w", encoding="utf-8") as f:
                f.write("")
            return {"total": 0, "live": 0, "dead": 0, "created_empty": True, "live_path": live_path, "dead_path": dead_path, "details": []}

        with open(file_path, "r", encoding="utf-8", errors="replace") as f:
            lines = [ln.strip() for ln in f.readlines() if ln.strip()]

        live_list = []
        dead_list = []
        details = []

        for ln in lines:
            user, pwd, cookie = cls.parse_cookie_line(ln)
            ok, uid, real_user = cls.verify_cookie(cookie)
            if ok:
                final_user = real_user or user or "Unknown"
                entry = f"{final_user}:{pwd}:{cookie}" if pwd else f"{final_user}:{uid}:{cookie}"
                live_list.append(entry)
                details.append({"ok": True, "username": final_user, "user_id": uid})
            else:
                dead_list.append(ln)
                details.append({"ok": False, "username": user or "Unknown", "user_id": None})

        with open(live_path, "w", encoding="utf-8") as f:
            f.write("\n".join(live_list) + ("\n" if live_list else ""))
        with open(dead_path, "w", encoding="utf-8") as f:
            f.write("\n".join(dead_list) + ("\n" if dead_list else ""))

        return {
            "total": len(lines),
            "live": len(live_list),
            "dead": len(dead_list),
            "created_empty": False,
            "live_path": live_path,
            "dead_path": dead_path,
            "details": details,
        }
