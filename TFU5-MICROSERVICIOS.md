# TFU5 — De monolito modular a microservicios (con una operación SOAP)

Este documento describe la implementación de lo planteado en **ADA2 - TFU - UT5.pdf**: la aplicación de pedidos
de las TFU anteriores se separa en microservicios con base de datos propia. Además, la consulta de productos
(`ObtenerProducto`) pasa de REST a **SOAP/XML**, y se agregan los patrones con los que respondemos a las
falacias de la computación distribuida (Timeout, Retry, Circuit Breaker, idempotencia y Cache-Aside).

> Es una **demo local**. Lo único que se publica es el Gatekeeper, en `127.0.0.1:5050`, así que no queda
> accesible ni desde la red local ni desde internet. Las API keys y contraseñas son valores de prueba.

El monolito de la TFU4 se mantiene **sin cambios** en la raíz del repositorio, para poder compararlo (sección 3 del PDF).
La TFU5 vive en la carpeta [`microservicios/`](microservicios/).

---

## 1. Cómo ejecutarlo

Requisitos: Docker Desktop. Desde `microservicios/`:

```bash
docker compose up -d --build
```

```powershell
.\scripts\demo.ps1            # recorre todos los escenarios (caché, idempotencia, SOAP, timeout, circuit breaker, recuperación)
.\scripts\medir-latencia.ps1  # mide la latencia de crear un pedido (promedio, p50, p95)
```

```bash
docker compose down           # agregar -v para borrar también las bases de datos
```

> El monolito (raíz) y los microservicios usan el mismo puerto 5050: hay que levantar uno a la vez.

Todas las llamadas pasan por el Gatekeeper y llevan el header `X-API-Key: dev-key-12345`.

| Método | Ruta (vía Gatekeeper) | Servicio |
|---|---|---|
| GET | `/health`, `/health/{catalogo\|clientes\|pedidos}` | Gatekeeper / cada servicio |
| GET, POST | `/api/productos`, `GET /api/productos/{id}` | Catálogo (REST) |
| GET | `/ws/productos.wsdl` | Catálogo (contrato SOAP, público) |
| GET, POST | `/api/clientes`, `GET /api/clientes/{id}`, `GET /api/clientes/{id}/validacion` | Clientes (REST) |
| POST | `/api/pedidos` | Pedidos: crea un pedido (orquesta todo el flujo) |
| GET | `/api/pedidos`, `/api/pedidos/{id}` | Pedidos |
| GET | `/api/pedidos/circuito` | Estado del Circuit Breaker hacia Catálogo |
| GET, POST | `/admin/catalogo/simulacion` `{"latenciaMs":7000,"fallar":false}` | Inyección de fallas en Catálogo (solo demo) |

Ejemplo de pedido:

```json
POST /api/pedidos
{ "id": "opcional-guid", "clienteId": "cliente-test-001",
  "items": [ { "productoId": "prod-001", "cantidad": 2 }, { "productoId": "prod-002", "cantidad": 1 } ] }
```

---

## 2. Qué se construyó

```
microservicios/
├── docker-compose.yaml        # 8 contenedores, 4 redes
├── gateway/nginx.conf         # Gatekeeper: routing, rate limiting, API key
├── ServicioCatalogo/          # catalogo.dll — REST + SOAP (WSDL), BD propia
│   ├── Soap/CatalogoSoapEndpoint.cs   # GetProducto, ReservarStock, LiberarStock
│   ├── Soap/Wsdl.cs                   # contrato WSDL 1.1 explícito
│   └── Services/InventarioService.cs  # reserva idempotente por pedidoId
├── ServicioClientes/          # clientes.dll — REST/JSON, BD propia
├── ServicioPedidos/           # pedidos.dll — orquesta la creación del pedido, BD propia
│   ├── Integracion/ProductSoapClient.cs     # cliente SOAP
│   ├── Integracion/ClientesClient.cs        # cliente REST
│   ├── Integracion/PoliticasResiliencia.cs  # Timeout + Retry + Circuit Breaker (Polly)
│   ├── Integracion/CatalogoCache.cs         # Cache-Aside sobre Redis, TTL 60 s
│   ├── Services/PedidoService.cs            # flujo del diagrama de secuencia + compensación
│   └── Strategies/                          # IDiscountStrategy (heredado de TFU3/4)
└── scripts/demo.ps1, scripts/medir-latencia.ps1
```

