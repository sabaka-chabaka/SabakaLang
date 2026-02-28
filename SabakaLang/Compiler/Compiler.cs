using SabakaLang.AST;
using SabakaLang.Exceptions;
using SabakaLang.Lexer;
using SabakaLang.Types;
using System.Reflection;

namespace SabakaLang.Compiler;

public class Compiler
{
    private readonly List<Instruction> _instructions = new();
    private Dictionary<string, int> _functions = new();
    private Dictionary<string, List<string>> _structs = new();
    private Dictionary<string, Dictionary<string, int>> _enums = new();
    private Dictionary<string, ClassDeclaration> _classes = new();
    private Dictionary<string, InterfaceDeclaration> _interfaces = new();
    private string? _currentClass = null;
    private Stack<Dictionary<string, string>> _typeScopes = new();
    private HashSet<string> _importedFiles = new();
    private string? _currentFilePath;
    private readonly object _lock = new();
    private List<(FunctionDeclaration func, string? className)> _functionsToCompile = new();
    private bool _isPreScan = false;
    private readonly Dictionary<string, (int ParamCount, Func<Value[], Value> Delegate)> _externalFunctions = new();
    private readonly Dictionary<string, Value> _externalVariables = new(); // For storing imported variables

    private readonly
        Dictionary<string, (object Instance, Dictionary<string, (int ParamCount, Func<Value[], Value> Delegate)> Methods
            )> _externalClasses = new();

    public IReadOnlyDictionary<string, Func<Value[], Value>> ExternalDelegates =>
        _externalFunctions
            .ToDictionary(kv => kv.Key, kv => kv.Value.Delegate)
            .Concat(
                _externalClasses.SelectMany(cls =>
                    cls.Value.Methods.Select(m =>
                        new KeyValuePair<string, Func<Value[], Value>>(
                            $"{cls.Key}.{m.Key}", m.Value.Delegate)))
            )
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    private static readonly string _executableDirectory;

