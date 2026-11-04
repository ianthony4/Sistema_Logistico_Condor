namespace Condor.Dominio.Entidades;

/// <summary>
/// Tipo de documento de identidad según el catálogo 06 de la SUNAT.
/// </summary>
public enum TipoDocumento
{
    SinDocumento = 0,
    Dni = 1,
    Ruc = 6,
}

/// <summary>
/// Cliente de un comprobante nominativo (HU-03).
/// </summary>
public sealed class Cliente
{
    public long Id { get; set; }

    public TipoDocumento TipoDocumento { get; set; }

    public string NumeroDocumento { get; set; } = string.Empty;

    /// <summary>Nombre completo (DNI) o razón social (RUC).</summary>
    public string Nombre { get; set; } = string.Empty;

    public string? Direccion { get; set; }

    /// <summary>Documento como se muestra e imprime: "DNI 45879632" o "RUC 20131312955".</summary>
    public string Documento => $"{Etiqueta(TipoDocumento)} {NumeroDocumento}".Trim();

    public static string Etiqueta(TipoDocumento tipo) => tipo switch
    {
        TipoDocumento.Dni => "DNI",
        TipoDocumento.Ruc => "RUC",
        _ => string.Empty,
    };

    public override string ToString() => $"{Nombre} ({Documento})";
}
