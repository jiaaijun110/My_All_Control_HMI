using System.Windows;
using Services.Core;

namespace AppMain
{
    public partial class App : Application
    {
        private PlcPollingEngine? _plcPollingEngine;
        private readonly PlcTelemetryModel _fallbackTelemetry = new();
        public PlcTelemetryModel PlcTelemetry => _plcPollingEngine?.Telemetry ?? _fallbackTelemetry;
        /// <param name="e">启动参数。</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            GlobalExceptionHandler.Register(this);
            LoggingDatabaseInitializer.InitializeSerilog();
            LoggingDatabaseInitializer.InitializeSqlite();
            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            LoggingDatabaseInitializer.LogStartupAudit(env);

            var plc = PlcChannelFactory.CreateFromConfiguration();
            _plcPollingEngine = new PlcPollingEngine(plc);
            _plcPollingEngine.Start();
            LoggingDatabaseInitializer.LogInformation(">>> 2路传感器后台采集引擎已启动 <<<");

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        /// <param name="e">退出参数。</param>
        protected override void OnExit(ExitEventArgs e)
        {
            LoggingDatabaseInitializer.LogInformation(">>> 软件准备关闭，开始清理系统资源 <<<");
            _plcPollingEngine?.Dispose();
            LoggingDatabaseInitializer.Shutdown();
            base.OnExit(e);
        }
    }
}

