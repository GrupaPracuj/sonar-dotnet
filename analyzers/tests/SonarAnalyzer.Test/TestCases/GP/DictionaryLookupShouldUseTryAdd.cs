using System.Collections.Generic;

namespace Tests.Diagnostics
{
    public class DictionaryLookupShouldUseTryAdd
    {
        public void SingleStatement(Dictionary<string, int> dict, string key, int value)
        {
            if (!dict.ContainsKey(key)) dict.Add(key, value); // Noncompliant {{Use 'TryAdd' instead of checking 'ContainsKey' before 'Add' - it does the lookup once instead of twice.}}
        }

        public void BlockStatement(Dictionary<string, int> dict, string key, int value)
        {
            if (!dict.ContainsKey(key)) // Noncompliant
            {
                dict.Add(key, value);
            }
        }

        public void Compliant(Dictionary<string, int> dict, string key, int value)
        {
            if (dict.ContainsKey(key))
            {
                dict.Add(key, value);
            }
        }
    }
}
