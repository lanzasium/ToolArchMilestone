using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ToolArchMilestone.Core.Services
{
    public interface IServerMappingService
    {
        string GetServerName(string ip);
    }

    public class ServerMappingService : IServerMappingService
    {
        private readonly Dictionary<string, string> _serverMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ServerMappingService()
        {
            LoadMappings();
        }

        private void LoadMappings()
        {
            try
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                var xmlPath = Path.Combine(basePath, "Assets", "Servers.xml");

                if (!File.Exists(xmlPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Servers.xml not found at {xmlPath}");
                    return;
                }

                var doc = XDocument.Load(xmlPath);
                foreach (var server in doc.Descendants("Server"))
                {
                    var ip = server.Attribute("Ip")?.Value;
                    var name = server.Attribute("Name")?.Value;

                    if (!string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(name))
                    {
                        _serverMap[ip.Trim()] = name.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading server mappings: {ex.Message}");
            }
        }

        public string GetServerName(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return string.Empty;

            // Normalize IP (remove protocol if present)
            var cleanIp = ip.Replace("http://", "").Replace("https://", "").Trim();
            if (cleanIp.Contains("/")) cleanIp = cleanIp.Split('/')[0];
            if (cleanIp.Contains(":")) cleanIp = cleanIp.Split(':')[0];

            if (_serverMap.TryGetValue(cleanIp, out var name))
            {
                return name;
            }

            return ip; // Return IP if no name found
        }
    }
}
