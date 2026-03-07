using System.Runtime.Loader;
using SabakaLang.AST;
using SabakaLang.Exceptions;
using SabakaLang.Lexer;
using SabakaLang.Types;
using System;
using System.IO;
using System.Reflection;
using SabakaLang.SDK;

namespace SabakaLang.Compiler;

public class Compiler
{
    private readonly List<Instruction> _instructions = new();
    private Dictionary<string, int> _functions = new();
    private Dictionary<string, List<string>> _structs = new();
    private Dictionary<string, Dictionary<string, int>> _enums = new();
    private Dictionary<string, ClassDeclaration> _classes = new();
    // Stores original generic class templates keyed by base name, e.g. "List"
    private Dictionary<string, ClassDeclaration> _genericTemplates = new();
    private Dictionary<string, InterfaceDeclaration> _interfaces = new();
    private string? _currentClass = null;
    private Stack<Dictionary<string, string>> _typeScopes = new();
    private HashSet<string> _importedFiles = new();
    private string? _currentFilePath;
    private readonly Dictionary<string, (int ParamCount, Func<Value[], Value> Delegate)> _externalFunctions = new();
    private readonly Dictionary<string, Value> _externalVariables = new(); // For storing imported variables

    // Modules implementing ICallbackReceiver — VM wires InvokeCallback into these
    private readonly List<object> _callbackReceivers = new();
    public IReadOnlyList<object> CallbackReceivers => _callbackReceivers;

    // Namespaced modules: alias -> (functions, variables)
    private readonly Dictionary<string, (
        Dictionary<string, (int ParamCount, Func<Value[], Value> Delegate)> Functions,
        Dictionary<string, Value> Variables
    )> _modules = new();

    // Maps "alias.funcname" -> real SabakaLang function name (for .sabaka namespaced imports)
    private readonly Dictionary<string, string> _moduleSabakaFunctions = new();

    // Top-level constants collected during compilation (for module variable access)
    private readonly Dictionary<string, Value> _globalConstants = new();

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
        Console.WriteLine("Compiling...");
        
        _currentFilePath = filePath;
        _importedFiles.Clear();
        if (_currentFilePath != null)
            _importedFiles.Add(_currentFilePath);

        _typeScopes.Clear();
        PushScope(); // Global scope

        foreach (var expr in expressions)
        {
            Emit(expr);
        }

