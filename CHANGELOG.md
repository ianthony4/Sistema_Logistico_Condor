# Cambios

## 0.3.0 – Sprint 3 (4 al 10 de noviembre de 2026)

Objetivo del sprint: que cada boleta emitida llegue sola a la SUNAT y quede
guardada su constancia (CDR), sin frenar la venta cuando falla el internet;
y tener registrados a los proveedores para las compras. Sprint de una semana.

### HU-04 · Registrar proveedores
- Entidad Proveedor y sus reglas: RUC válido, razón social, teléfono y correo
  (T-36).
- Migración V006 y repositorio de proveedores, un solo proveedor por RUC aunque
  dos PC lo registren a la vez; búsqueda por RUC o razón social sin tildes
  (T-37).
- Diálogo Nuevo proveedor con los datos comerciales (T-38).
- Ventana Proveedores (F6) con búsqueda y + Nuevo proveedor (T-39).
- Pruebas de aceptación (T-40).

### HU-07 · Enviar a SUNAT y guardar el CDR
- Proyecto Condor.Sunat, basado en Greenter / sunat-manager: XML UBL 2.1 de la
  boleta y la factura (T-41), firma digital XMLDSig con el certificado (T-42),
  lectura del CDR y estados aceptado, observado, rechazado o pendiente (T-43)
  y envío del ZIP por sendBill con la clave SOL (T-44).
- Migración V007: domicilio fiscal de la empresa y tabla comprobante_sunat con
  el XML firmado, el CDR y los intentos (T-45).
- Sección Sunat de condor.json: ambiente beta o producción, usuario y clave
  SOL y certificado (T-46).
- Envío al emitir, en paralelo con la impresión: el resultado se ve bajo
  Emitir; sin internet el ticket sale igual y queda pendiente (T-47).
- Columna SUNAT en el Historial (F5) con el motivo de los pendientes y
  rechazados (T-48).
- Pruebas de aceptación con la SUNAT simulada y contra el ambiente beta real,
  y guía docs/sunat.md (T-49).

### Transversales
- Datos de prueba de proveedores, SUNAT falsa y repositorios en memoria (T-50).

### Pendiente para el Sprint 4
- Reenvío automático de los comprobantes pendientes (HU-17).
- Factura con RUC desde la pantalla de venta (HU-06); el XML ya la soporta.
- Pasar a producción con el certificado digital y el usuario SOL de la ferretería.

## 0.2.0 – Sprint 2 (21 de octubre al 3 de noviembre de 2026)

Objetivo del sprint: que el vendedor emita la boleta de la venta, con o sin
DNI, y la entregue impresa en la ticketera sin pasos adicionales.

### HU-03 · Registrar o buscar cliente por DNI/RUC
- Validación del DNI (8 dígitos) y del RUC (11 dígitos, prefijo y dígito
  verificador módulo 11) (T-21).
- Repositorio de clientes en la base central, sin documentos repetidos (T-22).
- Panel Cliente en Nueva venta: un solo campo DNI o RUC; Buscar completa el
  nombre o la razón social y la dirección (T-23).
- Diálogo Nuevo cliente: el cliente queda registrado y elegido en la venta (T-24).
- Pruebas de aceptación (T-25).

### HU-05 · Emitir boleta con o sin DNI
- Migración V005: datos de la empresa, impresiones por comprobante y control
  de que gravada + IGV = total (T-26).
- Reglas de emisión: serie B001, boleta con DNI y DNI obligatorio en boletas
  de más de S/ 700.00 (T-27).
- Emisión en una sola transacción: correlativo, cabecera y detalle; si algo
  falla no queda nada a medias ni números saltados (T-28).
- Botón Emitir e imprimir (F10) con el tipo Boleta / Con DNI; sin conexión,
  la venta se conserva para reintentar (T-29).
- Pruebas de aceptación y emisión simultánea desde tres PC (T-30).

### HU-12 · Imprimir en la ticketera térmica
- Ticket de 80 mm con RUC, tipo y número, cliente, detalle, op. gravada,
  IGV, total y el importe en letras (T-31).
- Proyecto Condor.Impresion: comandos ESC/POS en la página de códigos 850 y
  envío directo al spooler de Windows, con aviso si la ticketera está
  apagada o sin papel (T-32).
- Impresión al emitir, aviso si falla, reimpresión con F11 y desde
  Historial (F5); vista previa en las PC sin ticketera (T-33).
- Pruebas de aceptación y guía de instalación de la ticketera (T-34).

### Transversales
- Datos de prueba de clientes, repositorios en memoria y ticketera falsa (T-35).

### Pendiente para el Sprint 3
- Enviar las boletas a la SUNAT por el PSE y guardar el CDR (HU-07): hoy
  quedan con estado PENDIENTE.

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
