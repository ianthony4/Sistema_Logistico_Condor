using Condor.Dominio.Entidades;

namespace Condor.Dominio.Contratos;

/// <summary>
/// Acceso al catálogo de productos (HU-01, HU-02). Lo implementa Condor.Datos.
/// </summary>
public interface IProductoRepositorio
{
    /// <summary>Registra un producto nuevo y devuelve el producto con su Id.</summary>
    Task<Producto> RegistrarAsync(Producto producto, CancellationToken ct = default);

    /// <summary>Productos activos cuyo nombre contiene el texto, los que empiezan por él primero.</summary>
    Task<IReadOnlyList<Producto>> BuscarPorNombreAsync(string texto, int limite = 20, CancellationToken ct = default);

    /// <summary>Todo el catálogo activo, ordenado por nombre.</summary>
    Task<IReadOnlyList<Producto>> ListarAsync(CancellationToken ct = default);
}

/// <summary>
/// Clientes de los comprobantes nominativos (HU-03). Lo implementa Condor.Datos.
/// </summary>
public interface IClienteRepositorio
{
    /// <summary>Cliente con ese DNI o RUC; null si aún no está registrado.</summary>
    Task<Cliente?> BuscarPorDocumentoAsync(TipoDocumento tipo, string numero, CancellationToken ct = default);

    /// <summary>
    /// Registra un cliente nuevo y lo devuelve con su Id. Lanza
    /// ClienteDuplicadoException si el documento ya estaba registrado.
    /// </summary>
    Task<Cliente> RegistrarAsync(Cliente cliente, CancellationToken ct = default);
}

/// <summary>
/// Datos de la ferretería para la cabecera de los comprobantes (HU-12).
/// </summary>
public interface IEmpresaRepositorio
{
    Task<Empresa> ObtenerAsync(CancellationToken ct = default);
}

/// <summary>
/// Comprobantes emitidos (HU-05, HU-12). Lo implementa Condor.Datos.
/// </summary>
public interface IComprobanteRepositorio
{
    /// <summary>
    /// Guarda el comprobante preparado (EmisionComprobante.Preparar) con el
    /// siguiente número de su serie, todo en una sola transacción: si algo
    /// falla no queda ni el comprobante ni el número usado. Devuelve el
    /// comprobante con su Id, número y fecha.
    /// </summary>
    Task<Comprobante> EmitirAsync(Comprobante comprobante, CancellationToken ct = default);

    /// <summary>Comprobante con su detalle y su cliente, para reimprimirlo; null si no existe.</summary>
    Task<Comprobante?> ObtenerAsync(long id, CancellationToken ct = default);

    /// <summary>Últimos comprobantes emitidos (sin detalle), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<Comprobante>> ListarRecientesAsync(int limite = 30, CancellationToken ct = default);

    /// <summary>Anota que el comprobante salió impreso (suma una impresión).</summary>
    Task RegistrarImpresionAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// Numeración de comprobantes en la base de datos central (HU-16).
/// </summary>
public interface ISerieRepositorio
{
    /// <summary>
    /// Reserva y devuelve el siguiente número de la serie. Es seguro aunque
    /// varias terminales lo pidan en el mismo instante.
    /// </summary>
    Task<long> SiguienteCorrelativoAsync(string codigoSerie, CancellationToken ct = default);
}

/// <summary>
/// Registro de las PC que usan el sistema (HU-14).
/// </summary>
public interface ITerminalRepositorio
{
    /// <summary>Registra la terminal si es nueva y actualiza su último acceso.</summary>
    Task<Terminal> RegistrarAccesoAsync(string nombre, CancellationToken ct = default);
}
