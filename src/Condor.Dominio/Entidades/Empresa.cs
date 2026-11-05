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

    // Domicilio fiscal tal como lo pide el XML de la SUNAT (HU-07). Por
    // defecto, el del local en Majes; se corrigen en la tabla empresa.

    /// <summary>Código de ubigeo del INEI (6 dígitos): 040520 es Majes, Caylloma, Arequipa.</summary>
    public string Ubigeo { get; set; } = "040520";

    public string Departamento { get; set; } = "AREQUIPA";

    public string Provincia { get; set; } = "CAYLLOMA";

    public string Distrito { get; set; } = "MAJES";

    /// <summary>Código del establecimiento anexo registrado en la SUNAT; 0000 es el domicilio fiscal.</summary>
    public string CodigoEstablecimiento { get; set; } = "0000";
}
