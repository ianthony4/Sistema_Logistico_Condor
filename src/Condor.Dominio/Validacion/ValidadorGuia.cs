using System.Text.RegularExpressions;
using Condor.Dominio.Entidades;

namespace Condor.Dominio.Validacion;

/// <summary>Campo del diálogo Nueva guía al que apunta un error, para señalarlo.</summary>
public enum CampoGuia
{
    Destinatario,
    Traslado,
    Partida,
    Llegada,
    Transporte,
    Detalle,
}

public sealed record ErrorGuia(CampoGuia Campo, string Mensaje);

/// <summary>
/// Reglas de la guía de remisión remitente (HU-10, T-91), las que la SUNAT
/// revisa al recibirla: destinatario identificado, puntos de partida y llegada
/// con su ubigeo, motivo, fecha de traslado, peso y los datos del transporte
/// según la modalidad (RUC del transportista, o placa y conductor).
/// </summary>
public static partial class ValidadorGuia
{
    public const string SerieGuia = "T001";

    public static IReadOnlyList<ErrorGuia> Errores(GuiaRemision guia, DateOnly hoy)
    {
        var errores = new List<ErrorGuia>();

        // Destinatario.
        if (ValidadorDocumento.Error(guia.DestinatarioTipo, guia.DestinatarioDocumento) is { } errorDoc)
            errores.Add(new(CampoGuia.Destinatario, $"Destinatario: {errorDoc}"));
        if (string.IsNullOrWhiteSpace(guia.DestinatarioNombre))
            errores.Add(new(CampoGuia.Destinatario, "Escriba el nombre o la razón social del destinatario."));

        // Traslado.
        if (!MotivoTraslado.Todos.Contains(guia.Motivo))
            errores.Add(new(CampoGuia.Traslado, "Elija el motivo del traslado."));
        else if (guia.Motivo == MotivoTraslado.Otros && string.IsNullOrWhiteSpace(guia.DescripcionMotivo))
            errores.Add(new(CampoGuia.Traslado, "Con el motivo Otros, describa el motivo del traslado."));
        if (guia.FechaTraslado < hoy)
            errores.Add(new(CampoGuia.Traslado, "La fecha de traslado no puede ser anterior a hoy."));
        if (guia.PesoBrutoKg <= 0)
            errores.Add(new(CampoGuia.Traslado, "Indique el peso bruto total de la carga en kilogramos."));

        // Puntos de partida y llegada.
        if (!EsUbigeo(guia.PartidaUbigeo))
            errores.Add(new(CampoGuia.Partida, "El ubigeo del punto de partida tiene 6 dígitos (040520 es Majes)."));
        if (string.IsNullOrWhiteSpace(guia.PartidaDireccion))
            errores.Add(new(CampoGuia.Partida, "Escriba la dirección del punto de partida."));
        if (!EsUbigeo(guia.LlegadaUbigeo))
            errores.Add(new(CampoGuia.Llegada, "El ubigeo del punto de llegada tiene 6 dígitos."));
        if (string.IsNullOrWhiteSpace(guia.LlegadaDireccion))
            errores.Add(new(CampoGuia.Llegada, "Escriba la dirección del punto de llegada (la obra)."));
        else if (guia.LlegadaUbigeo == guia.PartidaUbigeo
                 && string.Equals(guia.LlegadaDireccion.Trim(), guia.PartidaDireccion.Trim(), StringComparison.OrdinalIgnoreCase))
            errores.Add(new(CampoGuia.Llegada, "El punto de llegada no puede ser el mismo de partida."));

        // Transporte.
        if (guia.Modalidad == ModalidadTraslado.Publico)
        {
            if (!ValidadorDocumento.EsRucValido(guia.TransportistaRuc))
                errores.Add(new(CampoGuia.Transporte, "Transporte público: digite el RUC de la empresa de transportes."));
            if (string.IsNullOrWhiteSpace(guia.TransportistaRazonSocial))
                errores.Add(new(CampoGuia.Transporte, "Transporte público: escriba la razón social del transportista."));
        }
        else if (guia.Modalidad == ModalidadTraslado.Privado)
        {
            if (!PatronPlaca().IsMatch(LimpiarPlaca(guia.VehiculoPlaca)))
                errores.Add(new(CampoGuia.Transporte, "Transporte privado: la placa del vehículo tiene 6 letras o números (ABC123)."));
            if (!ValidadorDocumento.EsDniValido(guia.ConductorDni))
                errores.Add(new(CampoGuia.Transporte, "Transporte privado: digite el DNI del conductor (8 dígitos)."));
            if (string.IsNullOrWhiteSpace(guia.ConductorNombres) || string.IsNullOrWhiteSpace(guia.ConductorApellidos))
                errores.Add(new(CampoGuia.Transporte, "Transporte privado: escriba los nombres y apellidos del conductor."));
            if (!PatronLicencia().IsMatch((guia.ConductorLicencia ?? string.Empty).Trim().ToUpperInvariant()))
                errores.Add(new(CampoGuia.Transporte, "Transporte privado: la licencia de conducir tiene de 9 a 10 letras o números (Q12345678)."));
        }
        else
        {
            errores.Add(new(CampoGuia.Transporte, "Elija transporte público o privado."));
        }

        // Bienes.
        if (guia.Detalle.Count == 0)
            errores.Add(new(CampoGuia.Detalle, "Agregue al menos un producto a la guía."));
        else if (guia.Detalle.Any(l => l.Cantidad <= 0))
            errores.Add(new(CampoGuia.Detalle, "Cada producto debe tener una cantidad mayor que cero."));

        return errores;
    }

