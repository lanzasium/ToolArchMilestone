using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;

namespace ToolArchMilestone.Core.Helpers
{
    public static class SitDocumentHelper
    {
        public static void CreateSitDocument(ArchivingJob job, string templatePath)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException("Template file not found", templatePath);

            var destinationDirectory = job.ExportPath;
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new InvalidOperationException("Export path is empty");

            if (!Directory.Exists(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            var baseFileName = $"SIT_ARCH_DT_VIDEO_{TextHelper.SanitizeFileName(job.WorkId ?? job.PublicJobId ?? "EXPORT")}";
            var destinationPath = Path.Combine(destinationDirectory, baseFileName + ".docx");
            int attempt = 1;
            while (File.Exists(destinationPath))
            {
                destinationPath = Path.Combine(destinationDirectory, $"{baseFileName}_{attempt:00}.docx");
                attempt++;
            }

            File.Copy(templatePath, destinationPath, true);

            ApplyReplacements(destinationPath, job);
        }

        private static void ApplyReplacements(string filePath, ArchivingJob job)
        {
            var replacements = BuildReplacementMap(job);

            using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Update))
            {
                var xmlEntries = archive.Entries
                    .Where(entry => entry.FullName.StartsWith("word/") && entry.FullName.EndsWith(".xml"))
                    .ToList();

                foreach (var entry in xmlEntries)
                {
                    string content;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        content = reader.ReadToEnd();
                    }

                    bool modified = false;
                    foreach (var kvp in replacements)
                    {
                        string placeholder = $"[{kvp.Key}]";
                        if (content.Contains(placeholder))
                        {
                            string rawValue = kvp.Value ?? string.Empty;
                            string sanitized = System.Security.SecurityElement.Escape(rawValue);
                            content = content.Replace(placeholder, sanitized);
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                        {
                            writer.Write(content);
                            writer.BaseStream.SetLength(writer.BaseStream.Position);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, string> BuildReplacementMap(ArchivingJob job)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string Safe(string? val) => val ?? "N/D";

            map["ID lavoro"] = Safe(job.WorkId);
            map["Magistrato"] = Safe(job.Magistrate);
            map["Password"] = Safe(job.Password);
            map["Periodo"] = $"{job.StartTime:dd/MM/yyyy HH:mm} - {job.EndTime:dd/MM/yyyy HH:mm}";
            map["Procedimento Penale"] = Safe(job.CriminalProceeding);
            map["Procura"] = Safe(job.Procura);
            map["RIT/SPEC"] = Safe(job.RitSpec);
            map["Target"] = Safe(job.Target);
            
            // Extra
            map["ID"] = Safe(job.PublicJobId);
            map["Telecamera"] = Safe(job.CameraName);
            map["Durata"] = Safe(job.Duration);
            map["Stato"] = Safe(job.Status.ToString());
            map["Cartella"] = Safe(job.ExportPath);
            map["Server"] = Safe(job.ServerAddress);
            map["Note"] = Safe(job.Note);
            map["DataDocumento"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

            return map;
        }
    }
}
