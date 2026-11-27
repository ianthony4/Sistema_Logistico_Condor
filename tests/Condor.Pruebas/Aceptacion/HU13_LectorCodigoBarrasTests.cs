using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-13: ingreso con lector de código de barras.
/// Criterio 1: si el producto tiene código de barras, al pasarlo por el lector
/// en el campo Producto el sistema lo agrega al detalle con una unidad (o la
/// suma a su línea) sin escribir el nombre.
/// Criterio 2: si el producto aún no tiene código, al elegirlo en Catálogo
/// (F2) y pasar el lector el sistema guarda el código, que no puede ser de
/// otro producto, y desde entonces se vende con el lector en las tres PC.
/// Criterio 3: si el código leído no está en el catálogo, el sistema avisa y
/// Nuevo producto lo propone como código del producto nuevo.
/// </summary>
/// <remarks>Usa la base de pruebas.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU13_LectorCodigoBarrasTests
{
    private readonly BaseDatosFixture _bd;

    public HU13_LectorCodigoBarrasTests(BaseDatosFixture bd) => _bd = bd;

    private static string CodigoNuevo() => "775" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999);

    [FactConBD]
    public async Task CA1_pasar_el_producto_por_el_lector_lo_agrega_y_otra_pasada_suma_una_unidad()
    {
        var codigo = CodigoNuevo();
        var producto = DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS");
        producto.CodigoBarras = codigo;
        await new ProductoRepositorio(_bd.Conexiones).RegistrarAsync(producto);
        var vm = Pantalla();

        vm.Buscador!.Texto = codigo; // el lector escribe el código y Enter
        Assert.True(await vm.EscanearAsync(vm.Buscador.Texto));
        vm.Buscador.Texto = codigo;
        await vm.EscanearAsync(vm.Buscador.Texto);

        var linea = Assert.Single(vm.Venta.Detalle);
        Assert.Equal(producto.Nombre, linea.Descripcion);
        Assert.Equal(2, linea.Cantidad);
        Assert.Equal(65.00m, vm.Venta.Total);
        Assert.Equal(string.Empty, vm.Buscador.Texto);
    }

    [FactConBD]
    public async Task CA1_la_venta_con_el_lector_se_emite_como_cualquier_otra()
    {
        var codigo = CodigoNuevo();
        var producto = DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Silicona"), 18.90m);
        producto.CodigoBarras = codigo;
        await new ProductoRepositorio(_bd.Conexiones).RegistrarAsync(producto);
        var vm = Pantalla();
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(_bd.Conexiones).RegistrarAccesoAsync("PC-HU13")));

        await vm.EscanearAsync(codigo);
        await vm.Emision!.EmitirCommand.EjecutarAsync();

        Assert.Null(vm.Emision.Error);
        var boleta = await new ComprobanteRepositorio(_bd.Conexiones).ObtenerAsync(vm.Emision.Ultimo!.Id);
        Assert.Equal(producto.Id, Assert.Single(boleta!.Detalle).ProductoId);
    }

    [FactConBD]
    public async Task CA2_asignar_el_codigo_en_el_catalogo_y_venderlo_desde_otra_PC()
    {
        await using var local = new TresPC();
        var nombre = DatosPrueba.NombreUnico("Lija al agua");
        var producto = await new ProductoRepositorio(local.Pc[0]).RegistrarAsync(DatosPrueba.Nuevo(nombre, 2.50m));
        var codigo = CodigoNuevo();

        // PC-1: Catálogo (F2), buscar el producto y pasarlo por el lector.
        var catalogo = new CatalogoViewModel(new ProductoRepositorio(local.Pc[0]), () => new NuevoProductoViewModel(new ProductoRepositorio(local.Pc[0])))
        {
            Texto = nombre,
        };
        await catalogo.BuscarAsync();
        catalogo.Codigo = codigo;
        await catalogo.AsignarCodigoCommand.EjecutarAsync();

        // PC-2: ya se vende con el lector.
        var pc2 = new MainViewModel(new ProductoRepositorio(local.Pc[1]));
        await pc2.EscanearAsync(codigo);

        Assert.Null(catalogo.Error);
        Assert.Equal(codigo, catalogo.Seleccionado!.CodigoBarras);
        Assert.Equal(producto.Id, Assert.Single(pc2.Venta.Detalle).ProductoId);
    }

    [FactConBD]
    public async Task CA2_un_codigo_que_ya_es_de_otro_producto_no_se_asigna()
    {
        var repo = new ProductoRepositorio(_bd.Conexiones);
        var codigo = CodigoNuevo();
        var cemento = DatosPrueba.Nuevo(DatosPrueba.NombreUnico("Cemento"), 32.50m, "BLS");
        cemento.CodigoBarras = codigo;
        await repo.RegistrarAsync(cemento);
        var nombre = DatosPrueba.NombreUnico("Yeso");
        await repo.RegistrarAsync(DatosPrueba.Nuevo(nombre, 12.00m));
        var catalogo = new CatalogoViewModel(repo, () => new NuevoProductoViewModel(repo)) { Texto = nombre };
        await catalogo.BuscarAsync();

        catalogo.Codigo = codigo;
        await catalogo.AsignarCodigoCommand.EjecutarAsync();

        Assert.Contains(cemento.Nombre, catalogo.Error);
        Assert.Null(catalogo.Seleccionado!.CodigoBarras);
        Assert.Equal(cemento.Nombre, (await repo.BuscarPorCodigoBarrasAsync(codigo))!.Nombre);
    }

    [FactConBD]
    public async Task CA3_un_codigo_que_no_esta_en_el_catalogo_se_avisa_y_se_registra_con_Nuevo_producto()
    {
        var codigo = CodigoNuevo();
        var vm = Pantalla();

        vm.Buscador!.Texto = codigo;
        await vm.EscanearAsync(vm.Buscador.Texto);
        var aviso = vm.Mensaje;
        await vm.Buscador.UltimaBusqueda;
        var nuevo = vm.CrearNuevoProducto()!;
        nuevo.Nombre = DatosPrueba.NombreUnico("Pegamento PVC");
        nuevo.PrecioTexto = "12.50";
        await nuevo.GuardarCommand.EjecutarAsync();
        vm.ProductoRegistrado(nuevo.Guardado!);
        vm.Venta.Cancelar();
        await vm.EscanearAsync(codigo);

        Assert.Contains("no está en el catálogo", aviso);
        Assert.Equal(codigo, nuevo.Guardado!.CodigoBarras);
        Assert.Equal(nuevo.Guardado.Id, Assert.Single(vm.Venta.Detalle).ProductoId);
    }

    private MainViewModel Pantalla()
    {
        var pc = _bd.Conexiones;
        var comprobantes = new ComprobanteRepositorio(pc);
        var vm = new MainViewModel(new ProductoRepositorio(pc), new ClienteRepositorio(pc), comprobantes);
        vm.Buscador!.Espera = TimeSpan.Zero;
        return vm;
    }
}
