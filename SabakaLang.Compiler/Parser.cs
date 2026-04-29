using System.Globalization;

namespace SabakaLang.Compiler;

public readonly record struct Span(Position Start, Position End);

public interface INode {Span Span { get; }}

public interface IExpr : INode {}
public interface IStmt : INode {}

public record IntLit (int Value, Span Span) : IExpr;
public record FloatLit (double Value, Span Span) : IExpr;
public record StringLit (string Value, Span Span) : IExpr;
public record CharLit (char Value, Span Span) : IExpr;
public record BoolLit (bool Value, Span Span) : IExpr;
public record NullLit(Span Span) : IExpr;

public record NameExpr(string Name, Span Span) : IExpr;
public record BinaryExpr(IExpr Left, TokenType Op, IExpr Right, Span Span) : IExpr;
public record UnaryExpr(TokenType Op, IExpr Operand, Span Span) : IExpr;
public record CallExpr(IExpr Callee, List<IExpr> Args, Span Span) : IExpr;
public record MemberExpr(IExpr Object, string Member, Span Span) : IExpr;
public record IndexExpr(IExpr Object, IExpr Index, Span Span) : IExpr;
public record ArrayExpr(List<IExpr> Elements, Span Span) : IExpr;
public record NewExpr(string TypeName, List<string> TypeArgs, List<IExpr> Args, Span Span) : IExpr;
public record SuperExpr(Span Span) : IExpr;
public record AssignExpr(IExpr Target, IExpr Value, Span Span) : IExpr;
public record TernaryExpr(IExpr Condition, IExpr Then, IExpr Else, Span Span) : IExpr;
public record InterpolatedStringExpr(List<IExpr> Parts, Span Span) : IExpr;

public record CoalesceExpr(IExpr Left, IExpr Right, Span Span) : IExpr;
public record IsExpr(IExpr Left, TypeRef Right, Span Span) : IExpr;

public record ExprStmt(IExpr Expr, Span Span) : IStmt;
public record ReturnStmt(IExpr? Value, Span Span) : IStmt;
public record IfStmt(IExpr Condition, List<IStmt> Then, List<IStmt>? Else, Span Span) : IStmt;
public record WhileStmt(IExpr Condition, List<IStmt> Body, Span Span) : IStmt;
public record ForStmt(IStmt? Init, IExpr? Condition, IExpr? Step, List<IStmt> Body, Span Span) : IStmt;
public record ForeachStmt(TypeRef ItemType, string ItemName, IExpr Collection, List<IStmt> Body, Span Span) : IStmt;
public record SwitchStmt(IExpr Value, List<SwitchCase> Cases, Span Span) : IStmt;
public record ImportStmt(string Path, List<string> Names, string? Alias, Span Span) : IStmt;

public record VarDecl(TypeRef Type, string Name, IExpr? Init, AccessMod Access, Span Span) : IStmt;
public record FuncDecl(
    TypeRef ReturnType,
    string Name,
    List<TypeParam> TypeParams,
    List<Param> Params,
    List<IStmt> Body,
    AccessMod Access,
    bool IsOverride,
    Span Span) : IStmt;
public record ClassDecl(
    string Name,
    List<TypeParam> TypeParams,
    string? Base,
    List<string> Interfaces,
    List<VarDecl> Fields,
    List<FuncDecl> Methods,
    Span Span) : IStmt;
public record InterfaceDecl(
    string Name,
    List<TypeParam> TypeParams,
    List<string> Parents,
    List<FuncDecl> Methods,
    Span Span) : IStmt;
public record StructDecl(string Name, List<VarDecl> Fields, Span Span) : IStmt;
public record EnumDecl(string Name, List<string> Members, Span Span) : IStmt;

public record SwitchCase(IExpr? Value, List<IStmt> Body);
public record TypeRef(string Name, List<string> TypeArgs, bool IsArray, bool IsNullable = false);
public record TypeParam(string Name);
public record Param(TypeRef Type, string Name);
public enum AccessMod { Public, Private, Protected }

public record ParseError(string Message, Position Position);

