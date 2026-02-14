using SabakaLang.AST;
using SabakaLang.Exceptions;
using SabakaLang.Lexer;

namespace SabakaLang.Parser;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _position;
    
    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }
    
    private Token Current => _position >= _tokens.Count ? _tokens[^1] : _tokens[_position];

    private Token Consume()
    {
        var current = Current;
        _position++;
        return current;
    }
    
    private Token Expect(TokenType type)
    {
        if (Current.Type != type)
            throw new ParserException($"Expected {type}, got {Current.Type}", _position);

        return Consume();
    }

    public Expr Parse()
    {
        return ParseExpression();
    }
    
    private Expr ParseExpression()
    {
        var left = ParseTerm();

        while (Current.Type == TokenType.Plus ||
               Current.Type == TokenType.Minus)
        {
            var op = Consume();
            var right = ParseTerm();
            left = new BinaryExpr(left, op, right);
        }

        return left;
    }

    private Expr ParseTerm()
    {
        var left = ParseFactor();

        while (Current.Type == TokenType.Star ||
               Current.Type == TokenType.Slash)
        {
            var op = Consume();
            var right = ParseFactor();
            left = new BinaryExpr(left, op, right);
        }

        return left;
    }

    private Expr ParseFactor()
    {
        if (Current.Type == TokenType.Number)
        {
            var number = Consume();
            return new NumberExpr(double.Parse(number.Value));
        }

        if (Current.Type == TokenType.LParen)
        {
            Consume();
            var expr = ParseExpression();
            Expect(TokenType.RParen);
            return expr;
        }

        throw new ParserException($"Unexpected token {Current.Type}", _position);
    }
}