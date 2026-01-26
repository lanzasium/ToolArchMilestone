using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace ExportSample.Services
{
    /// <summary>
    /// Classe che gestisce procura resolver all'interno del tool.
    /// </summary>
    public sealed class ProcuraResolver
    {
        private readonly string _mappingFilePath;
        private IReadOnlyDictionary<int, MappingEntry> _mapping;

        /// <summary>
        /// Costruttore di ProcuraResolver, inizializza il contesto senza effetti collaterali.
        /// </summary>
        public ProcuraResolver(string mappingFilePath)
        {
            _mappingFilePath = mappingFilePath;
        }

        /// <summary>
        /// Risolve lo stato senza interventi manuali.
        /// </summary>
        public ProcuraResult Resolve()
        {
            EnsureMappingLoaded();
            if (_mapping.Count == 0)
                return null;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var ipProps = nic.GetIPProperties();
                if (ipProps == null)
                    continue;

                foreach (var address in ipProps.UnicastAddresses ?? Enumerable.Empty<UnicastIPAddressInformation>())
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (IPAddress.IsLoopback(address.Address))
                        continue;

                    var bytes = address.Address.GetAddressBytes();
                    if (bytes.Length != 4)
                        continue;

                    // Ignora indirizzi APIPA (169.254.x.x) e loopback già gestiti
                    if (bytes[0] == 169 && bytes[1] == 254)
                        continue;

                    int code;
                    if (bytes[0] == 10)
                    {
                        // Per reti 10.x.y.z usa il terzo ottetto come codice
                        code = bytes[2];
                    }
                    else
                    {
                        // Per le altre reti private (172.x, 192.x, 130.x, ecc.) usa il secondo ottetto
                        code = bytes[1];
                    }

                    if (code < 0)
                        continue;

                    if (_mapping.TryGetValue(code, out var entry) && entry != null)
                    {
                        return new ProcuraResult(entry.Name, code, address.Address.ToString(), nic.Name);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Si assicura che la parte mapping loaded sia pronta prima di procedere.
        /// </summary>
        private void EnsureMappingLoaded()
        {
            if (_mapping != null)
                return;

            try
            {
                if (!File.Exists(_mappingFilePath))
                {
                    _mapping = new Dictionary<int, MappingEntry>();
                    return;
                }

                var json = File.ReadAllText(_mappingFilePath);
                var entries = JsonConvert.DeserializeObject<List<ProcuraMapping>>(json) ?? new List<ProcuraMapping>();

                _mapping = entries
                    .Where(e => e != null && e.Code >= 0 && !string.IsNullOrWhiteSpace(e.Name))
                    .GroupBy(e => e.Code)
                    .ToDictionary(
                        g => g.Key,
                        g => new MappingEntry(g.First().Name));
            }
            catch
            {
                _mapping = new Dictionary<int, MappingEntry>();
            }
        }

        /// <summary>
        /// Classe che gestisce mapping entry all'interno del tool.
        /// </summary>
        private sealed class MappingEntry
        {
            /// <summary>
            /// Costruttore di MappingEntry, inizializza il contesto senza effetti collaterali.
            /// </summary>
            public MappingEntry(string name)
            {
                Name = name?.Trim();
            }

            /// <summary>
            /// Nome leggibile associato a name.
            /// </summary>
            public string Name { get; }
        }

        /// <summary>
        /// Classe che gestisce procura mapping all'interno del tool.
        /// </summary>
        private sealed class ProcuraMapping
        {
            /// <summary>
            /// Valore code esposto pubblicamente.
            /// </summary>
            [JsonProperty("code")]
            public int Code { get; set; }

            /// <summary>
            /// Nome leggibile associato a name.
            /// </summary>
            [JsonProperty("name")]
            public string Name { get; set; }
        }

        /// <summary>
        /// Classe che gestisce procura result all'interno del tool.
        /// </summary>
        public sealed class ProcuraResult
        {
            /// <summary>
            /// Costruttore di ProcuraResult, inizializza il contesto senza effetti collaterali.
            /// </summary>
            public ProcuraResult(string name, int code, string sourceAddress, string interfaceName)
            {
                Name = name;
                Code = code;
                SourceAddress = sourceAddress;
                InterfaceName = interfaceName;
            }

            /// <summary>
            /// Nome leggibile associato a name.
            /// </summary>
            public string Name { get; }
            /// <summary>
            /// Valore code esposto pubblicamente.
            /// </summary>
            public int Code { get; }
            /// <summary>
            /// Valore source address esposto pubblicamente.
            /// </summary>
            public string SourceAddress { get; }
            /// <summary>
            /// Nome leggibile associato a interface name.
            /// </summary>
            public string InterfaceName { get; }
        }
    }
}

