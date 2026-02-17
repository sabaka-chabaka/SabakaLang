namespace SabakaLang.AST;

public class InterfaceDeclaration : Expr
{
    public string Name { get; }
    public List<string> Parents { get; }
    public List<FunctionDeclaration> Methods { get; }

    public InterfaceDeclaration(string name, List<string> parents, List<FunctionDeclaration> methods)
    {
        Name = name;
        Parents = parents;
        Methods = methods;
    }
}
