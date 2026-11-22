-- V012: compras a los proveedores (HU-11; T-79).

-- Cada compra es un comprobante (factura o boleta) que entregó un proveedor
-- registrado (V006), con su detalle de productos y lo que costaron. Sirve para
-- controlar los egresos; el sistema no lleva stock (HU-01).
CREATE TABLE compra (
    id              bigserial      PRIMARY KEY,
    proveedor_id    bigint         NOT NULL REFERENCES proveedor (id),
    tipo_documento  char(2)        NOT NULL CHECK (tipo_documento IN ('01', '03')),
    serie           char(4)        NOT NULL CHECK (serie ~ '^[A-Z0-9]{4}$'),
    numero          bigint         NOT NULL CHECK (numero BETWEEN 1 AND 99999999),
    fecha_emision   date           NOT NULL,
    registrada_en   timestamptz    NOT NULL DEFAULT now(),
    terminal_id     int            NOT NULL REFERENCES terminal (id),
    observacion     varchar(250),
    op_gravada      numeric(12,2)  NOT NULL,
    igv             numeric(12,2)  NOT NULL,
    total           numeric(12,2)  NOT NULL,
    CONSTRAINT ck_compra_totales CHECK (op_gravada + igv = total AND total > 0),
    -- El mismo comprobante del proveedor no se registra dos veces, aunque dos
    -- PC lo intenten a la vez (HU-11, criterio 2).
    CONSTRAINT ux_compra_documento UNIQUE (proveedor_id, tipo_documento, serie, numero)
);

CREATE TABLE compra_detalle (
    id              bigserial      PRIMARY KEY,
    compra_id       bigint         NOT NULL REFERENCES compra (id) ON DELETE CASCADE,
    item            int            NOT NULL,
    producto_id     bigint         NOT NULL REFERENCES producto (id),
    descripcion     varchar(150)   NOT NULL,
    cantidad        numeric(10,3)  NOT NULL CHECK (cantidad > 0),
    -- El costo puede tener más decimales que el precio (un clavo a S/ 0.0450).
    costo_unitario  numeric(12,4)  NOT NULL CHECK (costo_unitario > 0),
    importe         numeric(12,2)  NOT NULL,
    UNIQUE (compra_id, item)
);

-- La ventana Compras (F4) filtra por fecha del comprobante y por proveedor.
CREATE INDEX ix_compra_fecha ON compra (fecha_emision);
CREATE INDEX ix_compra_proveedor ON compra (proveedor_id, fecha_emision);
