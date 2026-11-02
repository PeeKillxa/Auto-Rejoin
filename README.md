# 🤖 Rokid-Engine x Roblox Multi-Emulator Watchdog v3.0 (Zero-Bug Edition)
**Author:** cook45 for clack  
**Platform:** Windows 10/11 • MuMu Player 12/15 (Native Hypervisor RPC) • LDPlayer 9 • BlueStacks

---

## 🔥 สิ่งที่ถูกยกเครื่องใหม่ทั้งหมดใน v3.0 (Rokid-Manager + Native Hypervisor RPC)

1. **⚡ Single-Shot Combined Guest Probe (ลดภาระ CPU 85% / ไม่มี Global Lock คอขวด)**:
   - แทนที่จะยิง `mumu-cli.exe` แยก 6–8 ครั้งต่อรอบเหมือนรุ่นเก่า ระบบใหม่รวบคำสั่งเช็ค **PID, Foreground Focus, Heartbeat, Disconnect Flag, Kernel RakNet UDP Socket (`/proc/net/udp`), ข้อมูลไอดี (`appStorage.json`), และ Logcat** เข้าเป็น **1 คำสั่งเดียวต่อรอบ**
   - ตัด Global Lock ทิ้ง ทำให้ทั้ง 6+ จอทำงานแบบขนานแท้ 100% (สแกนครบ 6 จอพร้อมกันใน ~1.3 วินาที)

2. **🛡️ 3-Layer Hybrid In-Game Verification (Heartbeat + Kernel RakNet UDP + Roblox Presence API)**:
   - **Layer 1 (Lua Heartbeat & GUI Hook):** ตรวจจับ `watchdog_heartbeat.txt` และ `executor_check_<Username>.txt` (มาตรฐาน Rokid) พร้อมดัก Error Popup ทันที
   - **Layer 2 (Linux Kernel `/proc/net/udp` RakNet Socket):** ตรวจสอบระดับ Kernel ของ Android ว่า UID ของแอป `com.roblox.client` มีการเชื่อมต่อ UDP ไปยังเซิร์ฟเวอร์เกมของ Roblox (`128.116.x.x:<port>`) อยู่จริงหรือไม่
   - **Layer 3 (Batched Roblox Presence API):** ยิงเช็คสถานะออนไลน์ของทุกไอดีพร้อมกันใน 1 HTTP Request (`IN-GAME` vs `HOME/WEB` vs `OFFLINE`) และแปลง `UserId` เป็นชื่อไอดีจริงอัตโนมัติ (แก้ปัญหาชื่อขึ้น `roblox_user_...`)

3. **🚫 แก้บั๊กฆ่าเกมตัวเองตอน Teleport / Replay (`Code 285: DisconnectClientInitiated`)**:
   - เมื่อเกมวาร์ปย้ายแมพ (เช่น Lobby <-> Story/Match หรือกด Replay ใน Anime Adventures) Roblox จะส่งรหัส `Code 285 (DisconnectClientInitiated)`
   - ระบบ v3.0 (ทั้งฝั่ง Python และ `tracker.lua` / `AA_Autoexec.lua`) กรองรหัส `285` ออกอย่างสมบูรณ์ และเปิดโหมด `TELEPORTING` ขยายเวลาโหลดแมพให้โดยไม่สั่งฆ่าเกมทิ้งกลางคัน
   - แยกคำสั่ง **Cold Launch (2-Stage: `ActivitySplash` -> `ActivityProtocolLaunch`)** ออกจาก **Warm Join Intent (`send_join_intent`)** โดยจะไม่สั่งลบ Cache (`rm -rf cache/*`) ขณะที่เกมกำลังรันอยู่เด็ดขาด

