using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Requestarr.Core.Provisioning
{
    // Replaces ${NAME} and ${NAME:-default} with environment values so the provisioning
    // file can be committed while secrets stay in a local, untracked environment file.
    // $${NAME} is an escaped literal.
    public static class EnvironmentSubstitution
    {
        private static readonly Regex VariableRegex = new(@"\$(\$)?\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?\}", RegexOptions.Compiled);

        public static string Substitute(string text, Func<string, string> lookup, List<string> missing)
        {
            return VariableRegex.Replace(text, match =>
            {
                if (match.Groups[1].Success)
                {
                    return match.Value.Substring(1);
                }

                var name = match.Groups[2].Value;
                var value = lookup(name);

                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }

                if (match.Groups[3].Success)
                {
                    return match.Groups[3].Value;
                }

                missing.Add(name);
                return string.Empty;
            });
        }
    }
}
