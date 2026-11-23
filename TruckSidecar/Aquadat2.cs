using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TruckSidecar;

/// <summary>
/// Aquadat2 (https://aquadat2.mwa.co.th) — เซสชันที่ login แล้ว: HttpClient (cookie jar) + Bearer JWT + refresh token
/// พอร์ตจาก Uroboros (branch Ruby) Aquadat2Session — token อยู่ในหน่วยความจำเท่านั้น ไม่บันทึกลงดิสก์
/// </summary>
public sealed class Aquadat2Session : IDisposable
{
    public const string ApiBackend = "/apibackend";   // VITE_API_AQDGEN (transactions)

    public HttpClient Http { get; }
    public string BaseUrl { get; }
    public string User { get; }
    public string AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }

    readonly SemaphoreSlim _refreshLock = new(1, 1);

    internal Aquadat2Session(HttpClient http, string baseUrl, string user, string access, string? refresh)
    {
        Http = http; BaseUrl = baseUrl; User = user; AccessToken = access; RefreshToken = refresh;
        ApplyBearer(access);
    }

    void ApplyBearer(string token) => Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>
    /// POST JSON → (status, body) — ถ้าเจอ HTTP 401 หรือ envelope messageCode 401/402/407
    /// จะ refresh token 1 ครั้งแล้วลองใหม่ ไม่ throw เมื่อ non-2xx (ผู้เรียกตรวจเอง)
    /// </summary>
    public async Task<(HttpStatusCode Status, string Body)> PostJsonAsync(string prefix, string route, string json, CancellationToken ct = default)
    {
        var (st, body) = await PostOnceAsync(prefix, route, json, ct).ConfigureAwait(false);
        if (NeedsRefresh(st, body) && await TryRefreshAsync(ct).ConfigureAwait(false))
            (st, body) = await PostOnceAsync(prefix, route, json, ct).ConfigureAwait(false);
        return (st, body);
    }

    async Task<(HttpStatusCode, string)> PostOnceAsync(string prefix, string route, string json, CancellationToken ct)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync($"{BaseUrl}{prefix}{route}", content, ct).ConfigureAwait(false);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    internal static bool NeedsRefresh(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.Unauthorized) return true;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("messageCode", out var mc) && mc.ValueKind == JsonValueKind.Number &&
                   mc.GetInt32() is 401 or 402 or 407;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>POST /api/Authen/verify-refresh-token {token: refreshToken} → data.{token, refreshToken}</summary>
    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(RefreshToken)) return false;
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { token = RefreshToken }), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync($"{BaseUrl}/api/Authen/verify-refresh-token", content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            var holder = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;
            if (!holder.TryGetProperty("token", out var t) || t.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(t.GetString()))
                return false;
            AccessToken = t.GetString()!;
            if (holder.TryGetProperty("refreshToken", out var rt) && rt.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rt.GetString()))
                RefreshToken = rt.GetString();
            ApplyBearer(AccessToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { return false; }
        finally { _refreshLock.Release(); }
    }

    public void Dispose() { Http.Dispose(); _refreshLock.Dispose(); }
}

/// <summary>
/// เข้าสู่ระบบ Aquadat2 ผ่าน AD/ADFS SAML ด้วย HTTP ล้วน (พอร์ตจาก Uroboros Aquadat2LoginProbe)
/// ผู้ใช้กรอก username/password เอง → ส่งให้ ADFS "ครั้งเดียว" — ไม่มีการวน/เดารหัสผ่าน
/// (aquadat2 ผูกกับ Active Directory: กรอกผิดนับรวมเป็นการล็อกบัญชี AD)
/// ขั้นตอน: GET /api/Authen/Saml/login (303→ADFS) → GET ฟอร์ม ADFS → POST user/pass
///         → หน้า auto-post SAMLResponse → POST /Saml2/Acs (200 + token ใน body)
///         → POST /api/Authen/Saml/loginbypass {token} → data.{token, refreshToken}
/// </summary>
public static class Aquadat2Login
{
    /// <summary>ADFS ต้องการ username แบบมีโดเมน (water.mwa\00102616) — เติมโดเมนให้ถ้าผู้ใช้กรอกแค่รหัสพนักงาน</summary>
    public static string QualifyUser(string user, string domain)
    {
        user = user.Trim();
        if (user.Length == 0 || string.IsNullOrWhiteSpace(domain) || user.Contains('\\') || user.Contains('@')) return user;
        return domain.Trim() + "\\" + user;
    }

