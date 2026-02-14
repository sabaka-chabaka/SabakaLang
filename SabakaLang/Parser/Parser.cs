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

    public List<Expr> ParseProgram()
    {
        var expressions = new List<Expr>();

        while (Current.Type != TokenType.EOF)
        {
            Expr expr;

            if (Current.Type == TokenType.If)
            {
                expr = ParseIf();
            }
            else if (Current.Type == TokenType.BoolKeyword)
            {
                expr = ParseVariableDeclaration();
            }
            else
            {
                expr = ParseAssignment();
            }

            expressions.Add(expr);

            if (Current.Type == TokenType.Semicolon)
            {
                Consume();
            }
        }

        return expressions;
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
        if (Current.Type == TokenType.True)
        {
            Consume();
            return new NumberExpr(1);
        }

        if (Current.Type == TokenType.False)
        {
            Consume();
            return new NumberExpr(0);
        }

        
        if (Current.Type == TokenType.Number)
        {
            var number = Consume();
            return new NumberExpr(double.Parse(number.Value));
        }

        if (Current.Type == TokenType.Identifier)
        {
            var identifier = Consume();

            if (Current.Type == TokenType.LParen)
            {
                Consume(); // (

                var argument = ParseExpression();

                Expect(TokenType.RParen);

                return new CallExpr(identifier.Value, argument);
            }

            // если нет скобок — это переменная
            return new VariableExpr(identifier.Value);
        }


        if (Current.Type == TokenType.LParen)
        {
            Consume();
            var expr = ParseExpression();
            Expect(TokenType.RParen);
            return expr;
        }

        throw new ParserException(
            $"Unexpected token {Current.Type}",
            _position
        );
    }

    private Expr ParseVariableDeclaration()
    {
        Consume(); // bool

        var nameToken = Expect(TokenType.Identifier);

        Expect(TokenType.Equal);

        var value = ParseExpression();

        return new VariableDeclaration(nameToken.Value, value);
    }
    
    private Expr ParseIf()
    {
        Consume(); // if

        Expect(TokenType.LParen);
        var condition = ParseExpression();
        Expect(TokenType.RParen);

        var thenBlock = ParseBlock();

        List<Expr>? elseBlock = null;

        if (Current.Type == TokenType.Else)
        {
            Consume();
            elseBlock = ParseBlock();
        }

        return new IfStatement(condition, thenBlock, elseBlock);
    }
    
    private List<Expr> ParseBlock()
    {
        Expect(TokenType.LBrace);

        var statements = new List<Expr>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EOF)
        {
            Expr stmt;

            if (Current.Type == TokenType.If)
                stmt = ParseIf();
            else if (Current.Type == TokenType.BoolKeyword)
                stmt = ParseVariableDeclaration();
            else
                stmt = ParseAssignment();

            statements.Add(stmt);

            if (Current.Type == TokenType.Semicolon)
                Consume();
        }

        Expect(TokenType.RBrace);

        return statements;
    }

    private Expr ParseAssignment()
    {
        if (Current.Type == TokenType.Identifier &&
            Peek().Type == TokenType.Equal)
        {
            var name = Consume().Value; // identifier
            Consume(); // =

            var value = ParseAssignment(); // важно! рекурсивно

            return new AssignmentExpr(name, value);
        }

        return ParseExpression();
    }

    private Token Peek(int offset = 1)
    {
        if (_position + offset >= _tokens.Count)
            return _tokens[^1];

        return _tokens[_position + offset];
    }
}