using System.Globalization;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;

namespace Condor.Dominio.Impresion;

/// <summary>
/// Arma el ticket de 80 mm de un comprobante (HU-12, T-31): RUC y datos de la
/// ferretería, tipo y número, fecha, cliente, detalle, op. gravada, IGV,
/// total y el importe en letras. La factura lleva además la razón social, el
/// RUC y la dirección del cliente y la forma de pago (HU-06, T-52). La nota
/// de venta solo lleva el total y avisa que es un documento interno (HU-08, T-67);
/// la proforma dice hasta cuándo valen sus precios (HU-09, T-72). Si el
/// comprobante ya salió impreso antes, el ticket dice que es una copia (HU-18, T-77).
/// </summary>
public static class FormatoTicket
{
    /// <summary>Ancho de un papel de 80 mm con la fuente normal de la ticketera (fuente A).</summary>
    public const int ColumnasPorDefecto = 48;

    /// <summary>Leyenda de la nota de venta: no sirve como comprobante de pago ante la SUNAT.</summary>
    public const string LeyendaNotaVenta =
        "Documento interno de control. No es comprobante de pago: si necesita boleta o factura, pídala en caja.";

    /// <summary>Marca de la reimpresión: el cliente ya se llevó el original (HU-18).</summary>
    public const string MarcaCopia = "*** COPIA - REIMPRESIÓN ***";

    /// <summary>Leyenda de la proforma: es una cotización, no un comprobante.</summary>
    public const string LeyendaProforma = "Cotización sin valor tributario. Al comprar, pida su boleta o factura.";

    private static readonly CultureInfo Numeros = CultureInfo.InvariantCulture;

    public static Ticket Generar(Empresa empresa, Comprobante comprobante, int columnas = ColumnasPorDefecto)
    {
        var t = new Ticket(columnas);
        var n = columnas;

        Cabecera(t, empresa);

        // Tipo y número del comprobante.
        Agregar(t, comprobante.NombreTipo.ToUpperInvariant(), Alineacion.Centro, negrita: true);
        t.Lineas.Add(new(comprobante.NumeroCompleto, Alineacion.Centro, Negrita: true, Grande: true));
        // Con 0 impresiones (la ticketera falló al emitir) el que sale ahora es el original.
        if (comprobante.Impresiones > 0)
            Agregar(t, MarcaCopia, Alineacion.Centro, negrita: true);
        Separador(t, '-');

        var fecha = comprobante.Fecha.ToOffset(EmisionComprobante.HoraPeru);
        // Con la cultura invariante, porque en un formato de fecha la "/" toma el
        // separador de Windows (en algunas PC es un espacio).
        var fechaTexto = fecha.ToString("dd/MM/yyyy HH:mm", Numeros);
        t.Lineas.Add(new(DosColumnas($"Fecha: {fechaTexto}", $"Caja: {comprobante.TerminalNombre ?? "-"}", n)));
        if (comprobante.TipoComprobante == TipoComprobante.Proforma && comprobante.ValidaHasta is { } vigencia)
            t.Lineas.Add(new($"Válida hasta: {Dia(vigencia)}", Negrita: true));
        if (comprobante.TipoComprobante == TipoComprobante.Factura && comprobante.Cliente is { } empresaCliente)
        {
            // HU-06: la factura identifica a la empresa que compra y dice
            // cómo se pagó (el XML la envía al contado, T-41).
            Agregar(t, $"Razón social: {empresaCliente.Nombre}");
            t.Lineas.Add(new($"RUC: {empresaCliente.NumeroDocumento}", Negrita: true));
            if (!string.IsNullOrWhiteSpace(empresaCliente.Direccion))
                Agregar(t, $"Dirección: {empresaCliente.Direccion}");
            t.Lineas.Add(new("Forma de pago: Contado"));
        }
        else if (comprobante.Cliente is { } cliente)
        {
            Agregar(t, $"Cliente: {cliente.Nombre}");
            t.Lineas.Add(new($"{Cliente.Etiqueta(cliente.TipoDocumento)}: {cliente.NumeroDocumento}"));
            if (!string.IsNullOrWhiteSpace(cliente.Direccion))
                Agregar(t, $"Dirección: {cliente.Direccion}");
        }
        else
        {
            t.Lineas.Add(new("Cliente: CLIENTES VARIOS"));
        }
        Separador(t, '-');

        // Detalle: descripción y, debajo, cantidad x precio e importe.
        t.Lineas.Add(new(DosColumnas("DESCRIPCIÓN", "IMPORTE", n), Negrita: true));
        Separador(t, '-');
        foreach (var linea in comprobante.Detalle)
        {
            Agregar(t, linea.Descripcion);
            var cantidad = $"  {linea.Cantidad.ToString("0.###", Numeros)} {linea.Unidad} x {Monto(linea.PrecioUnitario)}";
            t.Lineas.Add(new(DosColumnas(cantidad, Monto(linea.Importe), n)));
        }
        Separador(t, '-');

        // Totales, alineados a la derecha. La nota de venta no es un documento
        // tributario: no desglosa el IGV, solo el total cobrado.
        if (comprobante.TipoComprobante != TipoComprobante.NotaVenta)
        {
            t.Lineas.Add(new(Total("OP. GRAVADA", comprobante.OpGravada), Alineacion.Derecha));
            t.Lineas.Add(new(Total($"IGV {CalculadoraVenta.TasaIgv * 100:0}%", comprobante.Igv), Alineacion.Derecha));
        }
        t.Lineas.Add(new(Total("TOTAL", comprobante.Total), Alineacion.Derecha, Negrita: true));
        Separador(t, '-');
        Agregar(t, $"SON: {MontoEnLetras.Soles(comprobante.Total)}");
        Separador(t, '=');

        // Pie.
        if (comprobante.EsElectronico)
            Agregar(t, $"Representación impresa de la {comprobante.NombreTipo}.", Alineacion.Centro);
        else if (comprobante.TipoComprobante == TipoComprobante.NotaVenta)
            Agregar(t, LeyendaNotaVenta, Alineacion.Centro, negrita: true);
        else if (comprobante.TipoComprobante == TipoComprobante.Proforma)
        {
            if (comprobante.ValidaHasta is { } hasta)
                Agregar(t, $"Precios válidos hasta el {Dia(hasta)}.", Alineacion.Centro, negrita: true);
            Agregar(t, LeyendaProforma, Alineacion.Centro);
        }
        t.Lineas.Add(new("¡Gracias por su compra!", Alineacion.Centro, Negrita: true));
        return t;
    }

