# Envío de comprobantes a la SUNAT (HU-07, T-49)

Desde la versión 0.3.0 cada boleta (y, con HU-06, cada factura) se envía a la
SUNAT en cuanto se emite. El proyecto `src/Condor.Sunat` sigue el mismo modelo
que Greenter / sunat-manager:

| Paso | Clase | Tarea |
|---|---|---|
| 1. XML UBL 2.1 del comprobante | `GeneradorXmlUbl` | T-41 |
| 2. Firma digital (XMLDSig, C14N, RSA-SHA1) | `FirmaXml`, `CertificadoDigital` | T-42 |
| 3. ZIP y envío por `sendBill` (SOAP + WS-Security) | `ClienteSunatSoap`, `ServicioEnvioSunat` | T-44 |
| 4. Lectura del CDR y estado | `LectorCdr` | T-43 |
| 5. Guardar estado, XML firmado y CDR | `EnvioSunatRepositorio` (tabla `comprobante_sunat`, V007) | T-45 |
| 6. Aviso en la pantalla de venta y en el Historial | `EnvioSunatViewModel`, `ComprobantesRecientesViewModel` | T-47, T-48 |

El envío corre en paralelo con la impresión: **el ticket sale aunque no haya
internet** y el vendedor puede seguir vendiendo.

## Estados

| Estado | Cuándo | Qué hacer |
|---|---|---|
| `PENDIENTE` | Sin internet, la SUNAT no respondió, falta configurar la SUNAT o el certificado falló. | Nada: se reenviará (HU-17, Sprint 4). El motivo se ve en el Historial (F5). |
| `ACEPTADO` | CDR con código 0. | — |
| `OBSERVADO` | CDR con código 0 y observaciones (4000 en adelante). | Corregir el dato en los siguientes comprobantes. |
| `RECHAZADO` | Código 2000 a 3999. | El comprobante no tiene validez: emitir otro corregido. |

## Configuración (sección `Sunat` de `condor.json`)

Solo la PC que tiene internet necesita la sección. Ejemplo para el **ambiente
beta** (pruebas del Sprint 3):

```jsonc
"Sunat": {
  "Ambiente": "Beta",
  "UsuarioSol": "MODDATOS",
  "ClaveSol": "moddatos",
  "Certificado": "",          // en beta, vacío = certificado de prueba autogenerado
  "ClaveCertificado": "",
  "EsperaSegundos": 30
}
```

En **producción**: `"Ambiente": "Produccion"`, el usuario secundario SOL creado
para facturación (sin el RUC delante), su clave y la ruta del certificado
digital `.pfx` con su clave. El pie de la pantalla muestra
«SUNAT beta», «SUNAT producción» o «SUNAT sin configurar».

\* El ambiente beta acepta cualquier RUC con el usuario `MODDATOS`. Si se le
envía muy seguido responde HTTP 401: el comprobante queda pendiente y basta con
reintentar unos segundos después.

## Prueba manual (Definition of Done de HU-07)

1. En la PC con internet, poner la sección `Sunat` de beta en `condor.json` y
   abrir el programa: el pie dice «SUNAT beta».
2. Emitir una boleta (F10). Bajo el botón aparece en verde
   «B001-…: Aceptada por la SUNAT.»
3. Desconectar el cable de red de internet (no el de la LAN) y emitir otra:
   el ticket sale y el aviso dice «quedó pendiente de envío a la SUNAT».
4. Abrir el Historial (F5): la primera dice «Aceptada» y la segunda
   «Pendiente»; al pasar el mouse se ve el motivo.
5. En `psql`: `SELECT comprobante_id, codigo, descripcion, intentos FROM comprobante_sunat ORDER BY 1 DESC LIMIT 5;`

\* Pendiente antes del piloto: tramitar el certificado digital de la
ferretería y el usuario secundario SOL para pasar a producción.
