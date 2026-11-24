using SabakaLang.AST;
using SabakaLang.AST.Interfaces;

namespace SabakaLang.LanguageServer;

/// <summary>Pre-order walker over statements/expressions (including interpolated string parts).</summary>
public abstract class AstWalker
{
    protected virtual void OnStmt(IStmt s) { }
    protected virtual void OnExpr(IExpr e) { }
    protected virtual void OnType(TypeRef t) { }

    public void WalkAll(IEnumerable<IStmt> stmts) => Body(stmts);

    protected void Body(IEnumerable<IStmt>? stmts)
    {
        if (stmts is null) return;
        foreach (var s in stmts) Stmt(s);
    }

    protected void Ty(TypeRef? t)
    {
        if (t is not null) OnType(t);
    }

    protected void Stmt(IStmt? s)
    {
        if (s is null) return;
        OnStmt(s);
        switch (s)
        {
            case VarDecl v: Ty(v.Type); Expr(v.Init); break;
            case ConstDecl cd: Ty(cd.Type); Expr(cd.Value); break;
            case FuncDecl fd:
                Ty(fd.ReturnType);
                foreach (var p in fd.Params) Ty(p.Type);
                Body(fd.Body);
                break;
            case ClassDecl cl: Body(cl.Fields); Body(cl.Methods); break;
            case InterfaceDecl id: Body(id.Methods); break;
            case StructDecl sd: Body(sd.Fields); break;
            case ExprStmt es: Expr(es.Expr); break;
            case ReturnStmt rs: Expr(rs.Value); break;
            case IfStmt ifs: Expr(ifs.Condition); Body(ifs.Then); Body(ifs.Else); break;
            case WhileStmt ws: Expr(ws.Condition); Body(ws.Body); break;
            case ForStmt fs: Stmt(fs.Init); Expr(fs.Condition); Expr(fs.Step); Body(fs.Body); break;
            case ForeachStmt fe: Ty(fe.ItemType); Expr(fe.Collection); Body(fe.Body); break;
            case SwitchStmt sw:
                Expr(sw.Value);
                foreach (var sc in sw.Cases) { Expr(sc.Value); Body(sc.Body); }
                break;
        }
    }

    protected void Expr(IExpr? e)
    {
        if (e is null) return;
        OnExpr(e);
        switch (e)
        {
            case BinaryExpr b: Expr(b.Left); Expr(b.Right); break;
            case UnaryExpr u: Expr(u.Operand); break;
            case CallExpr c: Expr(c.Callee); foreach (var a in c.Args) Expr(a); break;
            case MemberExpr m: Expr(m.Object); break;
            case IndexExpr ix: Expr(ix.Object); Expr(ix.Index); break;
            case ArrayExpr arr: foreach (var el in arr.Elements) Expr(el); break;
            case NewExpr n: foreach (var a in n.Args) Expr(a); break;
            case AssignExpr asg: Expr(asg.Target); Expr(asg.Value); break;
            case TernaryExpr t: Expr(t.Condition); Expr(t.Then); Expr(t.Else); break;
            case CoalesceExpr co: Expr(co.Left); Expr(co.Right); break;
            case IsExpr isx: Expr(isx.Left); Ty(isx.Right); break;
            case NewArrayExpr na: Ty(na.ElementType); Expr(na.Size); break;
            case InterpolatedStringExpr ie: foreach (var part in ie.Parts) Expr(part); break;
        }
    }
}
