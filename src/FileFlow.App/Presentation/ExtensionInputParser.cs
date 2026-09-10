namespace FileFlow.App.Presentation;

public static class ExtensionInputParser
{
    // This is input tokenization, not rule normalization. Empty entries are deliberately retained.
    public static IReadOnlyList<string> Parse(string text) => text.Split(',', StringSplitOptions.None);
}
