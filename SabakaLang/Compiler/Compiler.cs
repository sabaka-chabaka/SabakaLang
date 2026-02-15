using SabakaLang.AST;
using SabakaLang.Lexer;
using SabakaLang.Types;

namespace SabakaLang.Compiler;

public class Compiler
{
    private readonly List<Instruction> _instructions = new();
    private Dictionary<string, int> _functions = new();
    private Dictionary<string, List<string>> _structs = new();
    private Dictionary<string, Dictionary<string, int>> _enums = new();


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
            var functionInstr = new Instruction(OpCode.Function);
            functionInstr.Name = func.Name;
            functionInstr.Extra = func.Parameters.Select(p => p.Name).ToList();

            _instructions.Add(functionInstr);
            var bodyStart = _instructions.Count;

            foreach (var stmt in func.Body)
                Emit(stmt);

            _instructions.Add(new Instruction(OpCode.Return));
            
            functionInstr.Operand = _instructions.Count;
            _functions[func.Name] = bodyStart;
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
                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.Print));
                return;
            }

            foreach (var arg in call.Arguments)
                Emit(arg);

            _instructions.Add(new Instruction(OpCode.Call, call.Arguments.Count)
            {
                Name = call.Name
            });
        }

        else if (expr is ArrayExpr arr)
        {
            foreach (var element in arr.Elements)
                Emit(element);

            _instructions.Add(new Instruction(OpCode.CreateArray, arr.Elements.Count));
        }

        else if (expr is ArrayAccessExpr access)
        {
            Emit(access.Array);
            Emit(access.Index);
            _instructions.Add(new Instruction(OpCode.ArrayLoad));
        }

        else if (expr is ArrayStoreExpr store)
        {
            Emit(store.Array);
            Emit(store.Index);
            Emit(store.Value);
            _instructions.Add(new Instruction(OpCode.ArrayStore));
        }

        else if (expr is MemberAccessExpr memberAccess)
        {
            if (memberAccess.Object is VariableExpr varExpr && _enums.ContainsKey(varExpr.Name))
            {
                var enumValues = _enums[varExpr.Name];
                if (!enumValues.ContainsKey(memberAccess.Member))
                    throw new Exception($"Unknown enum member {memberAccess.Member} in {varExpr.Name}");

                int value = enumValues[memberAccess.Member];
                _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(value)));
            }
            else
            {
                Emit(memberAccess.Object);
                _instructions.Add(new Instruction(OpCode.LoadField)
                {
                    Name = memberAccess.Member
                });
            }
        }

        else if (expr is ReturnStatement ret)
        {
            if (ret.Value != null)
                Emit(ret.Value);
            _instructions.Add(new Instruction(OpCode.Return));
        }

        else if (expr is EnumDeclaration enumDecl)
        {
            var values = new Dictionary<string, int>();

            for (int i = 0; i < enumDecl.Members.Count; i++)
            {
                values[enumDecl.Members[i]] = i;
            }

            _enums[enumDecl.Name] = values;
        }


        else if (expr is VariableDeclaration decl)
        {
            if (decl.Initializer != null)
            {
                Emit(decl.Initializer);
            }
            else
            {
                if (decl.CustomType != null && _structs.TryGetValue(decl.CustomType, out var fields))
                {
                    _instructions.Add(new Instruction(OpCode.CreateStruct)
                    {
                        Name = decl.CustomType,
                        Extra = fields
                    });
                }
                else
                {
                    _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(0)));
                }
            }

            var instr = new Instruction(OpCode.Declare);
            instr.Name = decl.Name;
            _instructions.Add(instr);
        }
        else if (expr is MemberAssignmentExpr assignmentExpr)
        {
            Emit(assignmentExpr.Object);
            Emit(assignmentExpr.Value);

            _instructions.Add(new Instruction(OpCode.StoreField)
            {
                Name = assignmentExpr.Member
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
        else if (expr is ForStatement forStmt)
        {
            _instructions.Add(new Instruction(OpCode.EnterScope));

            // init
            if (forStmt.Initializer != null)
                Emit(forStmt.Initializer);

            int loopStart = _instructions.Count;

            // condition
            if (forStmt.Condition != null)
                Emit(forStmt.Condition);
            else
                _instructions.Add(new Instruction(OpCode.Push, Value.FromBool(true)));

            var jumpIfFalse = new Instruction(OpCode.JumpIfFalse, 0);
            _instructions.Add(jumpIfFalse);

            // body
            foreach (var stmt in forStmt.Body)
                Emit(stmt);

            // increment
            if (forStmt.Increment != null)
                Emit(forStmt.Increment);

            _instructions.Add(new Instruction(OpCode.Jump, loopStart));

            jumpIfFalse.Operand = _instructions.Count;

            _instructions.Add(new Instruction(OpCode.ExitScope));
        }   
        else if (expr is ForeachStatement fe)
        {
            _instructions.Add(new Instruction(OpCode.EnterScope));

            // index = 0
            string indexName = "__index" + _instructions.Count;

            _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(0)));
            _instructions.Add(new Instruction(OpCode.Declare) { Name = indexName });

            int loopStart = _instructions.Count;

            // index < array.length
            _instructions.Add(new Instruction(OpCode.Load) { Name = indexName });
            Emit(fe.Collection);
            _instructions.Add(new Instruction(OpCode.ArrayLength));
            _instructions.Add(new Instruction(OpCode.Less));

            var jumpIfFalse = new Instruction(OpCode.JumpIfFalse, 0);
            _instructions.Add(jumpIfFalse);

            // BODY
            _instructions.Add(new Instruction(OpCode.EnterScope));

            // x = array[index]
            Emit(fe.Collection);
            _instructions.Add(new Instruction(OpCode.Load) { Name = indexName });
            _instructions.Add(new Instruction(OpCode.ArrayLoad));

            _instructions.Add(new Instruction(OpCode.Declare)
            {
                Name = fe.VarName
            });

            foreach (var stmt in fe.Body)
                Emit(stmt);

            _instructions.Add(new Instruction(OpCode.ExitScope));

            // index++
            _instructions.Add(new Instruction(OpCode.Load) { Name = indexName });
            _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(1)));
            _instructions.Add(new Instruction(OpCode.Add));
            _instructions.Add(new Instruction(OpCode.Store) { Name = indexName });

            _instructions.Add(new Instruction(OpCode.Jump, loopStart));

            jumpIfFalse.Operand = _instructions.Count;

            _instructions.Add(new Instruction(OpCode.ExitScope));
        }
        else if (expr is StructDeclaration sd)
        {
            _structs[sd.Name] = sd.Fields;
        }

    }
}