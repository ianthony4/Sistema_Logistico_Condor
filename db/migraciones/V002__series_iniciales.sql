-- V002: series de numeración con las que empieza el local (T-03).
-- Boleta B001, factura F001, nota de venta NV01 y proforma PR01.

INSERT INTO serie (tipo_comprobante, codigo) VALUES
    ('03', 'B001'),
    ('01', 'F001'),
    ('NV', 'NV01'),
    ('PR', 'PR01')
ON CONFLICT (codigo) DO NOTHING;
