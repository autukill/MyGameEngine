namespace GameEngine.Tools.AssetCompiler;

using System.Text;
using System.Text.Json;

internal static class GeneratedCodeUtilities
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    };

    public static string ToIdentifier(string logicalName, string kind)
    {
        var result = new StringBuilder(logicalName.Length + 1);
        bool startOfWord = true;
        foreach (char character in logicalName)
        {
            if (!char.IsLetterOrDigit(character))
            {
                startOfWord = true;
                continue;
            }
            if (result.Length == 0 && char.IsDigit(character)) result.Append('_');
            result.Append(startOfWord ? char.ToUpperInvariant(character) : character);
            startOfWord = false;
        }
        if (result.Length == 0)
            throw new InvalidDataException(
                $"{kind} name '{logicalName}' cannot be represented as a C# identifier.");
        return result.ToString();
    }

    public static void ValidateNamespace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string[] segments = value.Split('.');
        if (segments.Length == 0 || segments.Any(segment => !IsValidIdentifier(segment)))
            throw new ArgumentException(
                $"Generated namespace '{value}' is not a valid C# namespace.",
                nameof(value));
    }

    public static void ValidateIdentifier(string value, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsValidIdentifier(value))
            throw new ArgumentException($"{fieldName} '{value}' is not a valid C# identifier.");
    }

    public static string Literal(string value) => JsonSerializer.Serialize(value);

    public static bool WriteIfChanged(string outputFile, string source)
    {
        if (File.Exists(outputFile) &&
            StringComparer.Ordinal.Equals(File.ReadAllText(outputFile), source))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        string temporary = outputFile + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, source, new UTF8Encoding(false));
            File.Move(temporary, outputFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return true;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0 || CSharpKeywords.Contains(value) ||
            !(value[0] == '_' || char.IsLetter(value[0])))
        {
            return false;
        }
        return value.Skip(1).All(character => character == '_' || char.IsLetterOrDigit(character));
    }
}