4. **🍪 Rokid Account & Cookie Toolkit (เมนู `[4]`)**:
   - **Auto-Detect Accounts:** ดึง `UserId` และ `Username` จากทุกจออัตโนมัติโดยไม่ต้องพิมพ์เอง
   - **Extract Cookies:** ดึง `.ROBLOSECURITY` ออกจากฐานข้อมูล SQLite (`/data/data/com.roblox.client/app_webview/Default/Cookies`) ของทุกจอผ่าน Root RPC มาลงไฟล์ `Cookies Storage/cookies-data.txt`
   - **Auto-Login via Cookie:** อ่าน Cookie จาก `cookie.txt` แล้วยัดเข้าฐานข้อมูลของแต่ละจอพร้อมตั้ง Permission (`chown`/`chmod`) ให้อัตโนมัติ
   - **Cookie Checker:** เช็ค Cookie ดี/เสีย แยกไฟล์ลง `Cookies Storage/live.txt` และ `dead.txt`

5. **📜 Non-Blocking Autoexec Manager (เมนู `[5]`)**:
   - รันสคริปต์ทุกตัวใน `AA_Autoexec.lua` แยกใน `task.spawn` อิสระ เพื่อไม่ให้สคริปต์ตัวแรกค้างขวางสคริปต์ตัวถัดไป พร้อมฝัง Watchdog Heartbeat Bridge ในตัว

---

## 🎮 วิธีใช้งาน (Usage Guide)

รันผ่านไฟล์:
```cmd
run.bat
```
หรือใช้ CLI Flags:
```cmd
python watchdog.py --start         # เริ่มมอนิเตอร์ทุกจอทันที
python watchdog.py --autorun       # โหมด Autorun ตอนเปิดเครื่อง
python watchdog.py --tile          # จัดเรียงหน้าต่างทุกจอบน Desktop
python watchdog.py --rejoin-now    # ทดสอบรีจอย
python watchdog.py --kill          # ปิด Roblox ทุกจอ
```

## อัปเดตให้เครื่องเพื่อน

- รัน `create_share_package.bat` เพื่อสร้างโฟลเดอร์แจกจ่ายชุดสะอาดครั้งแรก โดยไม่รวม `config.json`, webhook, cache, log หรือข้อมูลเฉพาะเครื่องของคุณ
- ให้เพื่อนตั้งค่า path ของ emulator และหมายเลข instance ใน `config.json` หลังเปิดครั้งแรก (โปรแกรมจะสร้างไฟล์ค่าเริ่มต้นให้)
- หลังจากนั้น เมื่อเปิด `run.bat` ระบบจะเช็ก GitHub repo `PeeKillxa/Auto-Rejoin` และอัปเดตเฉพาะไฟล์โปรแกรมที่อยู่ใน allowlist ก่อนเริ่ม watchdog
- เมื่อแก้โค้ดในเครื่องหลัก ให้รัน `publish_update.bat` เพื่อส่งไฟล์โปรแกรมขึ้น repo นี้ เพื่อนแค่ปิดแล้วเปิด `run.bat` ใหม่เพื่อรับเวอร์ชันล่าสุด
- ถ้า GitHub หรืออินเทอร์เน็ตใช้ไม่ได้ updater จะเปิดโปรแกรมเวอร์ชันเดิมต่อ และจะลองใหม่เมื่อเปิดครั้งถัดไป
- `config.json`, `username_cache.json`, log และข้อมูลประจำเครื่องไม่ถูกส่งขึ้น GitHub หรือเขียนทับระหว่างอัปเดต แต่ละคนเก็บ config ของตัวเองไว้

## ตรวจ UI ของ Anime Adventures

หลัง tracker ส่ง heartbeat และ watchdog เห็นว่าเข้าแมพแล้ว ระบบรอ 15 วินาที จากนั้นตรวจ `PlayerGui.XT_AnimeAdventuresUI` ว่ามีอยู่และเปิดใช้งานหรือไม่เป็นเวลา 90 วินาที หากไม่พบ UI ที่เปิดอยู่ภายในช่วงนี้ จะสั่ง rejoin เฉพาะ instance นั้น
