# MaquetaApi - API de pedidos escalable

API REST del mini e-commerce de la TFU3. La solución usa componentes e interfaces, dos réplicas independientes de la API, Nginx como balanceador y PostgreSQL como persistencia compartida.

Para probar todos los endpoints desde Postman, consulta [MANUAL-POSTMAN.md](MANUAL-POSTMAN.md).

## Arquitectura ejecutable

```text
Cliente
  |
Nginx :5050
  |----------------|
API 1            API 2
  |----------------|
PostgreSQL :5432
```

Las réplicas no guardan pedidos, clientes, productos ni flags de simulación en memoria. Todos los datos viven en PostgreSQL. El endpoint `X-Api-Instance` permite observar qué réplica atendió una solicitud.

## Requisitos No Funcionales

### RNF 3 — Configuración del comportamiento
El tipo de descuento aplicado a los pedidos puede modificarse mediante la configuración de la aplicación sin modificar el código fuente.

**Táctica:** Binding en tiempo de configuración

**Implementación:** Al iniciar la API, el sistema lee el parámetro `DiscountStrategy` desde `appsettings.json` (por ejemplo: `percentage` o `fixed`). Un contenedor de Inyección de Dependencias evalúa esta clave e instancia la estrategia correspondiente.

### RNF 4 — Incorporación de nuevos descuentos
El sistema permite incorporar nuevos tipos de descuentos sin modificar la lógica principal de procesamiento de pedidos.

**Táctica:** Polimorfismo

**Implementación:** La lógica del servicio de pedidos está desacoplada de las reglas concretas de descuento mediante la interfaz `IDiscountStrategy`. Para agregar una nueva regla de descuento, basta con crear una nueva clase que implemente dicha interfaz.

## Endpoints

### Balanceador y componentes

El `BalanceadorController` funciona como punto de entrada único para los
componentes de la maqueta:

- `GET /api/productos`: consulta el catálogo mediante `ICatalogo`.
- `GET /api/clientes`: consulta clientes mediante `IClientes`.
- `GET /api/pedidos`: consulta pedidos mediante `IPersistencia`.
- `POST /api/pedidos`: valida el cliente y procesa el pedido mediante una
  réplica seleccionada por `BalanceadorService`.

Los contratos de componentes están en `Components/`:

- `ICatalogo`: lectura de productos.
- `IClientes`: lectura y validación de clientes.
- `IDescuento`: aplicación de la estrategia de descuento.
- `IPersistencia`: almacenamiento y consulta de pedidos.

En producción local, Nginx reparte las solicitudes entre `api1` y `api2`. Dentro de cada proceso, `BalanceadorService` conserva el ejemplo de selección round-robin de implementaciones de `IPedidoService`.

### GET /api/pedido/rollbasico
Procesa un pedido de prueba aplicando la estrategia de descuento configurada.

**Respuesta exitosa (200 OK):**
```json
{
  "id": "guid",
  "clienteId": "cliente-test-001",
  "items": [
    {
      "productoId": "prod-001",
      "nombre": "Producto A",
      "cantidad": 2,
      "precioUnitario": 50.0
    },
    {
      "productoId": "prod-002",
      "nombre": "Producto B",
      "cantidad": 1,
      "precioUnitario": 30.0
    }
  ],
  "subtotal": 130.0,
  "total": 117.0,
  "fechaCreacion": "2026-09-02T14:00:00Z"
}
```

**Respuesta con error (500):**
```json
{
  "error": "Error simulado para demostrar rollback ACID."
}
```

### POST /api/pedidos?simularError=true
Fuerza un error únicamente para esa solicitud. No se guarda ningún estado de simulación en la API.

La respuesta es `400 Bad Request` y la transacción de PostgreSQL revierte el descuento de stock y la inserción del pedido.

## Configuración

### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "DiscountStrategy": "percentage"
}
```

**Valores posibles para `DiscountStrategy`:**
- `percentage`: Aplica un descuento del 10% sobre el subtotal
- `fixed`: Aplica un descuento fijo de $10 sobre el subtotal

## Docker Compose

### Construir la imagen
Desde la carpeta `MaquetaApi/`:

```bash
docker compose up -d --build
```

La API queda disponible en `http://localhost:5050`. Para detenerla:

```bash
docker compose down
```

El despliegue contiene los servicios `db`, `api1`, `api2` y `load-balancer`. El volumen `postgres_data` conserva la base entre reinicios.

## Flujo de Prueba

Ejecutar en PowerShell con Compose levantado:

```powershell
.\scripts\demo.ps1
```

El script verifica salud, réplicas, stock compartido, creación de pedido y rollback ACID.

## Arquitectura

```
MaquetaApi/
├── Controllers/
│   ├── BalanceadorController.cs
│   └── PedidoController.cs
├── Components/
│   ├── ICatalogo.cs
│   ├── IClientes.cs
│   ├── IDescuento.cs
│   └── IPersistencia.cs
├── Models/
│   ├── Cliente.cs
│   ├── Producto.cs
│   ├── Pedido.cs
│   └── PedidoItem.cs
├── Services/
│   ├── BalanceadorService.cs
│   ├── CatalogoService.cs
│   ├── ClientesService.cs
│   ├── IPedidoService.cs
│   ├── PedidoPersistencia.cs
│   ├── PedidoService.cs
├── Strategies/
│   ├── IDiscountStrategy.cs
│   ├── PercentageDiscountStrategy.cs
│   └── FixedDiscountStrategy.cs
├── Data/
│   └── AppDbContext.cs
├── docker-compose.yaml
├── nginx.conf
├── scripts/
│   └── demo.ps1
├── Program.cs
├── appsettings.json
├── Dockerfile
└── README.md
```

## Tecnologías

- .NET 9.0
- ASP.NET Core Web API
- Entity Framework Core + PostgreSQL
- Transacciones ACID con aislamiento Serializable
- Inyección de Dependencias
- Docker
