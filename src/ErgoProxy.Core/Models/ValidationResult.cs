namespace ErgoProxy.Core.Models;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success() => new(true, Array.Empty<string>());
    public static ValidationResult Failure(params string[] errors) => new(false, errors.ToList());
    public static ValidationResult Failure(IEnumerable<string> errors) => new(false, errors.ToList());
}