    static Compiler()
    {
        string? location = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(location))
            _executableDirectory = Path.GetDirectoryName(location) ?? AppDomain.CurrentDomain.BaseDirectory;
        else
            _executableDirectory = AppDomain.CurrentDomain.BaseDirectory;
    }

    private void PushScope() => _typeScopes.Push(new Dictionary<string, string>());
    private void PopScope() => _typeScopes.Pop();
    private void DeclareVar(string name, string type) => _typeScopes.Peek()[name] = type;

    private string? GetVarType(string name)
    {
        foreach (var scope in _typeScopes)
        {
            if (scope.TryGetValue(name, out var type)) return type;
        }

        return null;
    }

    public List<Instruction> Compile(List<Expr> expressions, string? filePath = null)
    {
        _currentFilePath = filePath;
        _importedFiles.Clear();
        if (_currentFilePath != null)
            _importedFiles.Add(_currentFilePath);

        _typeScopes.Clear();
        PushScope(); // Global scope

        _functionsToCompile.Clear();
        
        // Phase 1: Pre-scan for declarations (sequential)
        _isPreScan = true;
        foreach (var expr in expressions)
        {
            Emit(expr);
        }
        _isPreScan = false;

        // Phase 2: Compile global code (sequential)
        foreach (var expr in expressions)
        {
            if (expr is not FunctionDeclaration && expr is not ClassDeclaration && expr is not InterfaceDeclaration && expr is not EnumDeclaration && expr is not StructDeclaration)
            {
                Emit(expr);
            }
        }

        // Phase 3: Parallel compilation of function/method bodies
        var results = new System.Collections.Concurrent.ConcurrentBag<(string Name, List<Instruction> Bytecode, int StartAddr)>();
        
        System.Threading.Tasks.Parallel.ForEach(_functionsToCompile, item =>
        {
            var localCompiler = CreateLocalCompiler();
            var bytecode = localCompiler.CompileIsolated(item.func, item.className);
            lock(_lock)
            {
                results.Add((item.className != null ? $"{item.className}.{item.func.Name}" : item.func.Name, bytecode, -1));
            }
        });

        foreach (var res in results)
        {
            var bodyStart = _instructions.Count;
            var instr = new Instruction(OpCode.Function)
            {
                Name = res.Name,
                Extra = _functionsToCompile.First(f => (f.className != null ? $"{f.className}.{f.func.Name}" : f.func.Name) == res.Name).func.Parameters.Select(p => p.Name).ToList()
            };
            _instructions.Add(instr);
            var actualStart = _instructions.Count;
            _instructions.AddRange(res.Bytecode);
            _instructions.Add(new Instruction(OpCode.Return));
            instr.Operand = _instructions.Count;
            _functions[res.Name] = actualStart;
        }

        return _instructions;
    }

    private Compiler CreateLocalCompiler()
    {
        var c = new Compiler();
        c._classes = _classes;
        c._interfaces = _interfaces;
        c._functions = _functions;
        c._structs = _structs;
        c._enums = _enums;
        c._externalFunctions.Clear();
        foreach (var kv in _externalFunctions) c._externalFunctions[kv.Key] = kv.Value;
        c._externalClasses.Clear();
        foreach (var kv in _externalClasses) c._externalClasses[kv.Key] = kv.Value;
        c._externalVariables.Clear();
        foreach (var kv in _externalVariables) c._externalVariables[kv.Key] = kv.Value;
        return c;
    }

    private List<Instruction> CompileIsolated(FunctionDeclaration func, string? className)
    {
        _currentClass = className;
        PushScope();
        if (className != null)
        {
            foreach (var field in GetAllFieldsFull(className))
                DeclareVar(field.Name, field.CustomType ?? field.TypeToken.ToString());
        }
        foreach (var p in func.Parameters)
            DeclareVar(p.Name, p.CustomType ?? p.Type.ToString());

        foreach (var stmt in func.Body)
            Emit(stmt);

        PopScope();
        return _instructions;
    }

    private void Emit(Expr expr)
    {
        if (_isPreScan)
        {
            if (expr is ImportStatement import) HandleImport(import);
            else if (expr is ClassDeclaration cd)
            {
                _classes[cd.Name] = cd;
                string? actualBaseClass = null;
                if (cd.BaseClassName != null)
                {
                    if (_interfaces.ContainsKey(cd.BaseClassName)) { } // handled elsewhere
                    else actualBaseClass = cd.BaseClassName;
                }
                if (actualBaseClass != null)
                    _instructions.Add(new Instruction(OpCode.Inherit) { Name = cd.Name, Operand = actualBaseClass });
                
                foreach (var method in cd.Methods) _functionsToCompile.Add((method, cd.Name));
            }
            else if (expr is FunctionDeclaration fd) _functionsToCompile.Add((fd, null));
            else if (expr is InterfaceDeclaration id) _interfaces[id.Name] = id;
            else if (expr is EnumDeclaration ed) 
            {
                var vals = new Dictionary<string, int>();
                for (int i = 0; i < ed.Members.Count; i++) vals[ed.Members[i]] = i;
                _enums[ed.Name] = vals;
            }
            else if (expr is StructDeclaration sd)
            {
                _structs[sd.Name] = sd.Fields;
            }
            return;
        }

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
        else if (expr is ImportStatement import)
        {
            HandleImport(import);
        }
        else if (expr is ClassDeclaration classDecl)
        {
            if (!_isPreScan) return;
            
            _classes[classDecl.Name] = classDecl;

            string? actualBaseClass = null;
            var allInterfaces = new List<string>(classDecl.Interfaces);

            if (classDecl.BaseClassName != null)
            {
                if (_interfaces.ContainsKey(classDecl.BaseClassName))
                {
                    allInterfaces.Add(classDecl.BaseClassName);
                }
                else
                {
                    actualBaseClass = classDecl.BaseClassName;
                }
            }

            if (actualBaseClass != null)
            {
                _instructions.Add(new Instruction(OpCode.Inherit)
                {
                    Name = classDecl.Name,
                    Operand = actualBaseClass
                });
            }

            foreach (var method in classDecl.Methods)
            {
                _functionsToCompile.Add((method, classDecl.Name));
            }
        }
        else if (expr is InterfaceDeclaration interfaceDecl)
        {
            _interfaces[interfaceDecl.Name] = interfaceDecl;
        }
        else if (expr is NewExpr newExpr)
        {
            if (_externalClasses.ContainsKey(newExpr.ClassName.ToLower()))
            {
                _instructions.Add(new Instruction(OpCode.Push,
                    Value.FromString($"__extern__{newExpr.ClassName.ToLower()}")));
                return;
            }

            var fields = GetAllFields(newExpr.ClassName);
            var cd = _classes.GetValueOrDefault(newExpr.ClassName);

            _instructions.Add(new Instruction(OpCode.CreateObject)
            {
                Name = newExpr.ClassName,
                Extra = fields
            });

            bool hasConstructor = cd != null && cd.Methods.Any(m => m.Name == newExpr.ClassName);
            if (!hasConstructor && cd?.BaseClassName != null)
            {
                // check base class for constructor?
                // For now let's assume constructors are not automatically inherited or we just check the chain
                hasConstructor = HasConstructorInChain(newExpr.ClassName);
            }

            if (hasConstructor)
            {
                _instructions.Add(new Instruction(OpCode.Dup));
                foreach (var arg in newExpr.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.CallMethod, newExpr.Arguments.Count)
                {
                    Name = newExpr.ClassName // This will be resolved by VM lookup
                });
                _instructions.Add(new Instruction(OpCode.Pop));
            }
            else if (newExpr.Arguments.Count > 0)
            {
                throw new CompilerException($"Class {newExpr.ClassName} does not have a constructor", 0);
            }
        }
        else if (expr is FunctionDeclaration func)
        {
            if (!_isPreScan) return;
            _functionsToCompile.Add((func, null));
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
            if (call.Target == null && call.Name == "print")
            {
                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.Print));
                return;
            }

            if (call.Target == null && call.Name == "sleep")
            {
                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.Sleep));
                return;
            }

            if (call.Target == null && call.Name == "input")
            {
                _instructions.Add(new Instruction(OpCode.Input));
                return;
            }

            if (call.Target == null && _externalFunctions.TryGetValue(call.Name, out var extInfo))
            {
                if (call.Arguments.Count != extInfo.ParamCount)
                    throw new CompilerException(
                        $"External function '{call.Name}' expects {extInfo.ParamCount} arguments, got {call.Arguments.Count}",
                        0);

                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.CallExternal, call.Arguments.Count)
                {
                    Name = call.Name
                });
                return;
            }

            if (call.Target == null && _classes.TryGetValue(call.Name, out var cDecl))
            {
                _instructions.Add(new Instruction(OpCode.CreateObject)
                {
                    Name = call.Name,
                    Extra = GetAllFields(call.Name)
                });

                bool hasConstructor = cDecl.Methods.Any(m => m.Name == call.Name);
                if (hasConstructor)
                {
                    _instructions.Add(new Instruction(OpCode.Dup));
                    foreach (var arg in call.Arguments)
                        Emit(arg);

                    _instructions.Add(new Instruction(OpCode.CallMethod, call.Arguments.Count)
                    {
                        Name = call.Name
                    });
                    _instructions.Add(new Instruction(OpCode.Pop));
                }
                else if (call.Arguments.Count > 0)
                {
                    throw new CompilerException($"Class {call.Name} does not have a constructor", 0);
                }

                return;
            }

            if (call.Target is SuperExpr)
            {
                if (_currentClass == null)
                    throw new CompilerException("super::member can only be used inside a class", 0);

                var baseClass = _classes[_currentClass].BaseClassName;
                if (baseClass == null)
                    throw new CompilerException($"Class {_currentClass} does not have a base class", 0);

                _instructions.Add(new Instruction(OpCode.PushThis));
                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.CallMethod, call.Arguments.Count)
                {
                    Name = call.Name,
                    Extra = baseClass
                });
                return;
            }

            if (call.Target != null)
            {
                string? objType = null;
                if (call.Target is VariableExpr ve) objType = GetVarType(ve.Name);
                else if (call.Target is NewExpr ne) objType = ne.ClassName;
                else if (call.Target is SuperExpr)
                    objType = (_currentClass != null && _classes.TryGetValue(_currentClass, out var cd))
                        ? cd.BaseClassName
                        : null;

                if (objType != null)
                {
                    var method = GetMethodInChain(objType, call.Name, call.Arguments.Count);
                    if (method != null)
                    {
                        string definingClass = GetDefiningClassForMethod(objType, call.Name, call.Arguments.Count);
                        CheckAccess(definingClass, method.AccessModifier, "method", method.Name);
                    }
                }

                if (call.Target is VariableExpr veCheck)
                {
                    var varType = GetVarType(veCheck.Name)?.ToLower();
                    if (varType != null && _externalClasses.TryGetValue(varType, out var extClass))
                    {
                        string extKey = $"{varType}.{call.Name.ToLower()}";
                        if (extClass.Methods.ContainsKey(call.Name.ToLower()))
                        {
                            foreach (var arg in call.Arguments)
                                Emit(arg);

                            _instructions.Add(new Instruction(OpCode.CallExternal, call.Arguments.Count)
                            {
                                Name = extKey
                            });
                            return;
                        }
                    }
                }

                Emit(call.Target);
                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.CallMethod, call.Arguments.Count)
                {
                    Name = call.Name
                });
            }
            else if (_currentClass != null && GetMethodInChain(_currentClass, call.Name, call.Arguments.Count) != null)
            {
                var method = GetMethodInChain(_currentClass, call.Name, call.Arguments.Count)!;
                string definingClass = GetDefiningClassForMethod(_currentClass, call.Name, call.Arguments.Count);
                CheckAccess(definingClass, method.AccessModifier, "method", method.Name);

                // Implicit this call
                _instructions.Add(new Instruction(OpCode.PushThis));
                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.CallMethod, call.Arguments.Count)
                {
                    Name = call.Name
                });
            }
            else
            {
                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.Call, call.Arguments.Count)
                {
                    Name = call.Name
                });
            }
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
                    throw new CompilerException($"Unknown enum member {memberAccess.Member} in {varExpr.Name}", 0);

                int value = enumValues[memberAccess.Member];
                _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(value)));
            }
            else
            {
                string? objType = null;
                if (memberAccess.Object is VariableExpr ve) objType = GetVarType(ve.Name);
                else if (memberAccess.Object is NewExpr ne) objType = ne.ClassName;
                else if (memberAccess.Object is SuperExpr)
                    objType = (_currentClass != null && _classes.TryGetValue(_currentClass, out var cd))
                        ? cd.BaseClassName
                        : null;

                if (objType != null)
                {
                    var field = GetFieldInChain(objType, memberAccess.Member);
                    if (field != null)
                    {
                        string definingClass = GetDefiningClassForField(objType, memberAccess.Member);
                        CheckAccess(definingClass, field.AccessModifier, "field", field.Name);
                    }
                }

                Emit(memberAccess.Object);
                _instructions.Add(new Instruction(OpCode.LoadField)
                {
                    Name = memberAccess.Member
                });
            }
        }

        else if (expr is SuperExpr)
        {
            if (_currentClass == null)
                throw new CompilerException("super can only be used inside a class", 0);
            _instructions.Add(new Instruction(OpCode.PushThis));
        }
        else if (expr is ReturnStatement ret)
        {
            if (ret.Value != null)
                Emit(ret.Value);
            _instructions.Add(new Instruction(OpCode.Return));
        }
        else if (expr is SpawnExpr spawn)
        {
            _instructions.Add(new Instruction(OpCode.SpawnThread, spawn.FunctionName));
        }
        else if (expr is JoinExpr join)
        {
            Emit(join.ThreadHandle);
            _instructions.Add(new Instruction(OpCode.JoinThread));
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
                else if (decl.CustomType != null && _classes.TryGetValue(decl.CustomType, out var cDecl2))
                {
                    _instructions.Add(new Instruction(OpCode.CreateObject)
                    {
                        Name = decl.CustomType,
                        Extra = GetAllFields(decl.CustomType)
                    });

                    bool hasConstructor = HasConstructorInChain(decl.CustomType);
                    if (hasConstructor)
                    {
                        _instructions.Add(new Instruction(OpCode.Dup));
                        _instructions.Add(new Instruction(OpCode.CallMethod, 0)
                        {
                            Name = decl.CustomType
                        });
                        _instructions.Add(new Instruction(OpCode.Pop));
                    }
                }
                else if (decl.CustomType != null && _externalClasses.ContainsKey(decl.CustomType.ToLower()))
                {
                    if (decl.Initializer != null)
                        Emit(decl.Initializer);
                    else
                        _instructions.Add(new Instruction(OpCode.Push,
                            Value.FromString($"__extern__{decl.CustomType.ToLower()}")));

                    _instructions.Add(new Instruction(OpCode.Declare) { Name = decl.Name });
                    DeclareVar(decl.Name, decl.CustomType.ToLower());
                    return;
                }
                else
                {
                    _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(0)));
                }
            }

            var instr = new Instruction(OpCode.Declare);
            instr.Name = decl.Name;
            _instructions.Add(instr);

            DeclareVar(decl.Name, decl.CustomType ?? decl.TypeToken.ToString());
        }
        else if (expr is MemberAssignmentExpr assignmentExpr)
        {
            string? objType = null;
            if (assignmentExpr.Object is VariableExpr ve) objType = GetVarType(ve.Name);
            else if (assignmentExpr.Object is NewExpr ne) objType = ne.ClassName;
            else if (assignmentExpr.Object is SuperExpr)
                objType = (_currentClass != null && _classes.TryGetValue(_currentClass, out var cd))
                    ? cd.BaseClassName
                    : null;

            if (objType != null)
            {
                var field = GetFieldInChain(objType, assignmentExpr.Member);
                if (field != null)
                {
                    string definingClass = GetDefiningClassForField(objType, assignmentExpr.Member);
                    CheckAccess(definingClass, field.AccessModifier, "field", field.Name);
                }
            }

            Emit(assignmentExpr.Object);
            Emit(assignmentExpr.Value);

            _instructions.Add(new Instruction(OpCode.StoreField)
            {
                Name = assignmentExpr.Member
            });
        }


        else if (expr is VariableExpr variable)
        {
            if (_currentClass != null)
            {
                var field = GetFieldInChain(_currentClass, variable.Name);
                if (field != null)
                {
                    string definingClass = GetDefiningClassForField(_currentClass, variable.Name);
                    CheckAccess(definingClass, field.AccessModifier, "field", field.Name);
                }
            }

            // Check if this is an external variable from an imported DLL
            if (_externalVariables.TryGetValue(variable.Name.ToLower(), out var externalValue))
            {
                _instructions.Add(new Instruction(OpCode.Push, externalValue));
            }
            else
            {
                _instructions.Add(new Instruction(OpCode.Load)
                {
                    Name = variable.Name
                });
            }
        }
        else if (expr is IfStatement ifStmt)
        {
            _instructions.Add(new Instruction(OpCode.EnterScope));
            PushScope();
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
            PopScope();
        }
        else if (expr is SwitchStatement switchStmt)
        {
            _instructions.Add(new Instruction(OpCode.EnterScope));
            PushScope();

            string switchVarName = "$switch_val_" + _instructions.Count;
            Emit(switchStmt.Expression);
            _instructions.Add(new Instruction(OpCode.Declare) { Name = switchVarName });
            DeclareVar(switchVarName, "object");

            var caseEndJumps = new List<int>();
            SwitchCase? defaultCase = null;

            foreach (var @case in switchStmt.Cases)
            {
                if (@case.Value == null)
                {
                    defaultCase = @case;
                    continue;
                }

                _instructions.Add(new Instruction(OpCode.Load) { Name = switchVarName });
                Emit(@case.Value);
                _instructions.Add(new Instruction(OpCode.Equal));

                var nextCaseJumpIndex = _instructions.Count;
                _instructions.Add(new Instruction(OpCode.JumpIfFalse));

                foreach (var stmt in @case.Body)
                    Emit(stmt);

                caseEndJumps.Add(_instructions.Count);
                _instructions.Add(new Instruction(OpCode.Jump));

                _instructions[nextCaseJumpIndex].Operand = _instructions.Count;
            }

            if (defaultCase != null)
            {
                foreach (var stmt in defaultCase.Body)
                    Emit(stmt);
            }

            foreach (var jumpIndex in caseEndJumps)
            {
                _instructions[jumpIndex].Operand = _instructions.Count;
            }

            _instructions.Add(new Instruction(OpCode.ExitScope));
            PopScope();
        }
        else if (expr is AssignmentExpr assign)
        {
            if (_currentClass != null)
            {
                var field = GetFieldInChain(_currentClass, assign.Name);
                if (field != null)
                {
                    string definingClass = GetDefiningClassForField(_currentClass, assign.Name);
                    CheckAccess(definingClass, field.AccessModifier, "field", field.Name);
                }
            }

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
            PushScope();

            foreach (var e in whileExpr.Body)
                Emit(e);

            _instructions.Add(new Instruction(OpCode.ExitScope));
            PopScope();


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
            PushScope();

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
            PopScope();
        }
        else if (expr is ForeachStatement fe)
        {
            _instructions.Add(new Instruction(OpCode.EnterScope));
            PushScope();

            // index = 0
            string indexName = "__index" + _instructions.Count;

            _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(0)));
            _instructions.Add(new Instruction(OpCode.Declare) { Name = indexName });
            DeclareVar(indexName, "int");

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
            PushScope();

            // x = array[index]
            Emit(fe.Collection);
            _instructions.Add(new Instruction(OpCode.Load) { Name = indexName });
            _instructions.Add(new Instruction(OpCode.ArrayLoad));

            _instructions.Add(new Instruction(OpCode.Declare)
            {
                Name = fe.VarName
            });
            // We don't know the exact element type easily here, but we can assume something or just skip for now.
            // If fe.Collection is an array, we'd need to know its element type.
            // Since we don't have full type inference yet, let's just use "object" as placeholder or skip.
            // But wait, if it's an array of class objects, we'd want to know.

            // For now let's just declare it without type info if we can't find it.
            DeclareVar(fe.VarName, "object");

            foreach (var stmt in fe.Body)
                Emit(stmt);

            _instructions.Add(new Instruction(OpCode.ExitScope));
            PopScope();

            // index++
            _instructions.Add(new Instruction(OpCode.Load) { Name = indexName });
            _instructions.Add(new Instruction(OpCode.Push, Value.FromInt(1)));
            _instructions.Add(new Instruction(OpCode.Add));
            _instructions.Add(new Instruction(OpCode.Store) { Name = indexName });

            _instructions.Add(new Instruction(OpCode.Jump, loopStart));

            jumpIfFalse.Operand = _instructions.Count;

            _instructions.Add(new Instruction(OpCode.ExitScope));
            PopScope();
        }
        else if (expr is StructDeclaration sd)
        {
            _structs[sd.Name] = sd.Fields;
        }
    }

    private List<FunctionDeclaration> GetAllInterfaceMethods(string interfaceName)
    {
        if (!_interfaces.TryGetValue(interfaceName, out var iface))
        {
            throw new CompilerException($"Interface {interfaceName} not found", 0);
        }

        var methods = new List<FunctionDeclaration>(iface.Methods);
        foreach (var parent in iface.Parents)
        {
            methods.AddRange(GetAllInterfaceMethods(parent));
        }

        return methods;
    }

    private bool HasMethodInChain(string className, string methodName, int paramCount)
    {
        var cd = _classes.GetValueOrDefault(className);
        if (cd == null) return false;

        if (cd.Methods.Any(m => m.Name == methodName && m.Parameters.Count == paramCount))
            return true;

        if (cd.BaseClassName != null && _classes.ContainsKey(cd.BaseClassName))
        {
            return HasMethodInChain(cd.BaseClassName, methodName, paramCount);
        }

        return false;
    }

    private List<string> GetAllFields(string className)
    {
        var fields = new List<string>();
        if (_classes.TryGetValue(className, out var cd))
        {
            if (cd.BaseClassName != null)
            {
                fields.AddRange(GetAllFields(cd.BaseClassName));
            }

            foreach (var f in cd.Fields)
            {
                fields.Add(f.Name);
            }
        }

        return fields;
    }

    private List<VariableDeclaration> GetAllFieldsFull(string className)
    {
        var fields = new List<VariableDeclaration>();
        if (_classes.TryGetValue(className, out var cd))
        {
            if (cd.BaseClassName != null)
            {
                fields.AddRange(GetAllFieldsFull(cd.BaseClassName));
            }

            foreach (var f in cd.Fields)
            {
                fields.Add(f);
            }
        }

        return fields;
    }

    private bool HasConstructorInChain(string className)
    {
        if (_classes.TryGetValue(className, out var cd))
        {
            // Constructor has the same name as the class it's defined in
            if (cd.Methods.Any(m => m.Name == cd.Name))
                return true;

            if (cd.BaseClassName != null)
                return HasConstructorInChain(cd.BaseClassName);
        }

        return false;
    }

    private VariableDeclaration? GetFieldInChain(string className, string fieldName)
    {
        if (!_classes.TryGetValue(className, out var cd)) return null;

        var field = cd.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field != null) return field;

        if (cd.BaseClassName != null)
            return GetFieldInChain(cd.BaseClassName, fieldName);

        return null;
    }

    private FunctionDeclaration? GetMethodInChain(string className, string methodName, int? paramCount = null)
    {
        if (!_classes.TryGetValue(className, out var cd)) return null;

        var method = cd.Methods.FirstOrDefault(m =>
            m.Name == methodName && (paramCount == null || m.Parameters.Count == paramCount));
        if (method != null) return method;

        if (cd.BaseClassName != null)
            return GetMethodInChain(cd.BaseClassName, methodName, paramCount);

        return null;
    }

    private void CheckAccess(string targetClassName, AccessModifier access, string kind, string name)
    {
        if (access == AccessModifier.Public) return;

        if (_currentClass == null)
            throw new CompilerException($"Cannot access {access.ToString().ToLower()} {kind} '{name}' from top-level",
                0);

        if (access == AccessModifier.Private)
        {
            if (_currentClass != targetClassName)
                throw new CompilerException(
                    $"Cannot access private {kind} '{name}' of class '{targetClassName}' from class '{_currentClass}'",
                    0);
        }
        else if (access == AccessModifier.Protected)
        {
            if (!IsDerivedFrom(_currentClass, targetClassName))
                throw new CompilerException(
                    $"Cannot access protected {kind} '{name}' of class '{targetClassName}' from class '{_currentClass}'",
                    0);
        }
    }

    private bool IsDerivedFrom(string child, string parent)
    {
        if (child == parent) return true;
        if (!_classes.TryGetValue(child, out var cd)) return false;
        if (cd.BaseClassName == null) return false;
        return IsDerivedFrom(cd.BaseClassName, parent);
    }

    private string GetDefiningClassForField(string className, string fieldName)
    {
        if (!_classes.TryGetValue(className, out var cd)) return className;
        if (cd.Fields.Any(f => f.Name == fieldName)) return className;
        if (cd.BaseClassName != null) return GetDefiningClassForField(cd.BaseClassName, fieldName);
        return className;
    }

    private string GetDefiningClassForMethod(string className, string methodName, int paramCount)
    {
        if (!_classes.TryGetValue(className, out var cd)) return className;
        if (cd.Methods.Any(m => m.Name == methodName && m.Parameters.Count == paramCount)) return className;
        if (cd.BaseClassName != null) return GetDefiningClassForMethod(cd.BaseClassName, methodName, paramCount);
        return className;
    }

    private void HandleImport(ImportStatement import)
    {
        string extension = Path.GetExtension(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Path.GetFileName(import.FilePath))).ToLowerInvariant();
        
        if (extension == ".dll")
        {
            string dllFileName = Path.GetFileName(import.FilePath);
            string dllPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), dllFileName);
        
            dllPath = Path.GetFullPath(dllPath);

            if (_importedFiles.Contains(dllPath))
                return;

            if (!File.Exists(dllPath))
                throw new CompilerException($"Import DLL not found in executable directory: {dllFileName}. Directory: {dllPath}", import.Start);

            LoadDll(dllPath, import.ImportNames);
            _importedFiles.Add(dllPath);
            return;
        }
        
        string basePath = Path.GetDirectoryName(_currentFilePath) ?? Directory.GetCurrentDirectory();
        string fullPath = Path.Combine(basePath, import.FilePath);

        fullPath = Path.GetFullPath(fullPath);

        if (_importedFiles.Contains(fullPath))
        {
            return;
        }

        if (!File.Exists(fullPath))
        {
            throw new CompilerException($"Import file not found: {import.FilePath}", import.Start);
        }

        string source = File.ReadAllText(fullPath);

        var oldFilePath = _currentFilePath;
        var oldInstructions = _instructions;
        var oldFunctions = _functions;

        var importCompiler = new Compiler();
        importCompiler._currentFilePath = fullPath;
        importCompiler._importedFiles = new HashSet<string>(_importedFiles);
        importCompiler._importedFiles.Add(fullPath);

        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);
        var parser = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var importedInstructions = importCompiler.Compile(program);

        _instructions.AddRange(importedInstructions);

        foreach (var kv in importCompiler._functions)
        {
            if (!_functions.ContainsKey(kv.Key))
                _functions[kv.Key] = kv.Value;
        }

        foreach (var kv in importCompiler._classes)
        {
            if (!_classes.ContainsKey(kv.Key))
                _classes[kv.Key] = kv.Value;
        }

        foreach (var kv in importCompiler._externalVariables)
        {
            if (!_externalVariables.ContainsKey(kv.Key))
                _externalVariables[kv.Key] = kv.Value;
        }

        _importedFiles.Add(fullPath);

        _currentFilePath = oldFilePath;
    }

    private void LoadDll(string fullPath, List<string> importNames = null)
    {
        Assembly asm;
        try
        {
            asm = Assembly.LoadFrom(fullPath);
        }
        catch (Exception ex)
        {
            throw new CompilerException($"Failed to load DLL '{fullPath}': {ex.Message}", 0);
        }
        
        // If importNames is specified, convert to lowercase for comparison
        var importNamesLower = importNames?.Count > 0 
            ? new HashSet<string>(importNames.Select(x => x.ToLower()), StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var type in asm.GetTypes())
        {
            var classAttr = type.GetCustomAttribute<SabakaExportAttribute>();
            if (classAttr == null) continue;

            string className = classAttr.Name.ToLower();

            // Найти конструктор с [SabakaExport] и создать экземпляр
            var exportedCtor = type.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length == 0);

            object createInstance;
            try 
            { 
                createInstance = Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                continue;
            }

            if (exportedCtor == null) continue;

            object instance;
            try { instance = exportedCtor.Invoke(Array.Empty<object>()); }
            catch (Exception ex)
            {
                throw new CompilerException($"Failed to instantiate '{type.Name}': {ex.Message}", 0);
            }

            foreach (var ctor in type.GetConstructors())
            {
                var attrs = ctor.GetCustomAttributes().ToList();
            }
            
            // Регистрируем все методы как "classname.methodname"
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = method.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (attr == null) continue;

                string methodExportName = (string)attr.GetType().GetProperty("Name")!.GetValue(attr)!;
                string key = $"{className}.{methodExportName.ToLower()}";
                
                // If specific imports are requested, check if this method is in the list
                if (importNamesLower != null && !importNamesLower.Contains(methodExportName))
                    continue;
    
                var parameters = method.GetParameters();
                int paramCount = parameters.Length;
                var returnType = method.ReturnType;
                var capturedInstance = instance;
                var capturedMethod = method;

                Func<Value[], Value> wrapper = (args) =>
                {
                    try
                    {
                        object?[] converted = new object?[paramCount];
                        for (int i = 0; i < paramCount; i++)
                            converted[i] = ConvertValueToNative(args[i], parameters[i].ParameterType);
                        var result = capturedMethod.Invoke(capturedInstance, converted);
                        return ConvertNativeToValue(result, returnType);
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw ex.InnerException ?? ex;
                    }
                };

                _externalFunctions[key] = (paramCount, wrapper);
            }

            // Регистрируем все публичные свойства и поля с SabakaExportAttribute
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = property.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (attr == null) continue;

                string varExportName = (string)attr.GetType().GetProperty("Name")!.GetValue(attr)!;
                
                // If specific imports are requested, check if this variable is in the list
                if (importNamesLower != null && !importNamesLower.Contains(varExportName))
                    continue;
                
                string key = varExportName.ToLower();

                try
                {
                    object? value = property.GetValue(instance);
                    Value convertedValue = ConvertNativeToValue(value, property.PropertyType);
                    _externalVariables[key] = convertedValue;
                }
                catch (Exception ex)
                {
                    throw new CompilerException($"Failed to read property '{varExportName}' from '{type.Name}': {ex.Message}", 0);
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = field.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (attr == null) continue;

                string varExportName = (string)attr.GetType().GetProperty("Name")!.GetValue(attr)!;
                
                // If specific imports are requested, check if this variable is in the list
                if (importNamesLower != null && !importNamesLower.Contains(varExportName))
                    continue;
                
                string key = varExportName.ToLower();

                try
                {
                    object? value = field.GetValue(instance);
                    Value convertedValue = ConvertNativeToValue(value, field.FieldType);
                    _externalVariables[key] = convertedValue;
                }
                catch (Exception ex)
                {
                    throw new CompilerException($"Failed to read field '{varExportName}' from '{type.Name}': {ex.Message}", 0);
                }
            }

            // Регистрируем сам класс как известный (чтобы new directory() не падал)
            // Добавляем фейковый класс в _classes через специальный маркер
            if (!_classes.ContainsKey(classAttr.Name))
            {
                _classes[classAttr.Name] = new ClassDeclaration(
                    classAttr.Name, null, new List<string>(),
                    new List<VariableDeclaration>(), new List<FunctionDeclaration>());
            }
        }
    }

    private static object? ConvertValueToNative(Value val, Type targetType)
    {
        if (targetType == typeof(int) || targetType == typeof(int?))
        {
            if (val.Type != SabakaType.Int) throw new Exception($"Expected int, got {val.Type}");
            return val.Int;
        }

        if (targetType == typeof(double) || targetType == typeof(double?))
        {
            if (val.Type == SabakaType.Int) return (double)val.Int;
            if (val.Type == SabakaType.Float) return val.Float;
            throw new Exception($"Expected number, got {val.Type}");
        }

        if (targetType == typeof(float) || targetType == typeof(float?))
        {
            if (val.Type == SabakaType.Int) return (float)val.Int;
            if (val.Type == SabakaType.Float) return (float)val.Float;
            throw new Exception($"Expected number, got {val.Type}");
        }

        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            if (val.Type != SabakaType.Bool) throw new Exception($"Expected bool, got {val.Type}");
            return val.Bool;
        }

        if (targetType == typeof(string))
        {
            if (val.Type != SabakaType.String) throw new Exception($"Expected string, got {val.Type}");
            return val.String;
        }

        throw new NotSupportedException($"Conversion to type {targetType} not supported");
    }

    private static Value ConvertNativeToValue(object? result, Type returnType)
    {
        if (returnType == typeof(void))
            return Value.FromInt(0);

        if (result == null)
            return Value.FromInt(0);

        if (returnType == typeof(int) || returnType == typeof(long) || returnType == typeof(short) ||
            returnType == typeof(byte))
            return Value.FromInt(Convert.ToInt32(result));
        if (returnType == typeof(double) || returnType == typeof(float))
            return Value.FromFloat(Convert.ToDouble(result));
        if (returnType == typeof(bool))
            return Value.FromBool((bool)result);
        if (returnType == typeof(string))
            return Value.FromString((string)result);

        throw new NotSupportedException($"Conversion from type {returnType} not supported");
    }
}