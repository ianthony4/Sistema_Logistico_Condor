-- V001: esquema inicial del Sistema de Ventas y Facturación (T-03).
-- Modelo descrito en docs/modelo_datos.md (T-01).

CREATE TABLE terminal (
    id              serial       PRIMARY KEY,
    nombre          varchar(60)  NOT NULL UNIQUE,
    registrado_en   timestamptz  NOT NULL DEFAULT now(),
    ultimo_acceso   timestamptz  NOT NULL DEFAULT now()
);

CREATE TABLE producto (
    id              bigserial     PRIMARY KEY,
    nombre          varchar(150)  NOT NULL CHECK (btrim(nombre) <> ''),
    precio          numeric(10,2) NOT NULL CHECK (precio > 0),
    unidad          varchar(10)   NOT NULL DEFAULT 'UND',
    codigo_barras   varchar(30)   UNIQUE,
    activo          boolean       NOT NULL DEFAULT true,
    creado_en       timestamptz   NOT NULL DEFAULT now()
);

-- Un mismo nombre no puede repetirse aunque cambien las mayúsculas (HU-01).
CREATE UNIQUE INDEX ux_producto_nombre ON producto (lower(nombre));

CREATE TABLE cliente (
    id                bigserial    PRIMARY KEY,
    tipo_documento    char(1)      NOT NULL CHECK (tipo_documento IN ('0', '1', '6')),
    numero_documento  varchar(11)  NOT NULL,
    nombre            varchar(200) NOT NULL,
    direccion         varchar(250),
    UNIQUE (tipo_documento, numero_documento)
);

CREATE TABLE serie (
    id                  serial   PRIMARY KEY,
    tipo_comprobante    char(2)  NOT NULL CHECK (tipo_comprobante IN ('01', '03', 'NV', 'PR')),
    codigo              char(4)  NOT NULL UNIQUE,
    ultimo_correlativo  bigint   NOT NULL DEFAULT 0
                                 CHECK (ultimo_correlativo BETWEEN 0 AND 99999999)
);

CREATE TABLE comprobante (
    id            bigserial     PRIMARY KEY,
    serie_id      int           NOT NULL REFERENCES serie (id),
    numero        bigint        NOT NULL,
    fecha         timestamptz   NOT NULL DEFAULT now(),
    cliente_id    bigint        REFERENCES cliente (id),
    terminal_id   int           NOT NULL REFERENCES terminal (id),
    usuario       varchar(20)   NOT NULL DEFAULT 'MAESTRO',
    op_gravada    numeric(12,2) NOT NULL,
    igv           numeric(12,2) NOT NULL,
    total         numeric(12,2) NOT NULL,
    estado_sunat  varchar(20)   NOT NULL DEFAULT 'PENDIENTE',
    -- Segunda barrera contra números repetidos entre terminales (HU-16).
    UNIQUE (serie_id, numero)
);

CREATE TABLE comprobante_detalle (
    id               bigserial     PRIMARY KEY,
    comprobante_id   bigint        NOT NULL REFERENCES comprobante (id) ON DELETE CASCADE,
    item             int           NOT NULL,
    producto_id      bigint        NOT NULL REFERENCES producto (id),
    descripcion      varchar(150)  NOT NULL,
    cantidad         numeric(10,3) NOT NULL CHECK (cantidad > 0),
    precio_unitario  numeric(10,2) NOT NULL CHECK (precio_unitario > 0),
    importe          numeric(12,2) NOT NULL,
    UNIQUE (comprobante_id, item)
);

CREATE INDEX ix_comprobante_fecha ON comprobante (fecha);
