using System.Net;
using System.Text;
using System.Text.Json;

namespace TruckSidecar;

public sealed record LoginResult(bool Ok, string Token, string Message);

/// <summary>
/// เข้าสู่ระบบ Aquadat ด้วย username/password ที่ "ผู้ใช้กรอกเอง" ในหน้าจอ sidecar
/// เป็นการ POST ครั้งเดียวไปยัง endpoint Enroll แล้วรับ token กลับ — ไม่มีการเดา/วนรหัสผ่าน
/// รหัสผ่านถูกเก็บไว้ในหน่วยความจำระหว่างใช้งานเท่านั้น ไม่บันทึกลงดิสก์
/// </summary>
public static class AquadatLogin
{
    static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    { Timeout = TimeSpan.FromSeconds(60) };

    public static async Task<LoginResult> LoginAsync(string enrollUrl, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new LoginResult(false, "", "กรุณากรอกชื่อผู้ใช้และรหัสผ่าน");

        var body = JsonSerializer.Serialize(new { username, password });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await Http.PostAsync(enrollUrl, content, ct).ConfigureAwait(false);
            var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(txt);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number && st.GetInt32() == 200 &&
                root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Object &&
                results.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(tok.GetString()))
            {
                return new LoginResult(true, tok.GetString()!, "เข้าสู่ระบบสำเร็จ");
            }

            // ข้อความจากเซิร์ฟเวอร์ (เช่น รหัสผ่านไม่ถูกต้อง/หมดอายุ)
            string msg = "เข้าสู่ระบบไม่สำเร็จ";
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                msg += ": " + m.GetString();
            return new LoginResult(false, "", msg);
        }
        catch (Exception ex)
        {
            return new LoginResult(false, "", "ติดต่อเซิร์ฟเวอร์ไม่ได้: " + ex.Message);
        }
    }
}
