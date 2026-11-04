using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Repositorio de clientes falso para probar los ViewModel sin base de datos.
/// Aplica las mismas reglas que el real: valida y no admite documentos repetidos.
/// </summary>
public sealed class ClienteRepositorioEnMemoria : IClienteRepositorio
{
    private readonly List<Cliente> _clientes = new();
    private long _siguienteId = 1;

    public ClienteRepositorioEnMemoria(IEnumerable<Cliente>? iniciales = null)
    {
        foreach (var c in iniciales ?? Enumerable.Empty<Cliente>())
            RegistrarAsync(c).GetAwaiter().GetResult();
    }

    /// <summary>Si se asigna, las dos operaciones lanzan esta excepción (para simular la red caída).</summary>
    public Exception? Error { get; set; }

    public int Busquedas { get; private set; }

    public IReadOnlyList<Cliente> Clientes => _clientes;

    public Task<Cliente?> BuscarPorDocumentoAsync(TipoDocumento tipo, string numero, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        Busquedas++;
        numero = ValidadorDocumento.Limpiar(numero);
        return Task.FromResult(_clientes.FirstOrDefault(c => c.TipoDocumento == tipo && c.NumeroDocumento == numero));
    }

    public Task<Cliente> RegistrarAsync(Cliente cliente, CancellationToken ct = default)
    {
        if (Error is not null)
            throw Error;

        ValidadorCliente.Validar(cliente);
        if (_clientes.Any(c => c.TipoDocumento == cliente.TipoDocumento && c.NumeroDocumento == cliente.NumeroDocumento))
            throw new ClienteDuplicadoException(cliente.TipoDocumento, cliente.NumeroDocumento);

        cliente.Id = _siguienteId++;
        _clientes.Add(cliente);
        return Task.FromResult(cliente);
    }
}
