using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Condor.Dominio.Contratos;
using Condor.Dominio.Entidades;
using Condor.Dominio.Validacion;
using Condor.Dominio.Ventas;
using Condor.Presentacion.Mvvm;

namespace Condor.Presentacion.ViewModels;

/// <summary>Opción de una lista del diálogo (motivo del traslado).</summary>
public sealed record OpcionCodigo(string Codigo, string Nombre)
{
    public override string ToString() => Nombre;
}

/// <summary>Bien de la guía en el diálogo: producto y cantidad.</summary>
public sealed class LineaGuiaViewModel : ObservableObject
{
    private int _item;

    public LineaGuiaViewModel(long productoId, string descripcion, string unidad, decimal cantidad)
    {
        ProductoId = productoId;
        Descripcion = descripcion;
        Unidad = unidad;
        Cantidad = cantidad;
    }

    public int Item
    {
        get => _item;
        set => SetProperty(ref _item, value);
    }

    public long ProductoId { get; }

    public string Descripcion { get; }

    public string Unidad { get; }

    public decimal Cantidad { get; }
}

/// <summary>
/// Diálogo Nueva guía de remisión (HU-10, T-94). Se puede traer todo de una
/// factura o boleta (destinatario y productos) o llenarlo a mano. Al emitir,
/// la guía se guarda con la serie T001; la ventana Guías la imprime y la envía.
/// </summary>
public sealed class NuevaGuiaViewModel : ObservableObject
{
    private readonly IGuiaRemisionRepositorio _guias;
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly IClienteRepositorio _clientes;
    private readonly IEmpresaRepositorio _empresas;
    private readonly Func<Sesion?> _sesion;
    private readonly Func<DateOnly> _hoy;
    private string _comprobanteTexto = string.Empty;
    private Comprobante? _comprobante;
    private string _documentoDestinatario = string.Empty;
    private string _nombreDestinatario = string.Empty;
    private OpcionCodigo _motivo;
    private string _descripcionMotivo = string.Empty;
    private DateTime? _fechaTraslado;
    private string _pesoTexto = string.Empty;
    private string _partidaUbigeo = string.Empty;
    private string _partidaDireccion = string.Empty;
    private string _llegadaUbigeo = string.Empty;
    private string _llegadaDireccion = string.Empty;
    private string _modalidad = ModalidadTraslado.Privado;
    private string _transportistaRuc = string.Empty;
    private string _transportistaRazonSocial = string.Empty;
    private string _placa = string.Empty;
    private string _conductorDni = string.Empty;
    private string _conductorNombres = string.Empty;
    private string _conductorApellidos = string.Empty;
    private string _conductorLicencia = string.Empty;
    private string _observacion = string.Empty;
    private Producto? _productoPorAgregar;
    private string _cantidadTexto = "1";
    private string? _error;
    private string? _aviso;

    /// <param name="hoy">Fecha de hoy en Perú; por defecto la del reloj de la PC.</param>
    public NuevaGuiaViewModel(IGuiaRemisionRepositorio guias, IComprobanteRepositorio comprobantes, IClienteRepositorio clientes,
        IProductoRepositorio productos, IEmpresaRepositorio empresas, Func<Sesion?> sesion, Func<DateOnly>? hoy = null)
    {
        _guias = guias;
        _comprobantes = comprobantes;
        _clientes = clientes;
        _empresas = empresas;
        _sesion = sesion;
        _hoy = hoy ?? (() => EmisionComprobante.FechaPeru(DateTimeOffset.Now));
        _motivo = Motivos[0];
        _fechaTraslado = _hoy().ToDateTime(TimeOnly.MinValue);

        Buscador = new BuscadorProductoViewModel(productos);
        Buscador.ProductoElegido += p => ProductoPorAgregar = p;
        Buscador.ConexionPerdida += () => ConexionPerdida?.Invoke();
        Lineas.CollectionChanged += AlCambiarLineas;

        TraerComprobanteCommand = new AsyncRelayCommand(TraerComprobanteAsync, () => !string.IsNullOrWhiteSpace(ComprobanteTexto));
        BuscarDestinatarioCommand = new AsyncRelayCommand(BuscarDestinatarioAsync, () => !string.IsNullOrWhiteSpace(DocumentoDestinatario));
        AgregarCommand = new RelayCommand(_ => Agregar(), _ => ProductoPorAgregar is not null);
        QuitarCommand = new RelayCommand(p => Lineas.Remove((p as LineaGuiaViewModel)!));
        EmitirCommand = new AsyncRelayCommand(EmitirAsync, () => Lineas.Count > 0);
        CancelarCommand = new RelayCommand(() => Cerrar?.Invoke(false));
    }

