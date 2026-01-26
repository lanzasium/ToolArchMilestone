using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ToolArchMilestone.Core.Helpers
{
    public static class PathHelper
    {
        private const int WindowsMaxPathLength = 260;

        public static string EnsureUniquePath(string basePath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return basePath;

            string directory = Path.GetDirectoryName(basePath) ?? string.Empty;
            string name = Path.GetFileName(basePath) ?? string.Empty;
            string nameWithoutExt = isDirectory ? name : (Path.GetFileNameWithoutExtension(basePath) ?? string.Empty);
            string extension = isDirectory ? string.Empty : (Path.GetExtension(basePath) ?? string.Empty);

            string candidate = basePath;
            int counter = 1;
            while ((isDirectory && Directory.Exists(candidate)) || (!isDirectory && File.Exists(candidate)))
            {
                string suffix = $"_{counter++}";
                string finalName = isDirectory ? nameWithoutExt + suffix : nameWithoutExt + suffix + extension;
                candidate = Path.Combine(directory, finalName);
            }

            return candidate;
        }

        public static bool CheckPathLength(string path)
        {
            return path.Length < WindowsMaxPathLength;
        }

        public static string BuildExportDestinationPath(string basePath, string procedimento, string ritSpec, string target, string idLavoro, bool isMultiLaunch, int multiLaunchIndex)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentException("Percorso base non valido.", nameof(basePath));

            string procedimentoSegment = PrepareExportSegment(procedimento, replaceSlashWithDash: true, replaceSpacesWithUnderscore: false, fallback: "PROC");
            string ritSpecSegment = PrepareExportSegment(ritSpec, replaceSlashWithDash: true, replaceSpacesWithUnderscore: true, fallback: "N");
            string targetSegment = PrepareExportSegment(target, replaceSlashWithDash: false, replaceSpacesWithUnderscore: true, fallback: "TARGET");
            string idSegment = PrepareExportSegment(idLavoro, replaceSlashWithDash: false, replaceSpacesWithUnderscore: true, fallback: "000000");

            var segments = new List<string> { procedimentoSegment, ritSpecSegment, targetSegment };

            if (isMultiLaunch)
            {
                segments.Add($"TLC{(multiLaunchIndex + 1)}");
            }

            segments.Add(idSegment);

            string folderName = string.Join("_", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
            folderName = ReplaceInvalidFileNameChars(folderName);
            folderName = CollapseRepeatedCharacters(folderName, '_').Trim('_');

            if (string.IsNullOrWhiteSpace(folderName))
                folderName = "EXPORT";

            // Safety margin logic from legacy
            const int safetyMargin = 100;
            int maxRootLength = WindowsMaxPathLength - safetyMargin;

            string combined = Path.Combine(basePath, folderName);
            if (combined.Length > maxRootLength && maxRootLength > (basePath.Length + 2))
            {
                int allowedFolderNameLength = maxRootLength - (basePath.Length + 1);
                if (allowedFolderNameLength < folderName.Length && allowedFolderNameLength > 0)
                {
                    folderName = folderName.Substring(0, allowedFolderNameLength).TrimEnd('_');
                    if (string.IsNullOrWhiteSpace(folderName))
                        folderName = "EXPORT";
                }
            }

            return Path.Combine(basePath, folderName);
        }

        private static string PrepareExportSegment(string value, bool replaceSlashWithDash, bool replaceSpacesWithUnderscore, string fallback)
        {
            string segment = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

            if (replaceSlashWithDash)
            {
                segment = segment.Replace('/', '-').Replace('\\', '-');
            }

            if (replaceSpacesWithUnderscore)
            {
                var parts = segment.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                segment = parts.Length > 0 ? string.Join("_", parts) : fallback;
                segment = CollapseRepeatedCharacters(segment, '_');
            }
            else
            {
                var parts = segment.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                segment = parts.Length > 0 ? string.Join(" ", parts) : fallback;
            }

            segment = ReplaceInvalidFileNameChars(segment);
            segment = replaceSpacesWithUnderscore ? segment.Trim('_') : segment.Trim();

            return string.IsNullOrWhiteSpace(segment) ? fallback : segment;
        }

        private static string ReplaceInvalidFileNameChars(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                builder.Append(invalidChars.Contains(c) ? '_' : c);
            }

            return builder.ToString();
        }

        private static string CollapseRepeatedCharacters(string value, char character)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var builder = new StringBuilder(value.Length);
            char? lastChar = null;

            foreach (char c in value)
            {
                if (c == character && lastChar == character)
                    continue;

                builder.Append(c);
                lastChar = c;
            }

            return builder.ToString();
        }
    }
}
