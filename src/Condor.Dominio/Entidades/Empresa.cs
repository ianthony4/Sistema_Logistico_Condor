namespace Condor.Dominio.Entidades;

/// <summary>
/// Datos de la ferretería que emite los comprobantes: van en la cabecera del
/// ticket (HU-12). Viven en la base de datos central para que las tres PC
/// impriman lo mismo.
/// </summary>
public sealed class Empresa
{
    public string Ruc { get; set; } = string.Empty;

    public string RazonSocial { get; set; } = string.Empty;

    public string? NombreComercial { get; set; }

    public string Direccion { get; set; } = string.Empty;

    public string? Telefono { get; set; }
}
