namespace SabakaLang.Runtime.UnitTests;

public class ArrayTests : Utilities
{
    [Fact]
    public void ArrayLiteral_CreatesCorrectly()
        => Assert.Equal("3", Output("int[] a = [1, 2, 3]; print(a.length);"));
 
    [Fact]
    public void EmptyArray_HasZeroLength()
        => Assert.Equal("0", Output("int[] a = []; print(a.length);"));
    
    [Fact]
    public void ArrayLoad_ReadsCorrectElement()
        => Assert.Equal("20", Output("int[] a = [10, 20, 30]; print(a[1]);"));
 
    [Fact]
    public void ArrayStore_UpdatesElement()
        => Assert.Equal("99", Output("int[] a = [1, 2, 3]; a[2] = 99; print(a[2]);"));
 
    [Fact]
    public void ArrayStore_GrowsArray_WhenIndexBeyondSize()
    {
        var src = """
                  int[] a = [0];
                  a[5] = 42;
                  print(a[5]);
                  """;
        Assert.Equal("42", Output(src));
    }
 
    [Fact]
    public void ArrayLoad_OutOfBounds_Throws()
        => RunError("int[] a = [1, 2]; print(a[5]);", "out of bounds");
 
    [Fact]
    public void ArrayLoad_NegativeIndex_Throws()
        => RunError("int[] a = [1]; print(a[-1]);", "out of bounds");
    
    [Fact]
    public void StringIndex_ReturnsCharacter()
        => Assert.Equal("e", Output("string s = \"hello\"; print(s[1]);"));
 
    [Fact]
    public void StringLength_ReturnsCount()
        => Assert.Equal("5", Output("print(\"hello\".length);"));
 
    [Fact]
    public void StringIndex_OutOfBounds_Throws()
        => RunError("string s = \"hi\"; print(s[10]);", "out of range");
    
    [Fact]
    public void ForLoop_IteratesArray()
    {
        var src = """
                  int[] a = [1, 2, 3, 4, 5];
                  int s = 0;
                  for (int i = 0; i < a.length; i = i + 1) {
                      s = s + a[i];
                  }
                  print(s);
                  """;
        Assert.Equal("15", Output(src));
    }
 
    [Fact]
    public void Foreach_CollectsStrings()
    {
        var src = """
                  string[] words = ["foo", "bar", "baz"];
                  string result = "";
                  foreach (string w in words) {
                      result = result + w;
                  }
                  print(result);
                  """;
        Assert.Equal("foobarbaz", Output(src));
    }
    
    [Fact]
    public void Function_ReturnsArray()
    {
        var src = """
                  int[] make() {
                      int[] a = [7, 8, 9];
                      return a;
                  }
                  int[] r = make();
                  print(r[0]);
                  print(r[2]);
                  """;
        Assert.Equal(["7\r", "9"], Lines(src));
    }
    
    [Fact]
    public void Ord_ReturnsAsciiCode()
        => Assert.Equal("65", Output("print(ord(\"A\"));"));
 
    [Fact]
    public void Chr_ReturnsCharacter()
        => Assert.Equal("Z", Output("print(chr(90));"));
 
    [Fact]
    public void OrdChr_RoundTrip()
        => Assert.Equal("K", Output("print(chr(ord(\"K\")));"));
 
    [Fact]
    public void Ord_EmptyString_Throws()
        => RunError("ord(\"\");", "empty string");
}