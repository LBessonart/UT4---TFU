param(
    [int]$Repeticiones = 5,
    [string]$BaseUrl = "http://localhost:5050"
)

function PedirReporte([bool]$Directo) {
    $url = "$BaseUrl/api/reportes/ventas-por-producto"
    if ($Directo) { $url += "?directo=true" }
    Invoke-RestMethod $url
}

# Una llamada de calentamiento y despues el promedio de N mediciones (tiempo medido en el servidor)
function Medir([bool]$Directo) {
    PedirReporte $Directo | Out-Null
    $tiempos = 1..$Repeticiones | ForEach-Object { (PedirReporte $Directo).milisegundos }
    ($tiempos | Measure-Object -Average).Average
}

function Resumen($reporte) {
    ($reporte.ventas | Sort-Object productoId |
        ForEach-Object { "$($_.productoId):$($_.unidadesVendidas):$($_.ingresos)" }) -join ";"
}

function UnidadesProd001($reporte) {
    [int]($reporte.ventas | Where-Object { $_.productoId -eq "prod-001" }).unidadesVendidas
}

# 0. Dejar la vista al dia antes de comparar
"REFRESH MATERIALIZED VIEW CONCURRENTLY ventas_por_producto;" |
    docker compose exec -T db psql -U postgres -d maquetaapi | Out-Null

# 1. Tiempos
$msTablas = Medir $true
$msVista = Medir $false
Write-Host ""
Write-Host ("Sin vista (recorre las tablas): {0:N1} ms en promedio" -f $msTablas)
Write-Host ("Con vista materializada       : {0:N1} ms en promedio" -f $msVista)

# 2. Mismos numeros
if ((Resumen (PedirReporte $true)) -eq (Resumen (PedirReporte $false))) {
    Write-Host "OK: los dos reportes dan exactamente los mismos numeros" -ForegroundColor Green
} else {
    Write-Host "ERROR: los reportes difieren" -ForegroundColor Red
}

# 3. Pedido nuevo: la vista queda atrasada hasta el proximo refresco
$json = '{"clienteId":"cliente-test-001","items":[{"productoId":"prod-001","cantidad":1}]}'
Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/pedidos" -ContentType "application/json" -Body $json | Out-Null

Write-Host ""
Write-Host "Despues de un pedido nuevo de prod-001:"
Write-Host ("  Vista  : {0} unidades" -f (UnidadesProd001 (PedirReporte $false)))
Write-Host ("  Tablas : {0} unidades" -f (UnidadesProd001 (PedirReporte $true)))

Write-Host "Esperando 35 s al refresco automatico..."
Start-Sleep -Seconds 35
Write-Host ("  Vista  : {0} unidades (ya refrescada)" -f (UnidadesProd001 (PedirReporte $false)))