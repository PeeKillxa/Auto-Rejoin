"""Package the self-contained Windows app and public Thai guide only."""
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parent.parent
stage = root / "dist" / "NexusRobloxManager-Portable"
stage.mkdir(parents=True, exist_ok=True)
exe = root / "NexusRobloxManager.exe"
(stage / exe.name).write_bytes(exe.read_bytes())
guide = stage / "START-HERE-TH.txt"
text = (root / "docs" / guide.name).read_text(encoding="utf-8-sig")
text += "\n\nMicrosoft WebView2 SDK license\n" + (root / "lib" / "WebView2" / "LICENSE.txt").read_text(encoding="utf-8-sig")
guide.write_text(text, encoding="utf-8-sig")
archive = root / "dist" / "NexusRobloxManager-Portable.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
    for name in [exe.name, guide.name]:
        package.write(stage / name, "NexusRobloxManager/" + name)
with zipfile.ZipFile(archive) as package:
    assert package.testzip() is None
    assert package.namelist() == ["NexusRobloxManager/NexusRobloxManager.exe", "NexusRobloxManager/START-HERE-TH.txt"]
    assert package.read("NexusRobloxManager/NexusRobloxManager.exe") == exe.read_bytes()
print("Portable ZIP verified:", archive)
