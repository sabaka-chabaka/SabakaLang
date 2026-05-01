using SabakaLang.Compiler;
using SabakaLang.Runtime;
using System.IO;
using SabakaLang.Compiler.Compiling;
using SabakaLang.Compiler.Runtime;

namespace SabakaLang.Runtime.UnitTests;

public class CoverageTests : Utilities
{
    [Fact]
    public void VM_Stack_RequireStack_Throws()
    {
        var vm = new VirtualMachine();
        var ex = Assert.Throws<RuntimeException>(() => vm.Execute([new Instruction(OpCode.Pop)]));
        Assert.Contains("need 1", ex.Message);
    }

    [Fact]
    public void VM_Is_InvalidType_Throws()
    {
        // "is: type must be string" is only thrown if it passes binder
        var (vm, sw) = BuildVm();
        var code = new List<Instruction>
        {
            new Instruction(OpCode.Push, Value.FromInt(1)),
            new Instruction(OpCode.Push, Value.FromInt(2)), // Should be string
            new Instruction(OpCode.Is)
        };
        var ex = Assert.Throws<RuntimeException>(() => vm.Execute(code));
        Assert.Contains("is: type must be string", ex.Message);
    }

    [Fact]
    public void VM_Negate_InvalidType_Throws()
    {
        RunError("print(-\"abc\");", "Negate requires number");
    }

    [Fact]
    public void VM_Logic_InvalidType_Throws()
    {
        // These are caught by VM when it gets non-bools. 
        // Need to bypass binder if it checks types (does it?)
        var (vm, sw) = BuildVm();
        Assert.Throws<RuntimeException>(() => vm.Execute([
            new Instruction(OpCode.Push, Value.FromInt(1)),
            new Instruction(OpCode.Push, Value.FromInt(2)),
            new Instruction(OpCode.And)
        ]));
        Assert.Throws<RuntimeException>(() => vm.Execute([
            new Instruction(OpCode.Push, Value.FromInt(1)),
            new Instruction(OpCode.Push, Value.FromInt(2)),
            new Instruction(OpCode.Or)
        ]));
        Assert.Throws<RuntimeException>(() => vm.Execute([
            new Instruction(OpCode.Push, Value.FromInt(1)),
            new Instruction(OpCode.Not)
        ]));
    }

    [Fact]
    public void VM_Declare_AlreadyDeclared_Throws()
    {
        RunError("var x = 1; var x = 2;", "already declared");
    }

    [Fact]
    public void VM_Call_Undefined_Throws()
    {
        // This usually caught by Binder, but VM should also handle it if somehow bypasses binder
        var vm = new VirtualMachine();
        var ex = Assert.Throws<RuntimeException>(() => vm.Execute([new Instruction(OpCode.Call, name: "nonexistent", operand: 0)]));
        Assert.Contains("Undefined function 'nonexistent'", ex.Message);
    }

    [Fact]
    public void VM_PushThis_OutsideContext_Throws()
    {
        var vm = new VirtualMachine();
        var ex = Assert.Throws<RuntimeException>(() => vm.Execute([new Instruction(OpCode.PushThis)]));
        Assert.Contains("No 'this' in current context", ex.Message);
    }

    [Fact]
    public void VM_CallMethod_OnNonObject_Throws()
    {
        RunError("var x = 1; x.toString();", "Cannot call method on Int");
    }

    [Fact]
    public void VM_ArrayLoad_InvalidIndexType_Throws()
    {
        RunError("var a = [1]; print(a[\"0\"]);", "Array index must be int");
    }

    [Fact]
    public void VM_ArrayLoad_String_IndexOutOfRange_Throws()
    {
        RunError("var s = \"abc\"; print(s[5]);", "out of range");
        RunError("var s = \"abc\"; print(s[-1]);", "out of range");
    }

    [Fact]
    public void VM_ArrayLoad_NotArray_Throws()
    {
        RunError("var x = 1; print(x[0]);", "not an array");
    }

    [Fact]
    public void VM_ArrayStore_NotArray_Throws()
    {
        RunError("var x = 1; x[0] = 2;", "not an array");
    }

    [Fact]
    public void VM_ArrayStore_InvalidIndexType_Throws()
    {
        RunError("var a = [1]; a[\"0\"] = 2;", "index must be int");
    }

    [Fact]
    public void VM_ReadLines_FileNotFound_Throws()
    {
        RunError("readLines(\"nonexistent_file_xyz.txt\");", "File not found");
    }

