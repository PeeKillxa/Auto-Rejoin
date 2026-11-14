# Nexus Auto-Rejoin PC

สำหรับ Roblox บน Windows 10/11 โดยใช้บัญชีและเกมที่ตั้งไว้ใน Real ของเครื่องผู้ใช้

## ติดตั้งครั้งแรก

1. ดาวน์โหลด [NexusRobloxManager-Portable.zip](https://github.com/PeeKillxa/Auto-Rejoin/raw/main/Auto-Rejoin-PC/NexusRobloxManager-Portable.zip) แล้วแตกไฟล์
2. เปิดบัญชีและเกมผ่าน Real ก่อน เปิด Auto Inject / All Instances / Auto Execute และปิด Auto Rejoin ของ Real
3. เปิด `NexusRobloxManager.exe` โดยเปิด Real ค้างไว้

รุ่นเก่าที่ไม่มี updater ต้องลงแพ็กเกจนี้หนึ่งครั้ง หลังจากนั้นโปรแกรมตรวจอัปเดตตอนเปิดและทุก 30 นาที หรือกด **ตรวจอัปเดต** ด้านบนได้ เมื่อมีรุ่นใหม่ โปรแกรมจะแสดงปุ่มอัปเดตและให้ยืนยันก่อนติดตั้ง

## เปิด/ปิด Rejoin รายบัญชี

คลิกคอลัมน์ **Auto Rejoin** ในแถวบัญชีเพื่อสลับ ON/OFF หรือเลือกหลายแถวแล้วคลิกขวาเพื่อเปิด/ปิดพร้อมกัน การปิดยกเลิกคิวของบัญชีนั้น และจำไว้หลังเปิดโปรแกรมใหม่ บัญชีอื่นและสวิตช์หลักไม่เปลี่ยน จอที่ยังระบุบัญชีไม่ได้จะแสดง `—`

## อัปเดตและข้อมูลผู้ใช้

ช่องทาง PC ใช้ `Auto-Rejoin-PC/update.json` และตรวจลายเซ็น RSA พร้อม SHA-256 ของ executable แยกจากชุด Emulator โปรแกรมรอ Nexus เก่าปิดก่อนแทนไฟล์ เก็บไฟล์สำรอง `.previous` และเปิดรุ่นใหม่ หากเปิดรุ่นใหม่ล้มเหลวจะพยายามคืนไฟล์เดิม

ตัว updater แทนเฉพาะ executable ไม่เขียนทับบัญชี Real หรือข้อมูลใน `%APPDATA%\NexusRobloxManager` ถ้าอินเทอร์เน็ตใช้ไม่ได้ก็ใช้งานรุ่นเดิมต่อได้ และจะไม่ติดตั้งระหว่าง watchdog กำลังทำงานกับโปรเซส

## สำหรับผู้ดูแล: ออกอัปเดตครั้งถัดไป

แก้โค้ดในโฟลเดอร์นี้ แล้วรันจาก root ของ repo:

```powershell
.\publish_pc_update.ps1 -Version 2.1.1.0 -Notes "รายละเอียดที่แก้"
```

สคริปต์ build โปรแกรม สร้างแพ็กเกจ ลงลายเซ็น manifest และ commit/push เฉพาะชุด PC ใช้ Windows .NET Framework compiler และ Python ที่ติดตั้ง `cryptography` บนเครื่องผู้เผยแพร่ ผู้ใช้งานโปรแกรมไม่ต้องติดตั้ง Python

กุญแจลงลายเซ็นอยู่เฉพาะเครื่องผู้เผยแพร่ที่ `%LOCALAPPDATA%\NexusRobloxManagerPublisher\pc-update-private.pem` ไม่อยู่ใน repo หรือ ZIP ให้สำรองกุญแจนี้ไว้ในที่ส่วนตัว เพราะโปรแกรมที่แจกไปจะตรวจด้วย public key เดิม

หากพัฒนาโค้ดใน workspace อีกแห่ง ให้เรียก `python checks/PublishRelease.py --repo <path-to-repo> --version <new-version> --notes <text>` จาก workspace นั้น แล้วตรวจ diff / commit / push ที่ repo

## Build / ตรวจสอบ

```powershell
.\Build.bat
.\checks\Run.ps1
```

ชุดตรวจสอบใช้บัญชีและหน้าต่างจำลอง ไม่แก้บัญชี Real จริง และทดสอบ helper อัปเดตจริงกับโปรแกรมทดสอบที่ไม่เกี่ยวกับ Roblox
