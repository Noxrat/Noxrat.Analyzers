namespace Noxrat.Sandbox.Tests;

public static partial class CodeGenStringConstantOther
{
    public const string KEY1 = "some_other_ke";

    public static string Test()
    {
        return CodeGenStringConstant.BAKED_STRING;
    }
}
