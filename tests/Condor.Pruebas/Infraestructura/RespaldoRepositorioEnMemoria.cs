using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;

namespace Condor.Pruebas.Infraestructura;

/// <summary>Registro de respaldos en memoria (HU-15): el último es el de fecha más reciente, como en la base.</summary>
public sealed class RespaldoRepositorioEnMemoria : IRespaldoRepositorio
{
    private readonly List<Respaldo> _respaldos = new();

    public RespaldoRepositorioEnMemoria(IEnumerable<Respaldo>? iniciales = null)
    {
        foreach (var r in iniciales ?? Enumerable.Empty<Respaldo>())
            RegistrarAsync(r).GetAwaiter().GetResult();
    }

    /// <summary>Si se asigna, las operaciones lanzan esta excepción (para simular la red caída).</summary>
    public Exception? Error { get; set; }

    public IReadOnlyList<Respaldo> Respaldos => _respaldos;

    public Task<Respaldo> RegistrarAsync(Respaldo respaldo, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;
        if (string.IsNullOrWhiteSpace(respaldo.Archivo) || respaldo.Bytes <= 0)
            throw new ArgumentException("El respaldo no tiene archivo o está vacío.", nameof(respaldo));

        respaldo.Id = _respaldos.Count + 1;
        _respaldos.Add(respaldo);
        return Task.FromResult(respaldo);
    }

    public async Task<Respaldo?> UltimoAsync(CancellationToken ct = default) => (await ListarAsync(1, ct)).FirstOrDefault();

    public Task<IReadOnlyList<Respaldo>> ListarAsync(int limite = 10, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;
        return Task.FromResult<IReadOnlyList<Respaldo>>(
            _respaldos.OrderByDescending(r => r.TerminadoEn).ThenByDescending(r => r.Id).Take(limite).ToList());
    }
}

/// <summary>
/// Generador de respaldos falso (HU-15): no ejecuta pg_dump ni escribe en
/// disco. Informa el avance tabla por tabla y puede simular que el disco
/// externo no está conectado, que pg_dump falla o que tarda.
/// </summary>
public sealed class GeneradorRespaldoFalso : IGeneradorRespaldo
{
    public static readonly string[] Tablas = { "cliente", "comprobante", "comprobante_detalle", "producto", "serie" };

    /// <summary>Carpetas que "existen" (discos conectados); por defecto la del disco externo de prueba.</summary>
    public HashSet<string> Conectadas { get; } = new(StringComparer.OrdinalIgnoreCase) { DatosPrueba.DiscoExterno };

    /// <summary>Si se asigna, GenerarAsync falla con este mensaje después de copiar la mitad.</summary>
    public string? Falla { get; set; }

    /// <summary>Si se asigna, el respaldo no termina hasta que se complete (para ver el progreso a medias).</summary>
    public TaskCompletionSource? Demora { get; set; }

    /// <summary>Hora del respaldo; por defecto la de la PC.</summary>
    public Func<DateTimeOffset> Ahora { get; set; } = () => DateTimeOffset.Now;

    public List<Respaldo> Generados { get; } = new();

    public async Task<Respaldo> GenerarAsync(string carpeta, IProgress<ProgresoRespaldo>? progreso = null, CancellationToken ct = default)
    {
        if (!Conectadas.Contains(carpeta))
            throw new RespaldoException($"No se encuentra la carpeta «{carpeta}». Conecte el disco externo a la PC principal y vuelva a intentarlo.");

        var inicio = Ahora();
        progreso?.Report(new ProgresoRespaldo(0, "Preparando el respaldo…"));
        for (var i = 0; i < Tablas.Length; i++)
        {
            if (i == Tablas.Length / 2)
            {
                if (Demora is not null)
                    await Demora.Task.WaitAsync(ct);
                if (Falla is not null)
                    throw new RespaldoException($"El respaldo no se completó: {Falla}");
            }
            progreso?.Report(new ProgresoRespaldo(5 + 90 * (i + 1) / Tablas.Length, $"Copiando {Tablas[i]} ({i + 1} de {Tablas.Length})…"));
        }
        progreso?.Report(new ProgresoRespaldo(100, "Respaldo terminado."));

        var respaldo = new Respaldo
        {
            Archivo = Respaldo.NombreArchivo(inicio),
            Carpeta = carpeta,
            Bytes = 845_312,
            IniciadoEn = inicio,
            TerminadoEn = Ahora(),
        };
        Generados.Add(respaldo);
        return respaldo;
    }
}
