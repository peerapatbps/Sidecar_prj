# เซิร์ฟเวอร์ Aquadat จำลอง — รองรับทั้ง aquadat เดิม (v1) และ aquadat2 (v2)
# รันในเทอร์มินอลของมันเอง แล้วปล่อยค้างไว้:  powershell -ExecutionPolicy Bypass -File mock\aquadat-stub.ps1
#   v1: Enroll → token ปลอม, GraphQL mutation → return_status:true
#   v2: จำลอง ADFS SAML ครบทุก hop (login → ADFS form → SAMLResponse → /Saml2/Acs → loginbypass)
#       + PTransJsonUpdate (configparam_id 401 = ตอบ 401 ครั้งแรกเพื่อทดสอบ refresh, 999 = success:false)
#       รหัสผ่าน "wrong-password" = ADFS ปฏิเสธ; ชื่อผู้ใช้ต้องมีโดเมน (domain\user) เหมือนของจริง
param([int]$Port = 18080)
Add-Type -AssemblyName System.Web

$log = Join-Path $PSScriptRoot 'received.log'
"" | Set-Content $log
$base = "http://localhost:$Port"
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("$base/")
$listener.Start()
Write-Host "Aquadat stub ฟังอยู่ที่ $base/  (Ctrl+C เพื่อหยุด)"
Write-Host "  v1 Enroll  : $base/api/aquaDATService/Enroll"
Write-Host "  v1 GraphQL : $base/graphql"
Write-Host "  v2 base    : $base   (config: aquadatApi=2, aquadat2BaseUrl=$base)"
Write-Host "payload ที่รับจะถูกบันทึกใน $log"

