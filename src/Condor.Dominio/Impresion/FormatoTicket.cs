using System.Globalization;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;

namespace Condor.Dominio.Impresion;

/// <summary>
/// Arma el ticket de 80 mm de un comprobante (HU-12, T-31): RUC y datos de la
/// ferretería, tipo y número, fecha, cliente, detalle, op. gravada, IGV,
/// total y el importe en letras.
/// </summary>
public static class FormatoTicket
{
    /// <summary>Ancho de un papel de 80 mm con la fuente normal de la ticketera (fuente A).</summary>
    public const int ColumnasPorDefecto = 48;

    /// <summary>Perú no cambia de hora en el año: la hora del ticket es siempre UTC-5.</summary>
    private static readonly TimeSpan HoraPeru = TimeSpan.FromHours(-5);

    private static readonly CultureInfo Numeros = CultureInfo.InvariantCulture;

    public static Ticket Generar(Empresa empresa, Comprobante comprobante, int columnas = ColumnasPorDefecto)
    {
        var t = new Ticket(columnas);
        var n = columnas;

        // Cabecera: la ferretería.
        var nombre = (empresa.NombreComercial ?? empresa.RazonSocial).ToUpperInvariant();
        foreach (var parte in Partir(nombre, n))
            t.Lineas.Add(new(parte, Alineacion.Centro, Negrita: true, Grande: true));
        if (empresa.NombreComercial is not null)
            Agregar(t, empresa.RazonSocial, Alineacion.Centro);
        t.Lineas.Add(new($"RUC {empresa.Ruc}", Alineacion.Centro, Negrita: true));
        Agregar(t, empresa.Direccion, Alineacion.Centro);
        if (!string.IsNullOrWhiteSpace(empresa.Telefono))
            Agregar(t, $"Tel. {empresa.Telefono}", Alineacion.Centro);
        Separador(t, '=');

        // Tipo y número del comprobante.
        Agregar(t, comprobante.NombreTipo.ToUpperInvariant(), Alineacion.Centro, negrita: true);
        t.Lineas.Add(new(comprobante.NumeroCompleto, Alineacion.Centro, Negrita: true, Grande: true));
        Separador(t, '-');

        var fecha = comprobante.Fecha.ToOffset(HoraPeru);
        // Con la cultura invariante, porque en un formato de fecha la "/" toma el
        // separador de Windows (en algunas PC es un espacio).
        var fechaTexto = fecha.ToString("dd/MM/yyyy HH:mm", Numeros);
        t.Lineas.Add(new(DosColumnas($"Fecha: {fechaTexto}", $"Caja: {comprobante.TerminalNombre ?? "-"}", n)));
        if (comprobante.Cliente is { } cliente)
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

        // Totales, alineados a la derecha.
        t.Lineas.Add(new(Total("OP. GRAVADA", comprobante.OpGravada), Alineacion.Derecha));
        t.Lineas.Add(new(Total($"IGV {CalculadoraVenta.TasaIgv * 100:0}%", comprobante.Igv), Alineacion.Derecha));
        t.Lineas.Add(new(Total("TOTAL", comprobante.Total), Alineacion.Derecha, Negrita: true));
        Separador(t, '-');
        Agregar(t, $"SON: {MontoEnLetras.Soles(comprobante.Total)}");
        Separador(t, '=');

        // Pie.
        if (comprobante.TipoComprobante is TipoComprobante.Boleta or TipoComprobante.Factura)
            Agregar(t, $"Representación impresa de la {comprobante.NombreTipo}.", Alineacion.Centro);
        t.Lineas.Add(new("¡Gracias por su compra!", Alineacion.Centro, Negrita: true));
        return t;
    }

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
