using Condor.Dominio.Entidades;
using Condor.Dominio.Impresion;
using Condor.Pruebas.Infraestructura;

namespace Condor.Pruebas.Dominio;

/// <summary>Representación impresa de la guía de remisión (HU-10, T-95).</summary>
public class FormatoGuiaTests
{
    private static GuiaRemision Guia(bool publico = false)
    {
        var guia = new GuiaRemision
        {
            Id = 5, SerieCodigo = "T001", Numero = 5, TerminalNombre = "CAJA-01",
            Fecha = new DateTimeOffset(2026, 11, 26, 15, 30, 0, TimeSpan.Zero),
            FechaTraslado = new DateOnly(2026, 11, 27),
            Motivo = MotivoTraslado.Venta, Modalidad = publico ? ModalidadTraslado.Publico : ModalidadTraslado.Privado,
            DestinatarioTipo = TipoDocumento.Ruc, DestinatarioDocumento = "20131312955",
            DestinatarioNombre = "Constructora Valle de Majes S.A.C.",
            PartidaUbigeo = "040520", PartidaDireccion = "Majes, Caylloma - Arequipa",
            LlegadaUbigeo = "040129", LlegadaDireccion = "Obra Av. Ejército 101, Yanahuara",
            PesoBrutoKg = 1235.5m,
            ComprobanteTipo = TipoComprobante.Factura, ComprobanteNumero = "F001-00000031",
            Detalle =
            {
                new() { Item = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS", Cantidad = 20 },
                new() { Item = 2, Descripcion = "Fierro corrugado 1/2 in x 9 m de grado 60 para columnas y vigas de concreto", Unidad = "VAR", Cantidad = 10 },
            },
        };
        if (publico)
        {
            guia.TransportistaRuc = "20100137390";
            guia.TransportistaRazonSocial = "Transportes Arequipa S.A.";
        }
        else
        {
            guia.VehiculoPlaca = "V1A952";
            guia.ConductorDni = "45879632";
            guia.ConductorNombres = "Juan";
            guia.ConductorApellidos = "Pérez Quispe";
            guia.ConductorLicencia = "Q45879632";
        }
        return guia;
    }

    [Fact]
    public void La_guia_lleva_numero_traslado_destinatario_puntos_y_bienes()
    {
        var texto = FormatoTicket.GenerarGuia(DatosPrueba.Empresa(), Guia()).ComoTexto();

        Assert.Contains("GUÍA DE REMISIÓN ELECTRÓNICA REMITENTE", texto);
        Assert.Contains("T001-00000005", texto);
        Assert.Contains("Emisión: 26/11/2026 10:30", texto);
        Assert.Contains("Inicio del traslado: 27/11/2026", texto);
        Assert.Contains("Motivo: Venta", texto);
        Assert.Contains("Comprobante: Factura F001-00000031", texto);
        Assert.Contains("Destinatario: Constructora Valle de Majes S.A.C.", texto);
        Assert.Contains("RUC: 20131312955", texto);
        Assert.Contains("Partida (040520)", texto);
        Assert.Contains("Llegada (040129)", texto);
        Assert.Contains("Peso bruto: 1235.5 KG", texto);
        Assert.Contains("20 BLS", texto);
        Assert.Contains("Representación impresa de la Guía", string.Join(" ", texto.Split(Environment.NewLine, StringSplitOptions.TrimEntries)));
        Assert.DoesNotContain("S/", texto); // la guía no lleva precios
    }

    [Fact]
    public void En_transporte_privado_imprime_vehiculo_y_conductor()
    {
        var texto = FormatoTicket.GenerarGuia(DatosPrueba.Empresa(), Guia()).ComoTexto();

        Assert.Contains("Transporte privado", texto);
        Assert.Contains("Vehículo: V1A952", texto);
        Assert.Contains("Conductor: Juan Pérez Quispe", texto);
        Assert.Contains("Lic.: Q45879632", texto);
    }

    [Fact]
    public void En_transporte_publico_imprime_al_transportista()
    {
        var texto = FormatoTicket.GenerarGuia(DatosPrueba.Empresa(), Guia(publico: true)).ComoTexto();

        Assert.Contains("Transporte público", texto);
        Assert.Contains("Transportista: Transportes Arequipa S.A.", texto);
        Assert.DoesNotContain("Vehículo", texto);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(42)]
    [InlineData(48)]
    public void Ninguna_linea_de_la_guia_pasa_del_ancho(int columnas)
    {
        var ticket = FormatoTicket.GenerarGuia(DatosPrueba.Empresa(), Guia(), columnas);

        Assert.All(ticket.Lineas, l => Assert.True(l.Texto.Length <= columnas, $"{l.Texto.Length} > {columnas}: {l.Texto}"));
    }

    [Fact]
    public void La_reimpresion_de_la_guia_dice_que_es_copia()
    {
        var guia = Guia();
        guia.Impresiones = 1;

        Assert.Contains(FormatoTicket.MarcaCopia, FormatoTicket.GenerarGuia(DatosPrueba.Empresa(), guia).ComoTexto());
    }
}
