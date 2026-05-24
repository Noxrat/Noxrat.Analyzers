using System;

namespace Noxrat.Analyzers;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class BakeStringConstantAttribute : Attribute
{
    public string VariableName { get; } = "BAKED_STRING";
    public string[]? ListOfFields { get; }
    public string Format { get; }

    public BakeStringConstantAttribute(string format, params string[] listOfFields)
    {
        ListOfFields = listOfFields;
        Format = format;
    }

    public BakeStringConstantAttribute(
        string variableName,
        string format,
        params string[] listOfFields
    )
        : this(format, listOfFields)
    {
        VariableName = variableName;
    }
}
