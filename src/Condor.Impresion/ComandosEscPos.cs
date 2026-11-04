using System.Text;
using Condor.Dominio.Impresion;

namespace Condor.Impresion;

/// <summary>
/// Traduce un Ticket a los comandos ESC/POS que entienden las ticketeras
/// térmicas de 80 mm (Epson TM-T20 y compatibles) (HU-12, T-32).
/// </summary>
/// <remarks>
/// Cada línea ya viene alineada con espacios desde el Ticket, así que no se
/// usa la alineación de la impresora: lo impreso es igual a la vista previa.
/// El texto va en la página de códigos 850 para que salgan las tildes, la ñ y
/// los signos ¡ ¿.
/// </remarks>
public static class ComandosEscPos
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private const byte SaltoLinea = 0x0A;

    /// <summary>Número de la página de códigos PC850 (Multilingual) en ESC t n.</summary>
    private const byte TablaPc850 = 2;

    /// <summary>Líneas en blanco antes del corte, para que el texto pase la cuchilla.</summary>
    public const byte LineasAntesDelCorte = 4;

    public static readonly byte[] Inicializar = { Esc, (byte)'@' };
    public static readonly byte[] TablaCaracteres = { Esc, (byte)'t', TablaPc850 };
    public static readonly byte[] NegritaSi = { Esc, (byte)'E', 1 };
    public static readonly byte[] NegritaNo = { Esc, (byte)'E', 0 };
    public static readonly byte[] DobleAlto = { Gs, (byte)'!', 0x01 };
    public static readonly byte[] TamanoNormal = { Gs, (byte)'!', 0x00 };
    public static readonly byte[] Avanzar = { Esc, (byte)'d', LineasAntesDelCorte };

    /// <summary>GS V 66 0: avanza hasta la cuchilla y hace un corte parcial.</summary>
    public static readonly byte[] Cortar = { Gs, (byte)'V', 66, 0 };

    private static readonly Encoding Pc850 = CrearCodificacion();

    public static byte[] Generar(Ticket ticket)
    {
        using var salida = new MemoryStream();
        Escribir(salida, Inicializar);
        Escribir(salida, TablaCaracteres);

        foreach (var linea in ticket.Lineas)
        {
            if (linea.Negrita)
                Escribir(salida, NegritaSi);
            if (linea.Grande)
                Escribir(salida, DobleAlto);

            var texto = Ticket.Alinear(linea.Texto, linea.Alineacion, ticket.Columnas).TrimEnd();
            Escribir(salida, Pc850.GetBytes(texto));
            salida.WriteByte(SaltoLinea);

            if (linea.Grande)
                Escribir(salida, TamanoNormal);
            if (linea.Negrita)
                Escribir(salida, NegritaNo);
        }

        Escribir(salida, Avanzar);
        Escribir(salida, Cortar);
        return salida.ToArray();
    }

    /// <summary>Texto en la página de códigos 850; lo que no exista en ella sale como '?'.</summary>
    public static byte[] Codificar(string texto) => Pc850.GetBytes(texto);

    private static Encoding CrearCodificacion()
    {
        // .NET 8 trae las páginas de códigos antiguas, pero hay que registrarlas.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(850, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

    private static void Escribir(Stream salida, byte[] bytes) => salida.Write(bytes, 0, bytes.Length);
}
