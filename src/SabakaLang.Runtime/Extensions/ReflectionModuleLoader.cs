using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SabakaLang.Extensions;

/// <summary>Builds <see cref="NativeModule"/>s from C# types carrying the Sabaka* attributes.</summary>
public static class ReflectionModuleLoader
{
    private const BindingFlags Pub = BindingFlags.Public;

    public static IReadOnlyList<NativeModule> LoadAssembly(System.Reflection.Assembly assembly)
    {
        var builders = new Dictionary<string, ModuleBuilder>(StringComparer.OrdinalIgnoreCase);
        ModuleBuilder Get(string name) => builders.TryGetValue(name, out var b) ? b : builders[name] = new ModuleBuilder(name);

        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        foreach (var t in types)
        {
            if (t.GetCustomAttribute<SabakaModuleAttribute>() is { } m)
            {
                var b = Get(m.Name);
                if (m.Description is not null) b.Description(m.Description);
                if (m.AutoImport) b.AutoImport();
                FillModule(b, t);
            }
            else if (typeof(ISabakaExtension).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }
                     && t.GetConstructor(Type.EmptyTypes) is not null)
            {
                var ext = (ISabakaExtension)Activator.CreateInstance(t)!;
                ext.Configure(Get(ext.Name));
            }
            else if (t.GetCustomAttribute<SabakaClassAttribute>() is { Module: { } mod } && t.DeclaringType?.GetCustomAttribute<SabakaModuleAttribute>() is null)
            {
                Get(mod).AddClass(BuildClass(mod, t));
            }
            else if (t.GetCustomAttribute<SabakaEnumAttribute>() is { Module: { } emod } && t.DeclaringType?.GetCustomAttribute<SabakaModuleAttribute>() is null)
            {
                Get(emod).AddEnum(BuildEnum(t));
            }
        }

