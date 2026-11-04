# Ticketera térmica (HU-12, T-34)

El ticket se imprime directo en la ticketera de 80 mm de la PC de caja, sin
diálogo de impresión: el programa envía los comandos ESC/POS al spooler de
Windows en modo RAW (`src/Condor.Impresion`).

## Instalación en cada PC con ticketera

1. Conectar la ticketera (USB) y encenderla.
2. Instalar el controlador del fabricante (Epson TM-T20, Xprinter XP-80 u
   otra compatible con ESC/POS). Si no hay controlador, en Windows usar
   **Agregar impresora → Genérico / Solo texto** en el puerto USB de la
   ticketera.
3. En **Configuración → Bluetooth y dispositivos → Impresoras y escáneres**
   anotar el nombre exacto de la impresora (por ejemplo `POS-80`).
4. En `condor.json`, junto a `CondorVentas.exe`, completar la sección:

   ```json
   "Ticketera": {
     "Nombre": "POS-80",
     "Columnas": 48,
     "EsperaSegundos": 10
   }
   ```

   - `Columnas`: 48 para papel de 80 mm con la fuente normal. Si las líneas
     salen cortadas, probar con 42.
   - `EsperaSegundos`: cuánto se espera a que salga el ticket antes de avisar
     que falló.

5. Abrir el programa, emitir una boleta de prueba y comprobar el ticket
   (ver la lista de abajo).

En las PC **sin** ticketera se deja `"Nombre": ""`: el ticket aparece en una
ventana de vista previa con el mismo texto que saldría en el papel.

## Si la ticketera falla

El comprobante **ya quedó guardado** con su número; solo falta el papel. El
programa muestra el motivo bajo el botón Emitir:

| Mensaje | Qué hacer |
|---|---|
| No se encontró la ticketera "…" | El nombre en `condor.json` no coincide con el de Windows. |
| Está apagada o desconectada | Encenderla o revisar el cable USB. |
| No tiene papel | Cambiar el rollo. |
| Tiene la tapa abierta o el papel atascado | Cerrar la tapa o retirar el papel trabado. |
| No imprimió | Revisar las tres anteriores; el trabajo se borró de la cola para que no salga tarde. |

Luego se reimprime con **F11** (último comprobante) o desde **Historial
(F5)**, donde los que no salieron dicen *Sin imprimir*.

## Prueba manual con la ticketera real (Definition of Done de HU-12)

Las pruebas automáticas usan una ticketera falsa. Con la real, en la PC de
caja:

1. Emitir una boleta sin DNI con tres productos. El ticket debe mostrar:
   - nombre y **RUC de la ferretería**, y su dirección;
   - **BOLETA DE VENTA ELECTRÓNICA** y el número `B001-…`;
   - fecha, hora y caja;
   - el **detalle** con cantidad, unidad, precio e importe;
   - **op. gravada, IGV 18 % y total**, y el total en letras;
   - las tildes y la ñ bien impresas, y el corte del papel al final.
2. Emitir una boleta con DNI: deben salir el nombre y el DNI del cliente.
3. Apagar la ticketera y emitir: debe aparecer el aviso, la boleta debe
   figurar en Historial como *Sin imprimir* y la venta siguiente debe poder
   hacerse.
4. Encender la ticketera y reimprimir desde Historial (F5): debe salir el
   ticket completo y la columna cambiar a *Sí*.
5. Retirar el rollo y repetir los pasos 3 y 4.
