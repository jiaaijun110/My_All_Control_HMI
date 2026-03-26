using System.Windows;
using Services.Core;
using Services;

namespace AppMain
{
    public partial class App : Application
    {
        private PlcPollingEngine? _plcPollingEngine;
		private PlcService? _plcService;
        private readonly PlcTelemetryModel _fallbackTelemetry = new();
        public PlcTelemetryModel PlcTelemetry => _plcPollingEngine?.Telemetry ?? _fallbackTelemetry;
		public PlcService PlcService => _plcService ?? throw new InvalidOperationException("PLC service 尚未初始化。");
        /// <param name="e">启动参数。</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            GlobalExceptionHandler.Register(this);
            LoggingDatabaseInitializer.InitializeSerilog();
            LoggingDatabaseInitializer.InitializeSqlite();
            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            LoggingDatabaseInitializer.LogStartupAudit(env);

            var plcContext = PlcChannelFactory.CreateContextFromConfiguration();
            _plcPollingEngine = new PlcPollingEngine(plcContext.Plc, plcContext.Config);
			_plcService = new PlcService(plcContext.Plc);
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

