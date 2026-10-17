$ErrorActionPreference = 'Stop'
$baseUrl = 'http://localhost:5050'

for ($attempt = 1; $attempt -le 20; $attempt++) {
    try {
        if ((Invoke-WebRequest "$baseUrl/health" -UseBasicParsing).StatusCode -eq 200) {
            break
        }
    }
    catch {
        if ($attempt -eq 20) {
            throw 'La API no estuvo disponible dentro del tiempo esperado.'
        }
        Start-Sleep -Seconds 1
    }
}

Write-Host '1. Salud y balanceo'
$instances = 1..6 | ForEach-Object {
    (Invoke-WebRequest "$baseUrl/health" -UseBasicParsing).Headers['X-Api-Instance']
} | Sort-Object -Unique
Write-Host "Instancias atendiendo: $($instances -join ', ')"

Write-Host '2. Lectura desde PostgreSQL compartido'
$productsBefore = Invoke-RestMethod "$baseUrl/api/productos"
$ordersBefore = (Invoke-RestMethod "$baseUrl/api/pedidos").Count
$productBefore = $productsBefore | Where-Object Id -eq 'prod-001'
Write-Host "Stock inicial prod-001: $($productBefore.Stock)"

$body = @{
    clienteId = 'cliente-test-001'
    items = @(
        @{
            productoId = 'prod-001'
            cantidad = 1
        }
    )
} | ConvertTo-Json -Depth 5

Write-Host '3. Pedido valido: descuenta stock y persiste el pedido'
$createdOrder = Invoke-RestMethod "$baseUrl/api/pedidos" -Method Post -ContentType 'application/json' -Body $body
$productsAfterSuccess = Invoke-RestMethod "$baseUrl/api/productos"
$productAfterSuccess = $productsAfterSuccess | Where-Object Id -eq 'prod-001'
Write-Host "Total calculado: $($createdOrder.total)"
Write-Host "Stock despues del pedido: $($productAfterSuccess.Stock)"

Write-Host '4. Pedido fallido: la transaccion ACID debe revertir el stock'
try {
    Invoke-RestMethod "$baseUrl/api/pedidos?simularError=true" -Method Post -ContentType 'application/json' -Body $body | Out-Null
    throw 'El pedido simulado no fallo como se esperaba.'
}
catch {
    Write-Host "Respuesta esperada del pedido fallido: HTTP 400"
}

$productsAfterFailure = Invoke-RestMethod "$baseUrl/api/productos"
$ordersAfterFailure = (Invoke-RestMethod "$baseUrl/api/pedidos").Count
$productAfterFailure = $productsAfterFailure | Where-Object Id -eq 'prod-001'

if ($productAfterFailure.Stock -ne $productAfterSuccess.Stock) {
    throw 'El rollback fallo: el stock cambio despues del pedido fallido.'
}

if ($ordersAfterFailure -ne ($ordersBefore + 1)) {
    throw 'El rollback fallo: se persistio el pedido fallido.'
}

Write-Host 'OK: el pedido fallido no cambio stock ni persistencia.'