public sealed class ParseResult(IReadOnlyList<IStmt> statements, IReadOnlyList<ParseError> errors)
{
    public IReadOnlyList<IStmt> Statements { get; } = statements;
    public IReadOnlyList<ParseError> Errors { get; } = errors;
    public bool HasErrors => Errors.Count > 0;
}

public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;
    private readonly List<ParseError> _errors = [];

    public Parser(LexerResult lexer)
    {
        _tokens = lexer.Tokens.Where(t => t.Type != TokenType.Comment).ToList();
    }
    
    public ParseResult Parse()
    {
        var stmts = new List<IStmt>();
        while (!IsAtEnd())
        {
            int posBeforeTop = _pos;
            var stmt = ParseTopLevel();
            if (!IsAtEnd() && _pos == posBeforeTop) Advance();
            if (stmt is not null) stmts.Add(stmt);
        }
        return new ParseResult(stmts, _errors);
    }

    private Token Current => Peek(0);

    private Token Peek(int offset = 1)
    {
        var i = _pos + offset;
        return i < _tokens.Count ? _tokens[i] : _tokens[^1];
    }

    private Token Advance()
    {
        var t = Current;
        if (!IsAtEnd()) _pos++;
        return t;
    }
    
    private bool IsAtEnd() => Current.Type == TokenType.Eof;
    
    private bool Check(TokenType type) => Current.Type == type;
    private bool Match(TokenType type)
    {
        if (!Check(type)) return false;
        Advance();
        return true;
    }

    private Token Expect(TokenType type)
    {
        if (Check(type)) return Advance();
        AddError($"Expected {type}, got {Current.Type}", SpanOf(Current).Start);
        return Current; 
    }
    
    private Span SpanFrom(Position start) => new(start, Current.Start);
    private Span SpanOf(Token t) => new(t.Start, t.End);
    
    private void Synchronize()
    {
        while (!IsAtEnd())
        {
            if (Current.Type == TokenType.Semicolon) { Advance(); return; }
            switch (Current.Type)
            {
                case TokenType.Class:
                case TokenType.If:
                case TokenType.While:
                case TokenType.For:
                case TokenType.Return:
                case TokenType.Case:
                case TokenType.Default:
                case TokenType.RBrace:
                    return;
            }
            Advance();
        }
    }

    private IStmt? ParseTopLevel()
    {
        try
        {
            return Current.Type switch
            {
                TokenType.Import    => ParseImport(),
                TokenType.Class     => ParseClass(),
                TokenType.Interface => ParseInterface(),
                TokenType.StructKeyword => ParseStruct(),
                TokenType.Enum      => ParseEnum(),
                _                   => ParseStatement()
            };
        }
        catch (ParseException)
        {
            Synchronize();
            return null;
        }
    }

    private IStmt ParseStatement()
    {
        return Current.Type switch
        {
            TokenType.If      => ParseIf(),
            TokenType.While   => ParseWhile(),
            TokenType.For     => ParseFor(),
            TokenType.Foreach => ParseForeach(),
            TokenType.Return  => ParseReturn(),
            TokenType.Switch  => ParseSwitch(),
            TokenType.Class     => ParseClass(),
            TokenType.Interface => ParseInterface(),
            TokenType.StructKeyword => ParseStruct(),
            TokenType.Enum      => ParseEnum(),
            TokenType.Import    => ParseImport(),
            _ => IsFuncDecl() ? ParseFuncDecl() : IsVarDecl() ? ParseVarDecl() : ParseExprStmt()
        };
    }

    private List<IStmt> ParseBlock()
    {
        Expect(TokenType.LBrace);
        var stmts = new List<IStmt>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            int posBeforeBlock = _pos;
            stmts.Add(ParseStatement());
            if (_pos == posBeforeBlock) Advance();
        }
        Expect(TokenType.RBrace);
        return stmts;
    }

    private List<IStmt> ParseBlockOrStmt()
    {
        if (Check(TokenType.LBrace)) return ParseBlock();
        return [ParseStatement()];
    }

    private bool IsVarDecl()
    {
        int offset = 0;
        if (IsAccessMod(Peek(offset).Type)) offset++;

        var cur = Peek(offset).Type;

        if (IsBuiltinType(cur) && cur != TokenType.VoidKeyword)
        {
            offset++;
            while (Peek(offset).Type == TokenType.LBracket && Peek(offset + 1).Type == TokenType.RBracket)
                offset += 2;
            if (Peek(offset).Type == TokenType.Question) offset++;
            return Peek(offset).Type == TokenType.Identifier;
        }

        if (cur == TokenType.Identifier)
        {
            offset++;
            if (Peek(offset).Type == TokenType.Less)
                offset = SkipAngles(offset);
            while (Peek(offset).Type == TokenType.LBracket && Peek(offset + 1).Type == TokenType.RBracket)
                offset += 2;
            if (Peek(offset).Type == TokenType.Question) offset++;
            if (Peek(offset).Type != TokenType.Identifier) return false;
            offset++;
            var next = Peek(offset).Type;
            return next is TokenType.Equal or TokenType.Semicolon or TokenType.Eof
                or TokenType.Comma or TokenType.RParen;
        }

        return false;
    }
    
    private int SkipAngles(int offset)
    {
        offset++; 
        int depth = 1;
        while (depth > 0 && _pos + offset < _tokens.Count)
        {
            var t = Peek(offset).Type;
            if (t == TokenType.Less) depth++;
            else if (t == TokenType.Greater) depth--;
            else if (t is TokenType.Semicolon or TokenType.Eof) break;
            offset++;
        }
        return offset;
    }

    private VarDecl ParseVarDecl(AccessMod defaultAccess  = AccessMod.Public)
    {
        var start = Current.Start;
        var access = TryConsumeAccessMod(defaultAccess);
        var type = ParseTypeRef();
        var name = Expect(TokenType.Identifier).Value;
        IExpr? init = null;
        if (Match(TokenType.Equal))
            init = ParseExpr();
        Match(TokenType.Semicolon);
        return new VarDecl(type, name, init, access, SpanFrom(start));
    }

    private FuncDecl ParseFuncDecl(AccessMod defaultAccess = AccessMod.Public)
    {
        var start = Current.Start;
        var access = TryConsumeAccessMod(defaultAccess);
        var isOverride = Match(TokenType.Override);
        var returnType = ParseTypeRef();
        var name = Expect(TokenType.Identifier).Value;
        var typeParams = TryParseTypeParams();
        Expect(TokenType.LParen);
        var parms = ParseParams();
        Expect(TokenType.RParen);
        var body = ParseBlock();
        return new FuncDecl(returnType, name, typeParams, parms, body, access, isOverride, SpanFrom(start));
    }
    
    private IfStmt ParseIf()
    {
        var start = Current.Start;
        Expect(TokenType.If);
        Expect(TokenType.LParen);
        var cond = ParseExpr();
        Expect(TokenType.RParen);
        var then = ParseBlockOrStmt();
        List<IStmt>? els = null;
        if (Match(TokenType.Else))
            els = ParseBlockOrStmt();
        return new IfStmt(cond, then, els, SpanFrom(start));
    }
 
    private WhileStmt ParseWhile()
    {
        var start = Current.Start;
        Expect(TokenType.While);
        Expect(TokenType.LParen);
        var cond = ParseExpr();
        Expect(TokenType.RParen);
        var body = ParseBlock();
        return new WhileStmt(cond, body, SpanFrom(start));
    }
 
    private ForStmt ParseFor()
    {
        var start = Current.Start;
        Expect(TokenType.For);
        Expect(TokenType.LParen);
        IStmt? init = null;
        if (!Check(TokenType.Semicolon))
            init = IsVarDecl() ? ParseVarDecl() : ParseExprStmt();
        else
            Advance();
        IExpr? cond = null;
        if (!Check(TokenType.Semicolon))
            cond = ParseExpr();
        Expect(TokenType.Semicolon);
        IExpr? step = null;
        if (!Check(TokenType.RParen))
            step = ParseExpr();
        Expect(TokenType.RParen);
        var body = ParseBlockOrStmt();
        return new ForStmt(init, cond, step, body, SpanFrom(start));
    }
 
    private ForeachStmt ParseForeach()
    {
        var start = Current.Start;
        Expect(TokenType.Foreach);
        Expect(TokenType.LParen);
        var type = ParseTypeRef();
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.In);
        var col = ParseExpr();
        Expect(TokenType.RParen);
        var body = ParseBlock();
        return new ForeachStmt(type, name, col, body, SpanFrom(start));
    }
 
    private ReturnStmt ParseReturn()
    {
        var start = Current.Start;
        Expect(TokenType.Return);
        IExpr? val = null;
        if (!Check(TokenType.Semicolon))
            val = ParseExpr();
        Match(TokenType.Semicolon);
        return new ReturnStmt(val, SpanFrom(start));
    }
 
    private SwitchStmt ParseSwitch()
    {
        var start = Current.Start;
        Expect(TokenType.Switch);
        Expect(TokenType.LParen);
        var val = ParseExpr();
        Expect(TokenType.RParen);
        Expect(TokenType.LBrace);
        var cases = new List<SwitchCase>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            int posBeforeSwitch = _pos;
            if (Check(TokenType.Case))
            {
                Advance();
                var caseVal = ParseExpr();
                Expect(TokenType.Colon);
                cases.Add(new SwitchCase(caseVal, ParseBlockOrStmt()));
            }
            else if (Check(TokenType.Default))
            {
                Advance();
                Expect(TokenType.Colon);
                cases.Add(new SwitchCase(null, ParseBlockOrStmt()));
            }
            else
            {
                AddError($"Expected 'case' or 'default', got {Current.Type}", Current.Start);
                Synchronize();
            }
            if (_pos == posBeforeSwitch) Advance();
        }
        Expect(TokenType.RBrace);
        return new SwitchStmt(val, cases, SpanFrom(start));
    }
 
    private ImportStmt ParseImport()
    {
        var start = Current.Start;
        Expect(TokenType.Import);
        var path = Expect(TokenType.StringLiteral).Value;
        var names = new List<string>();
        string? alias = null;
        if (Current is { Type: TokenType.Identifier, Value: "from" })
        {
            Advance();
            names.Add(Expect(TokenType.Identifier).Value);
            while (Match(TokenType.Comma))
                names.Add(Expect(TokenType.Identifier).Value);
        }
        if (Current is { Type: TokenType.Identifier, Value: "as" })
        {
            Advance();
            alias = Expect(TokenType.Identifier).Value;
        }
        Match(TokenType.Semicolon);
        return new ImportStmt(path, names, alias, SpanFrom(start));
    }
 
    private ClassDecl ParseClass()
    {
        var start = Current.Start;
        Expect(TokenType.Class);
        var name = Expect(TokenType.Identifier).Value;
        var typeParams = TryParseTypeParams();
        string? baseClass = null;
        var interfaces = new List<string>();
        if (Match(TokenType.Colon))
        {
            baseClass = Expect(TokenType.Identifier).Value;
            while (Match(TokenType.Comma))
                interfaces.Add(Expect(TokenType.Identifier).Value);
        }
        Expect(TokenType.LBrace);
        var fields = new List<VarDecl>();
        var methods = new List<FuncDecl>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            int posBeforeClass = _pos;
            if (IsFuncDecl()) methods.Add(ParseFuncDecl());
            else              fields.Add(ParseVarDecl());
            if (_pos == posBeforeClass) Advance();
        }
        Expect(TokenType.RBrace);
        return new ClassDecl(name, typeParams, baseClass, interfaces, fields, methods, SpanFrom(start));
    }
 
    private InterfaceDecl ParseInterface()
    {
        var start = Current.Start;
        Expect(TokenType.Interface);
        var name = Expect(TokenType.Identifier).Value;
        var typeParams = TryParseTypeParams();
        var parents = new List<string>();
        if (Match(TokenType.Colon))
        {
            parents.Add(Expect(TokenType.Identifier).Value);
            while (Match(TokenType.Comma))
                parents.Add(Expect(TokenType.Identifier).Value);
        }
        Expect(TokenType.LBrace);
        var methods = new List<FuncDecl>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            int posBeforeIface = _pos;
            var mStart = Current.Start;
            var retType = ParseTypeRef();
            var mName = Expect(TokenType.Identifier).Value;
            Expect(TokenType.LParen);
            var parms = ParseParams();
            Expect(TokenType.RParen);
            Expect(TokenType.Semicolon);
            methods.Add(new FuncDecl(retType, mName, [], parms, [], AccessMod.Public, false, SpanFrom(mStart)));
            if (_pos == posBeforeIface) Advance();
        }
        Expect(TokenType.RBrace);
        return new InterfaceDecl(name, typeParams, parents, methods, SpanFrom(start));
    }
 
    private StructDecl ParseStruct()
    {
        var start = Current.Start;
        Expect(TokenType.StructKeyword);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);
        var fields = new List<VarDecl>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            int posBeforeStruct = _pos;
            fields.Add(ParseVarDecl());
            if (_pos == posBeforeStruct) Advance();
        }
        Expect(TokenType.RBrace);
        return new StructDecl(name, fields, SpanFrom(start));
    }
 
    private EnumDecl ParseEnum()
    {
        var start = Current.Start;
        Expect(TokenType.Enum);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LBrace);
        var members = new List<string>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            members.Add(Expect(TokenType.Identifier).Value);
            if (!Match(TokenType.Comma)) break;
        }
        Expect(TokenType.RBrace);
        return new EnumDecl(name, members, SpanFrom(start));
    }
 
    private ExprStmt ParseExprStmt()
    {
        var start = Current.Start;
        var expr = ParseExpr();
        Match(TokenType.Semicolon);
        return new ExprStmt(expr, SpanFrom(start));
    }

    private IExpr ParseExpr() => ParseAssign();

    private IExpr ParseAssign()
    {
        var start = Current.Start;
        var left = ParseTernary();

        if (Check(TokenType.Equal))
        {
            Advance();
            var right = ParseAssign();

            if (left is NameExpr or MemberExpr or IndexExpr)
                return new AssignExpr(left, right, SpanFrom(start));

            AddError("Invalid assignment target", start);
            return left;
        }

        if (Check(TokenType.PlusEqual) ||
            Check(TokenType.MinusEqual) ||
            Check(TokenType.StarEqual))
        {
            var op = Current.Type;
            Advance();

            var right = ParseAssign();

            if (left is NameExpr or MemberExpr or IndexExpr)
            {
                var binaryOp = op switch
                {
                    TokenType.PlusEqual => TokenType.Plus,
                    TokenType.MinusEqual => TokenType.Minus,
                    TokenType.StarEqual => TokenType.Star,
                    _ => throw new Exception("Unexpected compound operator")
                };

                var value = new BinaryExpr(left, binaryOp, right, SpanFrom(start));
                return new AssignExpr(left, value, SpanFrom(start));
            }

            AddError("Invalid assignment target", start);
        }

        return left;
    }
    
    private IExpr ParseTernary()
    {
        var start = Current.Start;
        var condition = ParseCoalesce();

        if (Match(TokenType.Question))
        {
            var thenExpr = ParseExpr();
            Expect(TokenType.Colon);
            var elseExpr = ParseTernary();

            return new TernaryExpr(condition, thenExpr, elseExpr, SpanFrom(start));
        }

        return condition;
    }

    private IExpr ParseCoalesce()
    {
        var start = Current.Start;
        var left = ParseLogicalOr();

        while (Match(TokenType.QuestionQuestion))
        {
            var rigth = ParseCoalesce();
            left = new CoalesceExpr(left, rigth, SpanFrom(start));
        }

        return left;
    }

    private IExpr ParseLogicalOr()
    {
        var start = Current.Start;
        var left = ParseLogicalAnd();
        while (Check(TokenType.OrOr))
        {
            var op = Advance().Type;
            var right = ParseLogicalAnd();
            left = new BinaryExpr(left, op, right, SpanFrom(start));
        }
        return left;
    }
    
    private IExpr ParseLogicalAnd()
    {
        var start = Current.Start;
        var left = ParseEquality();
        while (Check(TokenType.AndAnd))
        {
            var op = Advance().Type;
            var right = ParseEquality();
            left = new BinaryExpr(left, op, right, SpanFrom(start));
        }
        return left;
    }
 
    private IExpr ParseEquality()
    {
        var start = Current.Start;
        var left = ParseIs();

        while (Check(TokenType.EqualEqual) || Check(TokenType.NotEqual))
        {
            var op = Advance().Type;
            var right = ParseIs();
            left = new BinaryExpr(left, op, right, SpanFrom(start));
        }

        return left;
    }

    private IExpr ParseIs()
    {
        var start = Current.Start;
        var left = ParseComparison();

        while (Check(TokenType.Is))
        {
            Advance();

            var right = ParseTypeRef();

            left = new IsExpr(left, right, SpanFrom(start));
        }

        return left;
    }
 
    private IExpr ParseComparison()
    {
        var start = Current.Start;
        var left = ParseAdditive();
        while (Check(TokenType.Greater) || Check(TokenType.Less) ||
               Check(TokenType.GreaterEqual) || Check(TokenType.LessEqual))
        {
            var op = Advance().Type;
            var right = ParseAdditive();
            left = new BinaryExpr(left, op, right, SpanFrom(start));
        }
        return left;
    }
 
    private IExpr ParseAdditive()
    {
        var start = Current.Start;
        var left = ParseMultiplicative();
        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var op = Advance().Type;
            var right = ParseMultiplicative();
            left = new BinaryExpr(left, op, right, SpanFrom(start));
        }
        return left;
    }
 
    private IExpr ParseMultiplicative()
    {
        var start = Current.Start;
        var left = ParseUnary();
        while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            var op = Advance().Type;
            var right = ParseUnary();
            left = new BinaryExpr(left, op, right, SpanFrom(start));
        }
        return left;
    }
 
    private IExpr ParseUnary()
    {
        var start = Current.Start;
        if (Check(TokenType.Minus) || Check(TokenType.Bang))
        {
            var op = Advance().Type;
            var operand = ParseUnary();
            return new UnaryExpr(op, operand, SpanFrom(start));
        }
        return ParsePostfix();
    }

    private IExpr ParsePostfix()
    {
        var start = Current.Start;
        var expr = ParsePrimary();
 
        while (true)
        {
            if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            {
                var op = Current.Type;
                Advance();

                if (expr is NameExpr or MemberExpr or IndexExpr)
                {
                    var one = new IntLit(1, SpanFrom(start));

                    var binaryOp = op == TokenType.PlusPlus
                        ? TokenType.Plus
                        : TokenType.Minus;

                    var value = new BinaryExpr(expr, binaryOp, one, SpanFrom(start));

                    expr = new AssignExpr(expr, value, SpanFrom(start));
                    continue;
                }

                AddError("Invalid increment target", start);
                continue;
            }
            if (Check(TokenType.LParen))
            {
                Advance();
                var args = ParseArgList();
                Expect(TokenType.RParen);
                expr = new CallExpr(expr, args, SpanFrom(start));
            }
            else if (Check(TokenType.Dot))
            {
                Advance();
                var member = Expect(TokenType.Identifier).Value;
                expr = new MemberExpr(expr, member, SpanFrom(start));
            }
            else if (Check(TokenType.LBracket))
            {
                Advance();
                var index = ParseExpr();
                Expect(TokenType.RBracket);
                expr = new IndexExpr(expr, index, SpanFrom(start));
            }
            else break;
        }
 
        return expr;
    }
    
    private IExpr ParsePrimary()
    {
        var start = Current.Start;
 
        if (Check(TokenType.IntLiteral))
        {
            var v = int.Parse(Advance().Value, CultureInfo.InvariantCulture);
            return new IntLit(v, SpanFrom(start));
        }
        if (Check(TokenType.FloatLiteral))
        {
            var v = double.Parse(Advance().Value, CultureInfo.InvariantCulture);
            return new FloatLit(v, SpanFrom(start));
        }
        if (Check(TokenType.StringLiteral))
            return new StringLit(Advance().Value, SpanFrom(start));
        if (Check(TokenType.CharLiteral))
            return  new CharLit(Advance().Value[0], SpanFrom(start));
        if (Check(TokenType.InterpolatedStringLiteral))
            return ParseInterpolatedString(start);
        if (Check(TokenType.True))  { Advance(); return new BoolLit(true,  SpanFrom(start)); }
        if (Check(TokenType.False)) { Advance(); return new BoolLit(false, SpanFrom(start)); }

        if (Check(TokenType.Null))
        {
            Advance();
            return new NullLit(SpanFrom(start));
        }
        
        if (Check(TokenType.Identifier))
            return new NameExpr(Advance().Value, SpanFrom(start));
 
        if (Check(TokenType.Super))
        {
            Advance();
            return new SuperExpr(SpanFrom(start));
        }
 
        if (Check(TokenType.New))
        {
            Advance();
            var typeName = Expect(TokenType.Identifier).Value;
            var typeArgs = TryParseTypeArgs();
            Expect(TokenType.LParen);
            var args = ParseArgList();
            Expect(TokenType.RParen);
            return new NewExpr(typeName, typeArgs, args, SpanFrom(start));
        }
 
        if (Check(TokenType.LBracket))
        {
            Advance();
            var elements = new List<IExpr>();
            if (!Check(TokenType.RBracket))
            {
                elements.Add(ParseExpr());
                while (Match(TokenType.Comma))
                    elements.Add(ParseExpr());
            }
            Expect(TokenType.RBracket);
            return new ArrayExpr(elements, SpanFrom(start));
        }
 
        if (Check(TokenType.LParen))
        {
            Advance();
            var expr = ParseExpr();
            Expect(TokenType.RParen);
            return expr;
        }
 
        AddError($"Unexpected token: {Current.Type}", Current.Start);
        throw new ParseException();
    }
    
    private List<IExpr> ParseArgList()
    {
        var args = new List<IExpr>();
        if (Check(TokenType.RParen)) return args;
        args.Add(ParseExpr());
        while (Match(TokenType.Comma)) args.Add(ParseExpr());
        return args;
    }

    private InterpolatedStringExpr ParseInterpolatedString(Position start)
    {
        var raw = Advance().Value;
        var parts = new List<IExpr>();
        var i = 0;

        while (i <= raw.Length)
        {
            int braceOpen = raw.IndexOf('{', i);
 
            if (braceOpen < 0)
            {
                var tail = raw.Substring(i);
                if (tail.Length > 0 || parts.Count == 0)
                    parts.Add(new StringLit(tail, SpanFrom(start)));
                break;
            }
 
            if (braceOpen > i)
            {
                var literal = raw.Substring(i, braceOpen - i);
                parts.Add(new StringLit(literal, SpanFrom(start)));
            }
 
            int depth = 1;
            int j = braceOpen + 1;
            while (j < raw.Length && depth > 0)
            {
                if (raw[j] == '{') depth++;
                else if (raw[j] == '}') depth--;
                if (depth > 0) j++;
            }
 
            if (depth != 0)
            {
                AddError("Unmatched '{' in interpolated string", start);
                break;
            }
 
            var exprSource = raw.Substring(braceOpen + 1, j - braceOpen - 1);
            var subLexer  = new Lexer(exprSource).Tokenize();
            var subParser = new Parser(subLexer);
            var subResult = subParser.Parse();
 
            foreach (var err in subResult.Errors)
                AddError($"In interpolated expression: {err.Message}", start);
 
            if (subResult.Statements is [ExprStmt es])
                parts.Add(es.Expr);
            else
                AddError("Expected a single expression inside interpolation braces", start);
 
            i = j + 1;
        }
        
        return new InterpolatedStringExpr(parts, SpanFrom(start));
    }
    
    private List<Param> ParseParams()
    {
        var parms = new List<Param>();
        if (Check(TokenType.RParen)) return parms;
        parms.Add(new Param(ParseTypeRef(), Expect(TokenType.Identifier).Value));
        while (Match(TokenType.Comma))
            parms.Add(new Param(ParseTypeRef(), Expect(TokenType.Identifier).Value));
        return parms;
    }
    
    private TypeRef ParseTypeRef()
    {
        var name = Current.Type switch
        {
            TokenType.IntKeyword    => "int",
            TokenType.FloatKeyword  => "float",
            TokenType.BoolKeyword   => "bool",
            TokenType.StringKeyword => "string",
            TokenType.CharKeyword   => "char",
            TokenType.VoidKeyword   => "void",
            TokenType.Identifier    => Current.Value,
            _ => null
        };
        if (name is null) { AddError($"Expected type, got {Current.Type}", Current.Start); return new TypeRef("?", [], false, false); }
        Advance();
        var typeArgs = TryParseTypeArgs();
        bool isArray = false;
        if (Check(TokenType.LBracket) && Peek().Type == TokenType.RBracket)
        {
            Advance(); Advance();
            isArray = true;
        }
        
        bool isNullable = Match(TokenType.Question);
        return new TypeRef(name, typeArgs, isArray, isNullable);
    }
    
    private List<TypeParam> TryParseTypeParams()
    {
        if (!Check(TokenType.Less)) return [];
        var saved = _pos;
        Advance(); 
        var result = new List<TypeParam>();
        while (Check(TokenType.Identifier))
        {
            result.Add(new TypeParam(Advance().Value));
            if (!Match(TokenType.Comma)) break;
        }
        if (!Check(TokenType.Greater)) { _pos = saved; return []; }
        Advance();
        return result;
    }
 
    private List<string> TryParseTypeArgs()
    {
        if (!Check(TokenType.Less)) return [];
        var saved = _pos;
        Advance(); 
        var result = new List<string>();
        while (IsTypeStart(Current.Type))
        {
            result.Add(Current.Value);
            Advance();
            if (!Match(TokenType.Comma)) break;
        }
        if (!Check(TokenType.Greater)) { _pos = saved; return []; }
        Advance();
        return result;
    }
    
    private bool IsFuncDecl()
    {
        int o = 0;
        if (IsAccessMod(Peek(o).Type)) o++;
        if (Peek(o).Type == TokenType.Override) o++;
        if (!IsTypeStart(Peek(o).Type)) return false;
        o++;
        if (Peek(o).Type == TokenType.Less) o = SkipAngles(o);
        while (Peek(o).Type == TokenType.LBracket && Peek(o + 1).Type == TokenType.RBracket) o += 2;
        if (Peek(o).Type != TokenType.Identifier) return false;
        o++;
        if (Peek(o).Type == TokenType.Less) o = SkipAngles(o);
        return Peek(o).Type == TokenType.LParen;
    }
 
    private static bool IsBuiltinType(TokenType t) =>
        t is TokenType.IntKeyword or TokenType.FloatKeyword or
            TokenType.BoolKeyword or TokenType.StringKeyword or TokenType.CharKeyword or TokenType.VoidKeyword;
 
    private static bool IsTypeStart(TokenType t) => IsBuiltinType(t) || t == TokenType.Identifier;
 
    private static bool IsAccessMod(TokenType t) =>
        t is TokenType.Public or TokenType.Private or TokenType.Protected;
 
    private AccessMod TryConsumeAccessMod(AccessMod def)
    {
        if (!IsAccessMod(Current.Type)) return def;
        return Advance().Type switch
        {
            TokenType.Public    => AccessMod.Public,
            TokenType.Private   => AccessMod.Private,
            TokenType.Protected => AccessMod.Protected,
            _                   => def
        };
    }
    
    private void AddError(string message, Position position)
    {
        _errors.Add(new ParseError(message, position));
    }
}

file sealed class ParseException : Exception { }