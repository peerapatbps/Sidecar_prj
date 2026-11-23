# Mock test — ทดสอบทั้ง workflow โดยไม่ต้องมี dongle / โปรแกรมชั่งจริง / เซิร์ฟเวอร์ Aquadat จริง

จำลอง 3 อย่าง:
- **โปรแกรมชั่ง** → ใช้ **Notepad** เป็นหน้าต่างเป้าหมายให้ sidecar เกาะ
- **data.mdb** → `mock\run\mock.mdb` (ก็อปโครงสร้างจริงมาแล้วล้างข้อมูล) สั่งชั่งเข้า/ออกด้วยสคริปต์
- **Aquadat** → เซิร์ฟเวอร์จำลองบน `localhost:18080` (คืน token ปลอม + ตอบ OK, บันทึก payload ที่รับ)

> ข้อมูลระดับถัง (`apiUrl`) ยังชี้ไปตัวจริง `172.16.198.150` เพราะเรียกได้อยู่แล้ว ถ้าต้องการตัดเน็ตให้แก้ `mock\run\config.json` เป็น URL ที่ไม่มีจริงได้ — sidecar จะขึ้น "ดึง API ไม่ได้" แต่ส่วนอื่นทำงานปกติ

คำสั่ง `sim.ps1` รีสตาร์ตตัวเองเป็น PowerShell x86 อัตโนมัติ (Jet เป็น 32 บิต) — เรียกจาก PowerShell ปกติได้เลย

---

## แบบที่ 2 — cmd เป็นโปรแกรมชั่ง + เมนู inject (แนะนำ)
ย้ายไปเป็นชุดพร้อมใช้ที่ **`mock\mock_deploy\`** แล้ว — ดับเบิลคลิก `mock_deploy\cmd-mock.exe` (ไอคอนเหมือนโปรแกรมชั่ง)
รายละเอียดเมนู / ไฟล์ / วิธีอัปเดต exe ดู [`mock_deploy\README.md`](mock_deploy/README.md)
- ตัวเปิด `cmd-mock.exe` build จาก `mock\launcher\` (`build.bat`)
- `sim.ps1` / `stop-mock.ps1` / `aquadat-stub.ps1` ในโฟลเดอร์นี้ยังใช้กับแบบที่ 1 (Notepad) — mock_deploy มีสำเนาของตัวเอง

---

## ครั้งแรก: build ให้มี exe ก่อน
```
dotnet publish -c Release -o publish
```

## ขั้นที่ 1 — เตรียมชุดจำลอง
```
powershell -ExecutionPolicy Bypass -File mock\sim.ps1 -Action setup
```
สร้าง `mock\run\` (exe + `mock.mdb` ว่าง + `config.json` ที่ชี้ไป stub และใช้ Notepad เป็นเป้าหมาย)

## ขั้นที่ 2 — เปิดเซิร์ฟเวอร์ Aquadat จำลอง (เทอร์มินอลแยก ปล่อยค้างไว้)
```
powershell -ExecutionPolicy Bypass -File mock\aquadat-stub.ps1
```
payload ที่รับจะถูกบันทึกใน `mock\received.log`

## ขั้นที่ 3 — เปิดหน้าต่างเป้าหมาย + sidecar
1. เปิด **Notepad** (แทนโปรแกรมชั่ง)
2. เปิด `mock\run\TruckSidecar.exe` → sidecar จะมาเกาะข้าง Notepad
   - ลองย้าย/ย่อ/ปิด Notepad ดู sidecar จะทำตาม (ลองกด maximize Notepad ดูโหมด AppBar)

## ขั้นที่ 4 — จำลองรถชั่งเข้า
```
powershell -ExecutionPolicy Bypass -File mock\sim.ps1 -Action weighin -Truck 70-9999 -Ticket 90001 -W1 25000 -Product 03
```
`-Product`: `01` = ALUM, `03` = PACL (ค่าเริ่มต้น), `02` = ทั่วไป (sidecar ไม่สนใจ)

**v2:** ภายใน ~3 วิ sidecar จะสลับไปแท็บตามสินค้า และเติมทะเบียน/Track ID ให้เอง (ไม่ต้องกดปุ่ม)
- ลองชั่งเข้ารถคันที่ 2 (`-Ticket 90002 -Product 01`) ขณะกำลังพิมพ์ฟอร์มอยู่ → ฟอร์มเดิมไม่ถูกแตะ รถใหม่ขึ้นเป็นชิปใน "รถรอบันทึก"
- ปุ่ม **"ดึงจากตาชั่ง"** ยังใช้ได้เป็นทางสำรอง

## ขั้นที่ 5 — กรอกฟอร์ม
กรอก ถ.พ. / อุณหภูมิ / ใบนำส่ง แล้วเพิ่มแถวถัง เช่น **PACL C** ก่อนเติม `11.9` (ค่าหลังเติมค่อยใส่หลังชั่งออก) กด **บันทึก** → ตั๋วจะไปอยู่รายการ "ยังบันทึกไม่ครบ"

## ขั้นที่ 6 — จำลองรถชั่งออก
```
powershell -ExecutionPolicy Bypass -File mock\sim.ps1 -Action weighout -Ticket 90001 -W2 11000 -Ticket2 80001 -W1 25000
```
ภายใน ~5 วินาที sidecar จะดึง W2/ตั๋วออก/น้ำหนักสุทธิเข้ามาเอง เปิดตั๋วนั้นมาใส่ระดับหลังเติม เช่น `24.1` แล้ว **บันทึก** → ตั๋ว "ครบ" → เข้าคิว Aquadat (แถบล่างขึ้น "Aquadat ค้างส่ง n")

## ขั้นที่ 7 — login + ส่ง
1. ล่างสุด กรอกรหัสผ่านอะไรก็ได้ (stub รับหมด) กด **"เข้าสู่ระบบ Aquadat"** → ได้ token ปลอม
2. ปุ่ม **"ส่ง Chemical income (n)"** โผล่มา กด → ส่งยอดตัดรายวัน
3. เปิด `mock\received.log` ดู payload จริงที่ถูกส่ง — ตรวจว่า `configparam_id`, `value` (ตัน), `time:"24:00"` ถูกต้อง

---

## คำสั่งช่วยอื่น ๆ
```
mock\sim.ps1 -Action reset          # ล้าง mock.mdb เริ่มใหม่
mock\sim.ps1 -Action weighin ...    # เพิ่มรถได้หลายคัน (Ticket ไม่ซ้ำ)
```
ทดสอบ interpolate/payload ล้วน ๆ โดยไม่เปิด GUI:
```
publish\TruckSidecar.exe --aqtest aqtest.txt
```

## รีเซ็ตข้อมูลที่ sidecar บันทึก
ลบ `mock\run\data\` (record + audit + outbox) แล้วเปิดใหม่ = เริ่มสะอาด
