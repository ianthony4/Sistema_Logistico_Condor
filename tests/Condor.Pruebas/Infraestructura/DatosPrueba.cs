using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Dominio.Ventas;

namespace Condor.Pruebas.Infraestructura;

/// <summary>
/// Datos de prueba comunes. Se toman de la lista de precios del local para que
/// los casos se parezcan a las ventas reales del mostrador.
/// </summary>
public static class DatosPrueba
{
    public static Producto Cemento() => Nuevo("Cemento Portland tipo I x 42.5 kg", 32.50m, "BLS");

    public static Producto Fierro() => Nuevo("Fierro corrugado 1/2 in x 9 m", 38.00m, "VAR");

    public static Producto EsmalteBlanco() => Nuevo("Esmalte sintético blanco 1 gal", 58.00m, "GAL");

    public static Producto EsmalteRojo() => Nuevo("Esmalte sintético rojo 1 gal", 58.00m, "GAL");

    public static Producto Clavos() => Nuevo("Clavo para madera 3 in x kg", 7.50m, "KG");

    public static IReadOnlyList<Producto> Catalogo() =>
        new[] { Cemento(), Fierro(), EsmalteBlanco(), EsmalteRojo(), Clavos() };

    public static Producto Nuevo(string nombre, decimal precio, string unidad = "UND") =>
        new() { Nombre = nombre, Precio = precio, Unidad = unidad };

    // Clientes (HU-03): un vecino con DNI y una constructora con RUC.
    public static Cliente ClienteDni(string? dni = null) => new()
    {
        TipoDocumento = TipoDocumento.Dni,
        NumeroDocumento = dni ?? "45879632",
        Nombre = "Juan Pérez Quispe",
        Direccion = "Calle Los Pinos 214, El Pedregal",
    };

    public static Cliente ClienteRuc(string? ruc = null) => new()
    {
        TipoDocumento = TipoDocumento.Ruc,
        NumeroDocumento = ruc ?? "20131312955",
        Nombre = "Constructora Valle de Majes S.A.C.",
        Direccion = "Av. Principal 450, Majes",
    };

    public static IReadOnlyList<Cliente> Clientes() => new[] { ClienteDni(), ClienteRuc() };

    // Proveedores (HU-04): los que surten al local de cemento, fierro y pinturas.
    public static Proveedor ProveedorAceros(string? ruc = null) => new()
    {
        Ruc = ruc ?? "20370146994",
        RazonSocial = "Corporación Aceros Arequipa S.A.",
        NombreComercial = "Aceros Arequipa",
        Direccion = "Av. Antonio Miroquesada 425, Lima",
        Telefono = "054 286314",
        Correo = "ventas@acerosarequipa.com",
        Contacto = "Carlos Huamán",
    };

    public static Proveedor ProveedorCemento(string? ruc = null) => new()
    {
        Ruc = ruc ?? "20100137390",
        RazonSocial = "Yura S.A.",
        Direccion = "Carretera a Yura km 26, Arequipa",
        Telefono = "054 495060",
    };

    public static Proveedor ProveedorPinturas(string? ruc = null) => new()
    {
        Ruc = ruc ?? "20100079501",
        RazonSocial = "Corporación Peruana de Productos Químicos S.A.",
        NombreComercial = "CPPQ Pinturas",
    };

    public static IReadOnlyList<Proveedor> Proveedores() =>
        new[] { ProveedorAceros(), ProveedorCemento(), ProveedorPinturas() };

    /// <summary>Datos de la ferretería como los deja la migración V005.</summary>
    public static Empresa Empresa() => new()
    {
        Ruc = "20600000005",
        RazonSocial = "FERRETERÍA CÓNDOR MAJES S.A.C.",
        NombreComercial = "Ferretería Cóndor",
        Direccion = "Majes, Caylloma - Arequipa",
    };

    // Facturas (HU-06): la constructora compra para una obra.

