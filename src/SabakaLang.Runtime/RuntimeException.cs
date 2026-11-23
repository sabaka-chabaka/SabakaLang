namespace SabakaLang.Runtime;

/// <summary>An error raised while a script runs (or by a native extension on behalf of the script).</summary>
public sealed class RuntimeException : Exception
{
    public RuntimeException(string message) : base(message) { }
    public RuntimeException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Script call stack at the time of the error, innermost frame first. Filled in by the VM.</summary>
    public IReadOnlyList<string> ScriptStack { get; set; } = [];

    /// <summary>1-based source line, when known.</summary>
    public int Line { get; set; }
}

/// <summary>Thrown by a native function (e.g. <c>exit(code)</c>) to stop the script cleanly.</summary>
public sealed class ScriptExitException(int exitCode) : Exception($"Script exited with code {exitCode}")
{
    public int ExitCode { get; } = exitCode;
}
