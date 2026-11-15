from pathlib import Path
import zipfile

root = Path(__file__).resolve().parent.parent
stage = root / "dist" / "NexusRobloxManager-Portable"
stage.mkdir(parents=True, exist_ok=True)
exe = root / "NexusRobloxManager.exe"
(stage / exe.name).write_bytes(exe.read_bytes())
guide = stage / "START-HERE-TH.txt"
text = guide.read_text(encoding="utf-8-sig").replace("14 พฤศจิกายน 2026", "15 พฤศจิกายน 2026")
if "ซ่อนจอหลัง Rejoin" not in text:
    text += "\nซ่อนจอหลัง Rejoin\nกดซ่อนทุกจอครั้งเดียวเพื่อซ่อนจากหน้าจอ Taskbar และ Alt+Tab พร้อมจำค่าซ่อนไปยังจอที่เปิดใหม่\nกดแสดงทุกจอหรือ F10 เพื่อแสดงกลับและยกเลิกโหมดซ่อนจอใหม่อัตโนมัติ\nเลือกแสดงเฉพาะบัญชีได้ โดยบัญชีอื่นยังคงซ่อนตามเดิม\n"
guide.write_text(text, encoding="utf-8-sig")
if "อัปเดตจาก GitHub" not in text:
    text += "\nอัปเดตจาก GitHub\nเวอร์ชันนี้เป็นรุ่นเริ่มต้นที่มีระบบอัปเดตในตัว เพื่อนต้องลงรุ่นนี้ครั้งเดียว\nหลังจากนั้นโปรแกรมตรวจเวอร์ชันใหม่ตอนเปิดและทุก 30 นาที หรือกดปุ่มตรวจอัปเดตด้านบนได้\nการติดตั้งอัปเดตจะรอให้ Nexus ปิดก่อนแทนไฟล์ เก็บไฟล์เก่าไว้ และคงการตั้งค่าเดิม\n\nเปิด-ปิด Auto Rejoin รายบัญชี\nคลิกคอลัมน์ Auto Rejoin ที่แถวบัญชีเพื่อสลับ ON/OFF หรือคลิกขวาเลือกเปิด/ปิดบัญชีที่เลือก\nการปิดจะยกเลิกคิวของบัญชีนั้น และไม่เปลี่ยนบัญชีอื่น ค่าจะจำไว้แม้ปิดเปิดโปรแกรมใหม่\n"
    guide.write_text(text, encoding="utf-8-sig")
if "เวลาล็อกอิน Real" not in text:
    text += "\nเวลาล็อกอิน Real\nปิด Real แล้วกดปุ่มเวลาล็อกอิน Real ใน Nexus เลือกตั้งเป็น 15 นาที แล้วเปิด Real ใหม่เพื่อเพิ่มบัญชีตามเดิม\nรองรับเฉพาะไฟล์ Real 3.1.1 ที่ตรวจสอบแล้ว มีปุ่มคืนค่า 2 นาทีและเก็บไฟล์สำรองไว้ในเครื่อง\nCAPTCHA และรหัส 2 ชั้นยังกรอกตามปกติ เมื่อ Real อัปเดต โปรแกรมจะตรวจรุ่นอีกครั้งก่อนแก้ไฟล์\n"
    guide.write_text(text, encoding="utf-8-sig")
if "ฟีเจอร์เวลาล็อกอินยังเป็นรุ่นทดลอง" not in text:
    text += "\nฟีเจอร์เวลาล็อกอินยังเป็นรุ่นทดลอง: Windows Security บล็อกสำเนา Real ระหว่างทดสอบ จึงยังไม่ยืนยันการใช้กับไฟล์ที่ติดตั้งและการล็อกอินจริง\n"
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
