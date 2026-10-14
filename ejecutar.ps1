<#
.SYNOPSIS
    Levanta todo lo necesario y abre el Sistema de Ventas Cóndor en esta PC.

.DESCRIPTION
    1. Arranca Docker Desktop si no está corriendo.
    2. Levanta PostgreSQL 16 en un contenedor (condor-ventas-pg) con sus datos
       en el volumen condor-ventas-datos, así que lo registrado se conserva entre ejecuciones.
    3. Compila y publica la aplicación WPF. Si la PC tiene el SDK de .NET 8 lo usa;
       si no, compila dentro de Docker (mcr.microsoft.com/dotnet/sdk:8.0).
    4. Escribe condor.json apuntando al PostgreSQL local y abre CondorVentas.exe.
       Al arrancar, la app aplica las migraciones y registra la terminal (HU-14).

.EXAMPLE
    .\ejecutar.ps1                 # compila y abre la aplicación
    .\ejecutar.ps1 -SinCompilar    # abre la última versión compilada
    .\ejecutar.ps1 -Detener        # detiene el contenedor de PostgreSQL
    .\ejecutar.ps1 -BorrarDatos    # detiene PostgreSQL y borra su base de datos

    Si PowerShell no deja ejecutar scripts:
    powershell -ExecutionPolicy Bypass -File .\ejecutar.ps1
#>
[CmdletBinding()]
param(
    # Puerto de esta PC donde se publica PostgreSQL.
    [int]$Puerto = 5432,
    # No recompila: abre lo que ya está en .ejecutar\app.
    [switch]$SinCompilar,
    [switch]$Detener,
    [switch]$BorrarDatos
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$Contenedor = 'condor-ventas-pg'
$Volumen    = 'condor-ventas-datos'
$BaseDatos  = 'condor'
$Usuario    = 'condor_app'
$Clave      = 'condor_local'          # Solo para desarrollo en esta PC.
$Salida     = Join-Path $PSScriptRoot '.ejecutar\app'
$Exe        = Join-Path $Salida 'CondorVentas.exe'

function Paso([string]$texto) { Write-Host "==> $texto" -ForegroundColor Cyan }

function Invoke-Docker {
    # Ejecuta docker y falla si devuelve error (docker no lanza excepciones).
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "Falló: docker $($args -join ' ')" }
}

# --- 1. Docker ---------------------------------------------------------------
Paso 'Comprobando Docker'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    $binDocker = @(
        "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin",
        "$env:ProgramFiles\Docker\Docker\resources\bin"
    ) | Where-Object { Test-Path (Join-Path $_ 'docker.exe') } | Select-Object -First 1
    if (-not $binDocker) { throw 'No se encontró Docker. Instale Docker Desktop: https://www.docker.com/products/docker-desktop/' }
    $env:PATH = "$binDocker;$env:PATH"
}