    public static async Task<(Aquadat2Session? Session, string Message)> LoginAsync(
        string baseUrl, string domain, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (null, "กรุณากรอกชื่อผู้ใช้และรหัสผ่าน");
        baseUrl = baseUrl.TrimEnd('/');
        var user = QualifyUser(username, domain);

        var http = NewClient();
        try
        {
            // [1] SP → 303 ไป ADFS (+ cookie Saml2.<RelayState> ที่ ACS ต้องใช้)
            using var start = await http.GetAsync($"{baseUrl}/api/Authen/Saml/login", ct).ConfigureAwait(false);
            var adfsUrl = start.Headers.Location is { } loc ? AbsUrl(loc.ToString(), baseUrl) : null;
            if (start.StatusCode is not (HttpStatusCode.SeeOther or HttpStatusCode.Found) || adfsUrl == null)
                return Fail(http, $"เริ่มเข้าสู่ระบบไม่ได้ (HTTP {(int)start.StatusCode})");

            // [2] ฟอร์ม login ของ ADFS
            using var formResp = await http.GetAsync(adfsUrl, ct).ConfigureAwait(false);
            var formHtml = await formResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!TryParseLoginForm(formHtml, adfsUrl, out var postUrl, out var fields))
                return Fail(http, "ไม่พบหน้าเข้าสู่ระบบ AD (ADFS)");

            // [3] POST รหัสผ่าน (ครั้งเดียว) แล้วตาม redirect ของ ADFS เองจนถึงหน้า SAMLResponse
            fields["UserName"] = user;
            fields["Password"] = password;
            if (!fields.ContainsKey("AuthMethod")) fields["AuthMethod"] = "FormsAuthentication";
            string html;
            using (var form = new FormUrlEncodedContent(fields))
            {
                var cur = await http.PostAsync(postUrl, form, ct).ConfigureAwait(false);
                html = await cur.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                for (int guard = 0; cur.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther && cur.Headers.Location != null && guard < 5; guard++)
                {
                    var next = AbsUrl(cur.Headers.Location.ToString(), postUrl);
                    cur.Dispose();
                    cur = await http.GetAsync(next, ct).ConfigureAwait(false);
                    html = await cur.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
                cur.Dispose();
            }
            if (!TryParseSamlResponseForm(html, out var acsUrl, out var samlResponse, out var relayState))
            {
                var err = ExtractAdfsError(html);
                return Fail(http, "เข้าสู่ระบบไม่สำเร็จ: รหัสพนักงานหรือรหัสผ่าน AD ไม่ถูกต้อง" +
                                  (err != null ? $" (ADFS: {err})" : "") +
                                  "\nระวัง: กรอกผิดหลายครั้งบัญชี AD จะถูกล็อก");
            }

            // [4] ACS → 200 + token ครั้งเดียวฝังอยู่ใน body (ไม่ใช่ redirect)
            var acsFields = new Dictionary<string, string> { ["SAMLResponse"] = samlResponse };
            if (!string.IsNullOrEmpty(relayState)) acsFields["RelayState"] = relayState;
            string? oneTime;
            using (var acsForm = new FormUrlEncodedContent(acsFields))
            using (var acs = await http.PostAsync(AbsUrl(acsUrl, baseUrl), acsForm, ct).ConfigureAwait(false))
            {
                var acsBody = await acs.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                oneTime = ExtractQueryParam(acs.Headers.Location?.ToString(), "token") ?? ExtractTokenFromBody(acsBody);
            }
            if (string.IsNullOrWhiteSpace(oneTime)) return Fail(http, "Aquadat2 ไม่ส่ง token กลับมา (ACS)");

            // [5] แลก token ครั้งเดียว → access JWT + refresh token
            using var bypassBody = new StringContent(JsonSerializer.Serialize(new { token = oneTime }), Encoding.UTF8, "application/json");
            using var bypass = await http.PostAsync($"{baseUrl}/api/Authen/Saml/loginbypass", bypassBody, ct).ConfigureAwait(false);
            var (access, refresh) = ReadLoginResult(await bypass.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (string.IsNullOrWhiteSpace(access)) return Fail(http, $"Aquadat2 ไม่ออก token (HTTP {(int)bypass.StatusCode})");

            return (new Aquadat2Session(http, baseUrl, user, access, refresh), "เข้าสู่ระบบสำเร็จ");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            http.Dispose();
            return (null, "ติดต่อ Aquadat2 ไม่ได้: " + ex.Message);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    static (Aquadat2Session?, string) Fail(HttpClient http, string msg) { http.Dispose(); return (null, msg); }

    static HttpClient NewClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,          // ทุก hop ต้องเก็บ cookie เอง/อ่าน Location เอง
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TruckSidecar-aq2/1.0");
        return http;
    }

    static string AbsUrl(string url, string baseAbsolute) =>
        Uri.TryCreate(url, UriKind.Absolute, out var abs) ? abs.ToString() : new Uri(new Uri(baseAbsolute), url).ToString();

    // ───────── form parsing (regex เหมือน Uroboros) ─────────
    static readonly Regex RxFormLogin = new(@"<form\b[^>]*\bid=[""']?loginForm[""']?[^>]*>", RegexOptions.IgnoreCase);
    static readonly Regex RxAction = new(@"\baction=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
    static readonly Regex RxInput = new(@"<input\b[^>]*>", RegexOptions.IgnoreCase);
    static readonly Regex RxAttr = new(@"\b(name|value|type)=[""']([^""']*)[""']", RegexOptions.IgnoreCase);
    static readonly Regex RxSamlResp = new(@"name=[""']SAMLResponse[""'][^>]*\bvalue=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
    static readonly Regex RxRelay = new(@"name=[""']RelayState[""'][^>]*\bvalue=[""']([^""']*)[""']", RegexOptions.IgnoreCase);
    static readonly Regex RxAcsAction = new(@"<form\b[^>]*\baction=[""']([^""']*Acs[^""']*)[""']", RegexOptions.IgnoreCase);
    static readonly Regex RxErrorText = new(@"id=[""']errorText[""'][^>]*>([^<]*)<", RegexOptions.IgnoreCase);
    static readonly Regex RxBypassToken = new(@"adfs_bypasstoken[^""'<>\s]*[?&]token=([^""'&<>\s\\]+)", RegexOptions.IgnoreCase);
    static readonly Regex RxAnyToken = new(@"[?&]token=([^""'&<>\s\\]+)", RegexOptions.IgnoreCase);

    internal static bool TryParseLoginForm(string html, string pageUrl, out string postUrl, out Dictionary<string, string> fields)
    {
        postUrl = "";
        fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var m = RxFormLogin.Match(html);
        if (!m.Success) return false;
        var am = RxAction.Match(m.Value);
        postUrl = AbsUrl(am.Success ? WebUtility.HtmlDecode(am.Groups[1].Value) : pageUrl, pageUrl);
        foreach (Match im in RxInput.Matches(html))
        {
            string? name = null, value = null, type = null;
            foreach (Match a in RxAttr.Matches(im.Value))
            {
                var v = WebUtility.HtmlDecode(a.Groups[2].Value);
                switch (a.Groups[1].Value.ToLowerInvariant())
                {
                    case "name": name = v; break;
                    case "value": value = v; break;
                    case "type": type = v; break;
                }
            }
            if (string.IsNullOrEmpty(name) || string.Equals(type, "submit", StringComparison.OrdinalIgnoreCase)) continue;
            fields.TryAdd(name, value ?? "");
        }
        return true;
    }

    internal static bool TryParseSamlResponseForm(string html, out string acsUrl, out string samlResponse, out string relayState)
    {
        acsUrl = samlResponse = relayState = "";
        var sm = RxSamlResp.Match(html);
        if (!sm.Success) return false;
        samlResponse = WebUtility.HtmlDecode(sm.Groups[1].Value);
        var rm = RxRelay.Match(html);
        relayState = rm.Success ? WebUtility.HtmlDecode(rm.Groups[1].Value) : "";
        var am = RxAcsAction.Match(html);
        acsUrl = am.Success ? WebUtility.HtmlDecode(am.Groups[1].Value) : "/Saml2/Acs";
        return true;
    }

    static string? ExtractAdfsError(string html)
    {
        var m = RxErrorText.Match(html);
        var s = m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";
        return s.Length > 0 ? s : null;
    }

    internal static string? ExtractTokenFromBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var m = RxBypassToken.Match(body);
        if (!m.Success) m = RxAnyToken.Match(body);
        return m.Success ? WebUtility.HtmlDecode(Uri.UnescapeDataString(m.Groups[1].Value)) : null;
    }

    static string? ExtractQueryParam(string? url, string key)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        int q = url.IndexOf('?');
        if (q < 0) return null;
        foreach (var kv in url[(q + 1)..].Split('&'))
        {
            var p = kv.Split('=', 2);
            if (p.Length == 2 && Uri.UnescapeDataString(p[0]) == key) return Uri.UnescapeDataString(p[1]);
        }
        return null;
    }

    static (string Access, string? Refresh) ReadLoginResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return ("", null);
            string? Get(string n) => data.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Get("token") ?? "", Get("refreshToken"));
        }
        catch (JsonException) { return ("", null); }
    }
}

