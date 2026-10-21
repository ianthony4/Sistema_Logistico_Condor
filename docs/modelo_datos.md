# Modelo de datos (T-01)

Base de datos central PostgreSQL 16 en la PC principal. Todas las terminales
leen y escriben sobre ella; por eso los correlativos se generan en la base de
datos y no en cada PC (HU-16).

## Diagrama entidad-relación

```mermaid
erDiagram
    TERMINAL ||--o{ COMPROBANTE : "emite"
    SERIE ||--o{ COMPROBANTE : "numera"
    CLIENTE |o--o{ COMPROBANTE : "recibe"
    COMPROBANTE ||--|{ COMPROBANTE_DETALLE : "contiene"
    PRODUCTO ||--o{ COMPROBANTE_DETALLE : "se vende en"

    TERMINAL {
        int id PK
        varchar nombre UK "nombre del equipo (PC-1, PC-2...)"
        timestamptz registrado_en
        timestamptz ultimo_acceso
    }
    PRODUCTO {
        bigint id PK
        varchar nombre UK "único sin distinguir mayúsculas"
        numeric precio "con IGV, mayor que 0"
        varchar unidad "UND, KG, M, GAL..."
        varchar codigo_barras "opcional (HU-13, futuro)"
        boolean activo
        timestamptz creado_en
    }
    CLIENTE {
        bigint id PK
        char tipo_documento "1 = DNI, 6 = RUC (catálogo 06 SUNAT)"
        varchar numero_documento UK
        varchar nombre "nombre o razón social"
        varchar direccion
    }
    SERIE {
        int id PK
        char tipo_comprobante "01 factura, 03 boleta, NV, PR"
        char codigo UK "B001, F001..."
        bigint ultimo_correlativo
    }
    COMPROBANTE {
        bigint id PK
        int serie_id FK
        bigint numero
        timestamptz fecha
        bigint cliente_id FK
        int terminal_id FK
        varchar usuario "siempre MAESTRO (HU-14)"
        numeric op_gravada
        numeric igv
        numeric total
        varchar estado_sunat
    }
    COMPROBANTE_DETALLE {
        bigint id PK
        bigint comprobante_id FK
        int item
        bigint producto_id FK
        varchar descripcion
        numeric cantidad
        numeric precio_unitario
        numeric importe
    }
```

## Decisiones

| Decisión | Motivo |
|---|---|
| Los precios se guardan **con IGV incluido**. | Es como la ferretería fija y anuncia sus precios; la operación gravada y el IGV se calculan al vender. |
| Sin columnas de stock en `producto`. | HU-01 lo excluye expresamente (sin control de inventario). |
| `serie.ultimo_correlativo` se incrementa con `UPDATE ... RETURNING`. | La fila queda bloqueada hasta el fin de la transacción: dos terminales no pueden obtener el mismo número (T-05). |
| `(serie_id, numero)` es único en `comprobante`. | Segunda barrera contra correlativos repetidos. |
| `comprobante_detalle.descripcion` y `precio_unitario` se copian del producto. | Si el producto cambia de precio, el comprobante emitido no cambia. |
| `terminal` guarda el nombre del equipo. | Cada documento registra desde qué PC se emitió (criterio de HU-14). |
| `usuario` es texto fijo `MAESTRO`. | Perfil único sin credenciales (RF-14). |

Las clases de `src/Condor.Dominio` reflejan estas tablas. Las sentencias SQL
están en `db/migraciones` (T-03).
