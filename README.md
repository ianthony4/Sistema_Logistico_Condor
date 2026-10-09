# Sistema de Ventas y Facturación – Ferretería Cóndor Majes S.A.C.

Aplicación de escritorio (C# .NET 8, WPF) para registrar las ventas de la
ferretería y emitir sus comprobantes electrónicos. Trabaja 100 % en la red
local: las tres PC del local se conectan a una base de datos PostgreSQL
central instalada en la PC principal.

## Estructura

| Carpeta | Contenido |
|---|---|
| `src/Condor.Escritorio` | Aplicación WPF (patrón MVVM). |
| `docs/` | Documentación técnica del proyecto. |

## Requisitos para compilar

- .NET 8 SDK (o Visual Studio 2022 con la carga de trabajo *Desarrollo de escritorio de .NET*).
- Windows 10 u 11.

```powershell
dotnet build CondorVentas.sln
dotnet run --project src/Condor.Escritorio
```

## Flujo de trabajo

- `main`: versión entregada al final de cada sprint.
- `develop`: integración del sprint en curso.
- Una rama por tarea: `feature/T-XX-descripcion-corta`, con pull request contra
  `develop` revisado por otro integrante (ver la Definition of Done).

Tablero Scrum: <https://github.com/users/ianthony4/projects/6>