    [Fact]
    public void VM_FileOps_Smoke()
    {
        var path = "test_file_coverage.txt";
        if (File.Exists(path)) File.Delete(path);

        Output($"writeFile(\"{path}\", \"line1\");");
        Assert.True(File.Exists(path));
        Assert.Equal("line1", Output($"print(readFile(\"{path}\"));"));
        
        Output($"appendFile(\"{path}\", \"\\nline2\");");
        var lines = Output($"var l = readLines(\"{path}\"); print(l[0]); print(l[1]);");
        Assert.Contains("line1", lines);
        Assert.Contains("line2", lines);

        Output($"deleteFile(\"{path}\");");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void VM_Time_Smoke()
    {
        var t1_str = Output("print(time());");
        var t1 = double.Parse(t1_str, System.Globalization.CultureInfo.InvariantCulture);
        var t2_str = Output("print(timeMs());");
        var t2 = int.Parse(t2_str, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(t1 > 0);
    }

    [Fact]
    public void VM_ExecEqual_DifferentTypes()
    {
        Assert.Equal("false", Output("print(1 == \"1\");"));
        Assert.Equal("false", Output("print(true == 1);"));
        Assert.Equal("false", Output("print(null == 0);"));
    }

    [Fact]
    public void VM_UnwrapValue_Default_ReturnsNull()
    {
        var (vm, sw) = BuildVm();
        // Since we can't easily call private methods, we use OpCode that uses it
        // Or we check how Instruction is built.
        // Actually, OpCode.Push uses UnwrapValue(instr.Operand)
        vm.Execute([
            new Instruction(OpCode.Push, operand: new object()),
            new Instruction(OpCode.Print)
        ]);
        Assert.Equal("null", sw.ToString().Trim());
    }

    [Fact]
    public void VM_UnwrapStringList_Various()
    {
        // CreateObject uses UnwrapStringList(instr.Extra)
        // extra can be List<string> or IEnumerable
        
        // case 1: List<string>
        var inst1 = new Instruction(OpCode.CreateObject, name: "C", extra: new List<string> { "f1" });
        var (vm, sw) = BuildVm();
        vm.Execute([
            inst1,
            new Instruction(OpCode.Dup),
            new Instruction(OpCode.Push, Value.FromInt(10)),
            new Instruction(OpCode.StoreField, name: "f1"),
            new Instruction(OpCode.LoadField, name: "f1"),
            new Instruction(OpCode.Print)
        ]);
        Assert.Equal("10", sw.ToString().Trim());

        // case 2: IEnumerable of Value
        var extraList = new List<Value> { Value.FromString("f2") };
        var inst2 = new Instruction(OpCode.CreateObject, name: "C2", extra: extraList);
        (vm, sw) = BuildVm();
        vm.Execute([
            inst2,
            new Instruction(OpCode.Dup),
            new Instruction(OpCode.Push, Value.FromInt(20)),
            new Instruction(OpCode.StoreField, name: "f2"),
            new Instruction(OpCode.LoadField, name: "f2"),
            new Instruction(OpCode.Print)
        ]);
        Assert.Equal("20", sw.ToString().Trim());
        
        // case 3: IEnumerable of other things
        var inst3 = new Instruction(OpCode.CreateObject, name: "C3", extra: new object[] { 123 });
        (vm, sw) = BuildVm();
        vm.Execute([
            inst3,
            new Instruction(OpCode.Dup),
            new Instruction(OpCode.Push, Value.FromInt(30)),
            new Instruction(OpCode.StoreField, name: "123"),
            new Instruction(OpCode.LoadField, name: "123"),
            new Instruction(OpCode.Print)
        ]);
        Assert.Equal("30", sw.ToString().Trim());
    }

    [Fact]
    public void VM_CallMethod_Externals()
    {
        var externals = new Dictionary<string, Func<Value[], Value>>
        {
            ["myclass.mymethod"] = (args) => Value.FromInt(args.Length)
        };
        var (vm, sw) = BuildVm(externals: externals);
        
        // We need an object of type "MyClass"
        var code = new List<Instruction>
        {
            new Instruction(OpCode.CreateObject, name: "MyClass"),
            new Instruction(OpCode.Push, Value.FromInt(10)),
            new Instruction(OpCode.Push, Value.FromInt(20)),
            new Instruction(OpCode.CallMethod, name: "myMethod", operand: 2), // 2 args
            new Instruction(OpCode.Print)
        };
        vm.Execute(code);
        Assert.Equal("2", sw.ToString().Trim());
    }

    [Fact]
    public void VM_Ord_Chr_Smoke()
    {
        Assert.Equal("65", Output("print(ord(\"A\"));"));
        Assert.Equal("A", Output("print(chr(65));"));
    }

    [Fact]
    public void VM_CallMethod_ToString_NoOverride()
    {
        var (vm, sw) = BuildVm();
        var code = new List<Instruction>
        {
            new Instruction(OpCode.CreateObject, name: "MyClass"),
            new Instruction(OpCode.CallMethod, name: "toString", operand: 0),
            new Instruction(OpCode.Print)
        };
        vm.Execute(code);
        Assert.Contains("MyClass", sw.ToString());
    }

    [Fact]
    public void VM_Array_Length_String()
    {
        var (vm, sw) = BuildVm();
        vm.Execute([
            new Instruction(OpCode.Push, Value.FromString("abc")),
            new Instruction(OpCode.ArrayLength),
            new Instruction(OpCode.Print)
        ]);
        Assert.Equal("3", sw.ToString().Trim());
    }

    [Fact]
    public void VM_GC_Smoke()
    {
        // Allocation triggers GC check. 
        // We set small threshold to ensure GC runs.
        var src = "class C {} for(var i = 0; i < 1000; i = i + 1) { var x = new C(); } print(\"done\");";
        Assert.Equal("done", Output(src, gcThreshold: 10));
    }
}