& docker info *> $null
if ($LASTEXITCODE -ne 0) {
    $escritorio = @(
        "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe",
        "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $escritorio) { throw 'Docker no está corriendo y no se encontró Docker Desktop.' }
    Paso 'Arrancando Docker Desktop (puede tardar un minuto)'
    Start-Process $escritorio
    $limite = (Get-Date).AddMinutes(3)
    do {
        Start-Sleep -Seconds 3
        & docker info *> $null
    } until ($LASTEXITCODE -eq 0 -or (Get-Date) -gt $limite)
    if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop no terminó de arrancar en 3 minutos.' }
}

# --- Detener / borrar --------------------------------------------------------
if ($Detener -or $BorrarDatos) {
    & docker rm -f $Contenedor *> $null
    Write-Host "PostgreSQL ($Contenedor) detenido."
    if ($BorrarDatos) {
        & docker volume rm $Volumen *> $null
        Write-Host "Base de datos borrada (volumen $Volumen)."
    }
    return
}

# --- 2. PostgreSQL -----------------------------------------------------------
$estado = (& docker ps -a --filter "name=^$Contenedor$" --format '{{.State}}') 2>$null
if (-not $estado) {
    Paso "Creando PostgreSQL 16 en el puerto $Puerto"
    Invoke-Docker run -d --name $Contenedor --restart unless-stopped `
        -e "POSTGRES_DB=$BaseDatos" -e "POSTGRES_USER=$Usuario" -e "POSTGRES_PASSWORD=$Clave" `
        -p "${Puerto}:5432" -v "${Volumen}:/var/lib/postgresql/data" `
        postgres:16 | Out-Null
} elseif ($estado -ne 'running') {
    Paso 'Arrancando PostgreSQL'
    Invoke-Docker start $Contenedor | Out-Null
} else {
    Paso 'PostgreSQL ya está corriendo'
}

$limite = (Get-Date).AddMinutes(1)
do {
    Start-Sleep -Seconds 1
    & docker exec $Contenedor pg_isready -q -h 127.0.0.1 -U $Usuario -d $BaseDatos *> $null
} until ($LASTEXITCODE -eq 0 -or (Get-Date) -gt $limite)
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL no respondió. Revise: docker logs $Contenedor" }

# --- 3. Compilar -------------------------------------------------------------
if (-not $SinCompilar -or -not (Test-Path $Exe)) {
    if (Test-Path $Salida) { Remove-Item -Recurse -Force $Salida }
    $proyecto = 'src/Condor.Escritorio/Condor.Escritorio.csproj'
    $opciones = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:EnableWindowsTargeting=true', '-v', 'q', '-nologo')

    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Paso 'Compilando con el SDK de .NET de esta PC'
        & dotnet publish $proyecto @opciones -o $Salida
        if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
    } else {
        Paso 'Compilando dentro de Docker (esta PC no tiene el SDK de .NET)'
        # Se copia el código a /tmp para no dejar obj/ de Linux en el repositorio;
        # los paquetes NuGet quedan en un volumen para que la próxima vez sea rápida.
        $comando = "cp -r /repo /tmp/src && cd /tmp/src && rm -rf .git .ejecutar && " +
                   "find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + && " +
                   "dotnet publish $proyecto $($opciones -join ' ') -o /salida"
        Invoke-Docker run --rm `
            -v "${PSScriptRoot}:/repo:ro" -v "${Salida}:/salida" -v 'condor-nuget:/root/.nuget/packages' `
            mcr.microsoft.com/dotnet/sdk:8.0 sh -c $comando
    }
}
if (-not (Test-Path $Exe)) { throw "No se generó $Exe" }

# --- 4. Configurar y abrir ---------------------------------------------------
Paso 'Escribiendo condor.json'
$config = [ordered]@{
    Servidor             = 'localhost'
    Puerto               = $Puerto
    BaseDatos            = $BaseDatos
    Usuario              = $Usuario
    Clave                = $Clave
    TiempoEsperaSegundos = 5
}
$config | ConvertTo-Json | Set-Content -Path (Join-Path $Salida 'condor.json') -Encoding UTF8

Paso 'Abriendo Sistema de Ventas Cóndor'
$inicio = Get-Date
$app = Start-Process -FilePath $Exe -WorkingDirectory $Salida -PassThru
Start-Sleep -Seconds 4
if ($app.HasExited -and $app.ExitCode -ne 0) {
    $bloqueo = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; StartTime = $inicio } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like '*CondorVentas*' -or $_.Message -like '*Condor.*' -or $_.Message -like '*Npgsql*' } |
        Select-Object -First 1
    if ($bloqueo) {
        Write-Warning "Windows (Control inteligente de aplicaciones) bloqueó un archivo de la aplicación:`n$($bloqueo.Message)"
    }
    throw "La aplicación se cerró con código $($app.ExitCode)."
}

Write-Host ''
Write-Host "Listo. PostgreSQL: localhost:$Puerto/$BaseDatos (usuario $Usuario, clave $Clave)." -ForegroundColor Green
Write-Host 'PostgreSQL sigue corriendo en segundo plano; para detenerlo: .\ejecutar.ps1 -Detener'
