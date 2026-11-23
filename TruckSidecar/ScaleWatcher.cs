namespace TruckSidecar;

/// <summary>
/// เฝ้า data.mdb: ทุก watchSeconds ดูเวลาแก้ไข+ขนาดไฟล์ ถ้าเปลี่ยน → query (DetectAsync)
/// และ query บังคับทุก forceCheckSeconds เผื่อ NTFS รายงานเวลาไฟล์ช้าขณะโปรแกรมชั่งเปิดไฟล์ค้าง
/// อ่านสำเนาเท่านั้น (ผ่าน ScaleDb) — ไม่แตะไฟล์ต้นฉบับ
/// </summary>
public sealed class ScaleWatcher(string mdbPath, int forceSeconds, int inWindowHours = 24)
{
    (DateTime, long)? _stamp;
    DateTime _lastQuery = DateTime.MinValue;

    public DateTime? LastOk { get; private set; }
    public string? LastError { get; private set; }
    public bool Enabled => !string.IsNullOrEmpty(mdbPath);

    /// <summary>null = ไม่มีอะไรเปลี่ยน (หรืออ่านไม่ได้ชั่วคราว ลองใหม่รอบหน้า)</summary>
    public async Task<DetectResult?> CheckAsync()
    {
        if (!Enabled) return null;
        var now = DateTime.Now;
        (DateTime, long) stamp;
        try
        {
            var fi = new FileInfo(mdbPath);
            if (!fi.Exists) { LastError = "ไม่พบไฟล์ " + mdbPath; return null; }
            stamp = (fi.LastWriteTimeUtc, fi.Length);
        }
        catch (Exception ex) { LastError = ex.Message; return null; }

        bool changed = _stamp != stamp;
        bool due = (now - _lastQuery).TotalSeconds >= Math.Max(5, forceSeconds);
        if (!changed && !due) return null;

        _lastQuery = now;
        try
        {
            var res = await ScaleDb.DetectAsync(mdbPath, inWindowHours);
            _stamp = stamp;          // จำ stamp เฉพาะเมื่ออ่านสำเร็จ (ไฟล์เขียนไม่เสร็จ → ลองใหม่ tick ถัดไป)
            LastOk = DateTime.Now;
            LastError = null;
            return res;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }
}
