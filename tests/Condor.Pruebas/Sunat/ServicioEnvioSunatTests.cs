using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Sunat;

namespace Condor.Pruebas.Sunat;

public class ServicioEnvioSunatTests
{
    private static readonly ConfiguracionSunat Beta = new() { UsuarioSol = "MODDATOS", ClaveSol = "moddatos" };

    [Fact]
    public async Task Envia_el_ZIP_firmado_por_sendBill_con_el_usuario_RUC_mas_SOL()
    {
        var sunat = new SunatSimulada(_ => Exito(LectorCdrTests.CdrXml("0", "La Boleta numero B001-12, ha sido aceptada")));
        var servicio = Servicio(sunat);

        await servicio.EnviarAsync(Empresa(), Boleta());

        var pedido = Assert.Single(sunat.Pedidos);
        Assert.Equal(ConfiguracionSunat.UrlBeta, pedido.Url);
        Assert.Equal("\"urn:sendBill\"", pedido.SoapAction);
        var sobre = XDocument.Parse(pedido.Cuerpo);
        Assert.Equal("20600000005MODDATOS", sobre.Descendants().Single(e => e.Name.LocalName == "Username").Value);
        Assert.Equal("moddatos", sobre.Descendants().Single(e => e.Name.LocalName == "Password").Value);
        Assert.Equal("20600000005-03-B001-12.zip", sobre.Descendants().Single(e => e.Name.LocalName == "fileName").Value);

        var zip = Convert.FromBase64String(sobre.Descendants().Single(e => e.Name.LocalName == "contentFile").Value);
        using var archivo = new ZipArchive(new MemoryStream(zip));
        var entrada = Assert.Single(archivo.Entries);
        Assert.Equal("20600000005-03-B001-12.xml", entrada.FullName);
        using var lector = new StreamReader(entrada.Open());
        Assert.True(FirmaXml.Verificar(await lector.ReadToEndAsync()));
    }

    [Fact]
    public async Task Con_CDR_aceptado_devuelve_el_estado_el_XML_el_hash_y_el_CDR()
    {
        var servicio = Servicio(new SunatSimulada(_ => Exito(LectorCdrTests.CdrXml("0", "La Boleta numero B001-12, ha sido aceptada"))));

        var respuesta = await servicio.EnviarAsync(Empresa(), Boleta());

        Assert.Equal(EstadoSunat.Aceptado, respuesta.Estado);
        Assert.Equal("0", respuesta.Codigo);
        Assert.Equal("La Boleta numero B001-12, ha sido aceptada", respuesta.Descripcion);
        Assert.Equal("20600000005-03-B001-12", respuesta.NombreArchivo);
        Assert.NotNull(respuesta.CdrZip);
        Assert.True(FirmaXml.Verificar(respuesta.XmlFirmado!));
        Assert.Equal(28, respuesta.Hash!.Length);
    }

    [Fact]
    public async Task Con_CDR_de_rechazo_queda_rechazado()
    {
        var servicio = Servicio(new SunatSimulada(_ => Exito(LectorCdrTests.CdrXml("2017", "El numero de documento de identidad del receptor debe ser RUC"))));

        var respuesta = await servicio.EnviarAsync(Empresa(), Boleta());

        Assert.Equal(EstadoSunat.Rechazado, respuesta.Estado);
        Assert.Equal("2017", respuesta.Codigo);
    }

    [Fact]
    public async Task Un_soap_Fault_del_rango_2000_es_rechazo_y_los_demas_quedan_pendientes()
    {
        var rechazo = await Servicio(new SunatSimulada(_ => Fault("soap-env:Client.2335", "El documento electronico ingresado ha sido alterado")))
            .EnviarAsync(Empresa(), Boleta());
        var clave = await Servicio(new SunatSimulada(_ => Fault("soap-env:Client.0111", "No tiene el perfil para enviar comprobantes electronicos")))
            .EnviarAsync(Empresa(), Boleta());

        Assert.Equal(EstadoSunat.Rechazado, rechazo.Estado);
        Assert.Equal("2335", rechazo.Codigo);
        Assert.Equal(EstadoSunat.Pendiente, clave.Estado);
        Assert.Equal("0111", clave.Codigo);
        Assert.Contains("perfil", clave.Descripcion);
        Assert.NotNull(clave.XmlFirmado);
    }

