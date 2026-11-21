using Condor.Dominio.Entidades;
using Condor.Dominio.Sunat;

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
/// Proveedores de la ferretería (HU-04). Lo implementa Condor.Datos.
/// </summary>
public interface IProveedorRepositorio
{
    /// <summary>
    /// Registra un proveedor nuevo y lo devuelve con su Id. Lanza
    /// ProveedorDuplicadoException si el RUC ya estaba registrado.
    /// </summary>
    Task<Proveedor> RegistrarAsync(Proveedor proveedor, CancellationToken ct = default);

    /// <summary>Proveedor con ese RUC; null si aún no está registrado.</summary>
    Task<Proveedor?> BuscarPorRucAsync(string ruc, CancellationToken ct = default);

    /// <summary>
    /// Proveedores activos por RUC (inicio) o por parte de la razón social o del
    /// nombre comercial; con el texto vacío, todos. Ordenados por razón social.
    /// </summary>
    Task<IReadOnlyList<Proveedor>> BuscarAsync(string? texto, int limite = 200, CancellationToken ct = default);
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

    /// <summary>
    /// Proformas (sin detalle) para convertirlas en venta (HU-09, T-71): por su
    /// número (PR01-00000004 o solo 4), por el DNI o RUC del cliente (inicio)
    /// o por parte de su nombre, sin distinguir mayúsculas ni tildes. Con el
    /// texto vacío, todas. De la más reciente a la más antigua; cada una dice
    /// si ya se convirtió y en qué comprobante.
    /// </summary>
    Task<IReadOnlyList<Comprobante>> ListarProformasAsync(string? texto, int limite = 100, CancellationToken ct = default);
}

/// <summary>
/// Respuestas de la SUNAT a cada comprobante (HU-07). Lo implementa Condor.Datos.
/// </summary>
public interface IEnvioSunatRepositorio
{
    /// <summary>
    /// Guarda lo que respondió la SUNAT: el estado del comprobante, el XML
    /// firmado, el CDR y suma un intento. Un estado definitivo (aceptado,
    /// observado o rechazado) ya no se cambia.
    /// </summary>
    Task RegistrarRespuestaAsync(long comprobanteId, RespuestaSunat respuesta, CancellationToken ct = default);

    /// <summary>Anota un intento que no llegó a la SUNAT (sin internet); el comprobante sigue PENDIENTE.</summary>
    Task RegistrarFallaAsync(long comprobanteId, string motivo, CancellationToken ct = default);

    /// <summary>Boletas y facturas aún pendientes de envío, de la más antigua a la más reciente (HU-17).</summary>
    Task<IReadOnlyList<long>> ListarPendientesAsync(int limite = 100, CancellationToken ct = default);

    /// <summary>
    /// Aparta para esta PC los pendientes que toca reenviar (HU-17, T-55): los
    /// emitidos hace más de <paramref name="espera"/> cuyo último intento
    /// también pasó la espera (más larga cuantos más intentos lleva) y que
    /// ninguna otra PC está reenviando. Devuelve sus Id, del más antiguo al
    /// más reciente. La reserva se libera al registrar la respuesta o la falla.
    /// </summary>
    Task<IReadOnlyList<long>> ReservarPendientesAsync(string terminal, TimeSpan espera, int limite = 20, CancellationToken ct = default);

    /// <summary>Devuelve los apartados que esta PC no llegó a enviar (se cortó internet), sin sumar intentos.</summary>
    Task LiberarAsync(string terminal, IReadOnlyCollection<long> comprobanteIds, CancellationToken ct = default);

    /// <summary>Cuántas boletas y facturas siguen pendientes y desde cuándo (para el aviso de la pantalla).</summary>
    Task<ResumenPendientes> ResumenPendientesAsync(CancellationToken ct = default);
}

/// <summary>
/// Respaldos manuales de la base de datos (HU-15). Lo implementa Condor.Datos.
/// </summary>
public interface IRespaldoRepositorio
{
    /// <summary>Anota un respaldo ya generado y lo devuelve con su Id.</summary>
    Task<Respaldo> RegistrarAsync(Respaldo respaldo, CancellationToken ct = default);

    /// <summary>El respaldo más reciente, hecho desde cualquier PC; null si nunca se hizo uno.</summary>
    Task<Respaldo?> UltimoAsync(CancellationToken ct = default);

    /// <summary>Últimos respaldos, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<Respaldo>> ListarAsync(int limite = 10, CancellationToken ct = default);
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
