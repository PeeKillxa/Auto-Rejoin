"""Restore the pinned official Microsoft WebView2 SDK build inputs. No runtime is installed."""
from hashlib import sha256
from io import BytesIO
from pathlib import Path
from urllib.request import urlopen
from zipfile import ZipFile
import json

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "lib" / "WebView2"
VERSION = "1.0.4314.35"
URL = f"https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/{VERSION}/microsoft.web.webview2.{VERSION}.nupkg"
PACKAGE_SHA256 = "90778deb1b77e36292828b4b5833c9255c27533f40e0ddd55891ae718944edc4"
FILES = {
    "lib/net462/Microsoft.Web.WebView2.Core.dll": ("Microsoft.Web.WebView2.Core.dll", "acdd517b951829f763d1255f32df1e6a936cb8623a7ebff6b8b24172dede6bd6"),
    "lib/net462/Microsoft.Web.WebView2.WinForms.dll": ("Microsoft.Web.WebView2.WinForms.dll", "c66437f80886aecf074804b61af4cc10ad90a95310c8e0a717f31ea377c26b03"),
    "runtimes/win-x64/native/WebView2Loader.dll": ("x64/WebView2Loader.dll", "d3fce90bdeecb08596e377dea6a316354dd8949e32ff09b89d51be00c14e21b6"),
    "runtimes/win-x86/native/WebView2Loader.dll": ("x86/WebView2Loader.dll", "1d348d3c0a5cb0d985389b68a0fd9bf966c2aed65f5e44bbc0233739b7a57f9e"),
}

with urlopen(URL, timeout=45) as response:
    package = response.read(32 * 1024 * 1024 + 1)
if len(package) > 32 * 1024 * 1024 or sha256(package).hexdigest() != PACKAGE_SHA256:
    raise SystemExit("Official SDK package hash does not match the pinned build input")
with ZipFile(BytesIO(package)) as archive:
    for source, (target, expected) in FILES.items():
        content = archive.read(source)
        if sha256(content).hexdigest() != expected:
            raise SystemExit(f"SDK member hash mismatch: {source}")
        path = DEST / target
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
    (DEST / "LICENSE.txt").write_bytes(archive.read("LICENSE.txt"))
(DEST / "package.json").write_text(json.dumps({
    "package": "Microsoft.Web.WebView2", "version": VERSION, "url": URL,
    "sha256": PACKAGE_SHA256, "files": {target: digest for target, digest in FILES.values()},
}, indent=2) + "\n", encoding="utf-8")
print("Verified Microsoft WebView2 SDK build inputs:", VERSION)
