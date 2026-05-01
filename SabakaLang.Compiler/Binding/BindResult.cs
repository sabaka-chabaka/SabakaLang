namespace SabakaLang.Compiler.Binding;

public sealed class BindResult(SymbolTable symbols, IReadOnlyList<BindError> errors, IReadOnlyList<BindWarning> warnings)
{
    public SymbolTable Symbols { get; } = symbols;
    public IReadOnlyList<BindError> Errors { get; } = errors;
    public IReadOnlyList<BindWarning> Warnings { get; } = warnings;
    public bool HasErrors => Errors.Count > 0;
}
