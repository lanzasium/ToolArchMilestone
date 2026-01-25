using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ToolArchMilestone.Core.Models;

namespace ToolArchMilestone.Core.Helpers
{
    public static class DateHelper
    {
        public static DateTime ParseDateText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return default(DateTime);

            text = TextHelper.NormalizeTimeSeparators(text.Trim());

            string[] formats = { "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                return parsed.Year < 1900 ? default(DateTime) : parsed;

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
                return parsed.Year < 1900 ? default(DateTime) : parsed;

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
            {
                parsed = parsed.ToLocalTime();
                return parsed.Year < 1900 ? default(DateTime) : parsed;
            }

            return default(DateTime);
        }

        public static List<IntervalRange> ParseIntervalsFromText(string text, out List<string> errors)
        {
            var result = new List<IntervalRange>();
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return result;

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int lineNumber = 0;

            foreach (var rawLine in lines)
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("#"))
                    continue;

                int hashIndex = line.IndexOf('#');
                if (hashIndex >= 0)
                {
                    line = line.Substring(0, hashIndex).Trim();
                    if (line.Length == 0)
                        continue;
                }

                string[] parts = null;

                var separators = new[] { " - ", " – ", " — ", "->", "→", " a " };
                foreach (var sep in separators)
                {
                    int idx = line.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        var left = line.Substring(0, idx).Trim();
                        var right = line.Substring(idx + sep.Length).Trim();
                        if (left.Length > 0 && right.Length > 0)
                        {
                            parts = new[] { left, right };
                            break;
                        }
                    }
                }

                if (parts == null)
                {
                    parts = line
                        .Split(new[] { ';', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToArray();
                }

                if (parts.Length < 2)
                {
                    errors.Add($"Linea {lineNumber}: formato intervallo non riconosciuto");
                    continue;
                }

                string startText = TextHelper.NormalizeTimeSeparators(parts[0]);
                string endText = TextHelper.NormalizeTimeSeparators(parts[1]);

                var start = ParseDateText(startText);
                var end = ParseDateText(endText);

                // Handle case where end is just time
                if (start != default(DateTime) && end == default(DateTime))
                {
                    // Basic check for time-only like "12:00"
                    if (TimeSpan.TryParse(endText, out var endTime))
                    {
                        end = start.Date.Add(endTime);
                    }
                }

                if (start == default(DateTime) || end == default(DateTime))
                {
                    errors.Add($"Linea {lineNumber}: data/ora non valida");
                    continue;
                }

                if (start >= end)
                {
                    errors.Add($"Linea {lineNumber}: l'inizio deve essere precedente alla fine");
                    continue;
                }

                result.Add(new IntervalRange
                {
                    Start = start,
                    End = end
                });
            }

            return result;
        }
    }
}
