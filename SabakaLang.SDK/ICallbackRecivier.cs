namespace SabakaLang.SDK;

/// <summary>
/// Implement alongside ISabakaModule to receive a callback delegate
/// that lets you invoke SabakaLang functions from C# (e.g. UI event handlers).
///
/// The delegate signature:
///   string InvokeCallback(string functionName, string[] args)
///
/// The returned string is the ToString() of the function's return value,
/// or empty string for void functions.
///
/// This is set by the SabakaRunner BEFORE Execute() is called.
/// </summary>
public interface ICallbackReceiver
{
    /// <summary>
    /// Set by the runtime. Call this to invoke a SabakaLang function by name.
    /// functionName must match exactly as declared in the script (case-sensitive).
    /// args are converted from string to the expected types automatically.
    /// </summary>
    Func<string, string[], string>? InvokeCallback { get; set; }
}