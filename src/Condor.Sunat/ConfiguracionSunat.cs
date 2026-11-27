using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Condor.Sunat;

/// <summary>
/// Sección "Sunat" de condor.json (HU-07, T-46): ambiente, usuario y clave
/// SOL y el certificado digital. Solo la llena la PC que envía los
/// comprobantes; si falta, se emite igual y quedan pendientes de envío.
/// Desde el Sprint 4 dice también cada cuánto se reenvían los pendientes
/// (HU-17, T-57); desde el Sprint 5, dónde consultar el CDR de un
/// comprobante que la SUNAT ya tenía registrado (T-84) y las credenciales de
/// la API de guías de remisión (HU-10, T-93).
/// </summary>
public sealed class ConfiguracionSunat
{
    public const string NombreArchivo = "condor.json";
    public const string Seccion = "Sunat";

    /// <summary>Servicio de pruebas de la SUNAT: acepta cualquier RUC con el usuario MODDATOS.</summary>
    public const string UrlBeta = "https://e-beta.sunat.gob.pe/ol-ti-itcpfegem-beta/billService";

    public const string UrlProduccion = "https://e-factura.sunat.gob.pe/ol-ti-itcpfegem/billService";

    /// <summary>
    /// Servicio billConsultService (getStatusCdr) de producción. La SUNAT no
    /// tiene uno de pruebas: en beta no se puede consultar el CDR.
    /// </summary>
    public const string UrlConsultaProduccion = "https://e-factura.sunat.gob.pe/ol-it-wsconscpegem/billConsultService";

    /// <summary>API de guías de remisión (GRE) de producción: seguridad (token) y comprobantes.</summary>
    public const string UrlGreSeguridadProduccion = "https://api-seguridad.sunat.gob.pe/v1";
    public const string UrlGreProduccion = "https://api-cpe.sunat.gob.pe/v1";

    /// <summary>Entorno de pruebas de la API de guías que usa Greenter (token y comprobantes en la misma URL).</summary>
    public const string UrlGreBeta = "https://gre-test.nubefact.com/v1";

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>"Beta" (pruebas, Sprint 3) o "Produccion".</summary>
    public string Ambiente { get; set; } = "Beta";

    /// <summary>
    /// Usuario secundario SOL creado para la facturación electrónica (sin el
    /// RUC delante). En beta es MODDATOS.
    /// </summary>
    public string UsuarioSol { get; set; } = string.Empty;

    public string ClaveSol { get; set; } = string.Empty;

    /// <summary>Ruta del .pfx / .p12 / .pem; relativa a la carpeta del programa o absoluta.</summary>
    public string Certificado { get; set; } = string.Empty;

    public string ClaveCertificado { get; set; } = string.Empty;

    /// <summary>Segundos que se espera a la SUNAT antes de dejar el comprobante pendiente.</summary>
    public int EsperaSegundos { get; set; } = 30;

    /// <summary>
    /// Cada cuántos minutos esta PC reenvía los comprobantes pendientes (HU-17,
    /// T-57). 0 lo desactiva: útil si otra PC con internet ya se encarga.
    /// </summary>
    public int ReenvioMinutos { get; set; } = 5;

    /// <summary>True si esta PC reenvía sola los pendientes.</summary>
    public bool ReenviaPendientes => Configurada && ReenvioMinutos > 0;

    /// <summary>Solo para pruebas: otra URL del servicio sendBill.</summary>
    public string? Url { get; set; }

    /// <summary>Solo para pruebas: otra URL del servicio getStatusCdr.</summary>
    public string? UrlConsulta { get; set; }

    /// <summary>
    /// Credenciales de la API de guías (HU-10): se generan en SUNAT
    /// Operaciones en Línea > Credenciales de API SUNAT. Sin ellas las guías
    /// se emiten e imprimen igual y quedan pendientes de envío.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Solo para pruebas: otra URL de la API de guías (token y comprobantes).</summary>
    public string? UrlGuias { get; set; }

    /// <summary>True si hay usuario SOL y credenciales de la API: se pueden enviar guías.</summary>
    public bool GuiasConfiguradas => Configurada && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>URL base para pedir el token de la API de guías.</summary>
    public string UrlGreSeguridad => !string.IsNullOrWhiteSpace(UrlGuias) ? UrlGuias! : EsProduccion ? UrlGreSeguridadProduccion : UrlGreBeta;

    /// <summary>URL base para enviar las guías y consultar sus tickets.</summary>
    public string UrlGre => !string.IsNullOrWhiteSpace(UrlGuias) ? UrlGuias! : EsProduccion ? UrlGreProduccion : UrlGreBeta;

    public bool EsProduccion => Ambiente.Trim().StartsWith("prod", StringComparison.OrdinalIgnoreCase);

    /// <summary>True si hay usuario y clave SOL: se puede enviar.</summary>
    public bool Configurada => !string.IsNullOrWhiteSpace(UsuarioSol) && !string.IsNullOrWhiteSpace(ClaveSol);

