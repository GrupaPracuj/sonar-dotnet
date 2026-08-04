namespace SonarAnalyzer.CSharp.Rules;

internal static class GpIdentifierWords
{
    // "token" already covers accessToken/refreshToken, and "secret" covers clientSecret, because matching is done
    // per word rather than on the whole identifier.
    private static readonly HashSet<string> SecretWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "secret", "token", "apikey", "credential", "credentials", "privatekey", "connectionstring", "bearer", "sessionid"
    };

    private static readonly HashSet<string> PiiWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Deliberately no three-letter abbreviations: matching is per word, so a short token like "nip" fires on any
        // identifier that happens to contain it and drowns the real findings in noise.
        "email", "pesel", "phone", "iban", "surname", "firstname", "lastname", "birthdate", "dateofbirth", "creditcard"
    };

    internal static bool ContainsWord(string identifier, string word) =>
        SplitWords(identifier).Any(x => x.Equals(word, StringComparison.OrdinalIgnoreCase));

    internal static bool ContainsSecretWord(string identifier) =>
        ContainsAnyWord(identifier, SecretWords);

    internal static bool ContainsPiiWord(string identifier) =>
        ContainsAnyWord(identifier, PiiWords);

    // Method names are PascalCase (e.g. "CreateOrder"): extract the leading capitalized word ("Create").
    internal static string LeadingWord(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || !char.IsUpper(identifier[0]))
        {
            return string.Empty;
        }

        var end = 1;
        while (end < identifier.Length && char.IsLower(identifier[end]))
        {
            end++;
        }

        return identifier.Substring(0, end);
    }

    // A keyword may be spread across several adjacent camelCase/PascalCase words: "ApiKey" -> "Api" + "Key" ->
    // "apikey", "DateOfBirth" -> "Date" + "Of" + "Birth" -> "dateofbirth".
    private const int MaxJoinedWords = 3;

    private static bool ContainsAnyWord(string identifier, HashSet<string> words)
    {
        var tokens = SplitWords(identifier).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            var joined = string.Empty;
            for (var length = 0; length < MaxJoinedWords && i + length < tokens.Length; length++)
            {
                joined += tokens[i + length];
                if (words.Contains(joined))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitWords(string identifier)
    {
        var start = 0;
        for (var i = 1; i < identifier.Length; i++)
        {
            if ((char.IsUpper(identifier[i]) && !char.IsUpper(identifier[i - 1])) || identifier[i] == '_')
            {
                yield return identifier.Substring(start, i - start).Trim('_');
                start = i;
            }
        }

        yield return identifier.Substring(start).Trim('_');
    }
}
