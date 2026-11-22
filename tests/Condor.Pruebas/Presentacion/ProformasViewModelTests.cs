using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Ventas;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class ProformasViewModelTests
{
    private static readonly DateOnly Hoy = DatosPrueba.Hoy();

    private static async Task<Comprobante> EmitirProformaAsync(ComprobanteRepositorioEnMemoria repo, Cliente? cliente = null)
    {
        var proforma = await repo.EmitirAsync(DatosPrueba.ProformaObra(cliente));
        proforma.TerminalId = 1;
        return proforma;
    }

    private static ProformasViewModel Ventana(ComprobanteRepositorioEnMemoria repo, ImpresoraFalsa? ticketera = null) =>
        new(repo, ticketera is null ? null : new ImpresionViewModel(ticketera, new EmpresaRepositorioEnMemoria(), repo), () => Hoy);

    [Fact]
    public async Task Lista_las_proformas_con_su_vigencia_y_elige_la_mas_reciente_vigente()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var vencida = await EmitirProformaAsync(repo);
        vencida.ValidaHasta = Hoy.AddDays(-1);
        var vigente = await EmitirProformaAsync(repo);
        var vm = Ventana(repo);

        await vm.BuscarAsync();

        Assert.Equal(2, vm.Proformas.Count);
        Assert.Same(vigente, vm.Seleccionada!.Proforma);
        Assert.Equal(EstadoProforma.Vigente, vm.Proformas[0].Estado);
        Assert.StartsWith("Vigente hasta el ", vm.Proformas[0].EstadoTexto);
        Assert.Equal(EstadoProforma.Vencida, vm.Proformas[1].Estado);
        Assert.Contains("1 vigente", vm.Mensaje);
        Assert.True(vm.ConvertirCommand.CanExecute(null));
    }

    [Fact]
    public async Task Busca_por_numero_o_por_cliente()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        await EmitirProformaAsync(repo);
        var otra = await EmitirProformaAsync(repo, DatosPrueba.ClienteDni());
        var vm = Ventana(repo);

        vm.Texto = "PR01-2";
        await vm.BuscarAsync();
        var porNumero = vm.Proformas.Select(f => f.Proforma).ToList();
        vm.Texto = "juan pérez";
        await vm.BuscarAsync();

        Assert.Same(otra, Assert.Single(porNumero));
        Assert.Same(otra, Assert.Single(vm.Proformas).Proforma);
    }

    [Fact]
    public async Task Sin_proformas_explica_como_emitir_una()
    {
        var vm = Ventana(new ComprobanteRepositorioEnMemoria());

        await vm.BuscarAsync();

        Assert.Contains("elija Proforma", vm.Mensaje);
        Assert.False(vm.ConvertirCommand.CanExecute(null));
    }

    [Fact]
    public async Task Convertir_entrega_la_proforma_completa()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var proforma = await EmitirProformaAsync(repo);
        var vm = Ventana(repo);
        Comprobante? elegida = null;
        vm.ProformaElegida += p => elegida = p;
        await vm.BuscarAsync();

        await vm.ConvertirCommand.EjecutarAsync();

        Assert.Same(proforma, elegida);
        Assert.Equal(2, elegida!.Detalle.Count);
    }

    [Fact]
    public async Task Una_proforma_convertida_o_vencida_no_se_puede_convertir()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var convertida = await EmitirProformaAsync(repo);
        convertida.ConvertidaEn = "F001-00000007";
        var vencida = await EmitirProformaAsync(repo);
        vencida.ValidaHasta = Hoy.AddDays(-3);
        var vm = Ventana(repo);
        await vm.BuscarAsync();

        Assert.All(vm.Proformas, f => Assert.False(f.PuedeConvertir));
        vm.Seleccionada = vm.Proformas.Single(f => f.Proforma == convertida);
        Assert.False(vm.ConvertirCommand.CanExecute(null));
        Assert.Equal("Convertida en F001-00000007", vm.Seleccionada.EstadoTexto);
    }

    [Fact]
    public async Task Si_otra_PC_la_convirtio_mientras_estaba_abierta_avisa_y_no_la_entrega()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        var proforma = await EmitirProformaAsync(repo);
        var vm = Ventana(repo);
        var elegidas = 0;
        vm.ProformaElegida += _ => elegidas++;
        await vm.BuscarAsync();

        // Otra PC la convierte: la base central ya la tiene enlazada a una factura.
        var factura = DatosPrueba.FacturaObra();
        factura.ProformaId = proforma.Id;
        await repo.EmitirAsync(factura);
        await vm.ConvertirCommand.EjecutarAsync();

        Assert.Equal(0, elegidas);
        Assert.Contains($"ya se convirtió en {factura.NumeroCompleto}", vm.Mensaje);
        Assert.Equal(EstadoProforma.Convertida, vm.Seleccionada!.Estado);
    }

    [Fact]
    public async Task Reimprime_la_proforma_elegida()
    {
        var repo = new ComprobanteRepositorioEnMemoria();
        await EmitirProformaAsync(repo);
        var ticketera = new ImpresoraFalsa();
        var vm = Ventana(repo, ticketera);
        await vm.BuscarAsync();

        await vm.ReimprimirCommand.EjecutarAsync();

        Assert.Contains("PROFORMA", Assert.Single(ticketera.Impresos).ComoTexto());
        Assert.Contains("reimpresa", vm.Mensaje);
    }

    [Fact]
    public async Task Sin_conexion_avisa()
    {
        var vm = new ProformasViewModel(new SinConexion(), hoy: () => Hoy);
        var avisos = 0;
        vm.ConexionPerdida += () => avisos++;

        await vm.BuscarAsync();

        Assert.Equal(1, avisos);
        Assert.Contains("Sin conexión", vm.Mensaje);
    }

    private sealed class SinConexion : IComprobanteRepositorio
    {
        private static Exception Falla() => new SinConexionException(new TimeoutException());

        public Task<Comprobante> EmitirAsync(Comprobante comprobante, CancellationToken ct = default) => throw Falla();

        public Task<Comprobante?> ObtenerAsync(long id, CancellationToken ct = default) => throw Falla();

        public Task<IReadOnlyList<Comprobante>> ListarRecientesAsync(int limite = 30, CancellationToken ct = default) => throw Falla();

        public Task RegistrarImpresionAsync(long id, CancellationToken ct = default) => throw Falla();

        public Task<IReadOnlyList<Comprobante>> ListarProformasAsync(string? texto, int limite = 100, CancellationToken ct = default) => throw Falla();

        public Task<ResultadoComprobantes> BuscarAsync(FiltroComprobantes filtro, CancellationToken ct = default) => throw Falla();
    }
}

