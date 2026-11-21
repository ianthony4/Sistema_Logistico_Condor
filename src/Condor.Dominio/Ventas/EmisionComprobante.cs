using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Dominio.Ventas;

/// <summary>Comprobante que el vendedor elige en la pantalla Nueva venta.</summary>
public enum TipoEmision
{
    /// <summary>Boleta sin identificar al cliente (o con el cliente elegido, si lo hay).</summary>
    Boleta,

    /// <summary>Boleta que exige el DNI del cliente (HU-05, criterio 2).</summary>
    BoletaConDni,

    /// <summary>Factura con RUC (HU-06, Sprint 4).</summary>
    Factura,

    /// <summary>
    /// Nota de venta para el control interno (HU-08, Sprint 5): serie NV01,
    /// no es comprobante electrónico y no se envía a la SUNAT.
    /// </summary>
    NotaVenta,

    /// <summary>
    /// Proforma (cotización) para los clientes de obra (HU-09, Sprint 5):
    /// serie PR01, vale <see cref="EmisionComprobante.DiasVigenciaProforma"/>
    /// días y luego se convierte en venta.
    /// </summary>
    Proforma,
}

/// <summary>Campo de la pantalla al que apunta un error, para señalarlo.</summary>
public enum CampoEmision
{
    Detalle,
    Documento,

    /// <summary>La proforma que se está convirtiendo (HU-09).</summary>
    Proforma,
}

public sealed record ErrorEmision(CampoEmision Campo, string Mensaje);

/// <summary>
/// Lo que hay en pantalla al presionar Emitir e imprimir. Si la venta salió
/// de una proforma (HU-09), <paramref name="Proforma"/> es esa proforma.
/// </summary>
public sealed record SolicitudEmision(TipoEmision Tipo, IReadOnlyList<DetalleComprobante> Detalle, Cliente? Cliente = null,
    Comprobante? Proforma = null);

/// <summary>
/// Reglas para emitir un comprobante (HU-05, T-27): qué serie usa, qué
/// cliente exige y cómo se arman sus líneas y totales. No toca la base de
/// datos: el número correlativo lo asigna el repositorio al guardar. Desde
/// el Sprint 5 emite también la nota de venta (HU-08, T-66) y la proforma,
/// y convierte la proforma en venta (HU-09, T-70).
/// </summary>
public static class EmisionComprobante
{
    public const string SerieBoleta = "B001";
    public const string SerieFactura = "F001";
    public const string SerieNotaVenta = "NV01";
    public const string SerieProforma = "PR01";

    /// <summary>
    /// Días que valen los precios de una proforma, contando el de emisión:
    /// una proforma del lunes 23 vale hasta el domingo 29.
    /// </summary>
    public const int DiasVigenciaProforma = 7;

    /// <summary>Perú no cambia de hora en el año: siempre UTC-5.</summary>
    public static readonly TimeSpan HoraPeru = TimeSpan.FromHours(-5);

    /// <summary>Fecha en Perú de un instante (la vigencia de la proforma se cuenta en días de Majes).</summary>
    public static DateOnly FechaPeru(DateTimeOffset momento) => DateOnly.FromDateTime(momento.ToOffset(HoraPeru).DateTime);

    /// <summary>Vigencia de una proforma emitida el día <paramref name="emision"/>.</summary>
    public static DateOnly VigenciaProforma(DateOnly emision) => emision.AddDays(DiasVigenciaProforma - 1);

    /// <summary>
    /// Si la boleta supera este importe, la SUNAT exige identificar al comprador
    /// (Reglamento de Comprobantes de Pago, R. 007-99/SUNAT, art. 8). Hasta
    /// este importe, la boleta puede ir sin DNI.
    /// </summary>
    public const decimal MontoBoletaIdentificada = 700m;

    public static string SerieDe(TipoEmision tipo) => tipo switch
    {
        TipoEmision.Factura => SerieFactura,
        TipoEmision.NotaVenta => SerieNotaVenta,
        TipoEmision.Proforma => SerieProforma,
        _ => SerieBoleta,
    };

    /// <summary>Código del tipo de comprobante: 01 o 03 del catálogo de la SUNAT, NV o PR.</summary>
    public static string TipoSunatDe(TipoEmision tipo) => tipo switch
    {
        TipoEmision.Factura => TipoComprobante.Factura,
        TipoEmision.NotaVenta => TipoComprobante.NotaVenta,
        TipoEmision.Proforma => TipoComprobante.Proforma,
        _ => TipoComprobante.Boleta,
    };

