using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Ticketera falsa: guarda los tickets "impresos" y puede simular que está
/// apagada o sin papel.
/// </summary>
public sealed class ImpresoraFalsa : IImpresoraTickets
{
    public string Nombre => "POS-80 (prueba)";

    /// <summary>Si se asigna, ImprimirAsync falla con este mensaje (ticketera apagada, sin papel...).</summary>
    public string? Falla { get; set; }

    public List<Ticket> Impresos { get; } = new();

    public Task ImprimirAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (Falla is not null)
            throw new ImpresionException(Falla);
        Impresos.Add(ticket);
        return Task.CompletedTask;
    }
}

/// <summary>Datos de la empresa sin base de datos.</summary>
public sealed class EmpresaRepositorioEnMemoria : IEmpresaRepositorio
{
    public Task<Empresa> ObtenerAsync(CancellationToken ct = default) => Task.FromResult(DatosPrueba.Empresa());
}
