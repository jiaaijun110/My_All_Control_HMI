using System;
using System.Windows;
using System.Windows.Controls;
using AppMain.ViewModels;
using Services;
using Services.Core;

namespace AppMain.Views
{
    public partial class MotorView : UserControl
    {
        private MotorDebugViewModel? _viewModel;

        public MotorView()
        {
            InitializeComponent();

            var telemetry = (Application.Current as App)?.PlcTelemetry ?? new PlcTelemetryModel();
            var plc = PlcChannelFactory.CreateFromConfiguration();
            var plcService = new PlcService(plc);

            _viewModel = new MotorDebugViewModel(telemetry, plcService);
            DataContext = _viewModel;

            Unloaded += MotorView_OnUnloaded;
        }

        private void MotorView_OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= MotorView_OnUnloaded;
            _viewModel?.Dispose();
            _viewModel = null;
        }
    }
}
