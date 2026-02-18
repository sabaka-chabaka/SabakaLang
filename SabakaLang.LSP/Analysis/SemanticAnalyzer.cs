using SabakaLang.AST;
using SabakaLang.Exceptions;
using SabakaLang.Lexer;

namespace SabakaLang.LSP.Analysis;

public class SemanticAnalyzer
{
    private Scope _currentScope;
    private readonly Scope _globalScope;
    private readonly List<Expr> _ast;
    private readonly List<Symbol> _allSymbols = new();
    private readonly Stack<(int start, int end)> _scopeRanges = new();
    private string? _currentParent = null;

    public IEnumerable<Symbol> AllSymbols => _allSymbols;
    public IEnumerable<Symbol> GlobalSymbols => _globalScope.Symbols;

    public SemanticAnalyzer(List<Expr> ast)
    {
        _ast = ast;
        _globalScope = new Scope();
        _currentScope = _globalScope;
        InitializeGlobalScope();
    }

    private void InitializeGlobalScope()
    {
        Declare("print", SymbolKind.BuiltIn, "void", 0, 0);
        Declare("input", SymbolKind.BuiltIn, "string", 0, 0);
    }

    private bool Declare(string name, SymbolKind kind, string type, int start, int end, int scopeStart = 0, int scopeEnd = int.MaxValue, string? parentName = null)
    {
        var symbol = new Symbol(name, kind, type, start, end, scopeStart, scopeEnd, parentName);
        if (!_currentScope.Declare(symbol))
            return false;
        
        _allSymbols.Add(symbol);
        return true;
    }

    private (int start, int end) CurrentScopeRange => _scopeRanges.Count > 0 ? _scopeRanges.Peek() : (0, int.MaxValue);

    public void Analyze()
    {
        // First pass: Register global declarations
        foreach (var expr in _ast)
        {
            RegisterGlobal(expr);
        }

        // Second pass: Analyze bodies and expressions
        foreach (var expr in _ast)
        {
            AnalyzeExpr(expr);
        }
    }

    private void RegisterGlobal(Expr expr)
    {
        switch (expr)
        {
            case FunctionDeclaration func:
                string funcReturnType = func.ReturnType == TokenType.Identifier ? "unknown" : func.ReturnType.ToString();
                if (!Declare(func.Name, SymbolKind.Function, funcReturnType, func.Start, func.End))
                {
                    throw new SemanticException($"Function '{func.Name}' is already declared", func.Start);
                }
                break;
            case ClassDeclaration cls:
                if (!Declare(cls.Name, SymbolKind.Class, cls.Name, cls.Start, cls.End))
                {
                    throw new SemanticException($"Class '{cls.Name}' is already declared", cls.Start);
                }
                break;
            case StructDeclaration str:
                if (!Declare(str.Name, SymbolKind.Class, str.Name, str.Start, str.End))
                {
                    throw new SemanticException($"Struct '{str.Name}' is already declared", str.Start);
                }
                break;
            case EnumDeclaration en:
                if (!Declare(en.Name, SymbolKind.Class, en.Name, en.Start, en.End))
                {
                    throw new SemanticException($"Enum '{en.Name}' is already declared", en.Start);
                }
                break;
        }
    }

