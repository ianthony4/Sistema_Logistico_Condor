using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Condor.Dominio.Sunat;

namespace Condor.Sunat;

/// <summary>Resultado de consultar un ticket de la API de guías.</summary>
/// <param name="Codigo">0 procesada (con CDR), 98 en proceso, 99 procesada con error.</param>
public sealed record EstadoTicketGre(string Codigo, string? NumeroError, string? Error, byte[]? CdrZip);

/// <summary>
/// Cliente de la API REST de guías de remisión electrónicas de la SUNAT
/// (HU-10, T-93): token OAuth2 con el usuario SOL y las credenciales de la
/// API, envío del ZIP firmado (que devuelve un ticket) y consulta del ticket.
/// Equivale a la clase Api de Greenter.
/// </summary>
public sealed class ClienteGreSunat
{
    public const string EnProceso = "98";
    public const string ConError = "99";

    private readonly HttpClient _http;
    private readonly ConfiguracionSunat _config;
    private readonly Func<DateTimeOffset> _ahora;
    private string? _token;
    private DateTimeOffset _venceToken;

    public ClienteGreSunat(HttpClient http, ConfiguracionSunat config, Func<DateTimeOffset>? ahora = null)
    {
        _http = http;
        _config = config;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Envía el ZIP (con el XML firmado) y devuelve el ticket con que la SUNAT
    /// lo recibió. Lanza SunatFaultException si la SUNAT lo rechaza de entrada
    /// y SunatNoDisponibleException si no hay internet.
    /// </summary>
    public async Task<string> EnviarAsync(string nombreArchivo, byte[] zip, CancellationToken ct = default)
    {
        var cuerpo = new
        {
            archivo = new
            {
                nomArchivo = nombreArchivo + ".zip",
                arcGreZip = Convert.ToBase64String(zip),
                hashZip = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(),
            },
        };
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, $"{_config.UrlGre}/contribuyente/gem/comprobantes/{nombreArchivo}")
        {
            Content = JsonContent.Create(cuerpo),
        };
        var respuesta = await PedirAsync(solicitud, ct);
        return respuesta.TryGetProperty("numTicket", out var ticket) && ticket.GetString() is { Length: > 0 } numero
            ? numero
            : throw new SunatFaultException("GRE", "La SUNAT no devolvió el ticket de la guía.");
    }

    /// <summary>Consulta el resultado de un ticket.</summary>
    public async Task<EstadoTicketGre> ConsultarAsync(string ticket, CancellationToken ct = default)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"{_config.UrlGre}/contribuyente/gem/comprobantes/envios/{ticket}");
        var respuesta = await PedirAsync(solicitud, ct);

        var codigo = respuesta.TryGetProperty("codRespuesta", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        string? numeroError = null, error = null;
        if (respuesta.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
        {
            numeroError = e.TryGetProperty("numError", out var n) ? n.ToString() : null;
            error = e.TryGetProperty("desError", out var d) ? d.GetString() : null;
        }
        var cdr = respuesta.TryGetProperty("arcCdr", out var a) && a.GetString() is { Length: > 0 } base64
            ? Convert.FromBase64String(base64)
            : null;
        return new EstadoTicketGre(codigo, numeroError, error, cdr);
    }

    private async Task<JsonElement> PedirAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        solicitud.Headers.Authorization = new("Bearer", await TokenAsync(ct));
        var (estado, json) = await MandarAsync(solicitud, ct);
        if (estado is HttpStatusCode.OK)
            return json;
        throw ErrorDe(estado, json);
    }

    /// <summary>Token OAuth2: se pide una vez y se reutiliza hasta un minuto antes de que venza.</summary>
    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is not null && _ahora() < _venceToken)
            return _token;

        var usuario = _config.UsuarioServicio(Ruc);
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, $"{_config.UrlGreSeguridad}/clientessol/{_config.ClientId}/oauth2/token/")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["scope"] = "https://api-cpe.sunat.gob.pe",
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["username"] = usuario,
                ["password"] = _config.ClaveSol,
            }),
        };
        var (estado, json) = await MandarAsync(solicitud, ct);
        if (estado is not HttpStatusCode.OK || !json.TryGetProperty("access_token", out var token))
            throw new SunatFaultException("TOKEN",
                "La SUNAT no dio acceso a la API de guías: revise el usuario y la clave SOL y el ClientId y ClientSecret de condor.json.");

        _token = token.GetString();
        var segundos = json.TryGetProperty("expires_in", out var vence) && vence.TryGetInt32(out var s) ? s : 3600;
        _venceToken = _ahora().AddSeconds(Math.Max(60, segundos) - 60);
        return _token!;
    }

    /// <summary>RUC del emisor: con él se arma el usuario (RUC + usuario SOL) del token.</summary>
    public string Ruc { get; set; } = string.Empty;

    private async Task<(HttpStatusCode Estado, JsonElement Json)> MandarAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        try
        {
            using var respuesta = await _http.SendAsync(solicitud, ct);
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            if ((int)respuesta.StatusCode >= 500)
                throw new SunatNoDisponibleException($"La API de guías de la SUNAT no está disponible (HTTP {(int)respuesta.StatusCode}).");
            JsonElement json;
            try
            {
                json = string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
            }
            catch (JsonException)
            {
                json = default;
            }
            return (respuesta.StatusCode, json.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}").RootElement : json);
        }
        catch (HttpRequestException ex)
        {
            throw new SunatNoDisponibleException("No hay conexión con la SUNAT (revise el internet de esta PC).", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SunatNoDisponibleException($"La SUNAT no respondió en {_http.Timeout.TotalSeconds:0} segundos.", ex);
        }
    }

    /// <summary>Error de la API: {"cod":"...","msg":"...","errors":[{"cod":"2335","msg":"..."}]}.</summary>
    private static SunatFaultException ErrorDe(HttpStatusCode estado, JsonElement json)
    {
        string? codigo = json.TryGetProperty("cod", out var c) ? c.ToString() : null;
        string? mensaje = json.TryGetProperty("msg", out var m) ? m.GetString() : null;
        if (json.TryGetProperty("errors", out var errores) && errores.ValueKind == JsonValueKind.Array && errores.GetArrayLength() > 0)
        {
            var primero = errores[0];
            codigo = primero.TryGetProperty("cod", out var ec) ? ec.ToString() : codigo;
            mensaje = primero.TryGetProperty("msg", out var em) ? em.GetString() : mensaje;
        }
        return new SunatFaultException(codigo ?? "HTTP", mensaje ?? $"La API de guías respondió HTTP {(int)estado}.");
    }
}
