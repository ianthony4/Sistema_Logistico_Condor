# Cambios

## 0.1.0 – Sprint 1 (7 al 20 de octubre de 2026)

Objetivo del sprint: que las tres PC trabajen sobre una base de datos central
en la LAN y que el vendedor pueda registrar productos y buscarlos por nombre
para armar el detalle de una venta.

### HU-14 · Acceso con el perfil único de Usuario Maestro
- El programa abre directamente la pantalla Nueva venta con el perfil MAESTRO,
  sin usuario ni contraseña (T-08, T-09).
- Cada PC queda registrada como terminal con el nombre del equipo (T-09).

### HU-16 · Base de datos central y trabajo simultáneo en LAN
- Modelo de datos y migraciones automáticas de PostgreSQL 16 (T-01, T-03).
- Instalación y apertura de la base central a la red del local (T-02).
- Capa de datos con conexión configurable en `condor.json` (T-04).
- Correlativo por serie con bloqueo de fila: sin números repetidos ni saltados (T-05).
- Aviso de pérdida de conexión con Reintentar, sin perder lo que hay en pantalla (T-06).
- Prueba de concurrencia con tres terminales (T-07).

### HU-01 · Registrar producto en el catálogo
- Diálogo Nuevo producto (T-11), repositorio con validaciones y control de
  duplicados (T-12).
- Integración con la base central y aviso inmediato a las otras PC (T-13).
- Pruebas de aceptación (T-14).

### HU-02 · Buscar producto por nombre al vender
- Búsqueda con índice de trigramas, sin distinguir mayúsculas ni tildes (T-15).
- Autocompletado desde la tercera letra, manejable solo con teclado (T-16).
- Detalle de la venta con operación gravada, IGV (18 %) y total (T-17).
- Pruebas de aceptación y de tiempo de respuesta: menos de 1 s con 3 000 productos (T-18).

### Transversales
- Proyecto de pruebas xUnit y datos de prueba (T-20).
- Carga inicial del catálogo desde la lista de precios (T-19).
- Integración continua en GitHub Actions.