### Correspondencia con los diagramas del PDF

| Elemento del PDF | Implementación |
|---|---|
| Gatekeeper (Nginx): Routing Rules + Rate Limiting | `gateway/nginx.conf`: una regla por prefijo (`/api/productos`→catálogo, etc.), `limit_req` 20 req/s por IP (burst 40, responde 429) y validación de `X-API-Key` |
| ServicioPedidos: IPedidoService, DiscountStrategy, ProductSoapClient, CircuitBreakerPolicy | `PedidoService`, `Strategies/`, `ProductSoapClient`, `PoliticasResiliencia` |
| ServicioCatalogo: WSDL en `/ws/productos.wsdl`, GetProducto, ReservarStock | `Soap/Wsdl.cs` + `Soap/CatalogoSoapEndpoint.cs` (SOAP 1.1 document/literal) |
| ServicioClientes: ValidarCliente(id) REST/JSON | `GET /api/clientes/{id}/validacion` → `{"clienteId":"…","valido":true}` |
| Contenedor Cache: CatalogoCache TTL 60 s, Cache-Aside | Contenedor `redis:7-alpine` + `CatalogoCache` (`Cache__TtlSegundos=60`) |
| BDPedidos, BDClientes, BDCatalogo | Tres contenedores `postgres:16-alpine`, uno por servicio |
| Despliegue: catalogo.dll, clientes.dll, pedidos.dll | `AssemblyName` de cada `.csproj` |
| "VM Aplicaciones" / "VM Bases de Datos" | Red `servicios` + una red de datos **por servicio** (`datos-catalogo`, `datos-clientes`, `datos-pedidos`) |

### Flujo "Crear un Pedido" (diagrama de secuencia)

1. `POST /api/pedidos` → Gatekeeper → Pedidos.
2. Si el `id` del pedido ya existe, devuelve el pedido existente (`200` + `X-Pedido-Existente: true`).
3. **Clientes** (REST): `ValidarCliente`. Si no existe, responde `404`.
4. Por cada producto distinto: **caché** `GET producto:{id}` → *hit*: usa el dato. *Miss*: **SOAP `GetProducto`** y luego `SET` con TTL de 60 s.
   La respuesta trae el header `X-Cache-Productos: hits=N; misses=M`.
5. Calcula el total con la `IDiscountStrategy` configurada.
6. **SOAP `ReservarStock(pedidoId, items)`**. Si no hay stock, responde `409`; si el producto no existe, `400`.
7. **INSERT** del pedido en la BD de Pedidos (transacción local) → `201 Created`.
8. Si Catálogo está caído, es lento o tiene el circuito abierto → **`503 Service Unavailable`** con `Retry-After` (fail fast).
9. Si el paso 7 falla después de haber reservado → **compensación** con SOAP `LiberarStock(pedidoId)`.

---

## 3. Patrones aplicados (sección 4 y 5 del PDF)

| Falacia / objetivo | Patrón | Dónde | Configuración |
|---|---|---|---|
| La red es confiable | **Timeout** en toda llamada entre servicios | `PoliticasResiliencia` | Catálogo 5 s, Clientes 3 s, por intento |
| La red es confiable | **Circuit Breaker** Pedidos→Catálogo | `PoliticasResiliencia` | Se abre con ≥50 % de fallas sobre un mínimo de 3 llamadas en 30 s; queda abierto 15 s y luego pasa a semi-abierto |
| La red es confiable | **Retry** con backoff exponencial y jitter | `PoliticasResiliencia` | 2 reintentos (200 ms, 400 ms aprox.) |
| La red es confiable | **Operaciones idempotentes** | `InventarioService.ReservarStockAsync` / `LiberarStockAsync`, `PedidoService` | Clave: `pedidoId` |
| La latencia es cero | **Cache-Aside** de productos | `CatalogoCache` | Redis, TTL 60 s, sin stock |
| La latencia es cero | **Timeouts** | ídem | ídem |
| Disponibilidad | **Database per Service** + aislamiento de redes | `docker-compose.yaml` | — |
| Seguridad / borde | **Gatekeeper** (routing, rate limiting, API key) | `gateway/nginx.conf` | 20 req/s por IP |
| Consistencia entre servicios | **Compensación** (Saga orquestada simple) | `PedidoService.CompensarAsync` | — |

