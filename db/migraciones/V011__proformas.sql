-- V011: proformas con vigencia y su conversión en venta (HU-09; T-70).

-- La proforma (serie PR01) es una cotización para los clientes de obra: los
-- precios valen hasta valida_hasta. Cuando el cliente vuelve, la proforma se
-- convierte en boleta, factura o nota de venta sin reescribir el detalle, y
-- el comprobante emitido guarda de qué proforma salió.
ALTER TABLE comprobante
    ADD COLUMN valida_hasta  date,
    ADD COLUMN proforma_id   bigint REFERENCES comprobante (id);

-- Una proforma se convierte una sola vez, aunque dos PC lo intenten en el
-- mismo instante: la segunda choca con este índice y no se emite.
CREATE UNIQUE INDEX ux_comprobante_proforma ON comprobante (proforma_id) WHERE proforma_id IS NOT NULL;

-- Un CHECK no puede mirar la serie ni la proforma de origen: por eso es un
-- trigger. La proforma lleva su vigencia; solo una proforma vigente se
-- convierte, y nunca en otra proforma.
CREATE FUNCTION ck_proforma() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    tipo char(2) := (SELECT tipo_comprobante FROM serie WHERE id = NEW.serie_id);
BEGIN
    IF tipo = 'PR' AND NEW.valida_hasta IS NULL THEN
        RAISE EXCEPTION 'La proforma requiere su fecha de vigencia.'
            USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_proforma';
    END IF;

    IF NEW.proforma_id IS NOT NULL THEN
        IF tipo = 'PR' OR NOT EXISTS (SELECT 1 FROM comprobante p JOIN serie s ON s.id = p.serie_id
                                      WHERE p.id = NEW.proforma_id AND s.tipo_comprobante = 'PR') THEN
            RAISE EXCEPTION 'Solo una proforma se convierte, y en boleta, factura o nota de venta.'
                USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_proforma';
        END IF;
        IF TG_OP = 'INSERT' AND EXISTS (SELECT 1 FROM comprobante p WHERE p.id = NEW.proforma_id
                                        AND p.valida_hasta < (now() AT TIME ZONE 'America/Lima')::date) THEN
            RAISE EXCEPTION 'La proforma está vencida.'
                USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_proforma_vencida';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER tg_proforma
    BEFORE INSERT OR UPDATE OF serie_id, proforma_id, valida_hasta ON comprobante
    FOR EACH ROW EXECUTE FUNCTION ck_proforma();
