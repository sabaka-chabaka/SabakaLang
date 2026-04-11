namespace SabakaLang.Runtime.UnitTests;

public class ClassTests : Utilities
{
    [Fact]
    public void NewObject_CreatesInstance()
    {
        var src = """
                  class Dog {}
                  Dog d = new Dog();
                  print("ok");
                  """;
        Assert.Equal("ok", Output(src));
    }
}