using SabakaLang.Compiler.Binding;

namespace SabakaLang.Compiler.Compiling;

public sealed class CompileResult(
    IReadOnlyList<Instruction> code,
    IReadOnlyList<CompileError> errors,
    SymbolTable? symbols = null)
{
    public IReadOnlyList<Instruction>  Code    { get; } = code;
    public IReadOnlyList<CompileError> Errors  { get; } = errors;
    public SymbolTable                 Symbols { get; } = symbols ?? new SymbolTable();
    public bool HasErrors => Errors.Count > 0;
}
