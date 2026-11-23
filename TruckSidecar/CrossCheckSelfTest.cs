namespace TruckSidecar;

/// <summary>
/// ทดสอบ crosscheck กฎข้อ 5 โดยตรง (ไม่ผ่าน UI ซึ่งกันกรณีเหล่านี้ไว้ก่อนแล้ว) — รันด้วย
///   TruckSidecar.exe --cctest &lt;out.txt&gt; &lt;TICKET1 สะอาด (ALUM ยังไม่มีบันทึก)&gt;
/// ใช้ data.mdb ตาม config (ควรเป็น mock.mdb) + ที่เก็บ record ชั่วคราว (ไม่แตะ data จริง)
/// ต้องมีใน mdb: 90001 ชั่งออกด้วย TICKET2 80001 และ 90012 ชั่งออกด้วย TICKET2 80001 เหมือนกัน
/// </summary>
public static class CrossCheckSelfTest
{
    public static void Run(AppConfig cfg, string? outPath, string cleanTicket)
    {
        var sb = new System.Text.StringBuilder();
        int pass = 0, fail = 0;
        void W(string s) => sb.AppendLine(s);

        var tmp = Path.Combine(Path.GetTempPath(), "TruckSidecar_cctest_" + Guid.NewGuid().ToString("N")[..8]);
        cfg.DataDir = tmp;
        try
        {
            var c = new SidecarController(cfg, tmp);
            string T(string n) => n.PadLeft(10, '0');
            var rows = ScaleDb.ByTicket1Async(cfg.MdbPath, new[] { T("90001"), T("90012"), T(cleanTicket) }).GetAwaiter().GetResult();
            W($"mdb: {cfg.MdbPath}  (พบ {rows.Count}/3 แถว)");

            UnloadRecord FromRow(string t, string chem, params string[] tanks)
            {
                var r = new UnloadRecord { Chemical = chem, Sg = 1.3, TempC = 30, BillBook = "1", BillNo = "1" };
                r.ApplyScale(rows[T(t)]);
                r.Fills = tanks.Select(k => new FillEntry { Tank = k }).ToList();
                return r;
            }

            void Case(string id, string title, UnloadRecord r, string? expectBlock)
            {
                var res = c.CrossCheckAsync(r).GetAwaiter().GetResult();
                bool ok = expectBlock == null
                    ? res.Blocks.Count == 0
                    : res.Blocks.Any(b => b.Contains(expectBlock));
                if (ok) pass++; else fail++;
                W($"{(ok ? "PASS" : "FAIL")}  {id}  {title}");
                W($"      คาดหวัง: {(expectBlock == null ? "ไม่ถูก block" : $"block มีคำว่า \"{expectBlock}\"")}");
                W($"      ได้: blocks=[{string.Join(" | ", res.Blocks)}]  warnings=[{string.Join(" | ", res.Warnings)}]" +
                  (res.MdbError != null ? $"  mdbError={res.MdbError}" : ""));
            }

            // record A = #90001 (PACL, ตั๋วออก 80001) บันทึกไว้แล้ว
            var a = FromRow("90001", "PACL", "PACL C");
            c.Store.Save(a);
            W($"setup: บันทึก A = #{a.TrackId.TrimStart('0')} {a.Plate} TICKET2={a.Ticket2.TrimStart('0')}");
            W("");

            // ── C3: TICKET1 ซ้ำ ──
            Case("C3-1", "record ใหม่ (Id ต่าง) ใช้ TICKET1 90001 ซ้ำกับ A", FromRow("90001", "PACL", "PACL C"), "ถูกบันทึกแล้ว");
            Case("C3-2", "บันทึก A ซ้ำ (แก้ไข record เดิม Id เดียวกัน) → ต้องไม่ถูก block", a.Clone(), null);

            // ── C4: TICKET2 ซ้ำ (ทวนซ้ำให้ครบชุด) ──
            Case("C4-1", "record #90012 ซึ่ง mdb ชั่งออกด้วย TICKET2 80001 (ซ้ำกับ A)", FromRow("90012", "PACL", "PACL B"), "ผูกกับ Track ID 90001");

            // ── C5: ถังคนละสารเคมี ──
            Case("C5-1", $"รถ ALUM #{cleanTicket} เลือกถัง PACL C", FromRow(cleanTicket, "ALUM", "PACL C"), "เลือกถัง PACL C");
            Case("C5-2", $"รถ ALUM #{cleanTicket} เลือก Alum A + PACL D (ปน)", FromRow(cleanTicket, "ALUM", "Alum A", "PACL D"), "เลือกถัง PACL D");
            Case("C5-3", $"รถ ALUM #{cleanTicket} เลือก Alum A + Alum B → ต้องไม่ถูก block", FromRow(cleanTicket, "ALUM", "Alum A", "Alum B"), null);
            Case("C5-4", $"record v1 (ไม่มี Chemical) #{cleanTicket} ถัง PACL C → ข้าม (UI เติม Chemical ให้ก่อนเสมอ)", FromRow(cleanTicket, "", "PACL C"), null);

            W("");
            W($"สรุป: PASS {pass} / FAIL {fail}");
        }
        catch (Exception ex)
        {
            fail++;
            W("ERROR: " + ex);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }

        var text = sb.ToString();
        if (outPath != null) File.WriteAllText(outPath, text);
        Environment.ExitCode = fail == 0 ? 0 : 1;
    }
}
