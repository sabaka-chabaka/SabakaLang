using SabakaLang.AST;
using SabakaLang.Lexer;

namespace SabakaLang.Compiler;

public class Compiler
{
    private readonly List<Instruction> _instructions = new();
    
    public List<Instruction> Compile(List<Expr> expressions)
    {
        foreach (var expr in expressions)
        {
            Emit(expr);
        }

        return _instructions;
    }

    private void Emit(Expr expr)
    {
        if (expr is NumberExpr num)
        {
            _instructions.Add(new Instruction(OpCode.Push, num.Value));
        }
        else if (expr is BinaryExpr bin)
        {
            Emit(bin.Left);
            Emit(bin.Right);

            switch (bin.Operator.Type)
            {
                case TokenType.Plus:
                    _instructions.Add(new Instruction(OpCode.Add));
                    break;

                case TokenType.Minus:
                    _instructions.Add(new Instruction(OpCode.Sub));
                    break;

                case TokenType.Star:
                    _instructions.Add(new Instruction(OpCode.Mul));
                    break;

                case TokenType.Slash:
                    _instructions.Add(new Instruction(OpCode.Div));
                    break;
            }
        }
        else if (expr is CallExpr call)
        {
            Emit(call.Argument);

            if (call.Name == "print")
            {
                _instructions.Add(new Instruction(OpCode.Print));
            }
            else
            {
                throw new Exception($"Unknown function '{call.Name}'");
            }
        }
        
        else if (expr is VariableDeclaration varDecl)
        {
            Emit(varDecl.Value);
            _instructions.Add(new Instruction(OpCode.Store, 0)
            {
                Name = varDecl.Name
            });
        }
        
        else if (expr is VariableExpr variable)
        {
            _instructions.Add(new Instruction(OpCode.Load)
            {
                Name = variable.Name
            });
        }
        else if (expr is IfStatement ifStmt)
        {
            Emit(ifStmt.Condition);

            var jumpIfFalseIndex = _instructions.Count;
            _instructions.Add(new Instruction(OpCode.JumpIfFalse));

            // then block
            foreach (var stmt in ifStmt.ThenBlock)
                Emit(stmt);

            if (ifStmt.ElseBlock != null)
            {
                var jumpIndex = _instructions.Count;
                _instructions.Add(new Instruction(OpCode.Jump));

                // patch jumpIfFalse
                _instructions[jumpIfFalseIndex].Operand =
                    _instructions.Count;

                foreach (var stmt in ifStmt.ElseBlock)
                    Emit(stmt);

                // patch jump
                _instructions[jumpIndex].Operand =
                    _instructions.Count;
            }
            else
            {
                _instructions[jumpIfFalseIndex].Operand =
                    _instructions.Count;
            }
        }
        else if (expr is AssignmentExpr assign)
        {
            Emit(assign.Value);

            _instructions.Add(new Instruction(OpCode.Store)
            {
                Name = assign.Name
            });
        }

    }
}
