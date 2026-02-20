using System;

namespace SabakaLang;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SabakaExportAttribute : Attribute
{
    public string Name { get; }

    public SabakaExportAttribute(string name)
    {
        Name = name;
    }
}