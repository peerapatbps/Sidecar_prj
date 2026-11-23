namespace TruckSidecar;

/// <summary>
/// ทดสอบ RecordStore (SQLite) โดยไม่เปิดหน้าจอ: TruckSidecar.exe --storetest &lt;dataDir&gt; &lt;out.txt&gt;
///   1) เปิด store บน dataDir → นำเข้า records\*.json เดิม (ถ้ามี) แล้วย้ายโฟลเดอร์ไป records_migrated_*
///   2) แก้ record แรก 1 ครั้ง (Version +1) แล้วเปิด store ใหม่ → ต้องอ่านค่าที่แก้กลับมาได้ครบ
/// ใช้ dataDir ทดสอบเท่านั้น — อย่าชี้ไปที่ data\ ของเครื่องใช้งาน
/// </summary>
static class StoreSelfTest
{
    public static void Run(string dataDir, string outPath)
    {
        var lines = new List<string>();
        void W(string s) => lines.Add(s);
        try
        {
            var s1 = new RecordStore(dataDir);
            W($"[1] open: records={s1.All.Count()} bad={s1.BadFiles.Count} {string.Join(" | ", s1.BadFiles)}");
            foreach (var r in s1.All.OrderBy(r => r.WeighInAt))
                W($"    {r.Id} {r.TrackId} {r.Plate} {r.Chemical} v{r.Version} complete={r.IsComplete} fills={string.Join(",", r.Fills.Select(f => $"{f.Tank}:{f.LevelBefore}->{f.LevelAfter}@{f.AfterRecordedAt:HH:mm:ss}"))}");
            W($"    latestAfter: {string.Join(", ", s1.LatestAfterReadings().Select(kv => $"{kv.Key}={kv.Value.Level}@{kv.Value.At:HH:mm:ss}"))}");

            var first = s1.All.OrderBy(r => r.Id).FirstOrDefault();
            if (first != null)
            {
                int v = first.Version;
                first.BillNo = first.BillNo + "-T";
                s1.Save(first, "storetest");
                s1.Audit("storetest", new { id = first.Id });

                var s2 = new RecordStore(dataDir);
                var back = s2.All.First(r => r.Id == first.Id);
                bool ok = back.Version == v + 1 && back.BillNo == first.BillNo && back.Fills.Count == first.Fills.Count
                          && s2.All.Count() == s1.All.Count();
                W($"[2] save+reopen: v{v}->v{back.Version} billNo={back.BillNo} fills={back.Fills.Count} total={s2.All.Count()} => {(ok ? "PASS" : "FAIL")}");
            }
            W("DONE");
        }
        catch (Exception ex)
        {
            W("FAILED: " + ex);
        }
        File.WriteAllLines(outPath, lines);
    }
}