        return builders.Values.Select(b => b.Build()).ToList();
    }

    /// <summary>Loads one [SabakaModule] class.</summary>
    public static NativeModule LoadModuleType(Type type)
    {
        var attr = type.GetCustomAttribute<SabakaModuleAttribute>()
                   ?? throw new ArgumentException($"{type.Name} is not tagged with [SabakaModule].");
        var b = new ModuleBuilder(attr.Name);
        if (attr.Description is not null) b.Description(attr.Description);
        if (attr.AutoImport) b.AutoImport();
        FillModule(b, type);
        return b.Build();
    }

    private static void FillModule(ModuleBuilder b, Type t)
    {
        foreach (var m in t.GetMethods(Pub | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (m.GetCustomAttribute<SabakaFunctionAttribute>() is { } fa)
                b.AddFunction(BuildFunction(b.Name, m, fa));
            if (m.GetCustomAttribute<SabakaExtensionAttribute>() is { } ea)
                b.AddExtension(BuildExtension(b.Name, m, ea));
        }

        foreach (var f in t.GetFields(Pub | BindingFlags.Static | BindingFlags.DeclaredOnly))
            if (f.GetCustomAttribute<SabakaConstantAttribute>() is { } ca)
                b.AddConstant(BuildConstant(ca.Name ?? f.Name, f.FieldType, f.GetValue(null)));
        foreach (var p in t.GetProperties(Pub | BindingFlags.Static | BindingFlags.DeclaredOnly))
            if (p.GetCustomAttribute<SabakaConstantAttribute>() is { } ca)
                b.AddConstant(BuildConstant(ca.Name ?? p.Name, p.PropertyType, p.GetValue(null)));

        foreach (var n in t.GetNestedTypes(Pub))
        {
            if (n.GetCustomAttribute<SabakaClassAttribute>() is not null) b.AddClass(BuildClass(b.Name, n));
            else if (n.IsEnum && n.GetCustomAttribute<SabakaEnumAttribute>() is not null) b.AddEnum(BuildEnum(n));
        }
    }

    private static NativeConstant BuildConstant(string name, Type type, object? value)
    {
        var ctx = ConstantContext.Instance;
        return new NativeConstant(name, Marshaller.MapType(type), Marshaller.ToValue(value, ctx));
    }

    private static NativeEnum BuildEnum(Type t) => new(Marshaller.EnumName(t), Enum.GetNames(t), t);

    // ---------------------------------------------------------------- functions / extensions

    private sealed record Bound(NativeSignature Signature, Func<NativeContext, object?, Value[], Value> Call);

    /// <summary>Creates a delegate that converts Sabaka arguments to the C# parameters of <paramref name="m"/> and back.</summary>
    private static Bound Bind(MethodInfo m, string name, string? overrideSig, int skip /* leading params that are not part of the signature (receiver) */)
    {
        var ps = m.GetParameters();
        var ctxFirst = ps.Length > 0 && ps[0].ParameterType == typeof(NativeContext);
        var first = ctxFirst ? 1 : 0;
        var hostParams = ps.Skip(first + skip).ToArray();
        var variadic = hostParams.Length > 0 && hostParams[^1].GetCustomAttribute<ParamArrayAttribute>() is not null;

        NativeSignature sig;
        if (overrideSig is not null) sig = NativeSignature.Parse(overrideSig);
        else
        {
            var list = hostParams.Select((p, i) =>
            {
                var type = variadic && i == hostParams.Length - 1
                    ? Marshaller.MapType(p.ParameterType.GetElementType()!)
                    : Marshaller.MapParameter(p);
                return new NativeParameter(type, p.Name ?? $"arg{i}");
            }).ToList();
            sig = new NativeSignature(name, Marshaller.MapReturn(m), list, variadic);
        }

        Func<NativeContext, object?, Value[], Value> call = (ctx, receiver, args) =>
        {
            var callArgs = new object?[ps.Length];
            var k = 0;
            if (ctxFirst) callArgs[k++] = ctx;
            var argIndex = 0;
            object? recv = receiver;
            if (skip == 1)
            {
                // receiver passed as first managed parameter (extension methods)
                callArgs[k++] = Marshaller.ToHost((Value)receiver!, ps[first].ParameterType, ctx);
                recv = null;
            }
            for (var i = 0; i < hostParams.Length; i++)
            {
                var p = hostParams[i];
                if (variadic && i == hostParams.Length - 1)
                {
                    var el = p.ParameterType.GetElementType()!;
                    var rest = args.Length - argIndex;
                    var arr = Array.CreateInstance(el, Math.Max(rest, 0));
                    for (var j = 0; j < rest; j++) arr.SetValue(Marshaller.ToHost(args[argIndex + j], el, ctx), j);
                    callArgs[k++] = arr;
                }
                else
                {
                    callArgs[k++] = Marshaller.ToHost(args[argIndex++], p.ParameterType, ctx);
                }
            }

            object? result;
            try { result = m.Invoke(skip == 1 ? null : recv, callArgs); }
            catch (TargetInvocationException ex) when (ex.InnerException is RuntimeException re)
            {
                ExceptionDispatchInfo.Capture(re).Throw();
                throw;
            }
            catch (TargetInvocationException ex)
            {
                throw new RuntimeException($"{name}: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
            }
            return Marshaller.ToValue(result, ctx);
        };

        return new Bound(sig, call);
    }

    private static NativeFunction BuildFunction(string module, MethodInfo m, SabakaFunctionAttribute a)
    {
        if (!m.IsStatic) throw new InvalidOperationException($"[SabakaFunction] method {m.Name} must be static.");
        var name = a.Name ?? Marshaller.Camel(m.Name);
        var bound = Bind(m, name, a.Signature, 0);
        return new NativeFunction
        {
            Module = module, Signature = bound.Signature, Description = a.Description,
            Implementation = (ctx, args) => bound.Call(ctx, null, args)
        };
    }

    private static NativeExtension BuildExtension(string module, MethodInfo m, SabakaExtensionAttribute a)
    {
        if (!m.IsStatic) throw new InvalidOperationException($"[SabakaExtension] method {m.Name} must be static.");
        var name = a.Name ?? Marshaller.Camel(m.Name);
        var bound = Bind(m, name, a.Signature, 1);
        return new NativeExtension
        {
            Module = module, Target = TypeSyntax.Parse(a.Target).ToString(), Signature = bound.Signature, IsProperty = a.IsProperty,
            Implementation = (ctx, self, args) => bound.Call(ctx, self, args)
        };
    }

    // ---------------------------------------------------------------- classes

    private static NativeClass BuildClass(string module, Type t)
    {
        var attr = t.GetCustomAttribute<SabakaClassAttribute>()!;
        var cls = new NativeClass { Module = module, Name = attr.Name ?? t.Name, ClrType = t, Description = attr.Description };

        var ctors = t.GetConstructors(Pub | BindingFlags.Instance);
        var tagged = ctors.Where(c => c.GetCustomAttribute<SabakaConstructorAttribute>() is not null).ToList();
        foreach (var c in tagged.Count > 0 ? tagged : ctors.ToList())
            cls.Constructors.Add(BuildConstructor(cls.Name, c));

        foreach (var m in t.GetMethods(Pub | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (m.GetCustomAttribute<SabakaMethodAttribute>() is not { } ma) continue;
            var name = ma.Name ?? Marshaller.Camel(m.Name);
            var bound = Bind(m, name, ma.Signature, 0);
            var isStatic = m.IsStatic;
            cls.Methods.Add(new NativeMethod
            {
                Class = cls.Name, Signature = bound.Signature, IsStatic = isStatic,
                Implementation = (ctx, self, args) => bound.Call(ctx, isStatic ? null : NativeOf(self, t), args)
            });
        }

        foreach (var p in t.GetProperties(Pub | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (p.GetCustomAttribute<SabakaPropertyAttribute>() is not { } pa || p.GetIndexParameters().Length > 0) continue;
            var name = pa.Name ?? Marshaller.Camel(p.Name);
            var pt = p.PropertyType;
            cls.Properties.Add(new NativeProperty
            {
                Class = cls.Name, Name = name,
                Type = Marshaller.MapType(pt, SafeNullability(p)),
                Getter = (ctx, self) => Marshaller.ToValue(p.GetValue(NativeOf(self, t)), ctx),
                Setter = p.SetMethod is { IsPublic: true }
                    ? (ctx, self, v) => p.SetValue(NativeOf(self, t), Marshaller.ToHost(v, pt, ctx))
                    : null
            });
        }

        foreach (var f in t.GetFields(Pub | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (f.GetCustomAttribute<SabakaPropertyAttribute>() is not { } pa) continue;
            var name = pa.Name ?? Marshaller.Camel(f.Name);
            var ft = f.FieldType;
            cls.Properties.Add(new NativeProperty
            {
                Class = cls.Name, Name = name, Type = Marshaller.MapType(ft),
                Getter = (ctx, self) => Marshaller.ToValue(f.GetValue(NativeOf(self, t)), ctx),
                Setter = f.IsInitOnly ? null : (ctx, self, v) => f.SetValue(NativeOf(self, t), Marshaller.ToHost(v, ft, ctx))
            });
        }

        return cls;
    }

    private static NullabilityInfo? SafeNullability(PropertyInfo p)
    {
        try { return new NullabilityInfoContext().Create(p); } catch { return null; }
    }

    private static object NativeOf(Value self, Type t)
    {
        var o = self.Object ?? throw new RuntimeException($"Expected {t.Name} instance, got {self.TypeName}");
        return o.Native is { } n && t.IsInstanceOfType(n)
            ? n
            : throw new RuntimeException($"Object of class '{o.ShortName}' has no {t.Name} state");
    }

    private static NativeConstructor BuildConstructor(string className, ConstructorInfo c)
    {
        var ps = c.GetParameters();
        var ctxFirst = ps.Length > 0 && ps[0].ParameterType == typeof(NativeContext);
        var host = ps.Skip(ctxFirst ? 1 : 0).ToArray();
        var sig = new NativeSignature("", "void",
            host.Select((p, i) => new NativeParameter(Marshaller.MapParameter(p), p.Name ?? $"arg{i}")).ToList());

        return new NativeConstructor
        {
            Class = className, Signature = sig,
            Implementation = (ctx, self, args) =>
            {
                var callArgs = new object?[ps.Length];
                var k = 0;
                if (ctxFirst) callArgs[k++] = ctx;
                for (var i = 0; i < host.Length; i++)
                    callArgs[k++] = Marshaller.ToHost(args[i], host[i].ParameterType, ctx);
                try { self.Native = c.Invoke(callArgs); }
                catch (TargetInvocationException ex) when (ex.InnerException is RuntimeException re)
                {
                    ExceptionDispatchInfo.Capture(re).Throw();
                }
                catch (TargetInvocationException ex)
                {
                    throw new RuntimeException($"{className}: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
                }
            }
        };
    }

    /// <summary>Context used only to convert constants at load time (no VM yet).</summary>
    private sealed class ConstantContext : NativeContext
    {
        public static readonly ConstantContext Instance = new();
        public override TextWriter Out => TextWriter.Null;
        public override TextReader In => TextReader.Null;
        public override ExtensionRegistry Registry { get; } = new();
        public override object? Host => null;
        public override Value CallFunction(string name, params Value[] args) => throw new NotSupportedException();
        public override Value CallMethod(Value target, string name, params Value[] args) => throw new NotSupportedException();
        public override Value NewObject(string className, params Value[] args) => throw new NotSupportedException();
        public override string Stringify(Value value) => value.ToString();
        protected override SabakaObject Allocate(string className) => new(className);
    }
}
