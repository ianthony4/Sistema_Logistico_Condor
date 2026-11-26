using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Dominio.Ventas;

namespace Condor.Pruebas.Dominio;

/// <summary>Reglas de la guía de remisión remitente (HU-10, T-91).</summary>
public class ValidadorGuiaTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 26);

    /// <summary>Guía de 20 bolsas de cemento a una obra en El Pedregal, en la camioneta del local.</summary>
    private static GuiaRemision GuiaPrivada() => new()
    {
        FechaTraslado = Hoy,
        Motivo = MotivoTraslado.Venta,
        Modalidad = ModalidadTraslado.Privado,
        DestinatarioTipo = TipoDocumento.Ruc,
        DestinatarioDocumento = "20131312955",
        DestinatarioNombre = " Constructora Valle de Majes S.A.C. ",
        PartidaUbigeo = "040520",
        PartidaDireccion = "Majes, Caylloma - Arequipa",
        LlegadaUbigeo = "040520",
        LlegadaDireccion = "Obra Av. Principal 450, El Pedregal",
        PesoBrutoKg = 850m,
        VehiculoPlaca = "v1a-952",
        ConductorDni = "45879632",
        ConductorNombres = "Juan",
        ConductorApellidos = "Pérez Quispe",
        ConductorLicencia = "q45879632",
        Detalle = { new() { ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS", Cantidad = 20 } },
    };

    [Fact]
    public void Una_guia_en_transporte_privado_completa_es_valida_y_se_normaliza()
    {
        var guia = ValidadorGuia.Preparar(GuiaPrivada(), Hoy);

        Assert.Equal("T001", guia.SerieCodigo);
        Assert.Equal("V1A952", guia.VehiculoPlaca);
        Assert.Equal("Q45879632", guia.ConductorLicencia);
        Assert.Equal("Constructora Valle de Majes S.A.C.", guia.DestinatarioNombre);
        Assert.Equal(1, guia.Detalle[0].Item);
        Assert.Null(guia.TransportistaRuc);
    }

    [Fact]
    public void En_transporte_publico_pide_el_RUC_del_transportista_y_descarta_el_vehiculo()
    {
        var guia = GuiaPrivada();
        guia.Modalidad = ModalidadTraslado.Publico;

        var errores = ValidadorGuia.Errores(guia, Hoy);
        guia.TransportistaRuc = "20100137390";
        guia.TransportistaRazonSocial = "Transportes Arequipa S.A.";
        ValidadorGuia.Preparar(guia, Hoy);

        Assert.All(errores, e => Assert.Equal(CampoGuia.Transporte, e.Campo));
        Assert.Contains(errores, e => e.Mensaje.Contains("RUC de la empresa de transportes"));
        Assert.Null(guia.VehiculoPlaca);
        Assert.Null(guia.ConductorDni);
    }

    [Theory]
    [InlineData("AB12", "placa")]
    [InlineData("", "placa")]
    public void En_transporte_privado_la_placa_es_obligatoria(string placa, string mensaje)
    {
        var guia = GuiaPrivada();
        guia.VehiculoPlaca = placa;

        Assert.Contains(mensaje, Assert.Single(ValidadorGuia.Errores(guia, Hoy)).Mensaje);
    }

    [Fact]
    public void En_transporte_privado_pide_DNI_nombres_y_licencia_del_conductor()
    {
        var guia = GuiaPrivada();
        guia.ConductorDni = "123";
        guia.ConductorApellidos = "";
        guia.ConductorLicencia = "Q1";

        var errores = ValidadorGuia.Errores(guia, Hoy);

        Assert.Equal(3, errores.Count);
        Assert.Contains(errores, e => e.Mensaje.Contains("DNI del conductor"));
        Assert.Contains(errores, e => e.Mensaje.Contains("apellidos"));
        Assert.Contains(errores, e => e.Mensaje.Contains("licencia"));
    }

    [Fact]
    public void El_destinatario_debe_tener_documento_valido_y_nombre()
    {
        var guia = GuiaPrivada();
        guia.DestinatarioDocumento = "20131312950"; // dígito verificador mal
        guia.DestinatarioNombre = "";

        var errores = ValidadorGuia.Errores(guia, Hoy);

        Assert.Equal(2, errores.Count);
        Assert.All(errores, e => Assert.Equal(CampoGuia.Destinatario, e.Campo));
    }

    [Theory]
    [InlineData("0405", "040520")]
    [InlineData("040520", "4052A0")]
    public void Los_ubigeos_tienen_6_digitos(string partida, string llegada)
    {
        var guia = GuiaPrivada();
        guia.PartidaUbigeo = partida;
        guia.LlegadaUbigeo = llegada;

        Assert.Contains(ValidadorGuia.Errores(guia, Hoy), e => e.Mensaje.Contains("6 dígitos"));
    }

    [Fact]
    public void La_llegada_no_puede_ser_el_mismo_punto_de_partida()
    {
        var guia = GuiaPrivada();
        guia.LlegadaDireccion = "majes, caylloma - arequipa";

        Assert.Equal(CampoGuia.Llegada, Assert.Single(ValidadorGuia.Errores(guia, Hoy)).Campo);
    }

    [Fact]
    public void El_traslado_no_puede_ser_antes_de_hoy_y_lleva_peso()
    {
        var guia = GuiaPrivada();
        guia.FechaTraslado = Hoy.AddDays(-1);
        guia.PesoBrutoKg = 0;

        var errores = ValidadorGuia.Errores(guia, Hoy);

        Assert.Equal(2, errores.Count);
        Assert.Contains(errores, e => e.Mensaje.Contains("anterior a hoy"));
        Assert.Contains(errores, e => e.Mensaje.Contains("peso"));
    }

    [Fact]
    public void Con_el_motivo_Otros_hay_que_describirlo()
    {
        var guia = GuiaPrivada();
        guia.Motivo = MotivoTraslado.Otros;

        var sinDescripcion = ValidadorGuia.Errores(guia, Hoy);
        guia.DescripcionMotivo = "Préstamo de andamios a la obra";

        Assert.Contains("describa", Assert.Single(sinDescripcion).Mensaje);
        Assert.Empty(ValidadorGuia.Errores(guia, Hoy));
        Assert.Equal("Otros", MotivoTraslado.Nombre(guia.Motivo));
    }

    [Fact]
    public void Sin_productos_no_se_emite()
    {
        var guia = GuiaPrivada();
        guia.Detalle.Clear();

        var ex = Assert.Throws<GuiaInvalidaException>(() => ValidadorGuia.Preparar(guia, Hoy));

        Assert.Equal(CampoGuia.Detalle, Assert.Single(ex.Errores).Campo);
    }

    [Fact]
    public void Desde_una_factura_toma_el_cliente_y_los_productos()
    {
        var cliente = new Cliente
        {
            Id = 2, TipoDocumento = TipoDocumento.Ruc, NumeroDocumento = "20131312955",
            Nombre = "Constructora Valle de Majes S.A.C.", Direccion = "Av. Principal 450, Majes",
        };
        var detalle = new List<DetalleComprobante>
        {
            new() { ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS", Cantidad = 20, PrecioUnitario = 32.50m },
            new() { ProductoId = 2, Descripcion = "Fierro corrugado 1/2 in x 9 m", Unidad = "VAR", Cantidad = 10, PrecioUnitario = 38.00m },
        };
        var factura = EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Factura, detalle, cliente));
        factura.Id = 31;
        factura.Numero = 31;
        var empresa = new Empresa { Ruc = "20600000005", RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.", Direccion = "Majes, Caylloma - Arequipa" };

        var guia = ValidadorGuia.DesdeComprobante(factura, empresa, Hoy);

        Assert.Equal(MotivoTraslado.Venta, guia.Motivo);
        Assert.Equal("20131312955", guia.DestinatarioDocumento);
        Assert.Equal("Av. Principal 450, Majes", guia.LlegadaDireccion);
        Assert.Equal("040520", guia.PartidaUbigeo);
        Assert.Equal("F001-00000031", guia.ComprobanteNumero);
        Assert.Equal(31, guia.ComprobanteId);
        Assert.Equal(new[] { 20m, 10m }, guia.Detalle.Select(l => l.Cantidad));
        Assert.Equal(Hoy, guia.FechaTraslado);
    }
}