    /// <summary>Lo escucha la ventana para cerrarse: true si se emitió.</summary>
    public event Action<bool>? Cerrar;

    public event Action? ConexionPerdida;

    /// <summary>Se agregó un producto: la vista vuelve al campo Producto.</summary>
    public event Action? LineaAgregada;

    public static IReadOnlyList<OpcionCodigo> Motivos { get; } =
        MotivoTraslado.Todos.Select(m => new OpcionCodigo(m, MotivoTraslado.Nombre(m))).ToList();

    /// <summary>Las mismas opciones, para el ComboBox (WPF no enlaza propiedades estáticas por instancia).</summary>
    public IReadOnlyList<OpcionCodigo> OpcionesMotivo => Motivos;

    /// <summary>Número de la factura o boleta (F001-31) para traer el destinatario y los productos.</summary>
    public string ComprobanteTexto
    {
        get => _comprobanteTexto;
        set
        {
            if (SetProperty(ref _comprobanteTexto, value ?? string.Empty))
                TraerComprobanteCommand.NotificarCambio();
        }
    }

    /// <summary>Comprobante que sustenta el traslado; null si la guía se llena a mano.</summary>
    public Comprobante? Comprobante
    {
        get => _comprobante;
        private set => SetProperty(ref _comprobante, value);
    }

    public string DocumentoDestinatario
    {
        get => _documentoDestinatario;
        set
        {
            if (SetProperty(ref _documentoDestinatario, value ?? string.Empty))
                BuscarDestinatarioCommand.NotificarCambio();
        }
    }

    public string NombreDestinatario
    {
        get => _nombreDestinatario;
        set => SetProperty(ref _nombreDestinatario, value ?? string.Empty);
    }

    public OpcionCodigo Motivo
    {
        get => _motivo;
        set
        {
            if (SetProperty(ref _motivo, value ?? Motivos[0]))
                OnPropertyChanged(nameof(PideDescripcionMotivo));
        }
    }

    public bool PideDescripcionMotivo => Motivo.Codigo == MotivoTraslado.Otros;

    public string DescripcionMotivo
    {
        get => _descripcionMotivo;
        set => SetProperty(ref _descripcionMotivo, value ?? string.Empty);
    }

    public DateTime? FechaTraslado
    {
        get => _fechaTraslado;
        set => SetProperty(ref _fechaTraslado, value?.Date);
    }

    /// <summary>Peso bruto total en kilogramos (acepta 1235.5 o 1235,5).</summary>
    public string PesoTexto
    {
        get => _pesoTexto;
        set => SetProperty(ref _pesoTexto, value ?? string.Empty);
    }

    public string PartidaUbigeo
    {
        get => _partidaUbigeo;
        set => SetProperty(ref _partidaUbigeo, value ?? string.Empty);
    }

    public string PartidaDireccion
    {
        get => _partidaDireccion;
        set => SetProperty(ref _partidaDireccion, value ?? string.Empty);
    }

    public string LlegadaUbigeo
    {
        get => _llegadaUbigeo;
        set => SetProperty(ref _llegadaUbigeo, value ?? string.Empty);
    }

    public string LlegadaDireccion
    {
        get => _llegadaDireccion;
        set => SetProperty(ref _llegadaDireccion, value ?? string.Empty);
    }

    /// <summary>Transporte privado: el vehículo de la ferretería (el caso de siempre).</summary>
    public bool EsPrivado
    {
        get => _modalidad == ModalidadTraslado.Privado;
        set
        {
            if (value)
                CambiarModalidad(ModalidadTraslado.Privado);
        }
    }

    /// <summary>Transporte público: una empresa de transportes.</summary>
    public bool EsPublico
    {
        get => _modalidad == ModalidadTraslado.Publico;
        set
        {
            if (value)
                CambiarModalidad(ModalidadTraslado.Publico);
        }
    }

    public string TransportistaRuc
    {
        get => _transportistaRuc;
        set => SetProperty(ref _transportistaRuc, value ?? string.Empty);
    }

    public string TransportistaRazonSocial
    {
        get => _transportistaRazonSocial;
        set => SetProperty(ref _transportistaRazonSocial, value ?? string.Empty);
    }

    public string Placa
    {
        get => _placa;
        set => SetProperty(ref _placa, value ?? string.Empty);
    }

    public string ConductorDni
    {
        get => _conductorDni;
        set => SetProperty(ref _conductorDni, value ?? string.Empty);
    }

    public string ConductorNombres
    {
        get => _conductorNombres;
        set => SetProperty(ref _conductorNombres, value ?? string.Empty);
    }