/// <summary>
/// ส่งยอดเข้า Aquadat2: REST POST /apibackend/api/Trans/PTransJsonUpdate (แทน GraphQL update_transaction_datavalues)
/// ชุดฟิลด์ต่อรายการเหมือนเดิม (date, time, value, configparam_id, trans_type, flag_value)
/// ต่างกันที่: ห่อด้วย {transUpdateItems:[…], apiPlantId:0} และ date เป็น ISO "yyyy-MM-ddTHH:mm:ss"
/// </summary>
public static class Aquadat2Client
{
    public const string Route = "/api/Trans/PTransJsonUpdate";

    public static string BuildPayload(IReadOnlyList<OutboxRow> rows)
    {
        var items = rows.Select(r => new Dictionary<string, object>
        {
            // วันตัดยอด (yyyy-MM-dd) → ISO; ช่องเวลาจริงอยู่ที่ "time" (ยอดตัดรายวัน = "24:00")
            ["date"] = r.Date + "T00:00:00",
            ["time"] = r.Time,
            ["value"] = r.Value.ToString(CultureInfo.InvariantCulture),   // STRING
            ["configparam_id"] = r.ConfigId,                              // NUMBER
            ["trans_type"] = r.TransType,
            ["flag_value"] = r.FlagValue,
        }).ToList();
        return JsonSerializer.Serialize(new { transUpdateItems = items, apiPlantId = 0 },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>ส่งแล้วคืน body ของเซิร์ฟเวอร์ — throw ถ้าไม่ success==true หรือ data.statusCode != "C"</summary>
    public static async Task<string> PushAsync(Aquadat2Session session, IReadOnlyList<OutboxRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return "";
        var (status, body) = await session.PostJsonAsync(Aquadat2Session.ApiBackend, Route, BuildPayload(rows), ct).ConfigureAwait(false);
        if (status == HttpStatusCode.Unauthorized || Aquadat2Session.NeedsRefresh(status, body))
            throw new Aquadat2AuthException("token Aquadat2 หมดอายุ — กรุณาเข้าสู่ระบบใหม่");
        if ((int)status is < 200 or >= 300)
            throw new InvalidOperationException($"Aquadat2 HTTP {(int)status}: {Trim(body, 300)}");

        bool success = false;
        string? code = null, msg = null, topMsg = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) topMsg = m.GetString();
            if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                if (d.TryGetProperty("statusCode", out var sc) && sc.ValueKind == JsonValueKind.String) code = sc.GetString();
                if (d.TryGetProperty("statusMessage", out var sm) && sm.ValueKind == JsonValueKind.String) msg = sm.GetString();
            }
        }
        catch (JsonException ex) { throw new InvalidOperationException($"Aquadat2 ตอบกลับผิดรูปแบบ: {ex.Message}"); }

        if (!success || (code != null && !string.Equals(code, "C", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Aquadat2 ไม่รับยอด: {msg ?? topMsg ?? "unknown"} (statusCode={code})");
        return body;
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

/// <summary>token ใช้ไม่ได้แล้ว (refresh ไม่สำเร็จ) → UI ต้องให้ผู้ใช้ login ใหม่</summary>
public sealed class Aquadat2AuthException(string message) : Exception(message);
