using System.Globalization;
using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Dominio.Ventas;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

public class MontoEnLetrasTests
{
    [Theory]
    [InlineData(745.00, "SETECIENTOS CUARENTA Y CINCO CON 00/100 SOLES")]
    [InlineData(182.90, "CIENTO OCHENTA Y DOS CON 90/100 SOLES")]
    [InlineData(1.00, "UNO CON 00/100 SOLES")]
    [InlineData(0.50, "CERO CON 50/100 SOLES")]
    [InlineData(16.05, "DIECISÉIS CON 05/100 SOLES")]
    public void Leyenda_en_soles(decimal monto, string esperado)
    {
        Assert.Equal(esperado, MontoEnLetras.Soles(monto));
    }

    [Theory]
    [InlineData(21, "VEINTIUNO")]
    [InlineData(22, "VEINTIDÓS")]
    [InlineData(30, "TREINTA")]
    [InlineData(31, "TREINTA Y UNO")]
    [InlineData(100, "CIEN")]
    [InlineData(101, "CIENTO UNO")]
    [InlineData(500, "QUINIENTOS")]
    [InlineData(1000, "MIL")]
    [InlineData(1001, "MIL UNO")]
    [InlineData(2026, "DOS MIL VEINTISÉIS")]
    [InlineData(21000, "VEINTIÚN MIL")]
    [InlineData(31000, "TREINTA Y UN MIL")]
    [InlineData(100000, "CIEN MIL")]
    [InlineData(101000, "CIENTO UN MIL")]
    [InlineData(1000000, "UN MILLÓN")]
    [InlineData(1000001, "UN MILLÓN UNO")]
    [InlineData(2500000, "DOS MILLONES QUINIENTOS MIL")]
    [InlineData(999999999, "NOVECIENTOS NOVENTA Y NUEVE MILLONES NOVECIENTOS NOVENTA Y NUEVE MIL NOVECIENTOS NOVENTA Y NUEVE")]
    public void Numeros_en_letras(long numero, string esperado)
    {
        Assert.Equal(esperado, MontoEnLetras.Numero(numero));
    }

    [Fact]
    public void Importe_negativo_no_se_convierte()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MontoEnLetras.Soles(-1m));
    }
}

public class FormatoTicketTests
{
    private static Comprobante Boleta(Cliente? cliente = null)
    {
        var detalle = new List<DetalleComprobante>
        {
            new() { ProductoId = 1, Descripcion = "Cemento Portland tipo IP x 42.5 kg", Unidad = "BLS", Cantidad = 3, PrecioUnitario = 30.80m },
            new() { ProductoId = 2, Descripcion = "Clavo de calamina 2 1/2 in", Unidad = "KG", Cantidad = 2, PrecioUnitario = 9.00m },
            new() { ProductoId = 3, Descripcion = "Tubo PVC agua 3/4\" x 5 m", Unidad = "UND", Cantidad = 5, PrecioUnitario = 14.50m },
        };
        var tipo = cliente is null ? TipoEmision.Boleta : TipoEmision.BoletaConDni;
        var boleta = EmisionComprobante.Preparar(new SolicitudEmision(tipo, detalle, cliente));
        boleta.Numero = 12;
        boleta.Fecha = new DateTimeOffset(2026, 10, 21, 16, 24, 0, TimeSpan.Zero); // 11:24 en Lima
        boleta.TerminalNombre = "CAJA-01";
        return boleta;
    }

    [Fact]
    public void El_ticket_del_wireframe_lleva_todos_los_datos_del_criterio_1()
    {
        var texto = FormatoTicket.Generar(DatosPrueba.Empresa(), Boleta()).ComoTexto();

        Assert.Contains("RUC 20600000005", texto);
        Assert.Contains("BOLETA DE VENTA ELECTRÓNICA", texto);
        Assert.Contains("B001-00000012", texto);
        Assert.Contains("21/10/2026 11:24", texto);
        Assert.Contains("Cemento Portland tipo IP x 42.5 kg", texto);
        Assert.Contains("3 BLS x 30.80", texto);
        Assert.Contains("92.40", texto);
        Assert.Contains("OP. GRAVADA  S/      155.00", texto);
        Assert.Contains("IGV 18%      S/       27.90", texto);
        Assert.Contains("TOTAL        S/      182.90", texto);
        Assert.Contains("SON: CIENTO OCHENTA Y DOS CON 90/100 SOLES", texto);
        Assert.Contains("CLIENTES VARIOS", texto);
    }

    [Fact]
    public void La_fecha_lleva_barras_aunque_Windows_use_otro_separador()
    {
        // En es-PE con la configuración regional de algunas PC el separador de fecha es un espacio.
        var anterior = CultureInfo.CurrentCulture;
        var rara = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        rara.DateTimeFormat.DateSeparator = " ";
        rara.DateTimeFormat.TimeSeparator = ".";
        CultureInfo.CurrentCulture = rara;
        try
        {
            var texto = FormatoTicket.Generar(DatosPrueba.Empresa(), Boleta()).ComoTexto();

            Assert.Contains("Fecha: 21/10/2026 11:24", texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public void Con_cliente_imprime_su_nombre_y_DNI()
    {
        var cliente = DatosPrueba.ClienteDni();
        cliente.Id = 5;

        var texto = FormatoTicket.Generar(DatosPrueba.Empresa(), Boleta(cliente)).ComoTexto();

        Assert.Contains("Cliente: Juan Pérez Quispe", texto);
        Assert.Contains("DNI: 45879632", texto);
        Assert.DoesNotContain("CLIENTES VARIOS", texto);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(42)]
    [InlineData(48)]
    public void Ninguna_linea_pasa_del_ancho_del_papel(int columnas)
    {
        var boleta = Boleta();
        boleta.Detalle[0].Descripcion = "Producto con un nombre larguísimo que no entra en una sola línea del ticket de ochenta milímetros";

        var ticket = FormatoTicket.Generar(DatosPrueba.Empresa(), boleta, columnas);

        Assert.All(ticket.Lineas, l => Assert.True(l.Texto.Length <= columnas, $"{l.Texto.Length} > {columnas}: {l.Texto}"));
        Assert.Contains(ticket.Lineas, l => l.Texto.StartsWith("Producto con un nombre"));
    }

    [Fact]
    public void Los_importes_van_pegados_a_la_derecha()
    {
        var ticket = FormatoTicket.Generar(DatosPrueba.Empresa(), Boleta());

        var linea = ticket.Lineas.Single(l => l.Texto.Contains("3 BLS x 30.80"));
        Assert.Equal(FormatoTicket.ColumnasPorDefecto, linea.Texto.Length);
        Assert.EndsWith("92.40", linea.Texto);
    }

    [Theory]
    [InlineData("uno dos tres", 7, new[] { "uno dos", "tres" })]
    [InlineData("abcdefghij", 4, new[] { "abcd", "efgh", "ij" })]
    [InlineData("  ", 10, new string[0])]
    public void Partir_respeta_las_palabras(string texto, int columnas, string[] esperado)
    {
        Assert.Equal(esperado, FormatoTicket.Partir(texto, columnas));
    }
}
