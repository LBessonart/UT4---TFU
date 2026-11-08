# Demo TFU5 - ejecutar desde la carpeta microservicios/ con el stack levantado:
#   docker compose up -d --build
#   .\scripts\demo.ps1
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5050'
$h = @{ 'X-API-Key' = 'dev-key-12345' }

function Pedido($body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-WebRequest "$base/api/pedidos" -Method Post -Headers $h -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 5) -UseBasicParsing
        "  HTTP $($r.StatusCode) en $($sw.ElapsedMilliseconds) ms  cache: $($r.Headers['X-Cache-Productos'])"
    } catch {
        $code = [int]$_.Exception.Response.StatusCode
        $msg = $_.ErrorDetails.Message
        if (-not $msg) { $msg = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() }
        "  HTTP $code en $($sw.ElapsedMilliseconds) ms  $msg"
    }
}
function Circuito { "  circuito -> catálogo: $((Invoke-RestMethod "$base/api/pedidos/circuito" -Headers $h).catalogo)" }
function Simular($ms, $fallar) {
    Invoke-RestMethod "$base/admin/catalogo/simulacion" -Method Post -Headers $h -ContentType 'application/json' `
        -Body (@{ latenciaMs = $ms; fallar = $fallar } | ConvertTo-Json) | Out-Null
}

for ($i = 1; $i -le 30; $i++) {
    try { Invoke-RestMethod "$base/health/pedidos" | Out-Null; break } catch { Start-Sleep 2 }
}
docker compose exec -T cache redis-cli FLUSHALL | Out-Null

Write-Host '1. Gatekeeper: sin API Key -> 401'
try { Invoke-RestMethod "$base/api/productos" | Out-Null } catch { "  HTTP $([int]$_.Exception.Response.StatusCode)" }

Write-Host '2. Crear pedido (caché vacía: GetProducto por SOAP)'
$pedidoId = [guid]::NewGuid().ToString()
$body = @{ id = $pedidoId; clienteId = 'cliente-test-001'; items = @(@{ productoId = 'prod-001'; cantidad = 1 }) }
Pedido $body

Write-Host '3. Otro pedido del mismo producto (Cache-Aside: hit, no hay llamada SOAP)'
Pedido @{ clienteId = 'cliente-test-001'; items = @(@{ productoId = 'prod-001'; cantidad = 1 }) }

Write-Host '4. Reenviar el pedido del paso 2 con el mismo id (idempotencia: 200, no descuenta stock)'
$stockAntes = (Invoke-RestMethod "$base/api/productos/prod-001" -Headers $h).stock
Pedido $body
"  stock prod-001 antes/después: $stockAntes / $((Invoke-RestMethod "$base/api/productos/prod-001" -Headers $h).stock)"

Write-Host '5. Contrato SOAP: WSDL público y ReservarStock por la red interna'
"  WSDL: $((Invoke-WebRequest "$base/ws/productos.wsdl" -UseBasicParsing).Content.Length) bytes"
$soap = '<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:cat="http://ucu.edu.uy/tfu5/catalogo"><soap:Body><cat:GetProductoRequest><cat:id>prod-002</cat:id></cat:GetProductoRequest></soap:Body></soap:Envelope>'
# El XML se pasa por stdin: PowerShell 5.1 elimina las comillas de los argumentos de programas nativos
"  " + ($soap | docker compose exec -T pedidos curl -s -X POST http://catalogo:8080/ws/productos -H 'Content-Type: text/xml' --data-binary '@-')

Write-Host '6. Catálogo lento (7 s > timeout 5 s): Timeout y luego Circuit Breaker abierto (fail fast)'
docker compose exec -T cache redis-cli FLUSHALL | Out-Null
Simular 7000 $false
1..4 | ForEach-Object { Pedido @{ clienteId = 'cliente-test-001'; items = @(@{ productoId = 'prod-002'; cantidad = 1 }) } }
Circuito

Write-Host '7. Con Catálogo degradado, Clientes sigue funcionando (falla contenida)'
"  clientes: $((Invoke-RestMethod "$base/api/clientes" -Headers $h).Count) registros"
"  pedidos existentes: $((Invoke-RestMethod "$base/api/pedidos" -Headers $h).Count) registros"

Write-Host '8. Catálogo se recupera: tras 15 s el circuito pasa a semi-abierto y se cierra solo'
Simular 0 $false
Start-Sleep 16
Pedido @{ clienteId = 'cliente-test-001'; items = @(@{ productoId = 'prod-002'; cantidad = 1 }) }
Circuito
