using System;

namespace Noxrat.Analyzers;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class RootNamespaceAttribute : Attribute
{
    public string RootNamespace { get; }
    public int FolderTraversalDepth { get; set; }

    public RootNamespaceAttribute(string rootNamespace, int folderTraversalDepth = 0)
    {
        this.RootNamespace = rootNamespace;
        this.FolderTraversalDepth = folderTraversalDepth;
    }
}
