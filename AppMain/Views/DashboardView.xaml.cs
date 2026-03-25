using System.Windows;
using System.Windows.Controls;
using Services.Core;

namespace AppMain.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();

            DataContext = (Application.Current as App)?.PlcTelemetry ?? new PlcTelemetryModel();
        }
    }
}

