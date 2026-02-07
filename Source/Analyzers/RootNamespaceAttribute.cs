using System;

namespace Noxrat.Analyzers;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class RootNamespaceAttribute : Attribute
{
    public string rootNamespace { get; }
    public int folderTraversalDepth { get; set; }

    public RootNamespaceAttribute(string rootNamespace)
    {
        this.rootNamespace = rootNamespace;
    }
}
