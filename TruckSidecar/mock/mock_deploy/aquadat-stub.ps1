# เซิร์ฟเวอร์ Aquadat จำลอง — รับ login แล้วคืน token ปลอม, รับ mutation แล้วตอบ return_status:true
# รันในเทอร์มินอลของมันเอง แล้วปล่อยค้างไว้:  powershell -ExecutionPolicy Bypass -File mock\aquadat-stub.ps1
param([int]$Port = 18080)

$log = Join-Path $PSScriptRoot 'received.log'
[IO.File]::WriteAllText($log, '')
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Aquadat stub listening on http://localhost:$Port/  (Ctrl+C to stop)"
Write-Host "  Enroll  : http://localhost:$Port/api/aquaDATService/Enroll"
Write-Host "  GraphQL : http://localhost:$Port/graphql"
Write-Host "received payloads are logged to $log"

try {
  while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $req = $ctx.Request
    $body = ''
    if ($req.HasEntityBody) {
      $sr = [System.IO.StreamReader]::new($req.InputStream, $req.ContentEncoding)
      $body = $sr.ReadToEnd(); $sr.Close()
    }
    $stamp = (Get-Date).ToString('HH:mm:ss')
    Add-Content $log "[$stamp] $($req.HttpMethod) $($req.Url.AbsolutePath)`n$body`n"
    Write-Host "[$stamp] $($req.Url.AbsolutePath)"

    if ($req.Url.AbsolutePath -like '*Enroll*') {
      $resp = '{"status":200,"results":{"token":"MOCK-TOKEN-1234567890"}}'
    } else {
      $resp = '{"data":{"update_transaction_datavalues":{"return_status":true,"return_message":"OK (mock)","__typename":"x"}}}'
    }
    $buf = [System.Text.Encoding]::UTF8.GetBytes($resp)
    $ctx.Response.ContentType = 'application/json'
    $ctx.Response.StatusCode = 200
    $ctx.Response.OutputStream.Write($buf, 0, $buf.Length)
    $ctx.Response.OutputStream.Close()
  }
} finally { $listener.Stop() }
