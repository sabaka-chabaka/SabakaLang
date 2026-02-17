using System.Globalization;
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

    private (TokenType type, string? customType) ConsumeTypeFull()
    {
        var token = Consume();
        var type = token.Type;
        string? customType = null;
        if (type == TokenType.Identifier)
            customType = token.Value;

        while (Current.Type == TokenType.LBracket)
        {
            Consume();
            Expect(TokenType.RBracket);
        }

        return (type, customType);
    }

    private TokenType ConsumeType()
    {
        return ConsumeTypeFull().type;
    }

    private bool IsTypeKeyword(TokenType type)
    {
        return type == TokenType.IntKeyword ||
               type == TokenType.FloatKeyword ||
               type == TokenType.BoolKeyword ||
               type == TokenType.StringKeyword ||
               type == TokenType.VoidKeyword;
    }

    private bool IsAccessModifier(TokenType type)
    {
        return type == TokenType.Public || type == TokenType.Private || type == TokenType.Protected;
    }

    private AccessModifier GetAccessModifier(TokenType type)
    {
        return type switch
        {
            TokenType.Public => AccessModifier.Public,
            TokenType.Private => AccessModifier.Private,
            TokenType.Protected => AccessModifier.Protected,
            _ => AccessModifier.Public
        };
    }

    private bool IsFunctionDeclaration()
    {
        int offset = 0;
        if (IsAccessModifier(Peek(offset).Type))
            offset++;

        if (Peek(offset).Type == TokenType.Override)
            offset++;

        if (!IsTypeKeyword(Peek(offset).Type) && Peek(offset).Type != TokenType.Identifier)
            return false;

        offset++;
        while (Peek(offset).Type == TokenType.LBracket)
        {
            if (Peek(offset + 1).Type != TokenType.RBracket)
                return false;
            offset += 2;
        }

        return Peek(offset).Type == TokenType.Identifier && Peek(offset + 1).Type == TokenType.LParen;
    }

    private bool IsVariableDeclaration()
    {
        int offset = 0;
        if (IsAccessModifier(Peek(offset).Type))
            offset++;

        if (IsTypeKeyword(Peek(offset).Type) && Peek(offset).Type != TokenType.VoidKeyword)
            return true;

        if (Peek(offset).Type == TokenType.Identifier)
        {
            offset++;
            while (Peek(offset).Type == TokenType.LBracket)
            {
                if (Peek(offset + 1).Type != TokenType.RBracket)
                    return false;
                offset += 2;
            }

            return Peek(offset).Type == TokenType.Identifier;
        }

        return false;
    }

    public List<Expr> ParseProgram()
    {
        var expressions = new List<Expr>();

        while (Current.Type != TokenType.EOF)
        {
            Expr expr;

            if (IsFunctionDeclaration())
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

    private Expr ParseVariableDeclaration(AccessModifier defaultAccess = AccessModifier.Public)
    {
        AccessModifier access = defaultAccess;
        if (IsAccessModifier(Current.Type))
        {
            access = GetAccessModifier(Consume().Type);
        }

        var typeToken = Consume();

        while (Current.Type == TokenType.LBracket)
        {
            Consume();
            Expect(TokenType.RBracket);
        }

        string? customType = null;
        TokenType type = typeToken.Type;

        // если это не builtin тип — значит struct
        if (typeToken.Type == TokenType.Identifier)
        {
            customType = typeToken.Value;
        }

        var nameToken = Expect(TokenType.Identifier);

        Expr? initializer = null;

        if (Current.Type == TokenType.Equal)
        {
            Consume();
            initializer = ParseAssignment();
        }

        return new VariableDeclaration(type, customType, nameToken.Value, initializer, access);
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
        else if (Current.Type == TokenType.Switch)
            stmt = ParseSwitch();
        else if (IsVariableDeclaration())
        {
            stmt = ParseVariableDeclaration();
            if (Current.Type == TokenType.Semicolon)
                Consume();
            return stmt;
        }
        else if (Current.Type == TokenType.While)
        {
            stmt = ParseWhile();
        }
        else if (Current.Type == TokenType.Return)
        {
            stmt = ParseReturn();
        }
        else if (Current.Type == TokenType.Foreach)
        {
            stmt = ParseForeach();
        }
        else if (Current.Type == TokenType.For)
            stmt = ParseFor();
        else if (Current.Type == TokenType.StructKeyword)
            return ParseStruct();
        else if (Current.Type == TokenType.Class)
        {
            stmt = ParseClass();
        }

        else if (Current.Type == TokenType.Interface)
        {
            stmt = ParseInterface();
        }

        else if (Current.Type == TokenType.Enum)
        {
            stmt = ParseEnum();
        }

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

    private Expr ParseSwitch()
    {
        Consume(); // switch

        Expect(TokenType.LParen);
        var expression = ParseAssignment();
        Expect(TokenType.RParen);

        Expect(TokenType.LBrace);
        var cases = new List<SwitchCase>();
        bool hasDefault = false;

        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF)
        {
            if (Current.Type == TokenType.Case)
            {
                Consume(); // case
                var value = ParseAssignment();
                Expect(TokenType.Colon);
                var body = ParseBlockOrStatement();
                cases.Add(new SwitchCase(value, body));
            }
            else if (Current.Type == TokenType.Default)
            {
                if (hasDefault)
                    throw new ParserException("Switch statement already has a default case", _position);
                
                Consume(); // default
                hasDefault = true;
                Expect(TokenType.Colon);
                var body = ParseBlockOrStatement();
                cases.Add(new SwitchCase(null, body));
            }
            else
            {
                throw new ParserException($"Expected 'case' or 'default', got {Current.Type}", _position);
            }
        }

        Expect(TokenType.RBrace);

        return new SwitchStatement(expression, cases);
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
        if (Current.Type == TokenType.Super)
        {
            if (Peek().Type == TokenType.ColonColon && Peek(2).Type == TokenType.Identifier && Peek(3).Type == TokenType.Equal)
            {
                Consume(); // super
                Consume(); // ::
                var member = Consume().Value;
                Consume(); // =
                var value = ParseAssignment();
                return new MemberAssignmentExpr(new SuperExpr(), member, value);
            }
        }

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

            if (Peek().Type == TokenType.Dot)
            {
                // Look ahead to see if this is an assignment
                int offset = 1;
                while (Peek(offset).Type == TokenType.Dot && Peek(offset + 1).Type == TokenType.Identifier)
                {
                    offset += 2;
                }

                if (Peek(offset).Type == TokenType.Equal)
                {
                    // Member assignment
                    var obj = new VariableExpr(Consume().Value) as Expr;
                    while (Peek().Type == TokenType.Dot && Peek(2).Type == TokenType.Identifier &&
                           Peek(3).Type != TokenType.Equal)
                    {
                        Consume(); // .
                        obj = new MemberAccessExpr(obj, Consume().Value);
                    }

                    Consume(); // the last .
                    var member = Expect(TokenType.Identifier).Value;
                    Expect(TokenType.Equal);
                    var value = ParseAssignment();
                    return new MemberAssignmentExpr(obj, member, value);
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
    Expr expr;

    // ===== LITERALS =====

    if (Current.Type == TokenType.New)
    {
        Consume();
        var name = Expect(TokenType.Identifier).Value;

        Expect(TokenType.LParen);
        var arguments = new List<Expr>();
        if (Current.Type != TokenType.RParen)
        {
            do
            {
                arguments.Add(ParseAssignment());
                if (Current.Type != TokenType.Comma) break;
                Consume();
            } while (true);
        }
        Expect(TokenType.RParen);

        return new NewExpr(name, arguments);
    }

    if (Current.Type == TokenType.IntLiteral)
    {
        expr = new IntExpr(int.Parse(Current.Value, CultureInfo.InvariantCulture));
        Consume();
    }
    else if (Current.Type == TokenType.FloatLiteral)
    {
        expr = new FloatExpr(double.Parse(Current.Value, CultureInfo.InvariantCulture));
        Consume();
    }
    else if (Current.Type == TokenType.StringLiteral)
    {
        expr = new StringExpr(Current.Value);
        Consume();
    }
    else if (Current.Type == TokenType.True)
    {
        expr = new BoolExpr(true);
        Consume();
    }
    else if (Current.Type == TokenType.False)
    {
        expr = new BoolExpr(false);
        Consume();
    }
    else if (Current.Type == TokenType.Identifier)
    {
        expr = new VariableExpr(Current.Value);
        Consume();
    }
    else if (Current.Type == TokenType.Super)
    {
        Consume();
        Expect(TokenType.ColonColon);
        var member = Expect(TokenType.Identifier).Value;
        expr = new MemberAccessExpr(new SuperExpr(), member);
    }
    
    else if (Current.Type == TokenType.LBracket)
    {
        Consume();
        var elements = new List<Expr>();
        if (Current.Type != TokenType.RBracket)
        {
            do
            {
                elements.Add(ParseAssignment());
                if (Current.Type != TokenType.Comma) break;
                Consume();
            } while (true);
        }
        Expect(TokenType.RBracket);
        expr = new ArrayExpr(elements);
    }
    else if (Current.Type == TokenType.LParen)
    {
        Consume();
        expr = ParseAssignment();
        Expect(TokenType.RParen);
    }
    else
    {
        throw new ParserException(
            $"Unexpected token {Current.Type}",
            _position
        );
    }

    // ===== POSTFIX LOOP =====

    while (true)
    {
        // function call
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

            if (expr is VariableExpr varExpr)
                expr = new CallExpr(varExpr.Name, arguments);
            else if (expr is MemberAccessExpr memberAccess)
                expr = new CallExpr(memberAccess.Member, arguments, memberAccess.Object);
            else
                throw new ParserException("Invalid function call target", _position);

            continue;
        }

        // dot access (struct or enum)
        if (Current.Type == TokenType.Dot)
        {
            Consume();
            var member = Expect(TokenType.Identifier);

            expr = new MemberAccessExpr(expr, member.Value);
            continue;
        }

        // array access
        if (Current.Type == TokenType.LBracket)
        {
            Consume();
            var index = ParseAssignment();
            Expect(TokenType.RBracket);

            expr = new ArrayAccessExpr(expr, index);
            continue;
        }

        break;
    }

    return expr;
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

    private Expr ParseFunction(AccessModifier defaultAccess = AccessModifier.Public)
    {
        AccessModifier access = defaultAccess;
        if (IsAccessModifier(Current.Type))
        {
            access = GetAccessModifier(Consume().Type);
        }

        bool isOverride = false;
        if (Current.Type == TokenType.Override)
        {
            Consume();
            isOverride = true;
        }

        var returnType = ConsumeType();
        var name = Expect(TokenType.Identifier).Value;

        Expect(TokenType.LParen);
        var parameters = ParseParameters();
        Expect(TokenType.RParen);

        var body = ParseBlock();

        return new FunctionDeclaration(returnType, name, parameters, body, isOverride, access);
    }

    private List<Parameter> ParseParameters()
    {
        var parameters = new List<Parameter>();

        if (Current.Type != TokenType.RParen)
        {
            do
            {
                // тип параметра
                var (paramType, customType) = ConsumeTypeFull();

                // имя параметра
                var paramName = Expect(TokenType.Identifier).Value;

                parameters.Add(new Parameter(paramType, paramName, customType));

                if (Current.Type != TokenType.Comma)
                    break;

                Consume();
            } while (true);
        }

        return parameters;
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

    private Expr ParseForeach()
    {
        Consume(); // foreach

        Expect(TokenType.LParen);

        // пропускаем тип
        ConsumeType();

        var varName = Expect(TokenType.Identifier).Value;

        Expect(TokenType.In);

        var collection = ParseAssignment();

        Expect(TokenType.RParen);

        var body = ParseBlock();

        return new ForeachStatement(varName, collection, body);
    }

    private Expr ParseStruct()
    {
        Consume(); // struct

        var name = Expect(TokenType.Identifier).Value;

        Expect(TokenType.LBrace);

        var fields = new List<string>();

        while (Current.Type != TokenType.RBrace)
        {
            ConsumeType(); // тип
            var fieldName = Expect(TokenType.Identifier).Value;
            Expect(TokenType.Semicolon);

            fields.Add(fieldName);
        }

        Expect(TokenType.RBrace);

        return new StructDeclaration(name, fields);
    }

    private Expr ParseEnum()
    {
        Consume(); // enum

        var name = Expect(TokenType.Identifier).Value;

        Expect(TokenType.LBrace);

        var members = new List<string>();

        while (Current.Type != TokenType.RBrace)
        {
            var member = Expect(TokenType.Identifier);
            members.Add(member.Value);

            if (Current.Type == TokenType.Comma)
                Consume();
        }

        Expect(TokenType.RBrace);

        return new EnumDeclaration(name, members);
    }
    
    private Expr ParseClass()
    {
        Consume(); // class

        var name = Expect(TokenType.Identifier).Value;

        string? baseClassName = null;
        var interfaces = new List<string>();

        if (Current.Type == TokenType.Colon)
        {
            Consume();
            var first = Expect(TokenType.Identifier).Value;
            // For now, let's assume the first one is a base class. 
            // In a better compiler we would check if 'first' is a class or an interface.
            baseClassName = first;

            while (Current.Type == TokenType.Comma)
            {
                Consume();
                interfaces.Add(Expect(TokenType.Identifier).Value);
            }
        }

        Expect(TokenType.LBrace);

        var fields = new List<VariableDeclaration>();
        var methods = new List<FunctionDeclaration>();

        while (Current.Type != TokenType.RBrace)
        {
            if (IsFunctionDeclaration())
            {
                methods.Add((FunctionDeclaration)ParseFunction());
            }
            else
            {
                var field = (VariableDeclaration)ParseVariableDeclaration();
                Expect(TokenType.Semicolon);
                fields.Add(field);
            }
        }

        Expect(TokenType.RBrace);

        return new ClassDeclaration(name, baseClassName, interfaces, fields, methods);
    }

    private Expr ParseInterface()
    {
        Consume(); // interface

        var name = Expect(TokenType.Identifier).Value;

        var parents = new List<string>();
        if (Current.Type == TokenType.Colon)
        {
            Consume();
            do
            {
                parents.Add(Expect(TokenType.Identifier).Value);
                if (Current.Type != TokenType.Comma) break;
                Consume();
            } while (true);
        }

        Expect(TokenType.LBrace);

        var methods = new List<FunctionDeclaration>();

        while (Current.Type != TokenType.RBrace)
        {
            // Interface methods don't have a body
            var returnType = ConsumeType();
            var methodName = Expect(TokenType.Identifier).Value;

            Expect(TokenType.LParen);
            var parameters = ParseParameters();
            Expect(TokenType.RParen);
            Expect(TokenType.Semicolon);

            methods.Add(new FunctionDeclaration(returnType, methodName, parameters, new List<Expr>()));
        }

        Expect(TokenType.RBrace);

        return new InterfaceDeclaration(name, parents, methods);
    }

}