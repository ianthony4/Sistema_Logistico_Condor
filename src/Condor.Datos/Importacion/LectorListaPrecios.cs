using System.Globalization;
using System.Text;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Datos.Importacion;

/// <summary>Una fila leída de la lista de precios, válida o con su error.</summary>
public sealed record FilaListaPrecios(int Linea, Producto? Producto, string? Error)
{
    public bool EsValida => Producto is not null;
}

/// <summary>
/// Lee la lista de precios del local (CSV guardado desde Excel) para la carga
/// inicial del catálogo (T-19). Columnas: nombre; precio; unidad (opcional).
/// </summary>
/// <remarks>
/// Acepta separador punto y coma (Excel en español) o coma, precio con punto
/// o coma decimal y campos entre comillas. La primera fila es la cabecera.
/// </remarks>
public static class LectorListaPrecios
{
    public static IReadOnlyList<FilaListaPrecios> LeerArchivo(string ruta) =>
        Leer(File.ReadAllLines(ruta, Encoding.UTF8));

    public static IReadOnlyList<FilaListaPrecios> Leer(IReadOnlyList<string> lineas)
    {
        var filas = new List<FilaListaPrecios>();
        if (lineas.Count == 0)
            return filas;

        var separador = lineas[0].Contains(';') ? ';' : ',';
        for (var i = 1; i < lineas.Count; i++)
        {
            var numeroLinea = i + 1;
            if (string.IsNullOrWhiteSpace(lineas[i]))
                continue;

            var campos = Separar(lineas[i], separador);
            filas.Add(Interpretar(numeroLinea, campos));
        }
        return filas;
    }

    private static FilaListaPrecios Interpretar(int linea, IReadOnlyList<string> campos)
    {
        if (campos.Count < 2)
            return new(linea, null, "Faltan columnas: se esperaba nombre y precio.");

        var textoPrecio = campos[1].Replace("S/", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!decimal.TryParse(textoPrecio.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var precio))
            return new(linea, null, $"Precio no válido: \"{campos[1]}\".");

        var producto = ValidadorProducto.Normalizar(new Producto
        {
            Nombre = campos[0],
            Precio = precio,
            Unidad = campos.Count > 2 ? campos[2] : "UND",
        });

        var errores = ValidadorProducto.Errores(producto);
        return errores.Count == 0
            ? new(linea, producto, null)
            : new(linea, null, string.Join(" ", errores));
    }

    /// <summary>Separa una línea CSV respetando las comillas ("Tubo 1/2"", 3 m").</summary>
    public static IReadOnlyList<string> Separar(string linea, char separador)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;

        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];
            if (c == '"')
            {
                if (entreComillas && i + 1 < linea.Length && linea[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else
                {
                    entreComillas = !entreComillas;
                }
            }
            else if (c == separador && !entreComillas)
            {
                campos.Add(actual.ToString().Trim());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }
        campos.Add(actual.ToString().Trim());
        return campos;
    }
}
