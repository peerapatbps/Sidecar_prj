namespace TruckSidecar;

/// <summary>
/// ทดสอบ Aquadat2 ครบวง (login ADFS SAML → PTransJsonUpdate) กับเซิร์ฟเวอร์จำลองในเครื่องเท่านั้น
/// (mock\aquadat-stub.ps1) — ปฏิเสธ host ที่ไม่ใช่ localhost กันส่งยอดทดสอบเข้า PROD โดยไม่ตั้งใจ
/// </summary>
public static class Aq2StubTest
{
    public static async Task Run(AppConfig cfg, string baseUrl, string user, string pass, string outPath)
    {
        var sb = new System.Text.StringBuilder();
        void W(string s) => sb.AppendLine(s);
        try
        {
            var host = new Uri(baseUrl).Host;
            if (host is not ("localhost" or "127.0.0.1"))
            {
                W($"ปฏิเสธ: {host} ไม่ใช่ localhost — คำสั่งนี้ใช้กับ stub เท่านั้น");
                return;
            }
            W($"qualify '{user}' → '{Aquadat2Login.QualifyUser(user, cfg.Aquadat2Domain)}'");

            var (bad, badMsg) = await Aquadat2Login.LoginAsync(baseUrl, cfg.Aquadat2Domain, user, "wrong-password");
            W($"login (รหัสผิด): ok={bad != null} msg={badMsg}");
            bad?.Dispose();

            var (s, msg) = await Aquadat2Login.LoginAsync(baseUrl, cfg.Aquadat2Domain, user, pass);
            W($"login: ok={s != null} msg={msg}");
            if (s == null) return;
            using (s)
            {
                W($"token len={s.AccessToken.Length} refresh={(s.RefreshToken != null ? "yes" : "no")}");
                var rows = new List<OutboxRow>
                {
                    new() { Date = "2026-11-23", Time = "24:00", ConfigId = 8424, Value = 14.2 },
                    new() { Date = "2026-11-23", Time = "24:00", ConfigId = 8037, Value = 19 },
                };
                W("payload: " + Aquadat2Client.BuildPayload(rows));
                W("push: " + await Aquadat2Client.PushAsync(s, rows));

                // stub คืน 401 ครั้งแรกเมื่อ configparam_id = 401 → ต้อง refresh แล้วสำเร็จในรอบสอง
                var r401 = new List<OutboxRow> { new() { Date = "2026-11-23", Time = "24:00", ConfigId = 401, Value = 1 } };
                W("push (401 → refresh): " + await Aquadat2Client.PushAsync(s, r401));

                // stub ตอบ success=false เมื่อ configparam_id = 999 → ต้อง throw
                try { await Aquadat2Client.PushAsync(s, new List<OutboxRow> { new() { Date = "2026-11-23", Time = "24:00", ConfigId = 999, Value = 1 } }); W("push (reject): ไม่ throw ✗"); }
                catch (InvalidOperationException ex) { W("push (reject): throw ✓ " + ex.Message); }
            }
        }
        catch (Exception ex) { W("ERROR " + ex); }
        finally
        {
            File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);
        }
    }
}
