# Respaldo de la base de datos (HU-15)

Desde la versión 0.4.0 el Encargado General genera un respaldo de toda la
base de datos central (catálogo, clientes, comprobantes con su CDR,
proveedores y respaldos anteriores) en un disco externo, desde el módulo
**Respaldo (F9)** de la PC principal. Si el último respaldo tiene más de 24
horas, las tres PC lo avisan al abrir el sistema.

## Configuración (una sola vez, en la PC principal)

1. Conectar el disco externo y crear la carpeta `Respaldos Condor`
   (por ejemplo `E:\Respaldos Condor`).
2. En el `condor.json` de la PC principal completar la sección `Respaldo`:

   ```json
   "Respaldo": {
     "Carpeta": "E:\\Respaldos Condor",
     "PgDump": "",
     "AvisoHoras": 24
   }
   ```

   - `Carpeta`: la del disco externo (en JSON la barra se escribe doble).
   - `PgDump`: vacío usa el `pg_dump.exe` de PostgreSQL 16 instalado en
     `C:\Program Files\PostgreSQL\16\bin`. Solo hay que llenarlo si está en
     otra carpeta.
   - `AvisoHoras`: a partir de cuántas horas sin respaldo se avisa (criterio 2).

Las otras PC no llevan la sección `Respaldo`: en ellas el módulo F9 muestra
el último respaldo y explica que se genera desde la PC principal.

## Generar un respaldo (criterio 1)

1. Conectar el disco externo a la PC principal.
2. Pulsar **Respaldo (F9)** y luego **Generar respaldo**. Si Windows le dio
   otra letra al disco, corregir la carpeta o usar **Examinar…**.
3. La barra muestra el avance tabla por tabla. Al terminar dice el nombre del
   archivo, por ejemplo `condor_2026-11-20_18-45.backup`, con la fecha y la
   hora, y la carpeta donde quedó.
4. Quitar el disco con **Quitar hardware de forma segura** y guardarlo fuera
   del local (lo ideal: dos discos que se alternan cada semana).

Mientras se genera no se puede cambiar de módulo ni cerrar la ventana. Se puede seguir vendiendo
en las otras PC: `pg_dump` lee una foto consistente de la base sin
bloquear las ventas.

| Mensaje | Qué hacer |
|---|---|
| «No se encuentra la carpeta … Conecte el disco externo» | El disco no está conectado o tiene otra letra: conectarlo o corregir la carpeta. |
| «El respaldo no se completó … No space left on device» | El disco está lleno: borrar respaldos viejos (dejar al menos los de la última semana). |
| «No se encontró pg_dump» | Instalar PostgreSQL 16 (basta con las *Command Line Tools*) o poner la ruta en `PgDump`. |
| «El archivo quedó en el disco, pero no se pudo anotar» | Se cayó la red local al final: el archivo sirve, pero conviene generar otro para que el aviso de 24 horas se actualice. |

Si algo falla no queda ningún archivo a medias: `pg_dump` escribe en
`… .backup.parcial` y solo al terminar bien se renombra a `.backup`.

## Aviso de respaldo vencido (criterio 2)

Al abrir el sistema, cualquiera de las tres PC consulta en la base central
la fecha del último respaldo (tabla `respaldo`, migración V010). Si tiene
más de `AvisoHoras` horas, o nunca se hizo uno, aparece una franja bajo el
título **Nueva venta** con los botones **Respaldo (F9)** y **Más tarde**.
Desaparece al generar el respaldo.

## Restaurar un respaldo

Solo en caso de desastre (disco de la PC principal dañado, base borrada).
En la PC principal, con PostgreSQL 16 instalado
([instalacion_postgresql.md](instalacion_postgresql.md)) y el programa
**cerrado en las tres PC**:

```powershell
cd "C:\Program Files\PostgreSQL\16\bin"

# 1. Base vacía para restaurar (como el usuario postgres).
.\psql.exe -U postgres -c "CREATE DATABASE condor_restaurada OWNER condor_app"

# 2. Cargar el respaldo elegido del disco externo.
.\pg_restore.exe -U condor_app -d condor_restaurada --no-owner --exit-on-error `
    "E:\Respaldos Condor\condor_2026-11-20_18-45.backup"

# 3. Revisar que estén los últimos comprobantes.
.\psql.exe -U condor_app -d condor_restaurada -c "SELECT max(fecha), count(*) FROM comprobante"
```

Si todo está bien, renombrar las bases (la dañada queda como `condor_danada`
por si hace falta algo de ella):

```powershell
.\psql.exe -U postgres -c "ALTER DATABASE condor RENAME TO condor_danada"
.\psql.exe -U postgres -c "ALTER DATABASE condor_restaurada RENAME TO condor"
```

Al abrir el programa, las migraciones nuevas (si las hay) se aplican solas.
Los comprobantes emitidos **después** del respaldo no están en él: hay que
volver a registrarlos a partir de sus tickets o de los CDR de la SUNAT.

### Ensayo de restauración (una vez al mes)

Restaurar el último respaldo en `condor_restaurada` (pasos 1 a 3) y luego
borrarla con `DROP DATABASE condor_restaurada`. Así se comprueba que los
respaldos del disco sirven antes de necesitarlos.

## Pruebas

`tests/Condor.Pruebas/Aceptacion/HU15_RespaldoTests` simula `pg_dump`. Para
correr también la prueba con el `pg_dump` real:

```powershell
$env:CONDOR_TEST_CONN = "Host=localhost;Port=5432;Database=condor_pruebas;Username=condor_app;Password=<clave>"
$env:CONDOR_PG_DUMP = "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe"
dotnet test --filter "FullyQualifiedName~HU15"
```
