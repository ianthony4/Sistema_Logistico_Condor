using System.IO.Compression;
using System.Net;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Condor.Dominio.Sunat;

namespace Condor.Sunat;

/// <summary>
/// Cliente SOAP 1.1 del servicio billService de la SUNAT (HU-07, T-44):
/// operación sendBill con la cabecera WS-Security (usuario RUC+SOL y clave en
/// texto). Equivale a SoapClient + BillSender de greenter.
/// </summary>
public sealed class ClienteSunatSoap
{
    private const string Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string Servicio = "http://service.sunat.gob.pe";
    private const string Wsse = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string TipoClave = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordText";

    /// <summary>Errores de la SUNAT que significan "servicio caído": se reintenta después.</summary>
    private static readonly HashSet<int> ServicioNoDisponible = new() { 109, 130, 131, 132, 133, 134, 135, 136, 137, 138, 200 };

    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _usuario;
    private readonly string _clave;

    public ClienteSunatSoap(HttpClient http, string url, string usuario, string clave)
    {
        _http = http;
        _url = url;
        _usuario = usuario;
        _clave = clave;
    }

    /// <summary>Último sobre SOAP enviado (sin la clave), para revisar errores.</summary>
    public string? UltimoEnvio { get; private set; }

    /// <summary>
    /// Comprime el XML firmado, lo envía y devuelve el ZIP del CDR.
    /// Lanza SunatFaultException si la SUNAT responde con un error y
    /// SunatNoDisponibleException si no se pudo hablar con ella.
    /// </summary>
    public async Task<byte[]> EnviarAsync(string nombreArchivo, string xmlFirmado, CancellationToken ct = default)
    {
        var zip = Comprimir(nombreArchivo + ".xml", xmlFirmado);
        var sobre = Sobre(nombreArchivo + ".zip", zip);
        UltimoEnvio = sobre.Replace(SecurityElement.Escape(_clave) ?? string.Empty, "*****");

        using var solicitud = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(sobre, Encoding.UTF8, "text/xml"),
        };
        solicitud.Headers.Add("SOAPAction", "\"urn:sendBill\"");

        HttpResponseMessage respuesta;
        string cuerpo;
        try
        {
            respuesta = await _http.SendAsync(solicitud, ct);
            cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SunatNoDisponibleException("No hay conexión con la SUNAT (revise el internet de esta PC).", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SunatNoDisponibleException($"La SUNAT no respondió en {_http.Timeout.TotalSeconds:0} segundos.", ex);
        }

        using (respuesta)
            return LeerRespuesta(respuesta.StatusCode, cuerpo);
    }

    /// <summary>ZIP con un solo archivo, como lo pide la SUNAT.</summary>
    public static byte[] Comprimir(string nombre, string contenido)
    {
        var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var flujo = zip.CreateEntry(nombre, CompressionLevel.Optimal).Open();
            flujo.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contenido));
        }
        return memoria.ToArray();
    }

    private string Sobre(string nombreZip, byte[] zip) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + $"<soapenv:Envelope xmlns:soapenv=\"{Soap}\" xmlns:ser=\"{Servicio}\">"
        + "<soapenv:Header>"
        + $"<wsse:Security xmlns:wsse=\"{Wsse}\"><wsse:UsernameToken>"
        + $"<wsse:Username>{SecurityElement.Escape(_usuario)}</wsse:Username>"
        + $"<wsse:Password Type=\"{TipoClave}\">{SecurityElement.Escape(_clave)}</wsse:Password>"
        + "</wsse:UsernameToken></wsse:Security>"
        + "</soapenv:Header>"
        + "<soapenv:Body><ser:sendBill>"
        + $"<fileName>{SecurityElement.Escape(nombreZip)}</fileName>"
        + $"<contentFile>{Convert.ToBase64String(zip)}</contentFile>"
        + "</ser:sendBill></soapenv:Body>"
        + "</soapenv:Envelope>";

    private static byte[] LeerRespuesta(HttpStatusCode estado, string cuerpo)
    {
        var inicio = cuerpo.IndexOf('<');
        if (inicio < 0)
            throw SinXml(estado);

        // Respuestas MTOM/multipart: se recorta el sobre SOAP.
        var xml = cuerpo[inicio..];
        var fin = xml.LastIndexOf("Envelope>", StringComparison.Ordinal);
        if (fin > 0)
            xml = xml[..(fin + "Envelope>".Length)];

        XDocument documento;
        try
        {
            documento = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            throw SinXml(estado);
        }

        var body = documento.Root?.Element(XName.Get("Body", Soap));
        if (body is null)
            throw SinXml(estado);

        var fault = body.Element(XName.Get("Fault", Soap));
        if (fault is not null)
        {
            var codigo = fault.Element("faultcode")?.Value.Trim() ?? string.Empty;
            var mensaje = fault.Element("faultstring")?.Value.Trim() ?? string.Empty;
            var numero = LectorCdr.Numero(codigo);
            if (numero < 0)
                numero = LectorCdr.Numero(mensaje); // a veces el código viene en faultstring
            if (ServicioNoDisponible.Contains(numero))
                throw new SunatNoDisponibleException($"La SUNAT no está disponible ({numero:0000}): {mensaje}");
            throw new SunatFaultException(numero >= 0 ? numero.ToString("0000") : codigo, mensaje);
        }

        var cdr = body.Descendants().FirstOrDefault(e => e.Name.LocalName == "applicationResponse")?.Value;
        if (string.IsNullOrWhiteSpace(cdr))
            throw new SunatFaultException("CDR", "La respuesta de la SUNAT no incluyó el CDR.");
        return Convert.FromBase64String(cdr.Trim());
    }

    private static Exception SinXml(HttpStatusCode estado) => (int)estado switch
    {
        // En beta también responde 401 si se le envía muy seguido: se reintenta luego.
        401 or 403 => new SunatFaultException("HTTP",
            $"La SUNAT rechazó el acceso (HTTP {(int)estado}): revise el usuario y la clave SOL, o reintente en unos segundos."),
        >= 500 => new SunatNoDisponibleException($"La SUNAT no está disponible (HTTP {(int)estado})."),
        _ => new SunatFaultException("HTTP", $"Respuesta no válida de la SUNAT (HTTP {(int)estado})."),
    };
}

/// <summary>La SUNAT respondió con un error SOAP (soap:Fault) en vez del CDR.</summary>
public sealed class SunatFaultException : Exception
{
    public SunatFaultException(string codigo, string mensaje) : base(mensaje) => Codigo = codigo;

    /// <summary>Código de error de la SUNAT (2335, 0111...) o "HTTP" / "CDR".</summary>
    public string Codigo { get; }
}
