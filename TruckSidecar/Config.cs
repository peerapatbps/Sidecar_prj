using System.Text.Json;

namespace TruckSidecar;

public sealed class TankConfig
{
    public string Name { get; set; } = "";
    public string Chemical { get; set; } = "";
    public int LevelId { get; set; }
    public int StatusId { get; set; }
    public double Capacity { get; set; }
    public int IncomeConfigId { get; set; }
    /// <summary>เฟสที่ถังนี้จ่ายสารให้ (ถังที่ phases เหมือนกัน = ใช้ถังร่วมกัน) ว่าง = ไม่ใช้อัตราจ่ายจริง</summary>
    public List<int> Phases { get; set; } = new();
}

/// <summary>อัตราน้ำดิบต่อเฟส: ค่า Aq[id] ÷ divisor → m³/วินาที (Ph1-2 เป็น m³/s อยู่แล้ว, Ph3-4 เป็น m³/h)</summary>
public sealed class PhaseFlowConfig
{
    public int Phase { get; set; }
    public int Id { get; set; }
    public double Divisor { get; set; } = 1;
}

/// <summary>L/ชม. = flow × dose(mg/L) × factor — doseIds: เฟส → configID ของ dose</summary>
public sealed class DosingConfig
{
    public double Factor { get; set; }
    public Dictionary<int, int> DoseIds { get; set; } = new();
}

public sealed class AppConfig
{
    public string TargetProcess { get; set; } = "truck93a";
    public string MdbPath { get; set; } = "";
    public int ScalePollSeconds { get; set; } = 30;   // v1 (ไม่ใช้แล้ว) — คงไว้ให้ config เก่าอ่านได้
    /// <summary>ตรวจเวลาแก้ไขของ data.mdb ทุกกี่วินาที (เปลี่ยน → query)</summary>
    public int WatchSeconds { get; set; } = 3;
    /// <summary>query บังคับแม้เวลาไฟล์ไม่เปลี่ยน (NTFS อาจรายงาน mtime ช้าขณะ VB6 เปิดไฟล์ค้าง)</summary>
    public int ForceCheckSeconds { get; set; } = 30;
    /// <summary>กฎข้อ 2: ตรวจรถ STAT 1 ที่ชั่งเข้าภายในกี่ชั่วโมง (รวมรถที่ชั่งเข้าก่อนเที่ยงคืน)</summary>
    public int InWindowHours { get; set; } = 24;
    /// <summary>งานคงค้าง: ดูรถที่ชั่งเข้าย้อนหลังกี่วัน (ไม่มีบันทึก/บันทึกไม่ครบ) + สรุปตัดยอดย้อนได้กี่วัน</summary>
    public int PendingLookbackDays { get; set; } = 7;
    /// <summary>WDATA.PRODUCT → สารเคมี (ต้องตรงกับ tanks[].chemical) รหัสอื่น เช่น 02 ทั่วไป = ไม่สนใจ</summary>
    public Dictionary<string, string> ProductMap { get; set; } = new() { ["01"] = "ALUM", ["03"] = "PACL" };
    /// <summary>พื้นหลังกระจก: "blur" (Win10 ลื่นสุด) / "acrylic" / "off" (ทึบ ไม่เบลอ)</summary>
    public string Glass { get; set; } = "blur";
    public string ApiUrl { get; set; } = "";
    public int ApiPollSeconds { get; set; } = 300;
    public string CentralApiUrl { get; set; } = "";
    public string AquadatUrl { get; set; } = "https://aquadat.mwa.co.th/graphql";
    public string AquadatEnrollUrl { get; set; } = "http://aquadat.mwa.co.th:12007/api/aquaDATService/Enroll";
    public string AquadatUsername { get; set; } = "";
    public int AquadatPushSeconds { get; set; } = 120;
    public string DataDir { get; set; } = "data";
    public int SidecarWidth { get; set; } = 400;
    /// <summary>รถชั่งเข้า (W1) / ชั่งออก (W2) → ดึง sidecar ขึ้นมา active แล้ววาง cursor ที่ช่องถัดไป (false = แค่แจ้ง toast)</summary>
    public bool ActivateOnScale { get; set; } = true;
    /// <summary>ดึงขึ้นมาเมื่อพนักงานหยุดพิมพ์/คลิกนานเท่านี้ (มิลลิวินาที) — กันตัวเลขที่กำลังพิมพ์ในโปรแกรมชั่งตกมาที่ sidecar</summary>
    public int ActivateIdleMs { get; set; } = 1500;
    /// <summary>รอจังหวะดึงขึ้นมาได้นานสุดกี่วินาที (เช่น หน้าต่างพิมพ์ตั๋วของโปรแกรมชั่งยังเปิด) เกินแล้วเลิก เหลือแค่ toast</summary>
    public int ActivateTimeoutSeconds { get; set; } = 60;
    public double LowLevelPercent { get; set; } = 20;
    /// <summary>อัตราใช้เริ่มต้น (m³/ชม.) ต่อถังที่ ON แยกตามสารเคมี — ใช้เมื่อประวัติ API ยังไม่พอคำนวณ</summary>
    public Dictionary<string, double> DefaultUsagePerHour { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>หน้าต่างเวลาย้อนหลัง (ชม.) ที่ใช้เฉลี่ยอัตราใช้จากประวัติ API</summary>
    public int RateWindowHours { get; set; } = 24;
    /// <summary>อัตราน้ำดิบแต่ละเฟส ใช้คำนวณอัตราจ่ายสารจริง</summary>
    public List<PhaseFlowConfig> PhaseFlow { get; set; } = new();
    /// <summary>สารเคมี → factor + configID ของ dose ต่อเฟส</summary>
    public Dictionary<string, DosingConfig> Dosing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TankConfig> Tanks { get; set; } = new();

    public string? ChemicalOf(string product) =>
        ProductMap.TryGetValue(product.Trim(), out var c) ? c : null;

    public static AppConfig Load(string path)
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), opts)
               ?? throw new InvalidDataException("config.json ว่างเปล่า");
    }
}
