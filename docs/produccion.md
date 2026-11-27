# Puesta en producción (T-99)

Pasos para que el sistema emita comprobantes con validez ante la SUNAT en la
ferretería. Se hacen **una sola vez**, en este orden, con el Encargado General.
Al abrir el programa en producción, una ventana avisa lo que todavía falte
(`ChequeoProduccion`).

## 1. Datos de la empresa

La migración V005 deja datos provisionales (RUC `20600000005`). Desde `psql`
en la PC principal, con los datos de la **ficha RUC**:

```sql
UPDATE empresa
SET ruc = '20XXXXXXXXX', razon_social = '...', nombre_comercial = '...',
    direccion = '...', telefono = '...',
    ubigeo = '040520', departamento = 'AREQUIPA', provincia = 'CAYLLOMA',
    distrito = 'MAJES', codigo_establecimiento = '0000'
WHERE id = 1;
```

## 2. Certificado digital

1. Tramitarlo con una entidad certificadora acreditada por INDECOPI (o el
   certificado gratuito de la SUNAT para quienes facturan desde SOL).
2. Guardar el `.pfx` en la carpeta del programa de la PC con internet (no se
   sube al repositorio) y anotar su clave en el sobre del Encargado General.
3. Registrarlo en SUNAT Operaciones en Línea: *Empresas > Comprobantes de pago
   > SEE - Del contribuyente > Registro de certificado digital*.

El programa avisa 30 días antes de que venza.

## 3. Usuario secundario SOL y credenciales de la API de guías

1. En SOL, *Administración de usuarios secundarios*: crear el usuario (por
   ejemplo `CONDOR01`) con los perfiles de **emisión electrónica** y
   **consulta de CDR**.
2. En SOL, *Credenciales de API SUNAT*: generar el **ClientId** y el
   **ClientSecret** para la guía de remisión electrónica (HU-10).
3. Darse de alta como emisor electrónico desde el sistema del contribuyente
   (SEE-Del contribuyente) y registrar las series `B001`, `F001` y `T001` si
   SOL lo pide.

## 4. `condor.json` de la PC con internet

```jsonc
"Sunat": {
  "Ambiente": "Produccion",
  "UsuarioSol": "CONDOR01",
  "ClaveSol": "********",
  "Certificado": "condor.pfx",
  "ClaveCertificado": "********",
  "EsperaSegundos": 30,
  "ReenvioMinutos": 5,
  "ClientId": "...",
  "ClientSecret": "..."
}
```

Las otras dos PC no necesitan la sección `Sunat`: emiten igual y la PC con
internet envía y reenvía lo pendiente.

## 5. Primera venta real

1. Generar un **respaldo** (F9) antes de empezar ([respaldo.md](respaldo.md)).
2. Abrir el programa: el pie debe decir «SUNAT producción» y no debe aparecer
   la ventana «Puesta en producción».
3. Emitir una boleta de S/ 1.00 y verificar que diga «Aceptada por la SUNAT».
   Consultarla en SOL (*Consulta de validez del comprobante*).
4. Emitir una guía de remisión de prueba y verificar que diga «Aceptada».

Si algo sale rechazado, el motivo se ve en el Historial (F5) o en Guías (F8)
al pasar el mouse por el estado; ver [sunat.md](sunat.md).
