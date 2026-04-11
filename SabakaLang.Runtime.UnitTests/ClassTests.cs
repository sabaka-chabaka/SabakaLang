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
 
    [Fact]
    public void Field_DefaultInt_IsZero()
    {
        var src = """
                  class Counter { int count; }
                  Counter c = new Counter();
                  print(c.count);
                  """;
        Assert.Equal("0", Output(src));
    }
 
    [Fact]
    public void Field_WithInitializer()
    {
        var src = """
                  class Box { int value = 42; }
                  Box b = new Box();
                  print(b.value);
                  """;
        Assert.Equal("42", Output(src));
    }
 
    [Fact]
    public void Field_Assign_UpdatesValue()
    {
        var src = """
                  class Pt { int x; }
                  Pt p = new Pt();
                  p.x = 99;
                  print(p.x);
                  """;
        Assert.Equal("99", Output(src));
    }
    
    [Fact]
    public void Constructor_InitializesFields()
    {
        var src = """
                  class Person {
                      string name;
                      void Person(string n) {
                          name = n;
                      }
                  }
                  Person p = new Person("Alice");
                  print(p.name);
                  """;
        Assert.Equal("Alice", Output(src));
    }
 
    [Fact]
    public void Constructor_MultipleParams()
    {
        var src = """
                  class Vec {
                      int x;
                      int y;
                      void Vec(int px, int py) {
                          x = px;
                          y = py;
                      }
                  }
                  Vec v = new Vec(3, 4);
                  print(v.x);
                  print(v.y);
                  """;
        Assert.Equal(["3", "4"], Lines(src));
    }
    
    [Fact]
    public void Method_VoidCallWorks()
    {
        var src = """
                  class Dog {
                      void bark() { print("woof"); }
                  }
                  Dog d = new Dog();
                  d.bark();
                  """;
        Assert.Equal("woof", Output(src));
    }
 
    [Fact]
    public void Method_ReadsThis()
    {
        var src = """
                  class Counter {
                      int count = 0;
                      void inc() { count = count + 1; }
                      int get() { return count; }
                  }
                  Counter c = new Counter();
                  c.inc();
                  c.inc();
                  c.inc();
                  print(c.get());
                  """;
        Assert.Equal("3", Output(src));
    }
 
    [Fact]
    public void Method_ReturnsValue()
    {
        var src = """
                  class Calc {
                      int double(int n) { return n * 2; }
                  }
                  Calc c = new Calc();
                  print(c.double(21));
                  """;
        Assert.Equal("42", Output(src));
    }
 
    [Fact]
    public void Method_ChainedCalls()
    {
        var src = """
                  class Builder {
                      string s = "";
                      void add(string part) { s = s + part; }
                      string build() { return s; }
                  }
                  Builder b = new Builder();
                  b.add("hello");
                  b.add(" ");
                  b.add("world");
                  print(b.build());
                  """;
        Assert.Equal("hello world", Output(src));
    }
    
    [Fact]
    public void Inheritance_DerivedHasBaseField()
    {
        var src = """
                  class Animal { string name; }
                  class Dog : Animal {}
                  Dog d = new Dog();
                  d.name = "Rex";
                  print(d.name);
                  """;
        Assert.Equal("Rex", Output(src));
    }
 
    [Fact]
    public void Inheritance_DerivedCallsBaseMethod()
    {
        var src = """
                  class Animal {
                      void speak() { print("..."); }
                  }
                  class Dog : Animal {
                      override void speak() { print("woof"); }
                  }
                  Dog d = new Dog();
                  d.speak();
                  """;
        Assert.Equal("woof", Output(src));
    }
 
    [Fact]
    public void Inheritance_BaseMethodCalledWhenNotOverridden()
    {
        var src = """
                  class Animal {
                      void breathe() { print("inhale"); }
                  }
                  class Dog : Animal {}
                  Dog d = new Dog();
                  d.breathe();
                  """;
        Assert.Equal("inhale", Output(src));
    }
 
    [Fact]
    public void Super_CallsBaseConstructor()
    {
        var src = """
                  class Animal {
                      string kind;
                      void Animal(string k) { kind = k; }
                  }
                  class Dog : Animal {
                      void Dog(string k) { super.Animal(k); }
                  }
                  Dog d = new Dog("canine");
                  print(d.kind);
                  """;
        Assert.Equal("canine", Output(src));
    }
 
    [Fact]
    public void Super_CallsBaseMethod()
    {
        var src = """
                  class Animal {
                      void speak() { print("base"); }
                  }
                  class Dog : Animal {
                      override void speak() {
                          super.speak();
                          print("woof");
                      }
                  }
                  Dog d = new Dog();
                  d.speak();
                  """;
        Assert.Equal(["base", "woof"], Lines(src));
    }
    
    [Fact]
    public void MultipleInstances_IndependentState()
    {
        var src = """
                  class Counter {
                      int n = 0;
                      void inc() { n = n + 1; }
                  }
                  Counter a = new Counter();
                  Counter b = new Counter();
                  a.inc(); a.inc();
                  b.inc();
                  print(a.n);
                  print(b.n);
                  """;
        Assert.Equal(["2", "1"], Lines(src));
    }
    
    [Fact]
    public void This_ReturnsCurrentObject()
    {
        var src = """
                  class Foo {
                      int x = 7;
                      int getX() { return this.x; }
                  }
                  Foo f = new Foo();
                  print(f.getX());
                  """;
        Assert.Equal("7", Output(src));
    }
    
    [Fact]
    public void Interface_ImplementingClass_MethodCalled()
    {
        var src = """
                  interface IGreeter { void greet(); }
                  class Hello : IGreeter {
                      void greet() { print("hello!"); }
                  }
                  Hello h = new Hello();
                  h.greet();
                  """;
        Assert.Equal("hello!", Output(src));
    }
}