using System;
using System.Windows;
using System.Windows.Controls;
using Serilog;
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

            var app = Application.Current as App;
            var telemetry = app?.PlcTelemetry ?? new PlcTelemetryModel();
            // 复用与轮询引擎相同的 PLC 实例，避免 Mock 模式下写入与轮询状态不一致。
            var plcService = app?.PlcService ?? new PlcService(PlcChannelFactory.CreateFromConfiguration());

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

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            try
            {
                await _viewModel.OnStartButtonClickAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "DB9.DBX0.0 写入 true 失败");
            }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            try
            {
                await _viewModel.OnStopButtonClickAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "DB9.DBX0.1 写入 true 失败");
            }
        }
    }
}
