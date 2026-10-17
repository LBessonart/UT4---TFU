# Manual de pruebas con Postman

## 1. Levantar la API

Desde la carpeta `MaquetaApi`:

```powershell
docker compose up -d --build
```

La URL pública es:

```text
http://localhost:5050
```

En Postman puedes crear una variable de entorno:

| Variable | Valor |
|---|---|
| `baseUrl` | `http://localhost:5050` |

Después, en las solicitudes, utiliza `{{baseUrl}}`.

Todas las solicitudes de este manual usan:

```http
Accept: application/json
```

Las solicitudes POST también necesitan:

```http
Content-Type: application/json
```

## 2. Verificar que la API está disponible

### GET Health Check

- Método: `GET`
- URL: `{{baseUrl}}/health`
- Body: ninguno

Respuesta esperada: `200 OK`

```json
{
  "status": "ok"
}
```

La respuesta incluye la cabecera `X-Api-Instance`, cuyo valor será `api1` o `api2`. Esto permite observar qué réplica atendió la solicitud.

## 3. Productos

### GET Consultar productos

- Método: `GET`
- URL: `{{baseUrl}}/api/productos`
- Body: ninguno

Respuesta esperada: `200 OK`

```json
[
  {
    "id": "prod-001",
    "nombre": "Producto A",
    "precio": 50,
    "stock": 100
  },
  {
    "id": "prod-002",
    "nombre": "Producto B",
    "precio": 30,
    "stock": 100
  }
]
```

El stock puede ser menor si ya se ejecutaron pedidos. El valor se consulta desde PostgreSQL, que es compartido por las dos réplicas.

### POST Registrar un producto

- Método: `POST`
- URL: `{{baseUrl}}/api/productos`
- Body: `raw` -> `JSON`

```json
{
  "id": "prod-postman-001",
  "nombre": "Producto creado desde Postman",
  "precio": 25.5,
  "stock": 20
}
```

Respuesta esperada: `201 Created`.

La propiedad `id` debe ser única. Si se reutiliza un ID ya existente, PostgreSQL rechazará el registro.

## 4. Clientes

### GET Consultar clientes

- Método: `GET`
- URL: `{{baseUrl}}/api/clientes`
- Body: ninguno

Respuesta esperada: `200 OK`

```json
[
  {
    "id": "cliente-test-001",
    "nombre": "Cliente de prueba",
    "email": "cliente@example.com"
  }
]
```

### POST Registrar un cliente

- Método: `POST`
- URL: `{{baseUrl}}/api/clientes`
- Body: `raw` -> `JSON`

```json
{
  "id": "cliente-postman-001",
  "nombre": "Cliente creado desde Postman",
  "email": "postman@example.com"
}
```

Respuesta esperada: `201 Created`.

La propiedad `id` debe ser única.

## 5. Pedidos

### GET Consultar pedidos

- Método: `GET`
- URL: `{{baseUrl}}/api/pedidos`
- Body: ninguno

Respuesta esperada: `200 OK`.

Devuelve los pedidos confirmados en PostgreSQL, incluyendo sus ítems. Los pedidos fallidos por error simulado no aparecen en esta lista.

### POST Crear un pedido

- Método: `POST`
- URL: `{{baseUrl}}/api/pedidos`
- Body: `raw` -> `JSON`

```json
{
  "clienteId": "cliente-test-001",
  "items": [
    {
      "productoId": "prod-001",
      "cantidad": 2
    },
    {
      "productoId": "prod-002",
      "cantidad": 1
    }
  ]
}
```

Respuesta esperada: `200 OK`.

```json
{
  "id": "guid-generado",
  "clienteId": "cliente-test-001",
  "items": [
    {
      "productoId": "prod-001",
      "nombre": "Producto A",
      "cantidad": 2,
      "precioUnitario": 50
    },
    {
      "productoId": "prod-002",
      "nombre": "Producto B",
      "cantidad": 1,
      "precioUnitario": 30
    }
  ],
  "subtotal": 130,
  "total": 117
}
```

El servidor obtiene el nombre y el precio desde el catálogo. Por eso, al crear un pedido basta con enviar `productoId` y `cantidad`.

El descuento porcentual configurado por defecto es del 10%. Para cambiarlo a descuento fijo, modifica `DiscountStrategy` en la configuración de Compose y recrea los contenedores.

