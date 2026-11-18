using System.Text;

namespace Dante.Server.Features.Command;

/// <summary>
/// Turns the "Program" and "Arguments" boxes into what to start. The Program box may hold a whole
/// command line (<c>sh -c</c>, <c>pwsh -NoProfile -File "my script.ps1"</c>): its first word is the
/// program and the rest come before the Arguments box's lines (one argument per line).
/// </summary>
public static class CommandLine
{
    public static (string File, IReadOnlyList<string> Args) Resolve(string program, string? argumentLines)
    {
        program = program.Trim();
        var lines = (argumentLines ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unwrap).ToArray();

        // A real path with spaces in it ("C:\Program Files\Tool\tool.exe") is a program, not a command line.
        if (File.Exists(program)) return (program, lines);

        var words = Split(program);
        if (words.Count == 0) return (program, lines);
        return (words[0], [.. words.Skip(1), .. lines]);
    }

    /// <summary>
    /// Each line already is one argument, so quotes around a whole line ("top -bn1 | grep x") are
    /// what people type for a shell, not part of the argument: drop them. Quotes inside stay.
    /// </summary>
    public static string Unwrap(string line) =>
        line.Length >= 2 && (line[0] is '"' or '\'') && line[^1] == line[0] && line.IndexOf(line[0], 1) == line.Length - 1
            ? line[1..^1]
            : line;

    /// <summary>Splits on spaces, keeping "double" or 'single' quoted parts together.</summary>
    public static List<string> Split(string commandLine)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        char? quote = null;
        var inWord = false;
        foreach (var c in commandLine)
        {
            if (quote is not null)
            {
                if (c == quote) quote = null;
                else word.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord) { words.Add(word.ToString()); word.Clear(); inWord = false; }
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }
        if (inWord) words.Add(word.ToString());
        return words;
    }
}
