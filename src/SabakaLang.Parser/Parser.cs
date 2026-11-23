using System.Globalization;
using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;
using SabakaLang.Lexer.Enums;
using SabakaLang.Lexer.Results;
using SabakaLang.Lexer.Structures;
using SabakaLang.Parser.Exceptions;
using SabakaLang.Parser.Results;
using SabakaLang.Parser.Structures;

namespace SabakaLang.Parser;

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
            TokenType.Break    => ParseBreakContinue(),
            TokenType.Continue => ParseBreakContinue(),
            _ => IsConstDecl() ? ParseConstDecl() :
                IsFuncDecl() ? ParseFuncDecl() :
                IsVarDecl() ? ParseVarDecl() : ParseExprStmt()
        };
    }

    private IStmt ParseBreakContinue()
    {
        var start = Current.Start;
        var isBreak = Advance().Type == TokenType.Break;
        Match(TokenType.Semicolon);
        var span = SpanFrom(start);
        return isBreak ? new BreakStmt(span) : new ContinueStmt(span);
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
            offset = SkipQualifiedName(offset);
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
    
    private int SkipQualifiedName(int offset)
    {
        while (Peek(offset).Type == TokenType.Dot && Peek(offset + 1).Type == TokenType.Identifier)
            offset += 2;
        return offset;
    }

    private bool IsConstDecl()
    {
        int offset = 0;
        if (IsAccessMod(Peek(offset).Type)) offset++;
        return Peek(offset).Type == TokenType.Const;
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
    
    private ConstDecl ParseConstDecl(AccessMod defaultAccess = AccessMod.Public)
    {
        var start = Current.Start;
        var access = TryConsumeAccessMod(defaultAccess);

        Expect(TokenType.Const);
        var type = ParseTypeRef();
        var name = Expect(TokenType.Identifier).Value;

        Expect(TokenType.Equal);
        var value = ParseExpr();

        Match(TokenType.Semicolon);

        return new ConstDecl(type, name, value, access, SpanFrom(start));
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
        
        TypeRef returnType;
        if (Check(TokenType.Identifier) && Current.Value == "func")
        {
            Advance();
            returnType = new TypeRef("void", [], false, false);
        }
        else
        {
            returnType = ParseTypeRef();
        }

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
        var body = ParseBlockOrStmt();
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
        var body = ParseBlockOrStmt();
        return new ForeachStmt(type, name, col, body, SpanFrom(start));
    }
 
    private ReturnStmt ParseReturn()
    {
        var start = Current.Start;
        Expect(TokenType.Return);
        IExpr? val = null;
        if (!Check(TokenType.Semicolon) && !Check(TokenType.RBrace) && !IsAtEnd())
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
        string path;
        if (Check(TokenType.Identifier))
        {
            path = Advance().Value;
            while (Check(TokenType.Dot) && Peek().Type == TokenType.Identifier)
            {
                Advance();
                path += "." + Advance().Value;
            }
        }
        else
        {
            path = Expect(TokenType.StringLiteral).Value;
        }
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
            baseClass = ParseBaseTypeName();
            while (Match(TokenType.Comma))
                interfaces.Add(ParseBaseTypeName());
        }
        Expect(TokenType.LBrace);
        var fields = new List<VarDecl>();
        var methods = new List<FuncDecl>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            int posBeforeClass = _pos;
            if (IsShortCtor(name))  methods.Add(ParseShortCtor());
            else if (IsFuncDecl())  methods.Add(ParseFuncDecl());
            else if (IsConstDecl()) fields.Add(ParseConstDecl().ToVarDecl());
            else                    fields.Add(ParseVarDecl());
            if (_pos == posBeforeClass) Advance();
        }
        Expect(TokenType.RBrace);
        return new ClassDecl(name, typeParams, baseClass, interfaces, fields, methods, SpanFrom(start));
    }
 
    /// <summary>Base class / interface reference: <c>Name</c>, <c>mod.Name</c> or <c>Name&lt;int,string&gt;</c> (kept as text).</summary>
    private string ParseBaseTypeName()
    {
        var name = Expect(TokenType.Identifier).Value;
        while (Check(TokenType.Dot) && Peek().Type == TokenType.Identifier)
        {
            Advance();
            name += "." + Advance().Value;
        }
        var args = TryParseTypeArgs();
        return args.Count > 0 ? $"{name}<{string.Join(",", args)}>" : name;
    }

    private bool IsShortCtor(string className)
    {
        var o = IsAccessMod(Peek(0).Type) ? 1 : 0;
        return Peek(o).Type == TokenType.Identifier && Peek(o).Value == className
               && Peek(o + 1).Type == TokenType.LParen;
    }

    /// <summary>Parses <c>Name(params) { ... }</c> – sugar for <c>void Name(params) { ... }</c>.</summary>
    private FuncDecl ParseShortCtor()
    {
        var start = Current.Start;
        var access = TryConsumeAccessMod(AccessMod.Public);
        var name = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LParen);
        var parms = ParseParams();
        Expect(TokenType.RParen);
        var body = ParseBlock();
        return new FuncDecl(new TypeRef("void", [], false), name, [], parms, body, access, false, SpanFrom(start));
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
            parents.Add(ParseBaseTypeName());
            while (Match(TokenType.Comma))
                parents.Add(ParseBaseTypeName());
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
            Check(TokenType.StarEqual) ||
            Check(TokenType.SlashEqual) ||
            Check(TokenType.PercentEqual))
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
                    TokenType.SlashEqual => TokenType.Slash,
                    TokenType.PercentEqual => TokenType.Percent,
                    _ => throw new Exception("Unexpected compound operator")
                };

                var value = new BinaryExpr(left, binaryOp, right, SpanFrom(start));
                return new AssignExpr(left, value, SpanFrom(start));
            }

            AddError("Invalid assignment target", start);
            return left;
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

            var right = ParseTypeRef(allowNullable: false);

            // `x is int? ...` – a trailing '?' is a nullable suffix only when no expression follows it,
            // otherwise it is the ternary operator: `x is int ? 1 : 2`.
            if (Check(TokenType.Question) && !IsExprStart(Peek().Type))
            {
                Advance();
                right = right with { IsNullable = true };
            }

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
        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
        {
            var op = Advance().Type;
            var operand = ParseUnary();
            if (operand is NameExpr or MemberExpr or IndexExpr)
            {
                var binaryOp = op == TokenType.PlusPlus ? TokenType.Plus : TokenType.Minus;
                var value = new BinaryExpr(operand, binaryOp, new IntLit(1, SpanFrom(start)), SpanFrom(start));
                return new AssignExpr(operand, value, SpanFrom(start));
            }
            AddError("Invalid increment target", start);
            return operand;
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
            var text = Advance().Value;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            {
                AddError($"Integer literal '{text}' is out of range for int", start);
                v = 0;
            }
            return new IntLit(v, SpanFrom(start));
        }
        if (Check(TokenType.FloatLiteral))
        {
            var text = Advance().Value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                AddError($"Invalid float literal '{text}'", start);
                v = 0;
            }
            return new FloatLit(v, SpanFrom(start));
        }
        if (Check(TokenType.StringLiteral))
            return new StringLit(Advance().Value, SpanFrom(start));
        if (Check(TokenType.CharLiteral))
        {
            var text = Advance().Value;
            return new CharLit(text.Length > 0 ? text[0] : '\0', SpanFrom(start));
        }
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

            // new int[n]  /  new Foo[n]
            if (IsBuiltinType(Current.Type) && Current.Type != TokenType.VoidKeyword ||
                Check(TokenType.Identifier) && IsArrayCreationAhead())
            {
                var elemStart = Current.Start;
                var elemName = Current.Type switch
                {
                    TokenType.IntKeyword    => "int",
                    TokenType.FloatKeyword  => "float",
                    TokenType.BoolKeyword   => "bool",
                    TokenType.StringKeyword => "string",
                    TokenType.CharKeyword   => "char",
                    _ => Current.Value
                };
                Advance();
                Expect(TokenType.LBracket);
                var size = ParseExpr();
                Expect(TokenType.RBracket);
                return new NewArrayExpr(new TypeRef(elemName, [], false, false, SpanFrom(elemStart)), size, SpanFrom(start));
            }

            var typeName = Expect(TokenType.Identifier).Value;
            while (Check(TokenType.Dot) && Peek().Type == TokenType.Identifier)
            {
                Advance();
                typeName += "." + Advance().Value;
            }
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
    
    private bool IsArrayCreationAhead() =>
        Peek(0).Type == TokenType.Identifier && Peek(1).Type == TokenType.LBracket;

    private static bool IsExprStart(TokenType t) => t is
        TokenType.IntLiteral or TokenType.FloatLiteral or TokenType.StringLiteral or TokenType.CharLiteral or
        TokenType.InterpolatedStringLiteral or TokenType.True or TokenType.False or TokenType.Null or
        TokenType.Identifier or TokenType.LParen or TokenType.LBracket or TokenType.Minus or TokenType.Bang or
        TokenType.New or TokenType.Super or TokenType.PlusPlus or TokenType.MinusMinus;

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
            var origin = new Position(start.Line, start.Column + 2 + braceOpen + 1, start.Offset + 2 + braceOpen + 1);
            var subLexer  = ShiftTokens(new Lexer.Lexer().Tokenize(exprSource), origin);
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
    
    private static LexerResult ShiftTokens(LexerResult r, Position origin)
    {
        Position Map(Position p) => new(
            origin.Line + p.Line - 1,
            p.Line == 1 ? origin.Column + p.Column - 1 : p.Column,
            origin.Offset + p.Offset);

        var tokens = r.Tokens.Select(t => t with { Start = Map(t.Start), End = Map(t.End) }).ToList();
        var errors = r.Errors.Select(e => e with { Position = Map(e.Position) }).ToList();
        return new LexerResult(tokens, errors);
    }

    private List<Param> ParseParams()
    {
        var parms = new List<Param>();
        if (Check(TokenType.RParen)) return parms;
        parms.Add(ParseParam());
        while (Match(TokenType.Comma))
            parms.Add(ParseParam());
        return parms;
    }
    
    private Param ParseParam()
    {
        var start = Current.Start;
        var type = ParseTypeRef();
        var name = Expect(TokenType.Identifier).Value;
        return new Param(type, name, SpanFrom(start));
    }

    private TypeRef ParseTypeRef(bool allowNullable = true)
    {
        var start = Current.Start;
        var isIdentifier = Current.Type == TokenType.Identifier;
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
        if (name is null) { AddError($"Expected type, got {Current.Type}", Current.Start); return new TypeRef("?", [], false, false, SpanOf(Current)); }
        Advance();

        // qualified name: module.Type
        if (isIdentifier)
        {
            while (Check(TokenType.Dot) && Peek().Type == TokenType.Identifier)
            {
                Advance();
                name += "." + Advance().Value;
            }
        }

        var typeArgs = TryParseTypeArgs();
        var depth = 0;
        while (Check(TokenType.LBracket) && Peek().Type == TokenType.RBracket)
        {
            Advance(); Advance();
            depth++;
        }

        var isNullable = allowNullable && Match(TokenType.Question);
        return new TypeRef(name, typeArgs, depth > 0, isNullable, SpanFrom(start)) { ArrayDepth = depth };
    }

    /// <summary>Canonical textual form of a type, used for <see cref="TypeRef.TypeArgs"/> entries.</summary>
    private static string TypeToString(TypeRef t)
    {
        var s = t.Name;
        if (t.TypeArgs.Count > 0) s += "<" + string.Join(",", t.TypeArgs) + ">";
        for (var i = 0; i < t.ArrayDepth; i++) s += "[]";
        if (t.IsNullable) s += "?";
        return s;
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
        var savedErrors = _errors.Count;
        Advance();
        var result = new List<string>();
        while (IsTypeStart(Current.Type))
        {
            result.Add(TypeToString(ParseTypeRef()));
            if (!Match(TokenType.Comma)) break;
        }
        if (result.Count == 0 || !Check(TokenType.Greater))
        {
            _pos = saved;
            _errors.RemoveRange(savedErrors, _errors.Count - savedErrors);
            return [];
        }
        Advance();
        return result;
    }
    
    private bool IsFuncDecl()
    {
        int o = 0;
        if (IsAccessMod(Peek(o).Type)) o++;
        if (Peek(o).Type == TokenType.Override) o++;

        if (Peek(o).Type == TokenType.Identifier && Peek(o).Value == "func")
            return true;

        if (!IsTypeStart(Peek(o).Type)) return false;
        var typeIsIdent = Peek(o).Type == TokenType.Identifier;
        o++;
        if (typeIsIdent) o = SkipQualifiedName(o);
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