### POST Pedido con cliente inexistente

- Método: `POST`
- URL: `{{baseUrl}}/api/pedidos`
- Body: el mismo formato anterior, cambiando el cliente:

```json
{
  "clienteId": "cliente-inexistente",
  "items": [
    {
      "productoId": "prod-001",
      "cantidad": 1
    }
  ]
}
```

Respuesta esperada: `404 Not Found`.

```json
{
  "error": "El cliente 'cliente-inexistente' no existe."
}
```

### POST Pedido con rollback ACID

Esta solicitud fuerza un error únicamente para esta petición. No activa un flag global en ninguna réplica.

- Método: `POST`
- URL: `{{baseUrl}}/api/pedidos?simularError=true`
- Body: `raw` -> `JSON`

```json
{
  "clienteId": "cliente-test-001",
  "items": [
    {
      "productoId": "prod-001",
      "cantidad": 1
    }
  ]
}
```

Respuesta esperada: `400 Bad Request`.

```json
{
  "error": "Error simulado para demostrar rollback ACID."
}
```

Para comprobar el rollback:

1. Ejecuta `GET /api/productos` y anota el stock de `prod-001`.
2. Ejecuta el POST anterior con `simularError=true`.
3. Ejecuta nuevamente `GET /api/productos`.
4. El stock debe conservar el mismo valor.
5. Ejecuta `GET /api/pedidos` y verifica que el pedido fallido no fue agregado.

### GET Pedido de demostración

- Método: `GET`
- URL: `{{baseUrl}}/api/pedido/rollbasico`
- Body: ninguno

Procesa automáticamente un pedido de prueba con `prod-001` y `prod-002`.

Respuesta esperada: `200 OK` si hay stock suficiente.

También permite forzar un error por query string:

```text
GET {{baseUrl}}/api/pedido/rollbasico?simularError=true
```

En este endpoint la respuesta de error es `500 Internal Server Error`, porque el controlador de demostración devuelve explícitamente ese código.

## 6. Demostrar el balanceo entre réplicas

Crea una solicitud `GET` a:

```text
{{baseUrl}}/health
```

En la pestaña **Headers** de la respuesta, revisa:

```text
X-Api-Instance: api1
```

o:

```text
X-Api-Instance: api2
```

Repite la solicitud varias veces. Las solicitudes son distribuidas por Nginx entre ambas réplicas.

También puedes usar este script desde PowerShell:

```powershell
.\scripts\demo.ps1
```

## 7. Orden recomendado para la demostración

1. `GET /health`: demostrar que la API está disponible.
2. Repetir `GET /health`: mostrar `X-Api-Instance` con `api1` y `api2`.
3. `GET /api/productos`: mostrar catálogo y stock inicial.
4. `GET /api/clientes`: mostrar clientes.
5. `POST /api/productos`: registrar un producto.
6. `POST /api/clientes`: registrar un cliente.
7. `POST /api/pedidos`: crear un pedido válido.
8. `GET /api/productos`: comprobar que el stock disminuyó.
9. `GET /api/pedidos`: comprobar que el pedido fue persistido.
10. `POST /api/pedidos?simularError=true`: provocar un fallo.
11. `GET /api/productos`: comprobar que el stock volvió a su valor anterior.
12. `GET /api/pedidos`: comprobar que el pedido fallido no fue guardado.

## 8. Operaciones no disponibles

Actualmente la API no implementa endpoints `DELETE`, `PUT` ni `PATCH`, ni consultas individuales por ID.

Por ejemplo, esta solicitud no está disponible:

```http
DELETE {{baseUrl}}/api/productos/prod-001
```

No debe utilizarse como prueba válida porque devolverá `404 Not Found`. La API actual permite altas y consultas de productos y clientes, y creación y consulta de pedidos.

## 9. Detener o reiniciar la solución

Detener los contenedores conservando los datos:

```powershell
docker compose down
```

Iniciarlos nuevamente:

```powershell
docker compose up -d
```

Detenerlos y eliminar también el volumen PostgreSQL para empezar desde cero:

```powershell
docker compose down -v
```

Después será necesario ejecutar nuevamente:

```powershell
docker compose up -d --build
```
