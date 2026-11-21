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

    // HU-08: la nota de venta es para el control interno.

    [Fact]
    public void Nota_de_venta_usa_NV01_y_no_es_comprobante_electronico()
    {
        var nota = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.NotaVenta, DetalleWireframe()));

        Assert.Equal("NV01", nota.SerieCodigo);
        Assert.Equal(TipoComprobante.NotaVenta, nota.TipoComprobante);
        Assert.Equal("Nota de venta", nota.NombreTipo);
        Assert.False(nota.EsElectronico);
        Assert.Equal(745.00m, nota.Total);
        Assert.Equal(nota.Total, nota.OpGravada + nota.Igv);
    }

    [Fact]
    public void Nota_de_venta_de_mas_de_700_soles_no_pide_DNI()
    {
        // La regla de los S/ 700 es de la boleta (SUNAT); la nota de venta no va a la SUNAT.
        Assert.Empty(EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.NotaVenta, DetalleWireframe())));
    }

    [Fact]
    public void Nota_de_venta_puede_llevar_cliente_con_DNI_o_con_RUC()
    {
        var conDni = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.NotaVenta, DetalleWireframe(), ConDni()));
        var conRuc = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.NotaVenta, DetalleWireframe(), ConRuc()));

        Assert.Equal(7, conDni.ClienteId);
        Assert.Equal(8, conRuc.ClienteId);
        Assert.All(new[] { conDni, conRuc }, n => Assert.Equal("NV01", n.SerieCodigo));
    }

    [Fact]
    public void Nota_de_venta_con_un_cliente_no_registrado_pide_registrarlo()
    {
        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.NotaVenta, DetalleWireframe(), ConDni(id: 0)));

        var error = Assert.Single(errores);
        Assert.Equal(CampoEmision.Documento, error.Campo);
        Assert.Contains("Nuevo cliente", error.Mensaje);
    }

    [Fact]
    public void Nota_de_venta_sin_productos_no_se_emite()
    {
        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.NotaVenta, new List<DetalleComprobante>()));

        Assert.Equal(CampoEmision.Detalle, Assert.Single(errores).Campo);
    }

    // HU-09: la proforma y su conversión en venta (T-70).

    private static Comprobante Proforma(Cliente? cliente = null, long id = 40)
    {
        var proforma = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Proforma, DetalleWireframe(), cliente));
        proforma.Id = id;
        proforma.Numero = 4;
        proforma.ValidaHasta = new DateOnly(2026, 12, 2);
        return proforma;
    }

    [Fact]
    public void Proforma_usa_PR01_puede_ir_sin_cliente_y_no_va_a_la_SUNAT()
    {
        var proforma = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Proforma, DetalleWireframe()));

        Assert.Equal("PR01", proforma.SerieCodigo);
        Assert.Equal(TipoComprobante.Proforma, proforma.TipoComprobante);
        Assert.Equal("Proforma", proforma.NombreTipo);
        Assert.False(proforma.EsElectronico);
        Assert.Null(proforma.ProformaId);
        Assert.Equal(745.00m, proforma.Total);
    }

    [Fact]
    public void La_proforma_vale_7_dias_contando_el_de_emision()
    {
        Assert.Equal(new DateOnly(2026, 12, 2), EmisionComprobante.VigenciaProforma(new DateOnly(2026, 11, 26)));
    }

    [Fact]
    public void La_fecha_de_la_proforma_se_cuenta_en_hora_de_Peru()
    {
        // 03:00 UTC del 27 son las 22:00 del 26 en Majes.
        Assert.Equal(new DateOnly(2026, 11, 26), EmisionComprobante.FechaPeru(new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void La_proforma_vence_al_dia_siguiente_de_su_vigencia()
    {
        var proforma = Proforma();

        Assert.False(proforma.Vencida(new DateOnly(2026, 12, 2)));
        Assert.True(proforma.Vencida(new DateOnly(2026, 12, 3)));
        Assert.False(new Comprobante { TipoComprobante = TipoComprobante.Boleta }.Vencida(new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Convertir_la_proforma_en_factura_lleva_el_enlace_a_la_proforma()
    {
        var proforma = Proforma(ConRuc());

        var factura = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Factura, proforma.Detalle, ConRuc(), proforma));

        Assert.Equal("F001", factura.SerieCodigo);
        Assert.Equal(40, factura.ProformaId);
        Assert.Equal(proforma.Total, factura.Total);
    }

    [Fact]
    public void Una_proforma_ya_convertida_no_se_vuelve_a_convertir()
    {
        var proforma = Proforma();
        proforma.ConvertidaEn = "B001-00000031";

        // Nota de venta: con S/ 745.00 una boleta sin DNI tendría además el error de los S/ 700.
        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.NotaVenta, proforma.Detalle, Proforma: proforma));

        var error = Assert.Single(errores);
        Assert.Equal(CampoEmision.Proforma, error.Campo);
        Assert.Contains("B001-00000031", error.Mensaje);
    }

    [Fact]
    public void La_proforma_no_se_convierte_en_otra_proforma()
    {
        var proforma = Proforma();

        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.Proforma, proforma.Detalle, Proforma: proforma));

        Assert.Equal(CampoEmision.Proforma, Assert.Single(errores).Campo);
    }

    [Fact]
    public void Solo_una_proforma_se_puede_convertir()
    {
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Boleta, new List<DetalleComprobante> { Linea(1, "Clavo", 1, 7.50m) }));

        var errores = EmisionComprobante.Errores(new SolicitudEmision(TipoEmision.Boleta, boleta.Detalle, Proforma: boleta));

        Assert.Contains("no es una proforma", Assert.Single(errores).Mensaje);
    }

    [Theory]
    [InlineData(TipoComprobante.Boleta, true)]
    [InlineData(TipoComprobante.Factura, true)]
    [InlineData(TipoComprobante.NotaVenta, false)]
    [InlineData(TipoComprobante.Proforma, false)]
    public void Solo_la_boleta_y_la_factura_van_a_la_SUNAT(string tipo, bool electronico)
    {
        Assert.Equal(electronico, new Comprobante { TipoComprobante = tipo }.EsElectronico);
    }
}
