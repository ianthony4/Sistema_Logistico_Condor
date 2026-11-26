-- V013: guías de remisión electrónicas remitente (HU-10; T-90).

-- La guía de remisión (tipo 09 del catálogo 01) numera en su propia serie T001.
ALTER TABLE serie DROP CONSTRAINT serie_tipo_comprobante_check;
ALTER TABLE serie ADD CONSTRAINT serie_tipo_comprobante_check
    CHECK (tipo_comprobante IN ('01', '03', 'NV', 'PR', '09'));

INSERT INTO serie (tipo_comprobante, codigo) VALUES ('09', 'T001')
ON CONFLICT (codigo) DO NOTHING;

-- Cada guía sustenta un traslado de mercadería: quién la recibe, de dónde a
-- dónde, cómo se lleva (empresa de transportes o vehículo propio) y qué
-- bienes. Se envía a la SUNAT por su API de guías: la respuesta llega con un
-- ticket y luego el CDR, que se guardan aquí mismo.
CREATE TABLE guia_remision (
    id                         bigserial      PRIMARY KEY,
    serie_id                   int            NOT NULL REFERENCES serie (id),
    numero                     bigint         NOT NULL,
    fecha                      timestamptz    NOT NULL DEFAULT now(),
    fecha_traslado             date           NOT NULL,
    motivo                     char(2)        NOT NULL CHECK (motivo IN ('01', '02', '04', '13')),
    descripcion_motivo         varchar(100),
    modalidad                  char(2)        NOT NULL CHECK (modalidad IN ('01', '02')),
    destinatario_tipo          char(1)        NOT NULL CHECK (destinatario_tipo IN ('1', '6')),
    destinatario_documento     varchar(11)    NOT NULL,
    destinatario_nombre        varchar(200)   NOT NULL,
    partida_ubigeo             char(6)        NOT NULL CHECK (partida_ubigeo ~ '^[0-9]{6}$'),
    partida_direccion          varchar(250)   NOT NULL,
    llegada_ubigeo             char(6)        NOT NULL CHECK (llegada_ubigeo ~ '^[0-9]{6}$'),
    llegada_direccion          varchar(250)   NOT NULL,
    peso_bruto_kg              numeric(12,3)  NOT NULL CHECK (peso_bruto_kg > 0),
    transportista_ruc          char(11),
    transportista_razon_social varchar(200),
    vehiculo_placa             varchar(8),
    conductor_dni              char(8),
    conductor_nombres          varchar(100),
    conductor_apellidos        varchar(100),
    conductor_licencia         varchar(10),
    comprobante_id             bigint         REFERENCES comprobante (id),
    observacion                varchar(250),
    terminal_id                int            NOT NULL REFERENCES terminal (id),
    impresiones                int            NOT NULL DEFAULT 0 CHECK (impresiones >= 0),
    -- Respuesta de la SUNAT (como comprobante_sunat, V007).
    estado_sunat               varchar(20)    NOT NULL DEFAULT 'PENDIENTE'
                               CHECK (estado_sunat IN ('PENDIENTE', 'ACEPTADO', 'OBSERVADO', 'RECHAZADO')),
    ticket                     varchar(60),
    codigo_sunat               varchar(10),
    descripcion_sunat          varchar(500),
    xml_firmado                text,
    cdr_zip                    bytea,
    intentos                   int            NOT NULL DEFAULT 0 CHECK (intentos >= 0),
    ultimo_intento             timestamptz,
    ultimo_error               varchar(500),
    UNIQUE (serie_id, numero),
    CHECK (fecha_traslado >= (fecha AT TIME ZONE 'America/Lima')::date),
    -- Público: la empresa de transportes. Privado: el vehículo y el conductor.
    CONSTRAINT ck_guia_transporte CHECK (
        (modalidad = '01' AND transportista_ruc IS NOT NULL AND transportista_razon_social IS NOT NULL)
        OR (modalidad = '02' AND vehiculo_placa IS NOT NULL AND conductor_dni IS NOT NULL
            AND conductor_licencia IS NOT NULL))
);

CREATE TABLE guia_detalle (
    id            bigserial      PRIMARY KEY,
    guia_id       bigint         NOT NULL REFERENCES guia_remision (id) ON DELETE CASCADE,
    item          int            NOT NULL,
    producto_id   bigint         NOT NULL REFERENCES producto (id),
    descripcion   varchar(150)   NOT NULL,
    cantidad      numeric(10,3)  NOT NULL CHECK (cantidad > 0),
    UNIQUE (guia_id, item)
);

CREATE INDEX ix_guia_fecha ON guia_remision (fecha);
CREATE INDEX ix_guia_pendiente ON guia_remision (id) WHERE estado_sunat = 'PENDIENTE';
