using Noxrat.Analyzers;

namespace Noxrat.Sandbox.Tests;

public static class RequiresAttributeNotDefined
{
    private static void TestGenericFunction<[RequiresAttribute(typeof(TypeeeAttribute))] T>(
        T objectToCheck
    ) { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Noxrat.Analyzer",
        "Noxrat0001:Requires attribute on the type is not found",
        Justification = "Test Reports warning successfully"
    )]
    private static void TestingObjectWithoutAttributeCall()
    {
        SomeObject obj = new SomeObject();
        TestGenericFunction(obj);
    }

    private static void TestingObjectWithAttributeCall()
    {
        SomeOtherObject obj2 = new SomeOtherObject();
        TestGenericFunction(obj2);
    }

    private static void TestANDGenericFunction<
        [RequiresAttribute(typeof(TypeeeAttribute))] [RequiresAttribute(typeof(Typeee2Attribute))] T
    >(T objectToCheck) { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Noxrat.Analyzer",
        "Noxrat0001:Requires attribute on the type is not found",
        Justification = "Test Reports warning successfully"
    )]
    private static void TestingObjectWithAttributeCallMultiAttribute()
    {
        SomeOtherObject obj2 = new SomeOtherObject();
        TestANDGenericFunction(obj2);
    }

    #region Defenitions
    private class SomeObject { }

    [Typeee]
    private class SomeOtherObject { }

    [Typeee2]
    [Typeee]
    private class ObjectWithAllAttributes { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class TypeeeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class Typeee2Attribute : Attribute { }
    #endregion
}
