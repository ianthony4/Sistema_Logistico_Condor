-- V005: emisión de boletas e impresión del ticket (HU-05, HU-12; T-26).

-- Datos de la ferretería que salen en la cabecera de cada ticket. Una sola
-- fila (id = 1) compartida por las tres PC.
CREATE TABLE empresa (
    id                int           PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    ruc               char(11)      NOT NULL,
    razon_social      varchar(200)  NOT NULL,
    nombre_comercial  varchar(200),
    direccion         varchar(250)  NOT NULL,
    telefono          varchar(30)
);

-- Valores provisionales: se reemplazan por los de la ficha RUC del local
-- antes del piloto (ver docs/modelo_datos.md).
INSERT INTO empresa (id, ruc, razon_social, nombre_comercial, direccion)
VALUES (1, '20600000005', 'FERRETERÍA CÓNDOR MAJES S.A.C.', 'Ferretería Cóndor',
        'Majes, Caylloma - Arequipa')
ON CONFLICT (id) DO NOTHING;

-- Cuántas veces se imprimió cada comprobante y cuándo fue la última: si la
-- ticketera falla, el comprobante queda registrado con 0 impresiones y se
-- reimprime después (HU-12).
ALTER TABLE comprobante
    ADD COLUMN impresiones  int          NOT NULL DEFAULT 0 CHECK (impresiones >= 0),
    ADD COLUMN impreso_en   timestamptz;

-- Los importes del comprobante deben cuadrar: gravada + IGV = total.
ALTER TABLE comprobante
    ADD CONSTRAINT ck_comprobante_totales CHECK (op_gravada + igv = total AND total > 0),
    ADD CONSTRAINT ck_comprobante_estado_sunat
        CHECK (estado_sunat IN ('PENDIENTE', 'ACEPTADO', 'OBSERVADO', 'RECHAZADO'));

-- Búsquedas de comprobantes por cliente (reimpresión e historial).
CREATE INDEX ix_comprobante_cliente ON comprobante (cliente_id);
