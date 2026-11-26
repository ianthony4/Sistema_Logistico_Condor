using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-11: registrar compras a proveedores.
/// Criterio 1: si el proveedor está registrado, al registrar una compra con el
/// número del comprobante del proveedor, la fecha y el detalle de productos
/// con su costo, el sistema la guarda con su total e IGV y la muestra en
/// Compras (F4).
/// Criterio 2: si ese comprobante del proveedor ya se registró, el sistema no
/// lo vuelve a registrar (aunque lo intente otra PC a la vez) y avisa.
/// Criterio 3: al filtrar por proveedor o rango de fechas, el sistema muestra
/// las compras y el total de egresos del período.
/// </summary>
/// <remarks>Usa la base de pruebas.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU11_ComprasTests
{
    private readonly BaseDatosFixture _bd;

    public HU11_ComprasTests(BaseDatosFixture bd) => _bd = bd;

    private static DateOnly Hoy => DatosPrueba.Hoy();

    [FactConBD]
    public async Task CA1_la_compra_se_guarda_con_su_total_e_IGV_y_aparece_en_Compras()
    {
        var (vm, proveedor) = await PantallaAsync();
        var compras = vm.CrearCompras()!;
        await compras.BuscarAsync();
        var dialogo = compras.CrearNuevaCompra()!;

        await LlenarAsync(dialogo, proveedor.Ruc, "F001", "12345", ("Fierro", "50", "31.50"), ("Clavo", "10", "6.20"));
        await dialogo.GuardarCommand.EjecutarAsync();
        await compras.CompraRegistradaAsync(dialogo.Guardado!);

        Assert.Null(dialogo.Error);
        var guardada = await new CompraRepositorio(_bd.Conexiones).ObtenerAsync(dialogo.Guardado!.Id);
        Assert.Equal("Factura F001-00012345", guardada!.DocumentoCompleto);
        Assert.Equal(proveedor.Ruc, guardada.Proveedor!.Ruc);
        Assert.Equal(1637.00m, guardada.Total);
        Assert.Equal(1387.29m, guardada.OpGravada);
        Assert.Equal(249.71m, guardada.Igv);
        Assert.Equal(2, guardada.Detalle.Count);
        Assert.Equal("PC-HU11", guardada.TerminalNombre);

        Assert.Equal(guardada.Id, compras.Seleccionada!.Id);
        await compras.UltimoDetalle;
        Assert.Equal(2, compras.Detalle.Count);
        Assert.Contains("Compra registrada", compras.Mensaje);
    }

    [FactConBD]
    public async Task CA2_el_mismo_comprobante_del_proveedor_no_se_registra_dos_veces_y_se_avisa()
    {
        var (vm, proveedor) = await PantallaAsync();
        var primero = vm.CrearCompras()!.CrearNuevaCompra()!;
        await LlenarAsync(primero, proveedor.Ruc, "E001", "731", ("Cemento", "40", "27.80"));
        await primero.GuardarCommand.EjecutarAsync();
        Assert.NotNull(primero.Guardado);

        // Días después, en otra PC, alguien vuelve a digitar la misma factura.
        var (otra, _) = await PantallaAsync();
        var repetido = otra.CrearCompras()!.CrearNuevaCompra()!;
        await LlenarAsync(repetido, proveedor.Ruc, "e001", "731", ("Cemento", "40", "27.80"));
        await repetido.GuardarCommand.EjecutarAsync();

        Assert.Null(repetido.Guardado);
        Assert.Contains("Factura E001-00000731", repetido.Error);
        Assert.Contains("ya está registrada", repetido.Error);
        Assert.Equal(1L, await ContarAsync(proveedor.Id));
    }

    [FactConBD]
    public async Task CA2_dos_PC_guardando_la_misma_factura_a_la_vez_solo_registran_una()
    {
        await using var local = new TresPC();
        var proveedor = await ProveedorAsync();
        var dialogos = new List<NuevaCompraViewModel>();
        foreach (var pc in local.Pc.Take(2))
        {
            var vm = await PantallaAsync(pc);
            var dialogo = vm.CrearCompras()!.CrearNuevaCompra()!;
            await LlenarAsync(dialogo, proveedor.Ruc, "F002", "88", ("Fierro", "5", "31.50"));
            dialogos.Add(dialogo);
        }

        await Task.WhenAll(dialogos.Select(d => Task.Run(() => d.GuardarCommand.EjecutarAsync())));

        Assert.Single(dialogos, d => d.Guardado is not null);
        Assert.Contains("ya está registrada", dialogos.Single(d => d.Guardado is null).Error);
        Assert.Equal(1L, await ContarAsync(proveedor.Id));
    }

    [FactConBD]
    public async Task CA3_por_proveedor_y_rango_de_fechas_muestra_sus_compras_y_el_total_de_egresos()
    {
        var (vm, aceros) = await PantallaAsync();
        var yura = await ProveedorAsync();
        await RegistrarAsync(vm, aceros, "F001", "1", Hoy.AddDays(-45), ("Fierro", "10", "31.50"));   // S/ 315.00, fuera del mes
        await RegistrarAsync(vm, aceros, "F001", "2", Hoy.AddDays(-3), ("Fierro", "20", "31.50"));    // S/ 630.00
        await RegistrarAsync(vm, aceros, "F001", "3", Hoy, ("Clavo", "100", "6.20"));                // S/ 620.00
        await RegistrarAsync(vm, yura, "B004", "9", Hoy, ("Cemento", "40", "27.80"));                // otro proveedor

        var compras = vm.CrearCompras()!;
        compras.Proveedor = aceros.Ruc;
        compras.Desde = Hoy.AddDays(-30).ToDateTime(TimeOnly.MinValue);
        compras.Hasta = Hoy.ToDateTime(TimeOnly.MinValue);
        await compras.BuscarCommand.EjecutarAsync();

        Assert.Equal(new[] { "F001-00000003", "F001-00000002" }, compras.Compras.Select(c => c.Documento));
        Assert.Equal(1250.00m, compras.Total);
        Assert.Equal("2 compras · Egresos del período: S/ 1250.00", compras.Resumen);

        await compras.LimpiarCommand.EjecutarAsync(); // Todo: sin fechas ni proveedor
        compras.Proveedor = aceros.Ruc;
        await compras.BuscarCommand.EjecutarAsync();
        Assert.Equal(3, compras.Encontradas);
        Assert.Equal(1565.00m, compras.Total);
    }

    /// <summary>Pantalla principal contra la base de pruebas, con un proveedor registrado.</summary>
    private async Task<(MainViewModel Vm, Proveedor Proveedor)> PantallaAsync()
    {
        var vm = await PantallaAsync(_bd.Conexiones);
        return (vm, await ProveedorAsync());
    }

    private async Task<MainViewModel> PantallaAsync(FabricaConexiones pc)
    {
        var productos = new ProductoRepositorio(pc);
        foreach (var p in DatosPrueba.Catalogo())
        {
            p.Nombre = DatosPrueba.NombreUnico(p.Nombre.Split(' ')[0]);
            await productos.RegistrarAsync(p);
        }

        var vm = new MainViewModel(productos, new ClienteRepositorio(pc), new ComprobanteRepositorio(pc),
            new ProveedorRepositorio(pc), new CompraRepositorio(pc));
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU11")));
        return vm;
    }

    /// <summary>Proveedor con un RUC nuevo, para que las ejecuciones no choquen.</summary>
    private async Task<Proveedor> ProveedorAsync()
    {
        var proveedor = DatosPrueba.ProveedorAceros(DatosPrueba.RucNuevo());
        proveedor.RazonSocial = DatosPrueba.NombreUnico("Aceros del Sur");
        return await new ProveedorRepositorio(_bd.Conexiones).RegistrarAsync(proveedor);
    }

    /// <summary>Llena el diálogo Nueva compra: proveedor por RUC, factura, fecha de hoy y los productos.</summary>
    private static async Task LlenarAsync(NuevaCompraViewModel dialogo, string ruc, string serie, string numero,
        params (string Producto, string Cantidad, string Costo)[] lineas)
    {
        dialogo.DocumentoProveedor = ruc;
        await dialogo.BuscarProveedorCommand.EjecutarAsync();
        Assert.NotNull(dialogo.Proveedor);
        dialogo.Serie = serie;
        dialogo.NumeroTexto = numero;
        foreach (var (producto, cantidad, costo) in lineas)
        {
            dialogo.Buscador.Texto = producto;
            await dialogo.Buscador.UltimaBusqueda;
            Assert.NotNull(dialogo.Buscador.Seleccionado);
            dialogo.Buscador.Elegir(dialogo.Buscador.Seleccionado);
            dialogo.CantidadTexto = cantidad;
            dialogo.CostoTexto = costo;
            Assert.True(dialogo.Agregar());
        }
    }

    private static async Task RegistrarAsync(MainViewModel vm, Proveedor proveedor, string serie, string numero, DateOnly fecha,
        params (string Producto, string Cantidad, string Costo)[] lineas)
    {
        var dialogo = vm.CrearCompras()!.CrearNuevaCompra()!;
        await LlenarAsync(dialogo, proveedor.Ruc, serie, numero, lineas);
        dialogo.Fecha = fecha.ToDateTime(TimeOnly.MinValue);
        await dialogo.GuardarCommand.EjecutarAsync();
        Assert.Null(dialogo.Error);
    }

    private async Task<long> ContarAsync(long proveedorId)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM compra WHERE proveedor_id = @p", conexion);
        cmd.Parameters.AddWithValue("p", proveedorId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
