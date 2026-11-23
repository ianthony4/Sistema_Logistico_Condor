using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Dominio.Sunat;
using Condor.Dominio.Ventas;
using Condor.Presentacion.ViewModels;
using Condor.Pruebas.Infraestructura;
using Npgsql;

namespace Condor.Pruebas.Aceptacion;

/// <summary>
/// HU-18: historial de ventas y reimpresión.
/// Criterio 1: si hay ventas registradas, al filtrar en el Historial (F5) por
/// rango de fechas, tipo de comprobante, cliente (DNI, RUC o nombre) o número,
/// el sistema muestra los comprobantes que coinciden con su estado ante la
/// SUNAT y el total del período.
/// Criterio 2: si un comprobante aparece en el Historial, al pulsar
/// Reimprimir el sistema imprime el ticket con el detalle completo marcado
/// como copia y anota la reimpresión.
/// </summary>
/// <remarks>Usa la base de pruebas, la ticketera falsa y la SUNAT falsa.</remarks>
[Collection(BaseDatos.Nombre)]
public class HU18_HistorialTests
{
    private readonly BaseDatosFixture _bd;

    public HU18_HistorialTests(BaseDatosFixture bd) => _bd = bd;

    [FactConBD]
    public async Task CA1_por_cliente_muestra_sus_comprobantes_con_el_estado_SUNAT_y_el_total()
    {
        var (vm, sunat, _) = await PantallaAsync();
        var dni = await RegistrarVecinoAsync();
        var aceptada = await VenderAsync(vm, "Cemento", "2", dni);
        sunat.SinInternet = true;
        var pendiente = await VenderAsync(vm, "Clavo", "4", dni);
        sunat.SinInternet = false;
        await VenderAsync(vm, "Fierro", "1"); // otro cliente: no aparece

        var historial = vm.CrearComprobantesRecientes()!;
        historial.Texto = dni;
        await historial.ActualizarCommand.EjecutarAsync();

        Assert.Equal(new[] { pendiente.Id, aceptada.Id }, historial.Comprobantes.Select(c => c.Id));
        Assert.Equal(EstadoSunat.Aceptado, historial.Comprobantes[1].EstadoSunat);
        Assert.Equal(EstadoSunat.Pendiente, historial.Comprobantes[0].EstadoSunat);
        Assert.Equal(1, historial.PendientesSunat);
        Assert.Equal(aceptada.Total + pendiente.Total, historial.Total);
        Assert.Equal($"2 comprobantes · Ventas del período: S/ {aceptada.Total + pendiente.Total:0.00}", historial.Resumen);
    }

    [FactConBD]
    public async Task CA1_por_nombre_sin_tildes_y_por_numero()
    {
        var (vm, _, _) = await PantallaAsync();
        var dni = await RegistrarVecinoAsync("Peñaloza Cáceres");
        var suya = await VenderAsync(vm, "Esmalte", "1", dni);

        var historial = vm.CrearComprobantesRecientes()!;
        historial.Texto = "penaloza caceres";
        await historial.ActualizarCommand.EjecutarAsync();
        var porNombre = historial.Comprobantes.Select(c => c.Id).ToList();
        historial.Texto = suya.NumeroCompleto;
        await historial.ActualizarCommand.EjecutarAsync();

        Assert.Contains(suya.Id, porNombre);
        Assert.Equal(suya.Id, Assert.Single(historial.Comprobantes).Id);
    }

    [FactConBD]
    public async Task CA1_por_rango_de_fechas_y_por_tipo()
    {
        var (vm, _, _) = await PantallaAsync();
        var dni = await RegistrarVecinoAsync();
        var deLaSemanaPasada = await VenderAsync(vm, "Cemento", "1", dni);
        var deHoy = await VenderAsync(vm, "Cemento", "3", dni);
        vm.Emision!.EsNotaVenta = true;
        var nota = await VenderAsync(vm, "Clavo", "1", dni);
        await EjecutarAsync("UPDATE comprobante SET fecha = now() - interval '7 days' WHERE id = @id", deLaSemanaPasada.Id);
        var hoy = EmisionComprobante.FechaPeru(DateTimeOffset.Now);

        var historial = vm.CrearComprobantesRecientes()!; // al abrir: las ventas de hoy
        historial.Texto = dni;
        await historial.ActualizarCommand.EjecutarAsync();
        var deHoyIds = historial.Comprobantes.Select(c => c.Id).ToList();

        historial.Desde = hoy.AddDays(-10).ToDateTime(TimeOnly.MinValue);
        historial.Hasta = hoy.AddDays(-5).ToDateTime(TimeOnly.MinValue);
        await historial.ActualizarCommand.EjecutarAsync();
        var semanaPasada = historial.Comprobantes.Select(c => c.Id).ToList();

        await historial.LimpiarCommand.EjecutarAsync();
        historial.Texto = dni;
        historial.Tipo = ComprobantesRecientesViewModel.Tipos.Single(t => t.Codigo == TipoComprobante.NotaVenta);
        await historial.ActualizarCommand.EjecutarAsync();

        Assert.Equal(new[] { nota.Id, deHoy.Id }, deHoyIds);
        Assert.Equal(deLaSemanaPasada.Id, Assert.Single(semanaPasada));
        Assert.Equal(nota.Id, Assert.Single(historial.Comprobantes).Id);
        Assert.False(historial.Comprobantes[0].EsElectronico); // la columna SUNAT dice «No aplica»
    }