    /// <summary>Errores que impiden emitir; vacía si se puede emitir.</summary>
    public static IReadOnlyList<ErrorEmision> Errores(SolicitudEmision solicitud)
    {
        var errores = new List<ErrorEmision>();
        var cliente = solicitud.Cliente;

        if (solicitud.Detalle.Count == 0)
            errores.Add(new(CampoEmision.Detalle, "Agregue al menos un producto antes de emitir."));

        switch (solicitud.Tipo)
        {
            case TipoEmision.BoletaConDni:
                if (cliente is null || cliente.TipoDocumento != TipoDocumento.Dni
                                    || !ValidadorDocumento.EsDniValido(cliente.NumeroDocumento))
                    errores.Add(new(CampoEmision.Documento, "Para la boleta con DNI digite el DNI del cliente (8 dígitos) y presione Buscar."));
                else if (cliente.Id <= 0)
                    errores.Add(new(CampoEmision.Documento, "Registre al cliente del DNI con + Nuevo cliente antes de emitir."));
                break;

            case TipoEmision.Factura:
                if (cliente is null || cliente.TipoDocumento != TipoDocumento.Ruc
                                    || !ValidadorDocumento.EsRucValido(cliente.NumeroDocumento))
                    errores.Add(new(CampoEmision.Documento, "La factura requiere un cliente con RUC: digítelo y presione Buscar."));
                else if (cliente.Id <= 0)
                    errores.Add(new(CampoEmision.Documento, "Registre al cliente del RUC con + Nuevo cliente antes de emitir."));
                break;

            case TipoEmision.Boleta:
                if (cliente is null && Total(solicitud.Detalle) > MontoBoletaIdentificada)
                    errores.Add(new(CampoEmision.Documento,
                        $"Las boletas de más de S/ {MontoBoletaIdentificada:0.00} deben llevar el DNI del cliente: elija Boleta con DNI."));
                else if (cliente is { Id: <= 0 })
                    errores.Add(new(CampoEmision.Documento, "Registre al cliente con + Nuevo cliente o quítelo de la venta."));
                break;

            case TipoEmision.NotaVenta or TipoEmision.Proforma:
                // HU-08 y HU-09: documentos internos; el cliente es opcional y
                // no hay tope de importe porque no van a la SUNAT.
                if (cliente is { Id: <= 0 })
                    errores.Add(new(CampoEmision.Documento,
                        $"Registre al cliente con + Nuevo cliente o quítelo de la {(solicitud.Tipo == TipoEmision.Proforma ? "proforma" : "nota de venta")}."));
                break;
        }

        // HU-09: la venta que sale de una proforma.
        if (solicitud.Proforma is { } proforma)
        {
            if (proforma.TipoComprobante != TipoComprobante.Proforma)
                errores.Add(new(CampoEmision.Proforma, $"{proforma.NumeroCompleto} no es una proforma."));
            else if (solicitud.Tipo == TipoEmision.Proforma)
                errores.Add(new(CampoEmision.Proforma, "La proforma se convierte en Boleta, Con DNI, Factura o Nota de venta, no en otra proforma."));
            else if (proforma.Convertida)
                errores.Add(new(CampoEmision.Proforma, $"La proforma {proforma.NumeroCompleto} ya se convirtió en {proforma.ConvertidaEn}."));
        }

        return errores;
    }

    /// <summary>
    /// Arma el comprobante listo para guardar: serie, cliente, líneas numeradas
    /// e importes y totales recalculados. Lanza ComprobanteInvalidoException si
    /// no cumple las reglas.
    /// </summary>
    public static Comprobante Preparar(SolicitudEmision solicitud)
    {
        var errores = Errores(solicitud);
        if (errores.Count > 0)
            throw new ComprobanteInvalidoException(errores);

        var comprobante = new Comprobante
        {
            TipoComprobante = TipoSunatDe(solicitud.Tipo),
            SerieCodigo = SerieDe(solicitud.Tipo),
            Cliente = solicitud.Cliente,
            ClienteId = solicitud.Cliente?.Id,
            ProformaId = solicitud.Proforma?.Id,
        };

        var item = 0;
        foreach (var linea in solicitud.Detalle)
        {
            comprobante.Detalle.Add(new DetalleComprobante
            {
                Item = ++item,
                ProductoId = linea.ProductoId,
                Descripcion = linea.Descripcion,
                Unidad = linea.Unidad,
                Cantidad = linea.Cantidad,
                PrecioUnitario = linea.PrecioUnitario,
                Importe = CalculadoraVenta.Importe(linea.Cantidad, linea.PrecioUnitario),
            });
        }

        var totales = CalculadoraVenta.Totales(comprobante.Detalle.Select(l => l.Importe));
        comprobante.OpGravada = totales.OpGravada;
        comprobante.Igv = totales.Igv;
        comprobante.Total = totales.Total;
        return comprobante;
    }

    private static decimal Total(IEnumerable<DetalleComprobante> detalle) =>
        CalculadoraVenta.Totales(detalle.Select(l => CalculadoraVenta.Importe(l.Cantidad, l.PrecioUnitario))).Total;
}

/// <summary>
/// Otra PC ya convirtió la proforma en venta (HU-09, criterio 3): la base
/// central admite una sola conversión por proforma.
/// </summary>
public sealed class ProformaYaConvertidaException : Exception
{
    public ProformaYaConvertidaException(string? numero, Exception? causa = null)
        : base($"La proforma {numero} ya se convirtió en venta (quizá en otra PC). Revise el Historial (F5).", causa)
    {
    }
}

/// <summary>El comprobante no cumple las reglas de emisión.</summary>
public sealed class ComprobanteInvalidoException : Exception
{
    public ComprobanteInvalidoException(IReadOnlyList<ErrorEmision> errores)
        : base(string.Join(" ", errores.Select(e => e.Mensaje))) => Errores = errores;

    public IReadOnlyList<ErrorEmision> Errores { get; }
}
