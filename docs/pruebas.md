# Pruebas (T-10, T-14, T-18)

Todas las pruebas están en `tests/Condor.Pruebas` (xUnit).

| Carpeta | Tipo |
|---|---|
| `Dominio/`, `Presentacion/`, `Datos/` | Unitarias: no necesitan base de datos. |
| `Aceptacion/` | Criterios de aceptación de cada historia (`HU14_...`, `HU01_...`, `HU02_...`, `HU16_...`). |
| `Infraestructura/` | Datos de prueba, repositorio en memoria y conexión a la BD de pruebas. |

## Correr solo las unitarias

```powershell
dotnet test
```

Las pruebas marcadas con `[FactConBD]` aparecen como *omitidas* si no hay base
de datos configurada; no fallan.

## Correr también las de integración

Necesitan la base `condor_pruebas` (se crea con `db/servidor/crear_bd.sql`).
**Nunca** apuntar a la base `condor` de producción.

```powershell
$env:CONDOR_TEST_CONN = "Host=localhost;Port=5432;Database=condor_pruebas;Username=condor_app;Password=<clave>"
dotnet test
```

Desde otra PC del local, cambiar `localhost` por la IP de la PC principal.

## Antes de mover una tarjeta a *Hecho*

1. `dotnet test` pasa completo con `CONDOR_TEST_CONN` definida.
2. Se revisaron a mano los criterios de aceptación de la historia en la red de
   pruebas con dos terminales (Definition of Done).
