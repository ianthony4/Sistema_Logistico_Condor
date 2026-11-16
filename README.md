# Sistema de Ventas y Facturación – Ferretería Cóndor Majes S.A.C.

Aplicación de escritorio (C# .NET 8, WPF) para registrar las ventas de la
ferretería y emitir sus comprobantes electrónicos. Trabaja 100 % en la red
local: las tres PC del local se conectan a una base de datos PostgreSQL 16
central instalada en la PC principal.

**Versión 0.3.0 – Incremento del Sprint 3** (4 al 10 de noviembre de 2026):
envío automático de cada boleta a la SUNAT con su CDR y registro de
proveedores, sobre lo de los sprints anteriores (perfil MAESTRO, base central
para tres terminales, catálogo con autocompletado, clientes por DNI o RUC,
boletas con o sin DNI e impresión en la ticketera). Ver [CHANGELOG](CHANGELOG.md).

## Estructura

| Carpeta | Contenido |
|---|---|
| `src/Condor.Escritorio` | Aplicación WPF: ventanas y arranque. |
| `src/Condor.Presentacion` | ViewModels (MVVM) sin dependencias de WPF. |
| `src/Condor.Dominio` | Entidades, reglas (validación, IGV) y contratos. |
| `src/Condor.Datos` | Acceso a PostgreSQL (Npgsql), migraciones y repositorios. |
| `src/Condor.Impresion` | Ticketera térmica: comandos ESC/POS y envío directo al spooler de Windows. |
| `src/Condor.Sunat` | Facturación electrónica: XML UBL 2.1, firma digital, envío a la SUNAT (sendBill) y lectura del CDR. |
| `db/migraciones` | Scripts SQL versionados; se aplican solos al abrir el programa. |
| `db/servidor` | Instalación de la base central en la PC principal. |
| `db/datos` | Lista de precios para la carga inicial del catálogo. |
| `herramientas/Condor.CargaCatalogo` | Programa de consola para la carga inicial. |
| `tests/Condor.Pruebas` | Pruebas unitarias, de integración y de aceptación (xUnit). |
| `docs/` | Modelo de datos, instalación, ticketera, SUNAT y pruebas. |

## Puesta en marcha

1. **PC principal:** instalar PostgreSQL 16 y abrirlo a la LAN según
   [docs/instalacion_postgresql.md](docs/instalacion_postgresql.md).
2. **Cada PC:** copiar `condor.ejemplo.json` como `condor.json` junto al
   ejecutable y poner la IP de la PC principal y la clave.
3. **Catálogo inicial:** ver [db/datos/README.md](db/datos/README.md).
4. **Datos de la ferretería** (RUC, razón social y dirección del ticket):
   ver [docs/modelo_datos.md](docs/modelo_datos.md#datos-de-la-empresa).
5. **PC con ticketera:** instalarla y poner su nombre en `condor.json`
   según [docs/ticketera.md](docs/ticketera.md). Sin ticketera, el ticket se
   muestra en pantalla.
6. **PC con internet:** completar la sección `Sunat` de `condor.json` según
   [docs/sunat.md](docs/sunat.md) (ambiente beta en este sprint). Sin ella, los
   comprobantes se emiten igual y quedan pendientes de envío.

## Atajos de la pantalla Nueva venta

| Tecla | Acción |
|---|---|
| Enter | Elegir el producto sugerido y, en Cantidad, agregarlo al detalle. |
| Alt+D | Ir al campo DNI o RUC del cliente (Enter busca). |
| Ctrl+N | Nuevo producto. |
| Alt+L | Nuevo cliente. |
| F10 | Emitir e imprimir. |
| F11 | Reimprimir el último ticket. |
| F5 | Historial: últimos comprobantes, su estado ante la SUNAT y reimpresión. |
| F6 | Proveedores: buscar por RUC o razón social; Alt+N registra uno nuevo. |
| Esc | Cancelar la venta en curso. |

## Desarrollo

Requisitos: .NET 8 SDK (o Visual Studio 2022) en Windows 10/11.

```powershell
dotnet build CondorVentas.sln
dotnet run --project src/Condor.Escritorio
dotnet test        # ver docs/pruebas.md para las pruebas con base de datos
```

## Flujo de trabajo

- `main`: versión entregada al final de cada sprint (etiquetas `v0.X.0`).
- `develop`: integración del sprint en curso.
- Una rama por tarea, `feature/T-XX-descripcion-corta`, con pull request contra
  `develop` revisado por otro integrante y con la integración continua en verde
  (Definition of Done).

Tablero Scrum: <https://github.com/users/ianthony4/projects/6>
