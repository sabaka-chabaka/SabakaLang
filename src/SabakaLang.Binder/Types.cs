namespace SabakaLang.Binder;

public enum TypeKind { Error, Void, Null, Dynamic, Int, Float, Bool, String, Char, Array, Class, Interface, Struct, Enum, TypeParam }

/// <summary>A resolved, immutable static type. Compare with <see cref="Same"/> (ignores nullability) rather than reference equality.</summary>
public sealed class BoundType
{
    public TypeKind Kind { get; }
    public string Name { get; }
    public Symbol? Symbol { get; }
    public BoundType? Element { get; }
    public IReadOnlyList<BoundType> Args { get; }
    public bool IsNullable { get; }

    private BoundType(TypeKind kind, string name, Symbol? symbol = null, BoundType? element = null,
        IReadOnlyList<BoundType>? args = null, bool nullable = false)
    {
        Kind = kind;
        Name = name;
        Symbol = symbol;
        Element = element;
        Args = args ?? [];
        IsNullable = nullable;
    }

    public static readonly BoundType Error = new(TypeKind.Error, "<error>");
    public static readonly BoundType Void = new(TypeKind.Void, "void");
    public static readonly BoundType Null = new(TypeKind.Null, "null", nullable: true);
    public static readonly BoundType Dynamic = new(TypeKind.Dynamic, "dynamic");
    public static readonly BoundType Int = new(TypeKind.Int, "int");
    public static readonly BoundType Float = new(TypeKind.Float, "float");
    public static readonly BoundType Bool = new(TypeKind.Bool, "bool");
    public static readonly BoundType String = new(TypeKind.String, "string");
    public static readonly BoundType Char = new(TypeKind.Char, "char");

    public static BoundType ArrayOf(BoundType element, bool nullable = false) =>
        new(TypeKind.Array, element.ToString() + "[]", null, element, null, nullable);

    public static BoundType Of(TypeSymbol symbol, IReadOnlyList<BoundType>? args = null, bool nullable = false)
    {
        var kind = symbol.Kind switch
        {
            SymbolKind.Interface => TypeKind.Interface,
            SymbolKind.Struct => TypeKind.Struct,
            SymbolKind.Enum => TypeKind.Enum,
            _ => TypeKind.Class
        };
        return new BoundType(kind, symbol.Name, symbol, null, args, nullable);
    }

    public static BoundType Param(TypeParamSymbol p) => new(TypeKind.TypeParam, p.Name, p);

    public BoundType WithNullable(bool nullable) =>
        nullable == IsNullable || Kind is TypeKind.Error or TypeKind.Void or TypeKind.Dynamic or TypeKind.Null
            ? this
            : new BoundType(Kind, Name, Symbol, Element, Args.Count == 0 ? null : Args, nullable);

    public BoundType NonNull => WithNullable(false);

    public bool IsError => Kind == TypeKind.Error;
    public bool IsDynamic => Kind == TypeKind.Dynamic;
    public bool IsNumeric => Kind is TypeKind.Int or TypeKind.Float;
    public bool IsArray => Kind == TypeKind.Array;
    public bool IsStruct => Kind == TypeKind.Struct;
    public bool IsClassLike => Kind is TypeKind.Class or TypeKind.Interface;
    public TypeSymbol? TypeSymbol => Symbol as TypeSymbol;

    /// <summary>Types whose values can be null without being declared nullable (reference types).</summary>
    public bool IsReferenceType => Kind is TypeKind.String or TypeKind.Array or TypeKind.Class or TypeKind.Interface
        or TypeKind.Dynamic or TypeKind.Null or TypeKind.TypeParam;

    /// <summary>Identity string ignoring nullability.</summary>
    public string Key => Kind switch
    {
        TypeKind.Array => Element!.Key + "[]",
        TypeKind.Class or TypeKind.Interface or TypeKind.Struct or TypeKind.Enum =>
            ((TypeSymbol)Symbol!).Id + (Args.Count > 0 ? "<" + string.Join(",", Args.Select(a => a.Key)) + ">" : ""),
        TypeKind.TypeParam => "$" + Name + "@" + ((TypeParamSymbol)Symbol!).Uid,
        _ => Name
    };

    public bool Same(BoundType other) => Key == other.Key;

    public override string ToString()
    {
        var core = Kind switch
        {
            TypeKind.Array => Element!.ToString() + "[]",
            TypeKind.Class or TypeKind.Interface when Args.Count > 0 => Name + "<" + string.Join(", ", Args) + ">",
            _ => Name
        };
        return IsNullable && Kind != TypeKind.Null && Kind != TypeKind.Dynamic ? core + "?" : core;
    }
}
