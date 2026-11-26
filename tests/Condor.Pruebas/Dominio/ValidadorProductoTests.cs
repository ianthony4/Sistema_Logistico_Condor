using Condor.Datos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Dominio;

public class ValidadorProductoTests
{
    private static Producto P(string nombre, decimal precio, string unidad = "UND") =>
        new() { Nombre = nombre, Precio = precio, Unidad = unidad };

    [Fact]
    public void Producto_con_nombre_y_precio_positivo_es_valido()
    {
        Assert.Empty(ValidadorProducto.Errores(P("Cemento Portland tipo I", 32.50m)));
    }

    [Theory]
    [InlineData("", 10, "nombre es obligatorio")]
    [InlineData("   ", 10, "nombre es obligatorio")]
    [InlineData("Martillo", 0, "mayor que cero")]
    [InlineData("Martillo", -5, "mayor que cero")]
    [InlineData("Martillo", 100000, "no puede pasar")]
    [InlineData("Martillo", 10.555, "dos decimales")]
    public void Datos_invalidos_dan_un_mensaje_claro(string nombre, decimal precio, string mensaje)
    {
        var errores = ValidadorProducto.Errores(P(nombre, precio));

        Assert.Contains(errores, e => e.Contains(mensaje));
    }

    // HU-13: código de barras (T-86).

    [Theory]
    [InlineData("7750182001236")]   // EAN-13 de un fabricante peruano
    [InlineData("77501820")]        // EAN-8
    [InlineData("cond-0042")]       // código propio del local
    public void Codigos_de_barras_validos(string codigo)
    {
        var producto = P("Martillo", 25m);
        producto.CodigoBarras = codigo;

        Assert.Empty(ValidadorProducto.Errores(ValidadorProducto.Normalizar(producto)));
        Assert.Equal(codigo.ToUpperInvariant(), producto.CodigoBarras);
    }

    [Theory]
    [InlineData("123", "entre 4 y 30")]
    [InlineData("7750 1820", "letras, números y guiones")]
    [InlineData("ABC*123", "letras, números y guiones")]
    public void Codigos_de_barras_invalidos(string codigo, string mensaje)
    {
        Assert.Contains(mensaje, ValidadorProducto.ErrorCodigo(codigo));
    }

    [Fact]
    public void Sin_codigo_de_barras_el_producto_es_valido()
    {
        var producto = ValidadorProducto.Normalizar(new Producto { Nombre = "Clavo", Precio = 7.5m, CodigoBarras = "  " });

        Assert.Null(producto.CodigoBarras);
        Assert.Empty(ValidadorProducto.Errores(producto));
    }

    [Theory]
    [InlineData("7750182001236", true)]
    [InlineData("cond-0042", true)]
    [InlineData("cemento", false)]     // un nombre escrito a mano
    [InlineData("tubo 1/2", false)]
    [InlineData("12", false)]
    public void Distingue_un_codigo_leido_de_un_nombre(string texto, bool esCodigo)
    {
        Assert.Equal(esCodigo, ValidadorProducto.PareceCodigo(texto));
    }

    [Fact]
    public void Nombre_de_mas_de_150_caracteres_no_es_valido()
    {
        Assert.NotEmpty(ValidadorProducto.Errores(P(new string('a', 151), 1m)));
    }

    [Fact]
    public void Normalizar_quita_espacios_y_pone_la_unidad_en_mayusculas()
    {
        var producto = ValidadorProducto.Normalizar(P("  Esmalte   sintético  rojo ", 58m, " gal "));

        Assert.Equal("Esmalte sintético rojo", producto.Nombre);
        Assert.Equal("GAL", producto.Unidad);
    }

    [Fact]
    public void Unidad_vacia_se_toma_como_UND()
    {
        Assert.Equal("UND", ValidadorProducto.Normalizar(P("Martillo", 25m, "")).Unidad);
    }

    [Fact]
    public void Validar_lanza_con_todos_los_errores()
    {
        var ex = Assert.Throws<ProductoInvalidoException>(() => ValidadorProducto.Validar(P("", 0)));

        Assert.Equal(2, ex.Errores.Count);
    }

    [Theory]
    [InlineData("50%", @"50\%")]
    [InlineData("tubo_pvc", @"tubo\_pvc")]
    [InlineData(@"a\b", @"a\\b")]
    public void La_busqueda_escapa_los_comodines_de_LIKE(string texto, string esperado)
    {
        Assert.Equal(esperado, ProductoRepositorio.EscaparLike(texto));
    }
}
