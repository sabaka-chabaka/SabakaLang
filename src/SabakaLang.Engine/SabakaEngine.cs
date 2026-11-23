using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.Binder;
using SabakaLang.Compiler;
using SabakaLang.Extensions;
using SabakaLang.Lexer.Structures;
using SabakaLang.Runtime;
using SabakaLang.StdLib;
using SabakaLang.VM;
using BinderImpl = SabakaLang.Binder.Binder;
using CompilerImpl = SabakaLang.Compiler.Compiler;

namespace SabakaLang.Engine;

public sealed class CompilationResult
{
    public CompiledProgram? Program { get; init; }
    public BindResult? Bind { get; init; }
    public required IReadOnlyList<Diagnostic> Errors { get; init; }
    public required IReadOnlyList<Diagnostic> Warnings { get; init; }
    public bool Success => Program is not null && Errors.Count == 0;
}

public sealed class RunResult
{
    public required CompilationResult Compilation { get; init; }
    public int ExitCode { get; init; }

    /// <summary>Set when the script failed while running.</summary>
    public RuntimeException? RuntimeError { get; init; }

    public bool Success => Compilation.Success && RuntimeError is null;
    public IReadOnlyList<Diagnostic> Errors => Compilation.Errors;
}

/// <summary>
/// Entry point for embedding SabakaLang: register extensions, then compile and run scripts.
/// <code>
/// var engine = new SabakaEngine();
/// engine.Extensions.RegisterType&lt;MyModule&gt;();
/// engine.Run("print(\"hi\");");
/// </code>
/// </summary>
public sealed class SabakaEngine
{
    public ExtensionRegistry Extensions { get; }
    public IModuleSource? ModuleSource { get; set; }
    public TextWriter Out { get; set; } = Console.Out;
    public TextReader In { get; set; } = Console.In;

    /// <summary>Handed to native functions as <see cref="NativeContext.Host"/>.</summary>
    public object? Host { get; set; }

    public SabakaEngine(bool standardLibrary = true, ExtensionRegistry? registry = null)
    {
        Extensions = registry ?? new ExtensionRegistry();
        if (standardLibrary) StandardLibrary.Install(Extensions);
    }

    // ------------------------------------------------------------------ compile

    public CompilationResult Compile(string source, string? path = null)
    {
        var errors = new List<Diagnostic>();
        var warnings = new List<Diagnostic>();
        var order = new List<ModuleInput>();
        var loader = new Loader(this, order, errors);

        var rootId = path is null ? null : Path.GetFullPath(path);
        var rootName = path is null ? "main" : Path.GetFileNameWithoutExtension(path);
        loader.Load(rootId ?? "<main>", rootName, source, isRoot: true);

        if (errors.Count > 0) return new CompilationResult { Errors = errors, Warnings = warnings };

        var bind = new BinderImpl(Extensions).Bind(order);
        errors.AddRange(bind.Errors);
        warnings.AddRange(bind.Warnings);
        if (errors.Count > 0) return new CompilationResult { Bind = bind, Errors = errors, Warnings = warnings };

        var program = new CompilerImpl().Compile(bind);
        return new CompilationResult { Program = program, Bind = bind, Errors = errors, Warnings = warnings };
    }

    public CompilationResult CompileFile(string path) => Compile(File.ReadAllText(path), path);

    // ------------------------------------------------------------------ run

    public VirtualMachine CreateVm(CompiledProgram program) =>
        new(program, Extensions, new VmOptions { Out = Out, In = In, Host = Host });

    public RunResult Run(string source, string? path = null)
    {
        var compilation = Compile(source, path);
        if (!compilation.Success) return new RunResult { Compilation = compilation, ExitCode = 1 };

        var vm = CreateVm(compilation.Program!);
        try
        {
            vm.Run();
            return new RunResult { Compilation = compilation, ExitCode = vm.ExitCode };
        }
        catch (RuntimeException ex)
        {
            return new RunResult { Compilation = compilation, RuntimeError = ex, ExitCode = 1 };
        }
    }

    public RunResult RunFile(string path) => Run(File.ReadAllText(path), path);

    // ------------------------------------------------------------------ module loading

    private sealed class Loader(SabakaEngine engine, List<ModuleInput> order, List<Diagnostic> errors)
    {
        private readonly Dictionary<string, string> _namesById = new();
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading = new();

        public string Load(string id, string preferredName, string source, bool isRoot)
        {
            var name = isRoot ? preferredName : UniqueName(preferredName);
            if (!isRoot) _namesById[id] = name;
            _names.Add(name);
            _loading.Add(id);

            var label = isRoot ? "" : name;
            var lex = new SabakaLang.Lexer.Lexer().Tokenize(source);
            foreach (var e in lex.Errors) errors.Add(new Diagnostic(e.Message, e.Position, label));
            var parse = new SabakaLang.Parser.Parser(lex).Parse();
            foreach (var e in parse.Errors) errors.Add(new Diagnostic(e.Message, e.Position, label));

            var imports = new Dictionary<string, ImportTarget>();
            foreach (var imp in parse.Statements.OfType<ImportStmt>())
            {
                if (imports.ContainsKey(imp.Path)) continue;

                if (engine.Extensions.FindModule(imp.Path) is { } native)
                {
                    imports[imp.Path] = new NativeImport(native.Name);
                    continue;
                }

                var text = engine.ModuleSource?.Load(imp.Path, id == "<main>" ? null : id);
                if (text is null)
                {
                    errors.Add(new Diagnostic($"Cannot resolve import '{imp.Path}': it is neither a registered native module nor a script file.", imp.Span.Start, label));
                    continue;
                }

                if (_loading.Contains(text.Id))
                {
                    errors.Add(new Diagnostic($"Circular import of '{imp.Path}'.", imp.Span.Start, label));
                    continue;
                }

                var childName = _namesById.TryGetValue(text.Id, out var known) ? known : Load(text.Id, text.Name, text.Source, isRoot: false);
                imports[imp.Path] = new SourceImport(childName);
            }

            _loading.Remove(id);
            order.Add(new ModuleInput { Name = name, IsRoot = isRoot, Statements = parse.Statements, Imports = imports });
            return name;
        }

        private string UniqueName(string name)
        {
            var candidate = name;
            for (var i = 2; _names.Contains(candidate); i++) candidate = $"{name}_{i}";
            return candidate;
        }
    }
}
