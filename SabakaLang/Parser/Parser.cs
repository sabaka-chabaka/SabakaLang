using System.Globalization;
using System.Linq;
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
        _tokens = tokens.Where(t => t.Type != TokenType.Comment).ToList();
    }

    private Token Current => _position >= _tokens.Count ? _tokens[^1] : _tokens[_position];

    private Token Consume()
    {
        var current = Current;
        _position++;
        return current;
    }

    private T WithPos<T>(T expr, Token start, Token end) where T : Expr
    {
        expr.Start = start.TokenStart;
        expr.End = end.TokenEnd;
        return expr;
    }

    private T WithPos<T>(T expr, int start, int end) where T : Expr
    {
        expr.Start = start;
        expr.End = end;
        return expr;
    }

    private T WithPos<T>(T expr, Token token) where T : Expr
    {
        expr.Start = token.TokenStart;
        expr.End = token.TokenEnd;
        return expr;
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
        var startToken = Current;
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

        return WithPos(new VariableDeclaration(type, customType, nameToken.Value, initializer, access), startToken, _tokens[_position - 1]);
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
        else if (Current.Type == TokenType.Import)
            stmt = ParseImport();
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
        var startToken = Consume(); // if

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

        return WithPos(new IfStatement(condition, thenBlock, elseBlock), startToken, _tokens[_position - 1]);
    }

    private Expr ParseSwitch()
    {
        var startToken = Consume(); // switch

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

        return WithPos(new SwitchStatement(expression, cases), startToken, _tokens[_position - 1]);
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
        var startToken = Current;
        if (Current.Type == TokenType.Super)
        {
            if (Peek().Type == TokenType.ColonColon && Peek(2).Type == TokenType.Identifier && Peek(3).Type == TokenType.Equal)
            {
                var sToken = Consume(); // super
                Consume(); // ::
                var member = Consume().Value;
                Consume(); // =
                var value = ParseAssignment();
                return WithPos(new MemberAssignmentExpr(WithPos(new SuperExpr(), sToken), member, value), startToken, _tokens[_position - 1]);
            }
        }

        if (Current.Type == TokenType.Identifier)
        {
            if (Peek().Type == TokenType.Equal)
            {
                var name = Consume().Value;
                Consume(); // =

                var value = ParseAssignment();

                return WithPos(new AssignmentExpr(name, value), startToken, _tokens[_position - 1]);
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
                    return WithPos(new ArrayStoreExpr(WithPos(new VariableExpr(identifier.Value), identifier), index, value), startToken, _tokens[_position - 1]);
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
                    var firstToken = Current;
                    var obj = WithPos(new VariableExpr(Consume().Value), firstToken) as Expr;
                    while (Peek().Type == TokenType.Dot && Peek(2).Type == TokenType.Identifier &&
                           Peek(3).Type != TokenType.Equal)
                    {
                        Consume(); // .
                        var memberToken = Expect(TokenType.Identifier);
                        obj = WithPos(new MemberAccessExpr(obj, memberToken.Value), firstToken, memberToken);
                    }

                    Consume(); // the last .
                    var member = Expect(TokenType.Identifier);
                    Expect(TokenType.Equal);
                    var value = ParseAssignment();
                    return WithPos(new MemberAssignmentExpr(obj, member.Value, value), startToken, _tokens[_position - 1]);
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
            var opToken = Consume();
            var right = ParseAdditive();
            left = WithPos(new BinaryExpr(left, opToken.Type, right), left.Start, right.End);
        }

        return left;
    }


    private Expr ParseAdditive()
    {
        var expr = ParseMultiplicative();

        while (Current.Type == TokenType.Plus ||
               Current.Type == TokenType.Minus)
        {
            var opToken = Consume();
            var right = ParseMultiplicative();

            expr = WithPos(new BinaryExpr(expr, opToken.Type, right), expr.Start, right.End);
        }

        return expr;
    }

    private Expr ParseMultiplicative()
    {
        var expr = ParseUnary();

        while (Current.Type == TokenType.Star ||
               Current.Type == TokenType.Slash)
        {
            var opToken = Consume();
            var right = ParseUnary();

            expr = WithPos(new BinaryExpr(expr, opToken.Type, right), expr.Start, right.End);
        }

        return expr;
    }

    private Expr ParseUnary()
    {
        var startToken = Current;
        if (Current.Type == TokenType.Minus)
        {
            var op = Consume().Type;
            var right = ParseUnary();
            return WithPos(new UnaryExpr(op, right), startToken, _tokens[_position - 1]);
        }

        if (Current.Type == TokenType.Bang)
        {
            var op = Consume().Type;
            var right = ParseUnary();
            return WithPos(new UnaryExpr(op, right), startToken, _tokens[_position - 1]);
        }

        return ParsePrimary();
    }


    private Expr ParsePrimary()
{
    Expr expr;
    var startToken = Current;

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

        expr = WithPos(new NewExpr(name, arguments), startToken, _tokens[_position - 1]);
    }
    else if (Current.Type == TokenType.IntLiteral)
    {
        expr = WithPos(new IntExpr(int.Parse(Current.Value, CultureInfo.InvariantCulture)), Current);
        Consume();
    }
    else if (Current.Type == TokenType.FloatLiteral)
    {
        expr = WithPos(new FloatExpr(double.Parse(Current.Value, CultureInfo.InvariantCulture)), Current);
        Consume();
    }
    else if (Current.Type == TokenType.StringLiteral)
    {
        expr = WithPos(new StringExpr(Current.Value), Current);
        Consume();
    }
    else if (Current.Type == TokenType.True)
    {
        expr = WithPos(new BoolExpr(true), Current);
        Consume();
    }
    else if (Current.Type == TokenType.False)
    {
        expr = WithPos(new BoolExpr(false), Current);
        Consume();
    }
    else if (Current.Type == TokenType.Identifier)
    {
        expr = WithPos(new VariableExpr(Current.Value), Current);
        Consume();
    }
    else if (Current.Type == TokenType.Super)
    {
        var sToken = Consume();
        Expect(TokenType.ColonColon);
        var memberToken = Expect(TokenType.Identifier);
        expr = WithPos(new MemberAccessExpr(WithPos(new SuperExpr(), sToken), memberToken.Value), sToken, memberToken);
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
        expr = WithPos(new ArrayExpr(elements), startToken, _tokens[_position - 1]);
    }
    else if (Current.Type == TokenType.LParen)
    {
        Consume();
        expr = ParseAssignment();
        Expect(TokenType.RParen);
        expr = WithPos(expr, startToken.TokenStart, _tokens[_position - 1].TokenEnd);
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
                expr = WithPos(new CallExpr(varExpr.Name, arguments), expr.Start, _tokens[_position - 1].TokenEnd);
            else if (expr is MemberAccessExpr memberAccess)
                expr = WithPos(new CallExpr(memberAccess.Member, arguments, memberAccess.Object), expr.Start, _tokens[_position - 1].TokenEnd);
            else
                throw new ParserException("Invalid function call target", _position);

            continue;
        }

        // dot access (struct or enum)
        if (Current.Type == TokenType.Dot)
        {
            Consume();
            var member = Expect(TokenType.Identifier);

            expr = WithPos(new MemberAccessExpr(expr, member.Value), expr.Start, member.TokenEnd);
            continue;
        }

        // array access
        if (Current.Type == TokenType.LBracket)
        {
            Consume();
            var index = ParseAssignment();
            Expect(TokenType.RBracket);

            expr = WithPos(new ArrayAccessExpr(expr, index), expr.Start, _tokens[_position - 1].TokenEnd);
            continue;
        }

        break;
    }

    return expr;
}



    private Expr ParseWhile()
    {
        var startToken = Consume(); // while

        Expect(TokenType.LParen);
        var condition = ParseAssignment();
        Expect(TokenType.RParen);

        var body = ParseBlock(); // у тебя уже есть ParseBlock()

        return WithPos(new WhileExpr(condition, body), startToken, _tokens[_position - 1]);
    }

    private Expr ParseLogicalOr()
    {
        var left = ParseLogicalAnd();

        while (Current.Type == TokenType.OrOr)
        {
            var opToken = Consume();
            var right = ParseLogicalAnd();
            left = WithPos(new BinaryExpr(left, opToken.Type, right), left.Start, right.End);
        }

        return left;
    }

    private Expr ParseLogicalAnd()
    {
        var left = ParseComparison();

        while (Current.Type == TokenType.AndAnd)
        {
            var opToken = Consume();
            var right = ParseComparison();
            left = WithPos(new BinaryExpr(left, opToken.Type, right), left.Start, right.End);
        }

        return left;
    }

    private Expr ParseFunction(AccessModifier defaultAccess = AccessModifier.Public)
    {
        var startToken = Current;
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

        return WithPos(new FunctionDeclaration(returnType, name, parameters, body, isOverride, access), startToken, _tokens[_position - 1]);
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
        var startToken = Consume(); // return

        if (Current.Type == TokenType.Semicolon)
            return WithPos(new ReturnStatement(null), startToken);

        var value = ParseAssignment();
        return WithPos(new ReturnStatement(value), startToken, _tokens[_position - 1]);
    }

    private Expr ParseFor()
    {
        var startToken = Consume(); // for

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

        return WithPos(new ForStatement(initializer, condition, increment, body), startToken, _tokens[_position - 1]);
    }

    private Expr ParseForeach()
    {
        var startToken = Consume(); // foreach

        Expect(TokenType.LParen);

        // пропускаем тип
        ConsumeType();

        var varName = Expect(TokenType.Identifier).Value;

        Expect(TokenType.In);

        var collection = ParseAssignment();

        Expect(TokenType.RParen);

        var body = ParseBlock();

        return WithPos(new ForeachStatement(varName, collection, body), startToken, _tokens[_position - 1]);
    }

    private Expr ParseStruct()
    {
        var startToken = Consume(); // struct

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

        return WithPos(new StructDeclaration(name, fields), startToken, _tokens[_position - 1]);
    }

    private Expr ParseEnum()
    {
        var startToken = Consume(); // enum

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

        return WithPos(new EnumDeclaration(name, members), startToken, _tokens[_position - 1]);
    }
    
    private Expr ParseClass()
    {
        var startToken = Consume(); // class

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

        return WithPos(new ClassDeclaration(name, baseClassName, interfaces, fields, methods), startToken, _tokens[_position - 1]);
    }

    private Expr ParseInterface()
    {
        var startToken = Consume(); // interface

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
            var methodStartToken = Current;
            // Interface methods don't have a body
            var returnType = ConsumeType();
            var methodName = Expect(TokenType.Identifier).Value;

            Expect(TokenType.LParen);
            var parameters = ParseParameters();
            Expect(TokenType.RParen);
            Expect(TokenType.Semicolon);

            methods.Add(WithPos(new FunctionDeclaration(returnType, methodName, parameters, new List<Expr>()), methodStartToken, _tokens[_position - 1]));
        }

        Expect(TokenType.RBrace);

        return WithPos(new InterfaceDeclaration(name, parents, methods), startToken, _tokens[_position - 1]);
    }

    private Expr ParseImport()
    {
        var startToken = Consume();

        var pathToken = Expect(TokenType.StringLiteral);
        var filePath = pathToken.Value;

        if (Current.Type == TokenType.Semicolon)
            Consume();

        return WithPos(new ImportStatement(filePath), startToken, _tokens[_position - 1]);
    }
}