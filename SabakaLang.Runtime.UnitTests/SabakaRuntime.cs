using SabakaLang.Compiler;

namespace SabakaLang.Runtime.UnitTests;

public sealed class SabakaRuntime
{
    private readonly Dictionary<string, Func<Value[], Value>> _externals = new();
    private readonly Dictionary<string, int> _externalArities = new();
    
    public void RegisterExternal(string name, int paramCount, Func<Value[], Value> impl)
    {
        _externals[name]       = impl;
        _externalArities[name] = paramCount;
    }

    public RunResult Run(string source,
                         TextReader?  input    = null,
                         TextWriter?  output   = null,
                         int          gcThreshold = 512)
    {
        var compile = Compile(source);
        if (compile.HasErrors)
            return new RunResult(compile.Errors.Select(e => e.ToString()).ToList(), null);

        var vm = BuildVm(input, output, gcThreshold);
        try
        {
            vm.Execute(compile.Code.ToList());
            return new RunResult([], vm);
        }
        catch (RuntimeException ex)
        {
            return new RunResult([ex.Message], vm);
        }
    }
    
    public CompileResult Compile(string source)
    {
        var lex   = new Lexer(source).Tokenize();
        if (lex.Errors.Count > 0)
        {
            var errs = lex.Errors.Select(e => new CompileError(e.Message, e.Position)).ToList();
            return new CompileResult([], errs);
        }

        var parse = new Parser(lex).Parse();
        if (parse.HasErrors)
        {
            var errs = parse.Errors.Select(e => new CompileError(e.Message, e.Position)).ToList();
            return new CompileResult([], errs);
        }

        var binder = new Binder();
        
        var externalSymbols = _externalArities.Select(kvp => 
            new Symbol(
                name: kvp.Key, 
                kind: SymbolKind.Function,
                type: "dynamic",
                span: new Span(default, default),
                parameters: new string(',', kvp.Value) 
            )
        );

        binder.AddGlobalSymbols(externalSymbols);
        
        var comp  = new Compiler.Compiler();

        foreach (var (name, arity) in _externalArities)
            comp.RegisterExternal(name, arity);

        return comp.Compile(parse.Statements, binder.Bind(parse.Statements));
    }
    
    public string RunAndCapture(string source, string? stdinText = null, int gcThreshold = 512)
    {
        var output = new StringWriter();
        var input  = stdinText is not null
            ? (TextReader)new StringReader(stdinText)
            : TextReader.Null;

        var result = Run(source, input, output, gcThreshold);

        if (result.Errors.Count > 0)
            throw new RuntimeException(string.Join("\n", result.Errors));

        return output.ToString();
    }
    
    private VirtualMachine BuildVm(TextReader? input, TextWriter? output, int gcThreshold) =>
        new(input, output, _externals, gcThreshold);
}

public sealed record RunResult(
    IReadOnlyList<string> Errors,
    VirtualMachine?       Vm)
{
    public bool HasErrors => Errors.Count > 0;
}