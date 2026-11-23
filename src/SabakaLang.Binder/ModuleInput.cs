using SabakaLang.AST.Interfaces;

namespace SabakaLang.Binder;

#if SABAKA_CS15
// C# 15 union: an import resolves to exactly one of these shapes. Pattern matching on the cases is exhaustive.
public sealed record NativeImport(string Module);
public sealed record SourceImport(string Module);
public union ImportTarget(NativeImport, SourceImport);
#else
/// <summary>What an <c>import "path"</c> resolved to: a native (C#) module or another script module.</summary>
public abstract record ImportTarget;
public sealed record NativeImport(string Module) : ImportTarget;
public sealed record SourceImport(string Module) : ImportTarget;
#endif

/// <summary>One parsed script file handed to the binder. Dependencies must precede the modules that import them.</summary>
public sealed class ModuleInput
{
    /// <summary>Unique module name (<c>main</c> for the entry script, otherwise derived from the file name).</summary>
    public required string Name { get; init; }

    public bool IsRoot { get; init; }

    public required IReadOnlyList<IStmt> Statements { get; init; }

    /// <summary>Import path as written in source → what it resolved to. Filled by the loader (see SabakaLang.Engine).</summary>
    public IReadOnlyDictionary<string, ImportTarget> Imports { get; init; } = new Dictionary<string, ImportTarget>();
}
