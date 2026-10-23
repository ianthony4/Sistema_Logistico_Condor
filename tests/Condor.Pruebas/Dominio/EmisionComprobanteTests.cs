using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;

namespace Condor.Pruebas.Dominio;

public class EmisionComprobanteTests
{
    private static DetalleComprobante Linea(long productoId, string descripcion, decimal cantidad, decimal precio) =>
        new() { ProductoId = productoId, Descripcion = descripcion, Cantidad = cantidad, PrecioUnitario = precio, Unidad = "UND" };

    // Detalle del wireframe: 10 bolsas de cemento, 8 varillas y 2 galones (S/ 745.00).
    private static List<DetalleComprobante> DetalleWireframe() => new()
    {
        Linea(1, "Cemento Portland tipo I x 42.5 kg", 10, 32.50m),
        Linea(2, "Fierro corrugado 1/2 in x 9 m", 8, 38.00m),
        Linea(3, "Esmalte sintético blanco 1 gal", 2, 58.00m),
    };

    private static Cliente ConDni(long id = 7) =>
        new() { Id = id, TipoDocumento = TipoDocumento.Dni, NumeroDocumento = "45879632", Nombre = "Juan Pérez" };

    private static Cliente ConRuc(long id = 8) =>
        new() { Id = id, TipoDocumento = TipoDocumento.Ruc, NumeroDocumento = "20131312955", Nombre = "Constructora" };

    [Fact]
    public void Boleta_sin_DNI_usa_la_serie_B001_y_recalcula_los_totales()
    {
        var detalle = new List<DetalleComprobante> { Linea(1, "Clavo 3 in x kg", 2, 7.50m), Linea(2, "Brocha 4 in", 1, 12.00m) };

        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Boleta, detalle));

        Assert.Equal(TipoComprobante.Boleta, boleta.TipoComprobante);
        Assert.Equal("B001", boleta.SerieCodigo);
        Assert.Null(boleta.ClienteId);
        Assert.Equal(new[] { 1, 2 }, boleta.Detalle.Select(l => l.Item));
        Assert.Equal(new[] { 15.00m, 12.00m }, boleta.Detalle.Select(l => l.Importe));
        Assert.Equal(27.00m, boleta.Total);
        Assert.Equal(boleta.Total, boleta.OpGravada + boleta.Igv);
    }

    [Fact]
    public void Boleta_con_DNI_lleva_al_cliente()
    {
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.BoletaConDni, DetalleWireframe(), ConDni()));

        Assert.Equal("B001", boleta.SerieCodigo);
        Assert.Equal(7, boleta.ClienteId);
        Assert.Equal("Juan Pérez", boleta.Cliente!.Nombre);
        Assert.Equal(745.00m, boleta.Total);
        Assert.Equal(631.36m, boleta.OpGravada);
        Assert.Equal(113.64m, boleta.Igv);
    }

    [Fact]
    public void Sin_productos_no_se_emite()
    {
        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.Boleta, new List<DetalleComprobante>()));

        var error = Assert.Single(errores);
        Assert.Equal(CampoEmision.Detalle, error.Campo);
    }

    [Theory]
    [InlineData(null)]          // DNI vacío
    [InlineData("4587963")]     // 7 dígitos
    [InlineData("20131312955")] // es un RUC
    public void Boleta_con_DNI_sin_un_DNI_de_8_digitos_senala_el_campo(string? numero)
    {
        var cliente = numero is null
            ? null
            : new Cliente
            {
                Id = 3,
                TipoDocumento = numero.Length == 11 ? TipoDocumento.Ruc : TipoDocumento.Dni,
                NumeroDocumento = numero,
                Nombre = "X",
            };

        var ex = Assert.Throws<ComprobanteInvalidoException>(() =>
            EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.BoletaConDni, DetalleWireframe(), cliente)));

        var error = Assert.Single(ex.Errores);
        Assert.Equal(CampoEmision.Documento, error.Campo);
        Assert.Contains("8 dígitos", error.Mensaje);
    }

    [Fact]
    public void Boleta_con_DNI_de_un_cliente_no_registrado_pide_registrarlo()
    {
        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.BoletaConDni, DetalleWireframe(), ConDni(id: 0)));

        Assert.Contains("Nuevo cliente", Assert.Single(errores).Mensaje);
    }

    [Fact]
    public void Boleta_de_mas_de_700_soles_sin_cliente_pide_el_DNI()
    {
        // 745.00 > 700.00
        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.Boleta, DetalleWireframe()));

        var error = Assert.Single(errores);
        Assert.Equal(CampoEmision.Documento, error.Campo);
        Assert.Contains("700.00", error.Mensaje);
    }

    [Fact]
    public void Boleta_de_exactamente_700_soles_puede_ir_sin_DNI()
    {
        var detalle = new List<DetalleComprobante> { Linea(1, "Cemento Portland tipo I x 42.5 kg", 20, 35.00m) };

        Assert.Empty(EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.Boleta, detalle)));
    }

    [Fact]
    public void Factura_usa_F001_y_exige_RUC()
    {
        var sinRuc = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.Factura, DetalleWireframe(), ConDni()));
        var factura = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Factura, DetalleWireframe(), ConRuc()));

        Assert.Contains("RUC", Assert.Single(sinRuc).Mensaje);
        Assert.Equal("F001", factura.SerieCodigo);
        Assert.Equal(TipoComprobante.Factura, factura.TipoComprobante);
        Assert.Equal("Factura electrónica", factura.NombreTipo);
    }
}
