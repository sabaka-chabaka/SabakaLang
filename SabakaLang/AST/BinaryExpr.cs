using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class BinaryExpr : Expr
{
    public Expr Left { get; }
    public Token Operator { get; }
    public Expr Right { get; }
    
    public BinaryExpr(Expr left, Token op, Expr right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
}
