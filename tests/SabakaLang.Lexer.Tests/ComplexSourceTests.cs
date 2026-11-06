using SabakaLang.Lexer.Enums;

namespace SabakaLang.Lexer.Tests;

public class ComplexSourceTests
{
    [Fact]
    public void Tokenize_FunctionDeclarationAndReturn_TokenizesSuccessfully()
    {
        var source = @"
            import std::io;

            public int add(int a, int b) {
                return a + b;
            }
        ";

        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Import);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.ColonColon);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Public);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Return);
        Assert.Equal(TokenType.Eof, result.Tokens[^1].Type);
    }

    [Fact]
    public void Tokenize_ControlFlowAndLoops_TokenizesSuccessfully()
    {
        var source = @"
            if (x > 0 && y <= 10) {
                while (count != 0) {
                    count--;
                }
            } else {
                foreach (item in collection) {
                    if (item is null) {
                        break;
                    }
                }
            }
        ";

        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.If);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.AndAnd);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.While);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.MinusMinus);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Else);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Foreach);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.In);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Is);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Null);
    }

    [Fact]
    public void Tokenize_SwitchCaseConstruct_TokenizesSuccessfully()
    {
        var source = @"
            switch (value) {
                case 1:
                    return true;
                default:
                    return false;
            }
        ";

        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Switch);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Case);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Colon);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Default);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.True);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.False);
    }

    [Fact]
    public void Tokenize_ClassAndInterfaceHierarchy_TokenizesSuccessfully()
    {
        var source = @"
            interface IGreeter {
                void greet();
            }

            class Person : super {
                protected const string name = ""Rex"";
                
                override void greet() {
                    var greeting = $""Hello {name}"";
                }
            }
        ";

        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Interface);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.VoidKeyword);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Class);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Super);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Protected);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Const);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.Override);
        Assert.Contains(result.Tokens, t => t.Type == TokenType.InterpolatedStringLiteral);
    }
}
