using Microsoft.Extensions.Options;
using FluentValidation;

public class FluentValidateOptions<TOptions> : IValidateOptions<TOptions> where TOptions : class
{
    private readonly IValidator<TOptions> _validator;
    public string? Name { get; }

    public FluentValidateOptions(string? name, IValidator<TOptions> validator)
    {
        Name = name;
        _validator = validator;
    }

    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        // 过滤掉不匹配的命名选项
        if (Name != null && name != Name) return ValidateOptionsResult.Skip;

        var result = _validator.Validate(options);
        if (result.IsValid) return ValidateOptionsResult.Success;

        // 将所有错误拼接成工业级诊断信息
        var errors = result.Errors.Select(e => $"[配置错误] 属性: {e.PropertyName}, 原因: {e.ErrorMessage}");
        return ValidateOptionsResult.Fail(errors);
    }
}