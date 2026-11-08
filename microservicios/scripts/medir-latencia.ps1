# Mide la latencia de POST /api/pedidos (compromiso "SOAP es más lento" de la TFU5).
# Uso: .\scripts\medir-latencia.ps1 [-N 50] [-Url http://localhost:5050]
# Sirve también contra el monolito de la TFU4 (raíz del repo) para comparar.
param([int]$N = 50, [string]$Url = 'http://localhost:5050')
$ErrorActionPreference = 'Stop'

$h = @{ 'X-API-Key' = 'dev-key-12345' }
$body = @{ clienteId = 'cliente-test-001'; items = @(@{ productoId = 'prod-001'; cantidad = 1 }, @{ productoId = 'prod-002'; cantidad = 1 }) } | ConvertTo-Json -Depth 5

# Calentamiento (JIT, pool de conexiones, primera carga de la caché)
1..3 | ForEach-Object { Invoke-RestMethod "$Url/api/pedidos" -Method Post -Headers $h -ContentType 'application/json' -Body $body | Out-Null }

$tiempos = foreach ($i in 1..$N) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-RestMethod "$Url/api/pedidos" -Method Post -Headers $h -ContentType 'application/json' -Body $body | Out-Null
    $sw.Elapsed.TotalMilliseconds
}

$ord = $tiempos | Sort-Object
"Pedidos: $N  promedio: {0:N1} ms  p50: {1:N1} ms  p95: {2:N1} ms  max: {3:N1} ms" -f `
    ($tiempos | Measure-Object -Average).Average, $ord[[int]($N * 0.5)], $ord[[math]::Min($N - 1, [int]($N * 0.95))], $ord[-1]