    public string ConductorApellidos
    {
        get => _conductorApellidos;
        set => SetProperty(ref _conductorApellidos, value ?? string.Empty);
    }

    public string ConductorLicencia
    {
        get => _conductorLicencia;
        set => SetProperty(ref _conductorLicencia, value ?? string.Empty);
    }

    public string Observacion
    {
        get => _observacion;
        set => SetProperty(ref _observacion, value ?? string.Empty);
    }

    /// <summary>Campo Producto con autocompletado, el mismo de la venta.</summary>
    public BuscadorProductoViewModel Buscador { get; }

    public Producto? ProductoPorAgregar
    {
        get => _productoPorAgregar;
        private set
        {
            if (SetProperty(ref _productoPorAgregar, value))
                AgregarCommand.NotificarCambio();
        }
    }

    public string CantidadTexto
    {
        get => _cantidadTexto;
        set => SetProperty(ref _cantidadTexto, value ?? string.Empty);
    }

    public ObservableCollection<LineaGuiaViewModel> Lineas { get; } = new();

    /// <summary>Por qué no se emitió; null si no hay error.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Lo que se trajo del comprobante o del cliente.</summary>
    public string? Aviso
    {
        get => _aviso;
        private set => SetProperty(ref _aviso, value);
    }

    /// <summary>Guía emitida, con su número; null mientras no se emita.</summary>
    public GuiaRemision? Emitida { get; private set; }

    public AsyncRelayCommand TraerComprobanteCommand { get; }

    public AsyncRelayCommand BuscarDestinatarioCommand { get; }

    public RelayCommand AgregarCommand { get; }

    public RelayCommand QuitarCommand { get; }

    public AsyncRelayCommand EmitirCommand { get; }

    public RelayCommand CancelarCommand { get; }

    /// <summary>El punto de partida es el local: se toma de los datos de la empresa al abrir el diálogo.</summary>
    public async Task InicializarAsync()
    {
        try
        {
            var empresa = await _empresas.ObtenerAsync();
            if (PartidaUbigeo.Length == 0)
                PartidaUbigeo = empresa.Ubigeo;
            if (PartidaDireccion.Length == 0)
                PartidaDireccion = empresa.Direccion;
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: no se pudieron leer los datos del local.";
            ConexionPerdida?.Invoke();
        }
    }

    /// <summary>Agrega el producto elegido con la cantidad escrita.</summary>
    public bool Agregar()
    {
        var producto = ProductoPorAgregar;
        if (producto is null)
            return false;
        if (!NuevoProductoViewModel.IntentarLeerPrecio(CantidadTexto, out var cantidad) || cantidad <= 0)
        {
            Error = "La cantidad debe ser un número mayor que cero.";
            return false;
        }

        Lineas.Add(new LineaGuiaViewModel(producto.Id, producto.Nombre, producto.Unidad, cantidad));
        Error = null;
        ProductoPorAgregar = null;
        Buscador.Limpiar();
        CantidadTexto = "1";
        LineaAgregada?.Invoke();
        return true;
    }

    /// <summary>Trae de la factura o boleta el destinatario, su dirección y los productos (HU-10, motivo venta).</summary>
    private async Task TraerComprobanteAsync()
    {
        Error = null;
        try
        {
            var encontrados = await _comprobantes.BuscarAsync(new FiltroComprobantes(Texto: ComprobanteTexto, Limite: 5));
            var resumen = encontrados.Comprobantes.FirstOrDefault(c => c.EsElectronico);
            var comprobante = resumen is null ? null : await _comprobantes.ObtenerAsync(resumen.Id);
            if (comprobante is null)
            {
                Error = $"No se encontró la factura o boleta {ComprobanteTexto.Trim()}.";
                return;
            }

            var empresa = await _empresas.ObtenerAsync();
            var guia = ValidadorGuia.DesdeComprobante(comprobante, empresa, _hoy());
            Comprobante = comprobante;
            Motivo = Motivos.First(m => m.Codigo == MotivoTraslado.Venta);
            DocumentoDestinatario = guia.DestinatarioDocumento;
            NombreDestinatario = guia.DestinatarioNombre;
            if (guia.LlegadaDireccion.Length > 0)
                LlegadaDireccion = guia.LlegadaDireccion;
            PartidaUbigeo = guia.PartidaUbigeo;
            PartidaDireccion = guia.PartidaDireccion;
            Lineas.Clear();
            foreach (var linea in guia.Detalle)
                Lineas.Add(new LineaGuiaViewModel(linea.ProductoId, linea.Descripcion, linea.Unidad, linea.Cantidad));
            Aviso = $"Se trajeron el destinatario y {Lineas.Count} productos de {comprobante.TipoCorto} {comprobante.NumeroCompleto}.";
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: no se puede buscar el comprobante.";
            ConexionPerdida?.Invoke();
        }
    }

