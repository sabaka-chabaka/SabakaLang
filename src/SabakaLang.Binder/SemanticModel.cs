using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.Lexer.Structures;
using SabakaLang.Runtime;

namespace SabakaLang.Binder;

/// <summary>Implicit conversion the compiler must apply right after evaluating an expression.</summary>
public enum Conversion { None, IntToFloat, CharToString, CloneStruct }

public enum MemberKind { Field, Length, EnumMember, Constant, NativeProperty, ExtensionProperty, Dynamic, ModuleVariable }

/// <param name="Symbol">Field / property / constant / module variable the member refers to.</param>
public sealed record MemberBinding(MemberKind Kind, Symbol? Symbol = null, string? NativeKey = null);

public enum CallKind
{
    /// <summary>Script function: <c>Call id</c>.</summary>
    Function,
    /// <summary>Virtual script method (receiver is the callee object, or <c>this</c> when <see cref="CallBinding.ImplicitThis"/>).</summary>
    Method,
    /// <summary>Non-virtual call of a base-class method: <c>super.m()</c>.</summary>
    SuperMethod,
    /// <summary><c>super(args)</c> inside a constructor.</summary>
    SuperConstructor,
    /// <summary>Native global function or static native method (no receiver).</summary>
    Native,
    /// <summary>Native instance method or extension method (receiver pushed first).</summary>
    NativeMethod,
    /// <summary>Receiver type unknown at compile time: resolved by name at run time.</summary>
    Dynamic
}

public sealed record CallBinding(CallKind Kind, FunctionSymbol? Target, string? NativeKey, bool ImplicitThis, string? MethodName);

public enum NewKind { Class, Struct, Native }

public sealed record NewBinding(NewKind Kind, TypeSymbol Type, FunctionSymbol? Constructor);

public sealed record ForeachInfo(VariableSymbol Item, int CollectionSlot, int IndexSlot, Conversion ItemConversion);

public sealed record SwitchInfo(int ValueSlot);

/// <summary>Side tables produced by the binder. Keys are AST nodes compared by reference.</summary>
public sealed class SemanticModel
{
    private static Dictionary<TK, TV> RefMap<TK, TV>() where TK : class => new(ReferenceEqualityComparer.Instance as IEqualityComparer<TK>);

    public Dictionary<IExpr, BoundType> Types { get; } = RefMap<IExpr, BoundType>();
    public Dictionary<IExpr, Value> Constants { get; } = RefMap<IExpr, Value>();
    public Dictionary<IExpr, Conversion> Conversions { get; } = RefMap<IExpr, Conversion>();
    public Dictionary<NameExpr, Symbol> Names { get; } = RefMap<NameExpr, Symbol>();
    public Dictionary<MemberExpr, MemberBinding> Members { get; } = RefMap<MemberExpr, MemberBinding>();
    public Dictionary<CallExpr, CallBinding> Calls { get; } = RefMap<CallExpr, CallBinding>();
    public Dictionary<NewExpr, NewBinding> News { get; } = RefMap<NewExpr, NewBinding>();
    public Dictionary<IsExpr, BoundType> IsTypes { get; } = RefMap<IsExpr, BoundType>();
    public Dictionary<NewArrayExpr, BoundType> ArrayElementTypes { get; } = RefMap<NewArrayExpr, BoundType>();

    /// <summary>Declaration → symbol (VarDecl→VariableSymbol, FuncDecl→FunctionSymbol, ClassDecl→TypeSymbol, ...).</summary>
    public Dictionary<IStmt, Symbol> Declarations { get; } = RefMap<IStmt, Symbol>();

    public Dictionary<ForeachStmt, ForeachInfo> Foreach { get; } = RefMap<ForeachStmt, ForeachInfo>();
    public Dictionary<SwitchStmt, SwitchInfo> Switches { get; } = RefMap<SwitchStmt, SwitchInfo>();

    public BoundType TypeOf(IExpr e) => Types.GetValueOrDefault(e) ?? BoundType.Error;
}

public sealed record Diagnostic(string Message, Position Position, string Module = "", bool IsWarning = false)
{
    public override string ToString() =>
        (string.IsNullOrEmpty(Module) ? "" : $"({Module}) ") + $"[{Position.Line}:{Position.Column}] {Message}";
}

/// <summary>A bound source module (top-level code + declarations).</summary>
public sealed class BoundModule
{
    public required string Name { get; init; }
    public required bool IsRoot { get; init; }
    public required IReadOnlyList<IStmt> Statements { get; init; }

    /// <summary>Function that holds the module's top-level code (locals of nested blocks live in its frame).</summary>
    public required FunctionSymbol Entry { get; init; }

    public List<FunctionSymbol> Functions { get; } = [];
    public List<TypeSymbol> Types { get; } = [];
}

public sealed class BindResult
{
    public required SemanticModel Model { get; init; }
    public required IReadOnlyList<BoundModule> Modules { get; init; }
    public required IReadOnlyList<Diagnostic> Errors { get; init; }
    public required IReadOnlyList<Diagnostic> Warnings { get; init; }
    public required IReadOnlyList<Symbol> AllSymbols { get; init; }
    public int GlobalCount { get; init; }
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Convenience for tools/tests: symbols with the given name (any module, any scope).</summary>
    public IEnumerable<Symbol> Lookup(string name) => AllSymbols.Where(s => s.Name == name);
}
