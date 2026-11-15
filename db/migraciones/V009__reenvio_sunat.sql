-- V009: reenvío automático de los comprobantes pendientes (HU-17; T-55).

-- Varias PC pueden tener la SUNAT configurada. Antes de reenviar, cada una
-- aparta (reserva) los pendientes que va a mandar: así un comprobante no se
-- envía dos veces a la vez. Si la PC se apaga a mitad del reenvío, la
-- reserva vence sola y otra PC lo retoma.
ALTER TABLE comprobante_sunat
    ADD COLUMN reservado_hasta  timestamptz,
    ADD COLUMN reservado_por    varchar(60);

-- Los pendientes más antiguos primero: la SUNAT da 3 días para enviarlos.
CREATE INDEX ix_comprobante_pendiente_fecha ON comprobante (fecha) WHERE estado_sunat = 'PENDIENTE';