    /// <summary>
    /// Deja la guía lista para guardar: serie T001, textos sin espacios
    /// sobrantes, placa y licencia en mayúsculas, los datos del transporte
    /// que no corresponden a la modalidad en null y las líneas numeradas.
    /// Lanza GuiaInvalidaException si no cumple las reglas.
    /// </summary>
    public static GuiaRemision Preparar(GuiaRemision guia, DateOnly hoy)
    {
        guia.SerieCodigo = SerieGuia;
        guia.DestinatarioDocumento = ValidadorDocumento.Limpiar(guia.DestinatarioDocumento);
        guia.DestinatarioNombre = guia.DestinatarioNombre.Trim();
        guia.PartidaDireccion = guia.PartidaDireccion.Trim();
        guia.LlegadaDireccion = guia.LlegadaDireccion.Trim();
        guia.DescripcionMotivo = guia.Motivo == MotivoTraslado.Otros ? Texto(guia.DescripcionMotivo) : null;
        guia.Observacion = Texto(guia.Observacion);
        if (guia.EsPublico)
        {
            guia.TransportistaRuc = ValidadorDocumento.Limpiar(guia.TransportistaRuc);
            guia.TransportistaRazonSocial = Texto(guia.TransportistaRazonSocial);
            guia.VehiculoPlaca = guia.ConductorDni = guia.ConductorNombres = guia.ConductorApellidos = guia.ConductorLicencia = null;
        }
        else
        {
            guia.VehiculoPlaca = LimpiarPlaca(guia.VehiculoPlaca);
            guia.ConductorDni = ValidadorDocumento.Limpiar(guia.ConductorDni);
            guia.ConductorNombres = Texto(guia.ConductorNombres);
            guia.ConductorApellidos = Texto(guia.ConductorApellidos);
            guia.ConductorLicencia = Texto(guia.ConductorLicencia)?.ToUpperInvariant();
            guia.TransportistaRuc = guia.TransportistaRazonSocial = null;
        }

        var errores = Errores(guia, hoy);
        if (errores.Count > 0)
            throw new GuiaInvalidaException(errores);

        var item = 0;
        foreach (var linea in guia.Detalle)
            linea.Item = ++item;
        return guia;
    }

    /// <summary>
    /// Guía para trasladar lo vendido en una boleta o factura (motivo venta):
    /// el destinatario es el cliente del comprobante y los bienes son sus
    /// líneas. Parte del domicilio de la ferretería; faltan la llegada, el
    /// transporte y el peso.
    /// </summary>
    public static GuiaRemision DesdeComprobante(Comprobante comprobante, Empresa empresa, DateOnly hoy)
    {
        var guia = new GuiaRemision
        {
            Motivo = MotivoTraslado.Venta,
            FechaTraslado = hoy,
            PartidaUbigeo = empresa.Ubigeo,
            PartidaDireccion = empresa.Direccion,
            ComprobanteId = comprobante.Id,
            ComprobanteTipo = comprobante.TipoComprobante,
            ComprobanteNumero = comprobante.NumeroCompleto,
        };
        if (comprobante.Cliente is { } cliente)
        {
            guia.DestinatarioTipo = cliente.TipoDocumento;
            guia.DestinatarioDocumento = cliente.NumeroDocumento;
            guia.DestinatarioNombre = cliente.Nombre;
            guia.LlegadaDireccion = cliente.Direccion ?? string.Empty;
        }
        foreach (var linea in comprobante.Detalle)
        {
            guia.Detalle.Add(new DetalleGuia
            {
                ProductoId = linea.ProductoId,
                Descripcion = linea.Descripcion,
                Unidad = linea.Unidad,
                Cantidad = linea.Cantidad,
            });
        }
        return guia;
    }

    public static bool EsUbigeo(string? ubigeo) => ubigeo is { Length: 6 } && ubigeo.All(char.IsAsciiDigit);

    /// <summary>Placa sin guion ni espacios, en mayúsculas: "abc-123" queda "ABC123".</summary>
    public static string LimpiarPlaca(string? placa) =>
        new((placa ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string? Texto(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    [GeneratedRegex("^[A-Z0-9]{6,8}$")]
    private static partial Regex PatronPlaca();

    [GeneratedRegex("^[A-Z0-9]{9,10}$")]
    private static partial Regex PatronLicencia();
}

/// <summary>La guía no cumple las reglas para emitirse.</summary>
public sealed class GuiaInvalidaException : Exception
{
    public GuiaInvalidaException(IReadOnlyList<ErrorGuia> errores)
        : base(string.Join(" ", errores.Select(e => e.Mensaje))) => Errores = errores;

    public IReadOnlyList<ErrorGuia> Errores { get; }
}