    /// <summary>
    /// Representación impresa de la guía de remisión remitente (HU-10, T-95):
    /// destinatario, traslado (motivo, fecha, peso), puntos de partida y
    /// llegada, transporte y los bienes, sin precios.
    /// </summary>
    public static Ticket GenerarGuia(Empresa empresa, GuiaRemision guia, int columnas = ColumnasPorDefecto)
    {
        var t = new Ticket(columnas);
        var n = columnas;

        Cabecera(t, empresa);
        Agregar(t, "GUÍA DE REMISIÓN ELECTRÓNICA REMITENTE", Alineacion.Centro, negrita: true);
        t.Lineas.Add(new(guia.NumeroCompleto, Alineacion.Centro, Negrita: true, Grande: true));
        if (guia.Impresiones > 0)
            Agregar(t, MarcaCopia, Alineacion.Centro, negrita: true);
        Separador(t, '-');

        var emision = guia.Fecha.ToOffset(EmisionComprobante.HoraPeru).ToString("dd/MM/yyyy HH:mm", Numeros);
        t.Lineas.Add(new(DosColumnas($"Emisión: {emision}", $"Caja: {guia.TerminalNombre ?? "-"}", n)));
        t.Lineas.Add(new($"Inicio del traslado: {Dia(guia.FechaTraslado)}", Negrita: true));
        Agregar(t, $"Motivo: {guia.DescripcionMotivo ?? MotivoTraslado.Nombre(guia.Motivo)}");
        if (guia.ComprobanteNumero is { } relacionado)
            Agregar(t, $"Comprobante: {(guia.ComprobanteTipo == TipoComprobante.Factura ? "Factura" : "Boleta")} {relacionado}");
        Separador(t, '-');

        Agregar(t, $"Destinatario: {guia.DestinatarioNombre}");
        t.Lineas.Add(new($"{Cliente.Etiqueta(guia.DestinatarioTipo)}: {guia.DestinatarioDocumento}"));
        Agregar(t, $"Partida ({guia.PartidaUbigeo}): {guia.PartidaDireccion}");
        Agregar(t, $"Llegada ({guia.LlegadaUbigeo}): {guia.LlegadaDireccion}");
        Separador(t, '-');

        Agregar(t, ModalidadTraslado.Nombre(guia.Modalidad), negrita: true);
        if (guia.EsPublico)
        {
            Agregar(t, $"Transportista: {guia.TransportistaRazonSocial}");
            t.Lineas.Add(new($"RUC: {guia.TransportistaRuc}"));
        }
        else
        {
            t.Lineas.Add(new($"Vehículo: {guia.VehiculoPlaca}"));
            Agregar(t, $"Conductor: {guia.ConductorNombres} {guia.ConductorApellidos}");
            t.Lineas.Add(new(DosColumnas($"DNI: {guia.ConductorDni}", $"Lic.: {guia.ConductorLicencia}", n)));
        }
        t.Lineas.Add(new($"Peso bruto: {guia.PesoBrutoKg.ToString("0.###", Numeros)} KG", Negrita: true));
        Separador(t, '-');

        // Bienes: descripción y, a la derecha, cantidad y unidad.
        t.Lineas.Add(new(DosColumnas("BIENES TRASLADADOS", "CANTIDAD", n), Negrita: true));
        Separador(t, '-');
        foreach (var linea in guia.Detalle)
        {
            var cantidad = $"{linea.Cantidad.ToString("0.###", Numeros)} {linea.Unidad}";
            var partes = Partir(linea.Descripcion, Math.Max(10, n - cantidad.Length - 1)).ToList();
            t.Lineas.Add(new(DosColumnas(partes[0], cantidad, n)));
            foreach (var resto in partes.Skip(1))
                t.Lineas.Add(new(resto));
        }
        if (!string.IsNullOrWhiteSpace(guia.Observacion))
        {
            Separador(t, '-');
            Agregar(t, $"Obs.: {guia.Observacion}");
        }
        Separador(t, '=');
        Agregar(t, "Representación impresa de la Guía de remisión electrónica remitente.", Alineacion.Centro);
        return t;
    }

    /// <summary>Cabecera común: nombre, razón social, RUC, dirección y teléfono de la ferretería.</summary>
    private static void Cabecera(Ticket t, Empresa empresa)
    {
        var nombre = (empresa.NombreComercial ?? empresa.RazonSocial).ToUpperInvariant();
        foreach (var parte in Partir(nombre, t.Columnas))
            t.Lineas.Add(new(parte, Alineacion.Centro, Negrita: true, Grande: true));
        if (empresa.NombreComercial is not null)
            Agregar(t, empresa.RazonSocial, Alineacion.Centro);
        t.Lineas.Add(new($"RUC {empresa.Ruc}", Alineacion.Centro, Negrita: true));
        Agregar(t, empresa.Direccion, Alineacion.Centro);
        if (!string.IsNullOrWhiteSpace(empresa.Telefono))
            Agregar(t, $"Tel. {empresa.Telefono}", Alineacion.Centro);
        Separador(t, '=');
    }

    /// <summary>Fecha con barras (29/11/2026), sin depender del separador de Windows.</summary>
    public static string Dia(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", Numeros);

    /// <summary>Monto con dos decimales y punto: 1234.50.</summary>
    public static string Monto(decimal monto) => monto.ToString("0.00", Numeros);

    /// <summary>Texto a la izquierda y valor pegado a la derecha; recorta la izquierda si no entra.</summary>
    public static string DosColumnas(string izquierda, string derecha, int columnas)
    {
        var espacio = columnas - derecha.Length - 1;
        if (izquierda.Length > espacio)
            izquierda = izquierda[..Math.Max(0, espacio)];
        return izquierda.PadRight(columnas - derecha.Length) + derecha;
    }

    /// <summary>Corta el texto en líneas de hasta <paramref name="columnas"/> caracteres, sin partir palabras si se puede.</summary>
    public static IEnumerable<string> Partir(string texto, int columnas)
    {
        var linea = string.Empty;
        foreach (var palabra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var resto = palabra;
            while (resto.Length > columnas)
            {
                if (linea.Length > 0)
                {
                    yield return linea;
                    linea = string.Empty;
                }
                yield return resto[..columnas];
                resto = resto[columnas..];
            }

            if (linea.Length == 0)
                linea = resto;
            else if (linea.Length + 1 + resto.Length <= columnas)
                linea += " " + resto;
            else
            {
                yield return linea;
                linea = resto;
            }
        }

        if (linea.Length > 0)
            yield return linea;
    }

    private static string Total(string etiqueta, decimal monto) => $"{etiqueta,-12} S/ {Monto(monto),11}";

    private static void Agregar(Ticket t, string texto, Alineacion alineacion = Alineacion.Izquierda, bool negrita = false)
    {
        foreach (var parte in Partir(texto, t.Columnas))
            t.Lineas.Add(new(parte, alineacion, negrita));
    }

    private static void Separador(Ticket t, char caracter) => t.Lineas.Add(new(new string(caracter, t.Columnas)));
}