    public string UrlServicio => !string.IsNullOrWhiteSpace(Url) ? Url! : EsProduccion ? UrlProduccion : UrlBeta;

    /// <summary>Dónde consultar el CDR (getStatusCdr); null en beta, que no tiene ese servicio.</summary>
    public string? UrlConsultaServicio => !string.IsNullOrWhiteSpace(UrlConsulta) ? UrlConsulta : EsProduccion ? UrlConsultaProduccion : null;

    public string NombreAmbiente => EsProduccion ? "producción" : "beta";

    /// <summary>Usuario para la cabecera WS-Security: RUC + usuario SOL (20600000005MODDATOS).</summary>
    public string UsuarioServicio(string ruc)
    {
        var usuario = UsuarioSol.Trim();
        return usuario.StartsWith(ruc, StringComparison.Ordinal) ? usuario : ruc + usuario.ToUpperInvariant();
    }

    /// <summary>
    /// Lee la sección Sunat de condor.json. Si el archivo o la sección no
    /// existen, devuelve una configuración vacía (no configurada).
    /// </summary>
    /// <exception cref="InvalidDataException">Si la sección está mal escrita o es incoherente.</exception>
    public static ConfiguracionSunat Cargar(string? carpeta = null)
    {
        var ruta = Path.Combine(carpeta ?? AppContext.BaseDirectory, NombreArchivo);
        if (!File.Exists(ruta))
            return new ConfiguracionSunat();

        ConfiguracionSunat? config = null;
        try
        {
            using var documento = JsonDocument.Parse(File.ReadAllText(ruta), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            foreach (var propiedad in documento.RootElement.EnumerateObject())
            {
                if (string.Equals(propiedad.Name, Seccion, StringComparison.OrdinalIgnoreCase))
                    config = propiedad.Value.Deserialize<ConfiguracionSunat>(OpcionesJson);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"La sección {Seccion} de {NombreArchivo} no es válida: {ex.Message}", ex);
        }

        config ??= new ConfiguracionSunat();
        config.Validar();
        return config;
    }

    public void Validar()
    {
        var ambiente = Ambiente.Trim().ToLowerInvariant();
        if (ambiente is not ("beta" or "produccion" or "producción"))
            throw new InvalidDataException($"{Seccion} > Ambiente debe ser \"Beta\" o \"Produccion\".");
        if (EsperaSegundos is < 5 or > 120)
            throw new InvalidDataException($"{Seccion} > EsperaSegundos debe estar entre 5 y 120.");
        if (ReenvioMinutos is < 0 or > 60)
            throw new InvalidDataException($"{Seccion} > ReenvioMinutos debe estar entre 1 y 60 (0 para no reenviar desde esta PC).");
        if (EsProduccion && Configurada && string.IsNullOrWhiteSpace(Certificado))
            throw new InvalidDataException($"{Seccion} > Certificado es obligatorio en producción: ponga la ruta del .pfx de la ferretería.");
        if (!string.IsNullOrWhiteSpace(Url) && !Uri.TryCreate(Url, UriKind.Absolute, out _))
            throw new InvalidDataException($"{Seccion} > Url no es una dirección válida.");
        if (!string.IsNullOrWhiteSpace(UrlConsulta) && !Uri.TryCreate(UrlConsulta, UriKind.Absolute, out _))
            throw new InvalidDataException($"{Seccion} > UrlConsulta no es una dirección válida.");
        if (!string.IsNullOrWhiteSpace(UrlGuias) && !Uri.TryCreate(UrlGuias, UriKind.Absolute, out _))
            throw new InvalidDataException($"{Seccion} > UrlGuias no es una dirección válida.");
        if (string.IsNullOrWhiteSpace(ClientId) != string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidDataException($"{Seccion} > ClientId y ClientSecret de la API de guías van juntos: complete los dos o deje ambos vacíos.");
    }

    /// <summary>
    /// Certificado con el que se firma: el del archivo configurado o, solo en
    /// beta y si no se indicó ninguno, uno autofirmado para pruebas.
    /// </summary>
    public X509Certificate2 CargarCertificado(string ruc, string? carpeta = null)
    {
        if (string.IsNullOrWhiteSpace(Certificado))
        {
            if (EsProduccion)
                throw new CertificadoInvalidoException("Falta el certificado digital para firmar en producción.");
            return CertificadoDigital.Autofirmado(ruc);
        }

        var ruta = Path.IsPathRooted(Certificado) ? Certificado : Path.Combine(carpeta ?? AppContext.BaseDirectory, Certificado);
        var certificado = CertificadoDigital.Cargar(ruta, ClaveCertificado);
        if (EsProduccion && CertificadoDigital.Vencido(certificado))
            throw new CertificadoInvalidoException(
                $"El certificado digital venció el {certificado.NotAfter:dd/MM/yyyy}: renuévelo con la entidad certificadora.");
        return certificado;
    }

    public override string ToString() => Configurada ? $"SUNAT {NombreAmbiente}" : "SUNAT sin configurar";
}