    [FactConBD]
    public async Task CA2_reimprimir_desde_el_historial_sale_como_copia_con_el_detalle_completo()
    {
        var (vm, _, ticketera) = await PantallaAsync();
        var dni = await RegistrarVecinoAsync();
        await AgregarAsync(vm, "Cemento", "2");
        var boleta = await VenderAsync(vm, "Clavo", "3", dni);
        var original = Assert.Single(ticketera.Impresos).ComoTexto();

        // Días después, en otra PC: el cliente pide una copia para un reclamo.
        var (otra, _, otraTicketera) = await PantallaAsync();
        var historial = otra.CrearComprobantesRecientes()!;
        historial.Desde = null;
        historial.Texto = boleta.NumeroCompleto;
        await historial.ActualizarCommand.EjecutarAsync();
        historial.Seleccionado = Assert.Single(historial.Comprobantes);
        await historial.ReimprimirCommand.EjecutarAsync();

        var copia = Assert.Single(otraTicketera.Impresos).ComoTexto();
        Assert.DoesNotContain("COPIA", original);
        Assert.Contains(FormatoTicket.MarcaCopia, copia);
        Assert.Contains(boleta.NumeroCompleto, copia);
        Assert.Contains($"DNI: {dni}", copia);
        Assert.Equal(2, boleta.Detalle.Count);
        Assert.All(boleta.Detalle, l => Assert.Contains(l.Descripcion, copia));
        Assert.Equal(2, await ImpresionesAsync(boleta.Id));
        Assert.Contains("reimpreso", historial.Mensaje);
    }

    /// <summary>Pantalla Nueva venta contra la base de pruebas, con ticketera y SUNAT falsas.</summary>
    private async Task<(MainViewModel Vm, EnvioSunatFalso Sunat, ImpresoraFalsa Ticketera)> PantallaAsync()
    {
        var pc = _bd.Conexiones;
        var productos = new ProductoRepositorio(pc);
        foreach (var p in DatosPrueba.Catalogo())
        {
            p.Nombre = DatosPrueba.NombreUnico(p.Nombre.Split(' ')[0]);
            await productos.RegistrarAsync(p);
        }

        var comprobantes = new ComprobanteRepositorio(pc);
        var ticketera = new ImpresoraFalsa();
        var sunat = new EnvioSunatFalso();
        var vm = new MainViewModel(productos, new ClienteRepositorio(pc), comprobantes)
        {
            Impresion = new ImpresionViewModel(ticketera, new EmpresaRepositorio(pc), comprobantes),
            EnvioSunat = new EnvioSunatViewModel(sunat, new EmpresaRepositorio(pc), new EnvioSunatRepositorio(pc)),
        };
        vm.Buscador!.Espera = TimeSpan.Zero;
        vm.IniciarSesion(new Sesion(await new TerminalRepositorio(pc).RegistrarAccesoAsync("PC-HU18")));
        return (vm, sunat, ticketera);
    }

    /// <summary>Registra un vecino con un DNI nuevo y devuelve el DNI.</summary>
    private async Task<string> RegistrarVecinoAsync(string? apellidos = null)
    {
        var cliente = DatosPrueba.ClienteDni(DatosPrueba.DniNuevo());
        if (apellidos is not null)
            cliente.Nombre = DatosPrueba.NombreUnico(apellidos);
        await new ClienteRepositorio(_bd.Conexiones).RegistrarAsync(cliente);
        return cliente.NumeroDocumento;
    }

    /// <summary>Agrega el producto y emite: boleta con DNI si se da el DNI, o el tipo ya elegido.</summary>
    private static async Task<Comprobante> VenderAsync(MainViewModel vm, string producto, string cantidad, string? dni = null)
    {
        await AgregarAsync(vm, producto, cantidad);
        if (dni is not null)
        {
            vm.Cliente!.Documento = dni;
            await vm.Cliente.BuscarCommand.EjecutarAsync();
            if (vm.Emision!.EsBoleta)
                vm.Emision.EsBoletaConDni = true;
        }
        await vm.Emision!.EmitirCommand.EjecutarAsync();
        Assert.Null(vm.Emision.Error);
        await Task.WhenAll(vm.UltimaImpresion, vm.UltimoEnvioSunat);
        return vm.Emision.Ultimo!;
    }

    private static async Task AgregarAsync(MainViewModel vm, string texto, string cantidad)
    {
        vm.Buscador!.Texto = texto;
        await vm.Buscador.UltimaBusqueda;
        Assert.NotNull(vm.Buscador.Seleccionado);
        vm.Buscador.Elegir(vm.Buscador.Seleccionado);
        vm.Venta.CantidadTexto = cantidad;
        Assert.True(vm.Venta.Agregar());
    }

    private async Task EjecutarAsync(string sql, long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand(sql, conexion);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> ImpresionesAsync(long id)
    {
        await using var conexion = await _bd.Conexiones.AbrirAsync();
        await using var cmd = new NpgsqlCommand("SELECT impresiones FROM comprobante WHERE id = @id", conexion);
        cmd.Parameters.AddWithValue("id", id);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
