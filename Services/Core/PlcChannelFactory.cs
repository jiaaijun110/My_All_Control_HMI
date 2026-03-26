using Common.CPlc;
using Drivers.DPlc.Siemens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Services.Core
{
    public static class PlcChannelFactory
    {
        public sealed record PlcRuntimeContext(IPlc Plc, PlcConfig Config);

        /// <param name="loggerFactory">日志工厂；为空则使用最小控制台输出。</param>
        /// <returns>PLC 访问接口。</returns>
        public static IPlc CreateFromConfiguration(ILoggerFactory? loggerFactory = null)
        {
            return CreateContextFromConfiguration(loggerFactory).Plc;
        }

        /// <summary>
        /// 创建 PLC 运行上下文：同时返回驱动实例与配置（用于心跳/重连逻辑）。
        /// </summary>
        public static PlcRuntimeContext CreateContextFromConfiguration(ILoggerFactory? loggerFactory = null)
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
                UseMock = bool.TryParse(section["UseMock"], out var mock) && mock,

                HeartbeatDbNumber = int.TryParse(section["HeartbeatDbNumber"], out var hbDb) ? hbDb : 7,
                HeartbeatByteOffset = int.TryParse(section["HeartbeatByteOffset"], out var hbOffset) ? hbOffset : 0,
                HeartbeatValueByteSize = int.TryParse(section["HeartbeatValueByteSize"], out var hbSize) ? hbSize : 4,
                HeartbeatTimeoutMs = int.TryParse(section["HeartbeatTimeoutMs"], out var hbTimeout) ? hbTimeout : 5000,
                ReconnectIntervalMs = int.TryParse(section["ReconnectIntervalMs"], out var hbReconnect) ? hbReconnect : 5000
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
                return new PlcRuntimeContext(new MockPlcDriver(factory.CreateLogger<MockPlcDriver>(), options), plcConfig);
            }

            return new PlcRuntimeContext(new RealPlcDriver(factory.CreateLogger<RealPlcDriver>(), options), plcConfig);
        }
    }
}

