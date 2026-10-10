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
/// Cliente de un comprobante nominativo (HU-03, Sprint 2).
/// </summary>
public sealed class Cliente
{
    public long Id { get; set; }

    public TipoDocumento TipoDocumento { get; set; }

    public string NumeroDocumento { get; set; } = string.Empty;

    /// <summary>Nombre completo (DNI) o razón social (RUC).</summary>
    public string Nombre { get; set; } = string.Empty;

    public string? Direccion { get; set; }
}
