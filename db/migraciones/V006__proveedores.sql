-- V006: proveedores de la ferretería (HU-04; T-37).
-- Las compras (HU-11, Sprint 5) se asociarán a esta tabla.

CREATE TABLE proveedor (
    id                bigserial     PRIMARY KEY,
    -- El proveedor factura: siempre tiene RUC, y uno solo por proveedor.
    ruc               char(11)      NOT NULL UNIQUE CHECK (ruc ~ '^(10|15|16|17|20)[0-9]{9}$'),
    razon_social      varchar(200)  NOT NULL CHECK (btrim(razon_social) <> ''),
    nombre_comercial  varchar(150),
    direccion         varchar(250),
    telefono          varchar(30),
    correo            varchar(120),
    contacto          varchar(120),
    activo            boolean       NOT NULL DEFAULT true,
    creado_en         timestamptz   NOT NULL DEFAULT now()
);

-- Búsqueda por parte de la razón social o del nombre comercial, sin
-- distinguir mayúsculas ni tildes, con la función de V003.
CREATE INDEX ix_proveedor_busqueda_trgm
    ON proveedor USING gin (texto_busqueda(razon_social || ' ' || coalesce(nombre_comercial, '')) gin_trgm_ops);