/// <summary>La venta que sale de una proforma, en la pantalla Nueva venta (HU-09, T-73).</summary>
public class ConversionProformaTests
{
    private sealed record Pantalla(MainViewModel Vm, ComprobanteRepositorioEnMemoria Comprobantes);

    private static Pantalla Crear()
    {
        var comprobantes = new ComprobanteRepositorioEnMemoria();
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()),
            new ClienteRepositorioEnMemoria(DatosPrueba.Clientes()), comprobantes);
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(new Terminal { Id = 1, Nombre = "PC-1" }));
        return new Pantalla(vm, comprobantes);
    }

    private static async Task AgregarAsync(MainViewModel vm, string texto, string cantidad)
    {
        vm.Buscador!.Texto = texto;
        await vm.Buscador.UltimaBusqueda;
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.Venta.CantidadTexto = cantidad;
        Assert.True(vm.Venta.Agregar());
    }

    [Fact]
    public async Task Emitir_una_proforma_la_guarda_con_PR01_y_su_vigencia()
    {
        var (vm, comprobantes) = Crear();
        await AgregarAsync(vm, "cemento", "40");
        vm.Emision!.EsProforma = true;

        await vm.Emision.EmitirCommand.EjecutarAsync();

        var proforma = Assert.Single(comprobantes.Comprobantes);
        Assert.Null(vm.Emision.Error);
        Assert.Equal("PR01-00000001", proforma.NumeroCompleto);
        Assert.Equal(EmisionComprobante.VigenciaProforma(DatosPrueba.Hoy()), proforma.ValidaHasta);
        Assert.Contains("Proforma PR01-00000001 emitida", vm.Mensaje);
    }

    [Fact]
    public async Task Convertir_lleva_el_detalle_y_el_cliente_sin_reescribirlos_y_propone_factura()
    {
        var (vm, comprobantes) = Crear();
        var proforma = await comprobantes.EmitirAsync(DatosPrueba.ProformaObra());

        vm.ConvertirProforma(proforma);

        Assert.Equal(proforma.Detalle.Select(l => (l.Descripcion, l.Cantidad, l.PrecioUnitario)),
            vm.Venta.Detalle.Select(l => (l.Descripcion, l.Cantidad, l.PrecioUnitario)));
        Assert.Equal(proforma.Total, vm.Venta.Total);
        Assert.Equal(proforma.Cliente!.NumeroDocumento, vm.Cliente!.Seleccionado!.NumeroDocumento);
        Assert.True(vm.Emision!.EsFactura);
        Assert.Same(proforma, vm.Emision.ProformaOrigen);
        Assert.Contains(proforma.NumeroCompleto, vm.Emision.AvisoProforma);
        Assert.Contains("Revise y pulse Emitir", vm.Mensaje);
    }

    [Fact]
    public async Task Al_emitir_la_factura_queda_enlazada_a_la_proforma()
    {
        var (vm, comprobantes) = Crear();
        var proforma = await comprobantes.EmitirAsync(DatosPrueba.ProformaObra());
        vm.ConvertirProforma(proforma);

        await vm.Emision!.EmitirCommand.EjecutarAsync();

        var factura = comprobantes.Comprobantes[^1];
        Assert.Null(vm.Emision.Error);
        Assert.Equal("F001-00000001", factura.NumeroCompleto);
        Assert.Equal(proforma.Id, factura.ProformaId);
        Assert.Equal(factura.NumeroCompleto, proforma.ConvertidaEn);
        Assert.Null(vm.Emision.ProformaOrigen); // la siguiente venta ya no sale de la proforma
        Assert.Contains("desde la proforma", vm.Mensaje);
    }

    [Fact]
    public async Task Sin_cliente_se_propone_boleta_y_con_DNI_boleta_con_DNI()
    {
        var (vm, comprobantes) = Crear();
        var sinCliente = await comprobantes.EmitirAsync(EmisionComprobante.Preparar(
            new SolicitudEmision(TipoEmision.Proforma, DatosPrueba.NotaVentaMostrador().Detalle)));
        var dni = DatosPrueba.ClienteDni();
        dni.Id = 1;
        var conDni = await comprobantes.EmitirAsync(DatosPrueba.ProformaObra(dni));

        vm.ConvertirProforma(sinCliente);
        var tipoSinCliente = vm.Emision!.Tipo;
        vm.ConvertirProforma(conDni);

        Assert.Equal(TipoEmision.Boleta, tipoSinCliente);
        Assert.Equal(TipoEmision.BoletaConDni, vm.Emision.Tipo);
        Assert.Same(conDni, vm.Emision.ProformaOrigen);
    }

    [Fact]
    public async Task Cancelar_la_venta_suelta_la_proforma()
    {
        var (vm, comprobantes) = Crear();
        var proforma = await comprobantes.EmitirAsync(DatosPrueba.ProformaObra());
        vm.ConvertirProforma(proforma);

        vm.CancelarVenta();

        Assert.Null(vm.Emision!.ProformaOrigen);
        Assert.Null(vm.Emision.AvisoProforma);
        Assert.Empty(vm.Venta.Detalle);
    }

    [Fact]
    public async Task Si_otra_PC_la_convirtio_antes_no_se_emite_y_se_avisa()
    {
        var (vm, comprobantes) = Crear();
        var proforma = await comprobantes.EmitirAsync(DatosPrueba.ProformaObra());
        vm.ConvertirProforma(proforma);
        var otraPc = DatosPrueba.FacturaObra();
        otraPc.ProformaId = proforma.Id;
        await comprobantes.EmitirAsync(otraPc);
        proforma.ConvertidaEn = null; // en esta PC aún se ve vigente
        var emitidas = comprobantes.Comprobantes.Count;

        await vm.Emision!.EmitirCommand.EjecutarAsync();

        Assert.Equal(emitidas, comprobantes.Comprobantes.Count);
        Assert.Contains("ya se convirtió en venta", vm.Emision.Error);
        Assert.Equal(2, vm.Venta.Detalle.Count); // la venta sigue en pantalla
    }

    [Fact]
    public async Task La_proforma_se_puede_completar_con_otro_producto_antes_de_emitir()
    {
        var (vm, comprobantes) = Crear();
        var proforma = await comprobantes.EmitirAsync(DatosPrueba.ProformaObra());
        vm.ConvertirProforma(proforma);

        await AgregarAsync(vm, "clavo", "2");
        await vm.Emision!.EmitirCommand.EjecutarAsync();

        var factura = comprobantes.Comprobantes[^1];
        Assert.Equal(3, factura.Detalle.Count);
        Assert.Equal(proforma.Total + 15.00m, factura.Total);
        Assert.Equal(proforma.Id, factura.ProformaId);
    }
}
