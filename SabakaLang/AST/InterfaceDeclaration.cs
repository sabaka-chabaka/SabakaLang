namespace SabakaLang.AST;

public class InterfaceDeclaration : Expr
{
    public string Name { get; }
    public List<string> TypeParams { get; }
    public List<string> Parents { get; }
    public List<FunctionDeclaration> Methods { get; }

    public bool IsGeneric => TypeParams.Count > 0;

    public InterfaceDeclaration(string name, List<string>? typeParams, List<string> parents, List<FunctionDeclaration> methods)
    {
        Name = name;
        TypeParams = typeParams ?? new List<string>();
        Parents = parents;
        Methods = methods;
    }
}