<#
.SYNOPSIS
    Abre PostgreSQL 16 de la PC principal a las PC de la red local (T-02).

.DESCRIPTION
    - listen_addresses = '*' en postgresql.conf
    - regla scram-sha-256 para la subred del local en pg_hba.conf
    - regla de firewall de Windows para el puerto 5432 (perfil Privado)
    - reinicio del servicio
    Es idempotente: se puede ejecutar varias veces sin duplicar reglas.

.EXAMPLE
    .\configurar_servidor.ps1 -Subred 192.168.1.0/24
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{1,3}(\.\d{1,3}){3}/\d{1,2}$')]
    [string] $Subred,

    [string] $DirectorioDatos = 'C:\Program Files\PostgreSQL\16\data',

    [string] $Servicio = 'postgresql-x64-16',

    [int] $Puerto = 5432
)

$ErrorActionPreference = 'Stop'

$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin) {
    throw 'Ejecute PowerShell como administrador.'
}

$conf = Join-Path $DirectorioDatos 'postgresql.conf'
$hba = Join-Path $DirectorioDatos 'pg_hba.conf'
foreach ($archivo in $conf, $hba) {
    if (-not (Test-Path $archivo)) { throw "No se encontró $archivo. Revise -DirectorioDatos." }
}

# 1. Escuchar en todas las interfaces.
$lineas = Get-Content $conf
if ($lineas -match "^\s*listen_addresses\s*=\s*'\*'") {
    Write-Host 'postgresql.conf ya escucha en todas las interfaces.'
} else {
    Copy-Item $conf "$conf.bak" -Force
    $lineas = $lineas | ForEach-Object {
        if ($_ -match '^\s*#?\s*listen_addresses\s*=') { "listen_addresses = '*'`t`t# Condor: acceso desde la LAN" } else { $_ }
    }
    Set-Content -Path $conf -Value $lineas -Encoding UTF8
    Write-Host "postgresql.conf actualizado (copia en $conf.bak)."
}

# 2. Permitir la subred del local con contraseña cifrada.
$regla = "host    all             condor_app      $Subred          scram-sha-256"
if ((Get-Content $hba) -contains $regla) {
    Write-Host 'pg_hba.conf ya tiene la regla de la LAN.'
} else {
    Copy-Item $hba "$hba.bak" -Force
    Add-Content -Path $hba -Value "`r`n# Condor: PC del local`r`n$regla" -Encoding UTF8
    Write-Host "Regla agregada a pg_hba.conf (copia en $hba.bak)."
}

# 3. Firewall: solo redes privadas (la red del local).
$nombreRegla = "Condor PostgreSQL $Puerto"
if (Get-NetFirewallRule -DisplayName $nombreRegla -ErrorAction SilentlyContinue) {
    Write-Host 'La regla de firewall ya existe.'
} else {
    New-NetFirewallRule -DisplayName $nombreRegla -Direction Inbound -Protocol TCP `
        -LocalPort $Puerto -RemoteAddress $Subred -Profile Private -Action Allow | Out-Null
    Write-Host "Regla de firewall '$nombreRegla' creada."
}

# 4. Reiniciar el servicio para aplicar los cambios.
Restart-Service -Name $Servicio
Write-Host "Servicio $Servicio reiniciado. Pruebe desde otra PC: Test-NetConnection <IP> -Port $Puerto"
