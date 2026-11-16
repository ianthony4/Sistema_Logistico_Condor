using System.Security.Cryptography.X509Certificates;
using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

namespace Condor.Sunat;

/// <summary>
/// Envío de un comprobante a la SUNAT (HU-07, T-44): arma el XML UBL 2.1
/// (T-41), lo firma (T-42), lo manda por sendBill y lee el CDR (T-43).
/// Es el "See" de greenter para esta ferretería.
/// </summary>
public sealed class ServicioEnvioSunat : IEnvioSunat
{
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
}
