using System.Collections.Generic;

namespace Tests.Diagnostics
{
    public class DictionaryLookupShouldUseTryAdd
    {
        public void SingleStatement(Dictionary<string, int> dict, string key, int value)
        {
            dict.TryAdd(key, value); // Fixed
        }

        public void BlockStatement(Dictionary<string, int> dict, string key, int value)
        {
            dict.TryAdd(key, value);
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