    [Fact]
    public async Task Sin_internet_o_sin_respuesta_avisa_que_la_SUNAT_no_esta_disponible()
    {
        var sinRed = Servicio(new SunatSimulada(_ => throw new HttpRequestException("No such host is known.")));
        var lenta = Servicio(new SunatSimulada(_ => throw new TaskCanceledException("timeout")));
        var caida = Servicio(new SunatSimulada(_ => Fault("soap-env:Server.0109", "El sistema de recepcion de facturas electronicas no esta disponible")));
        var error500 = Servicio(new SunatSimulada(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Service Unavailable") }));

        foreach (var servicio in new[] { sinRed, lenta, caida, error500 })
            await Assert.ThrowsAsync<SunatNoDisponibleException>(() => servicio.EnviarAsync(Empresa(), Boleta()));
    }

    [Fact]
    public async Task Clave_SOL_equivocada_HTTP_401_queda_pendiente_con_el_motivo()
    {
        var servicio = Servicio(new SunatSimulada(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Unauthorized") }));

        var respuesta = await servicio.EnviarAsync(Empresa(), Boleta());

        Assert.Equal(EstadoSunat.Pendiente, respuesta.Estado);
        Assert.Contains("clave SOL", respuesta.Descripcion);
    }

    [Fact]
    public async Task El_registro_del_envio_no_guarda_la_clave_SOL()
    {
        var servicio = Servicio(new SunatSimulada(_ => Exito(LectorCdrTests.CdrXml("0", "aceptada"))));

        await servicio.EnviarAsync(Empresa(), Boleta());

        Assert.DoesNotContain("moddatos", servicio.UltimoEnvio);
        Assert.Contains("*****", servicio.UltimoEnvio);
    }

    // T-84: la SUNAT ya tenía el comprobante (1033) y se consulta su CDR.

    private const string UrlConsultaPrueba = "https://consulta.prueba/billConsultService";

    [Fact]
    public async Task Si_la_SUNAT_ya_lo_tenia_registrado_consulta_su_CDR_y_lo_guarda_aceptado()
    {
        var sunat = new SunatSimulada(p => EsConsulta(p)
            ? StatusCdr("0004", "La constancia existe", LectorCdrTests.CdrXml("0", "La Boleta numero B001-12, ha sido aceptada"))
            : Fault("soap-env:Client.1033", "El comprobante fue registrado previamente con otros datos"));
        var servicio = new ServicioEnvioSunat(ConConsulta(), new HttpClient(sunat), CertificadoDigital.Autofirmado);

        var respuesta = await servicio.EnviarAsync(Empresa(), Boleta());

        Assert.Equal(2, sunat.Pedidos.Count);
        var consulta = sunat.Pedidos[1];
        Assert.Equal(UrlConsultaPrueba, consulta.Url);
        Assert.Equal("\"urn:getStatusCdr\"", consulta.SoapAction);
        var sobre = XDocument.Parse(consulta.Cuerpo);
        string Valor(string nombre) => sobre.Descendants().Single(e => e.Name.LocalName == nombre).Value;
        Assert.Equal("20600000005", Valor("rucComprobante"));
        Assert.Equal("03", Valor("tipoComprobante"));
        Assert.Equal("B001", Valor("serieComprobante"));
        Assert.Equal("12", Valor("numeroComprobante"));
        Assert.Equal("20600000005MODDATOS", Valor("Username"));

        Assert.Equal(EstadoSunat.Aceptado, respuesta.Estado);
        Assert.Equal("0", respuesta.Codigo);
        Assert.NotNull(respuesta.CdrZip);
        Assert.Equal("20600000005-03-B001-12", respuesta.NombreArchivo);
    }

    [Fact]
    public async Task Si_el_CDR_consultado_es_de_rechazo_queda_rechazado()
    {
        var sunat = new SunatSimulada(p => EsConsulta(p)
            ? StatusCdr("0004", "La constancia existe", LectorCdrTests.CdrXml("2017", "El numero de documento de identidad del receptor debe ser RUC"))
            : Fault("soap-env:Client.1033", "El comprobante fue registrado previamente con otros datos"));

        var respuesta = await new ServicioEnvioSunat(ConConsulta(), new HttpClient(sunat), CertificadoDigital.Autofirmado)
            .EnviarAsync(Empresa(), Boleta());

        Assert.Equal(EstadoSunat.Rechazado, respuesta.Estado);
        Assert.Equal("2017", respuesta.Codigo);
    }

    [Fact]
    public async Task Si_la_consulta_no_trae_el_CDR_queda_pendiente_con_el_motivo()
    {
        var sunat = new SunatSimulada(p => EsConsulta(p)
            ? StatusCdr("0012", "No existe la constancia de recepcion", cdrXml: null)
            : Fault("soap-env:Client.1033", "El comprobante fue registrado previamente con otros datos"));

        var respuesta = await new ServicioEnvioSunat(ConConsulta(), new HttpClient(sunat), CertificadoDigital.Autofirmado)
            .EnviarAsync(Empresa(), Boleta());

        Assert.Equal(EstadoSunat.Pendiente, respuesta.Estado);
        Assert.Equal("0012", respuesta.Codigo);
        Assert.Contains("1033", respuesta.Descripcion);
        Assert.Contains("No existe la constancia", respuesta.Descripcion);
        Assert.NotNull(respuesta.XmlFirmado);
    }

    [Fact]
    public async Task Si_la_consulta_responde_con_error_queda_pendiente()
    {
        var sunat = new SunatSimulada(p => EsConsulta(p)
            ? Fault("soap-env:Client.0102", "Usuario o contraseña incorrectos")
            : Fault("soap-env:Client.1033", "El comprobante fue registrado previamente con otros datos"));

        var respuesta = await new ServicioEnvioSunat(ConConsulta(), new HttpClient(sunat), CertificadoDigital.Autofirmado)
            .EnviarAsync(Empresa(), Boleta());

        Assert.Equal(EstadoSunat.Pendiente, respuesta.Estado);
        Assert.Contains("consulta de su CDR falló", respuesta.Descripcion);
    }

    [Fact]
    public async Task En_beta_no_hay_consulta_de_CDR_y_queda_pendiente_explicandolo()
    {
        var sunat = new SunatSimulada(_ => Fault("soap-env:Client.1033", "El comprobante fue registrado previamente con otros datos"));

        var respuesta = await Servicio(sunat).EnviarAsync(Empresa(), Boleta());

        Assert.Single(sunat.Pedidos); // no intenta consultar
        Assert.Equal(EstadoSunat.Pendiente, respuesta.Estado);
        Assert.Equal("1033", respuesta.Codigo);
        Assert.Contains("En beta no hay consulta de CDR", respuesta.Descripcion);
    }

    [Fact]
    public async Task Sin_internet_al_consultar_avisa_que_la_SUNAT_no_esta_disponible()
    {
        var sunat = new SunatSimulada(p => EsConsulta(p)
            ? throw new HttpRequestException("No such host is known.")
            : Fault("soap-env:Client.1033", "El comprobante fue registrado previamente con otros datos"));

        await Assert.ThrowsAsync<SunatNoDisponibleException>(() =>
            new ServicioEnvioSunat(ConConsulta(), new HttpClient(sunat), CertificadoDigital.Autofirmado).EnviarAsync(Empresa(), Boleta()));
    }

    private static ConfiguracionSunat ConConsulta() =>
        new() { UsuarioSol = "MODDATOS", ClaveSol = "moddatos", UrlConsulta = UrlConsultaPrueba };

    private static bool EsConsulta(HttpRequestMessage pedido) =>
        pedido.Headers.GetValues("SOAPAction").Single().Contains("getStatusCdr");

    /// <summary>Respuesta SOAP de getStatusCdr, con el CDR en base64 si se da.</summary>
    private static HttpResponseMessage StatusCdr(string codigo, string mensaje, string? cdrXml) => Xml(HttpStatusCode.OK,
        "<soap-env:Envelope xmlns:soap-env=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap-env:Body>"
        + "<ns2:getStatusCdrResponse xmlns:ns2=\"http://service.sunat.gob.pe\"><statusCdr>"
        + (cdrXml is null ? string.Empty : $"<content>{Convert.ToBase64String(LectorCdrTests.Zip(cdrXml))}</content>")
        + $"<statusCode>{codigo}</statusCode><statusMessage>{mensaje}</statusMessage>"
        + "</statusCdr></ns2:getStatusCdrResponse></soap-env:Body></soap-env:Envelope>");

    [Fact]
    public void Sin_usuario_ni_clave_SOL_no_se_puede_crear()
    {
        Assert.Throws<ArgumentException>(() => new ServicioEnvioSunat(new ConfiguracionSunat()));
    }

    private static ServicioEnvioSunat Servicio(SunatSimulada sunat) =>
        new(Beta, new HttpClient(sunat), CertificadoDigital.Autofirmado);

    /// <summary>Respuesta SOAP de sendBill con el CDR en base64, como la de la SUNAT.</summary>
    private static HttpResponseMessage Exito(string cdrXml) => Xml(HttpStatusCode.OK,
        "<soap-env:Envelope xmlns:soap-env=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap-env:Header/>"
        + "<soap-env:Body><br:sendBillResponse xmlns:br=\"http://service.sunat.gob.pe\">"
        + $"<applicationResponse>{Convert.ToBase64String(LectorCdrTests.Zip(cdrXml))}</applicationResponse>"
        + "</br:sendBillResponse></soap-env:Body></soap-env:Envelope>");

    private static HttpResponseMessage Fault(string codigo, string mensaje) => Xml(HttpStatusCode.InternalServerError,
        "<soap-env:Envelope xmlns:soap-env=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap-env:Body>"
        + $"<soap-env:Fault><faultcode>{codigo}</faultcode><faultstring>{mensaje}</faultstring></soap-env:Fault>"
        + "</soap-env:Body></soap-env:Envelope>");

    private static HttpResponseMessage Xml(HttpStatusCode estado, string cuerpo) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "text/xml") };

    private static Empresa Empresa() => new()
    {
        Ruc = "20600000005",
        RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.",
        Direccion = "Majes, Caylloma - Arequipa",
    };

    private static Comprobante Boleta()
    {
        var boleta = new Comprobante
        {
            Id = 12, TipoComprobante = TipoComprobante.Boleta, SerieCodigo = "B001", Numero = 12,
            Fecha = DateTimeOffset.Now, OpGravada = 55.08m, Igv = 9.92m, Total = 65.00m,
        };
        boleta.Detalle.Add(new DetalleComprobante
        {
            Item = 1, ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS",
            Cantidad = 2, PrecioUnitario = 32.50m, Importe = 65.00m,
        });
        return boleta;
    }

    /// <summary>Servidor de la SUNAT simulado: anota cada pedido y responde con la función dada.</summary>
    private sealed class SunatSimulada : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public SunatSimulada(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<(string Url, string? SoapAction, string Cuerpo)> Pedidos { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            Pedidos.Add((request.RequestUri!.ToString(), request.Headers.GetValues("SOAPAction").FirstOrDefault(), cuerpo));
            return _responder(request);
        }
    }
}
