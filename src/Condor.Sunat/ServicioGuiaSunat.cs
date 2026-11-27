using System.Security.Cryptography.X509Certificates;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

namespace Condor.Sunat;

/// <summary>
/// Envío de una guía de remisión a la SUNAT (HU-10, T-93): arma el XML
/// DespatchAdvice (T-92), lo firma, lo comprime, lo envía por la API de guías
/// y consulta el ticket hasta tener el CDR. Si la SUNAT sigue procesando, la
/// guía queda pendiente con su ticket y se consulta en la siguiente vuelta.
/// </summary>
public sealed class ServicioGuiaSunat : IEnvioGuiaSunat
{
    private readonly ConfiguracionSunat _config;
    private readonly ClienteGreSunat _cliente;
    private readonly Func<string, X509Certificate2> _certificado;
    private readonly TimeSpan _esperaConsulta;
    private readonly int _consultas;
    private X509Certificate2? _cargado;

    /// <param name="http">Para las pruebas; por defecto uno con la espera de la configuración.</param>
    /// <param name="certificado">Para las pruebas; por defecto el de la configuración.</param>
    /// <param name="esperaConsulta">Pausa entre consultas del ticket recién enviado.</param>
    /// <param name="consultas">Veces que se consulta el ticket antes de dejarlo pendiente.</param>
    public ServicioGuiaSunat(ConfiguracionSunat config, HttpClient? http = null, Func<string, X509Certificate2>? certificado = null,
        TimeSpan? esperaConsulta = null, int consultas = 3)
    {
        if (!config.GuiasConfiguradas)
            throw new ArgumentException("Faltan el usuario SOL o el ClientId y ClientSecret de la API de guías en condor.json.", nameof(config));
        _config = config;
        _cliente = new ClienteGreSunat(http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(config.EsperaSegundos) }, config);
        _certificado = certificado ?? (ruc => config.CargarCertificado(ruc));
        _esperaConsulta = esperaConsulta ?? TimeSpan.FromSeconds(2);
        _consultas = Math.Max(1, consultas);
    }

    public string Ambiente => _config.NombreAmbiente;

    public async Task<RespuestaSunat> EnviarAsync(Empresa empresa, GuiaRemision guia, CancellationToken ct = default)
    {
        _cliente.Ruc = empresa.Ruc;
        var nombre = GeneradorXmlGuia.NombreArchivo(empresa, guia);
        _cargado ??= _certificado(empresa.Ruc);
        var firmado = FirmaXml.Firmar(GeneradorXmlGuia.Generar(empresa, guia), _cargado);

        string ticket;
        if (guia.Ticket is { Length: > 0 } yaEnviada)
        {
            ticket = yaEnviada;
        }
        else
        {
            try
            {
                ticket = await _cliente.EnviarAsync(nombre, ClienteSunatSoap.Comprimir(nombre + ".xml", firmado.Xml), ct);
            }
            catch (SunatFaultException ex)
            {
                return Respuesta(LectorCdr.EstadoDeError(ex.Codigo), ex.Codigo, ex.Message, nombre, firmado, null, null);
            }
        }

        try
        {
            for (var intento = 1; ; intento++)
            {
                var estado = await _cliente.ConsultarAsync(ticket, ct);
                if (estado.Codigo == ClienteGreSunat.EnProceso)
                {
                    if (intento >= _consultas)
                        return Respuesta(EstadoSunat.Pendiente, ClienteGreSunat.EnProceso,
                            $"La SUNAT está procesando la guía (ticket {ticket}); se consulta de nuevo en la próxima vuelta.", nombre, firmado, ticket, null);
                    await Task.Delay(_esperaConsulta, ct);
                    continue;
                }

                if (estado.CdrZip is { } zip)
                {
                    var cdr = LectorCdr.LeerZip(zip);
                    return new RespuestaSunat
                    {
                        Estado = LectorCdr.Estado(cdr),
                        Codigo = cdr.Codigo,
                        Descripcion = cdr.Descripcion,
                        Notas = cdr.Notas,
                        NombreArchivo = nombre,
                        XmlFirmado = firmado.Xml,
                        Hash = firmado.Hash,
                        CdrZip = zip,
                        Ticket = ticket,
                    };
                }

                // Código 99 sin CDR: la SUNAT dice el error (rechazo si es 2000-3999).
                var codigo = estado.NumeroError ?? estado.Codigo;
                return Respuesta(LectorCdr.EstadoDeError(codigo), codigo, estado.Error ?? "La SUNAT no procesó la guía.", nombre, firmado, ticket, null);
            }
        }
        catch (SunatFaultException ex)
        {
            return Respuesta(EstadoSunat.Pendiente, ex.Codigo, ex.Message, nombre, firmado, ticket, null);
        }
        catch (CdrInvalidoException ex)
        {
            return Respuesta(EstadoSunat.Pendiente, "CDR", ex.Message, nombre, firmado, ticket, null);
        }
    }

    private static RespuestaSunat Respuesta(string estado, string codigo, string descripcion, string nombre, XmlFirmado firmado,
        string? ticket, byte[]? cdr) => new()
    {
        Estado = estado,
        Codigo = codigo,
        Descripcion = descripcion,
        NombreArchivo = nombre,
        XmlFirmado = firmado.Xml,
        Hash = firmado.Hash,
        Ticket = ticket,
        CdrZip = cdr,
    };
}
