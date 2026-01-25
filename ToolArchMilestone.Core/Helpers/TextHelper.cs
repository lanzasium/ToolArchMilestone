using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ToolArchMilestone.Core.Helpers
{
    public static class TextHelper
    {
        private static readonly char[] TimeSeparatorCandidates = { '.', ',', ';' };

        public static string NormalizeServerAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return string.Empty;

            address = address.Trim();
            if (address.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                address = "http://" + address;
            }
            return address;
        }

        public static string NormalizeTimeSeparators(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            foreach (var ch in TimeSeparatorCandidates)
                value = value.Replace(ch, ':');

            return value;
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "NA";

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(invalidChars.Contains(c) ? '_' : c);
            }
            return builder.ToString().Replace(' ', '_');
        }

        public static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public static string NormalizeStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "IN CORSO";

            // Simple title case manually if CultureInfo not handy or just ToUpper/Lower
            // Legacy code used CultureInfo.CurrentCulture.TextInfo.ToTitleCase
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(status.ToLowerInvariant());
        }
    }
}
