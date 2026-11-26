namespace Condor.Dominio.Entidades;

/// <summary>Motivo del traslado (catálogo 20 de la SUNAT): los que usa la ferretería.</summary>
public static class MotivoTraslado
{
    public const string Venta = "01";
    public const string Compra = "02";
    public const string TrasladoEstablecimientos = "04";
    public const string Otros = "13";

    public static IReadOnlyList<string> Todos { get; } = new[] { Venta, Compra, TrasladoEstablecimientos, Otros };

    public static string Nombre(string? motivo) => motivo switch
    {
        Venta => "Venta",
        Compra => "Compra",
        TrasladoEstablecimientos => "Traslado entre establecimientos de la misma empresa",
        Otros => "Otros",
        _ => "Motivo no válido",
    };
}

/// <summary>Modalidad del traslado (catálogo 18 de la SUNAT).</summary>
public static class ModalidadTraslado
{
    /// <summary>Lo lleva una empresa de transporte: se indica su RUC.</summary>
    public const string Publico = "01";

    /// <summary>Lo lleva la ferretería en su propio vehículo: placa y conductor.</summary>
    public const string Privado = "02";

    public static string Nombre(string? modalidad) => modalidad switch
    {
        Publico => "Transporte público",
        Privado => "Transporte privado",
        _ => "Modalidad no válida",
    };
}

/// <summary>
/// Guía de remisión electrónica remitente (HU-10, T-91): sustenta el
/// traslado de la mercadería a las obras. Serie T001, se envía a la SUNAT por
/// su API de guías y se imprime en la ticketera.
/// </summary>
public sealed class GuiaRemision
{
    /// <summary>Código del catálogo 01 de la SUNAT para la guía de remisión remitente.</summary>
    public const string TipoSunat = "09";

    public long Id { get; set; }

    public int SerieId { get; set; }

    public string SerieCodigo { get; set; } = "T001";

    public long Numero { get; set; }

    /// <summary>Cuándo se emitió.</summary>
    public DateTimeOffset Fecha { get; set; }

    /// <summary>Día en que empieza el traslado; no antes del de emisión.</summary>
    public DateOnly FechaTraslado { get; set; }

    /// <summary>Catálogo 20 (<see cref="MotivoTraslado"/>).</summary>
    public string Motivo { get; set; } = MotivoTraslado.Venta;

    /// <summary>Obligatoria si el motivo es Otros.</summary>
    public string? DescripcionMotivo { get; set; }

    /// <summary>Catálogo 18 (<see cref="ModalidadTraslado"/>).</summary>
    public string Modalidad { get; set; } = ModalidadTraslado.Privado;

    // Destinatario: quien recibe la mercadería (la constructora, el vecino...).
    public TipoDocumento DestinatarioTipo { get; set; } = TipoDocumento.Ruc;

    public string DestinatarioDocumento { get; set; } = string.Empty;

    public string DestinatarioNombre { get; set; } = string.Empty;

    /// <summary>Ubigeo INEI de 6 dígitos del punto de partida (el local: 040520).</summary>
    public string PartidaUbigeo { get; set; } = string.Empty;

    public string PartidaDireccion { get; set; } = string.Empty;

    /// <summary>Ubigeo INEI de 6 dígitos del punto de llegada (la obra).</summary>
    public string LlegadaUbigeo { get; set; } = string.Empty;

    public string LlegadaDireccion { get; set; } = string.Empty;

    /// <summary>Peso bruto total de la carga, en kilogramos.</summary>
    public decimal PesoBrutoKg { get; set; }

    // Transporte público: la empresa de transportes.
    public string? TransportistaRuc { get; set; }

    public string? TransportistaRazonSocial { get; set; }

    // Transporte privado: el vehículo y el conductor de la ferretería.
    public string? VehiculoPlaca { get; set; }

    public string? ConductorDni { get; set; }

    public string? ConductorNombres { get; set; }

    public string? ConductorApellidos { get; set; }

    public string? ConductorLicencia { get; set; }

    /// <summary>Boleta o factura que sustenta el traslado (motivo venta); null si no hay.</summary>
    public long? ComprobanteId { get; set; }

    /// <summary>Tipo (01 o 03) y número del comprobante relacionado, para el XML y el ticket.</summary>
    public string? ComprobanteTipo { get; set; }

    public string? ComprobanteNumero { get; set; }

    public string? Observacion { get; set; }

    public int TerminalId { get; set; }

    public string? TerminalNombre { get; set; }

    /// <summary>PENDIENTE, ACEPTADO, OBSERVADO o RECHAZADO (como los comprobantes, HU-07).</summary>
    public string EstadoSunat { get; set; } = "PENDIENTE";

    /// <summary>Respuesta de la SUNAT o motivo del último intento; null si aún no se envió.</summary>
    public string? MensajeSunat { get; set; }

    /// <summary>Ticket que dio la SUNAT al recibirla; con él se consulta el CDR.</summary>
    public string? Ticket { get; set; }

    public int Impresiones { get; set; }

    public List<DetalleGuia> Detalle { get; } = new();

    public string NumeroCompleto => Serie.Formatear(SerieCodigo, Numero);

    public bool EsPublico => Modalidad == ModalidadTraslado.Publico;
}

/// <summary>Bien que se traslada: sin precios, solo qué y cuánto.</summary>
public sealed class DetalleGuia
{
    public int Item { get; set; }

    public long ProductoId { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public string Unidad { get; set; } = "UND";

    public decimal Cantidad { get; set; }
}
