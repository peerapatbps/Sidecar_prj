using System.Globalization;

namespace TruckSidecar;

/// <summary>ตรวจ interpolate + รูปแบบ payload โดยไม่ส่งขึ้น Aquadat (รันด้วย --aqtest)</summary>
public static class AqSelfTest
{
    public static void Run(AppConfig cfg, string? outPath)
    {
        var sb = new System.Text.StringBuilder();
        void W(string s) => sb.AppendLine(s);

        void Case(string title, UnloadRecord r)
        {
            W("== " + title);
            W($"   ชั่งเข้า {r.W1:#,0} - ชั่งออก {r.W2:#,0} = สุทธิ {r.NetKg:#,0} กก. ({r.NetKg / 1000.0:0.###} ตัน)");
            var allocs = Interpolation.Allocate(r, cfg.Tanks);
            foreach (var a in allocs)
                W($"   {a.Tank}  vol={a.FillVolumeM3} m³  → cfg {a.ConfigId}  {a.Tonnes} ตัน");
            var byCfg = Interpolation.ByConfig(allocs);
            var rows = byCfg.Select(kv => new OutboxRow
            {
                Date = "2026-09-25", Time = "24:00", ConfigId = kv.Key,
                Value = kv.Value, TransType = "", FlagValue = "0",
            }).ToList();
            double sum = byCfg.Values.Sum();
            W($"   รวมตามคอนฟิก: {string.Join(", ", byCfg.Select(kv => $"{kv.Key}={kv.Value}"))}  (รวม {sum:0.###} ตัน)");
            W("   payload: " + BuildPayload(rows));
            W("");
        }

        // เที่ยวเดียว 1 ถัง: PACL C, สุทธิ 14.2 ตัน → ต้องเข้า cfg 8424 ทั้งหมด
        Case("รถ 1 คัน → PACL C", new UnloadRecord
        {
            Plate = "53-4043", TrackId = "0000014806", W1 = 25010, W2 = 10810,
            Fills = { new FillEntry { Tank = "PACL C", LevelBefore = 11.9, LevelAfter = 24.1 } },
        });

        // เที่ยวเดียว 2 ถังคนละคอนฟิก: PACL A (3 m³) + PACL C (9 m³) → แบ่ง 25%/75%
        Case("รถ 1 คัน → PACL A + PACL C (แบ่งตามปริมาตร)", new UnloadRecord
        {
            Plate = "70-1111", TrackId = "0000014900", W1 = 24000, W2 = 12000,
            Fills =
            {
                new FillEntry { Tank = "PACL A", LevelBefore = 20, LevelAfter = 23 },
                new FillEntry { Tank = "PACL C", LevelBefore = 12, LevelAfter = 21 },
            },
        });

        // Alum 2 ถังคอนฟิกเดียวกัน (A+B = 8037)
        Case("รถ 1 คัน → Alum A + Alum B (คอนฟิกเดียวกัน 8037)", new UnloadRecord
        {
            Plate = "80-2222", TrackId = "0000014901", W1 = 31000, W2 = 12000,
            Fills =
            {
                new FillEntry { Tank = "Alum A", LevelBefore = 200, LevelAfter = 210 },
                new FillEntry { Tank = "Alum B", LevelBefore = 540, LevelAfter = 550 },
            },
        });

        var text = sb.ToString();
        if (outPath != null) File.WriteAllText(outPath, text, System.Text.Encoding.UTF8);
        Console.Out.Write(text);
    }

    static string BuildPayload(List<OutboxRow> rows)
    {
        var inputs = rows.Select(r =>
        {
            string v = r.Value.ToString(CultureInfo.InvariantCulture);
            return $"{{date: \"{r.Date}\", configparam_id: {r.ConfigId}, time: \"{r.Time}\", value: \"{v}\", trans_type: \"{r.TransType}\", flag_value: \"{r.FlagValue}\"}}";
        });
        string mutation = "mutation {\\n  update_transaction_datavalues(\\n    input_data: [" +
                          string.Join(", ", inputs) + "]\\n  ) {\\n    return_status\\n    return_message\\n    __typename\\n  }\\n}\\n";
        return System.Text.Json.JsonSerializer.Serialize(new { variables = new { }, query = mutation },
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
