using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Condor.Dominio.Sunat;
using Condor.Sunat;

namespace Condor.Pruebas.Sunat;

public class LectorCdrTests
{
    [Fact]
    public void Lee_el_codigo_la_descripcion_y_la_referencia_del_CDR_aceptado()
    {
        var cdr = LectorCdr.LeerZip(Zip(CdrXml("0", "La Boleta numero B001-12, ha sido aceptada")));

        Assert.Equal("B001-12", cdr.ReferenciaId);
        Assert.Equal("0", cdr.Codigo);
        Assert.Equal("La Boleta numero B001-12, ha sido aceptada", cdr.Descripcion);
        Assert.Empty(cdr.Notas);
        Assert.Equal(EstadoSunat.Aceptado, LectorCdr.Estado(cdr));
    }

    [Fact]
    public void Un_CDR_con_notas_es_aceptado_con_observaciones()
    {
        var cdr = LectorCdr.LeerZip(Zip(CdrXml("0", "ha sido aceptada",
            "4031 - Debe indicar el nombre comercial", "4001 - El numero de RUC del receptor no existe.")));

        Assert.Equal(2, cdr.Notas.Count);
        Assert.Equal(EstadoSunat.Observado, LectorCdr.Estado(cdr));
    }

    [Theory]
    [InlineData("2017", EstadoSunat.Rechazado)]
    [InlineData("3105", EstadoSunat.Rechazado)]
    [InlineData("4252", EstadoSunat.Observado)]
    [InlineData("0", EstadoSunat.Aceptado)]
    [InlineData("0109", EstadoSunat.Pendiente)]
    [InlineData("", EstadoSunat.Pendiente)]
    public void El_estado_depende_del_rango_del_codigo(string codigo, string estado)
    {
        Assert.Equal(estado, LectorCdr.Estado(new Cdr("B001-1", codigo, "", Array.Empty<string>())));
    }

    [Theory]
    [InlineData("soap-env:Client.2335", EstadoSunat.Rechazado)]
    [InlineData("soap-env:Client.0111", EstadoSunat.Pendiente)] // sin perfil: se corrige la clave SOL y se reenvía
    [InlineData("soap-env:Server", EstadoSunat.Pendiente)]
    [InlineData("1033", EstadoSunat.Pendiente)]
    public void Un_error_SOAP_rechaza_solo_si_es_del_rango_2000_a_3999(string faultcode, string estado)
    {
        Assert.Equal(estado, LectorCdr.EstadoDeError(faultcode));
    }

    [Theory]
    [InlineData("soap-env:Client.0111", 111)]
    [InlineData("2335", 2335)]
    [InlineData("soap-env:Server", -1)]
    [InlineData(null, -1)]
    public void Extrae_el_numero_del_faultcode(string? faultcode, int numero)
    {
        Assert.Equal(numero, LectorCdr.Numero(faultcode));
    }

    [Fact]
    public void Lee_el_CDR_en_ISO_8859_1_como_lo_envia_la_SUNAT()
    {
        var xml = CdrXml("0", "La Boleta número B001-12, ha sido aceptada");
        var bytes = Encoding.Latin1.GetBytes(xml.Replace("encoding=\"utf-8\"", "encoding=\"ISO-8859-1\""));

        var cdr = LectorCdr.LeerZip(Zip(bytes));

        Assert.Contains("número", cdr.Descripcion);
    }

    [Fact]
    public void Un_ZIP_sin_XML_o_que_no_es_ZIP_da_un_error_claro()
    {
        var vacio = new MemoryStream();
        using (new ZipArchive(vacio, ZipArchiveMode.Create, leaveOpen: true)) { }

        Assert.Throws<CdrInvalidoException>(() => LectorCdr.LeerZip(vacio.ToArray()));
        Assert.Throws<CdrInvalidoException>(() => LectorCdr.LeerZip(Encoding.UTF8.GetBytes("no es zip")));
    }

    [Fact]
    public void Un_XML_que_no_es_CDR_da_un_error_claro()
    {
        Assert.Throws<CdrInvalidoException>(() => LectorCdr.Leer(XDocument.Parse("<Invoice/>")));
    }

    /// <summary>CDR como el de la SUNAT (ApplicationResponse), con observaciones opcionales.</summary>
    internal static string CdrXml(string codigo, string descripcion, params string[] notas) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <ApplicationResponse xmlns="urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2"
            xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
            xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:UBLVersionID>2.0</cbc:UBLVersionID>
          <cbc:ID>1730728800000</cbc:ID>
          {string.Concat(notas.Select(n => $"<cbc:Note>{n}</cbc:Note>"))}
          <cac:DocumentResponse>
            <cac:Response>
              <cbc:ReferenceID>B001-12</cbc:ReferenceID>
              <cbc:ResponseCode>{codigo}</cbc:ResponseCode>
              <cbc:Description>{descripcion}</cbc:Description>
            </cac:Response>
            <cac:DocumentReference><cbc:ID>B001-12</cbc:ID></cac:DocumentReference>
          </cac:DocumentResponse>
        </ApplicationResponse>
        """;

    internal static byte[] Zip(string xml) => Zip(Encoding.UTF8.GetBytes(xml));

    internal static byte[] Zip(byte[] contenido, string nombre = "R-20600000005-03-B001-12.xml")
    {
        var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("dummy/");
            using var flujo = zip.CreateEntry(nombre).Open();
            flujo.Write(contenido);
        }
        return memoria.ToArray();
    }
}