        Console.WriteLine($"Compiled {_instructions.Count} instructions");
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
        else if (expr is ImportStatement import)
        {
            HandleImport(import);
        }
        else if (expr is ClassDeclaration classDecl)
        {
            // If this is a generic class definition, store as template and don't compile yet.
            // It will be instantiated (monomorphized) when a NewExpr with type args is encountered.
            if (classDecl.IsGeneric)
            {
                _genericTemplates[classDecl.Name] = classDecl;
                return;
            }

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

            // Interface implementation check
            foreach (var interfaceName in allInterfaces)
            {
                var ifaceMethods = GetAllInterfaceMethods(interfaceName);

                foreach (var ifaceMethod in ifaceMethods)
                {
                    if (!HasMethodInChain(classDecl.Name, ifaceMethod.Name, ifaceMethod.Parameters.Count))
                    {
                        throw new CompilerException(
                            $"Class {classDecl.Name} does not implement interface method {ifaceMethod.Name}", 0);
                    }
                }
            }

            var oldClass = _currentClass;
            _currentClass = classDecl.Name;

            var fields = GetAllFields(classDecl.Name);

            foreach (var method in classDecl.Methods)
            {
                var methodFqn = $"{classDecl.Name}.{method.Name}";
                var methodInstr = new Instruction(OpCode.Function)
                {
                    Name = methodFqn,
                    Extra = method.Parameters.Select(p => p.Name).ToList()
                };

                _instructions.Add(methodInstr);
                var bodyStart = _instructions.Count;

                PushScope();
                // Declare fields in scope so we know their types
                foreach (var field in GetAllFieldsFull(classDecl.Name))
                {
                    DeclareVar(field.Name, field.CustomType ?? field.TypeToken.ToString());
                }

                foreach (var p in method.Parameters)
                {
                    DeclareVar(p.Name, p.CustomType ?? p.Type.ToString());
                }

                foreach (var stmt in method.Body)
                    Emit(stmt);

                PopScope();

                _instructions.Add(new Instruction(OpCode.Return));

                methodInstr.Operand = _instructions.Count;
                _functions[methodFqn] = bodyStart;
            }

            _currentClass = oldClass;
        }
        else if (expr is InterfaceDeclaration interfaceDecl)
        {
            // Generic interfaces are stored but not validated until instantiation
            _interfaces[interfaceDecl.Name] = interfaceDecl;
        }
        else if (expr is NewExpr newExpr)
        {
            // If this is a generic instantiation (e.g. new List<int>()),
            // monomorphize the template class before proceeding.
            if (newExpr.TypeArgs.Count > 0)
            {
                string mangledName = newExpr.MangledName;
                if (!_classes.ContainsKey(mangledName))
                {
                    if (!_genericTemplates.TryGetValue(newExpr.ClassName, out var template))
                        throw new CompilerException($"Generic class '{newExpr.ClassName}' not found", 0);

                    var instantiated = MonomorphizeClass(template, newExpr.TypeArgs);
                    // Compile the instantiated class
                    Emit(instantiated);
                }

                // Now emit as a regular NewExpr with the mangled name
                var concreteNew = new NewExpr(mangledName, newExpr.Arguments);
                Emit(concreteNew);
                return;
            }

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

            // If the class has field initializers, emit code to set them on the newly created object.
            if (cd != null)
            {
                foreach (var fieldDecl in GetAllFieldsFull(newExpr.ClassName))
                {
                    if (fieldDecl.Initializer != null)
                    {
                        // duplicate object reference
                        _instructions.Add(new Instruction(OpCode.Dup));
                        // emit initializer (pushes the value)
                        Emit(fieldDecl.Initializer);
                        // store into field
                        _instructions.Add(new Instruction(OpCode.StoreField)
                        {
                            Name = fieldDecl.Name
                        });
                    }
                }
            }

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
            var functionInstr = new Instruction(OpCode.Function);
            functionInstr.Name = func.Name;
            functionInstr.Extra = func.Parameters.Select(p => p.Name).ToList();

            _instructions.Add(functionInstr);
            var bodyStart = _instructions.Count;

            PushScope();
            foreach (var p in func.Parameters)
                DeclareVar(p.Name, p.CustomType ?? p.Type.ToString());

            foreach (var stmt in func.Body)
                Emit(stmt);

            PopScope();

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

            // File IO builtins
            if (call.Target == null && call.Name == "readFile")
            {
                if (call.Arguments.Count != 1)
                    throw new CompilerException("readFile expects 1 argument (path)", 0);
                Emit(call.Arguments[0]);
                _instructions.Add(new Instruction(OpCode.ReadFile));
                return;
            }

            if (call.Target == null && call.Name == "writeFile")
            {
                if (call.Arguments.Count != 2)
                    throw new CompilerException("writeFile expects 2 arguments (path, content)", 0);
                Emit(call.Arguments[0]);
                Emit(call.Arguments[1]);
                _instructions.Add(new Instruction(OpCode.WriteFile));
                return;
            }

            if (call.Target == null && call.Name == "appendFile")
            {
                if (call.Arguments.Count != 2)
                    throw new CompilerException("appendFile expects 2 arguments (path, content)", 0);
                Emit(call.Arguments[0]);
                Emit(call.Arguments[1]);
                _instructions.Add(new Instruction(OpCode.AppendFile));
                return;
            }

            if (call.Target == null && call.Name == "fileExists")
            {
                if (call.Arguments.Count != 1)
                    throw new CompilerException("fileExists expects 1 argument (path)", 0);
                Emit(call.Arguments[0]);
                _instructions.Add(new Instruction(OpCode.FileExists));
                return;
            }

            if (call.Target == null && call.Name == "deleteFile")
            {
                if (call.Arguments.Count != 1)
                    throw new CompilerException("deleteFile expects 1 argument (path)", 0);
                Emit(call.Arguments[0]);
                _instructions.Add(new Instruction(OpCode.DeleteFile));
                return;
            }

            if (call.Target == null && call.Name == "readLines")
            {
                if (call.Arguments.Count != 1)
                    throw new CompilerException("readLines expects 1 argument (path)", 0);
                Emit(call.Arguments[0]);
                _instructions.Add(new Instruction(OpCode.ReadLines));
                return;
            }

            if (call.Target == null && call.Name == "time")
            {
                if (call.Arguments.Count != 0)
                    throw new CompilerException("time expects no arguments", 0);
                _instructions.Add(new Instruction(OpCode.Time));
                return;
            }

            if (call.Target == null && call.Name == "timeMs")
            {
                if (call.Arguments.Count != 0)
                    throw new CompilerException("timeMs expects no arguments", 0);
                _instructions.Add(new Instruction(OpCode.TimeMs));
                return;
            }

            if (call.Target == null && call.Name == "httpGet")
            {
                if (call.Arguments.Count != 1)
                    throw new CompilerException("httpGet expects 1 argument (url)", 0);
                Emit(call.Arguments[0]);
                _instructions.Add(new Instruction(OpCode.HttpGet));
                return;
            }

            if (call.Target == null && call.Name == "httpPost")
            {
                if (call.Arguments.Count != 2)
                    throw new CompilerException("httpPost expects 2 arguments (url, body)", 0);
                Emit(call.Arguments[0]);
                Emit(call.Arguments[1]);
                _instructions.Add(new Instruction(OpCode.HttpPost));
                return;
            }

            if (call.Target == null && call.Name == "httpPostJson")
            {
                if (call.Arguments.Count != 2)
                    throw new CompilerException("httpPostJson expects 2 arguments (url, json)", 0);
                Emit(call.Arguments[0]);
                Emit(call.Arguments[1]);
                _instructions.Add(new Instruction(OpCode.HttpPostJson));
                return;
            }

            if (call.Target == null && call.Name == "ord")
            {
                if (call.Arguments.Count != 1)
                    throw new CompilerException("ord expects 1 argument (string)", 0);
                Emit(call.Arguments[0]);
                _instructions.Add(new Instruction(OpCode.Ord));
                return;
            }

            if (call.Target == null && call.Name == "chr")
            {
                if (call.Arguments.Count != 1)
                    throw new CompilerException("chr expects 1 argument (int)", 0);
                Emit(call.Arguments[0]);
                _instructions.Add(new Instruction(OpCode.Chr));
                return;
            }

            // Module alias call: math.sqrt(x), rng.randomRange(0,10)
            // First check if it's a .sabaka function (Call opcode)
            if (call.Target is VariableExpr moduleSabakaVe &&
                _moduleSabakaFunctions.TryGetValue($"{moduleSabakaVe.Name.ToLower()}.{call.Name.ToLower()}", out var realFuncName))
            {
                foreach (var arg in call.Arguments)
                    Emit(arg);
                _instructions.Add(new Instruction(OpCode.Call, call.Arguments.Count) { Name = realFuncName });
                return;
            }

            // Then check if it's a dll function (CallExternal opcode)
            if (call.Target is VariableExpr moduleVe &&
                _modules.TryGetValue(moduleVe.Name.ToLower(), out var mod) &&
                mod.Functions.TryGetValue(call.Name.ToLower(), out var modFunc))
            {
                if (call.Arguments.Count != modFunc.ParamCount)
                    throw new CompilerException(
                        $"Module function '{moduleVe.Name}.{call.Name}' expects {modFunc.ParamCount} arguments, got {call.Arguments.Count}",
                        call.Start);

                foreach (var arg in call.Arguments)
                    Emit(arg);

                _instructions.Add(new Instruction(OpCode.CallExternal, call.Arguments.Count)
                {
                    Name = $"{moduleVe.Name.ToLower()}.{call.Name.ToLower()}"
                });
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

                // Initialize field initializers if present
                foreach (var fieldDecl in GetAllFieldsFull(call.Name))
                {
                    if (fieldDecl.Initializer != null)
                    {
                        _instructions.Add(new Instruction(OpCode.Dup));
                        Emit(fieldDecl.Initializer);
                        _instructions.Add(new Instruction(OpCode.StoreField)
                        {
                            Name = fieldDecl.Name
                        });
                    }
                }

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
            // Module variable access: math.PI, rng.seed
            if (memberAccess.Object is VariableExpr modVarExpr &&
                _modules.TryGetValue(modVarExpr.Name.ToLower(), out var modVars) &&
                modVars.Variables.TryGetValue(memberAccess.Member.ToLower(), out var modVarValue))
            {
                _instructions.Add(new Instruction(OpCode.Push) { Operand = modVarValue });
                return;
            }

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
                // Special-case: allow array.length member access.
                // Emit the object expression and then ArrayLength opcode. Runtime will validate the type.
                if (string.Equals(memberAccess.Member, "length", StringComparison.OrdinalIgnoreCase))
                {
                    Emit(memberAccess.Object);
                    _instructions.Add(new Instruction(OpCode.ArrayLength));
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
            // If customType is a mangled generic name (e.g. "Box$int"), ensure the class is instantiated
            if (decl.CustomType != null && decl.CustomType.Contains('$') && !_classes.ContainsKey(decl.CustomType))
            {
                EnsureGenericClassInstantiated(decl.CustomType);
            }

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
            // Module alias variable: math.PI, rng.seed
            // This case is handled via MemberAccessExpr below, but guard here too
            if (_modules.TryGetValue(variable.Name.ToLower(), out _))
            {
                // Bare module alias used as expression — push null/zero placeholder
                // Real value access is via MemberAccessExpr: math.PI
                _instructions.Add(new Instruction(OpCode.Push) { Operand = Value.FromInt(0) });
                return;
            }

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

    /// <summary>
    /// Given a mangled name like "Box$int" or "Pair$string$int", ensures the generic
    /// class has been monomorphized and compiled. Parses the mangled name to extract
    /// the base class name and type arguments.
    /// </summary>
    private void EnsureGenericClassInstantiated(string mangledName)
    {
        if (_classes.ContainsKey(mangledName)) return;

        int dollar = mangledName.IndexOf('$');
        if (dollar < 0) return;

        string baseName = mangledName.Substring(0, dollar);
        var typeArgs = mangledName.Substring(dollar + 1).Split('$').ToList();

        if (!_genericTemplates.TryGetValue(baseName, out var template))
            throw new CompilerException($"Generic class '{baseName}' not found", 0);

        var instantiated = MonomorphizeClass(template, typeArgs);
        Emit(instantiated);
    }

    /// <summary>
    /// Performs monomorphization: clones a generic class template substituting
    /// type parameters with concrete type arguments. Returns a non-generic
    /// ClassDeclaration with a mangled name (e.g. "List$int").
    /// </summary>
    private ClassDeclaration MonomorphizeClass(ClassDeclaration template, List<string> typeArgs)
    {
        if (template.TypeParams.Count != typeArgs.Count)
            throw new CompilerException(
                $"Generic class '{template.Name}' expects {template.TypeParams.Count} type parameter(s), got {typeArgs.Count}", 0);

        // Build substitution map: T -> int, U -> string, etc.
        var subst = new Dictionary<string, string>();
        for (int i = 0; i < template.TypeParams.Count; i++)
            subst[template.TypeParams[i]] = typeArgs[i];

        string mangledName = $"{template.Name}${string.Join("$", typeArgs)}";

        // Clone fields with substituted types
        var newFields = template.Fields.Select(f =>
        {
            string? newCustom = f.CustomType != null && subst.TryGetValue(f.CustomType, out var sc) ? sc : f.CustomType;
            TokenType newType = f.TypeToken;
            if (f.CustomType != null && subst.ContainsKey(f.CustomType))
                newType = TokenType.Identifier; // keep as Identifier, customType holds concrete name
            return new VariableDeclaration(newType, newCustom, f.Name, f.Initializer, f.AccessModifier);
        }).ToList();

        // Clone methods with substituted parameter/return types
        var newMethods = template.Methods.Select(m =>
        {
            var newParams = m.Parameters.Select(p =>
            {
                string? newCustom = p.CustomType != null && subst.TryGetValue(p.CustomType, out var sc) ? sc : p.CustomType;
                return new Parameter(p.Type, p.Name, newCustom);
            }).ToList();
            // Substitute method name if it equals the class name (constructor)
            string methodName = m.Name == template.Name ? mangledName : m.Name;
            return new FunctionDeclaration(m.ReturnType, methodName, newParams, m.Body, m.IsOverride, m.AccessModifier);
        }).ToList();

        return new ClassDeclaration(mangledName, null, template.BaseClassName, template.Interfaces, newFields, newMethods);
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
        string extension = Path.GetExtension(import.FilePath).ToLowerInvariant();

        if (extension == ".dll")
        {
            string dllFileName = Path.GetFileName(import.FilePath);

            // Look next to script first, then next to exe
            string scriptDir = Path.GetDirectoryName(_currentFilePath) ?? Directory.GetCurrentDirectory();
            string dllPathNearScript = Path.GetFullPath(Path.Combine(scriptDir, dllFileName));
            string dllPathNearExe    = Path.GetFullPath(Path.Combine(_executableDirectory, dllFileName));

            string dllPath = File.Exists(dllPathNearScript) ? dllPathNearScript : dllPathNearExe;

            if (_importedFiles.Contains(dllPath))
                return;

            if (!File.Exists(dllPath))
                throw new CompilerException(
                    "Import DLL not found: '" + dllFileName + "'. Looked in:\n  " + dllPathNearScript + "\n  " + dllPathNearExe,
                    import.Start);

            string? alias = import.Alias?.ToLower();
            if (alias == null)
                alias = Path.GetFileNameWithoutExtension(dllFileName).ToLower();

            LoadDll(dllPath, import.ImportNames, alias, import.Alias != null);
            _importedFiles.Add(dllPath);
            return;
        }

        // .sabaka file import
        string basePath = Path.GetDirectoryName(_currentFilePath) ?? Directory.GetCurrentDirectory();
        string fullPath = Path.Combine(basePath, import.FilePath);
        fullPath = Path.GetFullPath(fullPath);

        if (_importedFiles.Contains(fullPath))
            return;

        if (!File.Exists(fullPath))
            throw new CompilerException($"Import file not found: {import.FilePath}", import.Start);

        Console.WriteLine($"Loading file: {fullPath}");

        string source = File.ReadAllText(fullPath);
        var oldFilePath = _currentFilePath;

        _importedFiles.Add(fullPath); // guard BEFORE recursion

        var importCompiler = new Compiler();
        importCompiler._currentFilePath = fullPath;
        importCompiler._importedFiles = new HashSet<string>(_importedFiles);
        importCompiler._importedFiles.Add(fullPath);

        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);
        var parser = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var importedInstructions = importCompiler.Compile(program, fullPath);

        int addressOffset = _instructions.Count;
        _instructions.AddRange(importedInstructions);

        // Fix up Jump targets
        for (int i = addressOffset; i < _instructions.Count; i++)
        {
            var instr = _instructions[i];
            if (instr.OpCode == OpCode.Jump ||
                instr.OpCode == OpCode.JumpIfFalse ||
                instr.OpCode == OpCode.JumpIfTrue ||
                instr.OpCode == OpCode.Function)
            {
                if (instr.Operand is int addr)
                    instr.Operand = addr + addressOffset;
            }
        }

        string? fileAlias = import.Alias?.ToLower();

        if (fileAlias != null)
        {
            // Namespaced: register symbols under alias, not globally
            var modFuncs = new Dictionary<string, (int ParamCount, Func<Value[], Value> Delegate)>();
            var modVars  = new Dictionary<string, Value>();

            foreach (var kv in importCompiler._functions)
                modFuncs[kv.Key.ToLower()] = (0, null!); // function addresses resolved at runtime via Call opcode

            // Register as callable module functions using Call opcode redirect
            // For .sabaka modules we store function names so CallExpr can emit Call (not CallExternal)
            if (!_modules.ContainsKey(fileAlias))
                _modules[fileAlias] = (new Dictionary<string, (int, Func<Value[], Value>)>(), new Dictionary<string, Value>());

            // Merge sabaka functions into module as Call-based entries
            foreach (var kv in importCompiler._functions)
            {
                var funcName = kv.Key.ToLower();
                _moduleSabakaFunctions[$"{fileAlias}.{funcName}"] = kv.Key;
            }

            // Merge variables
            foreach (var kv in importCompiler._externalVariables)
                _modules[fileAlias].Variables[kv.Key.ToLower()] = kv.Value;

            // Also copy top-level Declare instructions' values into module vars at compile time
            // (global float/int/string constants from the imported file)
            foreach (var kv in importCompiler._globalConstants)
                _modules[fileAlias].Variables[kv.Key.ToLower()] = kv.Value;
        }
        else
        {
            // Global: merge everything as-is (old behaviour)
            foreach (var kv in importCompiler._functions)
                if (!_functions.ContainsKey(kv.Key))
                    _functions[kv.Key] = kv.Value;

            foreach (var kv in importCompiler._classes)
                if (!_classes.ContainsKey(kv.Key))
                    _classes[kv.Key] = kv.Value;

            foreach (var kv in importCompiler._genericTemplates)
                if (!_genericTemplates.ContainsKey(kv.Key))
                    _genericTemplates[kv.Key] = kv.Value;

            foreach (var kv in importCompiler._externalVariables)
                if (!_externalVariables.ContainsKey(kv.Key))
                    _externalVariables[kv.Key] = kv.Value;

            foreach (var kv in importCompiler._globalConstants)
                if (!_externalVariables.ContainsKey(kv.Key))
                    _externalVariables[kv.Key] = kv.Value;
        }

        // Classes are always global (variant B)
        foreach (var kv in importCompiler._classes)
            if (!_classes.ContainsKey(kv.Key))
                _classes[kv.Key] = kv.Value;

        foreach (var kv in importCompiler._genericTemplates)
            if (!_genericTemplates.ContainsKey(kv.Key))
                _genericTemplates[kv.Key] = kv.Value;

        // Merge module sabaka functions
        foreach (var kv in importCompiler._moduleSabakaFunctions)
            if (!_moduleSabakaFunctions.ContainsKey(kv.Key))
                _moduleSabakaFunctions[kv.Key] = kv.Value;

        // Merge modules
        foreach (var kv in importCompiler._modules)
            if (!_modules.ContainsKey(kv.Key))
                _modules[kv.Key] = kv.Value;

        // Merge external functions (dll functions from sub-imports)
        foreach (var kv in importCompiler._externalFunctions)
            if (!_externalFunctions.ContainsKey(kv.Key))
                _externalFunctions[kv.Key] = kv.Value;

        // Merge imported files guard
        foreach (var f in importCompiler._importedFiles)
            _importedFiles.Add(f);

        _currentFilePath = oldFilePath;

        Console.WriteLine($"Loaded file: {fullPath}");
    }

    private void LoadDll(string fullPath, List<string>? importNames, string alias, bool namespaced)
    {
        // Use a collectible load context so missing dependencies (e.g. WPF)
        // return null instead of throwing FileNotFoundException.
        var alc = new SabakaDllLoadContext(fullPath);
        Assembly asm;
        try { asm = alc.LoadFromAssemblyPath(fullPath); }
        catch (Exception ex)
        {
            alc.Unload();
            throw new CompilerException($"Failed to load DLL '{fullPath}': {ex.Message}", 0);
        }

        Type[] dllTypes;
        try { dllTypes = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { dllTypes = ex.Types.Where(t => t != null).ToArray()!; }
        catch (Exception ex)
        {
            alc.Unload();
            throw new CompilerException($"Failed to read types from '{fullPath}': {ex.Message}", 0);
        }

        var importNamesLower = importNames?.Count > 0
            ? new HashSet<string>(importNames.Select(x => x.ToLower()), StringComparer.OrdinalIgnoreCase)
            : null;

        // Prepare module bucket if namespaced
        if (namespaced && !_modules.ContainsKey(alias))
            _modules[alias] = (
                new Dictionary<string, (int, Func<Value[], Value>)>(),
                new Dictionary<string, Value>()
            );

        foreach (var type in dllTypes)
        {
            if (type == null) continue;
            // Accept classes marked with [SabakaExport] OR implementing ISabakaModule
            var classAttr = type.GetCustomAttribute<SabakaExportAttribute>();
            bool isSabakaModule = type.GetInterfaces()
                .Any(i => i.Name == "ISabakaModule");

            if (classAttr == null && !isSabakaModule) continue;

            // Derive export name: from attribute, or from class name
            string exportedClassName = classAttr?.Name ?? type.Name;
            // Patch: replace exportedClassName usages below with exportedClassName


            object instance;
            try { instance = Activator.CreateInstance(type)!; }
            catch { continue; }

            // Track modules that implement ICallbackReceiver
            if (type.GetInterfaces().Any(i => i.Name == "ICallbackReceiver"))
                _callbackReceivers.Add(instance);

            // Register methods
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = method.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (attr == null) continue;

                string exportName = (string)attr.GetType().GetProperty("Name")!.GetValue(attr)!;

                if (importNamesLower != null && !importNamesLower.Contains(exportName))
                    continue;

                var parameters  = method.GetParameters();
                int paramCount  = parameters.Length;
                var returnType  = method.ReturnType;
                var capInstance = instance;
                var capMethod   = method;

                Func<Value[], Value> wrapper = (args) =>
                {
                    object?[] converted = new object?[paramCount];
                    for (int i = 0; i < paramCount; i++)
                        converted[i] = ConvertValueToNative(args[i], parameters[i].ParameterType);
                    var result = capMethod.Invoke(capInstance, converted);
                    return ConvertNativeToValue(result, returnType);
                };

                if (namespaced)
                {
                    string key = exportName.ToLower();
                    _modules[alias].Functions[key] = (paramCount, wrapper);
                    // Also register under "alias.name" in _externalFunctions so CallExternal opcode finds it
                    _externalFunctions[$"{alias}.{key}"] = (paramCount, wrapper);
                }
                else
                {
                    string key = $"{exportedClassName.ToLower()}.{exportName.ToLower()}";
                    _externalFunctions[key] = (paramCount, wrapper);
                }
            }

            // Register properties/fields as variables
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = prop.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (attr == null) continue;

                string exportName = (string)attr.GetType().GetProperty("Name")!.GetValue(attr)!;
                if (importNamesLower != null && !importNamesLower.Contains(exportName)) continue;

                try
                {
                    Value val = ConvertNativeToValue(prop.GetValue(instance), prop.PropertyType);
                    if (namespaced) _modules[alias].Variables[exportName.ToLower()] = val;
                    else            _externalVariables[exportName.ToLower()] = val;
                }
                catch { }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var attr = field.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (attr == null) continue;

                string exportName = (string)attr.GetType().GetProperty("Name")!.GetValue(attr)!;
                if (importNamesLower != null && !importNamesLower.Contains(exportName)) continue;

                try
                {
                    Value val = ConvertNativeToValue(field.GetValue(instance), field.FieldType);
                    if (namespaced) _modules[alias].Variables[exportName.ToLower()] = val;
                    else            _externalVariables[exportName.ToLower()] = val;
                }
                catch { }
            }

            // Classes always global
            if (!_classes.ContainsKey(exportedClassName))
            {
                _classes[exportedClassName] = new ClassDeclaration(
                    exportedClassName, new List<string>(), null, new List<string>(),
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
/// <summary>
/// Collectible AssemblyLoadContext for loading native DLL libraries.
/// Returns null for unresolvable dependencies (e.g. PresentationFramework/WPF)
/// instead of throwing — the runtime then skips affected types via
/// ReflectionTypeLoadException, which the caller handles gracefully.
/// </summary>
internal sealed class SabakaDllLoadContext : System.Runtime.Loader.AssemblyLoadContext
{
    private readonly string _dllDir;

    public SabakaDllLoadContext(string dllPath)
        : base(name: "Sabaka-" + Path.GetFileName(dllPath), isCollectible: true)
    {
        _dllDir = Path.GetDirectoryName(dllPath) ?? "";
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // 1. Try next to the imported DLL (e.g. SabakaLang.SDK.dll)
        string local = Path.Combine(_dllDir, name.Name + ".dll");
        if (File.Exists(local))
        {
            try { return LoadFromAssemblyPath(local); }
            catch { /* fall through */ }
        }

        // 2. Try the default context (system / already-loaded assemblies)
        try { return Default.LoadFromAssemblyName(name); }
        catch { /* fall through */ }

        // 3. Unknown dep (WPF etc.) — return null, caller catches the
        //    resulting ReflectionTypeLoadException and skips bad types.
        return null;
    }
}