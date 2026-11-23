# Pruebas (T-10, T-14, T-18, T-25, T-30, T-34, T-40, T-49, T-54, T-59, T-64, T-69, T-74, T-78)

Todas las pruebas están en `tests/Condor.Pruebas` (xUnit).

| Carpeta | Tipo |
|---|---|
| `Dominio/`, `Presentacion/`, `Datos/`, `Impresion/`, `Sunat/` | Unitarias: no necesitan base de datos ni ticketera. |
| `Aceptacion/` | Criterios de aceptación de cada historia (`HU14_...`, `HU01_...`, `HU02_...`, `HU16_...`, `HU03_...`, `HU05_...`, `HU12_...`, `HU04_...`, `HU07_...`, `HU06_...`, `HU17_...`, `HU15_...`, `HU08_...`, `HU09_...`, `HU18_...`). Cada prueba empieza con el criterio que comprueba (`CA1_`, `CA2_`...). |
| `Infraestructura/` | Datos de prueba, repositorios en memoria (productos, clientes, comprobantes, proveedores, envíos, respaldos, compras), ticketera falsa, SUNAT falsa, generador de respaldos falso y conexión a la BD de pruebas. |

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

## Emisión de boletas desde tres PC (T-30)

`Aceptacion/HU05_EmitirBoletaTests` emite 25 boletas desde cada una de las
tres PC simuladas, todas a la vez, sobre una serie de prueba (`Z001`,
`Z002`...). Se comprueba que los números van del 1 al 75 sin repetidos ni
huecos y que cada boleta tiene su detalle. Para repetirla con las PC reales:

```powershell
dotnet test --filter "FullyQualifiedName~HU05_EmitirBoleta"
```

Las pruebas de `CA1_` emiten sobre la serie real `B001` de la base de
**pruebas**; nunca correrlas contra la base `condor` del local.

## Ticketera (T-34)

