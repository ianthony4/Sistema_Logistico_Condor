using System.Windows.Controls;
using Condor.Presentacion.ViewModels;

namespace Condor.Escritorio.Vistas.Modulos;

public partial class ComprasVista : UserControl
{
    public ComprasVista()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            CampoProveedor.Focus();
            if (DataContext is ComprasViewModel vm)
                await vm.BuscarAsync();
        };
    }
}
