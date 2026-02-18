using System.Collections.Generic;
using SabakaLang.AST;
using SabakaIDE.Models;

namespace SabakaIDE.Services;

public class AstIndexer
{
    public SymbolTable Index(List<Expr> program)
    {
        var symbolTable = new SymbolTable();
        foreach (var expr in program)
        {
            IndexExpr(expr, symbolTable);
        }
        return symbolTable;
    }

    private void IndexExpr(Expr expr, SymbolTable symbolTable)
    {
        if (expr == null) return;

        switch (expr)
        {
            case FunctionDeclaration func:
                symbolTable.Add(new Symbol
                {
                    Name = func.Name,
                    Type = SymbolType.Function,
                    StartOffset = func.Start,
                    EndOffset = func.End,
                    DeclarationNode = func
                });
                foreach (var stmt in func.Body)
                {
                    IndexExpr(stmt, symbolTable);
                }
                break;

            case VariableDeclaration varDecl:
                symbolTable.Add(new Symbol
                {
                    Name = varDecl.Name,
                    Type = SymbolType.Variable,
                    StartOffset = varDecl.Start,
                    EndOffset = varDecl.End,
                    DeclarationNode = varDecl
                });
                if (varDecl.Initializer != null)
                {
                    IndexExpr(varDecl.Initializer, symbolTable);
                }
                break;

            case ClassDeclaration classDecl:
                symbolTable.Add(new Symbol
                {
                    Name = classDecl.Name,
                    Type = SymbolType.Class,
                    StartOffset = classDecl.Start,
                    EndOffset = classDecl.End,
                    DeclarationNode = classDecl
                });
                foreach (var field in classDecl.Fields)
                {
                    IndexExpr(field, symbolTable);
                }
                foreach (var method in classDecl.Methods)
                {
                    IndexExpr(method, symbolTable);
                }
                break;

            case StructDeclaration structDecl:
                symbolTable.Add(new Symbol
                {
                    Name = structDecl.Name,
                    Type = SymbolType.Struct,
                    StartOffset = structDecl.Start,
                    EndOffset = structDecl.End,
                    DeclarationNode = structDecl
                });
                break;

            case InterfaceDeclaration interfaceDecl:
                symbolTable.Add(new Symbol
                {
                    Name = interfaceDecl.Name,
                    Type = SymbolType.Interface,
                    StartOffset = interfaceDecl.Start,
                    EndOffset = interfaceDecl.End,
                    DeclarationNode = interfaceDecl
                });
                foreach (var method in interfaceDecl.Methods)
                {
                    IndexExpr(method, symbolTable);
                }
                break;

            case EnumDeclaration enumDecl:
                symbolTable.Add(new Symbol
                {
                    Name = enumDecl.Name,
                    Type = SymbolType.Enum,
                    StartOffset = enumDecl.Start,
                    EndOffset = enumDecl.End,
                    DeclarationNode = enumDecl
                });
                break;

            case IfStatement ifStmt:
                foreach (var stmt in ifStmt.ThenBlock) IndexExpr(stmt, symbolTable);
                if (ifStmt.ElseBlock != null)
                {
                    foreach (var stmt in ifStmt.ElseBlock) IndexExpr(stmt, symbolTable);
                }
                break;

            case WhileExpr whileExpr:
                foreach (var stmt in whileExpr.Body) IndexExpr(stmt, symbolTable);
                break;

            case ForStatement forStmt:
                if (forStmt.Initializer != null) IndexExpr(forStmt.Initializer, symbolTable);
                foreach (var stmt in forStmt.Body) IndexExpr(stmt, symbolTable);
                break;

            case ForeachStatement foreachStmt:
                foreach (var stmt in foreachStmt.Body) IndexExpr(stmt, symbolTable);
                break;

            case SwitchStatement switchStmt:
                foreach (var @case in switchStmt.Cases)
                {
                    foreach (var stmt in @case.Body) IndexExpr(stmt, symbolTable);
                }
                break;
        }
    }
}
