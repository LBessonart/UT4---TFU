# Patrones de Cloud Implementados

## Disponibilidad

### ✅ Retry Pattern
**Archivos:**
- `Services/Reintento.cs` - Lógica de reintento con backoff exponencial
- `Services/PersistenciaConReintentos.cs` - Decorador que aplica reintentos a operaciones de persistencia

**Uso:**
- Se aplica automáticamente a las operaciones de guardado de pedidos
- Reintenta errores transitorios de PostgreSQL (serialización, deadlocks)
- Configuración externa vía `config.parametros.reintentos.maxIntentos`

### ✅ Health Endpoint Monitoring
**Archivos:**
- `Program.cs` - Endpoint `/health`

**Uso:**
```bash
GET /health
```
- Retorna `{ "status": "ok" }` si el servicio está operativo
- No requiere autenticación (endpoint público)

---

## Rendimiento

### ✅ Cache-Aside Pattern
**Archivos:**
- `Services/CatalogoConCacheService.cs` - Decorador con IMemoryCache
- `Controllers/CatalogoController.cs` - Endpoint para probar el caché

**Uso:**
```bash
# Obtener productos (usa caché si está disponible)
GET /api/catalogo

# Obtener producto específico (usa caché si está disponible)
GET /api/catalogo/{id}

# Registrar producto (invalida caché)
POST /api/catalogo
Content-Type: application/json
{
  "id": "prod-003",
  "nombre": "Producto C",
  "precio": 75.00,
  "stock": 50
}
```

**Configuración:**
- Duración del caché: 5 minutos
- Invalidación automática al registrar nuevos productos

### ✅ Materialized View
**Archivos:**
- `Services/RefrescoVistaService.cs` - Background service que refresca la vista
- `Controllers/ReportesController.cs` - Endpoint para consultar la vista

**Uso:**
```bash
# Consultar desde vista materializada (rápido)
GET /api/reportes/ventas-por-producto?directo=false

# Consultar desde tablas (lento, para comparación)
GET /api/reportes/ventas-por-producto?directo=true
```

**Configuración:**
- Intervalo de refresco: configurado en `config.parametros.vista.refrescoSegundos` (default: 30s)
- Refresco concurrente para no bloquear consultas

---

## Seguridad

### ✅ Gatekeeper Pattern
**Archivos:**
- `Middleware/GatekeeperMiddleware.cs` - Middleware de autenticación
- `Controllers/AuthController.cs` - Endpoint para verificar autenticación

**Uso:**
Todas las solicitudes requieren el header `X-API-Key`:

```bash
# Solicitar con API Key válida
GET /api/catalogo
X-API-Key: dev-key-12345

# Solicitar con API Key inválida (401 Unauthorized)
GET /api/catalogo
X-API-Key: invalid-key
```

**API Keys válidas:**
- `dev-key-12345` - Rol: Developer
- `prod-key-67890` - Rol: Production

**Endpoints públicos (sin autenticación):**
- `/health`
- `/debug/config/{clave}`

### ✅ Valet Key Pattern
**Archivos:**
- `Services/ValetKeyService.cs` - Servicio de generación y validación de tokens
- `Controllers/ValetKeyController.cs` - Endpoints para gestionar tokens

**Uso:**

```bash
# Generar un token de acceso temporal
POST /api/valetkey/generar
Content-Type: application/json
{
  "recursoId": "prod-001",
  "permiso": "lectura",
  "minutosExpiracion": 5
}

# Respuesta:
{
  "tokenId": "abc123...",
  "recursoId": "prod-001",
  "permiso": "lectura",
  "expiraEn": "2026-10-17T12:35:00Z",
  "usosRestantes": 1
}

# Validar token
POST /api/valetkey/validar
Content-Type: application/json
{
  "tokenId": "abc123...",
  "recursoId": "prod-001",
  "permiso": "lectura"
}

# Demo de acceso con token
GET /api/valetkey/demo-acceso?tokenId=abc123...&recursoId=prod-001&permiso=lectura

# Revocar token
DELETE /api/valetkey/{tokenId}
```

**Características:**
- Tokens de un solo uso (por defecto)
- Expiración configurable
- Validación de recurso y permiso específicos
- Revocación manual disponible

---

## Modificación y Despliegue

### ✅ External Configuration Store
**Archivos:**
- `Services/ConfiguracionExternaService.cs` - Servicio de configuración
- `Components/IConfiguracionExterna.cs` - Interfaz
- `Program.cs` - Endpoint `/debug/config/{clave}`

**Uso:**
```bash
# Obtener valor de configuración
GET /debug/config/reintentos.maxIntentos

# Respuesta:
{
  "clave": "reintentos.maxIntentos",
  "valor": "4"
}
```

**Configuraciones disponibles:**
- `reintentos.maxIntentos` - Máximo de reintentos (default: 4)
- `vista.refrescoSegundos` - Intervalo de refresco de vista (default: 30)

**Almacenamiento:**
- Base de datos PostgreSQL, tabla `config.parametros`
- Los cambios son dinámicos (no requieren reinicio)

---

## Resumen de Todos los Patrones

| Patrón | Estado | Archivo Principal |
|--------|--------|-------------------|
| Retry | ✅ Implementado | `Services/Reintento.cs` |
| Health Endpoint | ✅ Implementado | `Program.cs` |
| Cache-Aside | ✅ Implementado | `Services/CatalogoConCacheService.cs` |
| Materialized View | ✅ Implementado | `Services/RefrescoVistaService.cs` |
| Gatekeeper | ✅ Implementado | `Middleware/GatekeeperMiddleware.cs` |
| Valet Key | ✅ Implementado | `Services/ValetKeyService.cs` |
| External Config Store | ✅ Implementado | `Services/ConfiguracionExternaService.cs` |

---

## Pruebas Recomendadas

1. **Probar Retry:**
   - Simular error transitorio en `/api/pedido/rollbasico?simularError=true`

2. **Probar Cache-Aside:**
   - Llamar `/api/catalogo` múltiples veces y observar logs de caché
   - Registrar nuevo producto y verificar invalidación

3. **Probar Materialized View:**
   - Comparar tiempos entre `directo=true` y `directo=false`

4. **Probar Gatekeeper:**
   - Solicitar sin API Key (debe fallar)
   - Solicitar con API Key inválida (debe fallar)
   - Solicitar con API Key válida (debe funcionar)

5. **Probar Valet Key:**
   - Generar token, usarlo, intentar reusarlo (debe fallar)
   - Generar token expirado y usarlo después de expiración (debe fallar)

6. **Probar External Config:**
   - Modificar valor en base de datos
   - Verificar que se aplica sin reiniciar servicio
