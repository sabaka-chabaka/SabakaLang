namespace SabakaLang.SDK;

/// <summary>
/// Implement this interface to create a SabakaLang native library (.dll).
///
/// Usage:
///   public class MyLib : ISabakaModule
///   {
///       [SabakaExport("greet")]
///       public string Greet(string name) => $"Hello, {name}!";
///
///       [SabakaExport("PI")]
///       public double PI => 3.14159265358979;
///   }
///
/// In SabakaLang:
///   import "MyLib.dll" as mylib;
///   string msg = mylib.greet("World");
///   float pi   = mylib.PI;
/// </summary>
public interface ISabakaModule
{
    // Marker interface — no required members.
    // All exports are declared via [SabakaExport] attributes.
}