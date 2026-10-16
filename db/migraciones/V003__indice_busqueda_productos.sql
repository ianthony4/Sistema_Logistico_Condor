-- V003: búsqueda de productos por nombre en menos de un segundo (HU-02, T-15).
--
-- La búsqueda es "contiene" (el vendedor escribe "esmal" o "rojo"), así que un
-- índice B-tree no sirve. Se usa un índice GIN de trigramas (pg_trgm) sobre el
-- nombre en minúsculas y sin tildes: "sintetico" encuentra "sintético".
-- pg_trgm y unaccent son extensiones "trusted": las puede crear el dueño de
-- la base de datos (condor_app) sin ser superusuario.

CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

-- unaccent() no es IMMUTABLE y no se puede indexar directamente; esta
-- envoltura fija el diccionario y sí lo es.
CREATE OR REPLACE FUNCTION texto_busqueda(texto text)
RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
AS $$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, texto)) $$;

CREATE INDEX IF NOT EXISTS ix_producto_nombre_trgm
    ON producto USING gin (texto_busqueda(nombre) gin_trgm_ops)
    WHERE activo;
