"""Build and sign a PC release. The private key is never copied to the repository."""
from pathlib import Path
import argparse
import base64
import hashlib
import json
import os
import re
import shutil
import subprocess
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding

root = Path(__file__).resolve().parent.parent
args = argparse.ArgumentParser()
args.add_argument("--repo", required=True, type=Path)
args.add_argument("--version", required=True)
args.add_argument("--notes", default="ปรับปรุง Nexus Auto-Rejoin PC")
options = args.parse_args()
if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", options.version):
    raise SystemExit("Version must have four numeric parts, for example 2.1.1.0")
key_path = Path(os.environ["LOCALAPPDATA"]) / "NexusRobloxManagerPublisher" / "pc-update-private.pem"
key = serialization.load_pem_private_key(key_path.read_bytes(), password=None)
version_file = root / "src" / "AppVersion.cs"
version_file.write_text(re.sub(r'Value = "[^"]+"', 'Value = "' + options.version + '"', version_file.read_text()), encoding="utf-8")
subprocess.run(["cmd.exe", "/c", "Build.bat"], cwd=root, check=True)
subprocess.run(["python", "checks/PackagePortable.py"], cwd=root, check=True)
pc = options.repo.resolve() / "Auto-Rejoin-PC"
releases = pc / "releases"
releases.mkdir(parents=True, exist_ok=True)
binary = (root / "NexusRobloxManager.exe").read_bytes()
asset = releases / ("NexusRobloxManager-" + options.version + ".exe")
if asset.exists() and asset.read_bytes() != binary:
    raise SystemExit("This version already exists. Increase the version before publishing a changed binary.")
asset.write_bytes(binary)
for folder in ["src", "checks", "docs"]:
    destination = pc / folder
    destination.mkdir(exist_ok=True)
    for source in (root / folder).iterdir():
        if source.is_file() and source.suffix in {".cs", ".ps1", ".py", ".md"}:
            shutil.copy2(source, destination / source.name)
for name in ["Build.bat", "app.ico", "app.manifest"]:
    shutil.copy2(root / name, pc / name)
shutil.copy2(root / "dist/NexusRobloxManager-Portable.zip", pc / "NexusRobloxManager-Portable.zip")
guide = pc / "dist/NexusRobloxManager-Portable"
guide.mkdir(parents=True, exist_ok=True)
shutil.copy2(root / "dist/NexusRobloxManager-Portable/START-HERE-TH.txt", guide / "START-HERE-TH.txt")
payload = json.dumps({
    "channel": "Auto-Rejoin-PC", "version": options.version,
    "url": "https://raw.githubusercontent.com/PeeKillxa/Auto-Rejoin/main/Auto-Rejoin-PC/releases/" + asset.name,
    "sha256": hashlib.sha256(binary).hexdigest(), "size": len(binary), "notes": options.notes,
}, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
signature = key.sign(payload, padding.PKCS1v15(), hashes.SHA256())
envelope = {"payload": base64.b64encode(payload).decode(), "signature": base64.b64encode(signature).decode()}
(pc / "update.json").write_text(json.dumps(envelope, indent=2) + "\n")
print("Signed PC release prepared:", options.version)
print("Review and commit the repository, then push main to publish the update.")
