namespace SabakaLang.AST;

public class ClassDeclaration : Expr
{
    public string Name { get; }
    public List<string> TypeParams { get; }   // e.g. ["T"] or ["T", "U"]
    public string? BaseClassName { get; }
    public List<string> Interfaces { get; }
    public List<VariableDeclaration> Fields { get; }
    public List<FunctionDeclaration> Methods { get; }

    public bool IsGeneric => TypeParams.Count > 0;

    public ClassDeclaration(
        string name,
        List<string>? typeParams,
        string? baseClassName,
        List<string> interfaces,
        List<VariableDeclaration> fields,
        List<FunctionDeclaration> methods)
    {
        Name = name;
        TypeParams = typeParams ?? new List<string>();
        BaseClassName = baseClassName;
        Interfaces = interfaces;
        Fields = fields;
        Methods = methods;
    }
}