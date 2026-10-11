# Instalación de PostgreSQL 16 en la PC principal (T-02)

La base de datos central vive en la **PC principal** del local. Las otras dos
PC se conectan a ella por la red Ethernet (puerto 5432). No se usa internet.

## 1. Instalar el motor (PC principal)

1. Descargar el instalador de PostgreSQL **16** para Windows x86-64 desde
   <https://www.postgresql.org/download/windows/> (instalador de EDB).
2. Ejecutarlo con las opciones por defecto:
   - Componentes: *PostgreSQL Server*, *Command Line Tools* (pgAdmin es opcional).
   - Directorio de datos: `C:\Program Files\PostgreSQL\16\data`.
   - Puerto: `5432`. Configuración regional: *Spanish, Peru*.
   - Anotar la clave del superusuario `postgres` y guardarla en el sobre del
     Encargado General (no se sube al repositorio).
3. No instalar nada desde *Stack Builder*.

## 2. Fijar la IP de la PC principal

En *Configuración > Red > Ethernet > Editar asignación de IP* poner una IP fija
dentro de la red del local, por ejemplo `192.168.1.10`, máscara
`255.255.255.0`. Las PC secundarias apuntarán a esa IP en su `condor.json`.

## 3. Abrir el acceso desde la LAN

En PowerShell **como administrador**, desde la carpeta del repositorio:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\db\servidor\configurar_servidor.ps1 -Subred 192.168.1.0/24
```

El script:

- pone `listen_addresses = '*'` en `postgresql.conf`;
- agrega en `pg_hba.conf` una regla `scram-sha-256` solo para la subred del local;
- crea la regla de firewall *Condor PostgreSQL 5432* (solo perfil Privado);
- reinicia el servicio `postgresql-x64-16`.

## 4. Crear la base de datos y el usuario de la aplicación

```powershell
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -f db\servidor\crear_bd.sql
```

Crea el rol `condor_app` (sin privilegios de superusuario) y las bases
`condor` (producción) y `condor_pruebas` (pruebas de integración). Cambiar la
clave del rol en la primera línea del script antes de ejecutarlo.

## 5. Verificar desde una PC secundaria

```powershell
Test-NetConnection 192.168.1.10 -Port 5432   # TcpTestSucceeded : True
```

Las tablas las crea la aplicación al iniciar (migraciones, T-03).

## Problemas frecuentes

| Síntoma | Causa probable |
|---|---|
| `TcpTestSucceeded : False` | La red está como *Pública*: cambiarla a *Privada* o revisar el firewall. |
| `no pg_hba.conf entry for host` | La IP de la PC secundaria está fuera de la subred indicada al script. |
| `password authentication failed` | Clave de `condor_app` distinta de la de `condor.json`. |
