using System.Security.Cryptography.X509Certificates;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

namespace Condor.Sunat;

/// <summary>
/// Envío de un comprobante a la SUNAT (HU-07, T-44): arma el XML UBL 2.1
/// (T-41), lo firma (T-42), lo manda por sendBill y lee el CDR (T-43).
/// Es el "See" de greenter para esta ferretería. Si la SUNAT ya lo tenía
/// registrado (código 1033: la respuesta del primer envío se perdió), consulta
/// su CDR con getStatusCdr en lugar de dejarlo pendiente (T-84).
/// </summary>
public sealed class ServicioEnvioSunat : IEnvioSunat
{
    /// <summary>«El comprobante fue registrado previamente»: hay que pedir su CDR, no volver a enviarlo.</summary>
    public const string CodigoYaRegistrado = "1033";

    private readonly ConfiguracionSunat _config;
    private readonly HttpClient _http;
    private readonly Func<string, X509Certificate2> _certificado;
    private X509Certificate2? _cargado;

    /// <param name="http">Para las pruebas; por defecto uno con la espera de la configuración.</param>
    /// <param name="certificado">Para las pruebas; por defecto el de la configuración.</param>
    public ServicioEnvioSunat(ConfiguracionSunat config, HttpClient? http = null, Func<string, X509Certificate2>? certificado = null)
    {
        if (!config.Configurada)
            throw new ArgumentException("Falta el usuario y la clave SOL en la sección Sunat de condor.json.", nameof(config));
        _config = config;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(config.EsperaSegundos) };
        _certificado = certificado ?? (ruc => config.CargarCertificado(ruc));
    }

    public string Ambiente => _config.NombreAmbiente;

    /// <summary>Último sobre SOAP enviado, sin la clave (para soporte).</summary>
    public string? UltimoEnvio { get; private set; }

    public async Task<RespuestaSunat> EnviarAsync(Empresa empresa, Comprobante comprobante, CancellationToken ct = default)
    {
        var nombre = GeneradorXmlUbl.NombreArchivo(empresa, comprobante);
        _cargado ??= _certificado(empresa.Ruc);
        var firmado = FirmaXml.Firmar(GeneradorXmlUbl.Generar(empresa, comprobante), _cargado);

        var cliente = new ClienteSunatSoap(_http, _config.UrlServicio, _config.UsuarioServicio(empresa.Ruc), _config.ClaveSol);
        try
        {
            var zip = await cliente.EnviarAsync(nombre, firmado.Xml, ct);
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
            };
        }
        catch (SunatFaultException ex) when (ex.Codigo == CodigoYaRegistrado)
        {
            return await ConsultarRegistradoAsync(cliente, empresa, comprobante, nombre, firmado, ct);
        }
        catch (SunatFaultException ex)
        {
            // Rechazo por error SOAP (2000-3999) o excepción para reenviar.
            return new RespuestaSunat
            {
                Estado = LectorCdr.EstadoDeError(ex.Codigo),
                Codigo = ex.Codigo,
                Descripcion = ex.Message,
                NombreArchivo = nombre,
                XmlFirmado = firmado.Xml,
                Hash = firmado.Hash,
            };
        }
        catch (CdrInvalidoException ex)
        {
            return new RespuestaSunat
            {
                Estado = EstadoSunat.Pendiente,
                Codigo = "CDR",
                Descripcion = ex.Message,
                NombreArchivo = nombre,
                XmlFirmado = firmado.Xml,
                Hash = firmado.Hash,
            };
        }
        finally
        {
            UltimoEnvio = cliente.UltimoEnvio;
        }
    }

    /// <summary>
    /// La SUNAT ya tenía el comprobante (1033): se pide su CDR con
    /// getStatusCdr y se guarda el estado que dice. Si no se puede (beta no
    /// tiene el servicio, o la SUNAT no entrega el CDR) queda pendiente con el
    /// motivo. Sin internet lanza SunatNoDisponibleException, como el envío.
    /// </summary>
    private async Task<RespuestaSunat> ConsultarRegistradoAsync(ClienteSunatSoap cliente, Empresa empresa, Comprobante comprobante,
        string nombre, XmlFirmado firmado, CancellationToken ct)
    {
        RespuestaSunat Pendiente(string codigo, string motivo) => new()
        {
            Estado = EstadoSunat.Pendiente,
            Codigo = codigo,
            Descripcion = motivo,
            NombreArchivo = nombre,
            XmlFirmado = firmado.Xml,
            Hash = firmado.Hash,
        };

        if (_config.UrlConsultaServicio is not { } url)
            return Pendiente(CodigoYaRegistrado,
                "La SUNAT ya tenía registrado este comprobante (1033). En beta no hay consulta de CDR: verifíquelo en SUNAT Operaciones en Línea.");

        try
        {
            var estado = await cliente.ConsultarCdrAsync(url, empresa.Ruc, comprobante.TipoComprobante, comprobante.SerieCodigo,
                comprobante.Numero, ct);
            if (estado.CdrZip is not { } zip)
                return Pendiente(estado.Codigo,
                    $"La SUNAT ya tenía registrado este comprobante (1033) pero no entregó su CDR ({estado.Codigo}): {estado.Mensaje}");

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
            };
        }
        catch (SunatFaultException ex)
        {
            return Pendiente(ex.Codigo, $"La SUNAT ya tenía registrado este comprobante (1033) y la consulta de su CDR falló: {ex.Message}");
        }
        catch (CdrInvalidoException ex)
        {
            return Pendiente("CDR", ex.Message);
        }
    }
}
