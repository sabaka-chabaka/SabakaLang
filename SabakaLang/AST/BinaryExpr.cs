using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class BinaryExpr : Expr
{
    public Expr Left { get; }
    public TokenType Operator { get; }
    public Expr Right { get; }

    public BinaryExpr(Expr left, TokenType op, Expr right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
}
