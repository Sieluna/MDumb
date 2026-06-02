namespace Dumb.Engine.Wgsl;

public enum DiagnosticSeverity
{
    Error,
    Warning
}

public readonly record struct WgslDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    int Line,
    string? FilePath)
{
    public override string ToString() => Severity switch
    {
        DiagnosticSeverity.Error => $"{FilePath}({Line}): error: {Message}",
        DiagnosticSeverity.Warning => $"{FilePath}({Line}): warning: {Message}",
        _ => $"{FilePath}({Line}): {Message}"
    };
}

public sealed class ProcessResult
{
    public string CombinedSource { get; init; } = "";
    public List<WgslDiagnostic> Diagnostics { get; init; } = [];
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public static ProcessResult Success(string source) => new() { CombinedSource = source };

    public static ProcessResult Failure(List<WgslDiagnostic> diagnostics) => new() { Diagnostics = diagnostics };
}
