# Handoff — TruckSidecar อัตราจ่ายสารจริง (2026-10-03)

ต่อจาก [handoff2609.md](handoff2609.md) — ไฟล์นี้ครอบคลุมเฉพาะ session วันที่ 03/10/2026:
**เปลี่ยนอัตราใช้สารในการคาดการณ์ระดับถัง จากค่าคงที่ใน config → อัตราจ่ายจริงที่คำนวณจาก API**

แผนที่อนุมัติ (รายละเอียดการออกแบบ): `C:\Users\peera\.claude\plans\async-hugging-stardust.md`

---

## 0. สถานะปัจจุบัน (อ่านตรงนี้ก่อน)

| เรื่อง | สถานะ |
|---|---|
| โค้ด + config ต้นฉบับ | ✅ build ผ่าน (Debug + Release publish) |
| ตัวเลขที่คำนวณได้ | ✅ ผู้ใช้ยืนยัน "คำนวณได้ถูกต้อง" |
| `Sidecar_prj_deploy\TruckSidecar\` | ✅ exe/pdb/config ตรงกับ `TruckSidecar\publish\` + `config.json` ต้นฉบับ (ตรวจด้วย `cmp`) — `data\` ไม่ถูกแตะ |
| เครื่องใช้งาน | ✅ ผู้ใช้ deploy แล้ว — **ยังไม่มีผลยืนยันบนหน้าจอเครื่องใช้งาน** |
| Backup ไฟล์ deploy เดิม | `Sidecar_prj\_backup\TruckSidecar_deploy_20261003_133551\` |
| `defaultUsagePerHour.PACL` | เปลี่ยนต้นฉบับเป็น **1.0** ให้ตรงกับที่ deploy ใช้อยู่ (คอมเมนต์ใน config ยังเขียน ≈0.75 ตามที่มาเดิม) |

---

## 1. สิ่งที่ทำ

**ที่มาของสูตร:** configID อัตราจ่ายตรง (Alum 87/90 L/hr, 149/152 L/min; PACl 8401/8403/8415/8417 L/hr) **ยังไม่มีใน `/api/chem/summary`** ผู้ใช้จึงให้คำนวณแบบ BellBeast
`C:\Users\peera\OneDrive\Desktop\BellBeast\BellBeast\BellBeast\wwwroot\js\CHEMview.js`:

```
L/ชม. = flow × dose(mg/L) × factor        flow: Ph1=1, Ph2=2 (m³/s), Ph3=39, Ph4=40 (m³/h → ÷3600)
ALUM dose 89/92/151/154  factor 5.4135
PACL dose 8402/8404/8416/8418  factor 8.6434
```

- ถังร่วม: Ph1-2 → ถัง A/B, Ph3-4 → ถัง C/D อัตรารวมของกลุ่ม ÷ จำนวนถังที่ ON ในกลุ่ม
- ลำดับอัตราใช้ (ถัง ON): **อัตราจริง (>0) → เฉลี่ยประวัติ API → `defaultUsagePerHour`**
- dose ไม่มี/ว่าง = เฟสนั้นไม่จ่าย (0); flow ไม่มี = ไม่ทราบ; กลุ่มรวม = 0 → fallback
- API ล่ม: ใช้อัตรารอบล่าสุดได้ไม่เกิน 6 ชม. (`MaxDoseAgeHours`)
- UI แสดงที่มาผ่าน `RateSource` เดิม เช่น `อัตราจ่ายจริง Ph1+2 2.25 m³/ชม. ÷ 1 ถัง (flow×dose 12:00)`

**ไฟล์ที่แก้:** `Config.cs` (`TankConfig.Phases`, `PhaseFlowConfig`, `DosingConfig`), `TankData.cs` (`DoseRates`, `ChemApi.ReadDoseRates`, `Predictor.Predict(... measured)`), `SidecarController.cs` (`Dose`, `TankViews`), `config.json` (`phaseFlow`, `dosing`, `phases` ทุกถัง), `mock/sim.ps1` (ตัวสร้าง mock config)

---

## 2. ข้อสังเกต / งานค้าง

- **ค่าจาก API ณ 2026-10-03 ~13:30:** ALUM Ph1+2 = 0 (dose 89/92 ยังไม่มีใน API → ใช้วิธีเดิม), ALUM Ph3+4 ≈ 0.71 (151 ไม่มี), PACL Ph1+2 ≈ 2.25, PACL Ph3+4 ≈ 1.12 (8418 `value_text:""`)
- ระดับ PACL A ช่วง 30/09 ลดลงจริงเฉลี่ยแค่ ~1.08 m³/ชม. (ต่างจาก 2.25 ราว 2 เท่า) แต่ผู้ใช้ยืนยันว่าการคำนวณถูกต้องแล้ว ถ้าภายหลังพบว่าคาดการณ์คลาด ให้ปรับ `dosing.*.factor` ใน config (ไม่ต้องแก้โค้ด)
- ถ้า API เพิ่ม configID อัตราจ่ายตรง (87, 90, …) ภายหลัง ยังไม่มีโค้ดอ่าน — ต้องเพิ่ม (ระวังหน่วย L/min ของ 149/152)
- ไฟล์ `TruckSidecar\mock\run\config.json` ยังเป็นของเก่า (สร้างใหม่ตอนรัน `sim.ps1` setup)
- ไม่ได้แตะ `_backup_v1\`, `bin\`, ไฟล์อื่นใน `Sidecar_prj_deploy\` (Launcher/Watchdog/Uroboros_mini ไม่ได้เปลี่ยน)
- **ข้อจำกัด session นี้:** ระบบสิทธิ์ไม่ให้ agent เขียนลง `Sidecar_prj_deploy\` — ผู้ใช้คัดลอกเองด้วย PowerShell

## 3. ตรวจผลที่เครื่องใช้งาน (ถัดไป)

1. เปิดกราฟถัง PACL A (ON) → ต้องเห็นข้อความ "อัตราจ่ายจริง Ph1+2 …"
2. Alum A/B → ยังเห็น "เฉลี่ยจาก API …" หรือ "ค่าเริ่มต้นใน config" (ไม่มี dose)
3. เทียบระดับคาดการณ์กับค่า API รอบถัดไป 2–3 รอบ

## 4. Build / Publish

- Publish: `"C:\Program Files\dotnet\dotnet.exe" publish -c Release -o publish` ใน `TruckSidecar\` (single-file win-x86)
- คัดลอก `TruckSidecar.exe`, `.pdb`, `config.json` → `Sidecar_prj_deploy\TruckSidecar\` (ห้ามทับ `data\`) หรือใช้ `Sidecar_prj\stage.ps1` / `deploy.ps1`
- ตรวจ API: `curl -s http://172.16.198.150:443/api/chem/summary` (โครงสร้าง `Aq[id] = {ts, value, value_text, updated_at}`, ค่าราย 1–2 ชม.)

## 5. Suggested skills

- `scrutinize` / `code-review` — ถ้าจะรีวิวการเปลี่ยนแปลงใน `TankData.cs` / `SidecarController.cs`
- `run` — เปิด sidecar (`--preview` หรือ `mock\start-mock.ps1`) เพื่อดู RateSource บนกราฟ
- `dataviz` — ถ้าจะปรับการแสดงผลอัตราใช้บน `Ui\TankBarChart.cs`
