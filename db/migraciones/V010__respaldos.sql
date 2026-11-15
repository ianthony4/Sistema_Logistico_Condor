-- V010: registro de los respaldos manuales de la base de datos (HU-15; T-60).

-- Cada respaldo que se genera desde la ventana Respaldo (F9) queda anotado:
-- así cualquiera de las tres PC sabe cuándo fue el último y avisa al abrir
-- el sistema si pasaron más de 24 horas (criterio 2). El archivo vive en el
-- disco externo; aquí solo se guarda dónde quedó y cuánto pesa.
CREATE TABLE respaldo (
    id            bigserial     PRIMARY KEY,
    archivo       varchar(260)  NOT NULL CHECK (btrim(archivo) <> ''),
    carpeta       varchar(260)  NOT NULL,
    bytes         bigint        NOT NULL CHECK (bytes > 0),
    iniciado_en   timestamptz   NOT NULL,
    terminado_en  timestamptz   NOT NULL DEFAULT now(),
    terminal_id   int           NOT NULL REFERENCES terminal (id),
    CHECK (terminado_en >= iniciado_en)
);

-- El aviso de las 24 horas solo lee el último.
CREATE INDEX ix_respaldo_terminado ON respaldo (terminado_en DESC);
