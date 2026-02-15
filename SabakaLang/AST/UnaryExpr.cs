using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class UnaryExpr : Expr
{
    public TokenType Operator { get; }
    public Expr Operand { get; }

    public UnaryExpr(TokenType op, Expr operand)
    {
        Operator = op;
        Operand = operand;
    }
}
