namespace SabakaLang.Binder;

/// <summary>Assignability, common types and generic substitution/inference. Pure functions over <see cref="BoundType"/>.</summary>
internal static class TypeRules
{
    /// <summary>Can a value of type <paramref name="from"/> be stored where <paramref name="to"/> is expected?</summary>
    public static bool IsAssignable(BoundType from, BoundType to, out Conversion conversion)
    {
        conversion = Conversion.None;
        if (from.IsError || to.IsError) return true;
        if (from.Kind == TypeKind.Void || to.Kind == TypeKind.Void) return false;

        if (to.IsDynamic)
            return true;

        if (from.IsDynamic)
        {
            // value is only known at run time: make sure it ends up with the representation of the target
            if (to.Kind == TypeKind.Float) conversion = Conversion.IntToFloat;
            return true;
        }

        if (from.Kind == TypeKind.Null) return true;

        if (from.Same(to)) return true;

        switch (to.Kind)
        {
            case TypeKind.Float when from.Kind == TypeKind.Int:
                conversion = Conversion.IntToFloat;
                return true;
            case TypeKind.String when from.Kind == TypeKind.Char:
                conversion = Conversion.CharToString;
                return true;
            case TypeKind.Array when from.Kind == TypeKind.Array:
                return from.Element!.IsDynamic || to.Element!.IsDynamic || from.Element.Same(to.Element!);
            case TypeKind.Class when from.Kind == TypeKind.Class:
                return IsSubclass(from, to);
            case TypeKind.Interface when from.Kind is TypeKind.Class or TypeKind.Interface:
                return from.TypeSymbol is { } fs && to.TypeSymbol is { } ts && fs.Implements(ts);
            default:
                return false;
        }
    }

    private static bool IsSubclass(BoundType from, BoundType to)
    {
        if (from.TypeSymbol is not { } fs || to.TypeSymbol is not { } ts) return false;
        if (!fs.IsSubclassOf(ts)) return false;
        // same generic class: type arguments must agree (raw types are lenient)
        if (ReferenceEquals(fs, ts) && from.Args.Count > 0 && to.Args.Count > 0)
            return from.Args.Count == to.Args.Count && from.Args.Zip(to.Args).All(p => p.First.Same(p.Second) || p.First.IsDynamic || p.Second.IsDynamic);
        return true;
    }

    /// <summary>Type of <c>cond ? a : b</c>, <c>a ?? b</c> and of array literal elements; null when there is none.</summary>
    public static BoundType? CommonType(BoundType a, BoundType b)
    {
        if (a.IsError) return b;
        if (b.IsError) return a;
        if (a.IsDynamic || b.IsDynamic) return BoundType.Dynamic;
        if (a.Kind == TypeKind.Null && b.Kind == TypeKind.Null) return BoundType.Null;
        if (a.Kind == TypeKind.Null) return b.WithNullable(true);
        if (b.Kind == TypeKind.Null) return a.WithNullable(true);
        var nullable = a.IsNullable || b.IsNullable;
        if (a.Same(b)) return a.WithNullable(nullable);
        if (a.IsNumeric && b.IsNumeric) return BoundType.Float.WithNullable(nullable);
        if (a.Kind == TypeKind.Char && b.Kind == TypeKind.String || a.Kind == TypeKind.String && b.Kind == TypeKind.Char)
            return BoundType.String.WithNullable(nullable);
        if (IsAssignable(a, b, out var c1) && c1 == Conversion.None) return b.WithNullable(nullable);
        if (IsAssignable(b, a, out var c2) && c2 == Conversion.None) return a.WithNullable(nullable);
        return null;
    }

    /// <summary>Replaces type parameters according to <paramref name="map"/> (unbound parameters stay as they are).</summary>
    public static BoundType Apply(BoundType t, IReadOnlyDictionary<TypeParamSymbol, BoundType> map)
    {
        if (map.Count == 0) return t;
        switch (t.Kind)
        {
            case TypeKind.TypeParam:
                return map.TryGetValue((TypeParamSymbol)t.Symbol!, out var r) ? r.WithNullable(t.IsNullable || r.IsNullable) : t;
            case TypeKind.Array:
                return BoundType.ArrayOf(Apply(t.Element!, map), t.IsNullable);
            case TypeKind.Class or TypeKind.Interface when t.Args.Count > 0:
                return BoundType.Of((TypeSymbol)t.Symbol!, t.Args.Select(a => Apply(a, map)).ToList(), t.IsNullable);
            default:
                return t;
        }
    }

    public static Dictionary<TypeParamSymbol, BoundType> MapFor(IReadOnlyList<TypeParamSymbol> ps, IReadOnlyList<BoundType> args)
    {
        var map = new Dictionary<TypeParamSymbol, BoundType>();
        for (var i = 0; i < ps.Count; i++)
            map[ps[i]] = i < args.Count ? args[i] : BoundType.Dynamic; // raw use of a generic type behaves like dynamic
        return map;
    }

    /// <summary>Infers type parameters of <paramref name="pattern"/> from <paramref name="actual"/>. False on a definite conflict.</summary>
    public static bool Unify(BoundType pattern, BoundType actual, Dictionary<TypeParamSymbol, BoundType> map, ISet<TypeParamSymbol> inferable)
    {
        if (actual.IsError) return true;
        switch (pattern.Kind)
        {
            case TypeKind.TypeParam when inferable.Contains((TypeParamSymbol)pattern.Symbol!):
            {
                var p = (TypeParamSymbol)pattern.Symbol!;
                if (actual.Kind == TypeKind.Null) return true; // says nothing about T
                if (!map.TryGetValue(p, out var bound))
                {
                    map[p] = actual.WithNullable(false);
                    return true;
                }
                var common = CommonType(bound, actual.WithNullable(false));
                if (common is null) return false;
                map[p] = common;
                return true;
            }
            case TypeKind.Array:
                return actual.Kind == TypeKind.Array
                    ? Unify(pattern.Element!, actual.Element!, map, inferable)
                    : actual.IsDynamic;
            case TypeKind.Class or TypeKind.Interface when pattern.Args.Count > 0:
                if (actual.Symbol == pattern.Symbol && actual.Args.Count == pattern.Args.Count)
                    return pattern.Args.Zip(actual.Args).All(p => Unify(p.First, p.Second, map, inferable));
                return true; // not an instantiation of the same generic type – assignability decides
            default:
                return true;
        }
    }

    public static bool ContainsTypeParam(BoundType t, ISet<TypeParamSymbol> ps) => t.Kind switch
    {
        TypeKind.TypeParam => ps.Contains((TypeParamSymbol)t.Symbol!),
        TypeKind.Array => ContainsTypeParam(t.Element!, ps),
        _ => t.Args.Any(a => ContainsTypeParam(a, ps))
    };
}
