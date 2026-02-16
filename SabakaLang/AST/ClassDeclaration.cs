namespace SabakaLang.AST;

public class ClassDeclaration : Expr
{
    public string Name { get; }
    public string? BaseClassName { get; }
    public List<VariableDeclaration> Fields { get; }
    public List<FunctionDeclaration> Methods { get; }

    public ClassDeclaration(
        string name,
        string? baseClassName,
        List<VariableDeclaration> fields,
        List<FunctionDeclaration> methods)
    {
        Name = name;
        BaseClassName = baseClassName;
        Fields = fields;
        Methods = methods;
    }
}