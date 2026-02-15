using SabakaLang.AST;
using SabakaLang.Lexer;
using SabakaLang.Types;

namespace SabakaLang.Compiler;

public class Compiler
{
    private readonly List<Instruction> _instructions = new();
    private Dictionary<string, int> _functions = new();


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
        if (expr is IntExpr intExpr)
        {
            _instructions.Add(
                new Instruction(OpCode.Push, Value.FromInt(intExpr.Value))
            );
        }
        else if (expr is FloatExpr f)
        {
            _instructions.Add(
                new Instruction(OpCode.Push, Value.FromFloat(f.Value))
            );
        }
        else if (expr is FunctionDeclaration func)
        {
            var funcStart = new Instruction(OpCode.Function)
            {
                Name = func.Name
            };
            _instructions.Add(funcStart);

            _functions[func.Name] = _instructions.Count;

            // Emit parameters as declarations from arguments on stack
            // Arguments are pushed in order, so they are in reverse on stack?
            // foo(1, 2) -> Push 1, Push 2. Stack: [1, 2].
            // To get 1 and then 2, we need to pop 2 then 1.
            for (int i = func.Parameters.Count - 1; i >= 0; i--)
            {
                var param = func.Parameters[i];
                var instr = new Instruction(OpCode.Declare) { Name = param.Name };
                _instructions.Add(instr);
            }

            foreach (var stmt in func.Body)
                Emit(stmt);

            _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(0)));
            _instructions.Add(new Instruction(OpCode.Return));

            funcStart.Operand = _instructions.Count;
        }

        else if (expr is BinaryExpr bin)
        {
            if (bin.Operator == TokenType.OrOr)
            {
                Emit(bin.Left);
                var jumpIfTrue = new Instruction(OpCode.JumpIfTrue, 0);
                _instructions.Add(jumpIfTrue);

                Emit(bin.Right);
                var endJump = new Instruction(OpCode.Jump, 0);
                _instructions.Add(endJump);

                jumpIfTrue.Operand = _instructions.Count;
                _instructions.Add(new Instruction(OpCode.Push, Value.FromBool(true)));

                endJump.Operand = _instructions.Count;
                return;
            }

            if (bin.Operator == TokenType.AndAnd)
            {
                Emit(bin.Left);
                var jumpIfFalse = new Instruction(OpCode.JumpIfFalse, 0);
                _instructions.Add(jumpIfFalse);

                Emit(bin.Right);
                var endJump = new Instruction(OpCode.Jump, 0);
                _instructions.Add(endJump);

                jumpIfFalse.Operand = _instructions.Count;
                _instructions.Add(new Instruction(OpCode.Push, Value.FromBool(false)));

                endJump.Operand = _instructions.Count;
                return;
            }

            Emit(bin.Left);
            Emit(bin.Right);

            switch (bin.Operator)
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

                case TokenType.EqualEqual:
                    _instructions.Add(new Instruction(OpCode.Equal));
                    break;

                case TokenType.NotEqual:
                    _instructions.Add(new Instruction(OpCode.NotEqual));
                    break;

                case TokenType.Greater:
                    _instructions.Add(new Instruction(OpCode.Greater));
                    break;

                case TokenType.Less:
                    _instructions.Add(new Instruction(OpCode.Less));
                    break;

                case TokenType.GreaterEqual:
                    _instructions.Add(new Instruction(OpCode.GreaterEqual));
                    break;

                case TokenType.LessEqual:
                    _instructions.Add(new Instruction(OpCode.LessEqual));
                    break;
            }
        }
        else if (expr is CallExpr call)
        {
            if (call.Name == "print")
            {
                if (call.Argument != null)
                    Emit(call.Argument);
                _instructions.Add(new Instruction(OpCode.Print));
                return;
            }

            if (call.Argument != null)
                Emit(call.Argument);

            _instructions.Add(new Instruction(OpCode.Call)
            {
                Name = call.Name
            });
        }

        else if (expr is ReturnStatement ret)
        {
            if (ret.Value != null)
                Emit(ret.Value);
            _instructions.Add(new Instruction(OpCode.Return));
        }


        else if (expr is VariableDeclaration decl)
        {
            Emit(decl.Initializer);

            var instr = new Instruction(OpCode.Declare);
            instr.Name = decl.Name;
            _instructions.Add(instr);
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
            _instructions.Add(new Instruction(OpCode.EnterScope));
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

            _instructions.Add(new Instruction(OpCode.ExitScope));
        }
        else if (expr is AssignmentExpr assign)
        {
            Emit(assign.Value);

            _instructions.Add(new Instruction(OpCode.Store)
            {
                Name = assign.Name
            });
        }
        else if (expr is WhileExpr whileExpr)
        {
            int loopStart = _instructions.Count;

            Emit(whileExpr.Condition);

            var jumpIfFalse = new Instruction(OpCode.JumpIfFalse, 0);
            _instructions.Add(jumpIfFalse);
            _instructions.Add(new Instruction(OpCode.EnterScope));

            foreach (var e in whileExpr.Body)
                Emit(e);

            _instructions.Add(new Instruction(OpCode.ExitScope));

            
            _instructions.Add(new Instruction(OpCode.Jump, loopStart));

            jumpIfFalse.Operand = _instructions.Count;
        }
        else if (expr is UnaryExpr unary)
        {
            Emit(unary.Operand);

            if (unary.Operator == TokenType.Minus)
                _instructions.Add(new Instruction(OpCode.Negate));

            else if (unary.Operator == TokenType.Bang)
                _instructions.Add(new Instruction(OpCode.Not));
        }

        else if (expr is BoolExpr b)
        {
            _instructions.Add(
                new Instruction(OpCode.Push, Value.FromBool(b.Value))
            );
        }
        else if (expr is StringExpr s)
        {
            _instructions.Add(
                new Instruction(OpCode.Push, Value.FromString(s.Value))
            );
        }

    }
}