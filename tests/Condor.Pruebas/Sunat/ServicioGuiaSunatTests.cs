using System.Net;
using System.Text;
using System.Text.Json;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;
using Condor.Sunat;

namespace Condor.Pruebas.Sunat;

/// <summary>Envío de la guía por la API de guías de la SUNAT (HU-10, T-93).</summary>
public class ServicioGuiaSunatTests
{
    private const string Url = "https://gre.prueba/v1";

    private static ConfiguracionSunat Config() => new()
    {
        UsuarioSol = "MODDATOS", ClaveSol = "moddatos", ClientId = "cliente-123", ClientSecret = "secreto", UrlGuias = Url,
    };

    private static Empresa Empresa() => new() { Ruc = "20600000005", RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.", Direccion = "Majes" };

    private static GuiaRemision Guia(string? ticket = null) => new()
    {
        Id = 5, SerieCodigo = "T001", Numero = 5, Fecha = DateTimeOffset.Now, FechaTraslado = DateOnly.FromDateTime(DateTime.Today),
        Motivo = MotivoTraslado.Venta, Modalidad = ModalidadTraslado.Privado,
        DestinatarioTipo = TipoDocumento.Ruc, DestinatarioDocumento = "20131312955", DestinatarioNombre = "Constructora",
        PartidaUbigeo = "040520", PartidaDireccion = "Majes", LlegadaUbigeo = "040129", LlegadaDireccion = "Obra Yanahuara",
        PesoBrutoKg = 850m, VehiculoPlaca = "V1A952", ConductorDni = "45879632", ConductorNombres = "Juan",
        ConductorApellidos = "Pérez", ConductorLicencia = "Q45879632", Ticket = ticket,
        Detalle = { new() { Item = 1, ProductoId = 1, Descripcion = "Cemento", Unidad = "BLS", Cantidad = 20 } },
    };

    private static ServicioGuiaSunat Servicio(SunatGreSimulada sunat, int consultas = 3) =>
        new(Config(), new HttpClient(sunat), CertificadoDigital.Autofirmado, TimeSpan.Zero, consultas);

    [Fact]
    public async Task Pide_el_token_envia_el_ZIP_y_con_el_ticket_trae_el_CDR_aceptado()
    {
        var sunat = new SunatGreSimulada { Respuestas = { Procesada(LectorCdrTests.CdrXml("0", "La Guia numero T001-5, ha sido aceptada")) } };

        var respuesta = await Servicio(sunat).EnviarAsync(Empresa(), Guia());

        Assert.Equal(EstadoSunat.Aceptado, respuesta.Estado);
        Assert.Equal("ticket-777", respuesta.Ticket);
        Assert.NotNull(respuesta.CdrZip);
        Assert.Equal("20600000005-09-T001-5", respuesta.NombreArchivo);
        Assert.True(FirmaXml.Verificar(respuesta.XmlFirmado!));

        var token = sunat.Pedidos[0];
        Assert.Equal($"{Url}/clientessol/cliente-123/oauth2/token/", token.Url);
        Assert.Contains("username=20600000005MODDATOS", token.Cuerpo);
        Assert.Contains("grant_type=password", token.Cuerpo);
        var envio = sunat.Pedidos[1];
        Assert.Equal($"{Url}/contribuyente/gem/comprobantes/20600000005-09-T001-5", envio.Url);
        Assert.Equal("Bearer token-abc", envio.Autorizacion);
        using var json = JsonDocument.Parse(envio.Cuerpo);
        var archivo = json.RootElement.GetProperty("archivo");
        Assert.Equal("20600000005-09-T001-5.zip", archivo.GetProperty("nomArchivo").GetString());
        Assert.Equal(64, archivo.GetProperty("hashZip").GetString()!.Length);
        Assert.Equal($"{Url}/contribuyente/gem/comprobantes/envios/ticket-777", sunat.Pedidos[2].Url);
    }

    [Fact]
    public async Task Si_la_SUNAT_sigue_procesando_queda_pendiente_con_el_ticket()
    {
        var sunat = new SunatGreSimulada { Respuestas = { EnProceso(), EnProceso() } };

        var respuesta = await Servicio(sunat, consultas: 2).EnviarAsync(Empresa(), Guia());

        Assert.Equal(EstadoSunat.Pendiente, respuesta.Estado);
        Assert.Equal("ticket-777", respuesta.Ticket);
        Assert.Contains("procesando", respuesta.Descripcion);
    }

    [Fact]
    public async Task Con_ticket_no_vuelve_a_enviar_solo_consulta()
    {
        var sunat = new SunatGreSimulada { Respuestas = { Procesada(LectorCdrTests.CdrXml("0", "aceptada")) } };

        var respuesta = await Servicio(sunat).EnviarAsync(Empresa(), Guia(ticket: "ticket-anterior"));

        Assert.Equal(EstadoSunat.Aceptado, respuesta.Estado);
        Assert.DoesNotContain(sunat.Pedidos, p => p.Metodo == "POST" && p.Url.Contains("/comprobantes/2060"));
        Assert.EndsWith("/envios/ticket-anterior", sunat.Pedidos[^1].Url);
    }

    [Fact]
    public async Task Un_rechazo_con_CDR_queda_rechazado()
    {
        var sunat = new SunatGreSimulada { Respuestas = { ConError(LectorCdrTests.CdrXml("2555", "El ubigeo de llegada no existe"), null) } };

        var respuesta = await Servicio(sunat).EnviarAsync(Empresa(), Guia());

        Assert.Equal(EstadoSunat.Rechazado, respuesta.Estado);
        Assert.Equal("2555", respuesta.Codigo);
    }

    [Fact]
    public async Task Un_error_sin_CDR_usa_el_numero_de_error()
    {
        var sunat = new SunatGreSimulada { Respuestas = { ConError(null, ("3341", "La placa no es valida")) } };

        var respuesta = await Servicio(sunat).EnviarAsync(Empresa(), Guia());

        Assert.Equal(EstadoSunat.Rechazado, respuesta.Estado);
        Assert.Equal("3341", respuesta.Codigo);
        Assert.Equal("La placa no es valida", respuesta.Descripcion);
    }

    [Fact]
    public async Task Credenciales_equivocadas_dejan_la_guia_pendiente_con_el_motivo()
    {
        var sunat = new SunatGreSimulada { TokenRechazado = true };

        var respuesta = await Servicio(sunat).EnviarAsync(Empresa(), Guia());

        Assert.Equal(EstadoSunat.Pendiente, respuesta.Estado);
        Assert.Contains("ClientId", respuesta.Descripcion);
    }

    [Fact]
    public async Task Sin_internet_avisa_que_la_SUNAT_no_esta_disponible()
    {
        var sunat = new SunatGreSimulada { SinInternet = true };

        await Assert.ThrowsAsync<SunatNoDisponibleException>(() => Servicio(sunat).EnviarAsync(Empresa(), Guia()));
    }

    [Fact]
    public async Task El_token_se_reutiliza_entre_guias()
    {
        var sunat = new SunatGreSimulada { Respuestas = { Procesada(LectorCdrTests.CdrXml("0", "a")), Procesada(LectorCdrTests.CdrXml("0", "b")) } };
        var servicio = Servicio(sunat);

        await servicio.EnviarAsync(Empresa(), Guia());
        await servicio.EnviarAsync(Empresa(), Guia());

        Assert.Single(sunat.Pedidos, p => p.Url.Contains("oauth2"));
    }

    [Fact]
    public void Sin_credenciales_de_la_API_no_se_puede_crear()
    {
        var config = Config();
        config.ClientSecret = "";

        Assert.False(config.GuiasConfiguradas);
        Assert.Throws<ArgumentException>(() => new ServicioGuiaSunat(config));
    }

    [Fact]
    public void Las_URL_de_la_API_de_guias_dependen_del_ambiente()
    {
        var produccion = new ConfiguracionSunat { Ambiente = "Produccion" };
        var beta = new ConfiguracionSunat();

        Assert.Equal(ConfiguracionSunat.UrlGreSeguridadProduccion, produccion.UrlGreSeguridad);
        Assert.Equal(ConfiguracionSunat.UrlGreProduccion, produccion.UrlGre);
        Assert.Equal(ConfiguracionSunat.UrlGreBeta, beta.UrlGre);
    }

    private static Func<HttpResponseMessage> Procesada(string cdrXml) =>
        () => Json(new { codRespuesta = "0", arcCdr = Convert.ToBase64String(LectorCdrTests.Zip(cdrXml)), indCdrGenerado = "1" });

    private static Func<HttpResponseMessage> EnProceso() => () => Json(new { codRespuesta = "98" });

    private static Func<HttpResponseMessage> ConError(string? cdrXml, (string Numero, string Mensaje)? error) => () => Json(new
    {
        codRespuesta = "99",
        arcCdr = cdrXml is null ? null : Convert.ToBase64String(LectorCdrTests.Zip(cdrXml)),
        error = error is { } e ? new { numError = e.Numero, desError = e.Mensaje } : null,
    });

    private static HttpResponseMessage Json(object cuerpo, HttpStatusCode estado = HttpStatusCode.OK) =>
        new(estado) { Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json") };

    /// <summary>API de guías simulada: token, envío (devuelve ticket-777) y respuestas del ticket en orden.</summary>
    private sealed class SunatGreSimulada : HttpMessageHandler
    {
        public List<Func<HttpResponseMessage>> Respuestas { get; } = new();

        public bool TokenRechazado { get; set; }

        public bool SinInternet { get; set; }

        public List<(string Metodo, string Url, string Cuerpo, string? Autorizacion)> Pedidos { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (SinInternet)
                throw new HttpRequestException("No such host is known.");
            var url = request.RequestUri!.ToString();
            var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Pedidos.Add((request.Method.Method, url, cuerpo, request.Headers.Authorization?.ToString()));

            if (url.Contains("oauth2"))
                return TokenRechazado
                    ? Json(new { error = "invalid_client" }, HttpStatusCode.BadRequest)
                    : Json(new { access_token = "token-abc", token_type = "JWT", expires_in = 3600 });
            if (url.Contains("/envios/"))
            {
                var siguiente = Respuestas[0];
                Respuestas.RemoveAt(0);
                return siguiente();
            }
            return Json(new { numTicket = "ticket-777", fecRecepcion = "2026-11-26T10:30:00" });
        }
    }
}
