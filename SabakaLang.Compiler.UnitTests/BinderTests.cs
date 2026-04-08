namespace SabakaLang.Compiler.UnitTests;

public class BinderTests
{
    private static BindResult Bind(string source)
    {
        var lexResult   = new Lexer(source).Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        if (parseResult.HasErrors)
            throw new Exception("Parse errors: " +
                                string.Join("\n", parseResult.Errors.Select(e => e.Message)));
        return new Binder().Bind(parseResult.Statements);
    }
 
    private static BindResult BindNoThrow(string source)
    {
        var lexResult   = new Lexer(source).Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        return new Binder().Bind(parseResult.Statements);
    }
 
    private static void AssertNoErrors(BindResult r) =>
        Assert.False(r.HasErrors, "Unexpected bind errors:\n" +
                                  string.Join("\n", r.Errors.Select(e => e.ToString())));
    
    [Fact]
    public void BuiltIn_Print_IsResolved()
    {
        var r = Bind("print(\"hello\");");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void BuiltIn_TimeMs_IsResolved()
    {
        var r = Bind("int t = timeMs();");
        AssertNoErrors(r);
    }
    
    [Fact]
    public void VarDecl_Simple_Declared()
    {
        var r = Bind("int x = 5;");
        AssertNoErrors(r);
        Assert.Single(r.Symbols.Lookup("x"));
    }
 
    [Fact]
    public void VarDecl_TypeRecorded()
    {
        var r = Bind("string name;");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("name"));
        Assert.Equal("string", sym.Type);
        Assert.Equal(SymbolKind.Variable, sym.Kind);
    }
 
