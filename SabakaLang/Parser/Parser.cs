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

            if ((Current.Type == TokenType.IntKeyword ||
                 Current.Type == TokenType.FloatKeyword ||
                 Current.Type == TokenType.BoolKeyword ||
                 Current.Type == TokenType.StringKeyword ||
                 Current.Type == TokenType.VoidKeyword) &&
                Peek().Type == TokenType.Identifier &&
                Peek(2).Type == TokenType.LParen)
            {
                expr = ParseFunction();
            }
            else
            {
                expr = ParseStatement();
            }

            expressions.Add(expr);
        }

        return expressions;
    }

    private Expr ParseExpression()
    {
        var left = ParseTerm();

        while (Current.Type == TokenType.Plus ||
               Current.Type == TokenType.Minus)
        {
            var op = Consume().Type;
            var right = ParseTerm();
            left = new BinaryExpr(left, op, right);
        }

        return left;
    }

    private Expr ParseTerm()
    {
        var left = ParsePrimary();

        while (Current.Type == TokenType.Star ||
               Current.Type == TokenType.Slash)
        {
            var op = Consume().Type;
            var right = ParsePrimary();
            left = new BinaryExpr(left, op, right);
        }

        return left;
    }

    private Expr ParseVariableDeclaration()
    {
        var typeToken = Consume(); // int / float / bool / string

        if (Current.Type == TokenType.LBracket)
        {
            Consume();
            Expect(TokenType.RBracket);
        }

        var nameToken = Expect(TokenType.Identifier);

        Expect(TokenType.Equal);

        var value = ParseAssignment();

        return new VariableDeclaration(typeToken.Type, nameToken.Value, value);
    }


    private List<Expr> ParseBlockOrStatement()
    {
        if (Current.Type == TokenType.LBrace)
            return ParseBlock();

        var stmt = ParseStatement();
        return new List<Expr> { stmt };
    }

    private Expr ParseStatement()
    {
        Expr stmt;
        if (Current.Type == TokenType.If)
            stmt = ParseIf();
        else if (Current.Type == TokenType.BoolKeyword ||
                 Current.Type == TokenType.IntKeyword ||
                 Current.Type == TokenType.FloatKeyword ||
                 Current.Type == TokenType.StringKeyword)
        {
            stmt = ParseVariableDeclaration();
        }
        else if (Current.Type == TokenType.While)
        {
            stmt = ParseWhile();
        }
        else if (Current.Type == TokenType.Return)
        {
            stmt = ParseReturn();
        }
        else if (Current.Type == TokenType.For)
            stmt = ParseFor();

        else
            stmt = ParseAssignment();
        

        if (Current.Type == TokenType.Semicolon)
            Consume();

        return stmt;
    }

    private Expr ParseIf()
    {
        Consume(); // if

        Expect(TokenType.LParen);
        var condition = ParseAssignment();

        Expect(TokenType.RParen);

        var thenBlock = ParseBlockOrStatement();

        List<Expr>? elseBlock = null;

        if (Current.Type == TokenType.Else)
        {
            Consume();
            elseBlock = ParseBlockOrStatement();
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
            statements.Add(ParseStatement());
        }

        Expect(TokenType.RBrace);

        return statements;
    }

    private Expr ParseAssignment()
    {
        if (Current.Type == TokenType.Identifier)
        {
            if (Peek().Type == TokenType.Equal)
            {
                var name = Consume().Value;
                Consume(); // =

                var value = ParseAssignment();

                return new AssignmentExpr(name, value);
            }
            
            if (Peek().Type == TokenType.LBracket)
            {
                // Potential array assignment: identifier[index] = value
                // Need to check if it's followed by ] and =
                int offset = 2;
                int depth = 1;
                while (_position + offset < _tokens.Count && depth > 0)
                {
                    if (_tokens[_position + offset].Type == TokenType.LBracket) depth++;
                    else if (_tokens[_position + offset].Type == TokenType.RBracket) depth--;
                    offset++;
                }

                if (_position + offset < _tokens.Count && _tokens[_position + offset].Type == TokenType.Equal)
                {
                    var identifier = Consume();
                    Consume(); // [
                    var index = ParseAssignment();
                    Expect(TokenType.RBracket);
                    Expect(TokenType.Equal);
                    var value = ParseAssignment();
                    return new ArrayStoreExpr(new VariableExpr(identifier.Value), index, value);
                }
            }
        }

        return ParseLogicalOr();
    }


    private Token Peek(int offset = 1)
    {
        if (_position + offset >= _tokens.Count)
            return _tokens[^1];

        return _tokens[_position + offset];
    }

    private Expr ParseComparison()
    {
        var left = ParseAdditive();

        while (Current.Type == TokenType.EqualEqual ||
               Current.Type == TokenType.NotEqual ||
               Current.Type == TokenType.Greater ||
               Current.Type == TokenType.Less ||
               Current.Type == TokenType.GreaterEqual ||
               Current.Type == TokenType.LessEqual)
        {
            var op = Consume().Type; // ← вот тут Consume правильно
            var right = ParseAdditive();
            left = new BinaryExpr(left, op, right);
        }

        return left;
    }


    private Expr ParseAdditive()
    {
        var expr = ParseMultiplicative();

        while (Current.Type == TokenType.Plus ||
               Current.Type == TokenType.Minus)
        {
            var op = Consume().Type;
            var right = ParseMultiplicative();

            expr = new BinaryExpr(expr, op, right);
        }

        return expr;
    }

    private Expr ParseMultiplicative()
    {
        var expr = ParseUnary();

        while (Current.Type == TokenType.Star ||
               Current.Type == TokenType.Slash)
        {
            var op = Consume().Type;
            var right = ParseUnary();

            expr = new BinaryExpr(expr, op, right);
        }

        return expr;
    }

    private Expr ParseUnary()
    {
        if (Current.Type == TokenType.Minus)
        {
            var op = Consume().Type;
            var right = ParseUnary();
            return new UnaryExpr(op, right);
        }

        if (Current.Type == TokenType.Bang)
        {
            var op = Consume().Type;
            var right = ParseUnary();
            return new UnaryExpr(op, right);
        }

        return ParsePrimary();
    }



    private Expr ParsePrimary()
    {
        if (Current.Type == TokenType.IntLiteral)
        {
            var value = int.Parse(Current.Value);
            Consume();
            return new IntExpr(value);
        }

        if (Current.Type == TokenType.FloatLiteral)
        {
            var value = double.Parse(Current.Value);
            Consume();
            return new FloatExpr(value);
        }
        
        if (Current.Type == TokenType.StringLiteral)
        {
            var value = Current.Value;
            Consume();
            return new StringExpr(value);
        }


        if (Current.Type == TokenType.True)
        {
            Consume();
            return new BoolExpr(true);
        }

        if (Current.Type == TokenType.False)
        {
            Consume();
            return new BoolExpr(false);
        }

        if (Current.Type == TokenType.Identifier)
        {
            var identifier = Consume();

            if (Current.Type == TokenType.LParen)
            {
                Consume();

                var arguments = new List<Expr>();

                if (Current.Type != TokenType.RParen)
                {
                    do
                    {
                        arguments.Add(ParseAssignment());

                        if (Current.Type != TokenType.Comma)
                            break;

                        Consume();
                    }
                    while (true);
                }

                Expect(TokenType.RParen);

                return new CallExpr(identifier.Value, arguments);
            }

            if (Current.Type == TokenType.LBracket)
            {
                Consume();
                var index = ParseAssignment();
                Expect(TokenType.RBracket);

                return new ArrayAccessExpr(new VariableExpr(identifier.Value), index);
            }

            
            return new VariableExpr(identifier.Value);
        }

        if (Current.Type == TokenType.LParen)
        {
            Consume();
            var expr = ParseAssignment();
            Expect(TokenType.RParen);
            return expr;
        }

        if (Current.Type == TokenType.LBracket)
        {
            Consume();

            var elements = new List<Expr>();

            if (Current.Type != TokenType.RBracket)
            {
                do
                {
                    elements.Add(ParseAssignment());

                    if (Current.Type != TokenType.Comma)
                        break;

                    Consume();
                }
                while (true);
            }

            Expect(TokenType.RBracket);

            return new ArrayExpr(elements);
        }



        throw new ParserException(
            $"Unexpected token {Current.Type}",
            _position
        );
    }


    private Expr ParseWhile()
    {
        Consume(); // while

        Expect(TokenType.LParen);
        var condition = ParseAssignment();
        Expect(TokenType.RParen);

        var body = ParseBlock(); // у тебя уже есть ParseBlock()

        return new WhileExpr(condition, body);
    }
    
    private Expr ParseLogicalOr()
    {
        var left = ParseLogicalAnd();

        while (Current.Type == TokenType.OrOr)
        {
            var op = Consume().Type;
            var right = ParseLogicalAnd();
            left = new BinaryExpr(left, op, right);
        }

        return left;
    }

    private Expr ParseLogicalAnd()
    {
        var left = ParseComparison();

        while (Current.Type == TokenType.AndAnd)
        {
            var op = Consume().Type;
            var right = ParseComparison();
            left = new BinaryExpr(left, op, right);
        }

        return left;
    }

    private Expr ParseFunction()
    {
        var returnType = Consume().Type;
        var name = Expect(TokenType.Identifier).Value;

        Expect(TokenType.LParen);

        var parameters = new List<Parameter>();

        if (Current.Type != TokenType.RParen)
        {
            do
            {
                // тип параметра
                var paramType = Consume().Type;

                // имя параметра
                var paramName = Expect(TokenType.Identifier).Value;

                parameters.Add(new Parameter(paramType, paramName));

                if (Current.Type != TokenType.Comma)
                    break;

                Consume();
            }
            while (true);
        }

        Expect(TokenType.RParen);

        var body = ParseBlock();

        return new FunctionDeclaration(returnType, name, parameters, body);
    }


    private Expr ParseReturn()
    {
        Consume(); // return

        if (Current.Type == TokenType.Semicolon)
            return new ReturnStatement(null);

        var value = ParseAssignment();
        return new ReturnStatement(value);
    }

    private Expr ParseFor()
    {
        Consume(); // for

        Expect(TokenType.LParen);

        // init
        Expr? initializer = null;
        if (Current.Type != TokenType.Semicolon)
            initializer = ParseStatement();
        else
            Consume();

        // condition
        Expr? condition = null;
        if (Current.Type != TokenType.Semicolon)
            condition = ParseAssignment();

        Expect(TokenType.Semicolon);

        // increment
        Expr? increment = null;
        if (Current.Type != TokenType.RParen)
            increment = ParseAssignment();

        Expect(TokenType.RParen);

        var body = ParseBlockOrStatement();

        return new ForStatement(initializer, condition, increment, body);
    }

}