    private async Task BuscarDestinatarioAsync()
    {
        Error = null;
        var numero = ValidadorDocumento.Limpiar(DocumentoDestinatario);
        if (ValidadorDocumento.TipoSegunLargo(numero) is not { } tipo)
        {
            Error = "Destinatario: digite un DNI (8 dígitos) o un RUC (11 dígitos).";
            return;
        }

        try
        {
            if (await _clientes.BuscarPorDocumentoAsync(tipo, numero) is { } cliente)
            {
                NombreDestinatario = cliente.Nombre;
                if (LlegadaDireccion.Length == 0 && cliente.Direccion is { } direccion)
                    LlegadaDireccion = direccion;
                Aviso = $"Destinatario: {cliente.Nombre}.";
            }
            else
            {
                Aviso = "Ese documento no es de un cliente registrado: escriba el nombre del destinatario.";
            }
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: no se puede buscar al destinatario.";
            ConexionPerdida?.Invoke();
        }
    }

    private void CambiarModalidad(string modalidad)
    {
        if (_modalidad == modalidad)
            return;
        _modalidad = modalidad;
        OnPropertyChanged(nameof(EsPrivado));
        OnPropertyChanged(nameof(EsPublico));
    }

    private void AlCambiarLineas(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Lineas.Count; i++)
            Lineas[i].Item = i + 1;
        EmitirCommand.NotificarCambio();
    }

    /// <summary>La guía tal como está en el diálogo (sin preparar).</summary>
    public GuiaRemision Armar()
    {
        var documento = ValidadorDocumento.Limpiar(DocumentoDestinatario);
        var guia = new GuiaRemision
        {
            FechaTraslado = FechaTraslado is { } f ? DateOnly.FromDateTime(f) : _hoy(),
            Motivo = Motivo.Codigo,
            DescripcionMotivo = DescripcionMotivo,
            Modalidad = _modalidad,
            DestinatarioTipo = ValidadorDocumento.TipoSegunLargo(documento) ?? TipoDocumento.Ruc,
            DestinatarioDocumento = documento,
            DestinatarioNombre = NombreDestinatario,
            PartidaUbigeo = PartidaUbigeo.Trim(),
            PartidaDireccion = PartidaDireccion,
            LlegadaUbigeo = LlegadaUbigeo.Trim(),
            LlegadaDireccion = LlegadaDireccion,
            PesoBrutoKg = NuevoProductoViewModel.IntentarLeerPrecio(PesoTexto, out var peso) ? peso : 0,
            TransportistaRuc = TransportistaRuc,
            TransportistaRazonSocial = TransportistaRazonSocial,
            VehiculoPlaca = Placa,
            ConductorDni = ConductorDni,
            ConductorNombres = ConductorNombres,
            ConductorApellidos = ConductorApellidos,
            ConductorLicencia = ConductorLicencia,
            Observacion = Observacion,
            ComprobanteId = Comprobante?.Id,
            ComprobanteTipo = Comprobante?.TipoComprobante,
            ComprobanteNumero = Comprobante?.NumeroCompleto,
        };
        foreach (var linea in Lineas)
            guia.Detalle.Add(new DetalleGuia { ProductoId = linea.ProductoId, Descripcion = linea.Descripcion, Unidad = linea.Unidad, Cantidad = linea.Cantidad });
        return guia;
    }

    private async Task EmitirAsync()
    {
        var hoy = _hoy();
        var guia = Armar();
        // Antes de preparar: Preparar descarta los datos del transporte que no corresponden.
        var errores = ValidadorGuia.Errores(guia, hoy);
        if (errores.Count > 0)
        {
            Error = string.Join(" ", errores.Select(e => e.Mensaje));
            return;
        }

        var sesion = _sesion();
        if (sesion is not { Registrada: true })
        {
            Error = "Esta PC aún no se registró en la base de datos central. Espere a que vuelva la conexión y pulse Emitir otra vez.";
            return;
        }
        guia.TerminalId = sesion.Terminal.Id;
        guia.TerminalNombre = sesion.Terminal.Nombre;

        try
        {
            Emitida = await _guias.EmitirAsync(ValidadorGuia.Preparar(guia, hoy));
            Cerrar?.Invoke(true);
        }
        catch (SinConexionException)
        {
            Error = "Sin conexión con la PC principal: la guía NO se emitió. Sus datos se conservan: pulse Emitir para reintentar.";
            ConexionPerdida?.Invoke();
        }
        catch (Exception ex)
        {
            Error = $"No se pudo emitir la guía: {ex.Message}";
        }
    }
}