    /// <summary>Pedido típico de una obra: 20 bolsas de cemento y 10 varillas de fierro (S/ 1030.00).</summary>
    public static List<DetalleComprobante> DetalleObra() => new()
    {
        new() { ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS", Cantidad = 20, PrecioUnitario = 32.50m },
        new() { ProductoId = 2, Descripcion = "Fierro corrugado 1/2 in x 9 m", Unidad = "VAR", Cantidad = 10, PrecioUnitario = 38.00m },
    };

    /// <summary>Factura F001 preparada por el dominio para la constructora (aún sin número).</summary>
    public static Comprobante FacturaObra(Cliente? cliente = null)
    {
        var constructora = cliente ?? ClienteRuc();
        if (constructora.Id == 0)
            constructora.Id = 2;
        return EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Factura, DetalleObra(), constructora));
    }

    // Notas de venta y proformas (HU-08, HU-09).

    /// <summary>Nota de venta NV01 del mostrador, sin cliente (aún sin número): 3 kg de clavos (S/ 22.50).</summary>
    public static Comprobante NotaVentaMostrador()
    {
        var detalle = new List<DetalleComprobante>
        {
            new() { ProductoId = 5, Descripcion = "Clavo para madera 3 in x kg", Unidad = "KG", Cantidad = 3, PrecioUnitario = 7.50m },
        };
        return EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.NotaVenta, detalle));
    }

    /// <summary>Proforma PR01 de la obra para la constructora (aún sin número): el mismo pedido de la factura.</summary>
    public static Comprobante ProformaObra(Cliente? cliente = null)
    {
        var constructora = cliente ?? ClienteRuc();
        if (constructora.Id == 0)
            constructora.Id = 2;
        return EmisionComprobante.Preparar(new SolicitudEmision(TipoEmision.Proforma, DetalleObra(), constructora));
    }

    // Compras (HU-11): lo que el local compra a sus proveedores.

    /// <summary>Hoy en Majes, para fechar las compras de prueba.</summary>
    public static DateOnly Hoy() => EmisionComprobante.FechaPeru(DateTimeOffset.Now);

    /// <summary>
    /// Factura de Aceros Arequipa preparada por el dominio: 50 varillas a
    /// S/ 31.50 y 10 kg de alambre a S/ 6.20 (S/ 1637.00).
    /// </summary>
    public static Compra CompraAceros(Proveedor? proveedor = null, long numero = 12345, DateOnly? fecha = null)
    {
        var aceros = proveedor ?? ProveedorAceros();
        if (aceros.Id == 0)
            aceros.Id = 1;
        var compra = new Compra
        {
            ProveedorId = aceros.Id,
            Proveedor = aceros,
            TipoDocumento = TipoDocumentoCompra.Factura,
            Serie = "F001",
            Numero = numero,
            FechaEmision = fecha ?? Hoy().AddDays(-1),
            TerminalId = 1,
            TerminalNombre = "PC-PRINCIPAL",
            Detalle =
            {
                new() { ProductoId = 2, Descripcion = "Fierro corrugado 1/2 in x 9 m", Unidad = "VAR", Cantidad = 50, CostoUnitario = 31.50m },
                new() { ProductoId = 7, Descripcion = "Alambre negro N.° 16", Unidad = "KG", Cantidad = 10, CostoUnitario = 6.20m },
            },
        };
        return ValidadorCompra.Preparar(compra, Hoy());
    }

    /// <summary>Boleta de Yura por 40 bolsas de cemento a S/ 27.80 (S/ 1112.00).</summary>
    public static Compra CompraCemento(Proveedor? proveedor = null, long numero = 731, DateOnly? fecha = null)
    {
        var yura = proveedor ?? ProveedorCemento();
        if (yura.Id == 0)
            yura.Id = 2;
        var compra = new Compra
        {
            ProveedorId = yura.Id,
            Proveedor = yura,
            TipoDocumento = TipoDocumentoCompra.Boleta,
            Serie = "B004",
            Numero = numero,
            FechaEmision = fecha ?? Hoy().AddDays(-3),
            TerminalId = 1,
            TerminalNombre = "PC-PRINCIPAL",
            Detalle =
            {
                new() { ProductoId = 1, Descripcion = "Cemento Portland tipo I x 42.5 kg", Unidad = "BLS", Cantidad = 40, CostoUnitario = 27.80m },
            },
        };
        return ValidadorCompra.Preparar(compra, Hoy());
    }

    // Código de barras (HU-13): el EAN-13 impreso en la bolsa de cemento.
    public const string CodigoCemento = "7751234500017";

    /// <summary>Catálogo de prueba con el cemento ya etiquetado con su código de barras.</summary>
    public static IReadOnlyList<Producto> CatalogoConCodigos()
    {
        var catalogo = Catalogo();
        catalogo[0].CodigoBarras = CodigoCemento;
        return catalogo;
    }

    // Guías de remisión (HU-10): el pedido de la obra va en la camioneta del local.

    /// <summary>
    /// Guía T001 preparada por el dominio (aún sin número): 20 bolsas de cemento
    /// y 10 varillas a una obra en Yanahuara, en transporte privado.
    /// </summary>
    public static GuiaRemision GuiaObra(bool publico = false, DateOnly? traslado = null)
    {
        var guia = new GuiaRemision
        {
            FechaTraslado = traslado ?? Hoy(),
            Motivo = MotivoTraslado.Venta,
            Modalidad = publico ? ModalidadTraslado.Publico : ModalidadTraslado.Privado,
            DestinatarioTipo = TipoDocumento.Ruc,
            DestinatarioDocumento = "20131312955",
            DestinatarioNombre = "Constructora Valle de Majes S.A.C.",
            PartidaUbigeo = "040520",
            PartidaDireccion = "Majes, Caylloma - Arequipa",
            LlegadaUbigeo = "040129",
            LlegadaDireccion = "Obra Av. Ejército 101, Yanahuara",
            PesoBrutoKg = 1235.5m,
            TerminalId = 1,
            TerminalNombre = "PC-PRINCIPAL",
        };
        if (publico)
        {
            guia.TransportistaRuc = "20100137390";
            guia.TransportistaRazonSocial = "Transportes Arequipa S.A.";
        }
        else
        {
            guia.VehiculoPlaca = "V1A-952";
            guia.ConductorDni = "45879632";
            guia.ConductorNombres = "Juan";
            guia.ConductorApellidos = "Pérez Quispe";
            guia.ConductorLicencia = "Q45879632";
        }
        foreach (var linea in DetalleObra())
            guia.Detalle.Add(new DetalleGuia { ProductoId = linea.ProductoId, Descripcion = linea.Descripcion, Unidad = linea.Unidad, Cantidad = linea.Cantidad });
        return ValidadorGuia.Preparar(guia, Hoy());
    }

    // Respaldos (HU-15): el disco externo de la PC principal.

    /// <summary>Carpeta del disco externo en la PC principal.</summary>
    public const string DiscoExterno = @"E:\Respaldos Condor";

    /// <summary>Respaldo terminado hace <paramref name="horas"/> horas en el disco externo.</summary>
    public static Respaldo RespaldoDeHace(double horas, DateTimeOffset? ahora = null)
    {
        var terminado = (ahora ?? DateTimeOffset.Now).AddHours(-horas);
        return new Respaldo
        {
            Archivo = Respaldo.NombreArchivo(terminado),
            Carpeta = DiscoExterno,
            Bytes = 845_312,
            IniciadoEn = terminado.AddSeconds(-6),
            TerminadoEn = terminado,
            TerminalId = 1,
            TerminalNombre = "PC-PRINCIPAL",
        };
    }

    /// <summary>DNI al azar, para que las pruebas con base de datos no choquen entre ejecuciones.</summary>
    public static string DniNuevo() => Random.Shared.Next(10_000_000, 99_999_999).ToString();

    /// <summary>RUC de persona jurídica al azar, con su dígito verificador correcto.</summary>
    public static string RucNuevo()
    {
        var primerosDiez = "20" + Random.Shared.Next(10_000_000, 99_999_999);
        return primerosDiez + ValidadorDocumento.DigitoVerificadorRuc(primerosDiez);
    }

    /// <summary>
    /// Nombre único por ejecución, para que las pruebas de integración no
    /// choquen con productos que dejó una ejecución anterior.
    /// </summary>
    public static string NombreUnico(string prefijo) => $"{prefijo} {Guid.NewGuid():N}"[..Math.Min(prefijo.Length + 13, 150)];
}
