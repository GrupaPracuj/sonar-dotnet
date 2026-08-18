/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

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

    // A trailing qualifier that turns the name into a pointer at, or a fact about, the secret rather than the secret
    // itself: apiKeyId, credentialReference, secretUri, tokenType, passwordLength. These are exactly the shapes the
    // secret rules recommend as the fix, so matching them would punish the remediation.
    private static readonly HashSet<string> PointerQualifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "ids", "reference", "references", "ref", "name", "names", "count", "length", "uri", "url", "path", "type"
    };

    internal static bool ContainsSecretWord(string identifier) =>
        ContainsAnyWord(identifier, SecretWords) && !IsPointerToSecret(identifier);

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

    private static bool IsPointerToSecret(string identifier) =>
        SplitWords(identifier).LastOrDefault() is { } last && PointerQualifiers.Contains(last);

    internal static IEnumerable<string> SplitWords(string identifier)
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

    // Exact wrong->right spellings from the "Capitalizing Compound Words and Common Terms" table at
    // https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/capitalization-conventions -
    // deliberately a small fixed list, not a general spell-checker, so it can never produce a false positive
    // on an unrelated word.
    private static readonly Dictionary<string, string> SingleWordFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cancelled"] = "Canceled",
        ["EMail"] = "Email",
        ["ID"] = "Id",
        ["OK"] = "Ok",
        ["PI"] = "Pi",
        ["Writeable"] = "Writable",
    };

    // A single (wrongly merged or wrongly left merged) word that should be split into two words.
    private static readonly Dictionary<string, (string First, string Second)> SplitWordFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bitflag"] = ("Bit", "Flag"),
        ["Filename"] = ("File", "Name"),
        ["Username"] = ("User", "Name"),
        ["Whitespace"] = ("White", "Space"),
    };

    // Two adjacent words that should be merged into one.
    private static readonly Dictionary<(string First, string Second), string> MergeWordFixes = new()
    {
        [("Call", "Back")] = "Callback",
        [("End", "Point")] = "Endpoint",
        [("Grid", "Line")] = "Gridline",
        [("Hash", "Table")] = "Hashtable",
        [("Meta", "Data")] = "Metadata",
        [("Multi", "Panel")] = "Multipanel",
        [("Multi", "View")] = "Multiview",
        [("Name", "Space")] = "Namespace",
        [("Place", "Holder")] = "Placeholder",
    };

    /// <summary>
    /// Looks for exactly one of a small set of well-known wrong compound-word spellings (Microsoft's own
    /// "Capitalizing Compound Words and Common Terms" table) inside <paramref name="identifier"/>. Returns true
    /// and the corrected identifier if found; the corrected identifier always differs from the original.
    /// </summary>
    internal static bool TryFixCompoundWord(string identifier, out string suggested)
    {
        var words = SplitWords(identifier).ToList();
        var normalized = words.Select(Capitalize).ToList();

        for (var i = 0; i < normalized.Count - 1; i++)
        {
            if (MergeWordFixes.TryGetValue((normalized[i], normalized[i + 1]), out var merged))
            {
                var newWords = new List<string>(words);
                newWords[i] = merged;
                newWords.RemoveAt(i + 1);
                if (TryBuildIdentifier(newWords, identifier, out suggested))
                {
                    return true;
                }
            }
        }

        for (var i = 0; i < normalized.Count; i++)
        {
            if (SingleWordFixes.TryGetValue(normalized[i], out var fixedWord))
            {
                var newWords = new List<string>(words);
                newWords[i] = fixedWord;
                if (TryBuildIdentifier(newWords, identifier, out suggested))
                {
                    return true;
                }
            }
        }

        for (var i = 0; i < normalized.Count; i++)
        {
            if (SplitWordFixes.TryGetValue(normalized[i], out var pair))
            {
                var newWords = new List<string>(words);
                newWords.RemoveAt(i);
                newWords.Insert(i, pair.Second);
                newWords.Insert(i, pair.First);
                if (TryBuildIdentifier(newWords, identifier, out suggested))
                {
                    return true;
                }
            }
        }

        suggested = null;
        return false;
    }

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);

    // False only if, after reassembly, the "fix" would be a no-op (e.g. a dictionary lookup matched
    // case-insensitively but the identifier already had the correct casing) - guards against ever reporting
    // an issue whose suggested fix is identical to the original name.
    private static bool TryBuildIdentifier(List<string> words, string originalIdentifier, out string result)
    {
        var leadingUnderscoreCount = originalIdentifier.TakeWhile(x => x == '_').Count();
        var candidate = string.Concat(words.Select(Capitalize));
        if (originalIdentifier.Length > leadingUnderscoreCount
            && char.IsLower(originalIdentifier[leadingUnderscoreCount])
            && candidate.Length > 0)
        {
            candidate = char.ToLowerInvariant(candidate[0]) + candidate.Substring(1);
        }

        result = originalIdentifier.Substring(0, leadingUnderscoreCount) + candidate;
        return !string.Equals(result, originalIdentifier, StringComparison.Ordinal);
    }
}
