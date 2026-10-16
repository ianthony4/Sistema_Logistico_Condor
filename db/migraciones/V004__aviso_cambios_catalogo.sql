-- V004: avisa a las tres PC cuando cambia el catálogo (HU-01, T-13).
-- Cada terminal escucha el canal 'catalogo' (LISTEN) y, al recibir el aviso,
-- vuelve a consultar las sugerencias que tenga abiertas.

CREATE OR REPLACE FUNCTION avisar_cambio_catalogo()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    -- El aviso se entrega recién al confirmar la transacción.
    PERFORM pg_notify('catalogo', TG_OP);
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS tr_producto_aviso ON producto;

CREATE TRIGGER tr_producto_aviso
AFTER INSERT OR UPDATE OR DELETE ON producto
FOR EACH STATEMENT
EXECUTE FUNCTION avisar_cambio_catalogo();
