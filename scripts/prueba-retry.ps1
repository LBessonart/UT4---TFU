param(
    [int]$MaxIntentos = 4,
    [int]$Pedidos = 10,
    [string]$BaseUrl = "http://localhost:5050"
)

Add-Type -AssemblyName System.Net.Http
# Sin esto, Windows PowerShell 5.1 solo manda 2 conexiones a la vez
[System.Net.ServicePointManager]::DefaultConnectionLimit = 50

function ObtenerStock {
    $productos = Invoke-RestMethod "$BaseUrl/api/productos"
    foreach ($p in $productos) {
        if ($p.id -eq "prod-001") { return [int]$p.stock }
    }
}

function ContarReintentos {
    @(docker compose logs api1 api2 | Select-String "Reintento \d+/\d+").Count
}

# 1. Configurar los intentos en la configuracion externa (sin reiniciar nada)
"UPDATE config.parametros SET ""Valor"" = '$MaxIntentos' WHERE ""Clave"" = 'reintentos.maxIntentos';" |
    docker compose exec -T db psql -U postgres -d maquetaapi | Out-Null

$stockAntes = ObtenerStock
$reintentosAntes = ContarReintentos

# 2. Mandar N pedidos del mismo producto al mismo tiempo
$json = '{"clienteId":"cliente-test-001","items":[{"productoId":"prod-001","cantidad":1}]}'
$cliente = [System.Net.Http.HttpClient]::new()
$tareas = @(1..$Pedidos | ForEach-Object {
    $contenido = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
    $cliente.PostAsync("$BaseUrl/api/pedidos", $contenido)
})
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tareas)
$codigos = $tareas | ForEach-Object { [int]$_.Result.StatusCode }

# 3. Resultados
$exitosos = @($codigos | Where-Object { $_ -eq 200 }).Count
$stockDespues = ObtenerStock
$bajo = $stockAntes - $stockDespues
$reintentos = (ContarReintentos) - $reintentosAntes

Write-Host ""
Write-Host "MaxIntentos configurado : $MaxIntentos"
Write-Host "Pedidos enviados        : $Pedidos"
$codigos | Group-Object | ForEach-Object { Write-Host "  HTTP $($_.Name): $($_.Count)" }
$tareas | Where-Object { [int]$_.Result.StatusCode -ne 200 } |
    ForEach-Object { $_.Result.Content.ReadAsStringAsync().Result } |
    Group-Object | ForEach-Object { Write-Host "  [$($_.Count)x] $($_.Name)" }
Write-Host "Reintentos registrados  : $reintentos"
Write-Host "Stock antes / despues   : $stockAntes / $stockDespues (bajo $bajo)"
if ($bajo -eq $exitosos) {
    Write-Host "OK: el stock bajo exactamente lo que suman los pedidos exitosos" -ForegroundColor Green
} else {
    Write-Host "ERROR: el stock no coincide con los pedidos exitosos" -ForegroundColor Red
}