    [Fact]
    public void VarDecl_Duplicate_Error()
    {
        var r = BindNoThrow("int x; int x;");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("x") && e.Message.Contains("already declared"));
    }
 
    [Fact]
    public void VarRef_Undeclared_Error()
    {
        var r = BindNoThrow("x + 1;");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("'x'"));
    }
 
    [Fact]
    public void VarRef_AfterDecl_NoError()
    {
        var r = Bind("int x = 0; int y = x + 1;");
        AssertNoErrors(r);
    }
    
    [Fact]
    public void FuncDecl_Simple_Declared()
    {
        var r = Bind("void foo() {}");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("foo"));
        Assert.Equal(SymbolKind.Function, sym.Kind);
        Assert.Equal("void", sym.Type);
    }
 
    [Fact]
    public void FuncDecl_Params_DeclaredInsideScope()
    {
        var r = Bind("int add(int a, int b) { return a + b; }");
        AssertNoErrors(r);
        Assert.Contains(r.Symbols.All, s => s.Name == "a" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(r.Symbols.All, s => s.Name == "b" && s.Kind == SymbolKind.Parameter);
    }
 
    [Fact]
    public void FuncDecl_ParamString_Recorded()
    {
        var r = Bind("int add(int a, int b) { return a + b; }");
        AssertNoErrors(r);
        var sym = r.Symbols.Lookup("add").First(s => s.Kind == SymbolKind.Function);
        Assert.Equal("int a, int b", sym.Parameters);
    }
 
    [Fact]
    public void FuncDecl_ReturnVoid_NoValueRequired()
    {
        var r = Bind("void doNothing() { return; }");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void FuncDecl_ReturnNonVoid_WithValue_NoError()
    {
        var r = Bind("int answer() { return 42; }");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void FuncDecl_ReturnVoid_WithValue_Error()
    {
        var r = BindNoThrow("void foo() { return 1; }");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("void"));
    }
 
    [Fact]
    public void FuncDecl_ReturnNonVoid_WithoutValue_Error()
    {
        var r = BindNoThrow("int foo() { return; }");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("return value"));
    }
 
    [Fact]
    public void Return_OutsideFunction_Error()
    {
        var r = BindNoThrow("return 1;");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("outside a function"));
    }
 
    [Fact]
    public void FuncDecl_ForwardRef_NoError()
    {
        const string src = "int fib(int n) { if (n <= 1) return n; return fib(n - 1) + fib(n - 2); }";
        var r = Bind(src);
        AssertNoErrors(r);
    }
 
    [Fact]
    public void FuncDecl_Generic_TypeParamDeclared()
    {
        var r = Bind("T identity<T>(T val) { return val; }");
        AssertNoErrors(r);
        Assert.Contains(r.Symbols.All, s => s.Name == "T" && s.Kind == SymbolKind.TypeParam);
    }
    
    [Fact]
    public void ClassDecl_Empty_Declared()
    {
        var r = Bind("class Foo {}");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("Foo"));
        Assert.Equal(SymbolKind.Class, sym.Kind);
    }
 
    [Fact]
    public void ClassDecl_Fields_DeclaredAsField()
    {
        var r = Bind("class Point { int x; int y; }");
        AssertNoErrors(r);
        var fields = r.Symbols.MembersOf("Point").ToList();
        Assert.Contains(fields, s => s.Name == "x" && s.Kind == SymbolKind.Field);
        Assert.Contains(fields, s => s.Name == "y" && s.Kind == SymbolKind.Field);
    }
 
    [Fact]
    public void ClassDecl_Methods_DeclaredAsMethod()
    {
        var r = Bind("class Counter { void increment() {} int get() { return 0; } }");
        AssertNoErrors(r);
        var methods = r.Symbols.MembersOf("Counter").ToList();
        Assert.Contains(methods, s => s.Name == "increment" && s.Kind == SymbolKind.Method);
        Assert.Contains(methods, s => s.Name == "get"       && s.Kind == SymbolKind.Method);
    }
 
    [Fact]
    public void ClassDecl_This_Declared()
    {
        var r = Bind("class Foo { void bar() {} }");
        AssertNoErrors(r);
        Assert.Contains(r.Symbols.All, s => s.Name == "this" && s.Type == "Foo");
    }
 
    [Fact]
    public void ClassDecl_Base_KnownType_NoError()
    {
        var r = Bind("class Animal {} class Dog : Animal {}");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void ClassDecl_Base_UnknownType_Error()
    {
        var r = BindNoThrow("class Dog : Animal {}");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("Animal"));
    }
 
    [Fact]
    public void ClassDecl_Interface_UnknownType_Error()
    {
        var r = BindNoThrow("class Cat : Animal, IFluffy {}");
        Assert.True(r.HasErrors);
    }
 
    [Fact]
    public void ClassDecl_Generic_TypeParam_Declared()
    {
        var r = Bind("class Box<T> {}");
        AssertNoErrors(r);
        Assert.Contains(r.Symbols.All, s => s.Name == "T" && s.Kind == SymbolKind.TypeParam);
    }
 
    [Fact]
    public void ClassDecl_Override_NoError()
    {
        var r = Bind("class Animal { void speak() {} } class Dog : Animal { override void speak() {} }");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Override_OutsideClass_Error()
    {
        var r = BindNoThrow("override void foo() {}");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("override"));
    }
    
    [Fact]
    public void InterfaceDecl_Declared()
    {
        var r = Bind("interface IShape {}");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("IShape"));
        Assert.Equal(SymbolKind.Interface, sym.Kind);
    }
 
    [Fact]
    public void InterfaceDecl_Methods_Declared()
    {
        var r = Bind("interface IShape { float area(); }");
        AssertNoErrors(r);
        Assert.Contains(r.Symbols.All, s => s.Name == "area" && s.Kind == SymbolKind.Method);
    }
 
    [Fact]
    public void InterfaceDecl_Parent_UnknownType_Error()
    {
        var r = BindNoThrow("interface IFlyingAnimal : IAnimal {}");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("IAnimal"));
    }
    
    [Fact]
    public void StructDecl_Declared()
    {
        var r = Bind("struct Vec2 { float x; float y; }");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("Vec2"));
        Assert.Equal(SymbolKind.Struct, sym.Kind);
    }
 
    [Fact]
    public void StructDecl_Fields_DeclaredAsField()
    {
        var r = Bind("struct Vec2 { float x; float y; }");
        AssertNoErrors(r);
        var fields = r.Symbols.MembersOf("Vec2").ToList();
        Assert.Contains(fields, s => s.Name == "x");
        Assert.Contains(fields, s => s.Name == "y");
    }
    
    [Fact]
    public void EnumDecl_Declared()
    {
        var r = Bind("enum Color { Red, Green, Blue }");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("Color"));
        Assert.Equal(SymbolKind.Enum, sym.Kind);
    }
 
    [Fact]
    public void EnumDecl_Members_Declared()
    {
        var r = Bind("enum Color { Red, Green, Blue }");
        AssertNoErrors(r);
        Assert.Contains(r.Symbols.All, s => s.Name == "Red"   && s.Kind == SymbolKind.EnumMember);
        Assert.Contains(r.Symbols.All, s => s.Name == "Green" && s.Kind == SymbolKind.EnumMember);
        Assert.Contains(r.Symbols.All, s => s.Name == "Blue"  && s.Kind == SymbolKind.EnumMember);
    }
    
    [Fact]
    public void Scope_VariableNotVisibleOutsideBlock()
    {
        const string src = "void foo() { if (true) { int x = 1; } int y = x; }";
        var r = BindNoThrow(src);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("'x'"));
    }
 
    [Fact]
    public void Scope_ForInit_VisibleInsideBody()
    {
        var r = Bind("void foo() { for (int i = 0; i < 10; i = i + 1) { int x = i; } }");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Scope_ForeachVar_VisibleInsideBody()
    {
        var r = Bind("void foo(int[] nums) { foreach (int n in nums) { int x = n; } }");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Scope_ForeachVar_NotVisibleOutside()
    {
        var r = BindNoThrow("void foo(int[] nums) { foreach (int n in nums) {} int x = n; }");
        Assert.True(r.HasErrors);
    }
    
    [Fact]
    public void New_KnownType_NoError()
    {
        var r = Bind("class Foo {} Foo f = new Foo();");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void New_UnknownType_Error()
    {
        var r = BindNoThrow("Foo f = new Foo();");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("'Foo'"));
    }
    
    [Fact]
    public void Super_InsideClass_NoError()
    {
        var r = Bind("class Animal { void speak() {} } class Dog : Animal { override void speak() { super.speak(); } }");
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Super_OutsideClass_Error()
    {
        var r = BindNoThrow("super.foo();");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("super"));
    }
    
    [Fact]
    public void Import_WithAlias_ModuleSymbolDeclared()
    {
        var r = Bind("import \"math\" as m;");
        AssertNoErrors(r);
        Assert.Contains(r.Symbols.All, s => s.Name == "m" && s.Kind == SymbolKind.Module);
    }
 
    [Fact]
    public void Import_NoAlias_NoError()
    {
        var r = Bind("import \"math\";");
        AssertNoErrors(r);
    }
    
    [Fact]
    public void SymbolTable_MembersOf_ReturnsOnlyMembers()
    {
        var r = Bind("class Point { int x; int y; } int z;");
        AssertNoErrors(r);
        var members = r.Symbols.MembersOf("Point").ToList();
        Assert.Equal(2, members.Count);
        Assert.DoesNotContain(members, s => s.Name == "z");
    }
 
    [Fact]
    public void SymbolTable_Lookup_FindsAllWithName()
    {
        var r = Bind("class A { int x; } void foo() { int x = 1; }");
        AssertNoErrors(r);
        Assert.Equal(2, r.Symbols.Lookup("x").Count());
    }
    
    [Fact]
    public void AddGlobalSymbols_InjectedBeforeBind_Resolved()
    {
        var lexResult   = new Lexer("externalFunc();").Tokenize();
        var parseResult = new Parser(lexResult).Parse();
 
        var binder = new Binder();
        binder.AddGlobalSymbols([
            new Symbol("externalFunc", SymbolKind.Function, "void",
                new Span(default, default), parameters: "")
        ]);
        var r = binder.Bind(parseResult.Statements);
        AssertNoErrors(r);
    }
    
    [Fact]
    public void Complex_ClassHierarchy_NoError()
    {
        const string src = """
                           interface IAnimal { void speak(); }
                           class Animal : IAnimal { void speak() {} }
                           class Dog : Animal { override void speak() { super.speak(); } }
                           """;
        var r = Bind(src);
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Complex_GenericClass_NoError()
    {
        const string src = "class Box<T> { T value; T get() { return value; } }";
        var r = Bind(src);
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Complex_NestedLoops_NoError()
    {
        const string src = """
                           void matrix() {
                               for (int i = 0; i < 10; i = i + 1) {
                                   for (int j = 0; j < 10; j = j + 1) {
                                       int cell = i + j;
                                   }
                               }
                           }
                           """;
        var r = Bind(src);
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Complex_ForeachWithBuiltin_NoError()
    {
        const string src = """
                           void printAll(string[] items) {
                               foreach (string s in items) {
                                   print(s);
                               }
                           }
                           """;
        var r = Bind(src);
        AssertNoErrors(r);
    }
 
    [Fact]
    public void Complex_Switch_NoError()
    {
        const string src = """
                           void classify(int n) {
                               switch (n) {
                                   case 0: print("zero");
                                   case 1: print("one");
                                   default: print("other");
                               }
                           }
                           """;
        var r = Bind(src);
        AssertNoErrors(r);
    }
}