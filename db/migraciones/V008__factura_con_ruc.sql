-- V008: la factura exige un cliente con RUC (HU-06; T-53).

-- La pantalla de venta ya no deja emitir una factura sin RUC, pero la base
-- central lo comprueba también: así ninguna PC con una versión vieja del
-- programa, ni un INSERT a mano, puede dejar una factura que la SUNAT
-- rechazaría (código 2800). Un CHECK no puede mirar otras tablas, por eso
-- es un trigger.
CREATE FUNCTION ck_factura_con_ruc() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF (SELECT tipo_comprobante FROM serie WHERE id = NEW.serie_id) = '01'
       AND NOT EXISTS (SELECT 1 FROM cliente
                       WHERE id = NEW.cliente_id AND tipo_documento = '6') THEN
        RAISE EXCEPTION 'La factura requiere un cliente con RUC.'
            USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_factura_con_ruc';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER tg_factura_con_ruc
    BEFORE INSERT OR UPDATE OF serie_id, cliente_id ON comprobante
    FOR EACH ROW EXECUTE FUNCTION ck_factura_con_ruc();
