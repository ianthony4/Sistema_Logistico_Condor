using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;

namespace Condor.Pruebas.Dominio;

public class ValidadorDocumentoTests
{
    [Theory]
    [InlineData("20131312955")] // RUC de la SUNAT
    [InlineData("10458796322")] // persona natural: 10 + DNI 45879632
    [InlineData("20600000005")]
    [InlineData("20100070970")] // resto 1: el dígito verificador es 0
    public void RUC_con_digito_verificador_correcto_es_valido(string ruc)
    {
        Assert.True(ValidadorDocumento.EsRucValido(ruc));
        Assert.Null(ValidadorDocumento.Error(TipoDocumento.Ruc, ruc));
    }

    [Theory]
    [InlineData("20131312954", "dígito verificador")]
    [InlineData("2013131295", "11 dígitos")]
    [InlineData("201313129551", "11 dígitos")]
    [InlineData("2013131295A", "11 dígitos")]
    [InlineData("30131312955", "empezar con")]
    [InlineData("", "11 dígitos")]
    public void RUC_invalido_da_un_mensaje_claro(string ruc, string mensaje)
    {
        Assert.False(ValidadorDocumento.EsRucValido(ruc));
        Assert.Contains(mensaje, ValidadorDocumento.Error(TipoDocumento.Ruc, ruc));
    }

    [Theory]
    [InlineData("45879632", true)]
    [InlineData("04587963", true)]
    [InlineData("4587963", false)]
    [InlineData("458796321", false)]
    [InlineData("4587963A", false)]
    public void DNI_tiene_ocho_digitos(string dni, bool valido)
    {
        Assert.Equal(valido, ValidadorDocumento.EsDniValido(dni));
        Assert.Equal(valido, ValidadorDocumento.Error(TipoDocumento.Dni, dni) is null);
    }

    [Theory]
    [InlineData("45879632", TipoDocumento.Dni)]
    [InlineData(" 4587 9632 ", TipoDocumento.Dni)]
    [InlineData("20131312955", TipoDocumento.Ruc)]
    [InlineData("20-131312955", TipoDocumento.Ruc)]
    [InlineData("123", null)]
    [InlineData("abcdefgh", null)]
    public void El_tipo_se_deduce_del_largo(string numero, TipoDocumento? tipo)
    {
        Assert.Equal(tipo, ValidadorDocumento.TipoSegunLargo(numero));
    }

    [Fact]
    public void Limpiar_quita_espacios_puntos_y_guiones()
    {
        Assert.Equal("20131312955", ValidadorDocumento.Limpiar(" 20.131.312-955 "));
        Assert.Equal(string.Empty, ValidadorDocumento.Limpiar(null));
    }
}

public class ValidadorClienteTests
{
    [Fact]
    public void Cliente_con_DNI_y_nombre_es_valido_y_se_normaliza()
    {
        var cliente = ValidadorCliente.Validar(new Cliente
        {
            TipoDocumento = TipoDocumento.Dni,
            NumeroDocumento = " 4587 9632",
            Nombre = "  Juan   Pérez  Quispe ",
            Direccion = "   ",
        });

        Assert.Equal("45879632", cliente.NumeroDocumento);
        Assert.Equal("Juan Pérez Quispe", cliente.Nombre);
        Assert.Null(cliente.Direccion);
        Assert.Equal("DNI 45879632", cliente.Documento);
    }

    [Fact]
    public void RUC_invalido_y_sin_razon_social_da_los_dos_errores()
    {
        var errores = ValidadorCliente.Errores(new Cliente
        {
            TipoDocumento = TipoDocumento.Ruc,
            NumeroDocumento = "20131312954",
        });

        Assert.Equal(2, errores.Count);
        Assert.Contains(errores, e => e.Contains("dígito verificador"));
        Assert.Contains(errores, e => e.Contains("razón social"));
    }

    [Fact]
    public void Sin_tipo_de_documento_no_se_registra()
    {
        var ex = Assert.Throws<ClienteInvalidoException>(() =>
            ValidadorCliente.Validar(new Cliente { Nombre = "Cliente varios" }));

        Assert.Contains("DNI o RUC", ex.Message);
    }
}
