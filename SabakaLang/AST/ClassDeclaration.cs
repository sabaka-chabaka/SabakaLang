namespace SabakaLang.AST;

public class ClassDeclaration : Expr
{
    public string Name { get; }
    public string? BaseClassName { get; }
    public List<string> Interfaces { get; }
    public List<VariableDeclaration> Fields { get; }
    public List<FunctionDeclaration> Methods { get; }

    public ClassDeclaration(
        string name,
        string? baseClassName,
        List<string> interfaces,
        List<VariableDeclaration> fields,
        List<FunctionDeclaration> methods)
    {
        Name = name;
        BaseClassName = baseClassName;
        Interfaces = interfaces;
        Fields = fields;
        Methods = methods;
    }
}