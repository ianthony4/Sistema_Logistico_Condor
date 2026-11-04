namespace Condor.Dominio.Entidades;

/// <summary>
/// Proveedor de la ferretería con sus datos comerciales (HU-04). Se identifica
/// por su RUC; las compras (HU-11) se asociarán a él.
/// </summary>
public sealed class Proveedor
{
    public long Id { get; set; }

    public string Ruc { get; set; } = string.Empty;

    public string RazonSocial { get; set; } = string.Empty;

    /// <summary>Nombre con el que se le conoce en el local (opcional).</summary>
    public string? NombreComercial { get; set; }

    public string? Direccion { get; set; }

    public string? Telefono { get; set; }

    public string? Correo { get; set; }

    /// <summary>Persona con la que se hacen los pedidos (vendedor del proveedor).</summary>
    public string? Contacto { get; set; }

    public bool Activo { get; set; } = true;

    public DateTimeOffset CreadoEn { get; set; }

    /// <summary>Nombre para mostrar: el comercial si lo tiene y, si no, la razón social.</summary>
    public string Nombre => string.IsNullOrWhiteSpace(NombreComercial) ? RazonSocial : NombreComercial;

    public override string ToString() => $"{RazonSocial} (RUC {Ruc})";
}