Todos los valores se pueden cambiar por variables de entorno (`Resiliencia__*`, `Cache__TtlSegundos`) sin recompilar.

---

## 4. Decisiones importantes y por qué

1. **El monolito no se borró: los microservicios están en una carpeta aparte.** Así se pueden levantar las dos
   versiones y comparar (sección 3 del PDF), y medir la latencia de las dos con el mismo script.
   El `MaquetaApi.csproj` excluye `microservicios/**` para que el monolito siga compilando igual.

2. **El SOAP está hecho a mano con `System.Xml.Linq` (sin CoreWCF ni otra librería).** El objetivo de la unidad es
   ver el contrato y el mensaje XML. Así el WSDL (`Soap/Wsdl.cs`), el sobre, los Faults y el parseo quedan
   explícitos en el código y no generados por un framework. Usamos SOAP 1.1 document/literal y los Faults
   se devuelven con HTTP 500, como dice la especificación.

3. **Faults "Client" y faults "Server" se tratan distinto.** Un `soap:Client` (stock insuficiente, producto inexistente)
   es un error de negocio: **no se reintenta y no abre el circuito**, porque el catálogo respondió bien.
   Solo las fallas de infraestructura (red, 5xx, `soap:Server`, timeouts) cuentan para el Circuit Breaker.

4. **El Retry no reintenta los timeouts.** En la primera prueba, con un Catálogo colgado, cada pedido tardaba
   **15,5 s** (3 intentos × 5 s) y el circuito todavía no se abría. Reintentar contra un servicio lento multiplica
   la espera y lo sobrecarga más. Ahora el Retry solo reintenta fallas **rápidas** (conexión cortada, 5xx), que es
   donde aparece el caso de "la respuesta se perdió en el camino". Los timeouts los cuenta el Circuit Breaker.
   Resultado medido: 3 pedidos de 5 s cada uno y desde el cuarto **503 en 5–7 ms**.

5. **Orden del pipeline: Retry → Circuit Breaker → Timeout.** El Timeout es el más interno y limita cada intento.
   El Circuit Breaker registra cada intento. El Retry no maneja `BrokenCircuitException`, así que con el circuito
   abierto no se reintenta y se falla al instante. El pipeline es un **singleton**: el estado del circuito lo
   comparten todas las solicitudes.

6. **`ReservarStock` recibe todos los ítems del pedido en una sola llamada, no `ReservarStock(id, cant)` por ítem.**
   Es una diferencia con el diagrama, y es a propósito. Una sola llamada significa una sola transacción en Catálogo:
   o se reservan todos los ítems o ninguno, y no queda stock descontado a medias. También hay una sola ida y vuelta
   por la red, que responde a la falacia "la latencia es cero".

7. **La idempotencia se apoya en la base de datos, no en la memoria.** La tabla `Reservas` tiene `PedidoId` como
   clave primaria. Un reintento devuelve la reserva original (`yaProcesada=true`) sin volver a descontar.
   Si llegan dos reintentos simultáneos, la clave única hace que uno solo gane. El descuento es un
   `UPDATE … SET Stock = Stock - n WHERE Stock >= n`, atómico aun con concurrencia, y los locks se toman
   siempre en el mismo orden para evitar deadlocks. Del lado del cliente, si el `POST /api/pedidos` trae `id`,
   repetirlo no duplica el pedido.

