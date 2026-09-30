namespace Signal.Application.Commands;

/// <summary>Finds the closest command name for "Did you mean …?" replies.</summary>
internal static class CommandSuggestions
{
    /// <summary>
    /// The candidate closest to <paramref name="typed"/>, compared case-insensitively. A candidate qualifies when it
    /// is at most 1 edit away (2 for names of 6 or more characters) and fewer edits away than the typed length, so
    /// very short input never matches unrelated one-letter names. Ties go to the shorter, then alphabetically first,
    /// candidate.
    /// </summary>
    /// <param name="typed">What the user typed.</param>
    /// <param name="candidates">Visible command, alias or group names.</param>
    /// <returns>The suggestion, or <see langword="null"/> if nothing is close enough.</returns>
    public static string? Closest(string typed, IEnumerable<string> candidates)
    {
        var maxDistance = Math.Min(typed.Length >= 6 ? 2 : 1, typed.Length - 1);
        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => (Candidate: candidate, Distance: Distance(typed, candidate)))
            .Where(c => c.Distance > 0 && c.Distance <= maxDistance)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Candidate.Length)
            .ThenBy(c => c.Candidate, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Candidate)
            .FirstOrDefault();
    }

    /// <summary>
    /// Optimal string alignment distance (Levenshtein plus transpositions of adjacent characters), case-insensitive:
    /// <c>hlep</c> → <c>help</c> is one edit.
    /// </summary>
    internal static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        // Three rolling rows: two back (for transpositions), previous and current.
        var twoBack = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    current[j] = Math.Min(current[j], twoBack[j - 2] + 1);
                }
            }

            (twoBack, previous, current) = (previous, current, twoBack);
        }

        return previous[b.Length];
    }
}
