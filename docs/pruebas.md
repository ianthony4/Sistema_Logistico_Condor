# Pruebas (T-10, T-14, T-18, T-25)

Todas las pruebas están en `tests/Condor.Pruebas` (xUnit).

| Carpeta | Tipo |
|---|---|
| `Dominio/`, `Presentacion/`, `Datos/` | Unitarias: no necesitan base de datos. |
| `Aceptacion/` | Criterios de aceptación de cada historia (`HU14_...`, `HU01_...`, `HU02_...`, `HU16_...`, `HU03_...`). Cada prueba empieza con el criterio que comprueba (`CA1_`, `CA2_`...). |
| `Infraestructura/` | Datos de prueba, repositorios en memoria (productos, clientes, comprobantes) y conexión a la BD de pruebas. |

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

## Prueba de concurrencia en la red del local (T-07)

`Aceptacion/HU16_ConcurrenciaTests` simula las tres PC con tres grupos de
conexiones. Para repetirla con las PC reales:

1. En la PC principal, base `condor_pruebas` creada y accesible desde la LAN
   (`docs/instalacion_postgresql.md`).
2. En **dos o tres PC** a la vez, con `CONDOR_TEST_CONN` apuntando a la IP de
   la PC principal:

   ```powershell
   dotnet test --filter "FullyQualifiedName~HU16_Concurrencia"
   ```

3. Las tres ejecuciones deben pasar. En `psql` se comprueba que ninguna serie
   de prueba (`Y001`, `Y002`...) tiene números repetidos ni huecos:

   ```sql
   SELECT s.codigo, count(*), min(c.numero), max(c.numero)
   FROM comprobante c JOIN serie s ON s.id = c.serie_id
   WHERE s.codigo LIKE 'Y%' GROUP BY s.codigo;   -- count = max y min = 1
   ```

## Antes de mover una tarjeta a *Hecho*

1. `dotnet test` pasa completo con `CONDOR_TEST_CONN` definida.
2. Se revisaron a mano los criterios de aceptación de la historia en la red de
   pruebas con dos terminales (Definition of Done).
