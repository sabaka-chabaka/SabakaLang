namespace SabakaLang.Compiler.UnitTests;

public class CompilerTests
{
    private static CompileResult Compile(string source)
    {
        var lex    = new Lexer(source).Tokenize();
        var parse  = new Parser(lex).Parse();
        if (parse.HasErrors)
            throw new Exception("Parse errors:\n" +
                                string.Join("\n", parse.Errors.Select(e => e.Message)));
        return new Compiler().Compile(parse.Statements);
    }
    
    private static IReadOnlyList<Instruction> Code(string source)
    { 
        var r = Compile(source);
        Assert.False(r.HasErrors,
            "Compile errors:\n" + string.Join("\n", r.Errors.Select(e => e.ToString())));
        return r.Code;
    }
    
    private static bool Has(IReadOnlyList<Instruction> code, OpCode op)
        => code.Any(i => i.OpCode == op);
 
    private static IEnumerable<Instruction> All(IReadOnlyList<Instruction> code, OpCode op)
        => code.Where(i => i.OpCode == op);
 
    private static Instruction First(IReadOnlyList<Instruction> code, OpCode op)
        => code.First(i => i.OpCode == op);
 
    private static void AssertNoErrors(CompileResult r)
        => Assert.False(r.HasErrors,
            string.Join("\n", r.Errors.Select(e => e.ToString())));

    [Fact]
    public void IntLiteral_EmitsPush()
    {
        var code = Code("42");
        var push = code.OfType<Instruction>().First(i => i.OpCode == OpCode.Push);
        
        Assert.Equal(Value.FromInt(42), push.Operand);
    }
    
    [Fact]
    public void FloatLiteral_EmitsPush()
    {
        var code = Code("3.14");
        var pushes = All(code, OpCode.Push).ToList();
        Assert.Contains(pushes, p => p.Operand is Value v && v.Type == SabakaType.Float);
    }

    [Fact]
    public void StringLiteral_EmitsPush()
    {
        var code = Code("\"hello\"");
        Assert.Contains(All(code, OpCode.Push), p => p.Operand is Value v && v.Type == SabakaType.String && v.String == "hello");
    }

    [Fact]
    public void BoolTrue_EmitsPushTrue()
    {
        var code = Code("true");
        Assert.Contains(All(code, OpCode.Push), p => p.Operand is Value v && v.Type == SabakaType.Bool && v.Bool == true);
    }
    
    [Fact]
    public void BoolFalse_EmitsPushFalse()
    {
        var code = Code("false");
        Assert.Contains(All(code, OpCode.Push), p => p.Operand is Value v && v.Type == SabakaType.Bool && v.Bool == false);
    }
}