`Aceptacion/HU12_ImprimirTicketTests` usa una ticketera falsa
(`Infraestructura/ImpresoraFalsa`) que puede simular estar apagada o sin
papel. La prueba con la ticketera real se hace a mano siguiendo
[ticketera.md](ticketera.md#prueba-manual-con-la-ticketera-real-definition-of-done-de-hu-12).

## Proveedores (T-40)

`Aceptacion/HU04_ProveedoresTests` registra un proveedor desde la PC-1 y lo
busca por RUC desde la PC-2 y la PC-3; también intenta guardar el mismo RUC
desde las tres PC a la vez y comprueba que queda un solo proveedor. Para
repetirla con las PC reales:

```powershell
dotnet test --filter "FullyQualifiedName~HU04_Proveedores"
```

Prueba manual (Definition of Done de HU-04): en la PC-1 abrir **Proveedores
(F6)**, pulsar **+ Nuevo proveedor** (Alt+N), registrar uno con RUC válido y,
en la PC-2, buscarlo por su RUC o por parte de la razón social.

## SUNAT (T-49)

`Aceptacion/HU07_EnviarSunatTests` tiene dos clases de pruebas:

- `[FactConBD]`: usan la SUNAT falsa (`Infraestructura/EnvioSunatFalso`), que
  acepta, rechaza, simula la falta de internet o tarda lo que se le pida. Así
  se comprueba que el ticket sale igual y que la venta siguiente no espera.
- `[FactSunatBeta]`: envían de verdad al **ambiente beta** de la SUNAT (usuario
  `MODDATOS`) una boleta sin DNI, una con DNI y una factura, y esperan el CDR
  con código 0. Se omiten si no se define la variable:

```powershell
$env:CONDOR_SUNAT_BETA = "1"
dotnet test --filter "FullyQualifiedName~HU07"
```

La configuración y la prueba manual están en [sunat.md](sunat.md).

## Factura con RUC (T-54)

`Aceptacion/HU06_EmitirFacturaTests` emite desde la pantalla Nueva venta una
factura a una constructora con RUC: comprueba que sale con la serie `F001` y
el siguiente número, que se envía a la SUNAT (falsa) y que el ticket dice
«FACTURA ELECTRÓNICA», el RUC y la forma de pago. También intenta emitirla
sin cliente y con un cliente de DNI: no se emite, se pide el RUC y no se
gasta ningún número de la `F001`. Las tres PC emiten facturas a la vez sobre
una serie de prueba (`V001`, `V002`...) sin repetir ni saltar números.

```powershell
dotnet test --filter "FullyQualifiedName~HU06"
```

Prueba manual (Definition of Done de HU-06): en la PC con ticketera elegir
**Factura** en el resumen; el cursor pasa al campo **RUC del cliente**.
Digitar el RUC de una empresa registrada, **Buscar** y **Emitir e imprimir
(F10)**: el ticket sale como factura `F001` y bajo el botón se ve la
respuesta de la SUNAT. Con Factura elegida y sin RUC, F10 no emite.

## Reenvío automático a la SUNAT (T-59)

`Aceptacion/HU17_ReenvioAutomaticoTests` vende sin internet (SUNAT falsa) y
comprueba que, al volver la conexión, la vuelta del reenvío envía los
pendientes del más antiguo al más reciente y guarda su estado y su CDR; que
mientras sigue sin internet no se gastan intentos en los demás; que la
factura pendiente también se reenvía; que el reenvío no se cruza con el envío
de una venta recién emitida; que dos PC que reenvían a la vez nunca mandan
el mismo comprobante, y que el pie avisa cuando un pendiente lleva más de 2
días (la SUNAT da 3). Antes de cada prueba, los pendientes que dejaron otras
pruebas quedan apartados para otra PC (`Infraestructura/PendientesDePrueba`).

```powershell
dotnet test --filter "FullyQualifiedName~HU17"
```

Prueba manual (Definition of Done de HU-17): en la PC con la SUNAT
configurada, desconectar el cable de internet (no el de la red local) y
emitir dos boletas: bajo Emitir dice «quedó pendiente» y el pie muestra
«2 pendientes SUNAT». Volver a conectar internet y esperar los minutos de
`ReenvioMinutos` (o pulsar **Reenviar a la SUNAT** en el Historial, F5):
el aviso desaparece y en el Historial las dos dicen «Aceptada».

## Respaldo (T-64)

`Aceptacion/HU15_RespaldoTests` genera el respaldo desde la ventana F9 de la
PC principal con un `pg_dump` simulado que escribe en una carpeta temporal
(el «disco externo»): comprueba el nombre con la fecha y la hora, el avance
y la confirmación, y que las otras PC lo ven como el último. Sin el disco
conectado no se genera nada. El aviso de las 24 horas se prueba simulando que
se abre el sistema 25 horas (aviso) y 3 horas (sin aviso) después del último
respaldo. La prueba `[FactConPgDump]` ejecuta el `pg_dump` real y comprueba
que el archivo tiene la firma `PGDMP`; se omite si no se define
`CONDOR_PG_DUMP`:

```powershell
$env:CONDOR_PG_DUMP = "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe"
dotnet test --filter "FullyQualifiedName~HU15"
```

Prueba manual (Definition of Done de HU-15): en la PC principal, con el
disco externo conectado, **Respaldo (F9)** > **Generar respaldo**; al
terminar, el archivo `condor_AAAA-MM-DD_HH-mm.backup` está en el disco. Luego
seguir el «Ensayo de restauración» de [respaldo.md](respaldo.md).

## Nota de venta (T-69)

`Aceptacion/HU08_NotasVentaTests` emite desde la pantalla Nueva venta una
nota de venta de más de S/ 700 sin DNI: comprueba que sale con la serie
`NV01` y el siguiente número, que se imprime con la leyenda «Documento
interno de control» y sin desglosar el IGV, y que puede llevar al cliente.
Comprueba también que no se envía a la SUNAT (falsa), que no queda ningún
intento anotado y que no aparece entre los pendientes que reenvía el
reenvío automático (HU-17); la boleta que se emite después sí se envía. Las
tres PC emiten notas de venta a la vez sobre una serie de prueba (`K001`,
`K002`...) sin repetir ni saltar números.

```powershell
dotnet test --filter "FullyQualifiedName~HU08"
```

Prueba manual (Definition of Done de HU-08): elegir **Nota venta** en el
resumen y **Emitir e imprimir (F10)**: el ticket sale como `NV01` con la
leyenda de documento interno, bajo el botón no aparece ninguna respuesta de la
SUNAT y el pie no suma pendientes.

## Proformas (T-74)

`Aceptacion/HU09_ProformasTests` emite desde la pantalla Nueva venta una
proforma para una constructora: comprueba que sale con la serie `PR01`, que
vale 7 días contando el de emisión, que el ticket dice «Válida hasta» y que no
se envía a la SUNAT. Desde otra pantalla la busca por el RUC en Proformas
(F7), la convierte y comprueba que el detalle y el cliente pasan tal cual, que
se propone Factura y que al emitir sale con el siguiente número de la `F001`,
enlazada a la proforma y enviada a la SUNAT. Si dos PC cargan la misma
proforma, la segunda en emitir recibe el aviso «ya se convirtió en venta» y no
gasta número; en la lista figura en qué factura terminó. Una proforma vencida
no se puede convertir ni desde la ventana ni forzando la emisión (la base
central la rechaza); en su último día de vigencia todavía se convierte.

```powershell
dotnet test --filter "FullyQualifiedName~HU09"
```

Prueba manual (Definition of Done de HU-09): armar un pedido de obra, buscar
el RUC del cliente, elegir **Proforma** y **Emitir e imprimir (F10)**. En otra
PC, **Proformas (F7)**, buscar por el RUC, **Convertir en venta (Enter)**: el
detalle aparece en Nueva venta con el aviso «Venta desde la proforma…» y
Factura elegida; **F10** emite la factura. Volver a F7: la proforma dice
«Convertida en F001-…» y el botón Convertir queda deshabilitado.

## Historial de ventas (T-78)

`Aceptacion/HU18_HistorialTests` vende a un vecino (una boleta aceptada por
la SUNAT falsa y otra pendiente, sin internet) y comprueba que el Historial
(F5) filtrado por su DNI muestra solo sus dos comprobantes, del más reciente
al más antiguo, con su estado ante la SUNAT y el total del período. Busca
también por el nombre escrito sin tildes y por el número; por rango de fechas
(una venta pasada a la semana anterior no sale en «hoy» y sí en ese rango) y
por tipo (nota de venta). Por último, en otra pantalla reimprime una boleta
desde el Historial: el ticket sale marcado «COPIA - REIMPRESIÓN», con el
detalle completo y el cliente, y la base anota la segunda impresión.

```powershell
dotnet test --filter "FullyQualifiedName~HU18"
```

Prueba manual (Definition of Done de HU-18): **Historial (F5)** abre con las
ventas de hoy y su total sobre la lista. Cambiar **Desde**/**Hasta**, el
**Tipo** o escribir un DNI, RUC, nombre o número y pulsar **Buscar** (Enter);
**Este mes** y **Todo** cambian el período de un clic. Elegir un comprobante y
**Reimprimir (F11)**: el ticket sale como copia.

## Antes de mover una tarjeta a *Hecho*

1. `dotnet test` pasa completo con `CONDOR_TEST_CONN` definida.
2. Se revisaron a mano los criterios de aceptación de la historia en la red de
   pruebas con dos terminales (Definition of Done).
