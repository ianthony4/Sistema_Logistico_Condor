using Condor.Dominio.Impresion;
using Condor.Impresion;

namespace Condor.Pruebas.Impresion;

public class ComandosEscPosTests
{
    private static Ticket Ticket(params LineaTicket[] lineas)
    {
        var ticket = new Ticket(48);
        ticket.Lineas.AddRange(lineas);
        return ticket;
    }

    private static int Buscar(byte[] datos, byte[] patron, int desde = 0)
    {
        for (var i = desde; i <= datos.Length - patron.Length; i++)
        {
            if (datos.AsSpan(i, patron.Length).SequenceEqual(patron))
                return i;
        }
        return -1;
    }

    [Fact]
    public void Empieza_inicializando_con_la_pagina_850_y_termina_cortando()
    {
        var datos = ComandosEscPos.Generar(Ticket(new LineaTicket("Hola")));

        Assert.Equal(ComandosEscPos.Inicializar.Concat(ComandosEscPos.TablaCaracteres), datos.Take(5));
        Assert.Equal(ComandosEscPos.Avanzar.Concat(ComandosEscPos.Cortar), datos.TakeLast(7));
    }

    [Fact]
    public void Las_tildes_la_enie_y_los_signos_salen_en_la_pagina_850()
    {
        var bytes = ComandosEscPos.Codificar("ÓÑñ¡É");

        Assert.Equal(new byte[] { 0xE0, 0xA5, 0xA4, 0xAD, 0x90 }, bytes);
    }

    [Fact]
    public void La_negrita_y_el_doble_alto_se_activan_y_se_apagan_en_su_linea()
    {
        var datos = ComandosEscPos.Generar(Ticket(
            new LineaTicket("B001-00000012", Alineacion.Centro, Negrita: true, Grande: true),
            new LineaTicket("normal")));

        var negrita = Buscar(datos, ComandosEscPos.NegritaSi);
        var grande = Buscar(datos, ComandosEscPos.DobleAlto);
        var texto = Buscar(datos, ComandosEscPos.Codificar("B001-00000012"));
        var normal = Buscar(datos, ComandosEscPos.TamanoNormal);
        var sinNegrita = Buscar(datos, ComandosEscPos.NegritaNo);
        var siguiente = Buscar(datos, ComandosEscPos.Codificar("normal"));

        Assert.True(negrita >= 0 && negrita < grande && grande < texto);
        Assert.True(texto < normal && normal < sinNegrita && sinNegrita < siguiente);
    }

    [Fact]
    public void Cada_linea_sale_alineada_con_espacios_como_en_la_vista_previa()
    {
        var datos = ComandosEscPos.Generar(Ticket(new LineaTicket("TOTAL", Alineacion.Derecha)));

        var esperado = ComandosEscPos.Codificar(new string(' ', 43) + "TOTAL\n");
        Assert.True(Buscar(datos, esperado) > 0);
    }
}

public class ConfiguracionTicketeraTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("condor-ticketera-").FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private void Escribir(string json) =>
        File.WriteAllText(Path.Combine(_carpeta, ConfiguracionTicketera.NombreArchivo), json);

    [Fact]
    public void Lee_la_seccion_Ticketera_del_condor_json()
    {
        Escribir("""
            {
              // comentario como en condor.ejemplo.json
              "Servidor": "192.168.1.10",
              "ticketera": { "nombre": "POS-80", "Columnas": 42, },
            }
            """);

        var config = ConfiguracionTicketera.Cargar(_carpeta);

        Assert.True(config.Configurada);
        Assert.Equal("POS-80", config.Nombre);
        Assert.Equal(42, config.Columnas);
        Assert.Equal(10, config.EsperaSegundos);
    }

    [Fact]
    public void Sin_seccion_ni_archivo_usa_la_vista_previa()
    {
        Assert.False(ConfiguracionTicketera.Cargar(_carpeta).Configurada);

        Escribir("""{ "Servidor": "localhost" }""");
        var config = ConfiguracionTicketera.Cargar(_carpeta);

        Assert.False(config.Configurada);
        Assert.Equal(48, config.Columnas);
        Assert.Equal("vista previa", config.ToString());
    }

    [Theory]
    [InlineData("""{ "Ticketera": { "Columnas": 100 } }""")]
    [InlineData("""{ "Ticketera": { "EsperaSegundos": 0 } }""")]
    [InlineData("""{ "Ticketera": { "Columnas": "cuarenta" } }""")]
    public void Valores_fuera_de_rango_o_mal_escritos_dan_un_error_claro(string json)
    {
        Escribir(json);

        var ex = Assert.Throws<InvalidDataException>(() => ConfiguracionTicketera.Cargar(_carpeta));
        Assert.Contains("Ticketera", ex.Message);
    }

    [Fact]
    public void El_ejemplo_que_viaja_con_el_programa_es_valido()
    {
        var ejemplo = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Condor.Escritorio", "condor.ejemplo.json");
        File.Copy(ejemplo, Path.Combine(_carpeta, ConfiguracionTicketera.NombreArchivo));

        var config = ConfiguracionTicketera.Cargar(_carpeta);

        Assert.False(config.Configurada);
        Assert.Equal(48, config.Columnas);
    }
}