8. **La consistencia entre servicios es eventual, con compensación.** Al tener una base por servicio se pierde la
   transacción ACID única de la TFU4. Si falla el INSERT del pedido después de reservar, se llama a `LiberarStock`
   (también idempotente). Una vez reservado el stock, el INSERT se hace con `CancellationToken.None`: si el cliente
   corta la conexión, el pedido igual se completa y no queda una reserva huérfana.

9. **El stock nunca se cachea.** `GetProducto` ni siquiera devuelve el stock: solo id, nombre y precio. El stock lo
   confirma Catálogo al reservar. El precio puede quedar desactualizado hasta 60 s, que el PDF ya acepta como compromiso.

10. **La caché es opcional para el sistema.** Si Redis está caído o tarda más de 500 ms, se trata como un *miss*
    y el pedido se crea igual (medido: 201 en ~1,2 s con Redis apagado). La caché mejora el rendimiento, pero no
    puede ser un nuevo punto único de falla. Sin Redis configurado (ejecución local), se usa una caché en memoria.

11. **Las consultas de productos van en paralelo.** Los *misses* de caché se resuelven con `Task.WhenAll`, así la
    latencia de un pedido con varios productos no se suma ítem por ítem.

12. **La API key y el rate limiting se validan en el Gatekeeper.** En la TFU4 la API key la validaba un middleware
    dentro de la API. Ahora se valida en el borde (un `map` de Nginx), así el tráfico no autorizado no llega a la
    red interna y los servicios no repiten esa lógica.

13. **El endpoint SOAP no se publica.** El Gatekeeper expone solo el WSDL (`GET /ws/productos.wsdl`).
    `POST /ws/productos` (que permite reservar y liberar stock) solo es accesible desde la red interna.
    Para probarlo a mano: `docker compose exec pedidos curl … http://catalogo:8080/ws/productos` (lo hace `demo.ps1`, paso 5).

14. **Cada base de datos está aislada en su propia red.** Cada BD comparte red solo con su servicio. Pedidos no
    puede conectarse a la BD de Catálogo aunque quisiera: los datos ajenos solo se obtienen por la API.

15. **Los servicios se despliegan de forma independiente.** Pedidos no depende del arranque de Catálogo ni de
    Clientes, y el Gatekeeper resuelve los nombres DNS en tiempo de ejecución (`resolver` + `proxy_pass` con variable).
    Así arranca aunque un servicio esté caído y sigue al contenedor cuando vuelve con otra IP. Si un servicio no
    está, el Gatekeeper responde `503` en JSON. Cada servicio reintenta conectarse a su BD al arrancar.

16. **No hay una librería compartida entre servicios.** Los modelos (`Producto`, `Pedido`) se repiten a propósito:
    compartir un DLL acoplaría los despliegues. El contrato entre servicios es el WSDL y el JSON.

17. **Usamos Polly v8 (`Polly.Core`) en lugar de escribir el Circuit Breaker a mano.** En la TFU4 escribimos el
    Retry a mano, pero un Circuit Breaker correcto (estados, semi-abierto, ventana de muestreo, concurrencia) es
    fácil de hacer mal. Polly es el estándar en .NET y el componente se llama igual que en el diagrama (`CircuitBreakerPolicy`).

---

## 5. Verificación realizada

Probado con `docker compose up` en Docker Desktop (Windows). Resultados reales:

