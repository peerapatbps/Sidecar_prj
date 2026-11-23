# TruckLauncher — ตัวเปิดโปรแกรมชั่ง + sidecar พร้อมกัน

## ทำไมต้องมี
พนักงานคุ้นกับการดับเบิลคลิกไอคอนโปรแกรมชั่ง จึงมีโอกาสเปิดแต่ `truck93a.exe` ไม่เปิด sidecar
Launcher นี้มี **ไอคอนเหมือนโปรแกรมชั่งทุกอย่าง** พอคลิกจะเปิด **โปรแกรมชั่งเดิม + sidecar** พร้อมกัน แล้วตัวมันเองจบการทำงานหายไป

## วิธี deploy
1. ก็อป `publish\TruckLauncher.exe` + `publish\launcher.json` ไปวางบนเครื่องโรงงาน (เช่น `C:\TruckSidecar\`)
2. แก้ `launcher.json` ให้ชี้ path ถูกต้อง:
   ```json
   {
     "truckExe":   "D:\\TRUCK93A\\truck93a.exe",
     "sidecarExe": "C:\\TruckSidecar\\TruckSidecar.exe",
     "sidecarProcess": "TruckSidecar"
   }
   ```
   (ใช้ backslash คู่ `\\` เสมอในไฟล์ JSON)
3. **เปลี่ยน shortcut บน Desktop / Taskbar ของพนักงาน** ให้ชี้มาที่ `TruckLauncher.exe` แทน `truck93a.exe`
   - ตั้งชื่อ shortcut ให้เหมือนเดิม เช่น "โปรแกรมชั่งรถ" พนักงานจะไม่รู้สึกว่าเปลี่ยน
   - ถ้าจะให้แนบเนียนกว่านี้ ตั้งชื่อไฟล์เป็นอย่างอื่นแล้วซ่อน exe จริงไว้ก็ได้

## พฤติกรรม
- เปิดโปรแกรมชั่งเดิม (จากโฟลเดอร์ของมันเอง เพื่อให้อ่าน `truck.ini` ถูก)
- เปิด sidecar (ถ้ายังไม่เปิดอยู่ — เช็ก process ก่อน ไม่เปิดซ้ำ)
- ตัว launcher จบทันที ไม่มีหน้าต่างค้าง
- ถ้าหา path ไม่เจอ / `launcher.json` ผิด จะเด้งข้อความเตือน (แต่โปรแกรมชั่งยังเปิดได้ถ้าเจอ path ของมัน)

## ข้อควรรู้
- ไอคอนดึงมาจาก `truck93a.exe` จริง (`truck.ico`) จึงเหมือนกัน 100%
- exe เป็น x86 self-contained (รันได้บนเครื่องที่มีแต่ .NET x86 หรือไม่มี .NET เลย)
- ไม่ได้แก้หรือแตะ `truck93a.exe` — แค่ "เรียกเปิด" เท่านั้น
- ถ้าพนักงานปิด sidecar ทิ้งระหว่างใช้งาน launcher ช่วยได้แค่ตอนเปิด ถ้าต้องการกันปิดถาวรให้ใช้ watchdog / Task Scheduler เพิ่ม (ดู handoff)
