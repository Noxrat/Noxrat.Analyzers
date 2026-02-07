namespace WrongNamespace;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Noxrat.Analyzer",
    "Noxrat0000:Namespace does not match rule",
    Justification = "Test Reports warning successfully"
)]
public class TestTypeWithNoNamespace { }
