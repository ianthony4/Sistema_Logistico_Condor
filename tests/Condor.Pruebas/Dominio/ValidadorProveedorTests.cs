using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Dominio;

public class ValidadorProveedorTests
{
    [Fact]
    public void Proveedor_con_RUC_valido_y_razon_social_no_tiene_errores()
    {
        var proveedor = Valido();

        Assert.Empty(ValidadorProveedor.Errores(proveedor));
    }

    [Theory]
    [InlineData("20131312954", "dígito verificador")]
    [InlineData("2013131295", "11 dígitos")]
    [InlineData("", "11 dígitos")]
    [InlineData("45879632", "11 dígitos")] // un DNI no sirve: el proveedor factura con RUC
    public void El_RUC_es_obligatorio_y_debe_ser_valido(string ruc, string mensaje)
    {
        var proveedor = Valido();
        proveedor.Ruc = ruc;

        Assert.Contains(ValidadorProveedor.Errores(proveedor), e => e.Contains(mensaje));
    }

    [Fact]
    public void La_razon_social_es_obligatoria()
    {
        var proveedor = Valido();
        proveedor.RazonSocial = "   ";

        Assert.Contains("La razón social es obligatoria.", ValidadorProveedor.Errores(proveedor));
    }

    [Fact]
    public void La_razon_social_no_pasa_de_200_caracteres()
    {
        var proveedor = Valido();
        proveedor.RazonSocial = new string('A', 201);

        Assert.Contains(ValidadorProveedor.Errores(proveedor), e => e.Contains("200"));
    }

    [Theory]
    [InlineData("054 586214", true)]
    [InlineData("959-123-456", true)]
    [InlineData("(054) 586214", true)]
    [InlineData("+51 959 123 456", true)]
    [InlineData("anexo 12", false)]
    [InlineData("123", false)]
    public void El_telefono_solo_admite_numeros_y_separadores(string telefono, bool valido)
    {
        var proveedor = Valido();
        proveedor.Telefono = telefono;

        Assert.Equal(valido, ValidadorProveedor.Errores(proveedor).Count == 0);
    }

    [Theory]
    [InlineData("ventas@aceros.com.pe", true)]
    [InlineData("pedidos@distribuidora.pe", true)]
    [InlineData("ventas@aceros", false)]
    [InlineData("ventas aceros.com", false)]
    [InlineData("@aceros.com", false)]
    public void El_correo_debe_tener_formato_valido(string correo, bool valido)
    {
        var proveedor = Valido();
        proveedor.Correo = correo;

        Assert.Equal(valido, ValidadorProveedor.Errores(proveedor).Count == 0);
    }

    [Fact]
    public void Normalizar_limpia_el_RUC_y_deja_en_null_los_opcionales_vacios()
    {
        var proveedor = new Proveedor
        {
            Ruc = " 20-131312955 ",
            RazonSocial = "  Aceros   Arequipa  S.A.  ",
            NombreComercial = "  ",
            Direccion = "",
            Telefono = " 054  586214 ",
            Correo = " Ventas@Aceros.COM ",
        };

        ValidadorProveedor.Normalizar(proveedor);

        Assert.Equal("20131312955", proveedor.Ruc);
        Assert.Equal("Aceros Arequipa S.A.", proveedor.RazonSocial);
        Assert.Null(proveedor.NombreComercial);
        Assert.Null(proveedor.Direccion);
        Assert.Equal("054 586214", proveedor.Telefono);
        Assert.Equal("ventas@aceros.com", proveedor.Correo);
    }

    [Fact]
    public void Validar_lanza_la_excepcion_con_todos_los_errores()
    {
        var proveedor = new Proveedor { Ruc = "123", RazonSocial = "", Correo = "mal" };

        var ex = Assert.Throws<ProveedorInvalidoException>(() => ValidadorProveedor.Validar(proveedor));

        Assert.Equal(3, ex.Errores.Count);
    }

    [Fact]
    public void El_nombre_para_mostrar_prefiere_el_nombre_comercial()
    {
        var proveedor = Valido();
        Assert.Equal("Corporación Aceros Arequipa S.A.", proveedor.Nombre);

        proveedor.NombreComercial = "Aceros Arequipa";
        Assert.Equal("Aceros Arequipa", proveedor.Nombre);
    }

    private static Proveedor Valido() => new()
    {
        Ruc = "20370146994",
        RazonSocial = "Corporación Aceros Arequipa S.A.",
    };
}
