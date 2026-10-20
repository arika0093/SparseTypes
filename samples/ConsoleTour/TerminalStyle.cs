// This is a helper class to add color to console output.
// It uses ANSI escape sequences, which are supported by most modern terminals.
// The class checks if the output is redirected or if the NO_COLOR environment variable is set,
// in which case it will not apply any color formatting.
internal static class TerminalStyle
{
    private const string Reset = "\u001b[0m";
    private static readonly bool UseAnsi =
        !Console.IsOutputRedirected
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
        && !string.Equals(
            Environment.GetEnvironmentVariable("TERM"),
            "dumb",
            StringComparison.OrdinalIgnoreCase
        );

    public static string Title(string text) => Apply(text, "\u001b[1;36m");

    public static string Step(string text) => Apply(text, "\u001b[1;33m");

    public static string Comment(string text) => Apply(text, "\u001b[90m");

    public static string Emphasis(string text) => Apply(text, "\u001b[1m");

    public static string Value(string text) => Apply(text, "\u001b[32m");

    public static string Before(object? value) =>
        Apply(value?.ToString() ?? "<null>", "\u001b[31m");

    public static string After(object? value) => Apply(value?.ToString() ?? "<null>", "\u001b[32m");

    public static string State(string text, bool isPositive) =>
        Apply(text, isPositive ? "\u001b[32m" : "\u001b[90m");

    private static string Apply(string text, string sequence) =>
        UseAnsi ? $"{sequence}{text}{Reset}" : text;
}
