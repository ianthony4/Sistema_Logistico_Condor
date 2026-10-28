using System.Text;

namespace Condor.Dominio.Impresion;

public enum Alineacion
{
    Izquierda,
    Centro,
    Derecha,
}

/// <summary>
/// Una línea del ticket. Negrita y Grande (doble alto) son sugerencias para la
/// ticketera; el texto nunca pasa del ancho del papel.
/// </summary>
public sealed record LineaTicket(string Texto, Alineacion Alineacion = Alineacion.Izquierda, bool Negrita = false, bool Grande = false);

/// <summary>
/// Ticket listo para imprimir, independiente de la impresora: lo arma
/// FormatoTicket y lo traduce a comandos la ticketera (Condor.Impresion) o la
/// vista previa.
/// </summary>
public sealed class Ticket
{
    public Ticket(int columnas)
    {
        if (columnas is < 24 or > 64)
            throw new ArgumentOutOfRangeException(nameof(columnas), "El ticket debe tener entre 24 y 64 columnas.");
        Columnas = columnas;
    }

    /// <summary>Caracteres por línea: 48 en papel de 80 mm con la fuente normal.</summary>
    public int Columnas { get; }

    public List<LineaTicket> Lineas { get; } = new();

    /// <summary>Texto plano con cada línea alineada, como saldría en el papel.</summary>
    public string ComoTexto()
    {
        var texto = new StringBuilder();
        foreach (var linea in Lineas)
            texto.AppendLine(Alinear(linea.Texto, linea.Alineacion, Columnas).TrimEnd());
        return texto.ToString();
    }

    public static string Alinear(string texto, Alineacion alineacion, int columnas)
    {
        if (texto.Length >= columnas)
            return texto[..columnas];
        return alineacion switch
        {
            Alineacion.Centro => texto.PadLeft((columnas + texto.Length) / 2).PadRight(columnas),
            Alineacion.Derecha => texto.PadLeft(columnas),
            _ => texto.PadRight(columnas),
        };
    }
}
