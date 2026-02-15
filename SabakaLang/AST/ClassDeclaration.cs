namespace SabakaLang.AST;

public class ClassDeclaration : Expr
{
    public string Name { get; }
    public List<VariableDeclaration> Fields { get; }
    public List<FunctionDeclaration> Methods { get; }

    public ClassDeclaration(
        string name,
        List<VariableDeclaration> fields,
        List<FunctionDeclaration> methods)
    {
        Name = name;
        Fields = fields;
        Methods = methods;
    }
}