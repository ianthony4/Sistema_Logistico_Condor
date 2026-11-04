# Sistema de Ventas y Facturación – Ferretería Cóndor Majes S.A.C.

Aplicación de escritorio (C# .NET 8, WPF) para registrar las ventas de la
ferretería y emitir sus comprobantes electrónicos. Trabaja 100 % en la red
local: las tres PC del local se conectan a una base de datos PostgreSQL 16
central instalada en la PC principal.

**Versión 0.2.0 – Incremento del Sprint 2** (21 de octubre al 3 de noviembre
de 2026): clientes por DNI o RUC, emisión de boletas con o sin DNI e
impresión directa en la ticketera térmica, sobre lo del Sprint 1 (perfil
MAESTRO, base central para tres terminales, catálogo y búsqueda con
autocompletado). Ver [CHANGELOG](CHANGELOG.md).

## Estructura

| Carpeta | Contenido |
|---|---|
| `src/Condor.Escritorio` | Aplicación WPF: ventanas y arranque. |
| `src/Condor.Presentacion` | ViewModels (MVVM) sin dependencias de WPF. |
| `src/Condor.Dominio` | Entidades, reglas (validación, IGV) y contratos. |
| `src/Condor.Datos` | Acceso a PostgreSQL (Npgsql), migraciones y repositorios. |
| `src/Condor.Impresion` | Ticketera térmica: comandos ESC/POS y envío directo al spooler de Windows. |
| `db/migraciones` | Scripts SQL versionados; se aplican solos al abrir el programa. |
| `db/servidor` | Instalación de la base central en la PC principal. |
| `db/datos` | Lista de precios para la carga inicial del catálogo. |
| `herramientas/Condor.CargaCatalogo` | Programa de consola para la carga inicial. |
| `tests/Condor.Pruebas` | Pruebas unitarias, de integración y de aceptación (xUnit). |
| `docs/` | Modelo de datos, instalación, ticketera y pruebas. |

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

## Atajos de la pantalla Nueva venta

| Tecla | Acción |
|---|---|
| Enter | Elegir el producto sugerido y, en Cantidad, agregarlo al detalle. |
| Alt+D | Ir al campo DNI o RUC del cliente (Enter busca). |
| Ctrl+N | Nuevo producto. |
| Alt+L | Nuevo cliente. |
| F10 | Emitir e imprimir. |
| F11 | Reimprimir el último ticket. |
| F5 | Historial: últimos comprobantes para reimprimir. |
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