    private void AnalyzeExpr(Expr expr)
    {
        switch (expr)
        {
            case VariableDeclaration varDecl:
                if (varDecl.Initializer != null)
                    AnalyzeExpr(varDecl.Initializer);
                
                var varKind = _currentParent != null ? SymbolKind.Field : SymbolKind.Variable;
                string typeName = varDecl.CustomType ?? varDecl.TypeToken.ToString();
                if (!Declare(varDecl.Name, varKind, typeName, varDecl.Start, varDecl.End, varDecl.End, CurrentScopeRange.end, _currentParent))
                {
                    throw new SemanticException($"Variable '{varDecl.Name}' is already declared in this scope", varDecl.Start);
                }
                break;

            case FunctionDeclaration funcDecl:
                {
                    var funcKind = _currentParent != null ? SymbolKind.Method : SymbolKind.Function;
                    string returnType = funcDecl.ReturnType == TokenType.Identifier ? (funcDecl.Name == "Constructor" ? "" : "unknown") : funcDecl.ReturnType.ToString();
                    Declare(funcDecl.Name, funcKind, returnType, funcDecl.Start, funcDecl.End, CurrentScopeRange.start, CurrentScopeRange.end, _currentParent);

                    var previousScope = _currentScope;
                    _currentScope = new Scope(previousScope);
                    _scopeRanges.Push((funcDecl.Start, funcDecl.End));
                    
                    var previousParent = _currentParent;
                    _currentParent = null;

                    foreach (var param in funcDecl.Parameters)
                    {
                        string paramType = param.CustomType ?? param.Type.ToString();
                        if (!Declare(param.Name, SymbolKind.Parameter, paramType, funcDecl.Start, funcDecl.End, funcDecl.Start, funcDecl.End))
                        {
                             throw new SemanticException($"Parameter '{param.Name}' is already declared in this scope", funcDecl.Start);
                        }
                    }
                    
                    foreach (var bodyExpr in funcDecl.Body)
                        AnalyzeExpr(bodyExpr);
                    
                    _currentParent = previousParent;
                    _scopeRanges.Pop();
                    _currentScope = previousScope;
                }
                break;

            case ClassDeclaration classDecl:
                {
                    Declare(classDecl.Name, SymbolKind.Class, classDecl.Name, classDecl.Start, classDecl.End, CurrentScopeRange.start, CurrentScopeRange.end);

                    var previousScope = _currentScope;
                    _currentScope = new Scope(previousScope);
                    _scopeRanges.Push((classDecl.Start, classDecl.End));
                    
                    Declare("this", SymbolKind.Variable, classDecl.Name, classDecl.Start, classDecl.End, classDecl.Start, classDecl.End);

                    var previousParent = _currentParent;
                    _currentParent = classDecl.Name;

                    foreach (var field in classDecl.Fields)
                        AnalyzeExpr(field);
                    foreach (var method in classDecl.Methods)
                        AnalyzeExpr(method);
                    
                    _currentParent = previousParent;
                    _scopeRanges.Pop();
                    _currentScope = previousScope;
                }
                break;

            case StructDeclaration structDecl:
                Declare(structDecl.Name, SymbolKind.Class, structDecl.Name, structDecl.Start, structDecl.End, CurrentScopeRange.start, CurrentScopeRange.end);
                foreach (var fieldName in structDecl.Fields)
                {
                    Declare(fieldName, SymbolKind.Field, "unknown", structDecl.Start, structDecl.End, structDecl.Start, structDecl.End, structDecl.Name);
                }
                break;

            case EnumDeclaration enumDecl:
                Declare(enumDecl.Name, SymbolKind.Class, enumDecl.Name, enumDecl.Start, enumDecl.End, CurrentScopeRange.start, CurrentScopeRange.end);
                foreach (var memberName in enumDecl.Members)
                {
                    Declare(memberName, SymbolKind.Field, enumDecl.Name, enumDecl.Start, enumDecl.End, enumDecl.Start, enumDecl.End, enumDecl.Name);
                }
                break;

            case VariableExpr varExpr:
                if (_currentScope.Resolve(varExpr.Name) == null)
                {
                    throw new SemanticException($"Undefined variable '{varExpr.Name}'", varExpr.Start);
                }
                break;

            case AssignmentExpr assignExpr:
                AnalyzeExpr(assignExpr.Value);
                if (_currentScope.Resolve(assignExpr.Name) == null)
                {
                    throw new SemanticException($"Undefined variable '{assignExpr.Name}'", assignExpr.Start);
                }
                break;

            case BinaryExpr binaryExpr:
                AnalyzeExpr(binaryExpr.Left);
                AnalyzeExpr(binaryExpr.Right);
                break;

            case UnaryExpr unaryExpr:
                AnalyzeExpr(unaryExpr.Operand);
                break;

            case CallExpr callExpr:
                if (callExpr.Target != null)
                    AnalyzeExpr(callExpr.Target);
                else if (_currentScope.Resolve(callExpr.Name) == null)
                {
                    throw new SemanticException($"Undefined function '{callExpr.Name}'", callExpr.Start);
                }

                foreach (var arg in callExpr.Arguments)
                    AnalyzeExpr(arg);
                break;

            case IfStatement ifStmt:
                AnalyzeExpr(ifStmt.Condition);
                {
                    var previousScope = _currentScope;
                    _currentScope = new Scope(previousScope);
                    _scopeRanges.Push((ifStmt.Start, ifStmt.End)); // Simplified range for both branches
                    foreach (var stmt in ifStmt.ThenBlock) AnalyzeExpr(stmt);
                    _scopeRanges.Pop();
                    _currentScope = previousScope;

                    if (ifStmt.ElseBlock != null)
                    {
                         _currentScope = new Scope(previousScope);
                         _scopeRanges.Push((ifStmt.Start, ifStmt.End));
                         foreach (var stmt in ifStmt.ElseBlock) AnalyzeExpr(stmt);
                         _scopeRanges.Pop();
                         _currentScope = previousScope;
                    }
                }
                break;

            case WhileExpr whileExpr:
                AnalyzeExpr(whileExpr.Condition);
                {
                    var previousScope = _currentScope;
                    _currentScope = new Scope(previousScope);
                    _scopeRanges.Push((whileExpr.Start, whileExpr.End));
                    foreach (var stmt in whileExpr.Body) AnalyzeExpr(stmt);
                    _scopeRanges.Pop();
                    _currentScope = previousScope;
                }
                break;

            case ForStatement forStmt:
                {
                    var previousScope = _currentScope;
                    _currentScope = new Scope(previousScope);
                    _scopeRanges.Push((forStmt.Start, forStmt.End));
                    if (forStmt.Initializer != null) AnalyzeExpr(forStmt.Initializer);
                    if (forStmt.Condition != null) AnalyzeExpr(forStmt.Condition);
                    if (forStmt.Increment != null) AnalyzeExpr(forStmt.Increment);
                    foreach (var stmt in forStmt.Body) AnalyzeExpr(stmt);
                    _scopeRanges.Pop();
                    _currentScope = previousScope;
                }
                break;

            case ForeachStatement foreachStmt:
                {
                    var previousScope = _currentScope;
                    _currentScope = new Scope(previousScope);
                    _scopeRanges.Push((foreachStmt.Start, foreachStmt.End));
                    AnalyzeExpr(foreachStmt.Collection);
                    Declare(foreachStmt.VarName, SymbolKind.Variable, "unknown", foreachStmt.Start, foreachStmt.End, foreachStmt.Start, foreachStmt.End);
                    foreach (var stmt in foreachStmt.Body) AnalyzeExpr(stmt);
                    _scopeRanges.Pop();
                    _currentScope = previousScope;
                }
                break;

            case SwitchStatement switchStmt:
                AnalyzeExpr(switchStmt.Expression);
                foreach (var @case in switchStmt.Cases)
                {
                    var previousScope = _currentScope;
                    _currentScope = new Scope(previousScope);
                    _scopeRanges.Push((switchStmt.Start, switchStmt.End));
                    if (@case.Value != null) AnalyzeExpr(@case.Value);
                    foreach (var stmt in @case.Body) AnalyzeExpr(stmt);
                    _scopeRanges.Pop();
                    _currentScope = previousScope;
                }
                break;

            case ReturnStatement ret:
                if (ret.Value != null)
                    AnalyzeExpr(ret.Value);
                break;

            case MemberAccessExpr member:
                AnalyzeExpr(member.Object);
                break;
            
            case MemberAssignmentExpr memberAssign:
                AnalyzeExpr(memberAssign.Object);
                AnalyzeExpr(memberAssign.Value);
                break;

            case NewExpr newExpr:
                foreach (var arg in newExpr.Arguments)
                    AnalyzeExpr(arg);
                break;

            case ArrayAccessExpr arrayAccess:
                AnalyzeExpr(arrayAccess.Array);
                AnalyzeExpr(arrayAccess.Index);
                break;

            case ArrayExpr arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                    AnalyzeExpr(element);
                break;

            case ArrayStoreExpr arrayStore:
                AnalyzeExpr(arrayStore.Array);
                AnalyzeExpr(arrayStore.Index);
                AnalyzeExpr(arrayStore.Value);
                break;
        }
    }
}
