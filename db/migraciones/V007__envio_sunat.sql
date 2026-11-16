-- V007: envío de boletas y facturas a la SUNAT y su CDR (HU-07; T-45).

-- Domicilio fiscal completo de la ferretería, como lo pide el XML UBL 2.1.
-- Valores del local en Majes (ubigeo INEI 040520); se corrigen con un UPDATE
-- si la ficha RUC dice otra cosa (docs/modelo_datos.md).
ALTER TABLE empresa
    ADD COLUMN ubigeo                  char(6)      NOT NULL DEFAULT '040520' CHECK (ubigeo ~ '^[0-9]{6}$'),
    ADD COLUMN departamento            varchar(30)  NOT NULL DEFAULT 'AREQUIPA',
    ADD COLUMN provincia               varchar(30)  NOT NULL DEFAULT 'CAYLLOMA',
    ADD COLUMN distrito                varchar(30)  NOT NULL DEFAULT 'MAJES',
    ADD COLUMN codigo_establecimiento  char(4)      NOT NULL DEFAULT '0000' CHECK (codigo_establecimiento ~ '^[0-9]{4}$');

-- Un registro por comprobante enviado (o que se intentó enviar): el XML
-- firmado, el CDR que devolvió la SUNAT y los intentos. El estado vive en
-- comprobante.estado_sunat (PENDIENTE, ACEPTADO, OBSERVADO o RECHAZADO).
CREATE TABLE comprobante_sunat (
    comprobante_id  bigint        PRIMARY KEY REFERENCES comprobante (id) ON DELETE CASCADE,
    nombre_archivo  varchar(60),
    hash            varchar(64),
    xml_firmado     text,
    cdr_zip         bytea,
    codigo          varchar(10),
    descripcion     varchar(500),
    notas           text,
    intentos        int           NOT NULL DEFAULT 0 CHECK (intentos >= 0),
    ultimo_intento  timestamptz,
    respondido_en   timestamptz,
    ultimo_error    varchar(500)
);

-- Los pendientes se reenvían (HU-17): se buscan rápido aunque haya miles de
-- comprobantes ya aceptados.
CREATE INDEX ix_comprobante_pendiente_sunat ON comprobante (id) WHERE estado_sunat = 'PENDIENTE';