function Send($ctx, [int]$code, [string]$body, [string]$type = 'application/json') {
  $buf = [System.Text.Encoding]::UTF8.GetBytes($body)
  $ctx.Response.ContentType = "$type; charset=utf-8"
  $ctx.Response.StatusCode = $code
  $ctx.Response.OutputStream.Write($buf, 0, $buf.Length)
  $ctx.Response.OutputStream.Close()
}
function Redirect($ctx, [int]$code, [string]$location) {
  $ctx.Response.StatusCode = $code
  $ctx.Response.Headers.Add('Location', $location)
  $ctx.Response.OutputStream.Close()
}
function LoginForm([string]$err) {
  $e = if ($err) { "<span id=`"errorText`">$err</span>" } else { '' }
  return "<html><body><form method=`"post`" id=`"loginForm`" action=`"/adfs/ls/?SAMLRequest=stub&amp;RelayState=r1`">" +
         "<input id=`"userNameInput`" name=`"UserName`" type=`"email`" value=`"`" />" +
         "<input id=`"passwordInput`" name=`"Password`" type=`"password`" />" +
         "<input id=`"optionForms`" type=`"hidden`" name=`"AuthMethod`" value=`"FormsAuthentication`" />" +
         "<span id=`"submitButton`" role=`"button`">Sign in</span>$e</form></body></html>"
}

$access = 'MOCK-JWT-ACCESS-1'
try {
  while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $req = $ctx.Request
    $path = $req.Url.AbsolutePath
    $body = ''
    if ($req.HasEntityBody) {
      $sr = [System.IO.StreamReader]::new($req.InputStream, [System.Text.Encoding]::UTF8)
      $body = $sr.ReadToEnd(); $sr.Close()
    }
    $stamp = (Get-Date).ToString('HH:mm:ss')
    $logBody = if ($path -like '/adfs/ls*' -and $req.HttpMethod -eq 'POST') { '(credentials hidden)' } else { $body }
    Add-Content $log "[$stamp] $($req.HttpMethod) $path`n$logBody`n"
    Write-Host "[$stamp] $($req.HttpMethod) $path"
    $auth = $req.Headers['Authorization']

    switch -Wildcard ($path) {
      # ───── v1 ─────
      '*Enroll*' { Send $ctx 200 '{"status":200,"results":{"token":"MOCK-TOKEN-1234567890"}}' }
      '/graphql' { Send $ctx 200 '{"data":{"update_transaction_datavalues":{"return_status":true,"return_message":"OK (mock)","__typename":"x"}}}' }

      # ───── v2: ADFS SAML ─────
      '/api/Authen/Saml/login' {
        $ctx.Response.Headers.Add('Set-Cookie', 'Saml2.r1=stub-relay; path=/; httponly')
        Redirect $ctx 303 "$base/adfs/ls/?SAMLRequest=stub&RelayState=r1"
      }
      '/adfs/ls*' {
        if ($req.HttpMethod -eq 'GET' -and $req.QueryString['done'] -eq '1') {
          if (-not $req.Cookies['MSISAuth']) { Send $ctx 200 (LoginForm 'session lost') 'text/html'; break }
          Send $ctx 200 ("<html><body><form method=`"POST`" name=`"hiddenform`" action=`"$base/Saml2/Acs`">" +
                         "<input type=`"hidden`" name=`"SAMLResponse`" value=`"U1RVQi1TQU1MLVJFU1BPTlNF`" />" +
                         "<input type=`"hidden`" name=`"RelayState`" value=`"r1`" /></form></body></html>") 'text/html'
        } elseif ($req.HttpMethod -eq 'GET') {
          Send $ctx 200 (LoginForm '') 'text/html'
        } else {
          $f = [System.Web.HttpUtility]::ParseQueryString($body)
          if ($f['Password'] -eq 'wrong-password' -or -not ($f['UserName'] -like '*\*') -or $f['AuthMethod'] -ne 'FormsAuthentication') {
            Send $ctx 200 (LoginForm 'Incorrect user ID or password.') 'text/html'
          } else {
            $ctx.Response.Headers.Add('Set-Cookie', 'MSISAuth=stub; path=/adfs; httponly')
            Redirect $ctx 302 "/adfs/ls/?done=1"
          }
        }
      }
      '/Saml2/Acs' {
        if (-not $req.Cookies['Saml2.r1'] -or $body -notlike '*SAMLResponse=*') { Send $ctx 400 '{"error":"no saml2 cookie"}'; break }
        $ctx.Response.Headers.Add('Set-Cookie', 'AuthCookie=stub; path=/; httponly')
        Send $ctx 200 "<html><script>window.location.href='/adfs_bypasstoken?token=ONE-TIME-STUB-TOKEN'</script></html>" 'text/html'
      }
      '/api/Authen/Saml/loginbypass' {
        if ($body -like '*ONE-TIME-STUB-TOKEN*') {
          $access = 'MOCK-JWT-ACCESS-1'
          Send $ctx 200 "{`"data`":{`"token`":`"$access`",`"refreshToken`":`"MOCK-REFRESH-1`",`"tokenTimeoutMins`":720,`"adFlag`":true}}"
        } else { Send $ctx 401 '{"success":false,"messageCode":401}' }
      }
      '/api/Authen/verify-refresh-token' {
        if ($body -like '*MOCK-REFRESH-*') {
          $access = 'MOCK-JWT-ACCESS-2'
          Send $ctx 200 "{`"data`":{`"token`":`"$access`",`"refreshToken`":`"MOCK-REFRESH-2`"}}"
        } else { Send $ctx 401 '{"success":false,"messageCode":401}' }
      }
      '/apibackend/api/Trans/PTransJsonUpdate' {
        if ($auth -ne "Bearer $access") { Send $ctx 401 '{"success":false,"messageCode":401}'; break }
        if ($body -like '*"configparam_id":401*' -and $access -eq 'MOCK-JWT-ACCESS-1') { Send $ctx 401 '{"success":false,"messageCode":401}'; break }
        if ($body -like '*"configparam_id":999*') {
          Send $ctx 200 '{"data":{"statusCode":"E","statusMessage":"configparam not found (mock)","returnId":"0"},"success":false,"message":"failed","messageCode":0}'
          break
        }
        $n = ([regex]::Matches($body, '"configparam_id"')).Count
        Send $ctx 200 "{`"data`":{`"statusCode`":`"C`",`"statusMessage`":`"บันทึกสำเร็จ [$n] รายการ (mock)`",`"returnId`":`"$n`"},`"success`":true,`"message`":`"successfully`",`"messageCode`":0}"
      }
      default { Send $ctx 404 '{"error":"not found (stub)"}' }
    }
  }
} finally { $listener.Stop() }
