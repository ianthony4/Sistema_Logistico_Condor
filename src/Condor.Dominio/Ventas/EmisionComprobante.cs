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
}

/// <summary>Campo de la pantalla al que apunta un error, para señalarlo.</summary>
public enum CampoEmision
{
    Detalle,
    Documento,
}

public sealed record ErrorEmision(CampoEmision Campo, string Mensaje);

/// <summary>Lo que hay en pantalla al presionar Emitir e imprimir.</summary>
public sealed record SolicitudEmision(TipoEmision Tipo, IReadOnlyList<DetalleComprobante> Detalle, Cliente? Cliente = null);

/// <summary>
/// Reglas para emitir un comprobante (HU-05, T-27): qué serie usa, qué
/// cliente exige y cómo se arman sus líneas y totales. No toca la base de
/// datos: el número correlativo lo asigna el repositorio al guardar.
/// </summary>
public static class EmisionComprobante
{
    public const string SerieBoleta = "B001";
    public const string SerieFactura = "F001";

    /// <summary>
    /// Si la boleta supera este importe, la SUNAT exige identificar al comprador
    /// (Reglamento de Comprobantes de Pago, R. 007-99/SUNAT, art. 8). Hasta
    /// este importe, la boleta puede ir sin DNI.
    /// </summary>
    public const decimal MontoBoletaIdentificada = 700m;

    public static string SerieDe(TipoEmision tipo) => tipo == TipoEmision.Factura ? SerieFactura : SerieBoleta;

    public static string TipoSunatDe(TipoEmision tipo) =>
        tipo == TipoEmision.Factura ? TipoComprobante.Factura : TipoComprobante.Boleta;

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

/// <summary>El comprobante no cumple las reglas de emisión.</summary>
public sealed class ComprobanteInvalidoException : Exception
{
    public ComprobanteInvalidoException(IReadOnlyList<ErrorEmision> errores)
        : base(string.Join(" ", errores.Select(e => e.Mensaje))) => Errores = errores;

    public IReadOnlyList<ErrorEmision> Errores { get; }
}
