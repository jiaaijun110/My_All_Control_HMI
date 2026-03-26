using FluentValidation;

namespace Common.CPlc
{
    public class PlcConfigValidator : AbstractValidator<PlcConfig>
    {
        public PlcConfigValidator()
        {
            // --- 1. 全局基础校验（无论什么环境都要过） ---

            RuleFor(x => x.IpAddress)
                .NotEmpty().WithMessage("配置项 IpAddress 不能为空")
                .Must(ip => !ip.Contains("99999")) // 额外加个防呆，防止乱写
                .WithMessage("IP 地址包含非法连续数字");

            // 将正则检查移到这里，取消 RuleSet 限制
            RuleFor(x => x.IpAddress)
                .Matches(@"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$")
                .When(x => !x.UseMock)
                .WithMessage("PLC IP 地址格式非法（必须是标准的 IPv4）");

            RuleFor(x => x.Port)
                .InclusiveBetween(1, 65535)
                .WithMessage("端口号必须在 1-65535 之间");

            RuleFor(x => x.HeartbeatDbNumber)
                .InclusiveBetween(0, int.MaxValue)
                .WithMessage("HeartbeatDbNumber 必须为非负整数");

            RuleFor(x => x.HeartbeatByteOffset)
                .InclusiveBetween(0, int.MaxValue)
                .WithMessage("HeartbeatByteOffset 必须为非负整数");

            RuleFor(x => x.HeartbeatValueByteSize)
                .Must(size => size == 2 || size == 4)
                .WithMessage("HeartbeatValueByteSize 仅支持 2(整数) 或 4(DINT)");

            RuleFor(x => x.HeartbeatTimeoutMs)
                .GreaterThan(0)
                .WithMessage("HeartbeatTimeoutMs 必须大于 0");

            RuleFor(x => x.ReconnectIntervalMs)
                .GreaterThan(0)
                .WithMessage("ReconnectIntervalMs 必须大于 0");
        }
    }
}