| Escenario | Resultado |
|---|---|
| Sin `X-API-Key` | `401` desde el Gatekeeper |
| Primer pedido (caché vacía) | `201`, `hits=0; misses=2`, total 117 (130 − 10 %) |
| Pedido repetido del mismo producto | `201`, `hits=1; misses=0` (no hay llamada SOAP) |
| Reenvío con el mismo `id` | `200` + `X-Pedido-Existente: true`, el stock no cambia (93 → 93) |
| `ReservarStock` SOAP dos veces con el mismo `pedidoId` | 1.ª `yaProcesada=false`, 2.ª `yaProcesada=true`, se descuenta una sola vez |
| Cliente inexistente / producto inexistente / sin stock / sin ítems | `404` / `400` / `409` / `400` |
| `POST /ws/productos` desde afuera | `404` (no se expone) |
| Catálogo con 7 s de latencia | 3 pedidos `503` en ~5 s (timeout) → circuito **Open** → siguiente `503` en **5–12 ms** |
| Catálogo vuelve | Después de 15 s: semi-abierto → un pedido `201` → circuito **Closed** |
| Contenedor de Catálogo apagado | Pedidos `503` (fail fast); Clientes `200`; listar pedidos `200` |
| Contenedor de Clientes apagado | Pedidos `503` (timeout de 3 s); Catálogo `200` |
| Redis apagado | Pedido `201` en ~1,2 s (sin caché) |
| `medir-latencia.ps1 -N 50` (caché caliente) | promedio **12,3 ms**, p50 11,9 ms, p95 17,2 ms |

> Para completar la comparación del PDF ("lo vamos a medir"), corran `medir-latencia.ps1` también contra el
> monolito de la TFU4 (raíz del repo). Se puede pasar `-Url` para apuntar a otra dirección.

---

## 6. Limitaciones conocidas (para mencionar en el informe)

- **Reserva huérfana.** Si `ReservarStock` se aplica en Catálogo pero la respuesta se pierde y además fallan todos
  los reintentos, el cliente recibe `503` y el stock queda reservado. Si el cliente reintenta **con el mismo `id`**,
  el pedido se completa sin descontar dos veces (la respuesta 503 se lo indica). Si no lo hace, la reserva queda sin
  pedido. Lo mismo pasa si falla la compensación (queda un log `Compensación PENDIENTE`). La solución completa es un
  *Outbox* o un proceso de conciliación, que quedan fuera del alcance.
- **Las réplicas no comparten el estado del circuito.** Hay una réplica por servicio. Si se escala Pedidos, cada
  réplica tiene su propio Circuit Breaker, lo cual es el comportamiento habitual.
- **No se migraron todas las funciones del monolito.** Los reportes con vista materializada, Valet Key y la
  configuración externa siguen solo en el monolito: el PDF no los incluye en la nueva arquitectura.
- La inyección de fallas (`/admin/catalogo/simulacion`) es solo para la demo.

---

## 7. Observaciones sobre el PDF (borrador)

Notas para terminar el documento. Son sugerencias, no cambios de código:

- **Tabla, fila "Despliegue", columna Microservicios ("RELLENAR").** Sugerencia: *"Nginx como Gatekeeper, un
  contenedor por servicio (catálogo, clientes, pedidos), una base PostgreSQL por servicio y un contenedor de caché
  (Redis), cada BD en una red aislada"*.
- **Sección 5, "Circuit Breaker" (vacía).** Sugerencia: *Táctica: Recuperarse de fallas / Prevenir fallas.
  Después de 3 fallas en 30 s el circuito se abre por 15 s: Pedidos responde 503 en milisegundos en lugar de bloquear
  hilos esperando al catálogo. Así se evita la falla en cascada y, al pasar a semi-abierto, el sistema se recupera solo
  cuando el catálogo vuelve.* También se pueden agregar Cache-Aside (la caché caída no tumba el sistema) y
  Database per Service (aislar fallas) como tácticas.
- **"Compromisos asociados a la elección", primer punto.** Dice "se busca consistencia inmediata", pero la tabla y la
  implementación usan **consistencia eventual** (compensación). Probablemente quisieron decir "se resigna la
  consistencia inmediata".
- **Tercer punto de compromisos (vacío).** Candidato: *más piezas que operar (8 contenedores frente a 4) y la
  necesidad de compensar fallas parciales.*
- **Falacia 2.** "La latencia se multiplica exponencialmente": con una llamada por ítem crece de forma **lineal**.
  Además, con las llamadas en paralelo y la caché, en la práctica no crece con la cantidad de ítems.
- **Diagrama de componentes.** `ReservarStock(id, cant)` se implementó como `ReservarStock(pedidoId, items[])`
  (ver la decisión 6). Conviene actualizar el diagrama o mencionarlo.
