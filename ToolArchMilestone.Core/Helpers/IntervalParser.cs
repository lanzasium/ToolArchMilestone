using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ToolArchMilestone.Core.Helpers
{
    public static class IntervalParser
    {
        public static List<(DateTime Start, DateTime End)> ParseContent(string content)
        {
            var results = new List<(DateTime Start, DateTime End)>();
            if (string.IsNullOrWhiteSpace(content)) return results;

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (TryParseLine(line, out var start, out var end))
                {
                    results.Add((start, end));
                }
            }

            return results;
        }

        private static bool TryParseLine(string line, out DateTime start, out DateTime end)
        {
            start = default;
            end = default;

            // Common separators
            var separators = new[] { " - ", " -> ", "->", ";", "\t", "|" };
            string[] parts = null;

            foreach (var sep in separators)
            {
                if (line.Contains(sep))
                {
                    parts = line.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) break;
                }
            }

            if (parts != null && parts.Length >= 2)
            {
                var s1 = parts[0].Trim();
                var s2 = parts[1].Trim();

                if (ParseDate(s1, out start) && ParseDate(s2, out end))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ParseDate(string input, out DateTime date)
        {
            // Try standard formats + Italian specific
            // We include many formats to be "smart"
            var formats = new[]
            {
                "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm",
                "dd/MM/yyyy",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm",
                "yyyy/MM/dd HH:mm",
            };

            // Try Italian culture first
            if (DateTime.TryParse(input, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out date))
                return true;

            // Try Invariant/Specific formats
            if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;

            // Last resort generic
            return DateTime.TryParse(input, out date);
        }
    }
}
