using Condor.Dominio.Entidades;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Presentacion;

public class VentaViewModelTests
{
    private static Producto ConId(Producto p, long id)
    {
        p.Id = id;
        return p;
    }

    private static void Agregar(VentaViewModel venta, Producto producto, string cantidad)
    {
        venta.ProductoPorAgregar = producto;
        venta.CantidadTexto = cantidad;
        Assert.True(venta.Agregar());
    }

    [Fact]
    public void Agregar_lineas_recalcula_gravada_IGV_y_total()
    {
        var venta = new VentaViewModel();

        Agregar(venta, ConId(DatosPrueba.Cemento(), 1), "10");
        Agregar(venta, ConId(DatosPrueba.Fierro(), 2), "8");
        Agregar(venta, ConId(DatosPrueba.EsmalteBlanco(), 3), "2");

        Assert.Equal(new[] { 1, 2, 3 }, venta.Detalle.Select(l => l.Item));
        Assert.Equal(745.00m, venta.Total);
        Assert.Equal(631.36m, venta.OpGravada);
        Assert.Equal(113.64m, venta.Igv);
    }

    [Fact]
    public void El_mismo_producto_suma_cantidad_en_su_linea()
    {
        var venta = new VentaViewModel();
        var cemento = ConId(DatosPrueba.Cemento(), 1);

        Agregar(venta, cemento, "2");
        Agregar(venta, cemento, "3");

        var linea = Assert.Single(venta.Detalle);
        Assert.Equal(5m, linea.Cantidad);
        Assert.Equal(162.50m, venta.Total);
    }

    [Fact]
    public void Corregir_la_cantidad_en_la_tabla_actualiza_el_total()
    {
        var venta = new VentaViewModel();
        Agregar(venta, ConId(DatosPrueba.Clavos(), 1), "1");

        venta.Detalle[0].Cantidad = 2.5m;

        Assert.Equal(18.75m, venta.Total);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("dos")]
    public void Cantidad_invalida_no_agrega_y_avisa(string cantidad)
    {
        var venta = new VentaViewModel { ProductoPorAgregar = ConId(DatosPrueba.Cemento(), 1), CantidadTexto = cantidad };

        Assert.False(venta.Agregar());
        Assert.Empty(venta.Detalle);
        Assert.NotNull(venta.Error);
    }

    [Fact]
    public void Quitar_renumera_y_recalcula()
    {
        var venta = new VentaViewModel();
        Agregar(venta, ConId(DatosPrueba.Cemento(), 1), "1");
        Agregar(venta, ConId(DatosPrueba.Fierro(), 2), "1");

        venta.Quitar(venta.Detalle[0]);

        Assert.Equal(1, Assert.Single(venta.Detalle).Item);
        Assert.Equal(38.00m, venta.Total);
    }

    [Fact]
    public void Sin_producto_elegido_Agregar_esta_deshabilitado()
    {
        var venta = new VentaViewModel();

        Assert.False(venta.AgregarCommand.CanExecute(null));
        venta.ProductoPorAgregar = ConId(DatosPrueba.Cemento(), 1);
        Assert.True(venta.AgregarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Elegir_en_el_buscador_y_agregar_deja_el_campo_listo_para_otro_producto()
    {
        var vm = new MainViewModel(new ProductoRepositorioEnMemoria(DatosPrueba.Catalogo()));
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.Buscador.Texto = "fierro";
        await vm.Buscador.UltimaBusqueda;

        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.Venta.CantidadTexto = "8";
        vm.Venta.Agregar();

        Assert.Equal(304.00m, vm.Venta.Total);
        Assert.Equal(string.Empty, vm.Buscador.Texto);
        Assert.Equal("1", vm.Venta.CantidadTexto);
    }
}
