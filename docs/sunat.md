# Envío de comprobantes y guías a la SUNAT (HU-07, HU-17, HU-10)

Desde la versión 0.3.0 cada boleta (y desde la 0.4.0, cada factura con RUC,
HU-06) se envía a la SUNAT en cuanto se emite; los que quedan pendientes por
falta de internet se reenvían solos (HU-17). El proyecto `src/Condor.Sunat` sigue el mismo modelo
que Greenter / sunat-manager:

| Paso | Clase | Tarea |
|---|---|---|
| 1. XML UBL 2.1 del comprobante | `GeneradorXmlUbl` | T-41 |
| 2. Firma digital (XMLDSig, C14N, RSA-SHA1) | `FirmaXml`, `CertificadoDigital` | T-42 |
| 3. ZIP y envío por `sendBill` (SOAP + WS-Security) | `ClienteSunatSoap`, `ServicioEnvioSunat` | T-44 |
| 4. Lectura del CDR y estado | `LectorCdr` | T-43 |
| 5. Guardar estado, XML firmado y CDR | `EnvioSunatRepositorio` (tabla `comprobante_sunat`, V007) | T-45 |
| 6. Aviso en la pantalla de venta y en el Historial | `EnvioSunatViewModel`, `ComprobantesRecientesViewModel` | T-47, T-48 |
| 7. Reenvío automático de los pendientes | `ReenvioPendientes`, `ReenvioSunatViewModel` (reserva en `comprobante_sunat`, V009) | T-55 a T-58 |
| 8. Consulta del CDR de un comprobante ya registrado (`getStatusCdr`) | `ClienteSunatSoap.ConsultarCdrAsync`, `ServicioEnvioSunat` | T-84 |
| 9. Guía de remisión: XML DespatchAdvice, API REST de guías (token, ticket y CDR) | `GeneradorXmlGuia`, `ClienteGreSunat`, `ServicioGuiaSunat`, `EnvioGuias` (tabla `guia_remision`, V013) | T-90 a T-94 |

### Guías de remisión (HU-10)

La guía de remisión remitente (serie `T001`) no va por `sendBill`: va por la
**API REST de guías** de la SUNAT. Primero se pide un token con el usuario y
la clave SOL más el `ClientId` y el `ClientSecret` de *Credenciales de API
SUNAT*; luego se envía el ZIP firmado, la SUNAT responde con un **ticket** y,
con él, se consulta el resultado. Si la SUNAT sigue procesando (código 98), la
guía queda pendiente con su ticket y la vuelta siguiente del reenvío lo
consulta sin volver a enviarla. Sin credenciales, la guía se emite e imprime
igual y queda pendiente. En beta se usa el entorno de pruebas
`https://gre-test.nubefact.com/v1` (el mismo de Greenter); en producción,
`api-seguridad.sunat.gob.pe` y `api-cpe.sunat.gob.pe`.

Las notas de venta (`NV01`, HU-08) y las proformas (`PR01`, HU-09) son
documentos internos: se imprimen pero **no se envían** a la SUNAT ni cuentan
como pendientes; en el Historial su columna SUNAT dice «No aplica».

El envío corre en paralelo con la impresión: **el ticket sale aunque no haya
internet** y el vendedor puede seguir vendiendo.

## Estados

| Estado | Cuándo | Qué hacer |
|---|---|---|
| `PENDIENTE` | Sin internet, la SUNAT no respondió, falta configurar la SUNAT o el certificado falló. | Nada: se reenvía solo (ver abajo). El motivo se ve en el Historial (F5). |
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
  "EsperaSegundos": 30,
  "ReenvioMinutos": 5         // 0 = esta PC no reenvía los pendientes
}
```

En **producción**: `"Ambiente": "Produccion"`, el usuario secundario SOL creado
para facturación (sin el RUC delante), su clave y la ruta del certificado
digital `.pfx` con su clave. El pie de la pantalla muestra
«SUNAT beta», «SUNAT producción» o «SUNAT sin configurar».

\* El ambiente beta acepta cualquier RUC con el usuario `MODDATOS`. Si se le
envía muy seguido responde HTTP 401: el comprobante queda pendiente y basta con
reintentar unos segundos después.

## Reenvío automático de los pendientes (HU-17)

Cada `ReenvioMinutos` (5 por defecto) la PC con la SUNAT configurada:

1. **Aparta** hasta 20 pendientes en la base central (`reservado_hasta`,
   `reservado_por`), del más antiguo al más reciente. Si otra PC también
   reenvía, cada una se lleva comprobantes distintos (`FOR UPDATE SKIP
   LOCKED`). Si la PC se apaga a mitad, la reserva vence a los 5 minutos.
2. Solo toma los emitidos hace más de 2 minutos (para no cruzarse con el
   envío al emitir) cuyo último intento también tiene más de 2 minutos; la
   espera crece con cada intento fallido, hasta 12 minutos.
3. Los envía uno por uno y guarda la respuesta. Si se corta internet, se
   detiene: los demás quedan libres para la próxima vuelta sin sumar intentos.

El pie de la pantalla de venta dice «N pendientes SUNAT»; se pone en rojo si
el más antiguo lleva más de 2 días, porque la SUNAT da **3 días calendario**
para enviar una boleta o factura. En el Historial (F5) el botón **Reenviar a
la SUNAT** hace una vuelta en el acto. Las PC sin la sección `Sunat` solo
muestran cuántos quedan.

### Comprobante que la SUNAT ya tenía (código 1033, T-84)

Si la SUNAT llegó a recibir un comprobante pero su respuesta se perdió (se
cortó internet justo después del envío), al reenviarlo responde 1033 «El
comprobante fue registrado previamente». Desde la versión 0.5.0 el sistema no
lo deja pendiente: pide su CDR con la operación `getStatusCdr` del servicio
`billConsultService` (con el mismo usuario y clave SOL) y guarda el estado que
dice (aceptado, observado o rechazado). Si la SUNAT no entrega el CDR (códigos
0011 o 0012) queda pendiente con ese motivo.

\* El ambiente beta **no tiene** servicio de consulta: allí un 1033 queda
pendiente con el aviso «En beta no hay consulta de CDR»; se verifica en la
consulta de comprobantes de SUNAT Operaciones en Línea. En producción se usa
`https://e-factura.sunat.gob.pe/ol-it-wsconscpegem/billConsultService`.

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

La prueba manual del reenvío automático (HU-17) está en
[pruebas.md](pruebas.md#reenvío-automático-a-la-sunat-t-59).

\* Pendiente antes del piloto: tramitar el certificado digital de la
ferretería y el usuario secundario SOL para pasar a producción.
