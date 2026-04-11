namespace SabakaLang.Runtime;

public sealed class VirtualMachine
{
    
}

public sealed class FunctionInfo
{
    public int           Address    { get; }
    public List<string>  Parameters { get; }
 
    public FunctionInfo(int address, List<string> parameters)
    {
        Address    = address;
        Parameters = parameters;
    }
}
