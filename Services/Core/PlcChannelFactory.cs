using Common.CPlc;
using Drivers.DPlc.Siemens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Services.Core
{
    public static class PlcChannelFactory
    {
        /// <param name="loggerFactory">日志工厂；为空则使用最小控制台输出。</param>
        /// <returns>PLC 访问接口。</returns>
        public static IPlc CreateFromConfiguration(ILoggerFactory? loggerFactory = null)
        {
            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var configuration = new ConfigurationBuilder()
                .SetBasePath(baseDir)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
                .Build();

            var section = configuration.GetSection("Plc");
            var plcConfig = new PlcConfig
            {
                IpAddress = section["IpAddress"] ?? string.Empty,
                Port = int.TryParse(section["Port"], out var port) ? port : 102,
                UseMock = bool.TryParse(section["UseMock"], out var mock) && mock
            };

            var factory = loggerFactory ?? LoggerFactory.Create(static b =>
            {
                b.SetMinimumLevel(LogLevel.Information);
                b.AddDebug();
                b.AddConsole();
            });
            var options = Options.Create(plcConfig);

            if (plcConfig.UseMock)
            {
                return new MockPlcDriver(factory.CreateLogger<MockPlcDriver>(), options);
            }

            return new RealPlcDriver(factory.CreateLogger<RealPlcDriver>(), options);
        }
    }
}

