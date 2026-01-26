using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security.Cryptography; // ✅ FIX #23: Per RNGCryptoServiceProvider
using Newtonsoft.Json; // Per JSON persistence
using VideoOS.Platform;
using VideoOS.Platform.SDK.Export;
using VideoOS.Platform.Util;
using AVIExporter = VideoOS.Platform.Data.AVIExporter;
using ExportSample.Helpers; // Per AnimationManager e UIEffects
using System.Globalization;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Xml.Linq;
using ToolArchiviazioniMilestone.Data;
using ExportSample.Services;
using Environment = System.Environment;

// P/Invoke per abilitare drag&drop quando l'app gira come amministratore
namespace ExportSample.NativeMethods
{
    internal static class DragDropHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

        [DllImport("ole32.dll")]
        public static extern int OleInitialize(IntPtr pvReserved);

        [DllImport("shell32.dll")]
        public static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

        [DllImport("shell32.dll")]
        public static extern void DragFinish(IntPtr hDrop);

        [DllImport("shell32.dll")]
        public static extern bool DragQueryPoint(IntPtr hDrop, out System.Drawing.Point lppt);

        public const uint WM_DROPFILES = 0x0233;
        public const uint WM_COPYDATA = 0x004A;
        public const uint WM_COPYGLOBALDATA = 0x0049;
        public const uint MSGFLT_ALLOW = 1;

        public static void AllowDragDropForWindow(IntPtr hWnd)
        {
            // Prova OleInitialize per assicurarsi che OLE sia inizializzato
            try { OleInitialize(IntPtr.Zero); } catch { }

            // Metodo 1: ChangeWindowMessageFilterEx per finestra specifica (Windows 7+)
            ChangeWindowMessageFilterEx(hWnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hWnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hWnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);

            // Metodo 2: ChangeWindowMessageFilter globale (fallback)
            ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ALLOW);
            ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ALLOW);
            ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ALLOW);

            // Metodo 3: DragAcceptFiles per shell drag&drop
            DragAcceptFiles(hWnd, true);
        }
    }
}

namespace ExportSample
{
    // Classe per le informazioni delle archiviazioni - ESTESA per persistenza
    public class ArchiviazioneInfo
    {
        public string Id { get; set; }
        public string Telecamera { get; set; }
        public DateTime Inizio { get; set; }
        public DateTime Fine { get; set; }
            public DateTime PeriodoInizio { get; set; }
            public DateTime PeriodoFine { get; set; }
        public int Progresso { get; set; }
        public string Stato { get; set; }
        public string Durata { get; set; }
        public string Dimensione { get; set; }
        public DateTime DataCompletamento { get; set; }
            public DateTime DataAvvio { get; set; }
        public string Cartella { get; set; }
        public string Password { get; set; }
        public string Note { get; set; }
        
        // Nuovi campi per dettagli estesi
        public string ServerAddress { get; set; }
        public string ProcedimentoPenale { get; set; }
        public string RitSpec { get; set; }
        public string Magistrato { get; set; }
        public string Target { get; set; }
        public string IdLavoro { get; set; }
        public List<string> ErrorLog { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
        public string Procura { get; set; }
        
        // ✅ FIX #5: Clone per evitare race conditions
        /// <summary>
        /// Esegue la logica clone senza cambiare il comportamento.
        /// </summary>
        public ArchiviazioneInfo Clone()
        {
            return (ArchiviazioneInfo)this.MemberwiseClone();
        }

    }

    // Classe helper per i pulsanti
    public class ButtonInfo
    {
        public string Text { get; set; }
        public Color BackColor { get; set; }
        public EventHandler ClickHandler { get; set; }
        public bool RequiresSelection { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Costruttore di ButtonInfo, inizializza il contesto senza effetti collaterali.
        /// </summary>
        public ButtonInfo(string text, Color backColor, EventHandler clickHandler, bool requiresSelection = false)
        {
            Text = text;
            BackColor = backColor;
            ClickHandler = clickHandler;
            RequiresSelection = requiresSelection;
            Name = text.Replace(" ", "").ToUpper(); // Nome di default
        }
    }

    internal class ExportLogMetadata
    {
        public string tool { get; set; }
        public string version { get; set; }
        public string jobId { get; set; }
        public string timestamp { get; set; }
        public string serverAddress { get; set; }
        public string camera { get; set; }
        public string start { get; set; }
        public string end { get; set; }
        public string procedimento { get; set; }
        public string ritSpec { get; set; }
        public string target { get; set; }
        public string idLavoro { get; set; }
        public string magistrato { get; set; }
        public string cartella { get; set; }
        public string password { get; set; }
    }

    public partial class MainForm : Form
    {
        // ==================== SISTEMA DI DESIGN UNIFICATO ====================
        #region Design System Constants
        
        // Colori tema scuro professionale
        private static class Colors
        {
            // Base
            public static readonly Color Background = Color.FromArgb(24, 24, 24);
            public static readonly Color Surface = Color.FromArgb(32, 32, 32);
            public static readonly Color SurfaceLight = Color.FromArgb(45, 45, 45);
            public static readonly Color Border = Color.FromArgb(60, 60, 60);
            
            // Testi
            public static readonly Color TextPrimary = Color.FromArgb(255, 255, 255);
            public static readonly Color TextSecondary = Color.FromArgb(189, 189, 189);
            public static readonly Color TextMuted = Color.FromArgb(128, 128, 128);
            public static readonly Color TextPlaceholder = Color.FromArgb(97, 97, 97);
            
            // Accenti brand
            public static readonly Color Primary = Color.FromArgb(33, 150, 243);      // Blu Material
            public static readonly Color PrimaryLight = Color.FromArgb(100, 181, 246);
            public static readonly Color PrimaryDark = Color.FromArgb(21, 101, 192);
            
            public static readonly Color Secondary = Color.FromArgb(255, 152, 0);     // Arancione
            public static readonly Color SecondaryLight = Color.FromArgb(255, 183, 77);
            public static readonly Color SecondaryDark = Color.FromArgb(245, 124, 0);
            
            public static readonly Color Success = Color.FromArgb(76, 175, 80);       // Verde
            public static readonly Color SuccessLight = Color.FromArgb(129, 199, 132);
            public static readonly Color SuccessDark = Color.FromArgb(56, 142, 60);
            
            public static readonly Color Error = Color.FromArgb(244, 67, 54);        // Rosso
            public static readonly Color ErrorLight = Color.FromArgb(239, 83, 80);
            public static readonly Color ErrorDark = Color.FromArgb(198, 40, 40);
            
            public static readonly Color Warning = Color.FromArgb(255, 193, 7);      // Giallo
            public static readonly Color Info = Color.FromArgb(0, 188, 212);         // Ciano
            
            // Input
            public static readonly Color InputBackground = Color.FromArgb(28, 28, 28);
            public static readonly Color InputBorder = Color.FromArgb(60, 60, 60);
            public static readonly Color InputFocus = Color.FromArgb(33, 150, 243);
            public static readonly Color InputHover = Color.FromArgb(48, 48, 48);
        }
        
        // Spaziature e dimensioni
        private static class Spacing
        {
            public const int XXS = 2;  // Ridotto per meno spazio
            public const int XS = 4;   // Ridotto
            public const int SM = 8;   // Ridotto
            public const int MD = 12;  // Ridotto
            public const int LG = 16;  // Ridotto
            public const int XL = 24;  // Ridotto
            public const int XXL = 32; // Ridotto
            public const int SectionGap = LG;
            
            // Padding standard
            public static readonly Padding ContainerPadding = new Padding(SM, XS, SM, SM);
            public static readonly Padding SectionPadding = new Padding(SM, XS, SM, SM);
            public static readonly Padding ControlPadding = new Padding(XS, XXS, XS, XXS);
            public static readonly Padding ButtonPadding = new Padding(SM, XXS, SM, XXS);
            public static readonly Padding PagePadding = new Padding(XL - XS, XL - XS, XL - XS, XL - XS);
            public static readonly Padding AccentContentPadding = new Padding(SM, XL + XS, SM, SM - 1);
        }
        
        // Dimensioni controlli
        private static class Sizes
        {
            public const int ButtonHeight = 32;
            public const int ButtonMinWidth = 0;
            public const int InputHeight = 32;
            public const int LabelHeight = 24;
            public const int TabHeight = 40;
            public const int StatusBarHeight = 28;
            public const int BorderWidth = 4;
            public const int ListViewRowHeight = 36;
        }
        
        // Min width colonne liste IN CORSO / TERMINATE (molto più compatti per evitare scroll orizzontale)
        private static readonly int[] InCorsoColumnMinWidths = { 70, 90, 100, 110, 90, 80, 120, 80 };
        private static readonly int[] TerminateColumnMinWidths = { 70, 90, 100, 110, 90, 80, 120, 70 };

        private static readonly string BaseDirectory =
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static readonly string ResourcesDirectory = Path.Combine(BaseDirectory, "Resources");
        private static readonly string ClientPayloadDirectory = Path.Combine(ResourcesDirectory, "ClientPayload");
        private static readonly string ClientFilesDirectory = Path.Combine(ClientPayloadDirectory, "Client Files");
        private static readonly string PrimaryClientFolder = Path.Combine(ClientFilesDirectory, "Client");
        private static readonly string PrimaryPlayerExecutable = Path.Combine(ClientPayloadDirectory, "SmartClient-Player.exe");
        private static readonly string AssetsDirectory = Path.Combine(BaseDirectory, "Assets");
        private static readonly string SitTemplateFile = Path.Combine(AssetsDirectory, "Templates", "SIT", "SIT_Template.docx");

        private static readonly string[][] ClientFolderRelativeCandidates =
        {
            new[] { "Resources", "ClientPayload", "Client Files", "Client" },
            new[] { "Resources", "ClientPayload" },
            new[] { "ClientPayload", "Client Files", "Client" },
            new[] { "ClientPayload" },
            new[] { "Client" },
            new[] { "Client Files", "Client" },
            new[] { "Dependencies", "Client" },
            new[] { "Dependencies", "Client Files", "Client" },
            new[] { "Dependencies", "Player 2024 R2", "Client Files", "Client" }
        };
        private static readonly string[][] ClientPayloadRootRelativeCandidates =
        {
            new[] { "Resources", "ClientPayload" },
            new[] { "ClientPayload" },
            new[] { "Client Files" },
            new[] { "Dependencies", "ClientPayload" },
            new[] { "Dependencies", "Client Files" },
            new[] { "Dependencies", "Player 2024 R2", "ClientPayload" }
        };

        private static readonly string[][] PlayerExecutableRelativeCandidates =
        {
            new[] { "Resources", "ClientPayload", "SmartClient-Player.exe" },
            new[] { "ClientPayload", "SmartClient-Player.exe" },
            new[] { "SmartClient-Player.exe" },
            new[] { "Client", "Player", "SmartClient-Player.exe" },
            new[] { "Client Files", "Client", "SmartClient-Player.exe" },
            new[] { "Dependencies", "Client", "Player", "SmartClient-Player.exe" },
            new[] { "Dependencies", "Player 2024 R2", "SmartClient-Player.exe" },
            new[] { "Dependencies", "Player 2024 R2", "Client Files", "Client", "SmartClient-Player.exe" }
        };
        private const long DefaultDiskSizeBytes = 931L * 1024L * 1024L * 1024L; // ~1 TB utile

        /// <summary>
        /// Restituisce in corso min width gia pronto.
        /// </summary>
        private static int GetInCorsoMinWidth(int index)
        {
            return GetColumnMinWidth("InCorsoListView", index);
        }

        /// <summary>
        /// Restituisce column min width gia pronto.
        /// </summary>
        private static int GetColumnMinWidth(string listName, int index)
        {
            int[] widths = string.Equals(listName, "TerminateListView", StringComparison.OrdinalIgnoreCase)
                ? TerminateColumnMinWidths
                : InCorsoColumnMinWidths;

            if (index >= 0 && index < widths.Length)
                return widths[index];

            return 60;
        }

        /// <summary>
        /// Esegue la logica normalize server address senza cambiare il comportamento.
        /// </summary>
        private string NormalizeServerAddress(string address)
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

        private static readonly Regex HostNameRegex = new Regex(
            @"^[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static string NormalizeArchiveServerInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            string value = input.Trim();

            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("http://".Length);
            else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("https://".Length);

            return value;
        }

        private static string NormalizeServerHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return host;

            host = host.Trim();

            // Se è un IP, prova a risolvere il hostname
            if (IsValidIPv4Address(host))
            {
                try
                {
                    var hostEntry = Dns.GetHostEntry(host);
                    if (hostEntry != null && !string.IsNullOrWhiteSpace(hostEntry.HostName))
                    {
                        // Prendi solo il nome base senza dominio se presente
                        string hostName = hostEntry.HostName;
                        int dotIndex = hostName.IndexOf('.');
                        if (dotIndex > 0)
                            hostName = hostName.Substring(0, dotIndex);
                        return hostName;
                    }
                }
                catch (SocketException)
                {
                    // DNS non raggiungibile o host non trovato - mantieni l'IP
                    Debug.WriteLine($"[DNS] Impossibile risolvere IP {host} a hostname");
                }
                catch (ArgumentException)
                {
                    // Formato non valido - mantieni l'IP
                    Debug.WriteLine($"[DNS] Formato non valido per {host}");
                }
                catch (Exception ex)
                {
                    // Altri errori - mantieni l'IP
                    Debug.WriteLine($"[DNS] Errore risoluzione {host}: {ex.Message}");
                }
                return host;
            }

            // Se è un hostname, prova a risolvere l'IP per verifica
            if (IsValidHostName(host))
            {
                try
                {
                    var hostEntry = Dns.GetHostEntry(host);
                    if (hostEntry != null && hostEntry.AddressList != null && hostEntry.AddressList.Length > 0)
                    {
                        // Verifica che l'hostname risolva correttamente
                        // Mantieni l'hostname originale se la risoluzione funziona
                        return host;
                    }
                }
                catch (SocketException)
                {
                    // DNS non raggiungibile o host non trovato - mantieni l'hostname
                    Debug.WriteLine($"[DNS] Impossibile risolvere hostname {host} a IP");
                }
                catch (ArgumentException)
                {
                    // Formato non valido - mantieni l'hostname
                    Debug.WriteLine($"[DNS] Formato non valido per {host}");
                }
                catch (Exception ex)
                {
                    // Altri errori - mantieni l'hostname
                    Debug.WriteLine($"[DNS] Errore risoluzione {host}: {ex.Message}");
                }
                return host;
            }

            return host;
        }

        private static bool AreSameServer(string server1, string server2)
        {
            if (string.IsNullOrWhiteSpace(server1) || string.IsNullOrWhiteSpace(server2))
                return false;

            string host1 = ExtractHostFromUnc(server1);
            string host2 = ExtractHostFromUnc(server2);

            if (string.IsNullOrWhiteSpace(host1) || string.IsNullOrWhiteSpace(host2))
                return false;

            // Confronto diretto
            if (string.Equals(host1, host2, StringComparison.OrdinalIgnoreCase))
                return true;

            // Normalizza entrambi
            string normalized1 = NormalizeServerHost(host1);
            string normalized2 = NormalizeServerHost(host2);

            return string.Equals(normalized1, normalized2, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractHostFromUnc(string uncPath)
        {
            if (string.IsNullOrWhiteSpace(uncPath))
                return null;

            string normalized = uncPath.Replace('/', '\\').TrimStart('\\');
            var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0] : null;
        }

        private static bool IsValidIPv4Address(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var octets = value.Split('.');
            if (octets.Length != 4)
                return false;

            foreach (var octet in octets)
            {
                if (octet.Length == 0 || octet.Length > 3)
                    return false;
                if (!int.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out int valueOctet))
                    return false;
                if (valueOctet < 0 || valueOctet > 255)
                    return false;
            }

            return true;
        }

        private static bool IsValidHostName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return HostNameRegex.IsMatch(value);
        }

        private static bool PathBelongsToServer(string path, string serverRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(serverRoot))
                return false;

            string normalizedPath = path.Replace('/', '\\');
            string normalizedServer = serverRoot.Replace('/', '\\').TrimEnd('\\');

            return normalizedPath.StartsWith(normalizedServer, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUncPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractShareFromPath(string path)
        {
            if (!IsUncPath(path))
                return null;

            string normalized = path.Replace('/', '\\').TrimStart('\\');
            var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
                return null;

            return $@"\\{segments[0]}\{segments[1]}";
        }

        #region Network share helpers

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        private const int RESOURCETYPE_DISK = 0x00000001;
        private const int CONNECT_TEMPORARY = 0x00000004;
        private const int CONNECT_INTERACTIVE = 0x00000008;

        private const int NO_ERROR = 0;
        private const int ERROR_ALREADY_ASSIGNED = 85;
        private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
        private const int ERROR_DEVICE_ALREADY_REMEMBERED = 1202;

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        private static string FindAvailableDriveLetter()
        {
            var used = new HashSet<char>(
                DriveInfo.GetDrives()
                    .Select(d => char.ToUpperInvariant(d.Name.FirstOrDefault()))
                    .Where(c => c >= 'A' && c <= 'Z'));

            for (char letter = 'Z'; letter >= 'D'; letter--)
            {
                if (!used.Contains(letter))
                    return $"{letter}:";
            }

            return null;
        }

        private static bool DisconnectNetworkDrive(string localNameOrShare)
        {
            if (string.IsNullOrWhiteSpace(localNameOrShare))
                return true;

            try
            {
                int result = WNetCancelConnection2(localNameOrShare, 0, true);
                return result == NO_ERROR;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Network] Unable to disconnect {localNameOrShare}: {ex.Message}");
                return false;
            }
        }

        private List<IntervalRange> ParseIntervalsFromText(string text, out List<string> errors)
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

                string startText = NormalizeTimeSeparators(parts[0]);
                string endText = NormalizeTimeSeparators(parts[1]);

                var start = ParseDateText(startText);
                var end = ParseDateText(endText);

                if (start != default(DateTime) && end == default(DateTime))
                {
                    if (TryParseTimeOfDay(endText, out var endTime))
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

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e?.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }

            e.Effect = DragDropEffects.None;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e?.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            var txtFile = files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase))
                          ?? files[0];

            // Determina se il mouse è sopra una delle due dropzone (server / archivio)
            var screenPoint = new Point(e.X, e.Y);

            bool hitServer = _serverIntervalsDropZone != null && !_serverIntervalsDropZone.IsDisposed &&
                             _serverIntervalsDropZone.Visible &&
                             _serverIntervalsDropZone.RectangleToScreen(_serverIntervalsDropZone.ClientRectangle).Contains(screenPoint);

            bool hitArchive = _archiveIntervalsDropZone != null && !_archiveIntervalsDropZone.IsDisposed &&
                              _archiveIntervalsDropZone.Visible &&
                              _archiveIntervalsDropZone.RectangleToScreen(_archiveIntervalsDropZone.ClientRectangle).Contains(screenPoint);

            if (!hitServer && !hitArchive)
                return;

            if (hitServer)
            {
                TryLoadIntervalsFromFile(txtFile, isServer: true);
            }

            if (hitArchive)
            {
                TryLoadIntervalsFromFile(txtFile, isServer: false);
            }
        }

        private bool TryLoadIntervalsFromFile(string filePath, bool isServer)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                ShowWarning("Intervalli TXT", $"Il file specificato non esiste:\n{filePath}");
                return false;
            }

            string baseName = Path.GetFileName(filePath);

            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                ShowError("Intervalli TXT", $"Impossibile leggere il file:\n{ex.Message}");
                return false;
            }

            List<string> errors;
            var intervals = ParseIntervalsFromText(content, out errors);

            if (intervals.Count == 0)
            {
                var builderEmpty = new StringBuilder();
                builderEmpty.AppendLine("Nessun intervallo valido è stato trovato nel file TXT.");
                if (errors.Count > 0)
                {
                    builderEmpty.AppendLine();
                    builderEmpty.AppendLine("Dettagli:");
                    foreach (var err in errors.Take(10))
                    {
                        builderEmpty.AppendLine("- " + err);
                    }
                    if (errors.Count > 10)
                    {
                        builderEmpty.AppendLine($"(+ altre {errors.Count - 10} righe non valide)");
                    }
                }

                ShowWarning("Intervalli TXT", builderEmpty.ToString().Trim());
                return false;
            }

            var builder = new StringBuilder();
            // Riga di riepilogo immediatamente sopra alla lista, senza riga vuota extra
            builder.AppendLine($"Sono stati interpretati {intervals.Count} intervalli:");

            // Mostra tutti gli intervalli nel riepilogo di conferma (scorribili nel popup)
            foreach (var interval in intervals)
            {
                builder.AppendLine("- " + interval);
            }

            if (errors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Righe ignorate:");
                foreach (var err in errors)
                {
                    builder.AppendLine("- " + err);
                }
            }

            var result = ShowConfirmation(
                "Conferma intervalli TXT",
                builder.ToString().Trim(),
                DarkDialogButtons.YesNo,
                DarkDialogIcon.Info,
                primaryButtonText: "Usa intervalli",
                secondaryButtonText: "Annulla");

            if (result != DialogResult.Yes)
                return false;

            if (isServer)
            {
                _serverIntervals.Clear();
                _serverIntervals.AddRange(intervals);
                _serverIntervalsFileName = baseName;
                UpdateServerIntervalsSummaryLabel();
            }
            else
            {
                _archiveIntervals.Clear();
                _archiveIntervals.AddRange(intervals);
                _archiveIntervalsFileName = baseName;
                UpdateArchiveIntervalsSummaryLabel();
                // Aggiorna automaticamente l'intervallo data/ora dell'archivio in base agli intervalli TXT
                if (_archiveIntervals.Count > 0)
                {
                    var minStart = _archiveIntervals.Min(r => r.Start);
                    var maxEnd = _archiveIntervals.Max(r => r.End);

                    if (_archiveStartTextBox != null)
                        _archiveStartTextBox.Text = minStart.ToString("dd/MM/yyyy HH:mm");
                    if (_archiveEndTextBox != null)
                        _archiveEndTextBox.Text = maxEnd.ToString("dd/MM/yyyy HH:mm");
                }

                // Aggiorna la selezione e la dimensione stimata quando si caricano intervalli da TXT
                RecalculateArchiveSelection();
            }

            return true;
        }

        private void ClearIntervals(bool isServer)
        {
            if (isServer)
            {
                _serverIntervals.Clear();
                _serverIntervalsFileName = null;
                UpdateServerIntervalsSummaryLabel();
            }
            else
            {
                _archiveIntervals.Clear();
                _archiveIntervalsFileName = null;
                UpdateArchiveIntervalsSummaryLabel();
                // Quando si cancellano gli intervalli TXT, ricalcola la selezione basata solo su date/ora
                RecalculateArchiveSelection();
            }
        }

        private void ShowIntervalsPreview(bool isServer)
        {
            var list = isServer ? _serverIntervals : _archiveIntervals;
            string fileName = isServer ? _serverIntervalsFileName : _archiveIntervalsFileName;

            if (list == null || list.Count == 0)
            {
                ShowInfo("Intervalli TXT", "Nessun intervallo è stato caricato.");
                return;
            }

            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(fileName))
            {
                builder.AppendLine($"File: {fileName}");
            }
            builder.AppendLine($"Intervalli caricati: {list.Count}");
            builder.AppendLine();

            foreach (var interval in list.Take(200))
            {
                builder.AppendLine("- " + interval);
            }

            if (list.Count > 200)
            {
                builder.AppendLine($"(+ altri {list.Count - 200} intervalli)");
            }

            ShowInfo("Intervalli TXT", builder.ToString().Trim());
        }

        private bool ConnectToNetworkShare(string sharePath, bool mapDriveLetter, out string mappedDrive)
        {
            mappedDrive = null;
            if (string.IsNullOrWhiteSpace(sharePath))
                return false;

            var resource = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpRemoteName = sharePath
            };

            if (mapDriveLetter)
            {
                string drive = FindAvailableDriveLetter();
                if (!string.IsNullOrEmpty(drive))
                {
                    resource.lpLocalName = drive;
                    int code = WNetAddConnection2(ref resource, null, null, CONNECT_TEMPORARY | CONNECT_INTERACTIVE);
                    if (code == NO_ERROR || code == ERROR_ALREADY_ASSIGNED || code == ERROR_DEVICE_ALREADY_REMEMBERED)
                    {
                        mappedDrive = drive;
                        return true;
                    }
                    else if (code == ERROR_SESSION_CREDENTIAL_CONFLICT)
                    {
                        // Existing session with different creds: disconnect and retry once
                        DisconnectNetworkDrive(drive);
                    }
                }
            }

            resource.lpLocalName = null;
            int fallbackCode = WNetAddConnection2(ref resource, null, null, CONNECT_TEMPORARY | CONNECT_INTERACTIVE);
            if (fallbackCode == NO_ERROR || fallbackCode == ERROR_ALREADY_ASSIGNED || fallbackCode == ERROR_DEVICE_ALREADY_REMEMBERED)
                return true;

            Debug.WriteLine($"[Network] WNetAddConnection2 failed ({fallbackCode}) for {sharePath}");
            return false;
        }

        private List<string> EnumerateServerShares(string host)
        {
            var shares = new List<string>();

            if (string.IsNullOrWhiteSpace(host))
                return shares;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c net view \\\\{host}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                    StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return shares;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    shares.AddRange(ParseNetViewShares(output));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Network] net view fallita per {host}: {ex.Message}");
            }

            return shares;
        }

        private static IEnumerable<string> ParseNetViewShares(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                yield break;

            bool dataSection = false;
            foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("---", StringComparison.Ordinal))
                {
                    dataSection = true;
                    continue;
                }

                if (!dataSection)
                    continue;

                if (line.StartsWith("The command", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Il comando", StringComparison.OrdinalIgnoreCase))
                    yield break;

                if (line.StartsWith("Share name", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0]))
                    continue;

                var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                var name = tokens[0];
                if (!string.IsNullOrWhiteSpace(name))
                    yield return name;
            }
        }

        #endregion

        private void ArchiveRootTextBox_TextChanged(object sender, EventArgs e)
        {
            if (_archiveRootTextBox == null)
                return;

            _archiveRootTextBox.ForeColor = Colors.TextPrimary;
        }

        private void ArchiveRootTextBox_Leave(object sender, EventArgs e)
        {
            NormalizeArchivePathInput();
            TryLoadArchivePackagesFromTextBox();
        }

        /// <summary>
        /// Normalizza l'input del percorso archivi: se l'utente scrive solo "vs01" o "vs01\D"
        /// lo trasforma in "\\vs01\D".
        /// </summary>
        private void NormalizeArchivePathInput()
        {
            if (_archiveRootTextBox == null)
                return;

            string raw = GetRealValue(_archiveRootTextBox);
            if (string.IsNullOrWhiteSpace(raw))
                return;

            string trimmed = raw.Trim();

            // Se è già un UNC valido, non fare nulla
            if (IsUncPath(trimmed))
                return;

            // Se sembra un drive locale (C:\...), non fare nulla
            if (trimmed.Length >= 2 && trimmed[1] == ':')
                return;

            // Altrimenti, assumiamo sia hostname\share o hostname/share
            // Normalizza e aggiungi \\
            string normalized = trimmed.Replace('/', '\\');
            if (!normalized.StartsWith(@"\\"))
                normalized = @"\\" + normalized;

            _archiveRootTextBox.Text = normalized;
            _archiveRootTextBox.ForeColor = Colors.TextPrimary;
        }

        /// <summary>
        /// Estrae l'hostname dal percorso UNC (es. \\vs01\D -> vs01).
        /// </summary>
        private static string ExtractHostnameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Trim().Replace('/', '\\');
            if (normalized.StartsWith(@"\\"))
                normalized = normalized.Substring(2);

            var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0] : null;
        }

        /// <summary>
        /// Estrae la share dal percorso UNC (es. \\vs01\D -> D).
        /// </summary>
        private static string ExtractShareNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Trim().Replace('/', '\\');
            if (normalized.StartsWith(@"\\"))
                normalized = normalized.Substring(2);

            var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 1 ? segments[1] : null;
        }

        private bool EnsureArchivePathAvailable(string path, bool allowCreateDirectories)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Trim();
            if (Directory.Exists(normalized))
                return true;

            if (IsUncPath(normalized))
            {
                string share = ExtractShareFromPath(normalized);
                if (!string.IsNullOrWhiteSpace(share))
                {
                    if (!Directory.Exists(share))
                    {
                        if (!ConnectToNetworkShare(share, mapDriveLetter: true, out string mappedDrive))
                        {
                            ShowWarning("Connessione percorso", $"Impossibile connettersi alla condivisione {share}. Verifica che sia condivisa e accessibile.");
                            return false;
                        }

                        if (!Directory.Exists(share))
                            return false;
                        
                        if (_archiveServerState != null)
                        {
                            _archiveServerState.ConnectedShare = share;
                            _archiveServerState.MappedDriveLetter = mappedDrive;
                        }
                    }

                    if (Directory.Exists(normalized))
                        return true;
                }
            }

            if (!allowCreateDirectories)
                return false;

            var confirmation = PromptCreateDirectory(normalized);
            if (confirmation != DialogResult.Yes)
                return false;

            try
            {
                Directory.CreateDirectory(normalized);
                return true;
            }
            catch (Exception ex)
            {
                ShowError("Creazione cartella", $"Non è stato possibile creare \"{normalized}\".\nDettagli: {ex.Message}");
                return false;
            }
        }

        private DialogResult PromptCreateDirectory(string path)
        {
            return ShowConfirmation(
                "Percorso archivi",
                $"Il percorso \"{path}\" non esiste. Vuoi crearlo automaticamente?",
                DarkDialogButtons.YesNo,
                DarkDialogIcon.Info,
                primaryButtonText: "Crea ora",
                secondaryButtonText: "Annulla");
        }

        private void EnsureShareConnectivityForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsUncPath(path))
                return;

            string share = ExtractShareFromPath(path);
            if (!string.IsNullOrWhiteSpace(share))
                EnsureArchivePathAvailable(share, allowCreateDirectories: false);
        }

        private static bool TrySetFolderBrowserPath(FolderBrowserDialog dialog, string targetPath)
        {
            if (dialog == null || string.IsNullOrWhiteSpace(targetPath))
                return false;

            try
            {
                dialog.SelectedPath = targetPath;
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        // Font system
        private static class Fonts
        {
            public const string Primary = "Segoe UI";
            public const string Mono = "Cascadia Code";
            public const string MonoFallback = "Consolas";
            
            public const float SizeXS = 8f;
            public const float SizeSM = 9f;
            public const float SizeMD = 10f;
            public const float SizeLG = 12f;
            public const float SizeXL = 14f;
            public const float SizeXXL = 18f;
        }
        
        // Icone Unicode per UI moderna
        private static class Icons
        {
            public const string Server = "🖧";
            public const string Camera = "📹";
            public const string Calendar = "🗓️";
            public const string Layout = "🧾";
            public const string Switch = "🔀";
            public const string ArrowUp = "⬆️";
            public const string ArrowDown = "⬇️";
            public const string Lock = "🔒";
            public const string Folder = "📁";
            public const string Search = "🔍";
            public const string Settings = "⚙️";
            public const string Rocket = "🚀";
            public const string Clipboard = "📋";
            public const string Detail = "📝";
            public const string Log = "📜";
            public const string Check = "✅";
            public const string Cross = "❌";
            public const string Eye = "👁️";
            public const string EyeSlash = "🙈";
            public const string Connected = "🟢";
            public const string Disconnected = "🔴";
            public const string Connecting = "🟡";
            public const string Info = "ℹ️";
            public const string Warning = "⚠️";
            public const string Generate = "✨";
            public const string Browse = "📂";
            public const string Pin = "📌";
            public const string Trash = "🗑️";
            public const string Import = "📥";
        }

        private enum DarkDialogIcon
        {
            None,
            Info,
            Success,
            Warning,
            Error
        }

        private enum DarkDialogButtons
        {
            Ok,
            OkCancel,
            YesNo
        }
        
        #endregion
        
        // ==================== CACHE E OTTIMIZZAZIONE ====================
        #region Resource Caching
        
        // ✅ FIX #4: Cache statica per Brush/Pen (evita GDI+ leak)
        private static readonly Dictionary<Color, SolidBrush> _brushCache = new Dictionary<Color, SolidBrush>();
        private static readonly Dictionary<(Color, float), Pen> _penCache = new Dictionary<(Color, float), Pen>();

        // ✅ FIX #30: Cache font (evita memory leak GetSafeFont)
        private static readonly Dictionary<(string, float, FontStyle), Font> _fontCache = new Dictionary<(string, float, FontStyle), Font>();
        private static readonly PrivateFontCollection _embeddedFontCollection = new PrivateFontCollection();
        private static readonly object _fontCollectionLock = new object();
        private static bool _embeddedCascadiaLoaded;
        #endregion
        
        // ==================== API WINDOWS PER TEMA SCURO ====================
        /// <summary>
        /// Esegue la logica dwm set window attribute senza cambiare il comportamento.
        /// </summary>
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Imposta window theme usando i parametri passati.
        /// </summary>
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        /// <summary>
        /// Restituisce window dc gia pronto.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        /// <summary>
        /// Esegue la logica release dc senza cambiare il comportamento.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// Esegue la logica find window ex senza cambiare il comportamento.
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        /// <summary>
        /// Esegue la logica invalidate rect senza cambiare il comportamento.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        /// <summary>
        /// Aggiorna window e mantiene lo stato coerente.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_MICA_EFFECT = 1029;
        private const int SignatureStatusBarClicksRequired = 5;
        private const int SignatureStatusBarClickTimeoutMs = 900;

        private Item _item = null;
        private string _path = null;
        private string _currentExportOutputPath = null;
        // ✅ FIX #18: Polling interval aumentato da 100ms a 500ms
        private Timer _timer = new Timer() {Interval = 500};
        VideoOS.Platform.Data.IExporter _exporter;
        private Item _selectedCamera;
        public List<Item> _allCams;
        private readonly List<Item> _selectedCameras = new List<Item>();
        private bool _isServerConnected;

        // Gestione multi-export
        private class ExportJob
        {
            public string Id { get; set; }
            public string CameraName { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public DateTime CompletedAtUtc { get; set; }
            public VideoOS.Platform.Data.IExporter Exporter { get; set; }
            public System.Windows.Forms.Timer Timer { get; set; }
            public int Progress { get; set; }
            public string Status { get; set; }
            public string Path { get; set; }
            public string Password { get; set; }
            public bool IsCanceled { get; set; } // ✅ FIX #6: Flag per race condition
            public EventHandler TimerHandler { get; set; }
            
            // Nuovi campi per dettagli estesi
            public string ProcedimentoPenale { get; set; }
            public string RitSpec { get; set; }
            public string Target { get; set; }
            public string IdLavoro { get; set; }
            public string Magistrato { get; set; }
            public string Procura { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
            public string LogFilePath { get; set; }
            public int LastLoggedProgress { get; set; } = -1;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public bool IsHistoricalSnapshot { get; set; }
            public DateTime QueuedAtUtc { get; set; }
            public DateTime StartedAtUtc { get; set; }
            public DateTime LastProgressUtc { get; set; }
            public DateTime LastActivityUtc { get; set; }
            public bool IsQueued { get; set; }
            public bool IsManualArchive { get; set; }
            public string ServerAddress { get; set; }
        }
        private sealed class ExportJobRequest
        {
            public PreparedLaunchJob PreparedJob { get; set; }
            public ExportCommonData CommonData { get; set; }
            public bool IsMultiLaunch { get; set; }
            public ExportJob Job { get; set; }
        }
        private sealed class ManualArchiveJobContext
        {
            public string SourceRoot { get; set; }
            public string DestinationPath { get; set; }
            public string PackagesRoot { get; set; }
            public IReadOnlyList<ArchivePackageInfo> Packages { get; set; }
            public bool SkipClientPayload { get; set; }
        }
        private List<ExportJob> _activeExports = new List<ExportJob>();
        private readonly List<ExportJob> _queuedExports = new List<ExportJob>();
        private readonly Queue<ExportJobRequest> _pendingJobRequests = new Queue<ExportJobRequest>();
        private static readonly TimeSpan JobProgressTimeout = TimeSpan.FromMinutes(5);
        private List<ExportJob> _inCorsoSnapshot = new List<ExportJob>();
        private string _inCorsoSearchTerm = string.Empty;
        private ExportJob _currentInCorsoDetailJob;
        private string _inCorsoLogSearchTerm = string.Empty;
        private string _terminateLogSearchTerm = string.Empty;
        private int _inCorsoLogHighlightStart = -1;
        private int _inCorsoLogHighlightLength = 0;
        private int _terminateLogHighlightStart = -1;
        private int _terminateLogHighlightLength = 0;
        private System.Windows.Forms.Timer _logRefreshTimer;
        private string _currentLogFile;
        private string _currentLogSnapshot;
        private object _exportLock = new object();
        private readonly List<ArchiviazioneInfo> _completedExports = new List<ArchiviazioneInfo>();
        private readonly object _completedExportsLock = new object();
        private List<ArchiviazioneInfo> _terminateSnapshot = new List<ArchiviazioneInfo>();
        private string _terminateSearchTerm = string.Empty;
        private bool _isAdjustingInCorsoColumns;
        private bool _isAdjustingTerminateColumns;
        private ArchiviazioneInfo _selectedTerminateInfo;
        private string _currentTerminateLogFile;
        private string _currentProcura = string.Empty;
        private ProcuraResolver _procuraResolver;
        private LogIndexService _logIndexService;
        private int _jobCounterValue;
        private int _jobCounterSuffixIndex = -1;
        private bool _jobCounterInitialized;
        private int _statusBarClickCount;
        private DateTime _statusBarLastClickUtc = DateTime.MinValue;
        private bool _signaturePopupVisible;

        // Controlli personalizzati per il tema scuro
        private Panel tabPanel;
        private Panel contentPanel;
        private Panel statusPanel;
        private Panel _tabUnderline;
        // ✅ FIX #28: Auto-save configuration
        private System.Windows.Forms.ErrorProvider _errorProvider;
        private System.Windows.Forms.ToolTip _toolTip;
        
        private DateTime _camerasCacheTime = DateTime.MinValue;
        private const int CACHE_DURATION_MINUTES = 5;
        
        /// <summary>
        /// Restituisce uniform section padding gia pronto.
        /// </summary>
        private static Padding GetUniformSectionPadding(bool compact = false)
        {
            if (compact)
            {
                int horizontal = Math.Max(0, Spacing.SM - 6);
                int verticalTop = Math.Max(0, Spacing.MD - 4);
                int verticalBottom = Math.Max(0, Spacing.SM - 6);
                return new Padding(horizontal, verticalTop, horizontal, verticalBottom);
            }

            int horizontalDefault = Math.Max(0, Spacing.SM - 4);
            int verticalTopDefault = Math.Max(0, Spacing.LG - 4);
            int verticalBottomDefault = Math.Max(0, Spacing.SM - 4);
            return new Padding(horizontalDefault, verticalTopDefault, horizontalDefault, verticalBottomDefault);
        }
        
        // ✅ FIX #11: Traccia tab corrente
        private int _currentTab = 0;
        private readonly List<Button> _lancioActionButtons = new List<Button>();
        private readonly List<Button> _sorgenteActionButtons = new List<Button>();

        private enum ArchiviazioneTipologia
        {
            Legale,
            Digitale
        }

        private enum LaunchMode
        {
            Server,
            Archivio
        }

        private ArchiviazioneTipologia _currentTipologia = ArchiviazioneTipologia.Legale;
        private LaunchMode _currentLaunchMode = LaunchMode.Server;
        private Panel _sorgenteHost;
        private TableLayoutPanel _leftStackLayout;
        private RadioButton _launchModeServerRadio;
        private RadioButton _launchModeArchiveRadio;
        private RadioButton _tipologiaLegaleRadio;
        private RadioButton _tipologiaDigitaleRadio;

        private TextBox _archiveRootTextBox;
        private TextBox _archiveStartTextBox;
        private TextBox _archiveEndTextBox;
        private TextBox _archiveBaseNamesTextBox;
        private Label _archiveSizeLabel;
        private RadioButton _archivePerGroupRadio;
        private RadioButton _archivePerIntervalRadio;
        private RadioButton _serverPerIntervalRadio;
        private Label _serverIntervalsDropLabel;
        private Label _archiveIntervalsDropLabel;
        private Panel _serverIntervalsDropZone;
        private Panel _archiveIntervalsDropZone;
        private readonly List<IntervalRange> _serverIntervals = new List<IntervalRange>();
        private readonly List<IntervalRange> _archiveIntervals = new List<IntervalRange>();
        private string _serverIntervalsFileName;
        private string _archiveIntervalsFileName;
        private ArchiveServerState _archiveServerState = new ArchiveServerState();
        private string _archiveRootPath;
        private readonly HashSet<string> _selectedArchiveBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ArchiveBaseNameOption> _availableArchiveBaseNames = new List<ArchiveBaseNameOption>();

        private readonly List<ArchivePackageInfo> _archivePackages = new List<ArchivePackageInfo>();
        private readonly Dictionary<string, List<ArchivePackageInfo>> _packagesByBaseName = new Dictionary<string, List<ArchivePackageInfo>>(StringComparer.OrdinalIgnoreCase);
        // Regex che individua il timestamp (YYYY-MM-DDTHH:mm:ss) con separatori flessibili (:/._ o caratteri unicode simili)
        private static readonly Regex ArchiveTimestampPattern = new Regex(
            @"\d{4}-\d{2}-\d{2}(?:T|\s)\d{2}(?:\D?\d{2}){2}(?:[+-]\d{2}\D?\d{2}|[+-]\d{4}|Z)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ArchiveTimestampCanonicalizerRegex = new Regex(
            @"^(?<date>\d{4}-\d{2}-\d{2})(?:T|\s)(?<hour>\d{2})\D?(?<minute>\d{2})\D?(?<second>\d{2})(?<zone>(?:[+-]\d{2}\D?\d{2}|[+-]\d{4}|Z)?)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly char[] ArchiveBaseTrimChars = { '-', '_', '.', ' ', '\t', '\u2010', '\u2011', '\u2012', '\u2013', '\u2014', '\u2015', '\u2212' };
        private static readonly string[] ArchiveTimestampFormats =
        {
            "yyyy-MM-ddTHHmmsszzz",   // Formato senza due punti: 2025-03-18T161254+0100
            "yyyy-MM-ddTHH:mm:sszzz", // Formato con due punti: 2025-03-18T16:12:54+01:00
            "yyyy-MM-ddTHHmmssK",     // Senza due punti, timezone Z o +00:00
            "yyyy-MM-ddTHH:mm:ssK",   // Con due punti, timezone Z o +00:00
            "yyyy-MM-ddTHHmmss",      // Senza due punti, senza timezone
            "yyyy-MM-ddTHH:mm:ss",    // Con due punti, senza timezone
            "yyyy-MM-ddTHH_mm_sszzz", // Con underscore e timezone
            "yyyy-MM-ddTHH_mm_ssK",
            "yyyy-MM-ddTHH_mm_ss",
            "yyyy-MM-dd HH:mm:ss"
        };
        private static readonly char[] ArchiveTimestampTrailingTrimChars = { ')', ']', '}', '>', '<', ',', ';', '.', '"', '\'', '`', '_', '-', '–', '—', '―' };
        private static readonly char[] ArchiveTimestampLeadingTrimChars = { '-', '_', '–', '—', '―', ':', ';', '=', '>', '(', '[', '{' };
        private static readonly string[] ArchiveTimestampSuffixes = { ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz", ".001", ".cab", ".bak" };
        private const int ArchiveTimestampTailScanLength = 160;
        private ArchiveSelectionInfo _currentArchiveSelection = ArchiveSelectionInfo.Empty;
        private int _lastArchiveScanTotalEntries;
        private int _lastArchiveScanRecognizedEntries;
        private readonly List<string> _lastArchiveScanSamples = new List<string>();
        private string _lastArchiveScanPath;
        private string _lastArchiveScanError;
        private DateTime? _lastArchiveScanUtc;
        private bool _archiveScanInProgress;
        private int _archiveSizeCalculationVersion;
        // Soglia sicura per la lunghezza massima del percorso su Windows (MAX_PATH ~260)
        // Include unità, due punti, backslash e terminatore di stringa.
        private const int WindowsMaxPathLength = 260;
        private const string ToolVersion = "1.0.0";
        private static readonly string[] ArchiveMetadataRequiredFiles = { "config.xml", "synckey" };

        // Sistema di persistenza
        private readonly string _dataFilePath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "ToolArchiviazioniMilestone",
            "export_history.json"
        );
        private readonly string _logDirectoryPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "ToolArchiviazioniMilestone",
            "logs"
        );
        private readonly string _legacyLogDirectoryPath = Path.Combine(Path.GetTempPath(), "ToolArchiviazioniMilestoneLogs");
        private readonly object _logDirectoryLock = new object();
        private bool _logDirectoryInitialized = false;
        // JSON serialization con Newtonsoft.Json
        private readonly string _preferencesFilePath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "ToolArchiviazioniMilestone",
            "user_preferences.json"
        );
        private Dictionary<string, string> _preferencesCache;
        private readonly ExportDataService _dataService;
        private ArchiviazioneInfo _lastExportConfiguration;
        
        // Sistema di animazioni
        private AnimationManager animationManager;
        // Indicatori di caricamento per i pulsanti
        private readonly Timer _buttonLoadingTimer = new Timer();
        private readonly Timer _buttonLoadingDelayTimer = new Timer();
        private Button _buttonLoadingTarget;
        private Color _buttonLoadingOriginalBackColor;
        private Color _buttonLoadingOriginalForeColor;
        private string _buttonLoadingOriginalText;
        private double _buttonLoadingPhase;
        private bool _buttonOperationInProgress;
        private const int BUTTON_LOADING_DELAY_MS = 150; // Mostra effetto solo se operazione > 150ms
        
        // Collezioni thread-safe

        // ==================== HELPER METHODS AVANZATI ====================
        #region Advanced Helper Methods
        
        // Crea un pulsante moderno con effetti hover e animazioni
        /// <summary>
        /// Crea modern button al volo.
        /// </summary>
        private Button CreateModernButton(string text, Color baseColor, EventHandler onClick = null)
        {
            var btn = new Button
            {
                Text = text,
                Height = Sizes.ButtonHeight,
                MinimumSize = new Size(0, Sizes.ButtonHeight),
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeMD, FontStyle.Bold),
                ForeColor = baseColor,
                BackColor = Color.FromArgb(28, 28, 28),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(Spacing.MD, 0, Spacing.MD, 0)
            };

            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);

            var originalBg = btn.BackColor;
            var originalFg = btn.ForeColor;
            bool isHovering = false;

            btn.MouseEnter += (s, e) =>
            {
                isHovering = true;
                btn.BackColor = Color.FromArgb(40, 40, 40);
                Color targetColor = originalFg;
                try
                {
                    targetColor = ControlPaint.Light(originalFg, 0.25f);
                }
                catch
                {
                    targetColor = originalFg;
                }
                btn.ForeColor = targetColor;
            };

            btn.MouseLeave += (s, e) =>
            {
                isHovering = false;
                btn.BackColor = originalBg;
                btn.ForeColor = originalFg;
            };

            btn.MouseDown += (s, e) =>
            {
                btn.BackColor = Color.FromArgb(20, 20, 20);
                if (animationManager != null)
                {
                    animationManager.StartRipple(btn, e.Location);
                    animationManager.StartScale(btn, 1.0, 0.95, 100);
                }
            };

            btn.MouseUp += (s, e) =>
            {
                btn.BackColor = Color.FromArgb(40, 40, 40);
                if (animationManager != null)
                {
                    animationManager.StartScale(btn, 0.95, 1.0, 100);
                }
            };

            btn.ForeColorChanged += (s, e) =>
            {
                // Mantieni il colore di base originale anche quando il sistema
                // cambia temporaneamente il ForeColor (es. stato disabilitato).
                if (!isHovering && btn.Enabled)
                    originalFg = btn.ForeColor;
            };

            if (onClick != null)
                btn.Click += onClick;

            return btn;
        }
        
        /// <summary>
        /// Wrappa automaticamente un click di pulsante con effetto caricamento (solo se l'operazione dura più di 150ms).
        /// </summary>
        private void WrapButtonClickWithLoading(Button button, Action action)
        {
            if (button == null || action == null)
                return;
                
            // Se c'è già un caricamento attivo (es. da RunWithButtonLoading manuale), non wrappare
            if (_buttonLoadingTarget != null && !_buttonLoadingTarget.IsDisposed)
                return;
                
            // Se c'è già un'operazione in corso, non wrappare per evitare conflitti
            if (_buttonOperationInProgress)
                return;
                
            _buttonOperationInProgress = true;
            bool loadingShown = false;
            DateTime startTime = DateTime.Now;
            Timer delayTimer = null;
            
            // Timer per delay prima di mostrare l'effetto
            delayTimer = new Timer();
            delayTimer.Interval = BUTTON_LOADING_DELAY_MS;
            delayTimer.Tick += (s, args) =>
            {
                if (delayTimer != null)
                {
                    delayTimer.Stop();
                    delayTimer.Dispose();
                    delayTimer = null;
                }
                
                // Se l'operazione è ancora in corso e non c'è già un caricamento manuale, mostra l'effetto
                if (_buttonOperationInProgress && (_buttonLoadingTarget == null || _buttonLoadingTarget.IsDisposed))
                {
                    loadingShown = true;
                    StartButtonLoading(button);
                }
            };
            delayTimer.Start();
            
            try
            {
                action.Invoke();
            }
            finally
            {
                _buttonOperationInProgress = false;
                
                if (delayTimer != null)
                {
                    delayTimer.Stop();
                    delayTimer.Dispose();
                }
                
                // Se l'effetto è stato mostrato da questo wrapper, fermalo solo se è ancora questo pulsante
                if (loadingShown && _buttonLoadingTarget == button)
                {
                    StopButtonLoading();
                }
            }
        }

        private void ServerBrowseIntervalsButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Seleziona file TXT intervalli (server)",
                Filter = "File di testo (*.txt)|*.txt|Tutti i file (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    TryLoadIntervalsFromFile(dialog.FileName, isServer: true);
                }
            }
        }

        private void ArchiveBrowseIntervalsButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Seleziona file TXT intervalli (archivio)",
                Filter = "File di testo (*.txt)|*.txt|Tutti i file (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    TryLoadIntervalsFromFile(dialog.FileName, isServer: false);
                }
            }
        }

        private void ServerIntervalsPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (e?.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Consenti il drop per qualsiasi file; il filtraggio avviene in DragDrop/TryLoadIntervalsFromFile
                e.Effect = DragDropEffects.Copy;

                // Feedback visivo: evidenzia l'area di drop
                if (sender is Control ctrl)
                {
                    ctrl.BackColor = Color.FromArgb(45, 45, 45);
                }
                return;
            }

            e.Effect = DragDropEffects.None;

            // Ripristina il colore se il drag esce o non è valido
            if (sender is Control resetCtrl)
            {
                resetCtrl.BackColor = Color.FromArgb(32, 32, 32);
            }
        }

        private void ServerIntervalsPanel_DragDrop(object sender, DragEventArgs e)
        {
            if (e?.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            var txtFile = files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase))
                          ?? files[0];

            TryLoadIntervalsFromFile(txtFile, isServer: true);
        }

        private void ServerIntervalsPanel_DragLeave(object sender, EventArgs e)
        {
            if (sender is Control ctrl)
            {
                ctrl.BackColor = Color.FromArgb(32, 32, 32);
            }
        }

        private void ServerIntervalsPanel_DragOver(object sender, DragEventArgs e)
        {
            // Riutilizza la stessa logica di ServerIntervalsPanel_DragEnter per mantenere l'effetto attivo
            ServerIntervalsPanel_DragEnter(sender, e);
        }

        private void ArchiveIntervalsPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (e?.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Consenti il drop per qualsiasi file; il filtraggio avviene in DragDrop/TryLoadIntervalsFromFile
                e.Effect = DragDropEffects.Copy;

                // Feedback visivo: evidenzia l'area di drop
                if (sender is Control ctrl)
                {
                    ctrl.BackColor = Color.FromArgb(45, 45, 45);
                }
                return;
            }

            e.Effect = DragDropEffects.None;

            // Ripristina il colore se il drag esce o non è valido
            if (sender is Control resetCtrl)
            {
                resetCtrl.BackColor = Color.FromArgb(32, 32, 32);
            }
        }

        private void ArchiveIntervalsPanel_DragDrop(object sender, DragEventArgs e)
        {
            if (e?.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;

            var txtFile = files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase))
                          ?? files[0];

            TryLoadIntervalsFromFile(txtFile, isServer: false);
        }

        private void ArchiveIntervalsPanel_DragLeave(object sender, EventArgs e)
        {
            if (sender is Control ctrl)
            {
                ctrl.BackColor = Color.FromArgb(32, 32, 32);
            }
        }

        private void ArchiveIntervalsPanel_DragOver(object sender, DragEventArgs e)
        {
            // Riutilizza la stessa logica di ArchiveIntervalsPanel_DragEnter per mantenere l'effetto attivo
            ArchiveIntervalsPanel_DragEnter(sender, e);
        }

        /// <summary>
        /// Attiva un effetto di caricamento sul pulsante (onda chiara) e abilita il cursore di attesa.
        /// </summary>
        private void StartButtonLoading(Button button, string loadingText = null)
        {
            StopButtonLoading();
            _buttonLoadingTarget = button;
            _buttonLoadingPhase = 0;
            UseWaitCursor = true;
            Cursor.Current = Cursors.AppStarting;

            if (button != null)
            {
                _buttonLoadingOriginalBackColor = button.BackColor;
                _buttonLoadingOriginalForeColor = button.ForeColor;
                _buttonLoadingOriginalText = button.Text;
                button.Enabled = false;
                button.Refresh();
            }
            Application.DoEvents();

            _buttonLoadingTimer.Start();
        }

        /// <summary>
        /// Disattiva l'effetto di caricamento e ripristina lo stato originale del pulsante.
        /// </summary>
        private void StopButtonLoading()
        {
            _buttonLoadingTimer.Stop();
            UseWaitCursor = false;
            Cursor.Current = Cursors.Default;

            if (_buttonLoadingTarget != null && !_buttonLoadingTarget.IsDisposed)
            {
                _buttonLoadingTarget.Enabled = true;
                _buttonLoadingTarget.Text = _buttonLoadingOriginalText;
                _buttonLoadingTarget.BackColor = _buttonLoadingOriginalBackColor;
                _buttonLoadingTarget.ForeColor = _buttonLoadingOriginalForeColor;
                _buttonLoadingTarget.Refresh();
            }

            _buttonLoadingTarget = null;
        }

        /// <summary>
        /// Timer tick per generare l'effetto "acqua" sul pulsante in caricamento.
        /// </summary>
        private void ButtonLoadingTimer_Tick(object sender, EventArgs e)
        {
            if (_buttonLoadingTarget == null || _buttonLoadingTarget.IsDisposed)
            {
                StopButtonLoading();
                return;
            }

            _buttonLoadingPhase += 0.15;
            double wave = (Math.Sin(_buttonLoadingPhase) + 1) / 2; // 0..1

            var baseColor = _buttonLoadingOriginalBackColor;
            Color highlight;
            try
            {
                highlight = ControlPaint.Light(baseColor, 0.35f);
            }
            catch
            {
                highlight = Color.FromArgb(
                    Math.Min(255, baseColor.R + 40),
                    Math.Min(255, baseColor.G + 40),
                    Math.Min(255, baseColor.B + 40));
            }

            _buttonLoadingTarget.BackColor = BlendColors(baseColor, highlight, wave);
            _buttonLoadingTarget.ForeColor = _buttonLoadingOriginalForeColor;
            _buttonLoadingTarget.Refresh();
        }

        /// <summary>
        /// Wrapper rapido per eseguire un'azione mostrando feedback sul pulsante.
        /// </summary>
        private void RunWithButtonLoading(Button button, string loadingText, Action action)
        {
            // Non cambiamo più il testo del pulsante: usiamo solo l'effetto grafico
            StartButtonLoading(button, loadingText);
            try
            {
                Application.DoEvents(); // Forza il rendering immediato dell'effetto
                action?.Invoke();
            }
            finally
            {
                StopButtonLoading();
            }
        }

        /// <summary>
        /// Miscela due colori in base a un fattore (0..1).
        /// </summary>
        private Color BlendColors(Color from, Color to, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            int r = (int)(from.R + (to.R - from.R) * amount);
            int g = (int)(from.G + (to.G - from.G) * amount);
            int b = (int)(from.B + (to.B - from.B) * amount);
            return Color.FromArgb(r, g, b);
        }

        private RadioButton CreateModernRadioButton(string text, bool usePrimaryStyle = false)
        {
            var radio = new ModernRadioButtonControl
            {
                Text = text,
                AutoSize = true,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, Spacing.XXS, Spacing.SM, Spacing.XXS),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };

            radio.CheckedColor = usePrimaryStyle ? Colors.Primary : Colors.Success;

            radio.CheckedChanged += (s, e) =>
            {
                radio.ForeColor = radio.Checked ? Colors.TextPrimary : Colors.TextSecondary;
                radio.Invalidate();
            };

            radio.EnabledChanged += (s, e) =>
            {
                radio.ForeColor = radio.Enabled
                    ? (radio.Checked ? Colors.TextPrimary : Colors.TextSecondary)
                    : Color.FromArgb(120, Colors.TextSecondary);
                radio.Invalidate();
            };

            return radio;
        }

        private sealed class ModernRadioButtonControl : RadioButton
        {
            private const int CircleSize = 14;
            private const int CircleSpacing = 8;
            private const int DotInset = 4;

            private static readonly Color ContainerGray = Color.FromArgb(32, 32, 32); // #202020

            public Color CheckedColor { get; set; } = Colors.Success;

            public ModernRadioButtonControl()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = ContainerGray;
                UseVisualStyleBackColor = false;
            }

            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
                pevent.Graphics.Clear(ContainerGray);
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                var textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font);
                int width = textSize.Width + CircleSize + CircleSpacing;
                int height = Math.Max(textSize.Height, CircleSize);
                return new Size(width, height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                // Usa il colore grigio del contenitore #202020
                e.Graphics.Clear(ContainerGray);

                var circleRect = new Rectangle(0, (Height - CircleSize) / 2, CircleSize, CircleSize);
                var activeColor = CheckedColor;
                var borderColor = !Enabled
                    ? Color.FromArgb(80, Colors.TextSecondary)
                    : (Checked ? activeColor : Colors.TextSecondary);

                using (var pen = new Pen(borderColor, 2f))
                {
                    e.Graphics.DrawEllipse(pen, circleRect);
                }

                if (Checked)
                {
                    var dotRect = Rectangle.Inflate(circleRect, -DotInset, -DotInset);
                    using (var brush = new SolidBrush(activeColor))
                    {
                        e.Graphics.FillEllipse(brush, dotRect);
                    }
                }

                var textColor = Enabled ? ForeColor : Color.FromArgb(100, ForeColor);
                var textRect = new Rectangle(circleRect.Right + CircleSpacing, 0, Width - circleRect.Right - CircleSpacing, Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text ?? string.Empty,
                    Font,
                    textRect,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                if (Focused && ShowFocusCues)
                {
                    var focusRect = textRect;
                    focusRect.Inflate(-2, -4);
                    ControlPaint.DrawFocusRectangle(e.Graphics, focusRect, textColor, ContainerGray);
                }
            }
        }

        /// <summary>
        /// Crea icon square button al volo.
        /// </summary>
        private Button CreateIconSquareButton(string text, Color color, EventHandler onClick = null, string name = null)
        {
            var button = new Button
            {
                Text = text,
                Name = name ?? string.Empty,
                Width = Sizes.InputHeight,
                Height = Sizes.InputHeight,
                MinimumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                MaximumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold),
                ForeColor = color,
                BackColor = Color.FromArgb(28, 28, 28),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(Spacing.XS, Spacing.XXS, 0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                UseMnemonic = false
            };

            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);

            button.MouseEnter += (s, e) =>
            {
                if (!button.Enabled)
                    return;

                button.BackColor = Color.FromArgb(40, 40, 40);
                button.ForeColor = ControlPaint.Light(color, 0.3f);
            };

            button.MouseLeave += (s, e) =>
            {
                if (!button.Enabled)
                    return;

                button.BackColor = Color.FromArgb(28, 28, 28);
                button.ForeColor = color;
            };

            button.EnabledChanged += (s, e) =>
            {
                if (button.Enabled)
                {
                    button.ForeColor = color;
                    button.BackColor = Color.FromArgb(28, 28, 28);
                }
                else
                {
                    button.ForeColor = Color.FromArgb(110, color.R, color.G, color.B);
                    button.BackColor = Color.FromArgb(24, 24, 24);
                }
            };

            if (onClick != null)
                button.Click += onClick;

            return button;
        }
        
        // Crea un TextBox moderno con bordi e focus effects
        /// <summary>
        /// Crea modern text box al volo.
        /// </summary>
        private TextBox CreateModernTextBox(string placeholder)
        {
            var textBox = new TextBox
            {
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeMD),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Colors.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            
            if (!string.IsNullOrEmpty(placeholder))
            {
                // Inizia vuoto, non con il placeholder come testo
                textBox.Text = "";
                textBox.ForeColor = Colors.TextPrimary;
                textBox.Tag = placeholder; // Salva il placeholder nel Tag
                
                // Mostra placeholder quando vuoto e non ha focus
                Action showPlaceholder = () => {
                    if (string.IsNullOrWhiteSpace(textBox.Text) && !textBox.Focused)
                    {
                        textBox.Text = placeholder;
                        textBox.ForeColor = Colors.TextPlaceholder;
                    }
                };
                
                // Nascondi placeholder quando ottiene focus
                textBox.Enter += (s, e) =>
                {
                    if (textBox.Text == placeholder)
                    {
                        textBox.Text = "";
                        textBox.ForeColor = Colors.TextPrimary;
                    }
                };
                
                // Mostra placeholder quando perde focus se vuoto
                textBox.Leave += (s, e) =>
                {
                    showPlaceholder();
                };
                
                // Mostra placeholder inizialmente
                showPlaceholder();
            }
            
            return textBox;
        }
        
        // Crea un pannello con bordo sinistro colorato e ombra
        /// <summary>
        /// Crea accent panel al volo.
        /// </summary>
        private Panel CreateAccentPanel(Color accentColor, string title = "", Control actionsPanel = null, Padding? customPadding = null)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Colors.Surface,
                Padding = customPadding ?? new Padding(UIEffects.Spacing.XS, 0, UIEffects.Spacing.XS, UIEffects.Spacing.XS),
                Margin = Padding.Empty
            };
            
            // Paint event per effetti avanzati
            panel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = panel.ClientRectangle;
                if (rect.Width <= 0 || rect.Height <= 0)
                    return;
                
                using (var background = new SolidBrush(Colors.Surface))
                {
                    e.Graphics.FillRectangle(background, rect);
                }
                
                // Bordo sinistro colorato con gradiente
                if (panel.Height > 0)
                {
                var borderRect = new Rectangle(0, 0, 4, panel.Height);
                using (var borderGradient = new LinearGradientBrush(
                    borderRect,
                    accentColor,
                    ControlPaint.Dark(accentColor, 0.3f),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(borderGradient, borderRect);
                    }
                }
            };
            
            // Barra superiore con titolo e azioni
            int headerPaddingY = Spacing.XS;
            int headerPaddingTop = (int)Math.Max(0, headerPaddingY * 0.5); // Ridotto per alzare i titoli
            int headerPaddingBottom = headerPaddingY;
            var headerBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = Sizes.ButtonHeight + headerPaddingTop + headerPaddingBottom + 1,
                MinimumSize = new Size(0, Sizes.ButtonHeight + headerPaddingTop + headerPaddingBottom + 1),
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = Padding.Empty
            };

            // Separatore che rispetta il padding orizzontale del pannello
            var separatorContainer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(UIEffects.Spacing.XS, 0, UIEffects.Spacing.XS, 0),
                Margin = Padding.Empty
            };
            var separator = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 1,
                BackColor = Color.FromArgb(70, 70, 70)
            };
            separatorContainer.Controls.Add(separator);

            var headerContent = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = actionsPanel != null ? 2 : 1,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(Spacing.SM, headerPaddingTop, Spacing.SM, headerPaddingBottom)
            };
            headerContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            headerContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            if (actionsPanel != null)
                headerContent.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            if (!string.IsNullOrWhiteSpace(title))
            {
                var titleLabel = new Label
                {
                    Text = title,
                    Font = GetCachedFont(Fonts.Primary, 12, FontStyle.Bold),
                    ForeColor = accentColor,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0)
                };
                headerContent.Controls.Add(titleLabel, 0, 0);
            }

            if (actionsPanel != null)
            {
                var actionsControl = actionsPanel;
                int leftMargin = Math.Max(actionsControl.Margin.Left, Spacing.SM);
                int topMargin = Math.Max(actionsControl.Margin.Top, 0);
                int rightMargin = actionsControl.Margin.Right;
                int bottomMargin = actionsControl.Margin.Bottom;
                actionsControl.Margin = new Padding(leftMargin, topMargin, Math.Max(Spacing.SM, rightMargin), bottomMargin);
                actionsControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                actionsControl.MinimumSize = new Size(actionsControl.MinimumSize.Width, Sizes.ButtonHeight);
                actionsControl.MaximumSize = new Size(int.MaxValue, Sizes.ButtonHeight);
                headerContent.Controls.Add(actionsControl, 1, 0);
            }

            headerBar.Controls.Add(headerContent);
            headerBar.Controls.Add(separatorContainer);
            separatorContainer.BringToFront();

            panel.Controls.Add(headerBar);
            headerBar.BringToFront();
            
            return panel;
        }

        /// <summary>
        /// Crea accent container al volo.
        /// </summary>
        private Panel CreateAccentContainer(Color accentColor, Padding? innerPadding = null)
        {
            var padding = innerPadding ?? new Padding(Spacing.LG);
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Colors.Surface,
                Padding = padding
            };

            panel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = panel.ClientRectangle;

                using (var background = new SolidBrush(Colors.Surface))
                {
                    e.Graphics.FillRectangle(background, rect);
                }

                var borderRect = new Rectangle(0, 0, 4, panel.Height);
                using (var borderGradient = new LinearGradientBrush(
                    borderRect,
                    accentColor,
                    ControlPaint.Dark(accentColor, 0.3f),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(borderGradient, borderRect);
                }

                using (var outlinePen = new Pen(Color.FromArgb(55, 55, 55), 1))
                {
                    var outlineRect = new Rectangle(borderRect.Width, 0, rect.Width - borderRect.Width - 1, rect.Height - 1);
                    if (outlineRect.Width > 0 && outlineRect.Height > 0)
                    {
                        e.Graphics.DrawRectangle(outlinePen, outlineRect);
                    }
                }
            };
            
            return panel;
        }
        // Crea un ListView ottimizzato con rendering custom
        /// <summary>
        /// Crea modern list view al volo.
        /// </summary>
        private ListView CreateModernListView(string name, string[] columns, int[] widths)
        {
            bool isInCorso = string.Equals(name, "InCorsoListView", StringComparison.OrdinalIgnoreCase);
            bool isTerminate = string.Equals(name, "TerminateListView", StringComparison.OrdinalIgnoreCase);
            bool isActiveView = isInCorso || isTerminate;
            int progressColumnIndex = Array.FindIndex(columns, c => string.Equals(c, "Avanzamento", StringComparison.OrdinalIgnoreCase));

            var activeBackground = Color.FromArgb(32, 32, 32);
            var activeHeaderColor = Color.FromArgb(42, 42, 42);
            var activeEvenColor = Color.FromArgb(40, 40, 40);
            var activeOddColor = Color.FromArgb(36, 36, 36);

            var list = new ListView
            {
                Name = name,
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = !isActiveView,
                BorderStyle = isActiveView ? BorderStyle.None : BorderStyle.FixedSingle,
                BackColor = isActiveView ? activeBackground : Color.FromArgb(36, 36, 36),
                ForeColor = Colors.TextPrimary,
                Font = GetCachedFont(Fonts.Primary, isActiveView ? Fonts.SizeSM : Fonts.SizeMD),
                OwnerDraw = true,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            
            typeof(ListView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, list, new object[] { true });
            
            var listState = EnsureListViewState(list);
            if (listState != null)
                list.ListViewItemSorter = new ListViewItemComparer(listState.SortColumn, listState.Ascending);
            
            for (int i = 0; i < columns.Length; i++)
            {
                HorizontalAlignment alignment;
                if (isActiveView)
                {
                    if (i == 0)
                        alignment = HorizontalAlignment.Center;
                    else if (i == columns.Length - 1)
                        alignment = HorizontalAlignment.Center;
                    else
                        alignment = HorizontalAlignment.Left;
                }
                else
                {
                    alignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                }

                list.Columns.Add(new ColumnHeader
                {
                    Text = columns[i],
                    Width = widths[i],
                    TextAlign = alignment
                });
            }

            Color ResolveCellBackground(int rowIndex, int columnIndex, bool isSelected, bool isHot)
            {
                if (isSelected)
                {
                    var baseColor = Colors.Secondary;
                    int alpha = isActiveView ? 180 : 200;
                    return Color.FromArgb(alpha, baseColor);
                }

                Color evenColor = isActiveView ? activeEvenColor : Color.FromArgb(46, 46, 46);
                Color oddColor = isActiveView ? activeOddColor : Color.FromArgb(52, 52, 52);

                Color background = ((rowIndex + columnIndex) % 2 == 0) ? evenColor : oddColor;

                if (isHot)
                {
                    background = ControlPaint.Light(background, isActiveView ? 0.25f : 0.2f);
                }

                return background;
            }

            list.DrawColumnHeader += (s, e) =>
            {
                var state = EnsureListViewState(list);
                bool isSortedColumn = state != null && state.SortColumn == e.ColumnIndex;
                string arrowText = isSortedColumn ? (state.Ascending ? "▲" : "▼") : string.Empty;

                if (isActiveView)
                {
                    e.Graphics.FillRectangle(GetCachedBrush(activeHeaderColor), e.Bounds);
                    var headerFont = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
                    var textBounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height);

                    if (!string.IsNullOrEmpty(arrowText))
                    {
                        var arrowSize = TextRenderer.MeasureText(arrowText, headerFont);
                        var arrowBounds = new Rectangle(e.Bounds.Right - arrowSize.Width - 6, e.Bounds.Top, arrowSize.Width + 4, e.Bounds.Height);
                        TextRenderer.DrawText(e.Graphics, arrowText, headerFont, arrowBounds, Colors.TextPrimary,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
                        if (textBounds.Width > arrowSize.Width + 8)
                            textBounds.Width -= arrowSize.Width + 8;
                    }

                    TextRenderer.DrawText(e.Graphics, list.Columns[e.ColumnIndex].Text,
                        headerFont, textBounds, Colors.TextPrimary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    return;
                }

                e.Graphics.FillRectangle(GetCachedBrush(Colors.SurfaceLight), e.Bounds);
                e.Graphics.DrawLine(GetCachedPen(Colors.Border), 
                    e.Bounds.Left, e.Bounds.Bottom - 1, 
                    e.Bounds.Right, e.Bounds.Bottom - 1);
                
                var defaultFont = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
                var defaultBounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height);

                if (!string.IsNullOrEmpty(arrowText))
                {
                    var arrowSize = TextRenderer.MeasureText(arrowText, defaultFont);
                    var arrowBounds = new Rectangle(e.Bounds.Right - arrowSize.Width - 6, e.Bounds.Top, arrowSize.Width + 4, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, arrowText, defaultFont, arrowBounds, Colors.TextSecondary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
                    if (defaultBounds.Width > arrowSize.Width + 8)
                        defaultBounds.Width -= arrowSize.Width + 8;
                }

                TextRenderer.DrawText(e.Graphics, list.Columns[e.ColumnIndex].Text,
                    defaultFont, defaultBounds, Colors.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            };
            
            // Ordinamento colonna con indicatore visivo
            list.ColumnClick += (s, e) => ApplyListViewSort(list, e.Column);
            
            list.DrawItem += (s, e) =>
            {
                // Sfondo disegnato in DrawSubItem per ottenere effetto a scacchiera
            };
            
            list.DrawSubItem += (s, e) =>
            {
                if (e.ItemIndex < 0)
                    return;
                bool isSelected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
                bool isHot = (e.ItemState & ListViewItemStates.Hot) == ListViewItemStates.Hot;

                Color background = ResolveCellBackground(e.ItemIndex, e.ColumnIndex, isSelected, isHot);
                        e.Graphics.FillRectangle(GetCachedBrush(background), e.Bounds);

                bool isProgressColumn = progressColumnIndex >= 0 && e.ColumnIndex == progressColumnIndex;
                var job = e.Item.Tag as ExportJob;

                if (isProgressColumn)
                {
                    int progressValue = job != null ? job.Progress : 0;
                    if (progressValue <= 0)
                        {
                            var numeric = (e.SubItem.Text ?? string.Empty).Replace("%", string.Empty).Trim();
                        if (int.TryParse(numeric, out var parsedProgress))
                            progressValue = parsedProgress;
                    }

                    progressValue = Math.Max(0, Math.Min(100, progressValue));

                    var trackBounds = new Rectangle(e.Bounds.Left, e.Bounds.Top, e.Bounds.Width, e.Bounds.Height);
                    if (trackBounds.Width > 0 && trackBounds.Height > 0)
                    {
                        Color trackColor = ControlPaint.Dark(background, isActiveView ? 0.15f : 0.1f);
                        e.Graphics.FillRectangle(GetCachedBrush(trackColor), trackBounds);

                        if (progressValue > 0)
                        {
                            int fillWidth = (int)Math.Round(trackBounds.Width * (progressValue / 100f));
                            fillWidth = Math.Max(0, Math.Min(trackBounds.Width, fillWidth));
                            if (fillWidth > 0)
                            {
                                var fillRect = new Rectangle(trackBounds.Left, trackBounds.Top, fillWidth, trackBounds.Height);
                                Color fillColor = isSelected
                                    ? Color.FromArgb(230, Color.White)
                                    : (isActiveView ? Colors.Primary : Colors.Warning);
                            e.Graphics.FillRectangle(GetCachedBrush(fillColor), fillRect);
                            }
                        }

                        using (var borderPen = new Pen(ControlPaint.Dark(background, 0.4f)))
                        {
                            e.Graphics.DrawRectangle(borderPen, trackBounds.Left, trackBounds.Top, trackBounds.Width - 1, trackBounds.Height - 1);
                        }

                        string statusText = NormalizeStatus(job?.Status);
                        string overlay = string.IsNullOrWhiteSpace(statusText)
                            ? $"{progressValue}%"
                            : $"{statusText} - {progressValue}%";

                        Color overlayColor = progressValue > 55 && !isSelected ? Color.Black : Colors.TextPrimary;
                        TextRenderer.DrawText(e.Graphics,
                            overlay,
                            GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold),
                            Rectangle.Inflate(trackBounds, -6, -2),
                            overlayColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    }
                }
                else
                {
                    var header = list.Columns[e.ColumnIndex];
                    var textFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                    switch (header.TextAlign)
                    {
                        case HorizontalAlignment.Center:
                            textFlags |= TextFormatFlags.HorizontalCenter;
                            break;
                        case HorizontalAlignment.Right:
                            textFlags |= TextFormatFlags.Right;
                            break;
                        default:
                            textFlags |= TextFormatFlags.Left;
                            break;
                    }

                    Color textColor = isSelected ? Colors.TextPrimary : (isActiveView ? Color.FromArgb(225, 225, 225) : Colors.TextSecondary);
                    Rectangle textBounds = Rectangle.Inflate(e.Bounds, -8, -2);

                    if (!isActiveView && e.ColumnIndex == 0 && job != null)
                    {
                        var statusIconBounds = new Rectangle(textBounds.Left, textBounds.Top + (textBounds.Height / 2 - 6), 12, 12);
                        e.Graphics.FillEllipse(GetCachedBrush(GetStatusColor(job.Status)), statusIconBounds);
                        textBounds = new Rectangle(textBounds.Left + 18, textBounds.Top, textBounds.Width - 18, textBounds.Height);
                    }

                    if (isActiveView && header.TextAlign == HorizontalAlignment.Left)
                    {
                        textBounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height);
                        }

                    if (!isInCorso && isActiveView && e.Item.Tag is ArchiviazioneInfo archiviazioneInfo && !IsCompletedStatus(archiviazioneInfo.Stato))
                    {
                        if (e.ColumnIndex == list.Columns.Count - 1)
                            textColor = Colors.Error;
                    }

                        TextRenderer.DrawText(e.Graphics,
                        e.SubItem.Text,
                        GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                        textBounds,
                        textColor,
                        textFlags);
                }

                if (isSelected)
                {
                    using (var pen = new Pen(Color.FromArgb(220, Colors.Secondary), 1f))
                    {
                        var rect = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                        e.Graphics.DrawRectangle(pen, rect);
                    }
                }
            };

            list.MouseDown += (s, e) =>
            {
                var state = EnsureListViewState(list);
                if (state == null)
                    return;

                var hit = list.HitTest(e.Location);
                
                // Verifica se il click è sull'header (quando OwnerDraw = true, HitTest potrebbe non funzionare correttamente)
                // Usa l'altezza dell'header per determinare se il click è nell'area dell'header
                const int headerHeight = 25; // Altezza approssimativa dell'header
                if (e.Y >= 0 && e.Y < headerHeight && e.Button == MouseButtons.Left)
                {
                    // Calcola quale colonna è stata cliccata
                    int x = e.X;
                    int columnIndex = -1;
                    int accumulatedWidth = 0;
                    
                    for (int i = 0; i < list.Columns.Count; i++)
                    {
                        int columnWidth = list.Columns[i].Width;
                        if (x >= accumulatedWidth && x < accumulatedWidth + columnWidth)
                        {
                            columnIndex = i;
                            break;
                        }
                        accumulatedWidth += columnWidth;
                    }
                    
                    if (columnIndex >= 0)
                    {
                        ApplyListViewSort(list, columnIndex);
                        return;
                    }
                }
                
                if (hit.Item != null)
                {
                    if (!hit.Item.Selected)
                    {
                        list.SelectedItems.Clear();
                        hit.Item.Selected = true;
                    }
                    list.FocusedItem = hit.Item;

                    int subIndex = hit.Item.SubItems.IndexOf(hit.SubItem);
                    state.SelectedSubItem = subIndex >= 0 ? subIndex : 0;
                }
            };

            list.ItemSelectionChanged += (s, e) =>
            {
                if (!e.IsSelected)
                    return;

                var state = EnsureListViewState(list);
                if (state == null)
                    return;

                int maxSubItems = e.Item.SubItems.Count;
                if (state.SelectedSubItem >= maxSubItems || state.SelectedSubItem < 0)
                    state.SelectedSubItem = Math.Min(Math.Max(state.SelectedSubItem, 0), maxSubItems - 1);
            };

            list.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedListViewCell(list);
                    e.Handled = true;
                }
            };
            
            return list;
        }
        
        // Helper per creare rettangoli arrotondati (usa il metodo da UIEffects)
        /// <summary>
        /// Crea rounded rectangle al volo.
        /// </summary>
        private System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            return UIEffects.GetRoundedRectPath(rect, radius);
        }
        #endregion

        /// <summary>
        /// Costruttore di MainForm, inizializza il contesto senza effetti collaterali.
        /// </summary>
        public MainForm()
        {
            // Inizializza animation manager
            animationManager = new AnimationManager();
            _buttonLoadingTimer.Interval = 45;
            _buttonLoadingTimer.Tick += ButtonLoadingTimer_Tick;
            _dataService = new ExportDataService();
            try
            {
                _dataService.Initialize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore inizializzazione database: {ex.Message}");
            }
            try
            {
                var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "ProcureMapping.json");
                _procuraResolver = new ProcuraResolver(mappingPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Impossibile inizializzare ProcuraResolver: {ex.Message}");
            }
            LoadDatabaseState();
            
            // Configurazione form base
            this.Text = "Tool Archiviazioni Milestone - v1.0";
            this.Size = new Size(1000, 750); // ✅ MIGLIORAMENTO: Dimensione maggiore
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.MinimumSize = this.Size; // ✅ FIX #13: Impedisce il ridimensionamento al di sotto del default
            // Inizializzazione OLE per drag&drop
            try { NativeMethods.DragDropHelper.OleInitialize(IntPtr.Zero); } catch { }
            
            this.AllowDrop = true; // Abilita drag&drop a livello di form
            
            // Carica e imposta l'icona
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
                if (File.Exists(iconPath))
                {
                    this.Icon = new Icon(iconPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Impossibile caricare l'icona: {ex.Message}");
            }
            
            // Applica tema scuro
            SetupDarkTheme();
            
            // Componenti UI di supporto
            _errorProvider = new ErrorProvider
            {
                BlinkStyle = ErrorBlinkStyle.NeverBlink
            };
            _toolTip = new ToolTip();

            // Crea interfaccia personalizzata
            CreateCustomInterface();
            
            // Inizializza controlli
            InitializeCustomControls();

            // Assicura la directory log unificata
            EnsureLogDirectory();
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.Alt)
                {
                    if (e.KeyCode == Keys.D1) { ShowTab(0); e.Handled = true; }
                    else if (e.KeyCode == Keys.D2) { ShowTab(1); e.Handled = true; }
                    else if (e.KeyCode == Keys.D3) { ShowTab(2); e.Handled = true; }
                }
            };
            
            // Applica tema scuro Windows dopo che i controlli sono stati creati
            this.Load += MainForm_Load;
            
            // ✅ FIX #16: FormClosing event per conferma chiusura
            this.FormClosing += MainForm_FormClosing;
        }
        /// <summary>
        /// Si assicura che la parte log directory sia pronta prima di procedere.
        /// </summary>
        private void EnsureLogDirectory()
        {
            if (_logDirectoryInitialized) return;

            lock (_logDirectoryLock)
            {
                if (_logDirectoryInitialized) return;

                try
                {
                    Directory.CreateDirectory(_logDirectoryPath);

                    if (!string.Equals(_logDirectoryPath, _legacyLogDirectoryPath, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(_legacyLogDirectoryPath))
                    {
                        foreach (var sourceFile in Directory.EnumerateFiles(_legacyLogDirectoryPath, "*.log"))
                        {
                            try
                            {
                                var destinationName = Path.GetFileName(sourceFile);
                                if (string.IsNullOrWhiteSpace(destinationName))
                                    continue;

                                var destinationPath = Path.Combine(_logDirectoryPath, destinationName);
                                if (File.Exists(destinationPath))
                                {
                                    string fallbackName = Path.GetFileNameWithoutExtension(destinationName) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log";
                                    destinationPath = Path.Combine(_logDirectoryPath, fallbackName);
                                }

                                File.Move(sourceFile, destinationPath);
                            }
                            catch
                            {
                                Debug.WriteLine($"Errore migrazione log '{sourceFile}'");
                            }
                        }

                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(_legacyLogDirectoryPath).Any())
                            {
                                Directory.Delete(_legacyLogDirectoryPath, false);
                            }
                        }
                        catch (Exception exCleanup)
                        {
                            Debug.WriteLine($"Errore eliminazione directory log legacy: {exCleanup.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Errore inizializzazione directory log: {ex.Message}");
                }

                _logDirectoryInitialized = true;
                if (_logIndexService == null)
                {
                    _logIndexService = new LogIndexService(_logDirectoryPath);
                }
            }
        }


        /// <summary>
        /// Risolve procura auto senza interventi manuali.
        /// </summary>
        private void ResolveProcuraAuto()
        {
            try
            {
                if (_procuraResolver == null)
                {
                    var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "ProcureMapping.json");
                    _procuraResolver = new ProcuraResolver(mappingPath);
                }

                var result = _procuraResolver.Resolve();
                if (result != null && !string.IsNullOrWhiteSpace(result.Name))
                {
                    _currentProcura = NormalizeProcuraName(result.Name);
                    Debug.WriteLine($"Procura rilevata automaticamente: {_currentProcura} (codice {result.Code}, IP {result.SourceAddress}, NIC {result.InterfaceName})");
                    UpdateProcuraStatusLabel();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore rilevamento automatico Procura: {ex.Message}");
            }
            finally
            {
                UpdateProcuraStatusLabel();
            }
        }

        // ✅ FIX #16: Conferma chiusura con export attivi
        /// <summary>
        /// Gestisce l'evento form closing del controllo main form per tenere la UI reattiva.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            lock (_exportLock)
            {
                if (_activeExports.Count > 0)
                {
                    var result = ShowConfirmation(
                        "Export in esecuzione",
                        $"Ci sono {_activeExports.Count} archiviazioni ancora in corso.\n\nChiudendo il tool interromperai tutti gli export attivi e potresti perdere i dati già raccolti.\n\nVuoi comunque uscire?",
                        DarkDialogButtons.YesNo,
                        DarkDialogIcon.Warning,
                        primaryButtonText: "Chiudi e annulla",
                        secondaryButtonText: "Resta nel tool");

                    if (result == DialogResult.No)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }
            
            // Cleanup risorse
            CleanupAllResources();
        }
        
        // ✅ FIX #1, #2, #3: Cleanup completo di tutte le risorse
        /// <summary>
        /// Pulisce all resources e rilascia risorse.
        /// </summary>
        private void CleanupAllResources()
        {
            // Ferma e disposa tutti gli export attivi
            lock (_exportLock)
            {
                foreach (var job in _activeExports.ToList())
                {
                    try
                    {
                        job.IsCanceled = true;
                        
                        if (job.Timer != null)
                        {
                            job.Timer.Stop();
                            if (job.TimerHandler != null)
                            {
                                job.Timer.Tick -= job.TimerHandler;
                                job.TimerHandler = null;
                            }
                            job.Timer.Dispose();
                            job.Timer = null;
                        }

                        if (job.Exporter != null)
                        {
                            job.Exporter.Cancel();
                            job.Exporter.EndExport();
                            job.Exporter.Close();
                            
                            if (job.Exporter is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                            job.Exporter = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore cleanup job {job.Id}: {ex.Message}");
                    }
                }
                _activeExports.Clear();
            }

            // Cleanup exporter principale
            if (_exporter != null)
            {
                try
                {
                    _exporter.Cancel();
                    _exporter.EndExport();
                    _exporter.Close();
                    
                    if (_exporter is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch { }
                _exporter = null;
            }

            // Cleanup timer principale
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }

            // Rimuovi server
            try
            {
                VideoOS.Platform.SDK.Environment.RemoveAllServers();
            }
            catch { }
            finally
            {
                _isServerConnected = false;
            }
        }
        
        // ✅ FIX #3, #4: Override Dispose per cleanup event handlers e cache GDI+
        /// <summary>
        /// Rilascia lo stato e chiude risorse gestite.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose animation manager
                animationManager?.Dispose();
                animationManager = null;
                
                CleanupAllResources();
                
                // Cleanup cache GDI+
                foreach (var brush in _brushCache.Values)
                {
                    brush?.Dispose();
                }
                _brushCache.Clear();
                
                foreach (var pen in _penCache.Values)
                {
                    pen?.Dispose();
                }
                _penCache.Clear();
                
                // Cleanup cache font
                foreach (var font in _fontCache.Values)
                {
                    font?.Dispose();
                }
                _fontCache.Clear();

                // Dispose dei componenti designer se presenti
                if (components != null)
                {
                    components.Dispose();
                    components = null;
                }
            }
            
            base.Dispose(disposing);
        }

        /// <summary>
        /// Gestisce l'evento load del controllo main form per tenere la UI reattiva.
        /// </summary>
        private void MainForm_Load(object sender, EventArgs e)
        {
            ApplyWindowsDarkTheme();
            ResolveProcuraAuto();
            LoadCompletedExports();
        }

        /// <summary>
        /// Applica windows dark theme alle impostazioni correnti.
        /// </summary>
        private void ApplyWindowsDarkTheme()
        {
            try
            {
                // Forza tema scuro per la finestra
                var windowHandle = this.Handle;
                int darkMode = 1;
                DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                
                // Disabilita effetto Mica
                int micaEffect = 0;
                DwmSetWindowAttribute(windowHandle, DWMWA_MICA_EFFECT, ref micaEffect, sizeof(int));
                
                // Applica tema scuro a tutti i controlli
                ApplyDarkThemeToControls(this);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore nell'applicazione del tema scuro: {ex.Message}");
            }
        }

        /// <summary>
        /// Applica dark theme to controls alle impostazioni correnti.
        /// </summary>
        private void ApplyDarkThemeToControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is ComboBox comboBox)
                {
                    // Forza tema scuro per ComboBox
                    SetWindowTheme(comboBox.Handle, "DarkMode_Explorer", null);
                    
                    // Stile personalizzato per ComboBox
                    comboBox.BackColor = Color.FromArgb(45, 45, 45);
                    comboBox.ForeColor = Color.White;
                    comboBox.FlatStyle = FlatStyle.System; // Usa stile Windows nativo
                }
                else if (control is TextBox textBox)
                {
                    // Forza tema scuro per TextBox
                    SetWindowTheme(textBox.Handle, "DarkMode_Explorer", null);
                }
                else if (control is Button button)
                {
                    // Forza tema scuro per Button
                    SetWindowTheme(button.Handle, "DarkMode_Explorer", null);
                }
                
                // Applica ricorsivamente a controlli figli
                if (control.HasChildren)
                {
                    ApplyDarkThemeToControls(control);
                }
            }
        }

        /// <summary>
        /// Imposta up dark theme usando i parametri passati.
        /// </summary>
        private void SetupDarkTheme()
        {
            // Colori tema scuro
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
        }
        /// <summary>
        /// Crea custom interface al volo.
        /// </summary>
        private void CreateCustomInterface()
        {
            // Tab Panel - DESIGN MODERNO AVANZATO con gradiente e ombra
            tabPanel = new Panel
            {
                Height = 56, // Altezza aumentata per icone e badge
                BackColor = Color.FromArgb(28, 28, 28),
                Dock = DockStyle.Top
            };

            // Aggiungi effetto gradiente al tab panel
            tabPanel.Paint += (s, e) =>
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    tabPanel.ClientRectangle,
                    Color.FromArgb(32, 32, 32),
                    Color.FromArgb(24, 24, 24),
                    System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(brush, tabPanel.ClientRectangle);
                }

                // Linea sottile superiore per separazione
                using (var pen = new Pen(Color.FromArgb(50, 50, 50), 1))
                {
                    e.Graphics.DrawLine(pen, 0, 0, tabPanel.Width, 0);
                }

                // Ombra inferiore
                using (var shadowBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, tabPanel.Height - 4, tabPanel.Width, 4),
                    Color.FromArgb(20, 0, 0, 0),
                    Color.Transparent,
                    System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(shadowBrush, 0, tabPanel.Height - 4, tabPanel.Width, 4);
                }
            };

            // Indicatore animato per tab attivo con effetto glow
            _tabUnderline = new Panel
            {
                Height = 4,
                BackColor = Color.FromArgb(33, 150, 243),
                Visible = true
            };

            // Effetto glow per l'indicatore
            _tabUnderline.Paint += (s, e) =>
            {
                var rect = _tabUnderline.ClientRectangle;
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddRectangle(rect);
                    using (var brush = new System.Drawing.Drawing2D.PathGradientBrush(path))
                    {
                        brush.CenterColor = _tabUnderline.BackColor;
                        brush.SurroundColors = new[] { Color.FromArgb(100, _tabUnderline.BackColor) };
                        e.Graphics.FillRectangle(brush, rect);
                    }
                }
            };

            // Content panel con transizioni fluide
            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(0)
            };

            // Status Panel migliorato
            statusPanel = new Panel
            {
                Height = 32,
                BackColor = Color.FromArgb(40, 40, 40),
                Dock = DockStyle.Bottom
            };

            // Aggiungi bordo superiore al status panel
            statusPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(60, 60, 60), 1))
                {
                    e.Graphics.DrawLine(pen, 0, 0, statusPanel.Width, 0);
                }
            };

            this.Controls.Add(contentPanel);
            this.Controls.Add(statusPanel);
            this.Controls.Add(tabPanel);
            tabPanel.Controls.Add(_tabUnderline);

            CreateEnhancedTabButtons();
            CreateMainContent();
            CreateStatusBar();
            InitializeStatusBarSignatureDetection();

            // Event handler per resize dinamico con animazione
            this.Resize += (s, e) => AnimateTabResize();
        }
        /// <summary>
        /// Crea enhanced tab buttons al volo.
        /// </summary>
        private void CreateEnhancedTabButtons()
        {
            // Crea i pulsanti del tab con design avanzato, icone e badge
            var lancioBtn = CreateAdvancedTabButton(
                "🚀 LANCIO", 
                "", 
                0, 
                Color.FromArgb(33, 150, 243),
                "Configura e avvia nuove archiviazioni"
            );
            
            var inCorsoBtn = CreateAdvancedTabButton(
                "⏳ IN CORSO", 
                "", 
                1, 
                Color.FromArgb(255, 152, 0),
                "Monitora gli export in esecuzione"
            );
            
            var completateBtn = CreateAdvancedTabButton(
                "✅ TERMINATE", 
                "", 
                2, 
                Color.FromArgb(76, 175, 80),
                "Gestisci le archiviazioni completate"
            );
            
            lancioBtn.Click += (s, e) => AnimateTabSwitch(0);
            inCorsoBtn.Click += (s, e) => AnimateTabSwitch(1);
            completateBtn.Click += (s, e) => AnimateTabSwitch(2);
            
            tabPanel.Controls.AddRange(new Control[] { lancioBtn, inCorsoBtn, completateBtn });
            
            // Aggiungi separatori verticali tra i tab
            AddTabSeparators();
            
            // Resize iniziale con animazione
            AnimateTabResize();
            
            // Inizializza con tab LANCIO attivo
            ShowTab(0);
            UpdateTabNotifications();
        }

        /// <summary>
        /// Esegue la logica animate tab resize senza cambiare il comportamento.
        /// </summary>
        private void AnimateTabResize()
        {
            if (tabPanel == null) return;
            
            int tabWidth = tabPanel.Width / 3;
            int index = 0;
            
            // Ridimensiona pulsanti
            foreach (Control ctrl in tabPanel.Controls)
            {
                if (ctrl is Button btn && btn.Tag is TabButtonInfo)
                {
                    btn.Width = tabWidth;
                    btn.Left = index * tabWidth;
                    index++;
                }
                else if (ctrl is Button btn2 && btn2.Tag is int)
                {
                    btn2.Width = tabWidth;
                    btn2.Left = index * tabWidth;
                    index++;
                }
            }
            
            // Aggiorna separatori
            int sepIndex = 0;
            foreach (Control ctrl in tabPanel.Controls)
            {
                if (ctrl is Panel && ctrl.Width == 1)
                {
                    sepIndex++;
                    ctrl.Left = tabWidth * sepIndex - 1;
                }
            }
            
            // Anima underline verso la nuova posizione
            if (_tabUnderline != null && _tabUnderline.Visible)
            {
                var activeBtn = tabPanel.Controls.OfType<Button>().FirstOrDefault(b => 
                    b.Tag is TabButtonInfo info && info.Index == _currentTab);
                    
                if (activeBtn != null)
                {
                    AnimateUnderline(activeBtn);
                }
            }
        }

        /// <summary>
        /// Esegue la logica animate underline senza cambiare il comportamento.
        /// </summary>
        private void AnimateUnderline(Button targetBtn)
        {
            if (_tabUnderline == null || targetBtn == null) return;
            
            Timer animTimer = new Timer { Interval = 10 };
            int startLeft = _tabUnderline.Left;
            int startWidth = _tabUnderline.Width;
            int targetLeft = targetBtn.Left;
            int targetWidth = targetBtn.Width;
            float progress = 0;
            
            animTimer.Tick += (s, e) =>
            {
                progress += 0.15f;
                
                if (progress >= 1)
                {
                    _tabUnderline.Left = targetLeft;
                    _tabUnderline.Width = targetWidth;
                    _tabUnderline.Top = tabPanel.Height - _tabUnderline.Height;
                    animTimer.Stop();
                    animTimer.Dispose();
                }
                else
                {
                    // Easing function per movimento fluido
                    float eased = EaseInOutCubic(progress);
                    _tabUnderline.Left = (int)(startLeft + (targetLeft - startLeft) * eased);
                    _tabUnderline.Width = (int)(startWidth + (targetWidth - startWidth) * eased);
                }
            };
            
            animTimer.Start();
        }

        /// <summary>
        /// Esegue la logica ease in out cubic senza cambiare il comportamento.
        /// </summary>
        private float EaseInOutCubic(float t)
        {
            return t < 0.5f ? 4 * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 3) / 2;
        }

        /// <summary>
        /// Esegue la logica animate tab switch senza cambiare il comportamento.
        /// </summary>
        private void AnimateTabSwitch(int tabIndex)
        {
            if (_currentTab == tabIndex) return;
            
            // Fade out contenuto corrente
            Timer fadeTimer = new Timer { Interval = 10 };
            int opacity = 255;
            
            fadeTimer.Tick += (s, e) =>
            {
                opacity -= 25;
                
                if (opacity <= 0)
                {
                    fadeTimer.Stop();
                    fadeTimer.Dispose();
                    ShowTab(tabIndex);
                    
                    // Fade in nuovo contenuto
                    Timer fadeInTimer = new Timer { Interval = 10 };
                    int fadeInOpacity = 0;
                    
                    fadeInTimer.Tick += (s2, e2) =>
                    {
                        fadeInOpacity += 25;
                        
                        if (fadeInOpacity >= 255)
                        {
                            fadeInTimer.Stop();
                            fadeInTimer.Dispose();
                        }
                    };
                    
                    fadeInTimer.Start();
                }
            };
            
            fadeTimer.Start();
        }

        /// <summary>
        /// Crea modern tab button al volo.
        /// </summary>
        private Button CreateModernTabButton(string text, int index, Color activeColor)
        {
            return CreateAdvancedTabButton(text, "", index, activeColor, "");
        }

        /// <summary>
        /// Crea advanced tab button al volo.
        /// </summary>
        private Button CreateAdvancedTabButton(string text, string icon, int index, Color themeColor, string tooltip)
        {
            var btn = new Button
            {
                Height = 52,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(160, 160, 160),
                FlatStyle = FlatStyle.Flat,
                Font = GetCachedFont("Segoe UI", 9.5f, FontStyle.Regular),
                Tag = new TabButtonInfo { Index = index, ThemeColor = themeColor, Icon = icon },
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(8, 0, 8, 0)
            };
            
            // Imposta testo con icona
            btn.Text = $"{icon}  {text}";
            
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 40);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(35, 35, 35);
            
            // Tooltip avanzato
            if (!string.IsNullOrEmpty(tooltip))
            {
                _toolTip.SetToolTip(btn, tooltip);
            }
            
            // Effetti hover avanzati
            bool isHovering = false;
            Timer hoverTimer = new Timer { Interval = 20 };
            
            btn.MouseEnter += (s, e) => 
            {
                isHovering = true;
                if (!IsTabActive(index))
                {
                    hoverTimer.Start();
                }
            };
            
            btn.MouseLeave += (s, e) => 
            {
                isHovering = false;
                if (!IsTabActive(index))
                {
                    hoverTimer.Start();
                }
            };
            
            // Animazione hover fluida
            int animStep = 0;
            hoverTimer.Tick += (s, e) =>
            {
                if (IsTabActive(index))
                {
                    hoverTimer.Stop();
                    return;
                }
                
                if (isHovering)
                {
                    animStep = Math.Min(animStep + 20, 100);
                    int r = InterpolateColor(160, 220, animStep / 100f);
                    btn.ForeColor = Color.FromArgb(r, r, r);
                    
                    if (animStep >= 100)
                    {
                        hoverTimer.Stop();
                    }
                }
                else
                {
                    animStep = Math.Max(animStep - 20, 0);
                    int r = InterpolateColor(160, 220, animStep / 100f);
                    btn.ForeColor = Color.FromArgb(r, r, r);
                    
                    if (animStep <= 0)
                    {
                        hoverTimer.Stop();
                    }
                }
            };
            
            // Effetto click con ripple
            btn.MouseDown += (s, e) =>
            {
                if (!IsTabActive(index))
                {
                    CreateRippleEffect(btn, e.Location, themeColor);
                }
            };
            
            return btn;
        }

        private class TabButtonInfo
        {
            public int Index { get; set; }
            public Color ThemeColor { get; set; }
            public string Icon { get; set; }
        }

        /// <summary>
        /// Indica se tab active soddisfa i criteri richiesti.
        /// </summary>
        private bool IsTabActive(int index)
        {
            return _currentTab == index;
        }

        /// <summary>
        /// Esegue la logica interpolate color senza cambiare il comportamento.
        /// </summary>
        private int InterpolateColor(int start, int end, float progress)
        {
            return (int)(start + (end - start) * progress);
        }

        /// <summary>
        /// Crea ripple effect al volo.
        /// </summary>
        private void CreateRippleEffect(Button btn, Point clickPoint, Color rippleColor)
        {
            // Crea un effetto ripple al click
            Timer rippleTimer = new Timer { Interval = 20 };
            int rippleRadius = 0;
            int maxRadius = Math.Max(btn.Width, btn.Height);
            
            btn.Paint += RipplePaint;
            
            void RipplePaint(object sender, PaintEventArgs e)
            {
                if (rippleRadius > 0 && rippleRadius < maxRadius)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(50 - (rippleRadius * 50 / maxRadius), rippleColor)))
                    {
                        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        e.Graphics.FillEllipse(brush, 
                            clickPoint.X - rippleRadius, 
                            clickPoint.Y - rippleRadius, 
                            rippleRadius * 2, 
                            rippleRadius * 2);
                    }
                }
            }
            
            rippleTimer.Tick += (s, e) =>
            {
                rippleRadius += 10;
                btn.Invalidate();
                
                if (rippleRadius >= maxRadius)
                {
                    rippleTimer.Stop();
                    btn.Paint -= RipplePaint;
                    btn.Invalidate();
                    rippleTimer.Dispose();
                }
            };
            
            rippleTimer.Start();
        }

        /// <summary>
        /// Aggiorna tab notifications e mantiene lo stato coerente.
        /// </summary>
        private void UpdateTabNotifications()
        {
            // Metodo mantenuto per compatibilità, ma non esegue più alcuna operazione
            // I badge sono stati rimossi completamente
        }

        /// <summary>
        /// Esegue la logica pulse tab button senza cambiare il comportamento.
        /// </summary>
        private void PulseTabButton(int tabIndex)
        {
            var btn = tabPanel.Controls.OfType<Button>().FirstOrDefault(b =>
            {
                if (b.Tag is TabButtonInfo info) return info.Index == tabIndex;
                if (b.Tag is int legacy) return legacy == tabIndex;
                return false;
            });

            if (btn == null || _currentTab == tabIndex) return;

            animationManager?.StartPulse(btn, 1200);
        }
        /// <summary>
        /// Mostra tab all'utente.
        /// </summary>
        private void ShowTab(int tabIndex)
        {
            _currentTab = tabIndex;

            foreach (var btn in tabPanel.Controls.OfType<Button>())
            {
                int buttonIndex;
                Color themeColor;

                if (btn.Tag is TabButtonInfo info)
                {
                    buttonIndex = info.Index;
                    themeColor = info.ThemeColor;
                }
                else if (btn.Tag is int legacyIndex)
                {
                    buttonIndex = legacyIndex;
                    themeColor = GetTabColor(legacyIndex);
                }
                else
                {
                    continue;
                }

                if (buttonIndex == tabIndex)
                {
                    btn.BackColor = Color.FromArgb(20, themeColor);
                    btn.ForeColor = themeColor;
                    btn.Font = GetCachedFont("Segoe UI", 10f, FontStyle.Bold);
                }
                else
                {
                    btn.BackColor = Color.Transparent;
                    btn.ForeColor = Color.FromArgb(160, 160, 160);
                    btn.Font = GetCachedFont("Segoe UI", 9.5f, FontStyle.Regular);
                }
            }

            var activeBtn = tabPanel.Controls.OfType<Button>().FirstOrDefault(b =>
            {
                if (b.Tag is TabButtonInfo info) return info.Index == tabIndex;
                if (b.Tag is int legacy) return legacy == tabIndex;
                return false;
            });

            if (activeBtn != null)
            {
                AnimateUnderline(activeBtn);
                _tabUnderline.BackColor = GetTabColor(tabIndex);
                _tabUnderline.Visible = true;
                _tabUnderline.BringToFront();
            }

            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();

            switch (tabIndex)
            {
                case 0:
                    CreateLancioContent();
                    break;
                case 1:
                    CreateInCorsoContent();
                    LoadActiveExports();
                    break;
                case 2:
                    CreateTerminateContent();
                    LoadCompletedExports();
                    break;
            }

            contentPanel.ResumeLayout();

            UpdateTabNotifications();
            SaveCurrentTabPreference(tabIndex);
        }

        /// <summary>
        /// Restituisce tab color gia pronto.
        /// </summary>
        private Color GetTabColor(int tabIndex)
        {
            switch (tabIndex)
            {
                case 0: return Color.FromArgb(33, 150, 243);
                case 1: return Color.FromArgb(255, 152, 0);
                case 2: return Color.FromArgb(76, 175, 80);
                default: return Color.FromArgb(128, 128, 128);
            }
        }

        /// <summary>
        /// Salva current tab preference in modo sicuro.
        /// </summary>
        private void SaveCurrentTabPreference(int tabIndex)
        {
            try
            {
                SaveUserSetting("LastTab", tabIndex.ToString());
            }
            catch { }
        }

        /// <summary>
        /// Esegue la logica add tab separators senza cambiare il comportamento.
        /// </summary>
        private void AddTabSeparators()
        {
            if (tabPanel == null) return;

            var separators = tabPanel.Controls.OfType<Panel>()
                .Where(p => Equals(p.Tag, "TabSeparator"))
                .ToList();

            foreach (var sep in separators)
            {
                tabPanel.Controls.Remove(sep);
                sep.Dispose();
            }

            var buttons = tabPanel.Controls.OfType<Button>().OrderBy(b => b.Left).ToList();
            if (buttons.Count <= 1) return;

            int tabWidth = tabPanel.Width > 0 ? tabPanel.Width / buttons.Count : 0;

            for (int i = 1; i < buttons.Count; i++)
            {
                var separator = new Panel
                {
                    Tag = "TabSeparator",
                    Width = 1,
                    Height = 30,
                    BackColor = Color.FromArgb(50, 50, 50),
                    Top = (tabPanel.Height - 30) / 2,
                    Left = tabWidth * i - 1
                };

                tabPanel.Controls.Add(separator);
                separator.SendToBack();
            }
        }

        /// <summary>
        /// Applica button style alle impostazioni correnti.
        /// </summary>
        private void ApplyButtonStyle(Button btn, Color baseColor, Color hoverColor, Color downColor)
        {
            if (btn == null) return;

            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = baseColor;
            btn.ForeColor = Color.White;
            btn.FlatAppearance.MouseOverBackColor = hoverColor;
            btn.FlatAppearance.MouseDownBackColor = downColor;
        }

        /// <summary>
        /// Applica list view sort alle impostazioni correnti.
        /// </summary>
        private void ApplyListViewSort(ListView lv, int column)
        {
            if (lv == null) return;

            var state = EnsureListViewState(lv) ?? new ListViewState();
            int previousColumn = state.SortColumn;

            if (state.SortColumn == column)
            {
                state.Ascending = !state.Ascending;
            }
            else
            {
                state.SortColumn = column;
                state.Ascending = true; // Default ascendente
            }

            lv.Tag = state;
            lv.ListViewItemSorter = new ListViewItemComparer(state.SortColumn, state.Ascending);
            lv.Sort();
            
            // Con OwnerDraw = true, dobbiamo forzare il ridisegno degli header manualmente
            // Invalida l'header control direttamente usando Win32 API
            if (lv.Handle != IntPtr.Zero)
            {
                // Trova l'header control del ListView (classe "SysHeader32")
                IntPtr headerHandle = FindWindowEx(lv.Handle, IntPtr.Zero, "SysHeader32", null);
                if (headerHandle != IntPtr.Zero)
                {
                    // Invalida l'intero header control
                    InvalidateRect(headerHandle, IntPtr.Zero, true);
                    UpdateWindow(headerHandle);
                }
            }
            
            // Invalida anche manualmente ogni colonna header per forzare il ridisegno
            // Questo è necessario perché con OwnerDraw il ridisegno potrebbe non essere automatico
            const int headerHeight = 30;
            int accumulatedWidth = 0;
            for (int i = 0; i < lv.Columns.Count; i++)
            {
                int columnWidth = lv.Columns[i].Width;
                var headerRect = new Rectangle(accumulatedWidth, 0, columnWidth, headerHeight);
                lv.Invalidate(headerRect);
                accumulatedWidth += columnWidth;
            }
            
            // Forza il ridisegno immediato usando Refresh() che forza il ridisegno completo
            lv.Refresh();
            
            // Invalida anche l'intero controllo per sicurezza e forza il ridisegno
            lv.Invalidate();
            lv.Refresh();
        }

        private class ListViewItemComparer : System.Collections.IComparer
        {
            private readonly int _column;
            private readonly bool _ascending;

            /// <summary>
            /// Costruttore di ListViewItemComparer, inizializza il contesto senza effetti collaterali.
            /// </summary>
            public ListViewItemComparer(int column, bool ascending)
            {
                _column = column;
                _ascending = ascending;
            }

            /// <summary>
            /// Esegue la logica compare senza cambiare il comportamento.
            /// </summary>
            public int Compare(object x, object y)
            {
                var itemX = x as ListViewItem;
                var itemY = y as ListViewItem;

                string textX = itemX != null && itemX.SubItems.Count > _column
                    ? itemX.SubItems[_column].Text
                    : string.Empty;

                string textY = itemY != null && itemY.SubItems.Count > _column
                    ? itemY.SubItems[_column].Text
                    : string.Empty;

                if (double.TryParse(textX.Replace("%", string.Empty), out double dx) &&
                    double.TryParse(textY.Replace("%", string.Empty), out double dy))
                {
                    return _ascending ? dx.CompareTo(dy) : dy.CompareTo(dx);
                }

                if (DateTime.TryParse(textX, out DateTime tx) && DateTime.TryParse(textY, out DateTime ty))
                {
                    return _ascending ? tx.CompareTo(ty) : ty.CompareTo(tx);
                }

                int result = string.Compare(textX, textY, StringComparison.InvariantCultureIgnoreCase);
                return _ascending ? result : -result;
            }
        }

        private class ListViewState
        {
            public int SortColumn { get; set; } = 0;
            public bool Ascending { get; set; } = true;
            public int SelectedSubItem { get; set; } = 0;
        }

        private sealed class ArchivePackageInfo
        {
            public string BaseName { get; set; }
            public DateTime Timestamp { get; set; }
            public string FullPath { get; set; }
            public long SizeBytes { get; set; }
            public bool IsDirectory { get; set; }
        }

        private sealed class ArchiveServerState
        {
            public string RawInput { get; set; }
            public string NormalizedHost { get; set; }
            public string PreferredShare { get; set; }
            public string ConnectedShare { get; set; }
            public string MappedDriveLetter { get; set; }
            public DateTime LastValidationUtc { get; set; }
            public string LastError { get; set; }
            public List<string> AvailableShares { get; } = new List<string>();

            public string ServerRoot => string.IsNullOrWhiteSpace(NormalizedHost) ? null : $@"\\{NormalizedHost}";
            public bool IsValid => string.IsNullOrWhiteSpace(LastError) && !string.IsNullOrWhiteSpace(ServerRoot);

            public void ResetShares(IEnumerable<string> shares)
            {
                AvailableShares.Clear();
                if (shares == null)
                    return;
                foreach (var share in shares)
                {
                    if (!string.IsNullOrWhiteSpace(share))
                        AvailableShares.Add(share);
                }
            }
        }

        private sealed class ArchiveBaseNameOption
        {
            public string BaseName { get; set; }
            public string Display { get; set; }
            public override string ToString() => Display;
        }

        private struct IntervalRange
        {
            public DateTime Start { get; set; }
            public DateTime End { get; set; }

            public override string ToString()
            {
                return $"{Start:dd/MM/yyyy HH:mm} -> {End:dd/MM/yyyy HH:mm}";
            }
        }

        private struct ArchiveSelectionInfo
        {
            public static readonly ArchiveSelectionInfo Empty = new ArchiveSelectionInfo(0, 0, Array.Empty<ArchivePackageInfo>());

            public ArchiveSelectionInfo(long totalBytes, int packageCount, IReadOnlyList<ArchivePackageInfo> packages)
            {
                TotalBytes = totalBytes;
                PackageCount = packageCount;
                Packages = packages ?? Array.Empty<ArchivePackageInfo>();
            }

            public long TotalBytes { get; }
            public int PackageCount { get; }
            public IReadOnlyList<ArchivePackageInfo> Packages { get; }
            public bool HasPackages => PackageCount > 0;
        }

        /// <summary>
        /// Si assicura che la parte list view state sia pronta prima di procedere.
        /// </summary>
        private ListViewState EnsureListViewState(ListView list)
        {
            if (list == null)
                return null;

            var state = list.Tag as ListViewState;
            if (state == null)
            {
                state = new ListViewState();
                list.Tag = state;
            }
            return state;
        }
        /// <summary>
        /// Esegue la logica copy selected list view cell senza cambiare il comportamento.
        /// </summary>
        private void CopySelectedListViewCell(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0)
                return;

            var state = EnsureListViewState(list);
            if (state == null)
                return;

            var item = list.SelectedItems[0];
            int columnIndex = state.SelectedSubItem;
            if (columnIndex < 0 || columnIndex >= item.SubItems.Count)
                columnIndex = 0;

            state.SelectedSubItem = columnIndex;

            string text = item.SubItems[columnIndex].Text ?? string.Empty;
            Clipboard.SetText(text);
        }

        /// <summary>
        /// Esegue la logica add copy cell menu senza cambiare il comportamento.
        /// </summary>
        private void AddCopyCellMenu(ListView listView, ContextMenuStrip cms)
        {
            if (listView == null || cms == null)
                return;

            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("Copia cella", null, (s, e) => CopySelectedListViewCell(listView));
        }

        /// <summary>
        /// Crea main content al volo.
        /// </summary>
        private void CreateMainContent()
        {
            CreateLancioContent();
        }

        /// <summary>
        /// Crea lancio content al volo.
        /// </summary>
        private void CreateLancioContent()
        {
            contentPanel.Controls.Clear();
            contentPanel.BackColor = Colors.Background;
            contentPanel.Padding = new Padding(0);
            _lancioActionButtons.Clear();
            
            // Container principale con margini uniformi
            var mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = Spacing.PagePadding,
                BackColor = Color.Transparent
            };
            
            // Layout principale con sezioni ben distanziate
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 4,
                BackColor = Color.Transparent,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            
            // Colonne: 50% | gap fisso | 50% - UGUALI
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.SectionGap)); // Gap fisso 16px
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            
            // Righe: sezioni | gap | pulsante1 | pulsante2
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Spacing.SectionGap));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            
            // Colonna sinistra con FORMATO + SORGENTE dinamico
            var leftColumn = CreateLaunchLeftColumn();
            mainLayout.Controls.Add(leftColumn, 0, 0);
            
            // SEZIONE CONFIGURAZIONE (colonna destra) 
            var configPanel = CreateModernConfigSection(_lancioActionButtons);
            mainLayout.Controls.Add(configPanel, 2, 0);
            NormalizeButtonWidths(_lancioActionButtons.ToArray());
            
            // PULSANTE AVVIA ARCHIVIAZIONE (largo tutto lo schermo)
            var avviaBtn = CreateModernButton($"{Icons.Rocket} AVVIA ARCHIVIAZIONE", Colors.Primary);
            avviaBtn.Name = "AvviaArchiviazioneButton";
            avviaBtn.Dock = DockStyle.Fill;
            avviaBtn.Height = 48;
            avviaBtn.Font = GetCachedFont(Fonts.Primary, Fonts.SizeLG, FontStyle.Bold);
            avviaBtn.Click += AvviaArchiviazione_Click;
            mainLayout.SetColumnSpan(avviaBtn, 3);
            mainLayout.Controls.Add(avviaBtn, 0, 2);
            
            // PULSANTE RECUPERA ULTIMO LANCIO (largo tutto lo schermo)
            var recuperaBtn = CreateModernButton($"{Icons.Import} RECUPERA ULTIMO LANCIO", Color.FromArgb(120, 120, 120));
            recuperaBtn.Name = "RecuperaUltimoButton";
            recuperaBtn.Dock = DockStyle.Fill;
            recuperaBtn.Height = 48;
            recuperaBtn.Font = GetCachedFont(Fonts.Primary, Fonts.SizeLG, FontStyle.Bold);
            recuperaBtn.Click += RecuperaUltimoLancio_Click;
            mainLayout.SetColumnSpan(recuperaBtn, 3);
            mainLayout.Controls.Add(recuperaBtn, 0, 3);
            
            mainContainer.Controls.Add(mainLayout);
            contentPanel.Controls.Add(mainContainer);
            ApplyLaunchConnectionState();
#if DEBUG
            mainLayout.Layout += (sender, args) =>
            {
                LogBounds("LANCIO - LeftColumn", leftColumn);
                LogBounds("LANCIO - ConfigPanel", configPanel);
                LogBounds("LANCIO - MainLayout", mainLayout);
                Debug.WriteLine("--------------------------------------------------");
            };
#endif
        }

        private Control CreateLaunchLeftColumn()
        {
            var leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };

            leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Spacing.SectionGap));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var formatoPanel = CreateFormatoSection();
            formatoPanel.Margin = Padding.Empty;
            leftLayout.Controls.Add(formatoPanel, 0, 0);

            _sorgenteHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };

            leftLayout.Controls.Add(_sorgenteHost, 0, 2);
            _leftStackLayout = leftLayout;
            RenderSorgenteContent();

            return leftLayout;
        }

        private Panel CreateFormatoSection()
        {
            var panel = CreateAccentPanel(Colors.Primary, $"{Icons.Layout} FORMATO");
            panel.MinimumSize = new Size(0, 150);
            var sectionPadding = GetUniformSectionPadding();
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(sectionPadding.Left, 28 + sectionPadding.Top, sectionPadding.Right, sectionPadding.Bottom),
                BackColor = Color.Transparent,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

            int row = 0;

            var tipologiaLabel = new Label
            {
                Text = $"{Icons.Layout} Tipologia archiviazione",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(tipologiaLabel, layout.ColumnCount);
            layout.Controls.Add(tipologiaLabel, 0, row++);
            EnsureRowStyle(layout, row - 1, Sizes.LabelHeight - 4);

            var tipologiaSelector = CreateTipologiaSelector();
            tipologiaSelector.Margin = new Padding(0, Spacing.XXS, 0, Spacing.XXS);
            layout.SetColumnSpan(tipologiaSelector, layout.ColumnCount);
            layout.Controls.Add(tipologiaSelector, 0, row);
            EnsureRowStyle(layout, row++, Sizes.ButtonHeight);

            var modalitaLabel = new Label
            {
                Text = $"{Icons.Switch} Modalità di lancio",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(modalitaLabel, layout.ColumnCount);
            layout.Controls.Add(modalitaLabel, 0, row++);
            EnsureRowStyle(layout, row - 1, Sizes.LabelHeight - 4);

            var modeSelector = CreateLaunchModeSelector();
            modeSelector.Margin = new Padding(0, Spacing.XXS, 0, 0);
            layout.SetColumnSpan(modeSelector, layout.ColumnCount);
            layout.Controls.Add(modeSelector, 0, row);
            EnsureRowStyle(layout, row++, Sizes.ButtonHeight);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control CreateLaunchModeSelector()
        {
            var container = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, Spacing.XXS, 0, 0),
                Padding = new Padding(0)
            };

            _launchModeServerRadio = CreateModernRadioButton("Da server", usePrimaryStyle: true);
            _launchModeServerRadio.Margin = new Padding(0, 0, Spacing.SM, 0);
            _launchModeServerRadio.Checked = _currentLaunchMode == LaunchMode.Server;
            _launchModeServerRadio.CheckedChanged += (s, e) =>
            {
                if (_launchModeServerRadio.Checked)
                {
                    SetLaunchMode(LaunchMode.Server);
                }
            };

            _launchModeArchiveRadio = CreateModernRadioButton("Da archivio", usePrimaryStyle: true);
            _launchModeArchiveRadio.Margin = new Padding(0, 0, Spacing.SM, 0);
            _launchModeArchiveRadio.Checked = _currentLaunchMode == LaunchMode.Archivio;
            _launchModeArchiveRadio.CheckedChanged += (s, e) =>
            {
                if (_launchModeArchiveRadio.Checked)
                {
                    SetLaunchMode(LaunchMode.Archivio);
                }
            };

            container.Controls.Add(_launchModeServerRadio);
            container.Controls.Add(_launchModeArchiveRadio);

            return container;
        }

        private Control CreateTipologiaSelector()
        {
            var container = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, Spacing.XXS, 0, 0),
                Padding = new Padding(0)
            };

            _tipologiaLegaleRadio = CreateModernRadioButton("Legale", usePrimaryStyle: true);
            _tipologiaLegaleRadio.Margin = new Padding(0, 0, Spacing.SM, 0);
            _tipologiaLegaleRadio.Checked = _currentTipologia == ArchiviazioneTipologia.Legale;
            _tipologiaLegaleRadio.CheckedChanged += (s, e) =>
            {
                if (_tipologiaLegaleRadio.Checked)
                {
                    SetTipologia(ArchiviazioneTipologia.Legale);
                }
            };

            _tipologiaDigitaleRadio = CreateModernRadioButton("Digitale", usePrimaryStyle: true);
            _tipologiaDigitaleRadio.Margin = new Padding(0, 0, Spacing.SM, 0);
            _tipologiaDigitaleRadio.Enabled = false;
            _tipologiaDigitaleRadio.Checked = _currentTipologia == ArchiviazioneTipologia.Digitale;

            container.Controls.Add(_tipologiaLegaleRadio);
            container.Controls.Add(_tipologiaDigitaleRadio);

            return container;
        }

        private Button CreateLaunchModeButton(string text, LaunchMode mode)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Colors.SurfaceLight,
                ForeColor = Colors.TextSecondary,
                Padding = new Padding(Spacing.MD, Spacing.XXS, Spacing.MD, Spacing.XXS),
                Margin = new Padding(0, 0, Spacing.XS, 0),
                Cursor = Cursors.Hand,
                Tag = mode,
                Height = Sizes.ButtonHeight
            };

            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Colors.Border;
            button.FlatAppearance.MouseOverBackColor = Colors.InputHover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 20, 20);

            button.Click += (s, e) =>
            {
                SetLaunchMode(mode);
            };

            return button;
        }

        private Button CreateTipologiaButton(string text, ArchiviazioneTipologia tipologia, bool enabled)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Colors.SurfaceLight,
                ForeColor = Colors.TextSecondary,
                Padding = new Padding(Spacing.MD, Spacing.XXS, Spacing.MD, Spacing.XXS),
                Margin = new Padding(0, 0, Spacing.XS, 0),
                Cursor = enabled ? Cursors.Hand : Cursors.No,
                Tag = tipologia,
                Enabled = enabled,
                Height = Sizes.ButtonHeight
            };

            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Colors.Border;
            button.FlatAppearance.MouseOverBackColor = Colors.InputHover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 20, 20);

            button.Click += (s, e) =>
            {
                if (!button.Enabled)
                    return;
                SetTipologia(tipologia);
            };

            return button;
        }

        private void SetLaunchMode(LaunchMode mode)
        {
            if (_currentLaunchMode == mode)
                return;

            _currentLaunchMode = mode;
            UpdateLaunchModeRadios();
            RenderSorgenteContent();
        }

        private void UpdateLaunchModeRadios()
        {
            if (_launchModeServerRadio != null)
                _launchModeServerRadio.Checked = _currentLaunchMode == LaunchMode.Server;
            if (_launchModeArchiveRadio != null)
                _launchModeArchiveRadio.Checked = _currentLaunchMode == LaunchMode.Archivio;
        }

        private void SetTipologia(ArchiviazioneTipologia tipologia)
        {
            if (_currentTipologia == tipologia)
                return;

            _currentTipologia = tipologia;
            UpdateTipologiaRadios();
        }

        private void UpdateTipologiaRadios()
        {
            if (_tipologiaLegaleRadio != null)
                _tipologiaLegaleRadio.Checked = _currentTipologia == ArchiviazioneTipologia.Legale;
            if (_tipologiaDigitaleRadio != null)
                _tipologiaDigitaleRadio.Checked = _currentTipologia == ArchiviazioneTipologia.Digitale;
        }

        private Panel CreateArchiveSourcePanel(List<Button> launchButtons)
        {
            var panel = CreateAccentPanel(Colors.Success, $"{Icons.Camera} SORGENTE");
            var sectionPadding = GetUniformSectionPadding();
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 13,
                Padding = new Padding(sectionPadding.Left, 28 + sectionPadding.Top, sectionPadding.Right, sectionPadding.Bottom),
                BackColor = Color.Transparent
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            int row = 0;
            int buttonVerticalOffset = -Spacing.XXS;

            var archivioLabel = new Label
            {
                Text = $"{Icons.Folder} Percorso archivi (pacchetti)",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, Spacing.XXS, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(archivioLabel, layout.ColumnCount);
            layout.Controls.Add(archivioLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);

            _archiveRootTextBox = CreateModernTextBox("es: vs01\\D oppure \\\\vs01\\D\\Archivio");
            _archiveRootTextBox.Name = "ArchiveRootTextBox";
            _archiveRootTextBox.Dock = DockStyle.Fill;
            _archiveRootTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            _archiveRootTextBox.TextChanged += ArchiveRootTextBox_TextChanged;
            _archiveRootTextBox.Leave += ArchiveRootTextBox_Leave;
            layout.Controls.Add(_archiveRootTextBox, 0, row);
            layout.Controls.Add(CreateSpacerCell(), 1, row);
            EnsureRowStyle(layout, row, Sizes.InputHeight);

            var browseArchiveButton = CreateModernButton($"{Icons.Folder} Sfoglia", Colors.Success, BrowseArchiveRootButton_Click);
            browseArchiveButton.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            browseArchiveButton.Dock = DockStyle.None;
            browseArchiveButton.AutoSize = true;
            browseArchiveButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            browseArchiveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browseArchiveButton.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            layout.Controls.Add(browseArchiveButton, 2, row);
            layout.Controls.Add(CreateSpacerCell(), 3, row);
            var pinArchiveRootButton = CreateIconSquareButton(Icons.Pin, Colors.Success, PinArchiveRootButton_Click, "PinArchiveRootButton");
            pinArchiveRootButton.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            layout.Controls.Add(pinArchiveRootButton, 4, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight);
            launchButtons?.Add(browseArchiveButton);

            var pinnedArchiveRoot = LoadUserSetting("PinnedArchiveRoot");
            if (!string.IsNullOrWhiteSpace(pinnedArchiveRoot))
            {
                _archiveRootTextBox.Text = pinnedArchiveRoot;
                _archiveRootTextBox.ForeColor = Colors.TextPrimary;
                _archiveRootPath = pinnedArchiveRoot;
            }

            var gruppoLabel = new Label
            {
                Text = $"{Icons.Folder} Gruppo pacchetti (base-name)",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, Spacing.XXS, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(gruppoLabel, layout.ColumnCount);
            layout.Controls.Add(gruppoLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);

            var archiveBaseRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            archiveBaseRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            archiveBaseRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            archiveBaseRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _archiveBaseNamesTextBox = CreateModernTextBox("Seleziona uno o più gruppi pacchetti...");
            _archiveBaseNamesTextBox.Name = "ArchiveBaseNamesTextBox";
            _archiveBaseNamesTextBox.ReadOnly = true;
            _archiveBaseNamesTextBox.Dock = DockStyle.Fill;
            _archiveBaseNamesTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            archiveBaseRow.Controls.Add(_archiveBaseNamesTextBox, 0, 0);

            archiveBaseRow.Controls.Add(CreateSpacerCell(), 1, 0);

            var archiveBaseButton = CreateModernButton($"{Icons.Search} Seleziona", Colors.Success);
            archiveBaseButton.Name = "SelectArchiveBaseButton";
            archiveBaseButton.Dock = DockStyle.None;
            archiveBaseButton.AutoSize = true;
            archiveBaseButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            archiveBaseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            archiveBaseButton.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            archiveBaseButton.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            archiveBaseButton.Click += ArchiveBaseNameButton_Click;
            archiveBaseRow.Controls.Add(archiveBaseButton, 2, 0);

            layout.SetColumnSpan(archiveBaseRow, layout.ColumnCount);
            layout.Controls.Add(archiveBaseRow, 0, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight);
            UpdateArchiveBaseNameText();

            _archivePerGroupRadio = CreateModernRadioButton("Crea un'archiviazione separata per ogni gruppo pacchetti selezionato");
            _archivePerGroupRadio.AutoCheck = false;
            // Stessa distanza verticale usata per il radio "per intervallo"
            _archivePerGroupRadio.Margin = new Padding(0, Spacing.XS, 0, Spacing.XXS);
            _archivePerGroupRadio.Click += (s, e) =>
            {
                _archivePerGroupRadio.Checked = !_archivePerGroupRadio.Checked;
            };
            layout.SetColumnSpan(_archivePerGroupRadio, layout.ColumnCount);
            layout.Controls.Add(_archivePerGroupRadio, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight + Spacing.XS);

            var intervalloLabel = new Label
            {
                Text = $"{Icons.Calendar} Intervallo data/ora (archivio)",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(intervalloLabel, layout.ColumnCount);
            layout.Controls.Add(intervalloLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);

            var datePanel = CreateArchiveDateRangePanel();
            layout.SetColumnSpan(datePanel, layout.ColumnCount);
            layout.Controls.Add(datePanel, 0, row);
            // Riga che contiene sia i campi data/ora che il rettangolo TXT, stessa altezza combinata del server (date + dropzone)
            EnsureRowStyle(layout, row++, Sizes.InputHeight + Spacing.XXS + Sizes.ButtonHeight + Spacing.XS);

            _archivePerIntervalRadio = CreateModernRadioButton("Crea un'archiviazione separata per ogni intervallo");
            _archivePerIntervalRadio.AutoCheck = false;
            _archivePerIntervalRadio.Margin = new Padding(0, Spacing.XS, 0, Spacing.XXS);
            _archivePerIntervalRadio.Click += (s, e) =>
            {
                _archivePerIntervalRadio.Checked = !_archivePerIntervalRadio.Checked;
            };
            layout.SetColumnSpan(_archivePerIntervalRadio, layout.ColumnCount);
            layout.Controls.Add(_archivePerIntervalRadio, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight + Spacing.XS);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control CreateArchiveDateRangePanel()
        {
            // Altezza analoga alla modalità server per avere distanze coerenti
            int dateRowHeight = Sizes.InputHeight + Spacing.XXS;

            var datePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = new Padding(0),
                MinimumSize = new Size(0, dateRowHeight)
            };

            var dateLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 2, // riga 0: Inizio/Fine, riga 1: rettangolo TXT
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                AutoSize = false
            };

            const float dateTextPercent = 0.38f;
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dateTextPercent * 100));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sizes.InputHeight));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dateTextPercent * 100));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sizes.InputHeight));
            // Riga 0: controlli data/ora, Riga 1: rettangolo TXT (più alta per non tagliare il contenuto)
            dateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, dateRowHeight));
            dateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Sizes.ButtonHeight + Spacing.XS));

            var inizioLabel = new Label
            {
                Text = "Inizio:",
                Font = GetCachedFont(Fonts.Primary, 9f),
                ForeColor = Colors.TextMuted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopRight,
                Padding = new Padding(0, Spacing.XXS, 5, 0),
                AutoSize = false,
                Margin = new Padding(0)
            };
            dateLayout.Controls.Add(inizioLabel, 0, 0);

            _archiveStartTextBox = CreateModernTextBox("__/__/____ __:__");
            _archiveStartTextBox.Name = "ArchiveStartTextBox";
            _archiveStartTextBox.Dock = DockStyle.Fill;
            _archiveStartTextBox.Font = GetCachedFont(Fonts.Primary, 10f);
            _archiveStartTextBox.BackColor = Color.FromArgb(35, 35, 35);
            _archiveStartTextBox.ForeColor = Colors.TextPrimary;
            _archiveStartTextBox.BorderStyle = BorderStyle.FixedSingle;
            _archiveStartTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            _archiveStartTextBox.MaxLength = 16;
            AttachDateTimeNormalization(_archiveStartTextBox);
            _archiveStartTextBox.TextChanged += ArchiveDateTextChanged;
            dateLayout.Controls.Add(_archiveStartTextBox, 1, 0);

            dateLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 2, 0);

            var calInizioBtn = new Button
            {
                Text = "📅",
                Name = "ArchiveCalStartButton",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Colors.Success,
                Font = GetCachedFont(Fonts.Primary, 9f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 0),
                MinimumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                MaximumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                TextAlign = ContentAlignment.MiddleCenter
            };
            calInizioBtn.FlatAppearance.BorderSize = 1;
            calInizioBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);
            calInizioBtn.MouseEnter += (s, e) =>
            {
                calInizioBtn.BackColor = Color.FromArgb(40, 40, 40);
                calInizioBtn.ForeColor = ControlPaint.Light(Colors.Success, 0.3f);
            };
            calInizioBtn.MouseLeave += (s, e) =>
            {
                calInizioBtn.BackColor = Color.FromArgb(28, 28, 28);
                calInizioBtn.ForeColor = Colors.Success;
            };
            calInizioBtn.Click += (s, e) => ShowDatePicker(_archiveStartTextBox, true);
            dateLayout.Controls.Add(calInizioBtn, 3, 0);

            var fineLabel = new Label
            {
                Text = "Fine:",
                Font = GetCachedFont(Fonts.Primary, 9f),
                ForeColor = Colors.TextMuted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopRight,
                Padding = new Padding(0, Spacing.XXS, 5, 0),
                AutoSize = false,
                Margin = new Padding(0)
            };
            dateLayout.Controls.Add(fineLabel, 4, 0);

            _archiveEndTextBox = CreateModernTextBox("__/__/____ __:__");
            _archiveEndTextBox.Name = "ArchiveEndTextBox";
            _archiveEndTextBox.Dock = DockStyle.Fill;
            _archiveEndTextBox.Font = GetCachedFont(Fonts.Primary, 10f);
            _archiveEndTextBox.BackColor = Color.FromArgb(35, 35, 35);
            _archiveEndTextBox.ForeColor = Colors.TextPrimary;
            _archiveEndTextBox.BorderStyle = BorderStyle.FixedSingle;
            _archiveEndTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            _archiveEndTextBox.MaxLength = 16;
            AttachDateTimeNormalization(_archiveEndTextBox);
            _archiveEndTextBox.TextChanged += ArchiveDateTextChanged;
            dateLayout.Controls.Add(_archiveEndTextBox, 5, 0);

            dateLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 6, 0);

            var calFineBtn = new Button
            {
                Text = "📅",
                Name = "ArchiveCalEndButton",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Colors.Success,
                Font = GetCachedFont(Fonts.Primary, 9f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 0),
                MinimumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                MaximumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                TextAlign = ContentAlignment.MiddleCenter
            };
            calFineBtn.FlatAppearance.BorderSize = 1;
            calFineBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);
            calFineBtn.MouseEnter += (s, e) =>
            {
                calFineBtn.BackColor = Color.FromArgb(40, 40, 40);
                calFineBtn.ForeColor = ControlPaint.Light(Colors.Success, 0.3f);
            };
            calFineBtn.MouseLeave += (s, e) =>
            {
                calFineBtn.BackColor = Color.FromArgb(28, 28, 28);
                calFineBtn.ForeColor = Colors.Success;
            };
            calFineBtn.Click += (s, e) => ShowDatePicker(_archiveEndTextBox, false);
            dateLayout.Controls.Add(calFineBtn, 7, 0);

            // Rettangolo dropzone TXT per gli intervalli, integrato nell'area Intervallo data/ora
            var archiveIntervalsDropZone = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 24), // leggermente più scuro
                Margin = new Padding(0, 0, Spacing.XS, 0),
                // Solo padding orizzontale per centrare meglio verticalmente il testo
                Padding = new Padding(Spacing.SM, 0, Spacing.SM, 0),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                MinimumSize = new Size(0, Sizes.ButtonHeight),
                Height = Sizes.ButtonHeight  // stessa altezza del rettangolo lato server
            };

            var archiveIntervalsDropLabel = new Label
            {
                Text = "Rilascia o clicca qui per inserire più intervalli",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS),
                ForeColor = Colors.TextSecondary
            };

            // Memorizza la label per poter mostrare gli intervalli direttamente nel rettangolo
            _archiveIntervalsDropLabel = archiveIntervalsDropLabel;

            // Click per aprire il file TXT
            archiveIntervalsDropLabel.Click += (s, e) => ArchiveBrowseIntervalsButton_Click(s, e);
            
            // Abilita drag&drop direttamente sulla label
            archiveIntervalsDropLabel.AllowDrop = true;
            archiveIntervalsDropLabel.DragEnter += (s, e) => {
                if (e?.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
                else
                    e.Effect = DragDropEffects.None;
            };
            archiveIntervalsDropLabel.DragDrop += (s, e) => {
                if (e?.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                var txtFile = files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase)) ?? files[0];
                TryLoadIntervalsFromFile(txtFile, isServer: false);
            };

            archiveIntervalsDropZone.Controls.Add(archiveIntervalsDropLabel);
            archiveIntervalsDropZone.Click += (s, e) => ArchiveBrowseIntervalsButton_Click(s, e);
            
            // Abilita drag&drop anche sul pannello
            archiveIntervalsDropZone.AllowDrop = true;
            archiveIntervalsDropZone.DragEnter += (s, e) => {
                if (e?.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
                else
                    e.Effect = DragDropEffects.None;
            };
            archiveIntervalsDropZone.DragDrop += (s, e) => {
                if (e?.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                var txtFile = files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase)) ?? files[0];
                TryLoadIntervalsFromFile(txtFile, isServer: false);
            };

            // Riga con rettangolo + pulsanti CONTROLLA / CANCELLA (layout come lato server)
            var archiveIntervalsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                // Nessuna indentazione speciale: stessa partenza orizzontale di "Intervallo data/ora"
                Margin = new Padding(0, Spacing.XS, 0, 0),
                Padding = new Padding(0)
            };
            archiveIntervalsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            archiveIntervalsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            archiveIntervalsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            archiveIntervalsRow.Controls.Add(archiveIntervalsDropZone, 0, 0);

            var archiveCheckButton = CreateModernButton($"{Icons.Check} Controlla", Colors.Secondary);
            archiveCheckButton.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            archiveCheckButton.Margin = new Padding(0, 0, 0, 0);
            archiveCheckButton.AutoSize = true;
            archiveCheckButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            archiveCheckButton.Click += (s, e) => ShowIntervalsPreview(isServer: false);
            archiveIntervalsRow.Controls.Add(archiveCheckButton, 1, 0);

            // Pulsante quadrato solo icona (X) per cancellare gli intervalli archivio
            var archiveClearButton = CreateIconSquareButton(Icons.Cross, Colors.Error, (s, e) => ClearIntervals(isServer: false), "ArchiveIntervalsClearButton");
            archiveClearButton.Margin = new Padding(Spacing.XS, 0, 0, 0);
            archiveIntervalsRow.Controls.Add(archiveClearButton, 2, 0);

            // Aggiungi la riga completa (rettangolo + pulsanti) sotto le date, come lato server (colonna 0 su tutta la larghezza)
            dateLayout.Controls.Add(archiveIntervalsRow, 0, 1);
            dateLayout.SetColumnSpan(archiveIntervalsRow, 8);

            // Memorizza solo il pannello rettangolo come dropzone archivio
            _archiveIntervalsDropZone = archiveIntervalsDropZone;

            datePanel.Controls.Add(dateLayout);
            return datePanel;
        }

        private void BrowseArchiveRootButton_Click(object sender, EventArgs e)
        {
            // Normalizza l'input prima di procedere
            NormalizeArchivePathInput();

            string rawPath = GetRealValue(_archiveRootTextBox);
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                ShowInfo("Percorso archivi", "Inserisci il percorso nel formato: vs01\\NomeDisco oppure \\\\vs01\\NomeDisco");
                return;
            }

            // Apri direttamente il dialog senza check preliminare (è troppo lento su UNC)
            // Il dialog gestirà gli errori di connessione
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Seleziona la cartella che contiene i pacchetti da archiviare."
            })
            {
                string initialPath = rawPath;
                    if (!string.IsNullOrWhiteSpace(_archiveRootPath) && Directory.Exists(_archiveRootPath))
                    initialPath = _archiveRootPath;

                // Prova a impostare il percorso, ma non bloccare se fallisce
                TrySetFolderBrowserPath(dialog, initialPath);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    
                    // Verifica veloce solo dopo la selezione
                    if (!Directory.Exists(selectedPath))
                    {
                        string hostname = ExtractHostnameFromPath(selectedPath);
                        string shareName = ExtractShareNameFromPath(selectedPath);
                        string serverName = !string.IsNullOrWhiteSpace(hostname) ? hostname.ToUpperInvariant() : "il server";
                        string diskName = !string.IsNullOrWhiteSpace(shareName) ? shareName : "il disco";

                        ShowWarning("Percorso non raggiungibile",
                            $"Il percorso \"{selectedPath}\" non è accessibile.\n\n" +
                            $"Verifica che:\n" +
                            $"• Il server {serverName} sia acceso e raggiungibile\n" +
                            $"• Il disco \"{diskName}\" sia condiviso su {serverName}\n\n" +
                            $"Per condividere il disco su {serverName}:\n" +
                            $"1. Accedi a {serverName}\n" +
                            $"2. Tasto destro sul disco → Proprietà → Condivisione\n" +
                            $"3. Condividi il disco");
                        return;
                    }

                    _archiveRootTextBox.Text = selectedPath;
                    _archiveRootTextBox.ForeColor = Colors.Success;

                    StartArchiveScan(selectedPath, sender as Button);
                }
            }
        }

        private void TryLoadArchivePackagesFromTextBox()
        {
            TryLoadArchivePackagesFromTextBox(null);
        }

        private void TryLoadArchivePackagesFromTextBox(Button sourceButton)
        {
            if (_archiveRootTextBox == null)
                return;

            var path = GetRealValue(_archiveRootTextBox);
            if (string.IsNullOrWhiteSpace(path))
                return;
            var normalized = path.Trim();
            if (!string.IsNullOrEmpty(_archiveRootPath) &&
                string.Equals(_archiveRootPath, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            StartArchiveScan(normalized, sourceButton);
        }

        private void StartArchiveScan(string path, Button sourceButton)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (_archiveScanInProgress)
                return;

            _archiveScanInProgress = true;

            string normalized = path.Trim();
            Button button = sourceButton;
            if (button != null)
            {
                string loadingText = string.Equals(button.Name, "SelectArchiveBaseButton", StringComparison.OrdinalIgnoreCase)
                    ? "Attendi"
                    : "SCANSIONE IN CORSO...";
                StartButtonLoading(button, loadingText);
            }

            Task.Run(() =>
            {
                try
                {
                    LoadArchivePackagesFromPath(normalized);
                }
                finally
                {
                    ExecuteOnUiThread(() =>
                    {
                        _archiveScanInProgress = false;
                        if (button != null)
                        {
                            StopButtonLoading();
                        }
                    });
                }
            });
        }

        private void LoadArchivePackagesFromPath(string path)
        {
            _archivePackages.Clear();
            _packagesByBaseName.Clear();
            _selectedArchiveBaseNames.Clear();
            UpdateArchiveSelectionSummary(ArchiveSelectionInfo.Empty);
            ExecuteOnUiThread(() => UpdateArchiveSizeDisplay(ArchiveSelectionInfo.Empty));
            _lastArchiveScanPath = null;
            _lastArchiveScanError = null;
            _lastArchiveScanSamples.Clear();
            _lastArchiveScanTotalEntries = 0;
            _lastArchiveScanRecognizedEntries = 0;
            _lastArchiveScanUtc = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                ExecuteOnUiThread(UpdateArchiveBaseGroupOptions);
                return;
            }

            string normalizedPath = path.Trim();
            Debug.WriteLine($"[Archive] Scansione percorso: {normalizedPath}");
            _lastArchiveScanPath = normalizedPath;

            // Check veloce: se non esiste, mostra messaggio
            if (!Directory.Exists(normalizedPath))
            {
                Debug.WriteLine($"[Archive] Percorso non esiste: {normalizedPath}");
                ShowWarning("Percorso non valido", $"Il percorso \"{normalizedPath}\" non esiste o non è accessibile.");
                _lastArchiveScanError = $"Percorso non accessibile: {normalizedPath}";
                _lastArchiveScanUtc = DateTime.UtcNow;
                ExecuteOnUiThread(UpdateArchiveBaseGroupOptions);
                return;
            }

            _archiveRootPath = normalizedPath;

            int totalFound = 0;
            int recognized = 0;
            var unrecognizedItems = new List<string>();

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(normalizedPath))
                {
                    totalFound++;
                    string rawName = Path.GetFileName(directory);
                    string displayName = GetArchiveDisplayName(directory, rawName);

                    if (AddArchivePackageCandidate(directory, displayName, true))
                        recognized++;
                    else
                    {
                        unrecognizedItems.Add(displayName);
                        Debug.WriteLine($"[Archive] Pacchetto non riconosciuto: {displayName}");
                    }
                }

                foreach (var file in Directory.EnumerateFiles(normalizedPath))
                {
                    totalFound++;
                    string rawName = Path.GetFileName(file);

                    if (AddArchivePackageCandidate(file, rawName, false))
                        recognized++;
                    else
                    {
                        unrecognizedItems.Add(rawName);
                        Debug.WriteLine($"[Archive] File non riconosciuto: {rawName}");
                    }
                }

                Debug.WriteLine($"[Archive] Trovati {totalFound} elementi, riconosciuti {recognized} pacchetti in {normalizedPath}");

                if (totalFound > 0 && recognized == 0)
                {
                    Debug.WriteLine($"[Archive] Nessun pacchetto riconosciuto in {normalizedPath}. Trovati {totalFound} elementi ma nessuno conforme al formato atteso.");
                    foreach (var sample in unrecognizedItems.Take(5))
                    {
                        Debug.WriteLine($"[Archive] Esempio elemento non riconosciuto: {sample}");
                    }
                }

                _lastArchiveScanTotalEntries = totalFound;
                _lastArchiveScanRecognizedEntries = recognized;
                _lastArchiveScanSamples.Clear();
                if (unrecognizedItems.Count > 0)
                    _lastArchiveScanSamples.AddRange(unrecognizedItems.Take(3));
                _lastArchiveScanUtc = DateTime.UtcNow;
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"[Archive] Accesso negato a {normalizedPath}: {ex.Message}");
                ShowError("Accesso negato", $"Non hai i permessi per accedere a \"{normalizedPath}\".\nVerifica le credenziali di accesso.");
                _lastArchiveScanError = $"Accesso negato alla cartella: {ex.Message}";
                _lastArchiveScanUtc = DateTime.UtcNow;
                ExecuteOnUiThread(UpdateArchiveBaseGroupOptions);
                return;
            }
            catch (DirectoryNotFoundException ex)
            {
                Debug.WriteLine($"[Archive] Cartella non trovata: {normalizedPath}: {ex.Message}");
                ShowWarning("Cartella non trovata", $"La cartella \"{normalizedPath}\" non esiste più.");
                _lastArchiveScanError = $"Cartella non trovata: {ex.Message}";
                _lastArchiveScanUtc = DateTime.UtcNow;
                ExecuteOnUiThread(UpdateArchiveBaseGroupOptions);
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive] Errore durante la scansione di {normalizedPath}: {ex.Message}");
                ShowError("Errore scansione", $"Errore durante la scansione di \"{normalizedPath}\":\n{ex.Message}");
                _lastArchiveScanError = $"Errore scansione: {ex.Message}";
                _lastArchiveScanUtc = DateTime.UtcNow;
                ExecuteOnUiThread(UpdateArchiveBaseGroupOptions);
                return;
            }

            ExecuteOnUiThread(UpdateArchiveBaseGroupOptions);
        }

        private bool AddArchivePackageCandidate(string fullPath, string name, bool isDirectory)
        {
            if (!TryParseArchivePackageName(name, out string baseName, out DateTime timestamp))
                return false;

            var info = new ArchivePackageInfo
            {
                BaseName = baseName,
                Timestamp = timestamp,
                FullPath = fullPath,
                SizeBytes = 0,
                IsDirectory = isDirectory
            };

            _archivePackages.Add(info);
            if (!_packagesByBaseName.TryGetValue(baseName, out var list))
            {
                list = new List<ArchivePackageInfo>();
                _packagesByBaseName[baseName] = list;
            }
            list.Add(info);
            return true;
        }

        private bool TryParseArchivePackageName(string name, out string baseName, out DateTime timestamp)
        {
            baseName = null;
            timestamp = default;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            string trimmed = name.Trim();
            string normalized = NormalizeArchiveTimestampText(trimmed);

            if (TryExtractTimestampFromNormalizedMatches(normalized, trimmed, out baseName, out timestamp))
                return true;

            if (TryExtractTimestampFromTrailingSegment(trimmed, out baseName, out timestamp))
                return true;

            return false;
        }

        private string NormalizeArchiveTimestampText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // Normalizzazione leggera: rimuove spazi doppi e uniforma i separatori orari più comuni.
            string normalized = value.Replace("\\", " ").Replace("/", " ");
            normalized = normalized.Replace("  ", " ");
            return normalized.Trim();
        }

        private bool TryExtractTimestampFromNormalizedMatches(string normalizedValue, string originalValue, out string baseName, out DateTime timestamp)
        {
            baseName = null;
            timestamp = default;

            var matches = ArchiveTimestampPattern.Matches(normalizedValue);
            if (matches.Count == 0)
                return false;

            // Scorri dai match più a destra per catturare l'ultimo timestamp nel nome
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                string timestampValue = normalizedValue.Substring(match.Index, match.Length);
                if (!TryParseArchiveTimestamp(timestampValue, out timestamp))
                    continue;

                if (TryExtractBaseName(originalValue, match.Index, out baseName))
                    return true;
            }

            return false;
        }

        private bool TryExtractTimestampFromTrailingSegment(string originalValue, out string baseName, out DateTime timestamp)
        {
            baseName = null;
            timestamp = default;

            if (string.IsNullOrWhiteSpace(originalValue))
                return false;

            string trimmed = originalValue.Trim();
            int tailLength = Math.Min(ArchiveTimestampTailScanLength, trimmed.Length);
            string tail = trimmed.Substring(trimmed.Length - tailLength);
            int tailOffset = trimmed.Length - tailLength;

            for (int i = 0; i < tail.Length; i++)
            {
                if (!char.IsDigit(tail[i]))
                    continue;

                string candidate = tail.Substring(i).Trim();
                if (candidate.Length < 6)
                    continue;

                candidate = CleanArchiveTimestampCandidate(candidate);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (!TryParseArchiveTimestamp(candidate, out timestamp))
                    continue;

                int baseEndIndex = Math.Max(0, tailOffset + i);
                if (baseEndIndex <= 0)
                    continue;

                if (TryExtractBaseName(trimmed, baseEndIndex, out baseName))
                    return true;
            }

            return false;
        }

        private static string CleanArchiveTimestampCandidate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            string cleaned = value.Trim();

            foreach (var suffix in ArchiveTimestampSuffixes)
            {
                if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - suffix.Length);
                    break;
                }
            }

            cleaned = cleaned.TrimEnd(ArchiveTimestampTrailingTrimChars).Trim();

            while (!string.IsNullOrEmpty(cleaned) && Array.IndexOf(ArchiveTimestampLeadingTrimChars, cleaned[0]) >= 0)
            {
                cleaned = cleaned.Substring(1).TrimStart();
            }

            return cleaned;
        }

        private bool TryParseArchiveTimestamp(string value, out DateTime timestamp)
        {
            timestamp = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();

            // Prova i formati espliciti definiti in ArchiveTimestampFormats
            if (DateTime.TryParseExact(trimmed, ArchiveTimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp))
            {
                return true;
            }

            // Tentativo di canonicalizzazione (es. rimuovere separatori strani) usando la regex dedicata
            var match = ArchiveTimestampCanonicalizerRegex.Match(trimmed);
            if (match.Success)
            {
                string datePart = match.Groups["date"].Value;
                string hour = match.Groups["hour"].Value;
                string minute = match.Groups["minute"].Value;
                string second = match.Groups["second"].Value;
                string zone = match.Groups["zone"].Value;

                string canonical = $"{datePart}T{hour}{minute}{second}{zone}";
                if (DateTime.TryParseExact(canonical, ArchiveTimestampFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out timestamp))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryExtractBaseName(string originalValue, int timestampIndex, out string baseName)
        {
            baseName = null;
            if (string.IsNullOrWhiteSpace(originalValue) || timestampIndex <= 0)
                return false;

            int safeIndex = Math.Min(timestampIndex, originalValue.Length);
            if (safeIndex <= 0)
                return false;

            string beforeTimestamp = originalValue.Substring(0, safeIndex).TrimEnd();
            baseName = beforeTimestamp.TrimEnd(ArchiveBaseTrimChars);
            return !string.IsNullOrWhiteSpace(baseName);
        }

        private string GetArchiveDisplayName(string fullPath, string fallbackName)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return fallbackName;

            try
            {
                if (!Directory.Exists(fullPath))
                    return fallbackName;

                string desktopIniPath = Path.Combine(fullPath, "desktop.ini");
                if (!File.Exists(desktopIniPath))
                    return fallbackName;

                using (var stream = new FileStream(desktopIniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    bool inShellSection = false;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";", StringComparison.Ordinal))
                            continue;

                        if (trimmed.StartsWith("[", StringComparison.Ordinal))
                        {
                            inShellSection = trimmed.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                            continue;
                        }

                        if (!inShellSection)
                            continue;

                        const string key = "LocalizedResourceName=";
                        if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                        {
                            string value = trimmed.Substring(key.Length).Trim();
                            if (string.IsNullOrEmpty(value))
                                break;

                            if (value[0] == '"' && value[value.Length - 1] == '"' && value.Length > 1)
                                value = value.Substring(1, value.Length - 2);

                            if (value.StartsWith("@", StringComparison.Ordinal))
                                break;

                            return value;
                        }
                    }
                }
            }
            catch
            {
                // Se qualcosa va storto, torna semplicemente al nome di fallback
            }

            return fallbackName;
        }

        private void UpdateArchiveBaseGroupOptions()
        {
            // Ordina i pacchetti per timestamp all'interno di ogni gruppo
            foreach (var kvp in _packagesByBaseName)
            {
                kvp.Value.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            }

            // Ricostruisci l'elenco dei gruppi disponibili
            _availableArchiveBaseNames.Clear();
            foreach (var kvp in _packagesByBaseName
                         .OrderByDescending(k => k.Value.Count)
                         .ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                _availableArchiveBaseNames.Add(new ArchiveBaseNameOption
                {
                    BaseName = kvp.Key,
                    Display = $"{kvp.Key} ({kvp.Value.Count} pacchetti)"
                });
            }

            // Rimuovi selezioni non più presenti
            var availableNames = new HashSet<string>(_availableArchiveBaseNames.Select(opt => opt.BaseName), StringComparer.OrdinalIgnoreCase);
            _selectedArchiveBaseNames.RemoveWhere(name => !availableNames.Contains(name));

            // Se non c'è nessuna selezione ma ci sono gruppi, seleziona il primo
            if (_selectedArchiveBaseNames.Count == 0 && _availableArchiveBaseNames.Count > 0)
            {
                _selectedArchiveBaseNames.Add(_availableArchiveBaseNames[0].BaseName);
            }

            if (_availableArchiveBaseNames.Count == 0)
            {
                UpdateArchiveBaseNameText();
                UpdateArchiveSelectionSummary(ArchiveSelectionInfo.Empty);
                UpdateArchiveSizeDisplay(ArchiveSelectionInfo.Empty);
                return;
            }

            UpdateArchiveBaseNameText();
            RecalculateArchiveSelection();
        }

        private void ArchiveBaseNameButton_Click(object sender, EventArgs e)
        {
            if (_archiveScanInProgress)
                return; // Durante la scansione ignoriamo il click, lo stato è già visibile su "Sfoglia"

            var button = sender as Button;

            RunWithButtonLoading(button, "Attendi", () =>
            {
                // Se il percorso è cambiato rispetto all'ultima scansione, ricarica i pacchetti
                if (_archiveRootTextBox != null)
                {
                    var path = GetRealValue(_archiveRootTextBox);
                    if (!string.IsNullOrWhiteSpace(path) && !string.Equals(path.Trim(), _archiveRootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        TryLoadArchivePackagesFromTextBox(button);
                    }
                }

                if (_availableArchiveBaseNames == null || _availableArchiveBaseNames.Count == 0)
                {
                    StopButtonLoading();
                    ShowInfo("Gruppi pacchetti", "Nessun pacchetto disponibile. Seleziona prima un percorso valido.");
                    return;
                }

                using (var selector = new ArchiveBaseNameSelectionForm(this, _availableArchiveBaseNames, _selectedArchiveBaseNames))
                {
                    if (selector.ShowDialog(this) == DialogResult.OK)
                    {
                        var selected = selector.GetSelectedBaseNames();
                        _selectedArchiveBaseNames.Clear();
                        foreach (var name in selected)
                            _selectedArchiveBaseNames.Add(name);

                        UpdateArchiveBaseNameText();
                        RecalculateArchiveSelection();
                    }
                }
            });
        }

        private void ArchiveDateTextChanged(object sender, EventArgs e)
        {
            RecalculateArchiveSelection();
        }

        private void UpdateArchiveBaseNameText()
        {
            if (_archiveBaseNamesTextBox == null)
                return;

            var selectedOptions = GetSelectedBaseNameOptions().ToList();
            if (selectedOptions.Count == 0)
            {
                _archiveBaseNamesTextBox.Text = "Seleziona uno o più gruppi pacchetti...";
                _archiveBaseNamesTextBox.ForeColor = Colors.TextPlaceholder;
            }
            else
            {
                var names = string.Join(", ",
                    selectedOptions
                        .Select(opt => opt.BaseName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase));

                _archiveBaseNamesTextBox.Text = names;
                _archiveBaseNamesTextBox.ForeColor = Colors.TextPrimary;
            }
        }

        private void UpdateArchiveSizeDisplay(ArchiveSelectionInfo selection)
        {
            if (_archiveSizeLabel == null)
                return;

            if (!selection.HasPackages)
            {
                _archiveSizeLabel.Text = "Dimensione stimata: —";
                _archiveSizeLabel.ForeColor = Colors.TextSecondary;
                return;
            }

            string sizeText = FormatSize(selection.TotalBytes);
            _archiveSizeLabel.Text = $"Dimensione stimata: {sizeText} ({selection.PackageCount} pacchetti)";
            _archiveSizeLabel.ForeColor = Colors.Success;
        }

        private IEnumerable<ArchiveBaseNameOption> GetSelectedBaseNameOptions()
        {
            if (_availableArchiveBaseNames == null || _availableArchiveBaseNames.Count == 0 || _selectedArchiveBaseNames.Count == 0)
                return Enumerable.Empty<ArchiveBaseNameOption>();

            return _availableArchiveBaseNames
                .Where(opt => opt != null && _selectedArchiveBaseNames.Contains(opt.BaseName));
        }

        private DateTime? ParseArchiveDate(TextBox textBox)
        {
            if (textBox == null)
                return null;

            string raw = GetRealValue(textBox);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = NormalizeTimeSeparators(raw.Trim());

            var formats = new[]
            {
                "dd/MM/yyyy HH:mm",
                "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yy HH:mm",
                "dd/MM/yy HH:mm:ss"
            };

            if (DateTime.TryParseExact(raw, formats, CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var value))
            {
                return value;
            }

            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out value))
            {
                return value;
            }

            return null;
        }

        private long GetArchivePackageSize(ArchivePackageInfo package)
        {
            if (package == null)
                return 0;

            if (package.SizeBytes > 0)
                return package.SizeBytes;

            long sizeBytes;

            // Per evitare blocchi della UI, non calcoliamo più ricorsivamente la dimensione
            // delle cartelle archivio. Stimiamo solo il peso dei file singoli e lasciamo 0 per le directory.
            if (package.IsDirectory)
            {
                sizeBytes = 0;
            }
            else
            {
                sizeBytes = CalculateFileSizeSafe(package.FullPath);
            }

            if (sizeBytes < 0)
                sizeBytes = 0;

            package.SizeBytes = sizeBytes;
            return sizeBytes;
        }

        private void UpdateServerIntervalsSummaryLabel()
        {
            // Mostra il riepilogo (nome file + conteggio) direttamente dentro il rettangolo server
            if (_serverIntervalsDropLabel == null)
                return;

            int count = _serverIntervals?.Count ?? 0;
            if (count <= 0 || string.IsNullOrEmpty(_serverIntervalsFileName))
            {
                _serverIntervalsDropLabel.Text = "Rilascia o clicca qui per inserire più intervalli";
                _serverIntervalsDropLabel.ForeColor = Colors.TextSecondary;
                // Testo sempre centrato orizzontalmente e verticalmente
                _serverIntervalsDropLabel.TextAlign = ContentAlignment.MiddleCenter;
                return;
            }

            string baseName = _serverIntervalsFileName;
            string suffix = count == 1 ? " (1 intervallo)" : $" ({count} intervalli)";

            _serverIntervalsDropLabel.Text = baseName + suffix;
            _serverIntervalsDropLabel.ForeColor = Colors.Success;
            // Anche quando ci sono intervalli caricati, mantieni il testo centrato nel rettangolo
            _serverIntervalsDropLabel.TextAlign = ContentAlignment.MiddleCenter;
        }

        private void UpdateArchiveIntervalsSummaryLabel()
        {
            // Mostra il riepilogo (nome file + conteggio) direttamente dentro il rettangolo archivio
            if (_archiveIntervalsDropLabel == null)
                return;

            int count = _archiveIntervals?.Count ?? 0;
            if (count <= 0 || string.IsNullOrEmpty(_archiveIntervalsFileName))
            {
                _archiveIntervalsDropLabel.Text = "Rilascia o clicca qui per inserire più intervalli";
                _archiveIntervalsDropLabel.ForeColor = Colors.TextSecondary;
                // Testo sempre centrato orizzontalmente e verticalmente
                _archiveIntervalsDropLabel.TextAlign = ContentAlignment.MiddleCenter;
                return;
            }

            string baseName = _archiveIntervalsFileName;
            string suffix = count == 1 ? " (1 intervallo)" : $" ({count} intervalli)";

            _archiveIntervalsDropLabel.Text = baseName + suffix;
            _archiveIntervalsDropLabel.ForeColor = Colors.Success;
            // Anche quando ci sono intervalli caricati, mantieni il testo centrato nel rettangolo
            _archiveIntervalsDropLabel.TextAlign = ContentAlignment.MiddleCenter;
        }

        private void RecalculateArchiveSelection()
        {
            var selectedOptions = GetSelectedBaseNameOptions().ToList();
            if (selectedOptions.Count == 0)
            {
                UpdateArchiveSelectionSummary(ArchiveSelectionInfo.Empty);
                UpdateArchiveSizeDisplay(ArchiveSelectionInfo.Empty);
                return;
            }

            DateTime? start = ParseArchiveDate(_archiveStartTextBox);
            DateTime? end = ParseArchiveDate(_archiveEndTextBox);

            if (start.HasValue && end.HasValue && start > end)
            {
                UpdateArchiveSelectionSummary(ArchiveSelectionInfo.Empty);
                UpdateArchiveSizeDisplay(ArchiveSelectionInfo.Empty);
                return;
            }

            var aggregatedPackages = new List<ArchivePackageInfo>();
            foreach (var option in selectedOptions)
            {
                var selection = BuildArchiveSelection(option.BaseName, start, end);
                if (selection.HasPackages)
                    aggregatedPackages.AddRange(selection.Packages);
            }

            ArchiveSelectionInfo combinedSelection;
            if (aggregatedPackages.Count == 0)
            {
                combinedSelection = ArchiveSelectionInfo.Empty;
            }
            else
            {
                var ordered = aggregatedPackages.OrderBy(p => p.Timestamp).ToList();
                long totalBytes = ordered.Sum(p => GetArchivePackageSize(p));
                combinedSelection = new ArchiveSelectionInfo(totalBytes, ordered.Count, ordered);
            }

            UpdateArchiveSelectionSummary(combinedSelection);
            UpdateArchiveSizeDisplay(combinedSelection);

            StartArchiveSizeRefinement(combinedSelection);
        }

        private void StartArchiveSizeRefinement(ArchiveSelectionInfo selection)
        {
            if (!selection.HasPackages)
                return;

            int currentVersion = ++_archiveSizeCalculationVersion;

            ExecuteOnUiThread(() =>
            {
                if (_archiveSizeLabel != null)
                {
                    _archiveSizeLabel.Text = "Dimensione stimata: calcolo in corso...";
                    _archiveSizeLabel.ForeColor = Colors.TextSecondary;
                }
            });

            var packages = selection.Packages;

            Task.Run(() =>
            {
                try
                {
                    long totalBytes = 0;

                    if (packages != null)
                    {
                        foreach (var pkg in packages)
                        {
                            if (currentVersion != _archiveSizeCalculationVersion)
                                return; // selezione cambiata, annulla

                            if (pkg == null)
                                continue;

                            long size = pkg.SizeBytes;
                            if (size <= 0)
                            {
                                if (pkg.IsDirectory)
                                {
                                    size = CalculateDirectorySizeSafe(pkg.FullPath);
                                }
                                else
                                {
                                    size = CalculateFileSizeSafe(pkg.FullPath);
                                }

                                if (size < 0)
                                    size = 0;

                                pkg.SizeBytes = size;
                            }

                            totalBytes += size;
                        }
                    }

                    if (currentVersion != _archiveSizeCalculationVersion)
                        return;

                    var refined = new ArchiveSelectionInfo(totalBytes, selection.PackageCount, selection.Packages);

                    ExecuteOnUiThread(() =>
                    {
                        UpdateArchiveSelectionSummary(refined);
                        UpdateArchiveSizeDisplay(refined);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Archive] Errore durante il calcolo dimensioni archivio: {ex.Message}");
                }
            });
        }

        private ArchiveSelectionInfo BuildArchiveSelection(string baseName, DateTime? start, DateTime? end)
        {
            if (!_packagesByBaseName.TryGetValue(baseName, out var packages) || packages.Count == 0)
            {
                return ArchiveSelectionInfo.Empty;
            }

            var ordered = packages.OrderBy(p => p.Timestamp).ToList();
            DateTime? effectiveStart = start?.AddMinutes(-1);
            DateTime? effectiveEnd = end?.AddMinutes(1);

            var filtered = ordered
                .Where(p => (!effectiveStart.HasValue || p.Timestamp >= effectiveStart.Value) &&
                            (!effectiveEnd.HasValue || p.Timestamp <= effectiveEnd.Value))
                .ToList();

            if (filtered.Count == 0 && (start.HasValue || end.HasValue))
            {
                if (start.HasValue)
                {
                    var candidate = ordered.FirstOrDefault(p => p.Timestamp >= start.Value) ?? ordered.Last();
                    if (candidate != null)
                    {
                        var idx = ordered.IndexOf(candidate);
                        if (idx > 0)
                            filtered.Add(ordered[idx - 1]);
                        filtered.Add(candidate);
                    }
                }

                if (end.HasValue)
                {
                    var candidate = ordered.LastOrDefault(p => p.Timestamp <= end.Value) ?? ordered.First();
                    if (candidate != null)
                    {
                        if (!filtered.Contains(candidate))
                            filtered.Add(candidate);
                        var idx = ordered.IndexOf(candidate);
                        if (idx < ordered.Count - 1)
                            filtered.Add(ordered[idx + 1]);
                    }
                }
            }
            else
            {
                if (start.HasValue && filtered.Count > 0)
                {
                    var firstIndex = ordered.IndexOf(filtered.First());
                    if (firstIndex > 0)
                        filtered.Insert(0, ordered[firstIndex - 1]);
                }

                if (end.HasValue && filtered.Count > 0)
                {
                    var lastIndex = ordered.IndexOf(filtered.Last());
                    if (lastIndex < ordered.Count - 1)
                        filtered.Add(ordered[lastIndex + 1]);
                }
            }

            if (filtered.Count == 0)
            {
                filtered = ordered;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<ArchivePackageInfo>();
            foreach (var pkg in filtered.OrderBy(p => p.Timestamp).ThenBy(p => p.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(pkg.FullPath))
                {
                    unique.Add(pkg);
                }
            }

            long totalBytes = unique.Sum(p => GetArchivePackageSize(p));
            return new ArchiveSelectionInfo(totalBytes, unique.Count, unique);
        }

        private void UpdateArchiveSelectionSummary(ArchiveSelectionInfo selection)
        {
            _currentArchiveSelection = selection;
        }

        private static long CalculateDirectorySizeSafe(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return 0;

                long total = 0;
                var pending = new Stack<DirectoryInfo>();
                pending.Push(new DirectoryInfo(path));

                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    FileInfo[] files = null;
                    try
                    {
                        files = current.GetFiles();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var file in files)
                    {
                        total += file.Length;
                    }

                    DirectoryInfo[] subdirs = null;
                    try
                    {
                        subdirs = current.GetDirectories();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var dir in subdirs)
                    {
                        pending.Push(dir);
                    }
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }

        private static long CalculateFileSizeSafe(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private int EstimateDiskCount(long totalBytes)
        {
            if (totalBytes <= 0)
                return 0;

            return (int)Math.Max(1, Math.Ceiling(totalBytes / (double)DefaultDiskSizeBytes));
        }

        private void RenderSorgenteContent()
        {
            if (_sorgenteHost == null)
                return;

            var buttons = new List<Button>();
            Panel content = _currentLaunchMode == LaunchMode.Server
                ? CreateServerSourcePanel(buttons)
                : CreateArchiveSourcePanel(buttons);

            content.Dock = DockStyle.Fill;
            content.Margin = Padding.Empty;
            _sorgenteHost.Controls.Clear();
            _sorgenteHost.Controls.Add(content);
            RegisterSorgenteButtons(buttons);

            NormalizeButtonWidths(_lancioActionButtons.ToArray());
        }

        private void RegisterSorgenteButtons(IEnumerable<Button> buttons)
        {
            if (_sorgenteActionButtons.Count > 0)
            {
                foreach (var btn in _sorgenteActionButtons)
                {
                    _lancioActionButtons.Remove(btn);
                }
                _sorgenteActionButtons.Clear();
            }

            if (buttons == null)
                return;

            foreach (var btn in buttons)
            {
                if (btn == null)
                    continue;
                _sorgenteActionButtons.Add(btn);
                if (!_lancioActionButtons.Contains(btn))
                {
                    _lancioActionButtons.Add(btn);
                }
            }
        }

        /// <summary>
        /// Indica la disponibilita di active exports.
        /// </summary>
        private bool HasActiveExports()
        {
            lock (_exportLock)
            {
                return _activeExports.Any(job => job != null && !job.IsCanceled && !IsFinalStatus(job.Status));
            }
        }

        /// <summary>
        /// Applica launch connection state alle impostazioni correnti.
        /// </summary>
        private void ApplyLaunchConnectionState()
        {
            if (contentPanel == null)
                return;

            bool hasRunningExports = HasActiveExports();

            var serverBox = contentPanel.Controls.Find("ServerAddressTextBox", true).FirstOrDefault() as TextBox;
            var selectCameraBtn = contentPanel.Controls.Find("SelectCameraButton", true).FirstOrDefault() as Button;
            var connectBtn = contentPanel.Controls.Find("ConnectButton", true).FirstOrDefault() as Button;

            if (_isServerConnected)
            {
                if (serverBox != null && !string.IsNullOrWhiteSpace(_currentServerAddress))
                {
                    var normalized = NormalizeServerAddress(_currentServerAddress);
                    serverBox.Text = normalized;
                    serverBox.ForeColor = Colors.TextPrimary;
                }

                if (selectCameraBtn != null)
                {
                    selectCameraBtn.Enabled = true;
                    selectCameraBtn.ForeColor = Colors.Success;
                }

                UpdateSelectedCamerasText();
            }
            else
            {
                if (selectCameraBtn != null)
                {
                    selectCameraBtn.Enabled = false;
                }
            }

            if (connectBtn != null)
            {
                bool shouldEnableConnect = !hasRunningExports;
                if (connectBtn.Enabled != shouldEnableConnect)
                {
                    connectBtn.Enabled = shouldEnableConnect;
                }

                if (shouldEnableConnect && _isServerConnected)
                {
                    connectBtn.ForeColor = Colors.Success;
                }

                if (_toolTip != null)
                {
                    if (!shouldEnableConnect)
                    {
                        _toolTip.SetToolTip(connectBtn, "Attendi il termine delle archiviazioni in corso per cambiare server.");
                    }
                    else
                    {
                        _toolTip.SetToolTip(connectBtn, "Connetti al server VideoOS");
                    }
                }
            }
        }
        /// <summary>
        /// Crea la sezione SORGENTE per la modalità server.
        /// </summary>
        private Panel CreateServerSourcePanel(List<Button> launchButtons)
        {
            var panel = CreateAccentPanel(Colors.Success, $"{Icons.Camera} SORGENTE");
            
            // Layout interno con TableLayoutPanel per allineamento perfetto
            var sectionPadding = GetUniformSectionPadding();
            int sorgenteContentGap = sectionPadding.Top;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 12,  // Aumentato: header intervalli + dropzone sono righe separate
                ColumnCount = 5,
                Padding = new Padding(sectionPadding.Left, 28 + sorgenteContentGap, sectionPadding.Right, sectionPadding.Bottom),
                BackColor = Color.Transparent
            };
            
            // Colonne: label | campo | bottone | gap | pin
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            
            int row = 0;
            
            // INDIRIZZO SERVER
            var serverLabel = new Label
            {
                Text = $"{Icons.Server} Indirizzo Server",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(serverLabel, layout.ColumnCount);
            layout.Controls.Add(serverLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);
            
            var serverTextBox = CreateModernTextBox("Inserisci indirizzo server...");
            serverTextBox.Name = "ServerAddressTextBox";
            serverTextBox.Dock = DockStyle.Fill;
            serverTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            serverTextBox.Leave += (s, e) =>
            {
                var normalized = NormalizeServerAddress(serverTextBox.Text);
                if (!string.Equals(normalized, serverTextBox.Text, StringComparison.OrdinalIgnoreCase))
                {
                    serverTextBox.Text = normalized;
                    serverTextBox.ForeColor = Colors.TextPrimary;
                }
            };
            layout.Controls.Add(serverTextBox, 0, row);
            layout.Controls.Add(CreateSpacerCell(), 1, row);
            EnsureRowStyle(layout, row, Sizes.InputHeight);
            
            // Pulsante di connessione con testo esplicito
            var connectBtn = CreateModernButton($"{Icons.Server} Connetti", Colors.Success);
            connectBtn.Dock = DockStyle.None;
            connectBtn.AutoSize = true;
            connectBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            connectBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            int buttonVerticalOffset = -Spacing.XXS;
            connectBtn.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            connectBtn.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            connectBtn.Name = "ConnectButton";
            connectBtn.Click += ConnectButton_Click;
            layout.Controls.Add(connectBtn, 2, row);
            layout.Controls.Add(CreateSpacerCell(), 3, row);
            EnsureRowStyle(layout, row, Sizes.InputHeight);
            launchButtons?.Add(connectBtn);

            var pinServerBtn = CreateIconSquareButton(Icons.Pin, Colors.Success, PinServerButton_Click, "PinServerButton");
            pinServerBtn.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            layout.Controls.Add(pinServerBtn, 4, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight);

            var pinnedServer = LoadUserSetting("PinnedServerAddress");
            if (!string.IsNullOrWhiteSpace(pinnedServer))
            {
                var normalized = NormalizeServerAddress(pinnedServer);
                serverTextBox.Text = normalized;
                serverTextBox.ForeColor = Colors.TextPrimary;
                _currentServerAddress = normalized;
            }
            else
            {
                var normalized = NormalizeServerAddress("localhost");
                serverTextBox.Text = normalized;
                serverTextBox.ForeColor = Colors.TextPrimary;
                _currentServerAddress = normalized;
            }
            
            // TELECAMERA
            var telecameraLabel = new Label
            {
                Text = $"{Icons.Camera} Telecamera",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(telecameraLabel, layout.ColumnCount);
            layout.Controls.Add(telecameraLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);
            
            // Usa un TextBox read-only per mostrare il nome della telecamera selezionata
            var telecameraRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            telecameraRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            telecameraRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            telecameraRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var telecameraTextBox = CreateModernTextBox("Seleziona una o più telecamere...");
            telecameraTextBox.Name = "TelecameraTextBox";
            telecameraTextBox.ReadOnly = false;
            telecameraTextBox.Enabled = true;
            telecameraTextBox.Dock = DockStyle.Fill;
            telecameraTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            telecameraRow.Controls.Add(telecameraTextBox, 0, 0);

            telecameraRow.Controls.Add(CreateSpacerCell(), 1, 0);

            var telecameraBtn = CreateModernButton($"{Icons.Camera} Seleziona", Colors.TextMuted);  // Grigio di default
            telecameraBtn.Name = "SelectCameraButton";
            telecameraBtn.Dock = DockStyle.None;
            telecameraBtn.AutoSize = true;
            telecameraBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            telecameraBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            telecameraBtn.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            telecameraBtn.Enabled = false;  // Disabilitato finché non connesso
            telecameraBtn.ForeColor = Colors.TextMuted;  // Grigio quando disabilitato
            telecameraBtn.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            telecameraBtn.Click += TelecameraButton_Click;
            telecameraRow.Controls.Add(telecameraBtn, 2, 0);

            layout.SetColumnSpan(telecameraRow, layout.ColumnCount);
            layout.Controls.Add(telecameraRow, 0, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight);
            launchButtons?.Add(telecameraBtn);
            
            // LABEL DATE/ORA (unica per entrambe) - Spazio corretto
            var dateLabel = new Label
            {
                Text = $"{Icons.Calendar} Intervallo data/ora",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,  // Allineamento standard
                Padding = new Padding(0, 0, 0, 0),  // Nessun padding extra
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(dateLabel, layout.ColumnCount);
            layout.Controls.Add(dateLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);
            
            // Pannello contenitore per le date
            var datePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            
            // Layout con label e textbox
            var dateLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                AutoSize = false
            };

            const float dateTextPercent = 0.38f; // percentuale per ciascun textbox, tot 76%
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55)); // Label Inizio
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dateTextPercent * 100));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sizes.InputHeight));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50)); // Label Fine
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dateTextPercent * 100));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            dateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sizes.InputHeight));

            dateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Sizes.InputHeight));

            // Label Inizio
            var inizioLabel = new Label
            {
                Text = "Inizio:",
                Font = GetCachedFont(Fonts.Primary, 9f),
                ForeColor = Colors.TextMuted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopRight,
                Padding = new Padding(0, Spacing.XXS, 5, 0),
                AutoSize = false,
                Margin = new Padding(0)
            };
            dateLayout.Controls.Add(inizioLabel, 0, 0);

            // TextBox per Data Inizio
            var dataInizioTextBox = CreateModernTextBox("__/__/____ __:__");
            dataInizioTextBox.Name = "InizioTextBox";
            dataInizioTextBox.Dock = DockStyle.Fill;
            dataInizioTextBox.Font = GetCachedFont(Fonts.Primary, 10f);
            dataInizioTextBox.BackColor = Color.FromArgb(35, 35, 35);
            dataInizioTextBox.ForeColor = Colors.TextPrimary;
            dataInizioTextBox.BorderStyle = BorderStyle.FixedSingle;
            dataInizioTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            dataInizioTextBox.MaxLength = 16;
            AttachDateTimeNormalization(dataInizioTextBox);
            dateLayout.Controls.Add(dataInizioTextBox, 1, 0);

            // Gap coerente
            dateLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 2, 0);

            // Bottone calendario per Inizio
            var calInizioBtn = new Button
            {
                Text = "📅",
                Name = "CalInizioButton",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Colors.Success,
                Font = GetCachedFont(Fonts.Primary, 9f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 0),
                MinimumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                MaximumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                TextAlign = ContentAlignment.MiddleCenter
            };
            calInizioBtn.FlatAppearance.BorderSize = 1;
            calInizioBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);
            calInizioBtn.MouseEnter += (s, e) =>
            {
                calInizioBtn.BackColor = Color.FromArgb(40, 40, 40);
                calInizioBtn.ForeColor = ControlPaint.Light(Colors.Success, 0.3f);
            };
            calInizioBtn.MouseLeave += (s, e) =>
            {
                calInizioBtn.BackColor = Color.FromArgb(28, 28, 28);
                calInizioBtn.ForeColor = Colors.Success;
            };
            calInizioBtn.Click += (s, e) => ShowDatePicker(dataInizioTextBox, true);
            dateLayout.Controls.Add(calInizioBtn, 3, 0);

            // Label Fine
            var fineLabel = new Label
            {
                Text = "Fine:",
                Font = GetCachedFont(Fonts.Primary, 9f),
                ForeColor = Colors.TextMuted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopRight,
                Padding = new Padding(0, Spacing.XXS, 5, 0),
                AutoSize = false,
                Margin = new Padding(0)
            };
            dateLayout.Controls.Add(fineLabel, 4, 0);

            // TextBox per Data Fine
            var dataFineTextBox = CreateModernTextBox("__/__/____ __:__");
            dataFineTextBox.Name = "FineTextBox";
            dataFineTextBox.Dock = DockStyle.Fill;
            dataFineTextBox.Font = GetCachedFont(Fonts.Primary, 10f);
            dataFineTextBox.BackColor = Color.FromArgb(35, 35, 35);
            dataFineTextBox.ForeColor = Colors.TextPrimary;
            dataFineTextBox.BorderStyle = BorderStyle.FixedSingle;
            dataFineTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            dataFineTextBox.MaxLength = 16;
            AttachDateTimeNormalization(dataFineTextBox);
            dateLayout.Controls.Add(dataFineTextBox, 5, 0);

            // Gap coerente
            dateLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 6, 0);

            // Bottone calendario per Fine
            var calFineBtn = new Button
            {
                Text = "📅",
                Name = "CalFineButton",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Colors.Success,
                Font = GetCachedFont(Fonts.Primary, 9f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 0),
                MinimumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                MaximumSize = new Size(Sizes.InputHeight, Sizes.InputHeight),
                TextAlign = ContentAlignment.MiddleCenter
            };
            calFineBtn.FlatAppearance.BorderSize = 1;
            calFineBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);
            calFineBtn.MouseEnter += (s, e) =>
            {
                calFineBtn.BackColor = Color.FromArgb(40, 40, 40);
                calFineBtn.ForeColor = ControlPaint.Light(Colors.Success, 0.3f);
            };
            calFineBtn.MouseLeave += (s, e) =>
            {
                calFineBtn.BackColor = Color.FromArgb(28, 28, 28);
                calFineBtn.ForeColor = Colors.Success;
            };
            calFineBtn.Click += (s, e) => ShowDatePicker(dataFineTextBox, false);
            dateLayout.Controls.Add(calFineBtn, 7, 0);

            // Validazione su cambio testo
            dataInizioTextBox.TextChanged += (s, e) => ValidateDateRange();
            dataFineTextBox.TextChanged += (s, e) => ValidateDateRange();
            
            datePanel.Controls.Add(dateLayout);
            layout.SetColumnSpan(datePanel, layout.ColumnCount);
            layout.Controls.Add(datePanel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight + Spacing.XXS);

            // === DROPZONE INTERVALLI TXT (riga separata, senza testo sopra/sotto) ===
            var serverIntervalsDropZone = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 24, 24), // leggermente più scuro
                Margin = new Padding(0, 0, Spacing.XS, 0),
                // Solo padding orizzontale per centrare meglio verticalmente il testo
                Padding = new Padding(Spacing.SM, 0, Spacing.SM, 0),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                MinimumSize = new Size(0, Sizes.ButtonHeight),
                Height = Sizes.ButtonHeight  // Same as archive
            };

            _serverIntervalsDropLabel = new Label
            {
                Text = "Rilascia o clicca qui per inserire più intervalli",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS),
                ForeColor = Colors.TextSecondary,
                Cursor = Cursors.Hand
            };

            // Click per aprire il file TXT
            _serverIntervalsDropLabel.Click += (s, e) => ServerBrowseIntervalsButton_Click(s, e);
            
            serverIntervalsDropZone.Controls.Add(_serverIntervalsDropLabel);
            serverIntervalsDropZone.Click += (s, e) => ServerBrowseIntervalsButton_Click(s, e);
            
            // Setup drag&drop
            serverIntervalsDropZone.AllowDrop = true;
            serverIntervalsDropZone.DragEnter += (s, e) => {
                System.Diagnostics.Debug.WriteLine("[SERVER] DragEnter triggered");
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                    serverIntervalsDropZone.BackColor = Color.FromArgb(40, 40, 40); // Visual feedback
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            };
            serverIntervalsDropZone.DragLeave += (s, e) => {
                serverIntervalsDropZone.BackColor = Color.FromArgb(24, 24, 24); // Reset color
            };
            serverIntervalsDropZone.DragDrop += (s, e) => {
                System.Diagnostics.Debug.WriteLine("[SERVER] DragDrop triggered");
                serverIntervalsDropZone.BackColor = Color.FromArgb(24, 24, 24); // Reset color
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var txtFile = files.FirstOrDefault(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) ?? files[0];
                    System.Diagnostics.Debug.WriteLine($"[SERVER] Loading file: {txtFile}");
                    TryLoadIntervalsFromFile(txtFile, isServer: true);
                }
            };

            // Memorizza la dropzone server per l'hit-test nel drag&drop a livello di form
            _serverIntervalsDropZone = serverIntervalsDropZone;

            // Riga con rettangolo + pulsanti CONTROLLA / CANCELLA
            var serverIntervalsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, Spacing.XS, 0, 0),
                Padding = new Padding(0)
            };
            serverIntervalsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            serverIntervalsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            serverIntervalsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            serverIntervalsRow.Controls.Add(serverIntervalsDropZone, 0, 0);

            var serverCheckButton = CreateModernButton($"{Icons.Check} Controlla", Colors.Secondary);
            serverCheckButton.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            serverCheckButton.Margin = new Padding(0, 0, 0, 0);
            serverCheckButton.AutoSize = true;
            serverCheckButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            serverCheckButton.Click += (s, e) => ShowIntervalsPreview(isServer: true);
            serverIntervalsRow.Controls.Add(serverCheckButton, 1, 0);

            // Pulsante quadrato solo icona (X) per cancellare gli intervalli server
            var serverClearButton = CreateIconSquareButton(Icons.Cross, Colors.Error, (s, e) => ClearIntervals(isServer: true), "ServerIntervalsClearButton");
            serverClearButton.Margin = new Padding(Spacing.XS, 0, 0, 0);
            serverIntervalsRow.Controls.Add(serverClearButton, 2, 0);

            // Aggiungi la riga completa (rettangolo + pulsanti) al layout principale
            layout.SetColumnSpan(serverIntervalsRow, layout.ColumnCount);
            layout.Controls.Add(serverIntervalsRow, 0, row);
            EnsureRowStyle(layout, row++, Sizes.ButtonHeight + Spacing.XS); // stessa altezza della dropzone archivio

            // Radio "per intervallo" lato server: stessi margini/altezza di quello "da archivio"
            _serverPerIntervalRadio = CreateModernRadioButton("Crea un'archiviazione separata per ogni intervallo");
            _serverPerIntervalRadio.AutoCheck = false;
            _serverPerIntervalRadio.Margin = new Padding(0, Spacing.XS, 0, Spacing.XXS);
            _serverPerIntervalRadio.Click += (s, e) =>
            {
                _serverPerIntervalRadio.Checked = !_serverPerIntervalRadio.Checked;
            };
            layout.SetColumnSpan(_serverPerIntervalRadio, layout.ColumnCount);
            layout.Controls.Add(_serverPerIntervalRadio, 0, row);
            // Riga leggermente più alta per evitare qualsiasi taglio del controllo
            EnsureRowStyle(layout, row++, Sizes.LabelHeight + Spacing.XS);

            panel.Controls.Add(layout);
            return panel;
        }
        /// <summary>
        /// Crea modern config section al volo.
        /// </summary>
        private Panel CreateModernConfigSection(List<Button> launchButtons)
        {
            var panel = CreateAccentPanel(Colors.Secondary, $"{Icons.Settings} CONFIGURAZIONE");
            
            // Layout interno con scrolling
            var sectionPadding = GetUniformSectionPadding();
            int configContentGap = sectionPadding.Top;
            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(sectionPadding.Left, 28 + configContentGap, sectionPadding.Right, sectionPadding.Bottom)
            };
            
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                RowCount = 14,
                ColumnCount = 5,
                Padding = new Padding(0, 0, 0, Spacing.SM),
                BackColor = Color.Transparent,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            
            // Colonne
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.XS));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            
            int row = 0;
            
            // PROCEDIMENTO PENALE
            AddConfigField(layout, ref row, "📄 Procedimento Penale", "ProcedimentoTextBox", "es: 12345/2025/N");

            // MAGISTRATO
            AddConfigField(layout, ref row, "⚖ Magistrato", "MagistratoTextBox", "Inserisci il magistrato di riferimento...");
            
            // RIT/SPEC
            AddConfigField(layout, ref row, "📋 RIT/SPEC", "RitSpecTextBox", "Inserisci RIT o SPEC (es: RIT 1234/2025)");
            
            // ID LAVORO
            AddConfigField(layout, ref row, "🆔 ID Lavoro", "IdLavoroTextBox", "Inserisci ID lavoro...");
            
            // TARGET
            AddConfigField(layout, ref row, "🎯 Target", "TargetTextBox", "Inserisci nome target...");
            
            // PASSWORD
            var passwordLabel = new Label
            {
                Text = "🔐 Password",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(passwordLabel, layout.ColumnCount);
            layout.Controls.Add(passwordLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);
            
            var passwordTextBox = CreateModernTextBox("Inserisci 8 caratteri...");
            passwordTextBox.Name = "PasswordTextBox";
            passwordTextBox.Dock = DockStyle.Fill;
            passwordTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            layout.Controls.Add(passwordTextBox, 0, row);
            layout.Controls.Add(CreateSpacerCell(), 1, row);
            EnsureRowStyle(layout, row, Sizes.InputHeight);

            var buttonVerticalOffset = -Spacing.XXS;
            var generatePasswordButton = CreateModernButton($"{Icons.Lock} Genera", Colors.Secondary);
            generatePasswordButton.Dock = DockStyle.None;
            generatePasswordButton.AutoSize = true;
            generatePasswordButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            generatePasswordButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            generatePasswordButton.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            generatePasswordButton.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            generatePasswordButton.Name = "GeneratePasswordButton";
            generatePasswordButton.Click += GeneraPassword_Click;
            layout.Controls.Add(generatePasswordButton, 2, row);
            layout.Controls.Add(CreateSpacerCell(), 3, row);
            EnsureRowStyle(layout, row, Sizes.InputHeight);
            launchButtons?.Add(generatePasswordButton);

            var pinPasswordButton = CreateIconSquareButton(Icons.Pin, Colors.Secondary, PinPasswordButton_Click, "PinPasswordButton");
            pinPasswordButton.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            layout.Controls.Add(pinPasswordButton, 4, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight);

            passwordTextBox.MaxLength = 8;
            var pinnedPassword = LoadUserSetting("PinnedPassword");
            if (!string.IsNullOrWhiteSpace(pinnedPassword))
            {
                passwordTextBox.Text = pinnedPassword;
                passwordTextBox.ForeColor = Colors.TextPrimary;
            }
            
            // CARTELLA DESTINAZIONE
            var cartellaLabel = new Label
            {
                Text = "📁 Cartella di esportazione",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };
            layout.SetColumnSpan(cartellaLabel, layout.ColumnCount);
            layout.Controls.Add(cartellaLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);
            
            var cartellaTextBox = CreateModernTextBox("Seleziona o incolla il percorso...");
            cartellaTextBox.Name = "CartellaTextBox";
            cartellaTextBox.Dock = DockStyle.Fill;
            cartellaTextBox.Margin = new Padding(0, Spacing.XXS, 0, 0);
            layout.Controls.Add(cartellaTextBox, 0, row);
            layout.Controls.Add(CreateSpacerCell(), 1, row);
            EnsureRowStyle(layout, row, Sizes.InputHeight);

            var sfogliaButton = CreateModernButton($"{Icons.Folder} Sfoglia", Colors.Secondary);
            sfogliaButton.Dock = DockStyle.None;
            sfogliaButton.AutoSize = true;
            sfogliaButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sfogliaButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sfogliaButton.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            sfogliaButton.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            sfogliaButton.Name = "BrowseFolderButton";
            sfogliaButton.Click += SelectDestination_Click;
            layout.Controls.Add(sfogliaButton, 2, row);
            layout.Controls.Add(CreateSpacerCell(), 3, row);
            EnsureRowStyle(layout, row, Sizes.InputHeight);
            launchButtons?.Add(sfogliaButton);

            var pinExportFolderButton = CreateIconSquareButton(Icons.Pin, Colors.Secondary, PinExportFolderButton_Click, "PinExportFolderButton");
            pinExportFolderButton.Margin = new Padding(0, buttonVerticalOffset, 0, 0);
            layout.Controls.Add(pinExportFolderButton, 4, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight);

            cartellaTextBox.ReadOnly = false;  // Permette input manuale
            var pinnedExportPath = LoadUserSetting("PinnedExportPath");
            if (!string.IsNullOrWhiteSpace(pinnedExportPath))
            {
                cartellaTextBox.Text = pinnedExportPath;
                cartellaTextBox.ForeColor = Colors.TextPrimary;
                _path = pinnedExportPath;
                _currentExportOutputPath = null;
            }
            // Valida il percorso quando l'utente esce dal campo
            cartellaTextBox.Leave += (s, e) => 
            {
                var path = GetRealValue(cartellaTextBox);
                if (!string.IsNullOrWhiteSpace(path) && path != "Seleziona o incolla il percorso...")
                {
                    // Normalizza il percorso
                    path = path.Trim().Replace('/', '\\');
                    
                    // Se il percorso esiste, aggiornalo
                    if (Directory.Exists(path))
                    {
                        _path = path;
                        _currentExportOutputPath = null;
                        cartellaTextBox.Text = path;
                        cartellaTextBox.ForeColor = Colors.Success;
                    }
                    else
                    {
                        // Prova a creare la directory se non esiste
                        try
                        {
                            Directory.CreateDirectory(path);
                            _path = path;
                            _currentExportOutputPath = null;
                            cartellaTextBox.Text = path;
                            cartellaTextBox.ForeColor = Colors.Success;
                        }
                        catch
                        {
                            cartellaTextBox.ForeColor = Colors.Error;
                            ShowWarning(
                                "Percorso non disponibile",
                                "Il percorso indicato non esiste e non è stato possibile crearlo. Verifica i permessi e riprova.");
                        }
                    }
                }
            };

            // Etichetta "Dimensione stimata" spostata sotto Cartella di esportazione (CONFIGURAZIONE)
            _archiveSizeLabel = new Label
            {
                Text = "Dimensione stimata: —",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                // Quasi attaccata al campo cartella per massima coerenza visiva
                Margin = new Padding(0, Spacing.XXS, 0, 0),
                Name = "ArchiveSizeLabel"
            };
            layout.SetColumnSpan(_archiveSizeLabel, layout.ColumnCount);
            layout.Controls.Add(_archiveSizeLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight);
            scrollPanel.Controls.Add(layout);
            panel.Controls.Add(scrollPanel);

            return panel;
        }


        /// <summary>
        /// Esegue la logica add config field senza cambiare il comportamento.
        /// </summary>
        private void AddConfigField(TableLayoutPanel layout, ref int row, string label, string fieldName, string placeholder)
        {
            var headerLabel = new Label
            {
                Text = label,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                ForeColor = Colors.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };

            layout.SetColumnSpan(headerLabel, layout.ColumnCount);
            layout.Controls.Add(headerLabel, 0, row);
            EnsureRowStyle(layout, row++, Sizes.LabelHeight - 4);
            
            var txt = CreateModernTextBox(placeholder);
            txt.Name = fieldName;
            txt.Dock = DockStyle.Fill;
            txt.Margin = new Padding(0, Spacing.XXS, 0, 0);
            layout.SetColumnSpan(txt, layout.ColumnCount);
            layout.Controls.Add(txt, 0, row);
            EnsureRowStyle(layout, row++, Sizes.InputHeight);
        }

        /// <summary>
        /// Si assicura che la parte row style sia pronta prima di procedere.
        /// </summary>
        private void EnsureRowStyle(TableLayoutPanel layout, int rowIndex, float height)
        {
            while (layout.RowStyles.Count <= rowIndex)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            }

            layout.RowStyles[rowIndex].Height = height;
            layout.RowStyles[rowIndex].SizeType = SizeType.Absolute;
        }

        /// <summary>
        /// Crea spacer cell al volo.
        /// </summary>
        private Control CreateSpacerCell()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
        }

        /// <summary>
        /// Esegue la logica normalize button widths senza cambiare il comportamento.
        /// </summary>
        private void NormalizeButtonWidths(params Button[] buttons)
        {
            if (buttons == null || buttons.Length == 0)
                return;

            int maxWidth = 0;
            var validButtons = new List<Button>();

            foreach (var button in buttons)
            {
                if (button == null)
                    continue;

                validButtons.Add(button);
                var preferredSize = button.GetPreferredSize(Size.Empty);
                if (preferredSize.Width > maxWidth)
                    maxWidth = preferredSize.Width;
            }

            if (maxWidth <= 0)
                return;

            foreach (var button in validButtons)
            {
                button.AutoSize = false;
                button.Width = maxWidth;
                button.MinimumSize = new Size(maxWidth, button.MinimumSize.Height);
            }
        }

        // Sezione stile "Sezione 1": bordo sinistro colorato, titolo, barra azioni interna, lista che riempie
        /// <summary>
        /// Crea section with list al volo.
        /// </summary>
        private Panel CreateSectionWithList(string listName, string title, Color accentColor, string[] columns, int[] columnWidths, params ButtonInfo[] actions)
        {
            Panel section = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(10, 8, 10, 10)
            };

            // Bordo colorato sinistro come sezione 1
            section.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(accentColor, 4))
                {
                    e.Graphics.DrawLine(pen, 0, 0, 0, section.Height);
                }
            };

            // Titolo
            var titleLabel = new Label
            {
                Text = title,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeMD, FontStyle.Bold),
                ForeColor = accentColor,
                Location = new Point(10, 8),
                AutoSize = true
            };
            section.Controls.Add(titleLabel);

            // Barra azioni a destra sotto al titolo
            var actionsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(section.Width - 260, 6)
            };
            actionsPanel.SizeChanged += (s, e) => { actionsPanel.Left = section.Width - actionsPanel.Width - 10; };
            section.Resize += (s, e) => { actionsPanel.Left = section.Width - actionsPanel.Width - 10; };

            foreach (var a in actions)
            {
                var btn = new Button
                {
                    Text = a.Text,
                    Height = 28,
                    Width = Math.Max(120, TextRenderer.MeasureText(a.Text, GetCachedFont("Cascadia Mono", 9, FontStyle.Bold)).Width + 20),
                    Font = GetCachedFont("Cascadia Mono", 9, FontStyle.Bold),
                    ForeColor = Color.White,
                    Margin = new Padding(6, 0, 0, 0)
                };
                ApplyButtonStyle(btn, a.BackColor, ControlPaint.Light(a.BackColor), ControlPaint.Dark(a.BackColor));
                if (a.ClickHandler != null) btn.Click += a.ClickHandler;
                btn.Enabled = !a.RequiresSelection; // verrà aggiornato da UpdateButtonStates
                btn.Name = a.Name ?? a.Text.Replace(" ", "");
                actionsPanel.Controls.Add(btn);
            }
            section.Controls.Add(actionsPanel);

            // Lista
            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45,45,45) };
            var listPanel = CreateListViewPanel(listName, columns, columnWidths);
            listPanel.Dock = DockStyle.Fill;
            listHost.Controls.Add(listPanel);

            // Posiziona lista sotto titolo (offset di 40)
            listHost.Top = 40;
            listHost.Height = section.Height - 48;
            listHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            section.Controls.Add(listHost);

            return section;
        }

        // Variante: azioni in basso, lista sopra, stile identico alla sezione 1
        /// <summary>
        /// Crea section with bottom actions al volo.
        /// </summary>
        private Panel CreateSectionWithBottomActions(string listName, string title, Color accentColor, string[] columns, int[] columnWidths, params ButtonInfo[] actions)
        {
            Panel section = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(16, 12, 16, 16)
            };

            // Bordo sinistro colorato
            section.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(accentColor, 4))
                {
                    e.Graphics.DrawLine(pen, 0, 0, 0, section.Height);
                }
            };

            // Titolo
            var titleLabel = new Label
            {
                Text = title,
                Font = GetCachedFont(Fonts.Primary, 12, FontStyle.Bold),
                ForeColor = accentColor,
                Location = new Point(10, 8),
                AutoSize = true,
                Dock = DockStyle.Top
            };

            // Spacer tra titolo e lista (coerente con sezione 1)
            var titleSpacer = new Panel 
            { 
                Dock = DockStyle.Top, 
                Height = 16, 
                BackColor = Color.Transparent 
            };

            // Barra azioni in basso con allineamento perfetto
            var bottomBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = Color.FromArgb(45,45,45),
                Padding = new Padding(10, 8, 10, 8)
            };
            
            // FlowLayoutPanel per allineamento automatico pulsanti
            var buttonFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                BackColor = Color.Transparent,
                MinimumSize = new Size(0, Sizes.ButtonHeight),
                MaximumSize = new Size(int.MaxValue, Sizes.ButtonHeight)
            };
            
            foreach (var a in actions)
            {
                var btn = new Button
                {
                    Text = a.Text,
                    Height = 28,
                    Width = Math.Max(120, TextRenderer.MeasureText(a.Text, GetCachedFont("Cascadia Mono", 9, FontStyle.Bold)).Width + 24),
                    Font = GetCachedFont("Cascadia Mono", 9, FontStyle.Bold),
                    ForeColor = Color.White,
                    Margin = new Padding(0, 0, 10, 0),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                ApplyButtonStyle(btn, a.BackColor, ControlPaint.Light(a.BackColor), ControlPaint.Dark(a.BackColor));
                if (a.ClickHandler != null) btn.Click += a.ClickHandler;
                btn.Enabled = !a.RequiresSelection;
                btn.Name = a.Name ?? a.Text.Replace(" ", "");
                buttonFlow.Controls.Add(btn);
            }
            bottomBar.Controls.Add(buttonFlow);

            // Lista
            var listPanel = CreateListViewPanel(listName, columns, columnWidths);
            listPanel.Dock = DockStyle.Fill;

            // Composizione controlli (ordine Dock corretto)
            section.Controls.Add(listPanel);
            section.Controls.Add(bottomBar);
            section.Controls.Add(titleSpacer);
            section.Controls.Add(titleLabel);

            return section;
        }
        // Contenitore sezione con bordo sinistro colorato a tutta altezza
        /// <summary>
        /// Crea section container al volo.
        /// </summary>
        private Panel CreateSectionContainer(Color accentColor)
        {
            Panel container = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(12, 8, 12, 12)
            };
            container.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(accentColor, 4))
                {
                    e.Graphics.DrawLine(pen, 0, 0, 0, container.Height);
                }
            };
            return container;
        }

        
        /// <summary>
        /// Crea avvia button al volo.
        /// </summary>
        private Button CreateAvviaButton()
        {
            Button btn = new Button
            {
                Name = "AvviaArchiviazioneButton",
                Text = "AVVIA ARCHIVIAZIONE",
                Dock = DockStyle.Fill, // Allineato alla larghezza completa
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = GetSafeFont("Cascadia Mono", 12, FontStyle.Bold), // Font coerente
                Margin = new Padding(0) // Rimuove margini per allineamento perfetto
            };
            ApplyButtonStyle(btn, Color.FromArgb(33,150,243), Color.FromArgb(41,182,246), Color.FromArgb(25,118,210));
            btn.Click += AvviaArchiviazione_Click;
            return btn;
        }

        /// <summary>
        /// Crea recupera button al volo.
        /// </summary>
        private Button CreateRecuperaButton()
        {
            Button btn = new Button
            {
                Text = "RECUPERA ULTIMO LANCIO",
                Dock = DockStyle.Fill, // Allineato alla larghezza completa
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = GetSafeFont("Cascadia Mono", 11, FontStyle.Bold), // Font leggermente più piccolo
                Margin = new Padding(0) // Rimuove margini per allineamento perfetto
            };
            ApplyButtonStyle(btn, Color.FromArgb(45,45,45), Color.FromArgb(66,66,66), Color.FromArgb(55,55,55));
            btn.Click += RecuperaUltimoLancio_Click;
            return btn;
        }
        /// <summary>
        /// Crea in corso content al volo.
        /// </summary>
        private void CreateInCorsoContent()
        {
            _logRefreshTimer?.Stop();
            _currentLogFile = null;
            _currentLogSnapshot = null;

            contentPanel.Controls.Clear();
            contentPanel.BackColor = Colors.Background;
            contentPanel.Padding = new Padding(0);

            _inCorsoSearchTerm = string.Empty;

            var mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = Spacing.PagePadding,
                BackColor = Color.Transparent
            };

            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.SectionGap));
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 220F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var uniformPadding = GetUniformSectionPadding();
            int sectionPaddingHorizontal = uniformPadding.Left;
            int sectionPaddingTop = Spacing.AccentContentPadding.Top + uniformPadding.Top;
            int sectionPaddingBottom = Math.Max(0, Spacing.AccentContentPadding.Bottom - 1);
            int separatorContentGap = uniformPadding.Top;

            // LISTA / AVANZAMENTO
            // Crea i pulsanti prima del pannello per poterli passare
            var actionsRow = CreateInCorsoActionsGrid(sectionPaddingHorizontal, sectionPaddingTop, sectionPaddingBottom);
            var listPanel = CreateAccentPanel(Colors.Secondary, $"{Icons.Rocket} ARCHIVIAZIONI IN CORSO", actionsRow);
            listPanel.Margin = Padding.Empty;
            var listBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(
                    sectionPaddingHorizontal,
                    sectionPaddingTop,
                    sectionPaddingHorizontal,
                    sectionPaddingBottom)
            };

            var listLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var tableContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            var listView = CreateModernListView(
                "InCorsoListView",
                new[] { "ID JOB", "ID Lavoro", "Telecamera", "Periodo", "Procedimento", "RIT/SPEC", "Target", "Avanzamento" },
                new[] { 140, 160, 220, 220, 180, 160, 240, 200 }
            );
            listView.ItemSelectionChanged += (s, e) =>
            {
                if (!e.IsSelected)
                {
                    if (listView.SelectedItems.Count == 0)
                    {
                        RenderInCorsoDetails(null);
                        UpdateButtonStates(listView,
                            "DeleteArchiviazioneButton",
                            "CopyInCorsoDetailsButton",
                            "InCorsoCopyToLaunchButton",
                            "MoveUpArchiviazioneButton",
                            "MoveDownArchiviazioneButton");
                    }
                    return;
                }

                var selectedItem = e.Item as ListViewItem;
                var selectedJob = selectedItem?.Tag as ExportJob;
                RenderInCorsoDetails(selectedJob);
                UpdateButtonStates(listView,
                    "DeleteArchiviazioneButton",
                    "CopyInCorsoDetailsButton",
                    "InCorsoCopyToLaunchButton",
                    "MoveUpArchiviazioneButton",
                    "MoveDownArchiviazioneButton");
            };
            listView.ColumnWidthChanging += (s, e) => OnInCorsoColumnWidthChanging(listView, e);
            listView.Resize += (s, e) => AdjustInCorsoColumns(listView);

            tableContainer.Controls.Add(listView);
            listLayout.Controls.Add(tableContainer, 0, 0);

            ApplyInCorsoInitialLayout(listView);

            listBody.Controls.Add(listLayout);
            listPanel.Controls.Add(listBody);
            listBody.SendToBack();

            var listWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            listWrapper.Controls.Add(listPanel);
            rootLayout.Controls.Add(listWrapper, 0, 0);
            rootLayout.SetColumnSpan(listWrapper, 3);

            // DETTAGLI
            var detailActions = CreateInCorsoDetailActionsPanel();
            var detailPanel = CreateAccentPanel(Colors.Success, $"{Icons.Detail} DETTAGLI", detailActions);
            detailPanel.Margin = new Padding(0, 0, 0, Spacing.SM);
            detailPanel.Name = "InCorsoDetailPanel";
            detailPanel.SizeChanged += (s, e) => UpdateInCorsoDetailFooterLayout(detailPanel);
            int detailPaddingTop = Math.Max(Spacing.MD, sectionPaddingTop / 2);
            int detailPaddingBottom = Math.Max(0, sectionPaddingBottom - Spacing.MD);
            var detailBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(
                    sectionPaddingHorizontal,
                    sectionPaddingTop,
                    sectionPaddingHorizontal,
                    Math.Max(0, detailPaddingBottom - 2))
            };

            var detailPlaceholder = new Panel
            {
                Name = "InCorsoDetailPlaceholder",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            var placeholderLabel = new Label
            {
                Name = "InCorsoDetailPlaceholderLabel",
                Text = "Seleziona un'archiviazione per visualizzare i dettagli",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Italic),
                ForeColor = Colors.TextMuted,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            detailPlaceholder.Controls.Add(placeholderLabel);

            var detailContent = new TableLayoutPanel
            {
                Name = "InCorsoDetailContent",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Visible = false
            };
            detailContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            detailContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            detailContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var detailDescription = new Label
            {
                Name = "InCorsoDetailDescription",
                Text = "Seleziona una riga dell'elenco per consultare i dettagli completi dell'archiviazione in corso.",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Italic),
                ForeColor = Colors.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Spacing.XS),
                Padding = new Padding(0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var infoTable = new TableLayoutPanel
            {
                Name = "InCorsoDetailTable",
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            infoTable.Margin = new Padding(0, Spacing.SM, 0, Spacing.SM);

            int detailRow = 0;
            AddInCorsoDetailRow(infoTable, "ID JOB", "InCorsoDetailIdValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "Procura", "InCorsoDetailProcuraValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "ID Lavoro", "InCorsoDetailWorkIdValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "Telecamera", "InCorsoDetailCameraValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "Periodo", "InCorsoDetailPeriodoValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "P.P. e RIT/SPEC", "InCorsoDetailPPValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "PM", "InCorsoDetailMagistratoValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "Target", "InCorsoDetailTargetValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "Cartella di esportazione", "InCorsoDetailPathValue", detailRow++);
            AddInCorsoDetailRow(infoTable, "Password", "InCorsoDetailPasswordValue", detailRow++);
            AddInCorsoDetailRow(infoTable, string.Empty, "InCorsoDetailSpacerValue", detailRow++);

            var progressPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, Spacing.XXS),
                Padding = new Padding(0)
            };
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            progressPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var progressLabel = new Label
            {
                Name = "InCorsoProgressValue",
                Text = "0%",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeLG, FontStyle.Bold),
                ForeColor = Colors.Success,
                AutoSize = true,
                Dock = DockStyle.Left,
                Margin = new Padding(0, 0, Spacing.XS, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var progressWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                Height = Sizes.InputHeight,
                MinimumSize = new Size(0, Sizes.InputHeight),
                MaximumSize = new Size(int.MaxValue, Sizes.InputHeight),
                BackColor = Color.Transparent,
                Padding = new Padding(0, Spacing.XXS, 0, Spacing.XXS),
                Margin = new Padding(0)
            };

            var progressBar = new ProgressBar
            {
                Name = "InCorsoProgressBar",
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Dock = DockStyle.Fill,
                Height = Sizes.InputHeight - (Spacing.XXS * 2),
                MinimumSize = new Size(0, Sizes.InputHeight - (Spacing.XXS * 2)),
                MaximumSize = new Size(int.MaxValue, Sizes.InputHeight - (Spacing.XXS * 2)),
                Margin = new Padding(0)
            };
            progressWrapper.Controls.Add(progressBar);

            progressPanel.Controls.Add(progressLabel, 0, 0);
            progressPanel.Controls.Add(progressWrapper, 1, 0);

            var detailFooter = new Label
            {
                Name = "InCorsoDetailFooter",
                Text = string.Empty,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Italic),
                ForeColor = Colors.TextMuted,
                AutoSize = true,
                Margin = new Padding(0)
            };

            var footerContainer = new TableLayoutPanel
            {
                Name = "InCorsoDetailFooterContainer",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, Spacing.SM, 0, 0),
                Padding = new Padding(0)
            };
            footerContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footerContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footerContainer.Controls.Add(progressPanel, 0, 0);
            footerContainer.Controls.Add(detailFooter, 0, 1);

            detailContent.Controls.Add(detailDescription, 0, 0);
            detailContent.Controls.Add(infoTable, 0, 1);
            detailContent.Controls.Add(footerContainer, 0, 2);

            detailBody.Controls.Add(detailContent);
            detailBody.Controls.Add(detailPlaceholder);
            detailPlaceholder.BringToFront();
            detailPanel.Controls.Add(detailBody);
            detailBody.SendToBack();

            var detailWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = Padding.Empty
            };
            detailWrapper.Controls.Add(detailPanel);

            var logActions = CreateInCorsoLogActionsPanel();
            var logAccentColor = Color.FromArgb(90, 90, 90);
            var logPanel = CreateAccentPanel(logAccentColor, $"{Icons.Log} LOG", logActions);
            logPanel.Margin = new Padding(0, 0, 0, Spacing.SM);
            logPanel.Name = "InCorsoLogPanel";
            var logBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(
                    sectionPaddingHorizontal,
                    sectionPaddingTop,
                    sectionPaddingHorizontal,
                    Math.Max(0, sectionPaddingBottom - 1))
            };

            var logTextBox = new RichTextBox
            {
                Name = "InCorsoLogTextBox",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Colors.TextPrimary,
                BorderStyle = BorderStyle.None,
                Font = GetCachedFont(Fonts.Mono, Fonts.SizeXS),
                WordWrap = false,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                ShortcutsEnabled = true
            };

            var logLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            logLayout.Controls.Add(logTextBox, 0, 0);

            logBody.Controls.Add(logLayout);
            logPanel.Controls.Add(logBody);
            logBody.SendToBack();

            var logWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            logWrapper.Controls.Add(logPanel);

            var bottomLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, Spacing.SectionGap, 0, 0),
                Padding = Padding.Empty
            };
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.SectionGap));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            bottomLayout.Controls.Add(detailWrapper, 0, 0);
            bottomLayout.Controls.Add(logWrapper, 2, 0);

            rootLayout.Controls.Add(bottomLayout, 0, 1);
            rootLayout.SetColumnSpan(bottomLayout, 3);

#if DEBUG
            rootLayout.Layout += (sender, args) =>
            {
                LogBounds("IN CORSO - ListPanel", listPanel);
                LogBounds("IN CORSO - DetailPanel", detailPanel);
                LogBounds("IN CORSO - LogPanel", logPanel);
                Debug.WriteLine("--------------------------------------------------");
            };
#endif

            mainContainer.Controls.Add(rootLayout);
            contentPanel.Controls.Add(mainContainer);

            SetInCorsoLogText(string.Empty);
            LoadInCorsoData();
            RenderInCorsoDetails(null);
            BindLogToJob(null);
        }

        /// <summary>
        /// Crea in corso action button al volo.
        /// </summary>
        private Button CreateInCorsoActionButton(string text, Color baseColor, EventHandler handler, string name, bool requiresSelection)
        {
            var button = CreateModernButton(text, baseColor, handler);
            button.Name = name;
            button.Dock = DockStyle.None;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Height = Sizes.InputHeight;
            button.MinimumSize = new Size(0, Sizes.InputHeight);
            button.MaximumSize = new Size(int.MaxValue, Sizes.InputHeight);
            button.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
            button.Margin = new Padding(Spacing.XS, 0, 0, 0);
            button.Enabled = !requiresSelection;

            button.EnabledChanged += (s, e) =>
            {
                var storedColor = baseColor;
                button.ForeColor = button.Enabled
                    ? storedColor
                    : Color.FromArgb(140, storedColor.R, storedColor.G, storedColor.B);
            };

            if (!button.Enabled)
            {
                button.ForeColor = Color.FromArgb(140, baseColor.R, baseColor.G, baseColor.B);
            }

            return button;
        }

        /// <summary>
        /// Crea in corso search box al volo.
        /// </summary>
        private TextBox CreateInCorsoSearchBox()
        {
            const string placeholder = "Cerca ID, telecamera o percorso...";
            var searchBox = CreateModernTextBox(placeholder);
            searchBox.Name = "InCorsoSearchBox";
            searchBox.Width = 260;
            searchBox.MinimumSize = new Size(200, Sizes.InputHeight);
            searchBox.Margin = new Padding(0, 0, Spacing.XS, 0);
            searchBox.BackColor = Color.FromArgb(40, 40, 40);
            searchBox.BorderStyle = BorderStyle.FixedSingle;

            searchBox.TextChanged += (s, e) =>
            {
                if (searchBox.Tag is string ph && searchBox.Text == ph && searchBox.ForeColor != Colors.TextPrimary)
                    return;

                var text = searchBox.Text;
                if (searchBox.Tag is string p && string.Equals(text, p, StringComparison.Ordinal))
                    text = string.Empty;

                _inCorsoSearchTerm = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
                ApplyInCorsoFilters();
            };

            return searchBox;
        }

        /// <summary>
        /// Crea in corso log search box al volo.
        /// </summary>
        private TextBox CreateInCorsoLogSearchBox()
        {
            const string placeholder = "Cerca nel log...";
            var searchBox = CreateModernTextBox(placeholder);
            searchBox.Name = "InCorsoLogSearchBox";
            searchBox.Width = 220;
            searchBox.MinimumSize = new Size(180, Sizes.InputHeight);
            searchBox.Margin = new Padding(0, 0, Spacing.XS, 0);
            searchBox.BackColor = Color.FromArgb(40, 40, 40);
            searchBox.BorderStyle = BorderStyle.FixedSingle;

            searchBox.TextChanged += (s, e) =>
            {
                if (searchBox.Tag is string ph && searchBox.Text == ph && searchBox.ForeColor != Colors.TextPrimary)
                    return;

                var text = searchBox.Text;
                if (searchBox.Tag is string p && string.Equals(text, p, StringComparison.Ordinal))
                    text = string.Empty;

                _inCorsoLogSearchTerm = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
                HighlightInCorsoLogSearchTerm();
            };

            return searchBox;
        }

        /// <summary>
        /// Crea in corso actions grid al volo.
        /// </summary>
        private Control CreateInCorsoActionsGrid(int paddingHorizontal, int paddingTop, int paddingBottom)
        {
            int actionButtonHeight = Sizes.ButtonHeight;

            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                MinimumSize = new Size(0, actionButtonHeight),
                MaximumSize = new Size(int.MaxValue, actionButtonHeight)
            };

            var searchBox = CreateInCorsoSearchBox();
            int searchBoxTop = Spacing.XXS;
            searchBox.Margin = new Padding(0, searchBoxTop, 0, 0);
            searchBox.MinimumSize = new Size(searchBox.MinimumSize.Width, actionButtonHeight - Spacing.SM);
            searchBox.MaximumSize = new Size(int.MaxValue, actionButtonHeight - Spacing.SM);
            flow.Controls.Add(searchBox);

            int buttonTop = 0;

            var moveUpButton = CreateInCorsoActionButton($"{Icons.ArrowUp} Sposta su", Colors.Info, MoveUpArchiviazione_Click, "MoveUpArchiviazioneButton", true);
            moveUpButton.Margin = new Padding(Spacing.XS, buttonTop, 0, 0);
            flow.Controls.Add(moveUpButton);

            var moveDownButton = CreateInCorsoActionButton($"{Icons.ArrowDown} Sposta giù", Colors.Info, MoveDownArchiviazione_Click, "MoveDownArchiviazioneButton", true);
            moveDownButton.Margin = new Padding(Spacing.XS, buttonTop, 0, 0);
            flow.Controls.Add(moveDownButton);

            var deleteButton = CreateInCorsoActionButton($"{Icons.Trash} Elimina", Colors.Error, DeleteArchiviazione_Click, "DeleteArchiviazioneButton", true);
            deleteButton.Margin = new Padding(Spacing.XS, buttonTop, 0, 0);
            flow.Controls.Add(deleteButton);

            return flow;
        }

        /// <summary>
        /// Crea in corso detail actions panel al volo.
        /// </summary>
        private Control CreateInCorsoDetailActionsPanel()
        {
            int actionButtonHeight = Sizes.ButtonHeight;
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                MinimumSize = new Size(0, actionButtonHeight),
                MaximumSize = new Size(int.MaxValue, actionButtonHeight)
            };

            var copyToLaunchButton = CreateInCorsoActionButton($"{Icons.Rocket} Compila LANCIO", Colors.Primary, CopyInCorsoToLaunch_Click, "InCorsoCopyToLaunchButton", true);
            copyToLaunchButton.Margin = new Padding(0, 0, 0, 0);
            flow.Controls.Add(copyToLaunchButton);

            var copyButton = CreateIconSquareButton(Icons.Clipboard, Colors.Success, CopyInCorsoDetailsToClipboard, "CopyInCorsoDetailsButton");
            copyButton.Margin = new Padding(Spacing.XS, 0, 0, 0);
            copyButton.Enabled = false;
            flow.Controls.Add(copyButton);

            return flow;
        }

        /// <summary>
        /// Crea in corso log actions panel al volo.
        /// </summary>
        private Control CreateInCorsoLogActionsPanel()
        {
            int actionButtonHeight = Sizes.ButtonHeight;
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                MinimumSize = new Size(0, actionButtonHeight),
                MaximumSize = new Size(int.MaxValue, actionButtonHeight)
            };

            var searchBox = CreateInCorsoLogSearchBox();
            int logSearchTop = Spacing.XXS;
            searchBox.Margin = new Padding(0, logSearchTop, 0, 0);
            searchBox.MinimumSize = new Size(searchBox.MinimumSize.Width, actionButtonHeight - Spacing.SM);
            searchBox.MaximumSize = new Size(int.MaxValue, actionButtonHeight - Spacing.SM);
            flow.Controls.Add(searchBox);

            return flow;
        }

        /// <summary>
        /// Crea terminate actions grid al volo.
        /// </summary>
        private Control CreateTerminateActionsGrid(int paddingHorizontal, int paddingTop, int paddingBottom)
        {
            int actionButtonHeight = Sizes.ButtonHeight;
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                MinimumSize = new Size(0, actionButtonHeight),
                MaximumSize = new Size(int.MaxValue, actionButtonHeight)
            };

            var searchBox = CreateTerminateSearchBox();
            int searchBoxTop = Spacing.XXS;
            searchBox.Margin = new Padding(0, searchBoxTop, 0, 0);
            searchBox.MinimumSize = new Size(searchBox.MinimumSize.Width, actionButtonHeight - Spacing.SM);
            searchBox.MaximumSize = new Size(int.MaxValue, actionButtonHeight - Spacing.SM);
            flow.Controls.Add(searchBox);

            var deleteButton = CreateInCorsoActionButton($"{Icons.Trash} Elimina", Colors.Error, EliminaCompletato_Click, "TerminateDeleteButton", true);
            int terminateDeleteTop = 0;
            deleteButton.Margin = new Padding(Spacing.XS, terminateDeleteTop, 0, 0);
            flow.Controls.Add(deleteButton);

            return flow;
        }

        /// <summary>
        /// Crea terminate search box al volo.
        /// </summary>
        private TextBox CreateTerminateSearchBox()
        {
            const string placeholder = "Cerca ID, telecamera o percorso...";
            var searchBox = CreateModernTextBox(placeholder);
            searchBox.Name = "TerminateSearchBox";
            searchBox.Width = 260;
            searchBox.MinimumSize = new Size(200, Sizes.InputHeight);
            searchBox.BackColor = Color.FromArgb(40, 40, 40);
            searchBox.BorderStyle = BorderStyle.FixedSingle;

            searchBox.TextChanged += (s, e) =>
            {
                if (searchBox.Tag is string ph && searchBox.Text == ph && searchBox.ForeColor != Colors.TextPrimary)
                    return;

                var text = searchBox.Text;
                if (searchBox.Tag is string p && string.Equals(text, p, StringComparison.Ordinal))
                    text = string.Empty;

                _terminateSearchTerm = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
                ApplyTerminateFilters(_selectedTerminateInfo, maintainSelection: true);
            };

            return searchBox;
        }

        /// <summary>
        /// Aggiorna terminate button states e mantiene lo stato coerente.
        /// </summary>
        private void UpdateTerminateButtonStates(ListView listView, params string[] buttonNames)
        {
            bool hasSelection = listView?.SelectedItems.Cast<ListViewItem>().Any(item => item?.Tag is ArchiviazioneInfo) ?? false;

            foreach (var buttonName in buttonNames ?? Array.Empty<string>())
            {
                if (contentPanel.Controls.Find(buttonName, true).FirstOrDefault() is Button button)
                    button.Enabled = hasSelection;
            }
        }

        /// <summary>
        /// Crea terminate detail actions panel al volo.
        /// </summary>
        private Control CreateTerminateDetailActionsPanel()
        {
            int actionButtonHeight = Sizes.ButtonHeight;
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                MinimumSize = new Size(0, actionButtonHeight),
                MaximumSize = new Size(int.MaxValue, actionButtonHeight)
            };

            var copyToLaunchButton = CreateInCorsoActionButton($"{Icons.Rocket} Compila LANCIO", Colors.Primary, CopyTerminateToLaunch_Click, "TerminateCopyToLaunchButton", true);
            copyToLaunchButton.Margin = new Padding(0, 0, 0, 0);
            flow.Controls.Add(copyToLaunchButton);

            var copyButton = CreateIconSquareButton(Icons.Clipboard, Colors.Success, CopyTerminateDetailsToClipboard, "TerminateCopyDetailsButton");
            copyButton.Margin = new Padding(Spacing.XS, 0, 0, 0);
            copyButton.Enabled = false;
            flow.Controls.Add(copyButton);

            var createSitButton = CreateInCorsoActionButton($"{Icons.Detail} Crea SIT", Colors.Success, CreateSitDocument_Click, "TerminateCreateSitButton", true);
            createSitButton.Margin = new Padding(Spacing.XS, 0, 0, 0);
            flow.Controls.Add(createSitButton);

            return flow;
        }

        /// <summary>
        /// Crea terminate log actions panel al volo.
        /// </summary>
        private Control CreateTerminateLogActionsPanel()
        {
            int actionButtonHeight = Sizes.ButtonHeight;
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                MinimumSize = new Size(0, actionButtonHeight),
                MaximumSize = new Size(int.MaxValue, actionButtonHeight)
            };

            var searchBox = CreateTerminateLogSearchBox();
            int logSearchTop = Spacing.XXS;
            searchBox.Margin = new Padding(0, logSearchTop, 0, 0);
            searchBox.MinimumSize = new Size(searchBox.MinimumSize.Width, actionButtonHeight - Spacing.SM);
            searchBox.MaximumSize = new Size(int.MaxValue, actionButtonHeight - Spacing.SM);
            flow.Controls.Add(searchBox);

            return flow;
        }

        /// <summary>
        /// Crea terminate log search box al volo.
        /// </summary>
        private TextBox CreateTerminateLogSearchBox()
        {
            const string placeholder = "Cerca nel log...";
            var searchBox = CreateModernTextBox(placeholder);
            searchBox.Name = "TerminateLogSearchBox";
            searchBox.Width = 220;
            searchBox.MinimumSize = new Size(200, Sizes.InputHeight);
            searchBox.BackColor = Color.FromArgb(40, 40, 40);
            searchBox.BorderStyle = BorderStyle.FixedSingle;
            searchBox.TextChanged += (s, e) =>
            {
                if (searchBox.Tag is string ph && searchBox.Text == ph && searchBox.ForeColor != Colors.TextPrimary)
                    return;

                var text = searchBox.Text;
                if (searchBox.Tag is string p && string.Equals(text, p, StringComparison.Ordinal))
                    text = string.Empty;

                _terminateLogSearchTerm = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
                HighlightTerminateLogSearchTerm();
            };

            return searchBox;
        }

        /// <summary>
        /// Crea in corso metric card al volo.
        /// </summary>
        private Panel CreateInCorsoMetricCard(string title, string value, Color accentColor, string description, string valueLabelName, string descriptionLabelName)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, Spacing.MD, 0),
                Padding = new Padding(Spacing.MD, Spacing.SM, Spacing.MD, Spacing.SM),
                BackColor = Color.FromArgb(32, 32, 32),
                MinimumSize = new Size(0, 68)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Text = title,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Bold),
                ForeColor = Colors.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Spacing.XXS)
            };

            var valueLabel = new Label
            {
                Name = valueLabelName,
                Text = value,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXL, FontStyle.Bold),
                ForeColor = Colors.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0)
            };

            var descriptionLabel = new Label
            {
                Name = descriptionLabelName,
                Text = description,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS),
                ForeColor = Colors.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0)
            };

            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(valueLabel, 0, 1);
            layout.Controls.Add(descriptionLabel, 0, 2);

            card.Controls.Add(layout);
            return card;
        }


        /// <summary>
        /// Esegue la logica add in corso detail row senza cambiare il comportamento.
        /// </summary>
        private void AddInCorsoDetailRow(TableLayoutPanel table, string labelText, string valueName, int rowIndex)
        {
            if (table == null)
                return;

            // Righe molto compatte per i dettagli, per evitare eccessiva distanza verticale
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));

            bool isPasswordValue = !string.IsNullOrWhiteSpace(valueName) &&
                                   valueName.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0;

            var keyLabel = new Label
            {
                Text = labelText,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Bold),
                ForeColor = Colors.TextSecondary,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, Spacing.LG, 0),
                Padding = new Padding(0, 0, 0, 0),
                TextAlign = ContentAlignment.TopLeft,
                MinimumSize = new Size(180, 0)
            };

            var valueLabel = new Label
            {
                Name = valueName,
                Text = string.Empty,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS),
                ForeColor = Colors.TextPrimary,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 0),
                Padding = isPasswordValue ? new Padding(0, 0, 0, 0) : new Padding(0, 0, 0, 0),
                AutoEllipsis = true,
                UseMnemonic = false,
                TextAlign = ContentAlignment.TopLeft,
                MinimumSize = new Size(0, 16)
            };

            valueLabel.DoubleClick += (s, e) =>
            {
                var lbl = s as Label;
                if (lbl == null)
                    return;
                var text = lbl.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return;
                try
                {
                    Clipboard.SetText(text);
                }
                catch
                {
                }
            };

            table.Controls.Add(keyLabel, 0, rowIndex);
            table.Controls.Add(valueLabel, 1, rowIndex);
        }

        /// <summary>
        /// Crea status chip al volo.
        /// </summary>
        private Label CreateStatusChip(string text, Color? accentColor = null, bool filled = true)
        {
            var baseColor = accentColor ?? Colors.Info;
            var chip = new Label
            {
                Text = string.IsNullOrWhiteSpace(text) ? "N/D" : text,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(Spacing.SM, Spacing.XXS, Spacing.SM, Spacing.XXS),
                Margin = new Padding(0, 0, Spacing.XS, Spacing.XS),
                BackColor = filled ? Color.FromArgb(40, baseColor) : Color.Transparent,
                ForeColor = filled ? Colors.TextPrimary : baseColor,
                BorderStyle = filled ? BorderStyle.None : BorderStyle.FixedSingle
            };

            if (!filled)
            {
                chip.ForeColor = baseColor;
                chip.BackColor = Color.Transparent;
            }

            return chip;
        }
        /// <summary>
        /// Applica in corso filters alle impostazioni correnti.
        /// </summary>
        private void ApplyInCorsoFilters(ExportJob preferredSelection = null, bool maintainSelection = true)
        {
            var listView = contentPanel.Controls.Find("InCorsoListView", true).FirstOrDefault() as ListView;
            if (listView == null)
                return;

            var currentSelection = preferredSelection;
            if (currentSelection == null && maintainSelection && _currentInCorsoDetailJob != null)
                currentSelection = _currentInCorsoDetailJob;
            if (currentSelection == null && maintainSelection && listView.SelectedItems.Count > 0)
                currentSelection = listView.SelectedItems[0].Tag as ExportJob;

            var filtered = FilterInCorsoJobs().ToList();

            listView.BeginUpdate();
            listView.Items.Clear();

                foreach (var job in filtered)
                {
                    var item = new ListViewItem(string.IsNullOrWhiteSpace(job.Id) ? "N/D" : job.Id);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(job.IdLavoro) ? "N/D" : job.IdLavoro);
                item.SubItems.Add(string.IsNullOrWhiteSpace(job.CameraName) ? "N/D" : job.CameraName);
                    item.SubItems.Add(FormatPeriodo(job.StartTime, job.EndTime));
                    item.SubItems.Add(string.IsNullOrWhiteSpace(job.ProcedimentoPenale) ? "N/D" : job.ProcedimentoPenale);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(job.RitSpec) ? "N/D" : job.RitSpec);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(job.Target) ? "N/D" : job.Target);
                    item.SubItems.Add($"{Math.Max(0, Math.Min(100, job.Progress))}%");
                    item.Tag = job;

                    if (currentSelection != null &&
                        ((object)job == currentSelection || (!string.IsNullOrEmpty(currentSelection.Id) && currentSelection.Id == job.Id)))
                    {
                        item.Selected = true;
                        item.Focused = true;
                    }

                    listView.Items.Add(item);
            }

            listView.EndUpdate();

            var state = EnsureListViewState(listView);
            if (state != null)
            {
                if (state.SelectedSubItem >= listView.Columns.Count)
                    state.SelectedSubItem = Math.Max(0, listView.Columns.Count - 1);

                listView.ListViewItemSorter = new ListViewItemComparer(state.SortColumn, state.Ascending);
                listView.Sort();
                listView.Invalidate();
            }

            AdjustInCorsoColumns(listView);
            RenderInCorsoDetails(listView.SelectedItems.Count > 0 ? listView.SelectedItems[0].Tag as ExportJob : null);
            UpdateButtonStates(listView,
                "DeleteArchiviazioneButton",
                "CopyInCorsoDetailsButton",
                "InCorsoCopyToLaunchButton",
                "MoveUpArchiviazioneButton",
                "MoveDownArchiviazioneButton");
        }

        /// <summary>
        /// Filtra in corso jobs con i criteri attivi.
        /// </summary>
        private IEnumerable<ExportJob> FilterInCorsoJobs()
        {
            IEnumerable<ExportJob> query = _inCorsoSnapshot ?? new List<ExportJob>();

            if (!string.IsNullOrWhiteSpace(_inCorsoSearchTerm))
            {
                var term = _inCorsoSearchTerm;
                query = query.Where(job =>
                    (!string.IsNullOrEmpty(job.Id) && job.Id.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.CameraName) && job.CameraName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.Path) && job.Path.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.ProcedimentoPenale) && job.ProcedimentoPenale.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.RitSpec) && job.RitSpec.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.Magistrato) && job.Magistrato.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.Procura) && job.Procura.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.Target) && job.Target.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(job.IdLavoro) && job.IdLavoro.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    $"{Math.Max(0, Math.Min(100, job.Progress))}%".IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            query = query
                .OrderBy(job => job?.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(job => job?.CreatedAt ?? DateTime.MinValue);

            return query;
        }

        /// <summary>
        /// Aggiorna in corso stats e mantiene lo stato coerente.
        /// </summary>
        private void UpdateInCorsoStats(IReadOnlyList<ExportJob> snapshot)
        {
            int total = snapshot?.Count ?? 0;
            var activeValueLabel = contentPanel.Controls.Find("InCorsoActiveValueLabel", true).FirstOrDefault() as Label;
            var activeDescriptionLabel = contentPanel.Controls.Find("InCorsoActiveDescriptionLabel", true).FirstOrDefault() as Label;

            if (activeValueLabel != null)
                activeValueLabel.Text = total.ToString();

            if (activeDescriptionLabel != null)
                activeDescriptionLabel.Text = "In esecuzione adesso";

            double averageProgress = snapshot != null && snapshot.Count > 0
                ? snapshot.Average(job => Math.Max(0, Math.Min(100, job.Progress)))
                : 0;

            var averageLabel = contentPanel.Controls.Find("InCorsoAverageProgressLabel", true).FirstOrDefault() as Label;
            var averageDescriptionLabel = contentPanel.Controls.Find("InCorsoAverageDescriptionLabel", true).FirstOrDefault() as Label;

            if (averageLabel != null)
                averageLabel.Text = snapshot != null && snapshot.Count > 0 ? $"{averageProgress:0}%" : "0%";

            if (averageDescriptionLabel != null)
            {
                averageDescriptionLabel.Text = snapshot != null && snapshot.Count > 0
                    ? "Media sull'elenco attivo"
                    : "Nessun dato disponibile";
            }

            TimeSpan averageRuntime = TimeSpan.Zero;
            if (snapshot != null && snapshot.Count > 0)
            {
                var runtimes = snapshot
                    .Select(job => DateTime.Now - job.StartTime)
                    .Where(span => span > TimeSpan.Zero)
                    .ToList();

                if (runtimes.Count > 0)
                    averageRuntime = TimeSpan.FromTicks((long)runtimes.Average(span => span.Ticks));
            }

            var runtimeLabel = contentPanel.Controls.Find("InCorsoAverageRuntimeLabel", true).FirstOrDefault() as Label;
            var runtimeDescriptionLabel = contentPanel.Controls.Find("InCorsoRuntimeDescriptionLabel", true).FirstOrDefault() as Label;

            if (runtimeLabel != null)
                runtimeLabel.Text = averageRuntime > TimeSpan.Zero ? FormatTimeSpan(averageRuntime) : "—";

            if (runtimeDescriptionLabel != null)
            {
                runtimeDescriptionLabel.Text = snapshot != null && snapshot.Count > 0
                    ? "Durata media attuale"
                    : "Durata non disponibile";
            }
        }
        /// <summary>
        /// Imposta in corso log text usando i parametri passati.
        /// </summary>
        private void SetInCorsoLogText(string message)
        {
            var logTextBox = contentPanel.Controls.Find("InCorsoLogTextBox", true).FirstOrDefault() as RichTextBox;
            if (logTextBox == null)
                return;

            ResetLogHighlight(logTextBox, ref _inCorsoLogHighlightStart, ref _inCorsoLogHighlightLength);

            logTextBox.SuspendLayout();
            logTextBox.Text = message ?? string.Empty;
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionLength = 0;
            logTextBox.ScrollToCaret();
            logTextBox.ResumeLayout();
            HighlightInCorsoLogSearchTerm();
        }

        /// <summary>
        /// Esegue la logica highlight in corso log search term senza cambiare il comportamento.
        /// </summary>
        private void HighlightInCorsoLogSearchTerm()
        {
            HighlightLogSearchTerm("InCorsoLogTextBox", "InCorsoLogSearchBox", _inCorsoLogSearchTerm, ref _inCorsoLogHighlightStart, ref _inCorsoLogHighlightLength);
        }

        /// <summary>
        /// Esegue la logica highlight terminate log search term senza cambiare il comportamento.
        /// </summary>
        private void HighlightTerminateLogSearchTerm()
        {
            HighlightLogSearchTerm("TerminateLogTextBox", "TerminateLogSearchBox", _terminateLogSearchTerm, ref _terminateLogHighlightStart, ref _terminateLogHighlightLength);
        }

        /// <summary>
        /// Reimposta log highlight ad un valore pulito.
        /// </summary>
        private void ResetLogHighlight(RichTextBox logTextBox, ref int highlightStart, ref int highlightLength)
        {
            if (logTextBox == null)
                return;

            logTextBox.SuspendLayout();
            if (logTextBox.TextLength > 0)
            {
                logTextBox.SelectAll();
                logTextBox.SelectionBackColor = logTextBox.BackColor;
                logTextBox.SelectionColor = logTextBox.ForeColor;
            }
            logTextBox.Select(logTextBox.TextLength, 0);
            logTextBox.ResumeLayout();

            highlightStart = -1;
            highlightLength = 0;
        }

        /// <summary>
        /// Esegue la logica highlight log search term senza cambiare il comportamento.
        /// </summary>
        private void HighlightLogSearchTerm(string logTextBoxName, string searchBoxName, string term, ref int highlightStart, ref int highlightLength)
        {
            var logTextBox = contentPanel.Controls.Find(logTextBoxName, true).FirstOrDefault() as RichTextBox;
            if (logTextBox == null)
                return;

            var searchBox = contentPanel.Controls.Find(searchBoxName, true).FirstOrDefault() as TextBox;

            ResetLogHighlight(logTextBox, ref highlightStart, ref highlightLength);

            if (string.IsNullOrWhiteSpace(term))
            {
                if (searchBox != null && searchBox.ForeColor != Colors.TextPlaceholder)
                    searchBox.ForeColor = Colors.TextPrimary;
                logTextBox.ScrollToCaret();
                return;
            }

            var text = logTextBox.Text ?? string.Empty;
            if (text.Length == 0)
            {
                if (searchBox != null && searchBox.ForeColor != Colors.TextPlaceholder)
                    searchBox.ForeColor = Colors.Error;
                return;
            }

            var highlightColor = Color.FromArgb(220, Colors.Error);
            var highlightTextColor = Color.Black;
            int firstMatchStart = -1;
            int firstMatchLength = 0;
            int lineStart = 0;

            while (lineStart < text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                    lineEnd = text.Length;
                int lineLength = Math.Max(0, lineEnd - lineStart);

                if (lineLength > 0)
                {
                    var line = text.Substring(lineStart, lineLength);
                    if (line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        logTextBox.Select(lineStart, lineLength);
                        logTextBox.SelectionBackColor = highlightColor;
                        logTextBox.SelectionColor = highlightTextColor;

                        if (firstMatchStart == -1)
                        {
                            firstMatchStart = lineStart;
                            firstMatchLength = lineLength;
                        }
                    }
                }

                lineStart = lineEnd + 1;
            }

            if (firstMatchStart >= 0)
            {
                highlightStart = firstMatchStart;
                highlightLength = firstMatchLength;
                logTextBox.Select(firstMatchStart, firstMatchLength);
                logTextBox.ScrollToCaret();

                if (searchBox != null && searchBox.ForeColor != Colors.TextPlaceholder)
                    searchBox.ForeColor = Colors.TextPrimary;
            }
            else
            {
                if (searchBox != null && searchBox.ForeColor != Colors.TextPlaceholder)
                    searchBox.ForeColor = Colors.Error;
                logTextBox.Select(logTextBox.TextLength, 0);
            }
        }
        /// <summary>
        /// Si assicura che la parte log refresh timer sia pronta prima di procedere.
        /// </summary>
        private void EnsureLogRefreshTimer()
        {
            if (_logRefreshTimer != null)
                return;

            _logRefreshTimer = new Timer
            {
                Interval = 2000
            };
            _logRefreshTimer.Tick += (s, e) => RefreshLogContent();
        }
        /// <summary>
        /// Esegue la logica bind log to job senza cambiare il comportamento.
        /// </summary>
        private void BindLogToJob(ExportJob job)
        {
            if (job == null)
            {
                StopLogRefresh(string.Empty);
                return;
            }

            var logPath = ResolveLogPath(job);
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                EnsureLogRefreshTimer();
                _logRefreshTimer?.Stop();
                _currentLogFile = logPath;
                _currentLogSnapshot = null;
                RefreshLogContent();
                _logRefreshTimer?.Start();
            }
            else
            {
                StopLogRefresh("Log non disponibile per questa archiviazione.");
            }
        }
        /// <summary>
        /// Risolve log path senza interventi manuali.
        /// </summary>
        private string ResolveLogPath(ExportJob job)
        {
            if (job == null)
                return null;

            if (_logIndexService != null && _logIndexService.TryGetLogPath(job.Id, out var indexedPath) && File.Exists(indexedPath))
            {
                job.LogFilePath = indexedPath;
                return indexedPath;
            }

            if (!string.IsNullOrWhiteSpace(job.LogFilePath) && File.Exists(job.LogFilePath))
                return job.LogFilePath;

            var candidateFolders = new List<string>();
            if (!string.IsNullOrEmpty(_logDirectoryPath))
                candidateFolders.Add(_logDirectoryPath);
            if (!string.IsNullOrEmpty(_legacyLogDirectoryPath))
                candidateFolders.Add(_legacyLogDirectoryPath);
            candidateFolders.Add(Path.GetTempPath());

            foreach (var folder in candidateFolders.Distinct().Where(Directory.Exists))
            {
                try
                {
                    var files = Directory.GetFiles(folder, $"{job.Id}*.log", SearchOption.TopDirectoryOnly);
                    var match = files
                        .OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(match))
                    {
                        job.LogFilePath = match;
                        return match;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ResolveLogPath(): {ex.Message}");
                }
            }

            return null;
        }
        /// <summary>
        /// Ferma log refresh e libera le risorse.
        /// </summary>
        private void StopLogRefresh(string message)
        {
            _logRefreshTimer?.Stop();
            _currentLogFile = null;
            _currentLogSnapshot = null;
            SetInCorsoLogText(string.IsNullOrWhiteSpace(message)
                ? "Seleziona un'archiviazione attiva per visualizzare il log in diretta."
                : message);
        }
        /// <summary>
        /// Rinfresca log content per avere info fresche.
        /// </summary>
        private void RefreshLogContent()
        {
            if (string.IsNullOrEmpty(_currentLogFile))
                return;

            try
            {
                if (!File.Exists(_currentLogFile))
                {
                    StopLogRefresh(string.Empty);
                    return;
                }

                string content;
                using (var stream = new FileStream(_currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    content = reader.ReadToEnd();
                }

                var lines = content.Replace("\r\n", "\n").Split('\n');
                if (lines.Length > 200)
                {
                    lines = lines.Skip(lines.Length - 200).ToArray();
                }

                var snapshot = string.Join(System.Environment.NewLine, lines);
                if (!string.Equals(snapshot, _currentLogSnapshot, StringComparison.Ordinal))
                {
                    _currentLogSnapshot = snapshot;
                    SetInCorsoLogText(snapshot);
                }
            }
            catch (Exception exFile)
            {
                Trace.WriteLine($"RefreshLogContent(): {exFile.Message}");
                Debug.WriteLine($"RefreshLogContent(): {exFile.Message}");
            }
        }

        /// <summary>
        /// Disegna in corso details sul form.
        /// </summary>
        private void RenderInCorsoDetails(ExportJob job)
        {
            _currentInCorsoDetailJob = job;
            var detailPanel = contentPanel.Controls.Find("InCorsoDetailPanel", true).FirstOrDefault() as Panel;
            if (detailPanel == null)
                return;

            var placeholder = detailPanel.Controls.Find("InCorsoDetailPlaceholder", true).FirstOrDefault();
            var content = detailPanel.Controls.Find("InCorsoDetailContent", true).FirstOrDefault() as TableLayoutPanel;
            if (placeholder == null || content == null)
                return;

            if (job == null)
            {
                content.Visible = false;
                placeholder.Visible = true;

                var placeholderLabel = placeholder.Controls.Find("InCorsoDetailPlaceholderLabel", true).FirstOrDefault() as Label;
                if (placeholderLabel != null)
                {
                    placeholderLabel.Text = _inCorsoSnapshot.Count == 0
                        ? "Non ci sono archiviazioni attive in questo momento"
                        : "Seleziona un'archiviazione per visualizzare i dettagli";
                }

                var descriptionLabel = content.Controls.Find("InCorsoDetailDescription", true).FirstOrDefault() as Label;
                if (descriptionLabel != null)
                {
                    descriptionLabel.Text = "Seleziona una riga dell'elenco per consultare i dettagli completi dell'archiviazione in corso.";
                }

                BindLogToJob(null);
                return;
            }

            placeholder.Visible = false;
            content.Visible = true;

            var activeDescriptionLabel = content.Controls.Find("InCorsoDetailDescription", true).FirstOrDefault() as Label;
            if (activeDescriptionLabel != null)
            {
                activeDescriptionLabel.Text = "Panoramica aggiornata dell'archiviazione selezionata dalla lista a sinistra.";
            }

            SetLabelText("InCorsoDetailIdValue", string.IsNullOrWhiteSpace(job.Id) ? "N/D" : job.Id);
            SetLabelText("InCorsoDetailWorkIdValue", string.IsNullOrWhiteSpace(job.IdLavoro) ? "N/D" : job.IdLavoro);
            SetLabelText("InCorsoDetailCameraValue", string.IsNullOrWhiteSpace(job.CameraName) ? "N/D" : job.CameraName);
            SetLabelText("InCorsoDetailPeriodoValue", FormatPeriodo(job.StartTime, job.EndTime));
            var ppDisplay = string.IsNullOrWhiteSpace(job.ProcedimentoPenale) ? "N/A" : job.ProcedimentoPenale;
            var ritDisplay = string.IsNullOrWhiteSpace(job.RitSpec) ? "N/A" : job.RitSpec;
            SetLabelText("InCorsoDetailPPValue", $"{ppDisplay} • {ritDisplay}");
            SetLabelText("InCorsoDetailMagistratoValue", string.IsNullOrWhiteSpace(job.Magistrato) ? "N/D" : job.Magistrato);
            SetLabelText("InCorsoDetailProcuraValue", string.IsNullOrWhiteSpace(job.Procura) ? "N/D" : job.Procura);
            SetLabelText("InCorsoDetailTargetValue", string.IsNullOrWhiteSpace(job.Target) ? "N/D" : job.Target);
            SetLabelText("InCorsoDetailPathValue", string.IsNullOrWhiteSpace(job.Path) ? "N/A" : job.Path, job.Path);
            SetLabelText("InCorsoDetailPasswordValue", string.IsNullOrWhiteSpace(job.Password) ? "Password non impostata" : job.Password);

            var progressBar = detailPanel.Controls.Find("InCorsoProgressBar", true).FirstOrDefault() as ProgressBar;
            var progressLabel = detailPanel.Controls.Find("InCorsoProgressValue", true).FirstOrDefault() as Label;
            int progressValue = Math.Max(0, Math.Min(100, job.Progress));

            if (progressBar != null)
                progressBar.Value = progressValue;

            if (progressLabel != null)
            {
                progressLabel.Text = $"{progressValue}%";
                progressLabel.ForeColor = Colors.Success;
            }

            var chipsPanel = content.Controls.Find("InCorsoStatusChips", true).FirstOrDefault() as FlowLayoutPanel;
            if (chipsPanel != null)
            {
                chipsPanel.Controls.Clear();
                // Rimossi chip "2 in corso" e "NAS Principale" - mostriamo solo errori se presenti
                if (job.Errors != null && job.Errors.Count > 0)
                    chipsPanel.Controls.Add(CreateStatusChip($"Errori: {job.Errors.Count}", Colors.Error));
            }

            var footer = content.Controls.Find("InCorsoDetailFooter", true).FirstOrDefault() as Label;
            if (footer != null)
            {
                var segments = new List<string>
                {
                    $"Creato il {job.CreatedAt:dd/MM/yyyy HH:mm}"
                };

                TimeSpan? elapsed = null;
                if (job.StartTime != default)
                {
                    var diff = DateTime.Now - job.StartTime;
                    if (diff < TimeSpan.Zero)
                        diff = TimeSpan.Zero;
                    elapsed = diff;
                    segments.Add($"In corso da {FormatTimeSpan(diff)}");
                }

                var remaining = elapsed.HasValue ? CalculateRemainingTime(elapsed.Value, progressValue) : null;
                if (remaining.HasValue)
                    segments.Add($"Tempo rimanente stimato: {FormatTimeSpan(remaining.Value)}");

                if (job.LastLoggedProgress >= 0)
                    segments.Add($"Ultimo log: {job.LastLoggedProgress}%");

                if (!string.IsNullOrWhiteSpace(job.LogFilePath))
                    segments.Add("Log disponibile");

                var normalizedSegments = segments
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .Select(segment => segment.Trim())
                    .ToArray();

                ApplyInCorsoDetailFooterText(detailPanel, footer, normalizedSegments);
            }

            BindLogToJob(job);
        }

        /// <summary>
        /// Applica in corso detail footer text alle impostazioni correnti.
        /// </summary>
        private void ApplyInCorsoDetailFooterText(Panel detailPanel, Label footer, string[] segments)
        {
            if (detailPanel == null || footer == null)
                return;

            footer.UseMnemonic = false;
            footer.AutoEllipsis = false;
            footer.TextAlign = ContentAlignment.TopLeft;
            footer.AutoSize = true;

            var cleaned = segments?
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .Select(segment => segment.Trim())
                .ToArray() ?? Array.Empty<string>();

            footer.Tag = cleaned.Length > 0 ? cleaned : Array.Empty<string>();
            footer.Text = cleaned.Length > 0
                ? string.Join("  •  ", cleaned)
                : string.Empty;

            var footerContainer = footer.Parent as Control;
            int footerWidth = footerContainer?.ClientSize.Width ?? detailPanel.ClientSize.Width;
            footer.MaximumSize = footerWidth > 0
                ? new Size(Math.Max(0, footerWidth - Spacing.LG), 0)
                : new Size(int.MaxValue, 0);
        }

        /// <summary>
        /// Aggiorna in corso detail footer layout e mantiene lo stato coerente.
        /// </summary>
        private void UpdateInCorsoDetailFooterLayout(Panel detailPanel)
        {
            if (detailPanel == null)
                return;

            var footer = detailPanel.Controls.Find("InCorsoDetailFooter", true).FirstOrDefault() as Label;
            if (footer == null)
                return;

            var segments = footer.Tag as string[];
            ApplyInCorsoDetailFooterText(detailPanel, footer, segments);
        }

        /// <summary>
        /// Aggiorna job progress ui e mantiene lo stato coerente.
        /// </summary>
        private void UpdateJobProgressUI(ExportJob job)
        {
            if (job == null)
                return;

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => UpdateJobProgressUI(job)));
                return;
            }

            var listView = contentPanel.Controls.Find("InCorsoListView", true).FirstOrDefault() as ListView;
            if (listView != null)
            {
                foreach (ListViewItem item in listView.Items)
                {
                    if (item.Tag == job)
                    {
                        int progressColumnIndex = 7;
                        if (item.SubItems.Count > progressColumnIndex)
                            item.SubItems[progressColumnIndex].Text = $"{Math.Max(0, Math.Min(100, job.Progress))}%";
                        break;
                    }
                }
            }

            if (_currentInCorsoDetailJob == job)
            {
                var progressBar = contentPanel.Controls.Find("InCorsoProgressBar", true).FirstOrDefault() as ProgressBar;
                if (progressBar != null)
                    progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, job.Progress));

                var progressLabel = contentPanel.Controls.Find("InCorsoProgressValue", true).FirstOrDefault() as Label;
                if (progressLabel != null)
                    progressLabel.Text = $"{Math.Max(0, Math.Min(100, job.Progress))}%";
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo copy in corso to launch per tenere la UI reattiva.
        /// </summary>
        private void CopyInCorsoToLaunch_Click(object sender, EventArgs e)
        {
            var job = _currentInCorsoDetailJob;
            if (job == null)
                return;

            var info = CreateArchiviazioneInfo(job);
            if (info == null)
                return;

            ApplyConfigurationToLaunch(info, remember: true);
        }

        /// <summary>
        /// Esegue la logica copy in corso details to clipboard senza cambiare il comportamento.
        /// </summary>
        private void CopyInCorsoDetailsToClipboard(object sender, EventArgs e)
        {
            var job = _currentInCorsoDetailJob;
            if (job == null)
                return;

            var detailPanel = contentPanel.Controls.Find("InCorsoDetailPanel", true).FirstOrDefault() as Panel;
            if (detailPanel == null)
                return;

            var detailContent = detailPanel.Controls.Find("InCorsoDetailContent", true).FirstOrDefault() as TableLayoutPanel;
            if (detailContent == null || !detailContent.Visible)
                return;

            var builder = new StringBuilder();

            var progressLabel = detailContent.Controls.Find("InCorsoProgressValue", true).FirstOrDefault() as Label;
            if (progressLabel != null)
                builder.AppendLine($"Avanzamento: {progressLabel.Text}");

            var fieldMap = new (string Title, string ControlName)[]
            {
                ("ID JOB", "InCorsoDetailIdValue"),
                ("ID Lavoro", "InCorsoDetailWorkIdValue"),
                ("Telecamera", "InCorsoDetailCameraValue"),
                ("Periodo", "InCorsoDetailPeriodoValue"),
                ("P.P. e RIT/SPEC", "InCorsoDetailPPValue"),
                ("PM", "InCorsoDetailMagistratoValue"),
                ("Procura", "InCorsoDetailProcuraValue"),
                ("Target", "InCorsoDetailTargetValue"),
                ("Percorso", "InCorsoDetailPathValue"),
                ("Password", "InCorsoDetailPasswordValue")
            };

            foreach (var (title, controlName) in fieldMap)
            {
                if (detailContent.Controls.Find(controlName, true).FirstOrDefault() is Label valueLabel)
                {
                    var value = string.IsNullOrWhiteSpace(valueLabel.Text) ? "—" : valueLabel.Text.Trim();
                    builder.AppendLine($"{title}: {value}");
                }
            }

            var footer = detailContent.Controls.Find("InCorsoDetailFooter", true).FirstOrDefault() as Label;
            if (footer != null && !string.IsNullOrWhiteSpace(footer.Text))
            {
                builder.AppendLine();
                builder.AppendLine(footer.Text.Trim());
            }

            var description = detailContent.Controls.Find("InCorsoDetailDescription", true).FirstOrDefault() as Label;
            if (description != null && !string.IsNullOrWhiteSpace(description.Text))
            {
                builder.AppendLine();
                builder.AppendLine(description.Text.Trim());
            }

            try
            {
                Clipboard.SetText(builder.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CopyInCorsoDetailsToClipboard(): {ex.Message}");
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo copy terminate to launch per tenere la UI reattiva.
        /// </summary>
        private void CopyTerminateToLaunch_Click(object sender, EventArgs e)
        {
            var info = _selectedTerminateInfo;
            if (info == null)
                return;

            ApplyConfigurationToLaunch(info, remember: true);
        }

        /// <summary>
        /// Esegue la logica copy terminate details to clipboard senza cambiare il comportamento.
        /// </summary>
        private void CopyTerminateDetailsToClipboard(object sender, EventArgs e)
        {
            var info = _selectedTerminateInfo;
            if (info == null)
                return;

            var builder = new StringBuilder();
            builder.AppendLine($"ID JOB: {info.Id ?? "N/D"}");
            builder.AppendLine($"Telecamera: {info.Telecamera ?? "N/D"}");
            builder.AppendLine($"Periodo: {FormatPeriodo(info.PeriodoInizio, info.PeriodoFine)}");

            var durata = !string.IsNullOrWhiteSpace(info.Durata) ? info.Durata : ResolveArchiveDuration(info);
            builder.AppendLine($"Durata: {(!string.IsNullOrWhiteSpace(durata) ? durata : "—")}");

            var stato = NormalizeStatus(info.Stato);
            builder.AppendLine($"Stato finale: {stato}");

            var dimensione = !string.IsNullOrWhiteSpace(info.Dimensione) ? info.Dimensione : FormatSize(ResolveArchiveSize(info));
            builder.AppendLine($"Dimensione: {(!string.IsNullOrWhiteSpace(dimensione) ? dimensione : "—")}");

            builder.AppendLine($"Completata il: {(info.DataCompletamento == default ? "—" : info.DataCompletamento.ToString("dd/MM/yyyy HH:mm"))}");
            builder.AppendLine($"Procedimento: {info.ProcedimentoPenale ?? "—"}");
            builder.AppendLine($"PM: {info.Magistrato ?? "—"}");
            builder.AppendLine($"RIT/SPEC: {info.RitSpec ?? "—"}");
            builder.AppendLine($"Procura: {info.Procura ?? "—"}");
            builder.AppendLine($"ID Lavoro: {info.IdLavoro ?? "—"}");
            builder.AppendLine($"Target: {info.Target ?? "—"}");
            builder.AppendLine($"Server: {info.ServerAddress ?? "—"}");
            builder.AppendLine($"Percorso: {info.Cartella ?? "—"}");
            builder.AppendLine($"Password: {info.Password ?? "—"}");

            try
            {
                Clipboard.SetText(builder.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CopyTerminateDetailsToClipboard(): {ex.Message}");
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo create sit document per tenere la UI reattiva.
        /// </summary>
        private void CreateSitDocument_Click(object sender, EventArgs e)
        {
            var info = _selectedTerminateInfo;
            if (info == null)
            {
                ShowInfo("Crea documento SIT", "Seleziona un'archiviazione completata per generare il documento SIT.");
                return;
            }

            try
            {
                CreateSitDocumentFromTemplate(info);
            }
            catch (Exception ex)
            {
                ShowError("Crea documento SIT", $"Si è verificato un errore durante la preparazione del documento SIT.\nDettagli: {ex.Message}");
            }
        }

        /// <summary>
        /// Crea sit document from template al volo.
        /// </summary>
        private void CreateSitDocumentFromTemplate(ArchiviazioneInfo info)
        {
            string templatePath = SitTemplateFile;
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
            {
                const string legacyTemplateName = "SIT_ARCH_DT_VIDEO_.Procura._.ProcedimentoPenale._.RITSPEC._.Target._.IDlavoro..docx";
                templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, legacyTemplateName);
            }

            if (!File.Exists(templatePath))
            {
                ShowWarning("Template SIT non trovato", $"Il file di template richiesto non è disponibile.\nPercorso atteso:\n{templatePath}");
                return;
            }

            var destinationDirectory = info.Cartella;
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                ShowWarning("Cartella non disponibile", "L'archiviazione selezionata non ha un percorso di destinazione valido.");
                return;
            }

            try
            {
                Directory.CreateDirectory(destinationDirectory);
            }
            catch (Exception ex)
            {
                ShowError("Accesso cartella non riuscito", $"Non è stato possibile accedere o creare la cartella di destinazione:\n{destinationDirectory}\n\nDettagli: {ex.Message}");
                    return;
            }

            var trimmedDirectory = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmedDirectory);
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = info.IdLavoro ?? info.Id ?? "EXPORT";
            string baseFileName = $"SIT_ARCH_DT_VIDEO_{SanitizeForFileName(folderName)}";
            int attempt = 1;
            string destinationPath;
            do
            {
                string suffix = attempt == 1 ? string.Empty : $"_{attempt:00}";
                destinationPath = Path.Combine(destinationDirectory, baseFileName + suffix + ".docx");
                attempt++;
            }
            while (File.Exists(destinationPath));

            try
            {
                File.Copy(ToExtendedPath(templatePath), ToExtendedPath(destinationPath), true);
            }
            catch (Exception ex)
            {
                ShowError("Copia template non riuscita", $"Si è verificato un problema durante la copia del template SIT.\nDettagli: {ex.Message}");
                return;
            }

            try
            {
                ApplySitTemplateReplacements(destinationPath, info);
            }
            catch (Exception ex)
            {
                ShowError("Compilazione template non riuscita", $"Non è stato possibile completare i dati del modello SIT.\nDettagli: {ex.Message}");
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"/select,\"{destinationPath}\"");
            }
            catch { }

            ShowSuccess("Documento SIT generato", $"Il documento è stato creato correttamente.\nPercorso: {destinationPath}");
        }

        /// <summary>
        /// Applica sit template replacements alle impostazioni correnti.
        /// </summary>
        private void ApplySitTemplateReplacements(string filePath, ArchiviazioneInfo info)
        {
            var replacements = BuildSitReplacementMap(info);

            using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Update))
            {
                var xmlEntries = archive.Entries
                    .Where(entry => entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase)
                                     && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var entry in xmlEntries)
                {
                    string content;
                    using (var readerStream = entry.Open())
                    using (var reader = new StreamReader(readerStream, Encoding.UTF8, true, 1024, false))
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
                            string sanitizedValue = rawValue.Replace("\r\n", "\n").Replace("\r", "\n");
                            string escapedValue = System.Security.SecurityElement.Escape(sanitizedValue).Replace("\n", "&#x0A;");
                            content = content.Replace(placeholder, escapedValue);
                            modified = true;
                        }
                    }

                    if (!modified)
                        continue;

                    using (var writerStream = entry.Open())
                    {
                        writerStream.SetLength(0);
                        using (var writer = new StreamWriter(writerStream, Encoding.UTF8))
                        {
                            writer.Write(content);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Compone sit replacement map pronto all'uso.
        /// </summary>
        private Dictionary<string, string> BuildSitReplacementMap(ArchiviazioneInfo info)
        {
            string FormatDimensione(ArchiviazioneInfo data)
            {
                if (!string.IsNullOrWhiteSpace(data.Dimensione))
                    return data.Dimensione;
                var size = ResolveArchiveSize(data);
                return size > 0 ? FormatSize(size) : "N/D";
            }

            string ResolveDuration(ArchiviazioneInfo data)
            {
                if (!string.IsNullOrWhiteSpace(data.Durata))
                    return data.Durata;
                return ResolveArchiveDuration(data);
            }

            string periodo = FormatPeriodo(info.PeriodoInizio, info.PeriodoFine);
            string completataIl = info.DataCompletamento == default
                ? "N/D"
                : info.DataCompletamento.ToString("dd/MM/yyyy HH:mm");

            string procura = FirstNonEmpty(
                info.Procura,
                TryGetMetadataValue(info, "Procura"),
                info.Magistrato,
                "N/D");

            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void SetReplacement(string key, string value)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;
                replacements[key] = value ?? string.Empty;
            }

            string idValue = FirstNonEmpty(info.IdLavoro, info.Id, "N/D");
            string periodoValue = FirstNonEmpty(periodo, "N/D");
            string durataValue = FirstNonEmpty(ResolveDuration(info), "N/D");
            string statoValue = FirstNonEmpty(NormalizeStatus(info.Stato), "N/D");
            string dimensioneValue = FormatDimensione(info);
            string cartellaValue = FirstNonEmpty(info.Cartella, "N/D");
            string procedimentoValue = FirstNonEmpty(info.ProcedimentoPenale, "N/D");
            string ritSpecValue = FirstNonEmpty(info.RitSpec, "N/D");
            string targetValue = FirstNonEmpty(info.Target, "N/D");
            string magistratoValue = FirstNonEmpty(info.Magistrato, "N/D");
            string telecameraValue = FirstNonEmpty(info.Telecamera, "N/D");
            string passwordValue = FirstNonEmpty(info.Password, "—");
            string serverValue = FirstNonEmpty(info.ServerAddress, "N/D");
            string noteValue = FirstNonEmpty(info.Note, "");
            string destinazioneValue = FirstNonEmpty(info.Target, info.Cartella, "N/D");

            // Segnaposto principali del template SIT
            SetReplacement("ID lavoro", idValue);
            SetReplacement("Magistrato", magistratoValue);
            SetReplacement("Password", passwordValue);
            SetReplacement("Periodo", periodoValue);
            SetReplacement("Procedimento Penale", procedimentoValue);
            SetReplacement("Procura", procura);
            SetReplacement("RIT/SPEC", ritSpecValue);
            SetReplacement("Target", targetValue);

            // Segnaposto aggiuntivi/supporto (per versioni future del template)
            SetReplacement("ID", idValue);
            SetReplacement("IDlavoro", idValue);
            SetReplacement("Telecamera", telecameraValue);
            SetReplacement("Durata", durataValue);
            SetReplacement("Stato", statoValue);
            SetReplacement("Dimensione", dimensioneValue);
            SetReplacement("Cartella", cartellaValue);
            SetReplacement("CompletataIl", completataIl);
            SetReplacement("ProcedimentoPenale", procedimentoValue);
            SetReplacement("RITSPEC", ritSpecValue);
            SetReplacement("Server", serverValue);
            SetReplacement("Note", noteValue);
            SetReplacement("DataDocumento", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            SetReplacement("Destinazione", destinazioneValue);

            return replacements;
        }

        /// <summary>
        /// Esegue la logica try get metadata value senza cambiare il comportamento.
        /// </summary>
        private static string TryGetMetadataValue(ArchiviazioneInfo info, string key)
        {
            if (info?.Metadata == null || string.IsNullOrEmpty(key))
                return null;

            foreach (var kvp in info.Metadata)
            {
                if (kvp.Key != null && kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value?.ToString();
            }

            return null;
        }

        /// <summary>
        /// Esegue la logica first non empty senza cambiare il comportamento.
        /// </summary>
        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        /// <summary>
        /// Esegue la logica sanitize file name senza cambiare il comportamento.
        /// </summary>
        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Documento";

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (var c in value)
            {
                builder.Append(invalidChars.Contains(c) ? '_' : c);
            }

            var sanitized = builder.ToString().Trim();
            return string.IsNullOrEmpty(sanitized) ? "Documento" : sanitized;
        }

        /// <summary>
        /// Si assicura che la parte job counter initialized sia pronta prima di procedere.
        /// </summary>
        private void EnsureJobCounterInitialized()
        {
            if (_jobCounterInitialized)
                return;

            if (!int.TryParse(LoadUserSetting("JobCounterValue"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _jobCounterValue) ||
                _jobCounterValue < 0 || _jobCounterValue > 999)
            {
                _jobCounterValue = 0;
            }

            if (!int.TryParse(LoadUserSetting("JobCounterSuffixIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _jobCounterSuffixIndex))
            {
                _jobCounterSuffixIndex = -1;
            }

            _jobCounterInitialized = true;
        }

        /// <summary>
        /// Esegue la logica advance job counter senza cambiare il comportamento.
        /// </summary>
        private void AdvanceJobCounter()
        {
            EnsureJobCounterInitialized();

            if (_jobCounterValue >= 999)
            {
                _jobCounterValue = 1;
                _jobCounterSuffixIndex++;
            }
            else
            {
                _jobCounterValue++;
            }

            SaveUserSetting("JobCounterValue", _jobCounterValue.ToString(CultureInfo.InvariantCulture));
            SaveUserSetting("JobCounterSuffixIndex", _jobCounterSuffixIndex.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Esegue la logica format job counter suffix senza cambiare il comportamento.
        /// </summary>
        private static string FormatJobCounterSuffix(int suffixIndex)
        {
            if (suffixIndex < 0)
                return string.Empty;

            var builder = new StringBuilder();
            int index = suffixIndex;

            while (index >= 0)
            {
                int remainder = index % 26;
                builder.Insert(0, (char)('a' + remainder));
                index = index / 26 - 1;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Esegue la logica generate job id senza cambiare il comportamento.
        /// </summary>
        private string GenerateJobId(string idLavoro)
        {
            _ = idLavoro; // Il prefisso non è più utilizzato ma manteniamo la firma per compatibilità
            AdvanceJobCounter();
            string suffix = FormatJobCounterSuffix(_jobCounterSuffixIndex);
            return $"{_jobCounterValue}{suffix}";
        }

        /// <summary>
        /// Imposta label text usando i parametri passati.
        /// </summary>
        private void SetLabelText(string controlName, string text, string tooltip = null)
        {
            if (string.IsNullOrEmpty(controlName))
                return;

            var label = contentPanel.Controls.Find(controlName, true).FirstOrDefault() as Label;
            if (label == null)
                return;

            label.Text = text ?? string.Empty;

            if (_toolTip != null)
            {
                if (!string.IsNullOrEmpty(tooltip))
                    _toolTip.SetToolTip(label, tooltip);
                else
                    _toolTip.SetToolTip(label, null);
            }
        }

        /// <summary>
        /// Genera un riepilogo testuale con i principali dettagli del job.
        /// </summary>
        private string BuildArchiveNotificationDetails(ExportJob job)
        {
            if (job == null)
                return string.Empty;

            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(job.Id))
                builder.AppendLine($"ID: {job.Id}");

            if (!string.IsNullOrWhiteSpace(job.CameraName))
                builder.AppendLine($"Sorgente: {job.CameraName}");

            builder.AppendLine($"Periodo: {FormatPeriodo(job.StartTime, job.EndTime)}");

            if (!string.IsNullOrWhiteSpace(job.Path))
                builder.AppendLine($"Cartella: {job.Path}");

            if (!string.IsNullOrWhiteSpace(job.ServerAddress))
                builder.AppendLine($"Server: {job.ServerAddress}");

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Mostra un avviso all'avvio dell'archiviazione.
        /// </summary>
        private void NotifyArchiveStart(ExportJob job)
        {
            if (job == null)
                return;

            string details = BuildArchiveNotificationDetails(job);
            if (!string.IsNullOrWhiteSpace(details))
                ShowInfo("Archiviazione avviata", details);
        }

        /// <summary>
        /// Mostra un avviso con l'esito di completamento dell'archiviazione.
        /// </summary>
        private void NotifyArchiveCompletion(ExportJob job, bool success, string additionalText = null)
        {
            if (job == null)
                return;

            var builder = new StringBuilder();
            string statusText = NormalizeStatus(job.Status);
            builder.AppendLine($"Esito: {statusText}");

            string details = BuildArchiveNotificationDetails(job);
            if (!string.IsNullOrWhiteSpace(details))
                builder.AppendLine(details);

            if (!string.IsNullOrWhiteSpace(additionalText))
                builder.AppendLine(additionalText.Trim());

            string message = builder.ToString().Trim();
            if (success)
                ShowSuccess("Archiviazione terminata", message);
            else
                ShowError("Archiviazione non riuscita", message);
        }

        /// <summary>
        /// Esegue la logica format periodo senza cambiare il comportamento.
        /// </summary>
        private string FormatPeriodo(DateTime start, DateTime end)
        {
            if (start == default && end == default)
                return "N/D";

            if (end < start)
                end = start;

            var startText = start == default ? "N/D" : start.ToString("dd/MM/yyyy HH:mm");
            var endText = end == default ? "In corso" : end.ToString("dd/MM/yyyy HH:mm");

            return $"{startText} → {endText}";
        }

        /// <summary>
        /// Esegue la logica format time span senza cambiare il comportamento.
        /// </summary>
        private string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return span.ToString("hh\\:mm\\:ss");

            return span.ToString("mm\\:ss");
        }

        /// <summary>
        /// Calcola remaining time per fornirlo agli altri step.
        /// </summary>
        private TimeSpan? CalculateRemainingTime(TimeSpan elapsed, int progress)
        {
            if (progress <= 0 || progress >= 100)
                return null;

            if (elapsed.TotalSeconds < 1)
                return null;

            var totalSeconds = elapsed.TotalSeconds * 100.0 / progress;
            if (double.IsInfinity(totalSeconds) || double.IsNaN(totalSeconds))
                return null;

            var remainingSeconds = totalSeconds - elapsed.TotalSeconds;
            if (remainingSeconds <= 0)
                return TimeSpan.Zero;

            return TimeSpan.FromSeconds(remainingSeconds);
        }

        /// <summary>
        /// Esegue la logica normalize status senza cambiare il comportamento.
        /// </summary>
        private string NormalizeStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "IN CORSO";

            var textInfo = CultureInfo.CurrentCulture.TextInfo;
            return textInfo.ToTitleCase(status.ToLowerInvariant());
        }

        /// <summary>
        /// Restituisce status color gia pronto.
        /// </summary>
        private Color GetStatusColor(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Colors.Secondary;

            var normalized = status.ToLowerInvariant();

            if (normalized.Contains("erro"))
                return Colors.Error;
            if (normalized.Contains("paus") || normalized.Contains("attesa"))
                return Colors.Warning;
            if (normalized.Contains("complet"))
                return Colors.Success;

            return Colors.Secondary;
        }
        /// <summary>
        /// Crea terminate content al volo.
        /// </summary>
        private void CreateTerminateContent()
        {
            contentPanel.Controls.Clear();
            contentPanel.BackColor = Colors.Background;
            contentPanel.Padding = new Padding(0);

            _terminateSearchTerm = string.Empty;
            _terminateLogSearchTerm = string.Empty;
            _selectedTerminateInfo = null;
            _currentTerminateLogFile = null;

            var mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = Spacing.PagePadding,
                BackColor = Color.Transparent
            };

            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.SectionGap));
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 220F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var uniformPadding = GetUniformSectionPadding();
            int sectionPaddingHorizontal = uniformPadding.Left;
            int sectionPaddingTop = Spacing.AccentContentPadding.Top + uniformPadding.Top;
            int sectionPaddingBottom = Math.Max(0, Spacing.AccentContentPadding.Bottom - 1);

            var actionsRow = CreateTerminateActionsGrid(sectionPaddingHorizontal, sectionPaddingTop, sectionPaddingBottom);

            var listPanel = CreateAccentPanel(Colors.Secondary, $"{Icons.Check} ARCHIVIAZIONI TERMINATE", actionsRow);
            listPanel.Margin = Padding.Empty;

            var listBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(
                    sectionPaddingHorizontal,
                    sectionPaddingTop,
                    sectionPaddingHorizontal,
                    sectionPaddingBottom)
            };

            var listLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var tableContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            var listView = CreateModernListView(
                "TerminateListView",
                new[] { "ID JOB", "ID Lavoro", "Telecamera", "Periodo", "Procedimento", "RIT/SPEC", "Target", "Esito" },
                new[] { 140, 160, 220, 220, 180, 160, 240, 180 }
            );
            listView.FullRowSelect = true;
            listView.MultiSelect = false;
            listView.ItemSelectionChanged += (s, e) =>
            {
                if (!e.IsSelected)
                {
                    if (listView.SelectedItems.Count == 0)
                    {
                        _selectedTerminateInfo = null;
                        RenderTerminateDetails(null);
                        LoadTerminateLogContent(null);
                        UpdateTerminateButtonStates(listView, "TerminateCopyDetailsButton", "TerminateCopyToLaunchButton", "TerminateCreateSitButton", "TerminateDeleteButton");
                        UpdateTerminateLogButtons();
                    }
                    return;
                }

                var selectedInfo = e.Item?.Tag as ArchiviazioneInfo;
                _selectedTerminateInfo = selectedInfo;
                RenderTerminateDetails(selectedInfo);
                LoadTerminateLogContent(selectedInfo);
                UpdateTerminateButtonStates(listView, "TerminateCopyDetailsButton", "TerminateCopyToLaunchButton", "TerminateCreateSitButton", "TerminateDeleteButton");
                UpdateTerminateLogButtons();
            };
            listView.HandleCreated += (s, e) => ApplyTerminateInitialLayout(listView);
            listView.ColumnWidthChanging += (s, e) => OnTerminateColumnWidthChanging(listView, e);
            listView.Resize += (s, e) => AdjustTerminateColumns(listView);

            tableContainer.Controls.Add(listView);
            listLayout.Controls.Add(tableContainer, 0, 0);

            listBody.Controls.Add(listLayout);
            listPanel.Controls.Add(listBody);
            listBody.SendToBack();

            var listWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            listWrapper.Controls.Add(listPanel);
            rootLayout.Controls.Add(listWrapper, 0, 0);
            rootLayout.SetColumnSpan(listWrapper, 3);

            var bottomLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, Spacing.SectionGap, 0, 0),
                Padding = Padding.Empty
            };
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Spacing.SectionGap));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var detailActions = CreateTerminateDetailActionsPanel();
            var detailPanel = CreateAccentPanel(Colors.Success, $"{Icons.Detail} DETTAGLI", detailActions);
            detailPanel.Margin = new Padding(0, 0, 0, Spacing.SM);
            detailPanel.Name = "TerminateDetailPanel";
            int detailPaddingBottom = Math.Max(0, sectionPaddingBottom - Spacing.MD);
            var detailBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(
                    sectionPaddingHorizontal,
                    sectionPaddingTop,
                    sectionPaddingHorizontal,
                    Math.Max(0, detailPaddingBottom - 2))
            };

            var detailPlaceholder = new Panel
            {
                Name = "TerminateDetailPlaceholder",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            var detailPlaceholderLabel = new Label
            {
                Name = "TerminateDetailPlaceholderLabel",
                Text = "Seleziona un'archiviazione terminata per visualizzare i dettagli",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Italic),
                ForeColor = Colors.TextMuted,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            detailPlaceholder.Controls.Add(detailPlaceholderLabel);

            var detailContent = new TableLayoutPanel
            {
                Name = "TerminateDetailContent",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Visible = false
            };
            detailContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            detailContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var infoTable = new TableLayoutPanel
            {
                Name = "TerminateDetailTable",
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                BackColor = Color.Transparent
            };
            infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int rowIndex = 0;
            AddInCorsoDetailRow(infoTable, "ID JOB", "TerminateDetailIdValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "Procura", "TerminateDetailProcuraValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "ID Lavoro", "TerminateDetailWorkIdValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "Telecamera", "TerminateDetailCameraValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "Periodo", "TerminateDetailPeriodoValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "P.P. e RIT/SPEC", "TerminateDetailPPValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "PM", "TerminateDetailMagistratoValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "Target", "TerminateDetailTargetValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "Cartella di esportazione", "TerminateDetailPathValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, "Password", "TerminateDetailPasswordValue", rowIndex++);
            AddInCorsoDetailRow(infoTable, string.Empty, "TerminateDetailSpacerValue", rowIndex++);

            var outcomePanel = new TableLayoutPanel
            {
                Name = "TerminateOutcomePanel",
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, Spacing.SM, 0, 0)
            };
            outcomePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outcomePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var outcomeValueLabel = new Label
            {
                Name = "TerminateOutcomeValue",
                Text = "Esito non disponibile",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXL, FontStyle.Bold),
                ForeColor = Colors.TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Spacing.XXS),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var outcomeDescriptionLabel = new Label
            {
                Name = "TerminateOutcomeDescription",
                Text = "Seleziona un'archiviazione per visualizzare l'esito.",
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Italic),
                ForeColor = Colors.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            outcomePanel.Controls.Add(outcomeValueLabel, 0, 0);
            outcomePanel.Controls.Add(outcomeDescriptionLabel, 0, 1);

            detailContent.Controls.Add(infoTable, 0, 0);
            detailContent.Controls.Add(outcomePanel, 0, 1);

            detailBody.Controls.Add(detailContent);
            detailBody.Controls.Add(detailPlaceholder);
            detailPlaceholder.BringToFront();
            detailPanel.Controls.Add(detailBody);

            var detailWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = Padding.Empty
            };
            detailWrapper.Controls.Add(detailPanel);
            bottomLayout.Controls.Add(detailWrapper, 0, 0);

            var logActions = CreateTerminateLogActionsPanel();
            var logAccentColor = Color.FromArgb(90, 90, 90);
            var logPanel = CreateAccentPanel(logAccentColor, $"{Icons.Log} LOG", logActions);
            logPanel.Margin = new Padding(0, 0, 0, Spacing.SM);
            logPanel.Name = "TerminateLogPanel";
            var logBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(
                    sectionPaddingHorizontal,
                    sectionPaddingTop,
                    sectionPaddingHorizontal,
                    Math.Max(0, sectionPaddingBottom - 1))
            };

            var logLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var logTextBox = new RichTextBox
            {
                Name = "TerminateLogTextBox",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Colors.TextPrimary,
                BorderStyle = BorderStyle.None,
                Font = GetCachedFont(Fonts.Mono, Fonts.SizeXS),
                WordWrap = false,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                ShortcutsEnabled = true
            };
            logLayout.Controls.Add(logTextBox, 0, 0);

            logBody.Controls.Add(logLayout);
            logPanel.Controls.Add(logBody);

            var logWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = Padding.Empty
            };
            logWrapper.Controls.Add(logPanel);
            bottomLayout.Controls.Add(logWrapper, 2, 0);

            rootLayout.Controls.Add(bottomLayout, 0, 1);
            rootLayout.SetColumnSpan(bottomLayout, 3);

            mainContainer.Controls.Add(rootLayout);
            contentPanel.Controls.Add(mainContainer);
            
            LoadCompletateData();
            ApplyTerminateInitialLayout(listView);
        }
        /// <summary>
        /// Applica terminate filters alle impostazioni correnti.
        /// </summary>
        private void ApplyTerminateFilters(ArchiviazioneInfo preferredSelection = null, bool maintainSelection = true)
        {
            var listView = contentPanel.Controls.Find("TerminateListView", true).FirstOrDefault() as ListView;
            if (listView == null)
                return;

            var currentSelection = preferredSelection ?? _selectedTerminateInfo;
            var filtered = FilterAndSortTerminateArchiviazioni().ToList();

            listView.BeginUpdate();
            listView.Items.Clear();

            foreach (var info in filtered)
            {
                var item = new ListViewItem(string.IsNullOrWhiteSpace(info.Id) ? "N/D" : info.Id);
                item.SubItems.Add(string.IsNullOrWhiteSpace(info.IdLavoro) ? "N/D" : info.IdLavoro);
                item.SubItems.Add(string.IsNullOrWhiteSpace(info.Telecamera) ? "N/D" : info.Telecamera);
                    item.SubItems.Add(FormatPeriodo(info.PeriodoInizio, info.PeriodoFine));
                item.SubItems.Add(string.IsNullOrWhiteSpace(info.ProcedimentoPenale) ? "N/A" : info.ProcedimentoPenale);
                item.SubItems.Add(string.IsNullOrWhiteSpace(info.RitSpec) ? "N/A" : info.RitSpec);

                string targetValue = string.IsNullOrWhiteSpace(info.Target) ? "N/D" : info.Target;
                item.SubItems.Add(targetValue);

                bool success = IsCompletedStatus(info.Stato);
                string outcomeLabel = success ? "Terminata" : "Fallita";
                item.SubItems.Add(outcomeLabel);
                item.Tag = info;
                item.ForeColor = success ? Colors.TextPrimary : Colors.Error;

                if (currentSelection != null &&
                    !string.IsNullOrEmpty(currentSelection.Id) &&
                    currentSelection.Id == info.Id &&
                    currentSelection.DataCompletamento == info.DataCompletamento)
                {
                    item.Selected = true;
                    item.Focused = true;
                }

                listView.Items.Add(item);
            }

            if (filtered.Count == 0)
            {
                var placeholder = new ListViewItem("Nessuna archiviazione trovata");
                while (placeholder.SubItems.Count < listView.Columns.Count)
                    placeholder.SubItems.Add(string.Empty);
                listView.Items.Add(placeholder);
            }

            listView.EndUpdate();

            AdjustTerminateColumns(listView);

            if (!maintainSelection)
                currentSelection = null;

            ArchiviazioneInfo selectedInfo = null;
            if (listView.SelectedItems.Count > 0 && listView.SelectedItems[0].Tag is ArchiviazioneInfo infoTag)
                selectedInfo = infoTag;

            _selectedTerminateInfo = selectedInfo;
            RenderTerminateDetails(selectedInfo);
            LoadTerminateLogContent(selectedInfo);
            UpdateTerminateButtonStates(listView, "TerminateCopyDetailsButton", "TerminateCopyToLaunchButton", "TerminateCreateSitButton", "TerminateDeleteButton");
            UpdateTerminateLogButtons();
            UpdateTerminateStats(filtered);
        }

        /// <summary>
        /// Filtra and sort terminate archiviazioni con i criteri attivi.
        /// </summary>
        private IEnumerable<ArchiviazioneInfo> FilterAndSortTerminateArchiviazioni()
        {
            IEnumerable<ArchiviazioneInfo> entries = (_terminateSnapshot ?? new List<ArchiviazioneInfo>())
                .Where(info => info != null);

            if (!string.IsNullOrWhiteSpace(_terminateSearchTerm))
            {
                var term = _terminateSearchTerm;
                entries = entries.Where(info =>
                    (!string.IsNullOrEmpty(info.Id) && info.Id.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(info.IdLavoro) && info.IdLavoro.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(info.Telecamera) && info.Telecamera.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(info.ProcedimentoPenale) && info.ProcedimentoPenale.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(info.RitSpec) && info.RitSpec.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(info.Procura) && info.Procura.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(info.Target) && info.Target.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(info.Cartella) && info.Cartella.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Ordina per parte numerica dell'ID JOB (più recente in alto), con ID stringa come chiave secondaria
            return entries
                .OrderByDescending(info => ExtractJobNumericId(info.Id))
                .ThenByDescending(info => info.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Estrae la componente numerica iniziale dell'ID JOB (es. "1234ab" -> 1234) per ordinamento.
        /// </summary>
        private static long ExtractJobNumericId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return 0;

            long value = 0;
            foreach (char c in id)
            {
                if (!char.IsDigit(c))
                    break;

                value = checked(value * 10 + (c - '0'));
            }

            return value;
        }

        /// <summary>
        /// Applica terminate initial layout alle impostazioni correnti.
        /// </summary>
        private void ApplyTerminateInitialLayout(ListView listView)
        {
            if (listView == null || listView.Columns.Count == 0)
                return;

            int totalWidth = listView.ClientSize.Width;
            if (totalWidth <= 0)
            {
                AdjustTerminateColumns(listView);
                return;
            }

            int columnCount = listView.Columns.Count;
            int minTotal = 0;

            for (int i = 0; i < columnCount; i++)
                minTotal += GetColumnMinWidth(listView.Name, i);

            try
            {
                _isAdjustingTerminateColumns = true;

                if (totalWidth <= minTotal)
                {
                    double scale = totalWidth / (double)Math.Max(1, minTotal);
                    for (int i = 0; i < columnCount; i++)
                    {
                        int min = GetColumnMinWidth(listView.Name, i);
                        int scaled = (int)Math.Round(min * scale);
                        listView.Columns[i].Width = Math.Max(min, scaled);
                    }
                }
                else
                {
                    int defaultWidth = totalWidth / columnCount;
                    for (int i = 0; i < columnCount; i++)
                    {
                        int min = GetColumnMinWidth(listView.Name, i);
                        listView.Columns[i].Width = Math.Max(min, defaultWidth);
                    }
                }
            }
            finally
            {
                _isAdjustingTerminateColumns = false;
            }

            AdjustTerminateColumns(listView);
        }

        /// <summary>
        /// Aggiorna terminate stats e mantiene lo stato coerente.
        /// </summary>
        private void UpdateTerminateStats(IReadOnlyList<ArchiviazioneInfo> view)
        {
            var totalSnapshot = _terminateSnapshot?.Count ?? 0;
            var filteredCount = view?.Count ?? 0;
            var completedCount = view?.Count(info => info != null && IsCompletedStatus(info.Stato)) ?? 0;
            var errorCount = view?.Count(info => info != null && NormalizeStatus(info.Stato).IndexOf("erro", StringComparison.OrdinalIgnoreCase) >= 0) ?? 0;

            long totalBytes = 0;
            if (view != null)
            {
                foreach (var info in view)
                    totalBytes += ResolveArchiveSize(info);
            }

            var totalLabel = contentPanel.Controls.Find("TerminateTotalValueLabel", true).FirstOrDefault() as Label;
            var totalDescription = contentPanel.Controls.Find("TerminateTotalDescriptionLabel", true).FirstOrDefault() as Label;
            var successLabel = contentPanel.Controls.Find("TerminateSuccessValueLabel", true).FirstOrDefault() as Label;
            var successDescription = contentPanel.Controls.Find("TerminateSuccessDescriptionLabel", true).FirstOrDefault() as Label;
            var spaceLabel = contentPanel.Controls.Find("TerminateSpaceValueLabel", true).FirstOrDefault() as Label;
            var spaceDescription = contentPanel.Controls.Find("TerminateSpaceDescriptionLabel", true).FirstOrDefault() as Label;

            if (totalLabel != null)
                totalLabel.Text = filteredCount.ToString();

            if (totalDescription != null)
            {
                totalDescription.Text = filteredCount == totalSnapshot
                    ? "Totale archiviazioni finalizzate"
                    : $"{filteredCount} su {totalSnapshot} archiviazioni finalizzate";
            }

            if (successLabel != null)
            {
                successLabel.Text = filteredCount == 0
                    ? "—"
                    : $"{Math.Round(completedCount * 100.0 / filteredCount, 1)}%";
            }

            if (successDescription != null)
            {
                if (filteredCount == 0)
                    successDescription.Text = "Nessuna archiviazione filtrata";
                else
                    successDescription.Text = $"{completedCount} completate • {errorCount} con errori";
            }

            if (spaceLabel != null)
                spaceLabel.Text = filteredCount == 0 ? "—" : FormatSize(totalBytes);

            if (spaceDescription != null)
            {
                if (filteredCount == 0)
                    spaceDescription.Text = "Nessun dato disponibile";
                else
                {
                    var averageBytes = filteredCount > 0 ? totalBytes / filteredCount : 0;
                    spaceDescription.Text = $"Somma su {filteredCount} elementi • Media {FormatSize(averageBytes)}";
                }
            }
        }
        /// <summary>
        /// Disegna terminate details sul form.
        /// </summary>
        private void RenderTerminateDetails(ArchiviazioneInfo info)
        {
            var detailPanel = contentPanel.Controls.Find("TerminateDetailPanel", true).FirstOrDefault() as Panel;
            if (detailPanel == null)
                return;

            var placeholder = detailPanel.Controls.Find("TerminateDetailPlaceholder", true).FirstOrDefault();
            var content = detailPanel.Controls.Find("TerminateDetailContent", true).FirstOrDefault() as TableLayoutPanel;
            if (placeholder == null || content == null)
                return;

            if (info == null)
            {
                content.Visible = false;
                placeholder.Visible = true;

                var placeholderLabel = placeholder.Controls.Find("TerminateDetailPlaceholderLabel", true).FirstOrDefault() as Label;
                if (placeholderLabel != null)
                {
                    placeholderLabel.Text = _terminateSnapshot.Count == 0
                        ? "Non ci sono archiviazioni terminate"
                        : "Seleziona un'archiviazione terminata per visualizzare i dettagli";
                }

                var outcomeValueLabel = detailPanel.Controls.Find("TerminateOutcomeValue", true).FirstOrDefault() as Label;
                if (outcomeValueLabel != null)
                {
                    outcomeValueLabel.Text = "Esito non disponibile";
                    outcomeValueLabel.ForeColor = Colors.TextMuted;
                }

                var outcomeDescriptionLabel = detailPanel.Controls.Find("TerminateOutcomeDescription", true).FirstOrDefault() as Label;
                if (outcomeDescriptionLabel != null)
                    outcomeDescriptionLabel.Text = "Seleziona un'archiviazione per visualizzare l'esito.";

                return;
            }

            placeholder.Visible = false;
            content.Visible = true;

            SetLabelText("TerminateDetailIdValue", string.IsNullOrWhiteSpace(info.Id) ? "N/D" : info.Id);
            SetLabelText("TerminateDetailWorkIdValue", string.IsNullOrWhiteSpace(info.IdLavoro) ? "N/D" : info.IdLavoro);
            SetLabelText("TerminateDetailCameraValue", string.IsNullOrWhiteSpace(info.Telecamera) ? "N/D" : info.Telecamera);
            SetLabelText("TerminateDetailPeriodoValue", FormatPeriodo(info.PeriodoInizio, info.PeriodoFine));

            bool success = IsCompletedStatus(info.Stato);
            var outcomeValue = detailPanel.Controls.Find("TerminateOutcomeValue", true).FirstOrDefault() as Label;
            if (outcomeValue != null)
            {
                outcomeValue.Text = success ? "TERMINATA" : "IN ERRORE";
                outcomeValue.ForeColor = success ? Colors.Success : Colors.Error;
            }

            var dimensione = !string.IsNullOrWhiteSpace(info.Dimensione)
                ? info.Dimensione
                : FormatSize(ResolveArchiveSize(info));
            var outcomeDescription = detailPanel.Controls.Find("TerminateOutcomeDescription", true).FirstOrDefault() as Label;
            if (outcomeDescription != null)
            {
                var segments = new List<string>
                {
                    info.DataCompletamento == default
                        ? "Data completamento N/D"
                        : $"Completata il {info.DataCompletamento:dd/MM/yyyy HH:mm}",
                    string.IsNullOrWhiteSpace(info.ServerAddress) ? "Server N/D" : $"Server: {info.ServerAddress}",
                    string.IsNullOrWhiteSpace(info.Cartella) ? "Cartella N/D" : $"Cartella: {info.Cartella}",
                    string.IsNullOrWhiteSpace(dimensione) ? "Dimensione N/D" : $"Dimensione: {dimensione}"
                };
                int descriptionWidth = detailPanel.ClientSize.Width;
                outcomeDescription.MaximumSize = descriptionWidth > 0
                    ? new Size(Math.Max(0, descriptionWidth - Spacing.LG), 0)
                    : new Size(int.MaxValue, 0);
                outcomeDescription.Text = string.Join(System.Environment.NewLine, segments);
            }

            SetLabelText("TerminateDetailPathValue", string.IsNullOrWhiteSpace(info.Cartella) ? "—" : info.Cartella, info.Cartella);
            var procedimentoDisplay = string.IsNullOrWhiteSpace(info.ProcedimentoPenale) ? "N/A" : info.ProcedimentoPenale;
            var ritDisplay = string.IsNullOrWhiteSpace(info.RitSpec) ? "N/A" : info.RitSpec;
            SetLabelText("TerminateDetailPPValue", $"{procedimentoDisplay} • {ritDisplay}");
            SetLabelText("TerminateDetailMagistratoValue", string.IsNullOrWhiteSpace(info.Magistrato) ? "N/D" : info.Magistrato);
            SetLabelText("TerminateDetailProcuraValue", string.IsNullOrWhiteSpace(info.Procura) ? "N/D" : info.Procura);
            SetLabelText("TerminateDetailTargetValue", string.IsNullOrWhiteSpace(info.Target) ? "N/D" : info.Target);
            SetLabelText("TerminateDetailPasswordValue", string.IsNullOrWhiteSpace(info.Password) ? "—" : info.Password);
        }

        /// <summary>
        /// Imposta terminate log text usando i parametri passati.
        /// </summary>
        private void SetTerminateLogText(string message, string summary = null)
        {
            var logTextBox = contentPanel.Controls.Find("TerminateLogTextBox", true).FirstOrDefault() as RichTextBox;
            if (logTextBox != null)
            {
                ResetLogHighlight(logTextBox, ref _terminateLogHighlightStart, ref _terminateLogHighlightLength);
                logTextBox.SuspendLayout();
                var displayText = message;
                if (string.IsNullOrEmpty(displayText) && !string.IsNullOrEmpty(summary))
                    displayText = summary;

                logTextBox.Text = displayText ?? string.Empty;
                logTextBox.SelectionStart = logTextBox.TextLength;
                logTextBox.SelectionLength = 0;
                logTextBox.ScrollToCaret();
                logTextBox.ResumeLayout();
                HighlightTerminateLogSearchTerm();
            }
        }

        /// <summary>
        /// Carica terminate log content e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadTerminateLogContent(ArchiviazioneInfo info)
        {
            if (info == null)
            {
                _currentTerminateLogFile = null;
                SetTerminateLogText(string.Empty, "Seleziona un'archiviazione per visualizzare il log");
                UpdateTerminateLogButtons();
                return;
            }

            var logPath = ResolveCompletedLogPath(info);
            _currentTerminateLogFile = logPath;

            bool logExists = !string.IsNullOrEmpty(logPath) && File.Exists(logPath);
            if (!logExists)
            {
                SetTerminateLogText(string.Empty, "Nessun log disponibile per l'archiviazione selezionata");
                UpdateTerminateLogButtons();
                return;
            }

            try
            {
                string content;
                using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    content = reader.ReadToEnd();
                }

                if (!string.IsNullOrEmpty(content))
                {
                    var lines = content.Replace("\r\n", "\n").Split('\n');
                    if (lines.Length > 400)
                        lines = lines.Skip(lines.Length - 400).ToArray();
                content = string.Join(System.Environment.NewLine, lines);
                }

                SetTerminateLogText(content, $"Log: {Path.GetFileName(logPath)}");
            }
            catch (Exception ex)
            {
                SetTerminateLogText(string.Empty, $"Errore lettura log: {ex.Message}");
            }

            UpdateTerminateLogButtons();
        }
        /// <summary>
        /// Risolve completed log path senza interventi manuali.
        /// </summary>
        private string ResolveCompletedLogPath(ArchiviazioneInfo info)
        {
            if (info == null)
                return null;

            if (_logIndexService != null && _logIndexService.TryGetLogPath(info.Id, out var indexed) && File.Exists(indexed))
                return indexed;

            if (!string.IsNullOrWhiteSpace(info.Cartella))
            {
                var postProcessing = Path.Combine(info.Cartella, "POST_PROCESSING_LOG.txt");
                if (File.Exists(postProcessing))
                    return postProcessing;

                try
                {
                    if (Directory.Exists(info.Cartella))
                    {
                        var innerLog = Directory.EnumerateFiles(info.Cartella, "*.log")
                            .OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
                            .FirstOrDefault();
                        if (!string.IsNullOrEmpty(innerLog))
                            return innerLog;
                    }
                }
                catch { }
            }

            var candidateFolders = new List<string>();
            if (!string.IsNullOrEmpty(_logDirectoryPath))
                candidateFolders.Add(_logDirectoryPath);
            if (!string.IsNullOrEmpty(_legacyLogDirectoryPath))
                candidateFolders.Add(_legacyLogDirectoryPath);

            foreach (var folder in candidateFolders.Where(Directory.Exists))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(info.Id))
                    {
                        var exact = Path.Combine(folder, $"{info.Id}.log");
                        if (File.Exists(exact))
                            return exact;

                        var matches = Directory.GetFiles(folder, $"{info.Id}*.log", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
                            .FirstOrDefault();
                        if (!string.IsNullOrEmpty(matches))
                            return matches;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Aggiorna terminate log buttons e mantiene lo stato coerente.
        /// </summary>
        private void UpdateTerminateLogButtons()
        {
            bool hasSelection = _selectedTerminateInfo != null;
            bool logAvailable = !string.IsNullOrEmpty(_currentTerminateLogFile) && File.Exists(_currentTerminateLogFile);

            var logButton = contentPanel.Controls.Find("TerminateLogButton", true).FirstOrDefault() as Button;
            if (logButton != null)
                logButton.Enabled = hasSelection && logAvailable;
        }

        /// <summary>
        /// Esegue la logica adjust terminate columns senza cambiare il comportamento.
        /// </summary>
        private void AdjustTerminateColumns(ListView listView)
        {
            if (_isAdjustingTerminateColumns)
                return;

            if (listView == null || listView.Columns.Count == 0)
                return;

            try
            {
                _isAdjustingTerminateColumns = true;
                listView.BeginUpdate();

                int columnCount = listView.Columns.Count;
                int clientWidth = Math.Max(0, listView.ClientSize.Width);
                bool hasVerticalScroll = listView.Items.Count * Sizes.ListViewRowHeight > listView.ClientSize.Height;
                if (hasVerticalScroll)
                {
                    clientWidth = Math.Max(0, clientWidth - SystemInformation.VerticalScrollBarWidth);
                }
                if (clientWidth == 0)
                    return;

                var minWidths = new int[columnCount];
                double totalMin = 0;
                for (int i = 0; i < columnCount; i++)
                {
                    minWidths[i] = GetColumnMinWidth(listView.Name, i);
                    totalMin += minWidths[i];
                }

                bool compress = clientWidth < totalMin && totalMin > 0;
                int assigned = 0;

                for (int i = 0; i < columnCount; i++)
                {
                    double ratio = totalMin > 0 ? minWidths[i] / totalMin : 0;
                    int suggested = (int)Math.Round(clientWidth * ratio);
                    int minWidth = compress ? Math.Max(40, suggested) : Math.Max(minWidths[i], suggested);

                    if (i == columnCount - 1)
                    {
                        int remaining = clientWidth - assigned;
                        if (remaining > 0)
                            minWidth = Math.Max(minWidth, remaining);
                    }

                    minWidth = Math.Max(40, minWidth);
                    listView.Columns[i].Width = minWidth;
                    assigned += minWidth;
                }

                // Correggi eventuali differenze dovute ad arrotondamenti distribuendo il delta sull'ultima colonna
                int totalAssigned = 0;
                for (int i = 0; i < columnCount; i++)
                    totalAssigned += listView.Columns[i].Width;

                int delta = clientWidth - totalAssigned;
                if (delta != 0 && columnCount > 0)
                {
                    int lastIndex = columnCount - 1;
                    int minLast = GetColumnMinWidth(listView.Name, lastIndex);
                    int newWidth = listView.Columns[lastIndex].Width + delta;
                    if (newWidth < minLast)
                        newWidth = minLast;
                    listView.Columns[lastIndex].Width = newWidth;
                }
            }
            finally
            {
                listView.EndUpdate();
                _isAdjustingTerminateColumns = false;
            }
        }

        /// <summary>
        /// Callback WinForms per terminate column width changing.
        /// </summary>
        private void OnTerminateColumnWidthChanging(ListView listView, ColumnWidthChangingEventArgs e)
        {
            if (listView == null || e.ColumnIndex < 0 || e.ColumnIndex >= listView.Columns.Count)
                return;

            int lastIndex = listView.Columns.Count - 1;
            int min = GetColumnMinWidth(listView.Name, e.ColumnIndex);
            int minLast = GetColumnMinWidth(listView.Name, lastIndex);

            if (e.ColumnIndex == lastIndex)
            {
                if (e.NewWidth < minLast)
                    e.NewWidth = minLast;
                return;
            }

            int clientWidth = listView.ClientSize.Width;
            if (clientWidth <= 0)
                return;

            int otherWidth = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                if (i == e.ColumnIndex)
                            continue;
                otherWidth += listView.Columns[i].Width;
            }

            int maxWidth = clientWidth - otherWidth - minLast;
            if (maxWidth < min)
                maxWidth = min;

            if (e.NewWidth < min)
                e.NewWidth = min;
            else if (e.NewWidth > maxWidth)
                e.NewWidth = maxWidth;

            int available = clientWidth - (otherWidth + e.NewWidth);
            int newLastWidth = Math.Max(minLast, available);

            if (newLastWidth != listView.Columns[lastIndex].Width)
            {
            try
            {
                _isAdjustingTerminateColumns = true;
                    listView.Columns[lastIndex].Width = newLastWidth;
            }
            finally
            {
                _isAdjustingTerminateColumns = false;
                }
            }
        }

        /// <summary>
        /// Risolve archive duration senza interventi manuali.
        /// </summary>
        private string ResolveArchiveDuration(ArchiviazioneInfo info)
        {
            if (info == null)
                return "—";

            if (!string.IsNullOrWhiteSpace(info.Durata))
                return info.Durata;

            DateTime actualStart = info.DataAvvio != default ? info.DataAvvio : info.Inizio;
            DateTime actualEnd = info.DataCompletamento != default ? info.DataCompletamento : info.Fine;

            TimeSpan? span = null;
            if (actualStart != default && actualEnd != default)
                span = actualEnd - actualStart;
            else if (info.PeriodoInizio != default && info.PeriodoFine != default)
                span = info.PeriodoFine - info.PeriodoInizio;

            if (!span.HasValue)
                return "—";

            var safeSpan = span.Value;
            if (safeSpan < TimeSpan.Zero)
                safeSpan = TimeSpan.Zero;

            return FormatTimeSpan(safeSpan);
        }

        private System.Windows.Forms.Timer _statusUpdateTimer;
        private long _totalBytesExported = 0;
        private DateTime _lastStatusUpdate = DateTime.Now;
        
        /// <summary>
        /// Crea status bar al volo.
        /// </summary>
        private void CreateStatusBar()
        {
            statusPanel.Controls.Clear();
            statusPanel.Padding = new Padding(Spacing.SM, 0, Spacing.SM, 0);
            statusPanel.BackColor = Color.FromArgb(20, 20, 20);
            statusPanel.MinimumSize = new Size(0, Sizes.StatusBarHeight);
            statusPanel.MaximumSize = new Size(int.MaxValue, Sizes.StatusBarHeight);
            statusPanel.Height = Sizes.StatusBarHeight;

            var container = new TableLayoutPanel
            {
                Name = "StatusContainer",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = new Padding(0),
                ColumnCount = 3,
                RowCount = 1
            };
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            TableLayoutPanel CreateSlot()
            {
                var slot = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                    Margin = Padding.Empty,
                    Padding = new Padding(0),
                    ColumnCount = 1,
                    RowCount = 1
                };
                slot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                slot.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                return slot;
            }

            var procuraLabel = new Label
            {
                Name = "ProcuraStatusText",
                Text = GetProcuraStatusText(),
                Font = GetSafeFont("Segoe UI", 9),
                ForeColor = Color.FromArgb(180, 180, 180),
                AutoSize = true,
                Anchor = AnchorStyles.None,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var connectionPanel = new FlowLayoutPanel
            {
                Name = "ConnectionPanel",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = new Padding(0)
            };
            
            var connectionIcon = new Label
            {
                Name = "ConnectionIcon",
                Text = "⬤",
                Font = GetSafeFont("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = true,
                Margin = new Padding(0, 1, 4, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            
            var connectionLabel = new Label
            {
                Name = "ConnectionStatusText",
                Text = "Disconnesso",
                Font = GetSafeFont("Segoe UI", 9),
                ForeColor = Color.FromArgb(255, 87, 34),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            
            connectionPanel.Controls.Add(connectionIcon);
            connectionPanel.Controls.Add(connectionLabel);
            
            var statsLabel = new Label
            {
                Name = "StatsText",
                Text = "Archiviazioni: 0 in corso, 0 completate",
                Font = GetSafeFont("Segoe UI", 9),
                ForeColor = Color.FromArgb(150, 150, 150),
                AutoSize = true,
                Anchor = AnchorStyles.None,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var leftSlot = CreateSlot();
            var centerSlot = CreateSlot();
            var rightSlot = CreateSlot();

            leftSlot.Controls.Add(procuraLabel, 0, 0);
            centerSlot.Controls.Add(connectionPanel, 0, 0);
            rightSlot.Controls.Add(statsLabel, 0, 0);

            connectionPanel.Anchor = AnchorStyles.None;

            container.Controls.Add(leftSlot, 0, 0);
            container.Controls.Add(centerSlot, 1, 0);
            container.Controls.Add(rightSlot, 2, 0);

            statusPanel.Controls.Add(container);

            if (_statusUpdateTimer == null)
            {
                _statusUpdateTimer = new System.Windows.Forms.Timer();
                _statusUpdateTimer.Interval = 500;
                _statusUpdateTimer.Tick += UpdateStatusBarLive;
                _statusUpdateTimer.Start();
            }
            else if (!_statusUpdateTimer.Enabled)
            {
                _statusUpdateTimer.Start();
        }

            UpdateStatusBarLive(null, EventArgs.Empty);
        }

        /// <summary>
        /// Aggiorna status bar live e mantiene lo stato coerente.
        /// </summary>
        private void UpdateStatusBarLive(object sender, EventArgs e)
        {
            try
            {
                UpdateProcuraStatusLabel();
                // Aggiorna icona connessione con animazione
                var connectionIcon = statusPanel.Controls.Find("ConnectionIcon", true).FirstOrDefault() as Label;
                var connectionText = statusPanel.Controls.Find("ConnectionStatusText", true).FirstOrDefault() as Label;
                
                if (connectionIcon != null && connectionText != null)
                {
                    bool environmentConnected = false;
                    try
                    {
                        environmentConnected = EnvironmentManager.Instance?.MasterSite?.ServerId != null;
                    }
                    catch
                    {
                        environmentConnected = false;
                    }

                    if (_isServerConnected && !environmentConnected)
                    {
                        _isServerConnected = false;
                    }

                    if (_isServerConnected)
                    {
                        // Connesso - icona verde fissa
                        connectionIcon.Text = "⬤";
                        connectionIcon.ForeColor = Colors.Success;
                        string serverName = string.IsNullOrWhiteSpace(_currentServerAddress)
                            ? "Server"
                            : _currentServerAddress;
                        connectionText.Text = $"Connesso a {serverName}";
                        connectionText.ForeColor = Colors.TextSecondary;
                    }
                    else
                    {
                        // Disconnesso - icona rossa
                        connectionIcon.Text = "⬤";
                        connectionIcon.ForeColor = Colors.Error;
                        connectionText.Text = "Disconnesso";
                        connectionText.ForeColor = Colors.TextMuted;
                    }
                }
                
                // Aggiorna statistiche
                var statsText = statusPanel.Controls.Find("StatsText", true).FirstOrDefault() as Label;
                if (statsText != null)
                {
                    int inProgress = GetArchiviazioniInCorso().Count;
                    int completed = GetArchiviazioniCompletate().Count;
                    
                    string sizeText = _totalBytesExported > 0 ? 
                        $" - {FormatFileSize(_totalBytesExported)} esportati" : "";
                    
                    statsText.Text = $"Arc: {inProgress} in corso, {completed} completate{sizeText}";
                }
            }
            catch { }
        }
        
        /// <summary>
        /// Esegue la logica normalize procura name senza cambiare il comportamento.
        /// </summary>
        private string NormalizeProcuraName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim();

            string[] prefixes =
            {
                "Procura di",
                "Procura della",
                "Procura del",
                "Procura delle",
                "Procura dei",
                "Procura dello"
            };

            foreach (var prefix in prefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(prefix.Length).TrimStart(' ', ':');
                    break;
                }
            }

            return normalized.ToUpperInvariant();
        }

        /// <summary>
        /// Restituisce procura status text gia pronto.
        /// </summary>
        private string GetProcuraStatusText()
        {
            return string.IsNullOrWhiteSpace(_currentProcura)
                ? "Procura di: --"
                : $"Procura di: {_currentProcura}";
        }
        
        /// <summary>
        /// Aggiorna procura status label e mantiene lo stato coerente.
        /// </summary>
        private void UpdateProcuraStatusLabel()
        {
            if (statusPanel == null)
                return;

            var procuraLabel = statusPanel.Controls.Find("ProcuraStatusText", true).FirstOrDefault() as Label;
            if (procuraLabel != null)
            {
                procuraLabel.Text = GetProcuraStatusText();
            }
        }
        
        /// <summary>
        /// Esegue la logica format file size senza cambiare il comportamento.
        /// </summary>
        private string FormatFileSize(long bytes)
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

        /// <summary>
        /// Mostra date picker all'utente.
        /// </summary>
        private void ShowDatePicker(TextBox targetTextBox, bool isStartDate)
        {
            if (targetTextBox == null)
                return;

            var existingCalendar = this.Controls.Find("InlineCalendar", true).FirstOrDefault();
            if (existingCalendar != null)
            {
                existingCalendar.Parent.Controls.Remove(existingCalendar);
                existingCalendar.Dispose();
            }

            var calendar = new MonthCalendar
            {
                Name = "InlineCalendar",
                MaxSelectionCount = 1,
                ShowToday = true,
                ShowTodayCircle = true,
                BackColor = Colors.InputBackground,
                ForeColor = Colors.TextPrimary,
                TitleBackColor = Colors.Primary,
                TitleForeColor = Colors.TextPrimary,
                TrailingForeColor = Colors.TextMuted,
                Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM)
            };

            DateTime currentValue = DateTime.Now;
            string currentText = NormalizeTimeSeparators(GetRealValue(targetTextBox));
            if (!DateTime.TryParseExact(currentText, "dd/MM/yyyy HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out currentValue))
            {
                DateTime.TryParseExact(currentText, "dd/MM/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out currentValue);
            }
            if (currentValue.Year < 1900)
                currentValue = DateTime.Now;
            calendar.SelectionStart = currentValue.Date;
            calendar.SelectionEnd = currentValue.Date;

            calendar.DateSelected += (s, e) =>
            {
                DateTime existingDateTime;
                TimeSpan timeToKeep = new TimeSpan(12, 0, 0);

                if (!DateTime.TryParseExact(NormalizeTimeSeparators(GetRealValue(targetTextBox)), "dd/MM/yyyy HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out existingDateTime))
                {
                    DateTime.TryParseExact(NormalizeTimeSeparators(GetRealValue(targetTextBox)), "dd/MM/yyyy HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out existingDateTime);
                }
                if (existingDateTime != DateTime.MinValue && existingDateTime.Year >= 1900)
                {
                    timeToKeep = new TimeSpan(existingDateTime.Hour, existingDateTime.Minute, 0);
                }

                DateTime newDateTime = e.Start.Date.Add(timeToKeep);
                targetTextBox.Text = newDateTime.ToString("dd/MM/yyyy HH:mm");
                targetTextBox.ForeColor = Colors.TextPrimary;

                if (calendar.Parent != null)
                {
                    calendar.Parent.Controls.Remove(calendar);
                    calendar.Dispose();
                }
            };

            calendar.Leave += (s, e) =>
            {
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer
                {
                    Interval = 200
                };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    if (calendar.Parent != null && !calendar.Focused)
                    {
                        calendar.Parent.Controls.Remove(calendar);
                        calendar.Dispose();
                    }
                };
                timer.Start();
            };

            var textBoxLocation = targetTextBox.Parent.PointToScreen(targetTextBox.Location);
            var formLocation = this.PointToClient(textBoxLocation);
            calendar.Location = new Point(formLocation.X, formLocation.Y + targetTextBox.Height + 2);

            this.Controls.Add(calendar);
            calendar.BringToFront();
            calendar.Focus();
        }
        // Validazione semplice dell'intervallo date/ore con ErrorProvider
        /// <summary>
        /// Valida date range prima di continuare.
        /// </summary>
        private void ValidateDateRange()
        {
            try
            {
                var inizioTextBox = contentPanel.Controls.Find("InizioTextBox", true).FirstOrDefault() as TextBox;
                var fineTextBox = contentPanel.Controls.Find("FineTextBox", true).FirstOrDefault() as TextBox;

                DateTime inizioDate = DateTime.Now;
                DateTime fineDate = DateTime.Now;
                bool hasValidDates = false;

                if (inizioTextBox != null && fineTextBox != null)
                {
                    DateTime tempInizio, tempFine;
                    string inizioText = NormalizeTimeSeparators(GetRealValue(inizioTextBox));
                    string fineText = NormalizeTimeSeparators(GetRealValue(fineTextBox));

                    bool inizioValid = DateTime.TryParseExact(inizioText, "dd/MM/yyyy HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out tempInizio);
                    if (!inizioValid)
                    {
                        inizioValid = DateTime.TryParseExact(inizioText, "dd/MM/yyyy HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out tempInizio);
                    }

                    if (inizioValid && tempInizio.Year < 1900)
                    {
                        inizioValid = false;
                    }

                    bool fineValid = DateTime.TryParseExact(fineText, "dd/MM/yyyy HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out tempFine);
                    if (!fineValid)
                    {
                        fineValid = DateTime.TryParseExact(fineText, "dd/MM/yyyy HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out tempFine);
                    }

                    if (fineValid && tempFine.Year < 1900)
                    {
                        fineValid = false;
                    }

                    if (inizioValid && fineValid)
                    {
                        inizioDate = tempInizio;
                        fineDate = tempFine;
                        hasValidDates = true;
                    }
                }

                if (!hasValidDates)
                {
                    var inizioPicker = contentPanel.Controls.Find("InizioPicker", true).FirstOrDefault() as DateTimePicker;
                    var finePicker = contentPanel.Controls.Find("FinePicker", true).FirstOrDefault() as DateTimePicker;

                    if (inizioPicker != null && finePicker != null)
                    {
                        inizioDate = inizioPicker.Value;
                        fineDate = finePicker.Value;
                        hasValidDates = true;
                    }
                }

                if (!hasValidDates)
                {
                    return;
                }

                if (inizioDate >= fineDate)
                {
                    if (fineTextBox != null && _errorProvider != null)
                        _errorProvider.SetError(fineTextBox, "La data di fine deve essere successiva a quella di inizio");
                }
                else
                {
                    if (fineTextBox != null && _errorProvider != null)
                        _errorProvider.SetError(fineTextBox, string.Empty);
                }
            }
            catch { }
        }
        // Metodi helper per creare componenti coerenti
        /// <summary>
        /// Crea header panel al volo.
        /// </summary>
        private Panel CreateHeaderPanel(string title, Color accentColor)
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(10, 8, 10, 10)
            };
            
            // Bordo colorato sinistro
            panel.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(accentColor, 4))
                {
                    e.Graphics.DrawLine(pen, 0, 0, 0, panel.Height);
                }
            };
            
            Label titleLabel = new Label
            {
                Text = title,
                Font = GetSafeFont("Cascadia Mono", 12, FontStyle.Bold), // Font ridotto per coerenza
                ForeColor = accentColor,
                Location = new Point(15, 8),
                AutoSize = true
            };
            
            panel.Controls.Add(titleLabel);
            return panel;
        }
        /// <summary>
        /// Crea header with actions al volo.
        /// </summary>
        private Panel CreateHeaderWithActions(string title, Color accentColor, params ButtonInfo[] actions)
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(10, 8, 10, 8)
            };

            // Bordo colorato sinistro
            panel.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(accentColor, 4))
                {
                    e.Graphics.DrawLine(pen, 0, 0, 0, panel.Height);
                }
            };

            Label titleLabel = new Label
            {
                Text = title,
                Font = GetSafeFont("Cascadia Mono", 12, FontStyle.Bold),
                ForeColor = accentColor,
                Location = new Point(15, 8),
                AutoSize = true
            };
            panel.Controls.Add(titleLabel);

            FlowLayoutPanel actionsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0),
                Margin = new Padding(0),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            foreach (var a in actions)
            {
                var btn = new Button
                {
                    Text = a.Text,
                    Height = 28,
                    Width = Math.Max(120, TextRenderer.MeasureText(a.Text, GetSafeFont("Cascadia Mono", 9, FontStyle.Bold)).Width + 20),
                    Font = GetSafeFont("Cascadia Mono", 9, FontStyle.Bold),
                    ForeColor = Color.White
                };
                ApplyButtonStyle(btn, a.BackColor, ControlPaint.Light(a.BackColor), ControlPaint.Dark(a.BackColor));
                if (a.ClickHandler != null) btn.Click += a.ClickHandler;
                btn.Enabled = !a.RequiresSelection; // aggiornata da UpdateButtonStates
                btn.Name = a.Name ?? a.Text.Replace(" ", "");
                actionsPanel.Controls.Add(btn);
            }

            panel.Controls.Add(actionsPanel);
            return panel;
        }
        /// <summary>
        /// Crea list view panel al volo.
        /// </summary>
        private Panel CreateListViewPanel(string name, string[] columns, int[] columnWidths)
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(10, 8, 10, 10)
            };
            
            bool isInCorso = name == "InCorsoListView";
            
            ListView listView = new ListView
            {
                Name = name,
                Dock = DockStyle.Fill,
                BackColor = isInCorso ? Color.FromArgb(30, 34, 44) : Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                FullRowSelect = true,
                GridLines = !isInCorso,
                View = View.Details,
                Font = GetSafeFont("Cascadia Mono", 9),
                OwnerDraw = true,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            
            // Abilita double buffering per performance
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(listView, true, null);
            }
            catch { }

            var listState = EnsureListViewState(listView);
            if (listState != null)
                listView.ListViewItemSorter = new ListViewItemComparer(listState.SortColumn, listState.Ascending);
            
            // Colonne con larghezze dinamiche
            for (int i = 0; i < columns.Length; i++)
            {
                listView.Columns.Add(columns[i], columnWidths[i]);
            }
            
            // Auto-resize colonne al caricamento e resize
            void ResizeColumns()
            {
                if (listView.Columns.Count == 0)
                    return;
                
                int totalWidth = listView.ClientSize.Width;
                if (totalWidth <= 0)
                    return;
                
                int minColumnWidth = 40;
                int[] minWidths = name == "InCorsoListView" 
                    ? new int[] { 50, 180, 200, 150, 130, 200, 180 }
                    : new int[] { 60, 130, 110, 110, 70, 90, 120 };
                    
                int columnCount = Math.Min(listView.Columns.Count, minWidths.Length);
                if (columnCount == 0)
                    return;

                int[] baseWidths = new int[columnCount];
                for (int i = 0; i < columnCount; i++)
                    baseWidths[i] = i < columnWidths.Length ? columnWidths[i] : minWidths[i];

                int minTotal = minWidths.Take(columnCount).Sum();
                int baseTotal = baseWidths.Sum();
                int[] widths = new int[columnCount];

                if (totalWidth <= minTotal)
                {
                    double scale = totalWidth / (double)minTotal;
                    for (int i = 0; i < columnCount; i++)
                    {
                        widths[i] = Math.Max(minColumnWidth, (int)Math.Round(minWidths[i] * scale));
                    }
                }
                else if (totalWidth <= baseTotal)
                {
                    double ratio = (double)(totalWidth - minTotal) / Math.Max(1, baseTotal - minTotal);
                    for (int i = 0; i < columnCount; i++)
                    {
                        int delta = Math.Max(0, baseWidths[i] - minWidths[i]);
                        widths[i] = Math.Max(minColumnWidth, minWidths[i] + (int)Math.Round(delta * ratio));
                    }
                }
                else
                {
                    int extra = totalWidth - baseTotal;
                    int[] weights = baseWidths.Select(w => Math.Max(1, w)).ToArray();
                    int weightSum = weights.Sum();
                    for (int i = 0; i < columnCount; i++)
                    {
                        int additional = weightSum > 0 ? (int)Math.Round(extra * (weights[i] / (double)weightSum)) : 0;
                        widths[i] = Math.Max(minColumnWidth, baseWidths[i] + additional);
                    }
                }

                int widthTotal = widths.Sum();
                int diff = totalWidth - widthTotal;
                if (diff != 0 && columnCount > 0)
                {
                    widths[columnCount - 1] = Math.Max(minColumnWidth, widths[columnCount - 1] + diff);
                }

                if (columnCount > 0 && widths[columnCount - 1] < minColumnWidth)
                {
                    int deficit = minColumnWidth - widths[columnCount - 1];
                    widths[columnCount - 1] = minColumnWidth;
                    for (int i = columnCount - 2; i >= 0 && deficit > 0; i--)
                    {
                        int reducible = widths[i] - minColumnWidth;
                        if (reducible <= 0)
                            continue;
                        int reduction = Math.Min(reducible, deficit);
                        widths[i] -= reduction;
                        deficit -= reduction;
                    }
                }

                for (int i = 0; i < columnCount; i++)
                {
                    listView.Columns[i].Width = widths[i];
                }
            }
            
            // Resize al caricamento e al ridimensionamento
            listView.HandleCreated += (s, e) => ResizeColumns();
            listView.Resize += (s, e) => ResizeColumns();
            
            // Ordinamento colonna con indicatore visivo
            listView.ColumnClick += (s, e) => ApplyListViewSort(listView, e.Column);
            
            // Context menu ottimizzato
            var cms = new ContextMenuStrip();
            if (name == "InCorsoListView")
            {
                cms.Items.Add("Elimina cartella", null, (s, e) => DeleteArchiviazione_Click(s, e));
                cms.Items.Add("Aggiorna lista", null, (s, e) => RefreshInCorso_Click(s, e));
            }
            else if (name == "TerminateListView")
            {
                cms.Items.Add("Mostra dettagli", null, (s, e) => DetailsArchiviazione_Click(s, e));
                cms.Items.Add("Apri cartella", null, (s, e) => OpenFolder_Click(s, e));
                cms.Items.Add("Apri log", null, (s, e) => VisualizzaLog_Click(s, e));
                cms.Items.Add(new ToolStripSeparator());
                cms.Items.Add("Aggiorna lista", null, (s, e) => RefreshCompletate_Click(s, e));
            }
            listView.ContextMenuStrip = cms;
            AddCopyCellMenu(listView, cms);
            
            // Rendering migliorato
            int Clamp(int value) => Math.Max(0, Math.Min(255, value));

            Color LerpColor(Color start, Color end, float amount)
            {
                amount = Math.Max(0f, Math.Min(1f, amount));
                int r = Clamp((int)(start.R + (end.R - start.R) * amount));
                int g = Clamp((int)(start.G + (end.G - start.G) * amount));
                int b = Clamp((int)(start.B + (end.B - start.B) * amount));
                return Color.FromArgb(r, g, b);
            }

            Color ResolveCellBackground(int rowIndex, int columnIndex, bool isSelected)
            {
                if (!isInCorso)
                    return Color.Empty;

                if (isSelected)
                    return Color.FromArgb(140, Colors.Primary);

                Color rowBase = rowIndex % 2 == 0 ? Color.FromArgb(36, 42, 54) : Color.FromArgb(32, 36, 46);
                float blendAmount = Math.Min(0.28f, columnIndex * 0.06f);
                return LerpColor(rowBase, Color.FromArgb(46, 82, 118), blendAmount);
            }

            listView.DrawColumnHeader += (s, e) =>
            {
                if (isInCorso)
                {
                    e.Graphics.FillRectangle(GetCachedBrush(Color.FromArgb(44, 52, 66)), e.Bounds);
                    TextRenderer.DrawText(e.Graphics, listView.Columns[e.ColumnIndex].Text,
                        GetSafeFont("Cascadia Mono", 9, FontStyle.Bold), e.Bounds,
                        Colors.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    return;
                }

                e.Graphics.FillRectangle(GetCachedBrush(Color.FromArgb(55,55,55)), e.Bounds);
                TextRenderer.DrawText(e.Graphics, listView.Columns[e.ColumnIndex].Text,
                    GetSafeFont("Cascadia Mono", 9, FontStyle.Bold), e.Bounds,
                    Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            };
            
            listView.DrawItem += (s, e) =>
            {
                if (!isInCorso && e.ItemIndex >= 0)
                {
                    bool isSelected = e.Item.Selected;
                    Color bgColor = isSelected ? Color.FromArgb(33, 150, 243) :
                                   (e.ItemIndex % 2 == 0 ? Color.FromArgb(35, 35, 35) : Color.FromArgb(42, 42, 42));
                    e.Graphics.FillRectangle(GetCachedBrush(bgColor), e.Bounds);
                }
            };
            
            listView.DrawSubItem += (s, e) =>
            {
                if (e.ItemIndex < 0)
                    return;

                if (isInCorso)
                {
                    bool isSelected = e.Item.Selected;
                    Color textColor = isSelected ? Color.White : Color.FromArgb(225, 225, 225);
                    Color cellBackground = ResolveCellBackground(e.ItemIndex, e.ColumnIndex, isSelected);
                    if (cellBackground != Color.Empty)
                        e.Graphics.FillRectangle(GetCachedBrush(cellBackground), e.Bounds);

                    if (isSelected)
                    {
                        using (var pen = new Pen(Color.FromArgb(180, Colors.Primary), 1f))
                        {
                            var rect = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                            e.Graphics.DrawRectangle(pen, rect);
                        }
                    }

                    if (e.ColumnIndex == listView.Columns.Count - 1)
                    {
                        string raw = e.SubItem.Text ?? string.Empty;
                        string numeric = raw.Replace("%", string.Empty).Trim();
                        int progressValue;
                        if (!int.TryParse(numeric, out progressValue))
                            progressValue = 0;
                        progressValue = Math.Max(0, Math.Min(100, progressValue));

                        Rectangle barBounds = Rectangle.Inflate(e.Bounds, -10, -12);
                        if (barBounds.Width < 1) barBounds.Width = 1;
                        if (barBounds.Height < 6) barBounds.Height = 6;

                        Color trackColor = isSelected ? Color.FromArgb(120, Colors.Primary) : Color.FromArgb(42, 48, 60);
                        using (var trackBrush = new SolidBrush(trackColor))
                        {
                            e.Graphics.FillRectangle(trackBrush, barBounds);
                        }

                        if (progressValue > 0)
                        {
                            int fillWidth = (int)Math.Round(barBounds.Width * (progressValue / 100f));
                            Rectangle fillRect = new Rectangle(barBounds.Left, barBounds.Top, Math.Max(0, fillWidth), barBounds.Height);
                            Color fillColor = isSelected ? Color.FromArgb(220, Color.White) : Colors.Primary;
                            e.Graphics.FillRectangle(GetCachedBrush(fillColor), fillRect);
                        }

                        TextRenderer.DrawText(e.Graphics, $"{progressValue}%", GetSafeFont("Cascadia Mono", 9, FontStyle.Bold), barBounds,
                            Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        return;
                    }

                    if (e.ColumnIndex == 0)
                    {
                        Rectangle iconRect = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height / 2 - 6), 12, 12);
                        var job = e.Item.Tag as ExportJob;
                        string status = job?.Status ?? string.Empty;
                        Color stateColor = GetStatusColor(status);

                        using (var brush = new SolidBrush(stateColor))
                        using (var pen = new Pen(Color.FromArgb(30, 30, 30), 1))
                        {
                            e.Graphics.FillEllipse(brush, iconRect);
                            e.Graphics.DrawEllipse(pen, iconRect);
                        }

                        var textBounds = new Rectangle(e.Bounds.Left + 32, e.Bounds.Top, e.Bounds.Width - 34, e.Bounds.Height);
                        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, GetSafeFont("Cascadia Mono", 9), textBounds, textColor,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        return;
                    }

                    var defaultBounds = new Rectangle(e.Bounds.Left + 12, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, e.SubItem.Text, GetSafeFont("Cascadia Mono", 9), defaultBounds, textColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    return;
                }

                bool isSelectedStandard = e.Item.Selected;
                Color textColorStandard = isSelectedStandard ? Color.White : Color.FromArgb(220, 220, 220);

                    if (e.ColumnIndex == 0)
                    {
                        Rectangle iconRect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top + (e.Bounds.Height/2 - 6), 12, 12);
                        string rowState = (e.Item.SubItems.Count > 5 ? e.Item.SubItems[5].Text : e.SubItem.Text) ?? string.Empty;
                        
                        Color stateColor = Color.FromArgb(120,120,120);
                        if (rowState.IndexOf("complet", StringComparison.InvariantCultureIgnoreCase) >= 0) stateColor = Color.FromArgb(76,175,80);
                        else if (rowState.IndexOf("in corso", StringComparison.InvariantCultureIgnoreCase) >= 0) stateColor = Color.FromArgb(33,150,243);
                        else if (rowState.IndexOf("erro", StringComparison.InvariantCultureIgnoreCase) >= 0) stateColor = Color.FromArgb(229,62,62);
                        else if (rowState.IndexOf("annull", StringComparison.InvariantCultureIgnoreCase) >= 0) stateColor = Color.FromArgb(158,158,158);
                        
                        using (var brush = new SolidBrush(stateColor))
                        using (var pen = new Pen(Color.FromArgb(40,40,40), 1))
                        {
                            e.Graphics.FillEllipse(brush, iconRect);
                            e.Graphics.DrawEllipse(pen, iconRect);
                        }
                        
                        var textBounds = new Rectangle(e.Bounds.Left + 24, e.Bounds.Top, e.Bounds.Width - 24, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, e.SubItem.Text, GetSafeFont("Cascadia Mono", 9), textBounds, textColorStandard, 
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        return;
                    }
                    
                    TextRenderer.DrawText(e.Graphics, e.SubItem.Text,
                        GetSafeFont("Cascadia Mono", 9), e.Bounds,
                    textColorStandard, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            };
            
            panel.Controls.Add(listView);
            return panel;
        }

        /// <summary>
        /// Crea buttons panel al volo.
        /// </summary>
        private Panel CreateButtonsPanel(params ButtonInfo[] buttonInfos)
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(10, 5, 10, 10)
            };
            
            int x = 0;
            int spacing = 10;
            
            foreach (var buttonInfo in buttonInfos)
            {
                Button button = new Button
                {
                    Name = buttonInfo.Name,
                    Text = buttonInfo.Text,
                    Location = new Point(x, 5),
                    Size = new Size(GetButtonWidth(buttonInfo.Text), 32),
                    BackColor = buttonInfo.BackColor,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = GetSafeFont("Cascadia Mono", 9, FontStyle.Bold),
                    Enabled = !buttonInfo.RequiresSelection
                };
                button.FlatAppearance.BorderSize = 0;
                button.Click += buttonInfo.ClickHandler;
                
                button.Margin = new Padding(0, 0, 0, Spacing.LG);
                button.Padding = new Padding(Spacing.LG, Spacing.XXS, Spacing.LG, Spacing.XXS);
                
                panel.Controls.Add(button);
                x += button.Width + spacing;
            }
            
            return panel;
        }

        /// <summary>
        /// Restituisce button width gia pronto.
        /// </summary>
        private int GetButtonWidth(string text)
        {
            // Calcola larghezza basata sul testo
            int baseWidth = 120;
            if (text.Length > 15) baseWidth = 150;
            if (text.Length > 20) baseWidth = 180;
            return baseWidth;
        }

        /// <summary>
        /// Indica se final status soddisfa i criteri richiesti.
        /// </summary>
        private static bool IsFinalStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;

            string normalized = status.ToLowerInvariant();
            return normalized.Contains("complet") ||
                   normalized.Contains("termin") ||
                   normalized.Contains("erro") ||
                   normalized.Contains("fall") ||
                   normalized.Contains("annull") ||
                   normalized.Contains("cancel");
        }

        /// <summary>
        /// Indica se completed status soddisfa i criteri richiesti.
        /// </summary>
        private static bool IsCompletedStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;

            string normalized = status.ToLowerInvariant();
            return normalized.Contains("complet") || normalized.Contains("termin");
        }
        /// <summary>
        /// Esegue la logica read archiviazioni from logs senza cambiare il comportamento.
        /// </summary>
        private List<ArchiviazioneInfo> ReadArchiviazioniFromLogs()
        {
            var results = new List<ArchiviazioneInfo>();

            try
            {
                EnsureLogDirectory();

                if (!Directory.Exists(_logDirectoryPath))
                    return results;

                var files = Directory.EnumerateFiles(_logDirectoryPath, "*.log")
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(stream))
                        {
                            string headerLine = reader.ReadLine();
                            if (string.IsNullOrWhiteSpace(headerLine))
                                continue;
                            
                            ExportLogMetadata metadata;
                            try
                            {
                                metadata = JsonConvert.DeserializeObject<ExportLogMetadata>(headerLine);
                            }
                            catch
                            {
                                continue;
                            }

                            DateTime? metadataStart = ParseLogTimestamp(metadata?.start);
                            DateTime? metadataEnd = ParseLogTimestamp(metadata?.end);
                            DateTime? initialTimestamp = ParseLogTimestamp(metadata?.timestamp);

                            int lastProgress = 0;
                            string status = "In corso";
                            string loggedSize = null;
                            string overridePath = metadata?.cartella;
                            bool hasCompleted = false;
                            bool hasError = false;
                            bool hasCanceled = false;
                            DateTime? lastSeenTimestamp = initialTimestamp;
                            var errorLog = new List<string>();

                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                    continue;

                                Dictionary<string, object> entry;
                                try
                                {
                                    entry = JsonConvert.DeserializeObject<Dictionary<string, object>>(line);
                                }
                                catch
                                {
                                    continue;
                                }

                                if (entry == null)
                                    continue;

                                if (entry.TryGetValue("ts", out var tsValue))
                                {
                                    var tsParsed = ParseLogTimestamp(Convert.ToString(tsValue));
                                    if (tsParsed.HasValue)
                                        lastSeenTimestamp = tsParsed;
                                }

                                string eventType = entry.TryGetValue("event", out var eventObj)
                                    ? Convert.ToString(eventObj)
                                    : string.Empty;

                                switch (eventType)
                                {
                                    case "progress":
                                        if (entry.TryGetValue("value", out var progressObj) && int.TryParse(Convert.ToString(progressObj), out var progressValue))
                                            lastProgress = progressValue;
                                        if (entry.TryGetValue("status", out var statusObj))
                                        {
                                            var candidate = Convert.ToString(statusObj);
                                            if (!string.IsNullOrWhiteSpace(candidate))
                                                status = candidate;
                                        }
                                        break;
                                    case "completed":
                                        hasCompleted = true;
                                        status = "Completato";
                                        lastProgress = 100;
                                        if (entry.TryGetValue("size", out var sizeObj))
                                            loggedSize = Convert.ToString(sizeObj);
                                        if (entry.TryGetValue("path", out var pathObj))
                                        {
                                            var candidatePath = Convert.ToString(pathObj);
                                            if (!string.IsNullOrWhiteSpace(candidatePath))
                                                overridePath = candidatePath;
                                        }
                                        if (!metadataEnd.HasValue && entry.TryGetValue("ts", out var tsCompleted))
                                        {
                                            var completedTs = ParseLogTimestamp(Convert.ToString(tsCompleted));
                                            if (completedTs.HasValue)
                                                metadataEnd = completedTs;
                                        }
                                        break;
                                    case "error":
                                        hasError = true;
                                        status = "Errore";
                                        if (entry.TryGetValue("code", out var codeObj))
                                        {
                                            var codeText = Convert.ToString(codeObj);
                                            if (!string.IsNullOrWhiteSpace(codeText))
                                                errorLog.Add($"Codice {codeText}");
                                        }
                                        if (entry.TryGetValue("message", out var messageObj))
                                        {
                                            var messageText = Convert.ToString(messageObj);
                                            if (!string.IsNullOrWhiteSpace(messageText))
                                                errorLog.Add(messageText);
                                        }
                                        break;
                                    case "canceled":
                                        hasCanceled = true;
                                        status = "Annullato";
                                        break;
                                }
                            }

                            var info = new ArchiviazioneInfo
                            {
                                Id = string.IsNullOrWhiteSpace(metadata?.jobId) ? Path.GetFileNameWithoutExtension(file.Name) : metadata.jobId,
                                Telecamera = metadata?.camera ?? "N/D",
                                ServerAddress = metadata?.serverAddress,
                                ProcedimentoPenale = metadata?.procedimento,
                                RitSpec = metadata?.ritSpec,
                                Target = metadata?.target,
                                IdLavoro = metadata?.idLavoro,
                                Cartella = overridePath,
                                Password = metadata?.password,
                                Progresso = Math.Max(0, Math.Min(100, lastProgress)),
                                Stato = status
                            };

                            DateTime fallbackTime = lastSeenTimestamp ?? metadataStart ?? DateTime.Now;
                            info.PeriodoInizio = metadataStart ?? fallbackTime;
                            info.PeriodoFine = metadataEnd ?? (hasCompleted || hasError || hasCanceled ? fallbackTime : info.PeriodoInizio);
                            if (info.PeriodoFine < info.PeriodoInizio)
                                info.PeriodoFine = info.PeriodoInizio;

                            var dataAvvio = initialTimestamp ?? info.PeriodoInizio;
                            info.DataAvvio = dataAvvio;

                            var dataCompletamento = (hasCompleted || hasError || hasCanceled)
                                ? (lastSeenTimestamp ?? info.PeriodoFine)
                                : (lastSeenTimestamp ?? info.PeriodoInizio);

                            if (dataCompletamento < dataAvvio)
                                dataCompletamento = dataAvvio;

                            info.DataCompletamento = dataCompletamento;
                            info.Inizio = dataAvvio;
                            info.Fine = dataCompletamento;

                            if (info.DataCompletamento > info.DataAvvio)
                                info.Durata = (info.DataCompletamento - info.DataAvvio).ToString(@"hh\:mm\:ss");
                            else
                                info.Durata = "N/D";

                            if (!string.IsNullOrWhiteSpace(loggedSize))
                            {
                                info.Dimensione = loggedSize;
                            }
                            else if (!string.IsNullOrWhiteSpace(info.Cartella) && Directory.Exists(info.Cartella) && IsFinalStatus(info.Stato))
                            {
                                try
                                {
                                    info.Dimensione = FormatSize(GetDirectorySize(info.Cartella));
                                }
                                catch
                                {
                                    info.Dimensione = "N/D";
                                }
                            }
                            else
                            {
                                info.Dimensione = "N/D";
                            }

                            if (errorLog.Count > 0)
                                info.ErrorLog = errorLog;

                            results.Add(info);
                        }
                    }
                    catch (Exception exFile)
                    {
                        Trace.WriteLine($"Errore lettura log '{file.Name}': {exFile.Message}");
                        Debug.WriteLine($"Errore lettura log '{file.Name}': {exFile.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento log archiviazioni: {ex.Message}");
            }
            
            return results;
        }
        // Metodi per caricare i dati delle archiviazioni
        /// <summary>
        /// Carica in corso data e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadInCorsoData()
        {
            var listView = contentPanel.Controls.Find("InCorsoListView", true).FirstOrDefault() as ListView;
            var previousSelection = listView?.SelectedItems.Count > 0 ? listView.SelectedItems[0].Tag as ExportJob : null;
            RefreshInCorsoSnapshot(previousSelection);
        }

        /// <summary>
        /// Rinfresca in corso snapshot per avere info fresche.
        /// </summary>
        private void RefreshInCorsoSnapshot(ExportJob preferredSelection = null, bool maintainSelection = true)
        {
                List<ExportJob> snapshot;
            lock (_exportLock)
            {
                snapshot = _queuedExports
                    .Concat(_activeExports)
                    .Where(job => job != null && !job.IsCanceled && !IsFinalStatus(job.Status))
                        .ToList();
                }

            try
            {
                var persistedInfos = _dataService.GetActiveExports();
                if (persistedInfos != null)
                {
                    foreach (var info in persistedInfos)
                    {
                        if (info == null || string.IsNullOrWhiteSpace(info.Id))
                            continue;

                        var existing = snapshot.FirstOrDefault(job =>
                            string.Equals(job.Id, info.Id, StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            existing.Progress = Math.Max(existing.Progress, Math.Max(0, Math.Min(100, info.Progresso)));

                            var normalizedStatus = NormalizeStatus(info.Stato);
                            if (!string.IsNullOrWhiteSpace(normalizedStatus) && !IsFinalStatus(normalizedStatus))
                                existing.Status = normalizedStatus;

                            if (existing.StartTime == default(DateTime) && info.Inizio != default(DateTime))
                                existing.StartTime = info.Inizio;

                            if (existing.EndTime == default(DateTime) && info.Fine != default(DateTime))
                                existing.EndTime = info.Fine;

                            if (string.IsNullOrWhiteSpace(existing.CameraName) && !string.IsNullOrWhiteSpace(info.Telecamera))
                                existing.CameraName = info.Telecamera;

                            if (string.IsNullOrWhiteSpace(existing.Path) && !string.IsNullOrWhiteSpace(info.Cartella))
                                existing.Path = info.Cartella;
                        }
                        else
                        {
                            var snapshotJob = CreateSnapshotJob(info);
                            if (snapshotJob != null)
                            {
                                snapshotJob.IsHistoricalSnapshot = true;
                                snapshot.Add(snapshotJob);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore aggiornamento archiviazioni attive dal database: {ex.Message}");
            }

            snapshot = snapshot
                .OrderByDescending(job => job.CreatedAt)
                .ThenBy(job => job.Status, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _inCorsoSnapshot = snapshot;

            UpdateInCorsoStats(snapshot);
            ApplyInCorsoFilters(preferredSelection, maintainSelection);

            UpdateTabNotifications();
        }

        /// <summary>
        /// Carica completate data e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadCompletateData(IEnumerable<ArchiviazioneInfo> snapshot = null)
        {
            List<ArchiviazioneInfo> archiviazioniCompletate;

            if (snapshot != null)
            {
                archiviazioniCompletate = snapshot
                    .Where(info => info != null && IsFinalStatus(info.Stato))
                    .Select(info => info.Clone())
                    .OrderByDescending(info => info.DataCompletamento)
                    .ToList();
            }
            else
            {
                lock (_completedExportsLock)
                {
                    archiviazioniCompletate = _completedExports
                        .Where(info => info != null)
                        .Select(info => info.Clone())
                        .OrderByDescending(info => info.DataCompletamento)
                        .ToList();
                }
            }

            _terminateSnapshot = archiviazioniCompletate;

            ApplyTerminateFilters(_selectedTerminateInfo, maintainSelection: true);
            UpdateTabNotifications();
        }

        /// <summary>
        /// Aggiorna button states e mantiene lo stato coerente.
        /// </summary>
        private void UpdateButtonStates(ListView listView, params string[] buttonNames)
        {
            bool hasSelection = listView?.SelectedItems
                .Cast<ListViewItem>()
                .Any(item => item?.Tag != null) ?? false;
            var selectedJob = hasSelection ? listView?.SelectedItems[0].Tag as ExportJob : null;
            List<ExportJob> queuedJobsSnapshot = null;
            Func<List<ExportJob>> getQueuedJobs = () =>
            {
                if (queuedJobsSnapshot != null)
                    return queuedJobsSnapshot;

                lock (_exportLock)
                {
                    queuedJobsSnapshot = _queuedExports
                        .Where(job => job != null && !job.IsCanceled && IsJobQueued(job))
                        .ToList();
                    return queuedJobsSnapshot;
                }
            };
            
            foreach (string buttonName in buttonNames)
            {
                Button button = FindControlRecursive(contentPanel, buttonName) as Button;
            if (button == null)
                    continue;

                bool enabled = hasSelection;

                if (buttonName == "DeleteArchiviazioneButton")
                {
                    bool pathAvailable = selectedJob != null && !string.IsNullOrWhiteSpace(selectedJob.Path);
                    enabled = hasSelection && pathAvailable;
                }
                else if (buttonName == "MoveUpArchiviazioneButton" || buttonName == "MoveDownArchiviazioneButton")
                {
                    enabled = hasSelection && selectedJob != null && IsJobQueued(selectedJob);

                    if (enabled)
                    {
                        var queuedJobs = getQueuedJobs();
                        int index = queuedJobs.FindIndex(job =>
                            ReferenceEquals(job, selectedJob) ||
                            (!string.IsNullOrEmpty(job.Id) && !string.IsNullOrEmpty(selectedJob.Id) &&
                             string.Equals(job.Id, selectedJob.Id, StringComparison.OrdinalIgnoreCase)));

                        if (index < 0)
                        {
                            enabled = false;
                        }
                        else if (buttonName == "MoveUpArchiviazioneButton")
                        {
                            enabled = index > 0;
                        }
                        else
                        {
                            enabled = index >= 0 && index < queuedJobs.Count - 1;
                        }
                    }
                }

                button.Enabled = enabled;
            }
        }

        /// <summary>
        /// Indica se job queued soddisfa i criteri richiesti.
        /// </summary>
        private bool IsJobQueued(ExportJob job)
        {
            if (job == null || string.IsNullOrWhiteSpace(job.Status))
                return false;

            var normalized = NormalizeStatus(job.Status);
            return normalized.Equals("In Coda", StringComparison.OrdinalIgnoreCase);
        }
        /// <summary>
        /// Esegue la logica find control recursive senza cambiare il comportamento.
        /// </summary>
        private Control FindControlRecursive(Control parent, string name)
        {
            if (parent.Name == name)
                return parent;
                
            foreach (Control child in parent.Controls)
            {
                Control found = FindControlRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        // Eventi per i pulsanti
        /// <summary>
        /// Gestisce l'evento click del controllo refresh in corso per tenere la UI reattiva.
        /// </summary>
        private void RefreshInCorso_Click(object sender, EventArgs e)
        {
            LoadInCorsoData();
        }

        /// <summary>
        /// Gestisce l'evento click del controllo move up archiviazione per tenere la UI reattiva.
        /// </summary>
        private void MoveUpArchiviazione_Click(object sender, EventArgs e)
        {
            ReorderSelectedInCorsoJob(-1);
        }
        /// <summary>
        /// Gestisce l'evento click del controllo move down archiviazione per tenere la UI reattiva.
        /// </summary>
        private void MoveDownArchiviazione_Click(object sender, EventArgs e)
        {
            ReorderSelectedInCorsoJob(1);
        }

        /// <summary>
        /// Restituisce selected in corso job gia pronto.
        /// </summary>
        private ExportJob GetSelectedInCorsoJob()
        {
            var listView = contentPanel.Controls.Find("InCorsoListView", true).FirstOrDefault() as ListView;
            if (listView?.SelectedItems.Count > 0)
                return listView.SelectedItems[0].Tag as ExportJob;
            return null;
        }

        /// <summary>
        /// Esegue la logica reorder selected in corso job senza cambiare il comportamento.
        /// </summary>
        private void ReorderSelectedInCorsoJob(int direction)
        {
            var job = GetSelectedInCorsoJob();
            string caption = direction < 0 ? "Sposta su" : "Sposta giù";

            if (job == null)
            {
                ShowInfo(caption, "Seleziona un'archiviazione dalla lista per modificarne la priorità.");
                return;
            }

            if (!IsJobQueued(job))
            {
                ShowInfo(caption, "Puoi modificare l'ordine solo delle archiviazioni ancora in coda.");
                return;
            }

            if (!TryReorderQueuedJob(job, direction))
            {
                ShowInfo(caption, "L'archiviazione selezionata è già nella posizione richiesta.");
                return;
            }

            RefreshInCorsoSnapshot(job, maintainSelection: true);
        }

        /// <summary>
        /// Esegue la logica try reorder queued job senza cambiare il comportamento.
        /// </summary>
        private bool TryReorderQueuedJob(ExportJob job, int direction)
        {
            if (job == null || direction == 0)
                return false;

            ExportJob targetJob = null;
            ExportJobRequest jobRequest = null;
            ExportJobRequest targetRequest = null;
            string jobOldId = null;
            string targetOldId = null;

            lock (_exportLock)
            {
                int currentIndex = _queuedExports.FindIndex(j => ReferenceEquals(j, job));
                if (currentIndex < 0 && !string.IsNullOrEmpty(job.Id))
                {
                    currentIndex = _queuedExports.FindIndex(j =>
                        j != null &&
                        !string.IsNullOrEmpty(j.Id) &&
                        string.Equals(j.Id, job.Id, StringComparison.OrdinalIgnoreCase));
                }

                if (currentIndex < 0)
                    return false;

                int newIndex = currentIndex + direction;
                if (newIndex < 0 || newIndex >= _queuedExports.Count)
                    return false;

                targetJob = _queuedExports[newIndex];
                if (targetJob == null || !IsJobQueued(targetJob))
                    return false;

                jobOldId = job.Id;
                targetOldId = targetJob.Id;

                (_queuedExports[currentIndex], _queuedExports[newIndex]) = (_queuedExports[newIndex], _queuedExports[currentIndex]);

                var requests = _pendingJobRequests.ToList();
                jobRequest = requests.FirstOrDefault(r => r?.Job != null && ReferenceEquals(r.Job, job));
                if (jobRequest == null && !string.IsNullOrEmpty(jobOldId))
                {
                    jobRequest = requests.FirstOrDefault(r =>
                        r?.Job != null &&
                        !string.IsNullOrEmpty(r.Job.Id) &&
                        string.Equals(r.Job.Id, jobOldId, StringComparison.OrdinalIgnoreCase));
                }

                targetRequest = requests.FirstOrDefault(r => r?.Job != null && ReferenceEquals(r.Job, targetJob));
                if (targetRequest == null && !string.IsNullOrEmpty(targetOldId))
                {
                    targetRequest = requests.FirstOrDefault(r =>
                        r?.Job != null &&
                        !string.IsNullOrEmpty(r.Job.Id) &&
                        string.Equals(r.Job.Id, targetOldId, StringComparison.OrdinalIgnoreCase));
                }

                if (jobRequest != null && targetRequest != null)
                {
                    int requestIndexA = requests.IndexOf(jobRequest);
                    int requestIndexB = requests.IndexOf(targetRequest);
                    if (requestIndexA >= 0 && requestIndexB >= 0)
                    {
                        (requests[requestIndexA], requests[requestIndexB]) = (requests[requestIndexB], requests[requestIndexA]);
                        _pendingJobRequests.Clear();
                        foreach (var req in requests)
                            _pendingJobRequests.Enqueue(req);
                    }
                }
            }

            if (targetJob == null || string.IsNullOrEmpty(jobOldId) || string.IsNullOrEmpty(targetOldId))
                return false;

            SwapQueuedJobIdentifiers(job, targetJob, jobOldId, targetOldId, jobRequest, targetRequest);
            return true;
        }

        /// <summary>
        /// Esegue la logica swap queued job identifiers senza cambiare il comportamento.
        /// </summary>
        private void SwapQueuedJobIdentifiers(
            ExportJob firstJob,
            ExportJob secondJob,
            string firstOldId,
            string secondOldId,
            ExportJobRequest firstRequest,
            ExportJobRequest secondRequest)
        {
            if (firstJob == null || secondJob == null)
                return;
            if (string.IsNullOrWhiteSpace(firstOldId) || string.IsNullOrWhiteSpace(secondOldId))
                return;
            if (string.Equals(firstOldId, secondOldId, StringComparison.OrdinalIgnoreCase))
                return;

            RenameLogFileIfExists(firstJob, firstOldId, secondOldId);
            RenameLogFileIfExists(secondJob, secondOldId, firstOldId);

            try
            {
                _dataService?.SwapJobPublicIds(firstOldId, secondOldId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SwapQueuedJobIdentifiers: errore aggiornamento database - {ex.Message}");
            }

            try
            {
                _logIndexService?.SwapJobIds(firstOldId, secondOldId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SwapQueuedJobIdentifiers: errore aggiornamento indice log - {ex.Message}");
            }

            lock (_exportLock)
            {
                firstJob.Id = secondOldId;
                secondJob.Id = firstOldId;

                if (firstRequest?.PreparedJob != null)
                    firstRequest.PreparedJob.JobId = firstJob.Id;
                if (secondRequest?.PreparedJob != null)
                    secondRequest.PreparedJob.JobId = secondJob.Id;
            }
        }

        /// <summary>
        /// Esegue la logica rename log file if exists senza cambiare il comportamento.
        /// </summary>
        private void RenameLogFileIfExists(ExportJob job, string oldId, string newId)
        {
            if (job == null || string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
                return;

            var currentPath = job.LogFilePath;
            if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
                return;

            try
            {
                var directory = Path.GetDirectoryName(currentPath);
                var fileName = Path.GetFileName(currentPath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                    return;

                if (!fileName.StartsWith(oldId, StringComparison.OrdinalIgnoreCase))
                    return;

                var newFileName = newId + fileName.Substring(oldId.Length);
                var newPath = Path.Combine(directory, newFileName);
                if (string.Equals(newPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    return;

                if (File.Exists(newPath))
                    return;

                File.Move(currentPath, newPath);
                job.LogFilePath = newPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RenameLogFileIfExists: {ex.Message}");
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo refresh completate per tenere la UI reattiva.
        /// </summary>
        private void RefreshCompletate_Click(object sender, EventArgs e)
        {
            LoadCompletedExports();
        }

        /// <summary>
        /// Gestisce l'evento click del controllo cancel archiviazione per tenere la UI reattiva.
        /// </summary>
        private void CancelArchiviazione_Click(object sender, EventArgs e)
        {
            ListView inCorsoListView = contentPanel.Controls.Find("InCorsoListView", true).FirstOrDefault() as ListView;
            if (inCorsoListView?.SelectedItems.Count > 0)
            {
                var selectedItem = inCorsoListView.SelectedItems[0];
                var exportJob = selectedItem.Tag as ExportJob;
                string jobId = exportJob?.Id;
                
                if (!string.IsNullOrEmpty(jobId))
                {
                    var result = ShowConfirmation(
                        "Conferma annullamento",
                        $"Vuoi annullare l'archiviazione {jobId}? L'operazione interromperà definitivamente il job in corso.",
                        DarkDialogButtons.YesNo,
                        DarkDialogIcon.Warning,
                        primaryButtonText: "Annulla archiviazione",
                        secondaryButtonText: "Mantieni attiva");
                        
                    if (result == DialogResult.Yes)
                    {
                        try
                        {
                            // Annulla l'archiviazione
                            CancelArchiviazione(jobId);
                            LoadInCorsoData(); // Ricarica la lista
                        }
                        catch (Exception ex)
                        {
                            ShowError("Annullamento non riuscito", $"Impossibile annullare l'archiviazione {jobId}.\nDettagli: {ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo delete archiviazione per tenere la UI reattiva.
        /// </summary>
        private void DeleteArchiviazione_Click(object sender, EventArgs e)
        {
            var job = GetSelectedInCorsoJob();
            if (job == null)
            {
                ShowInfo("Elimina archiviazione", "Seleziona l'archiviazione che desideri eliminare.");
                return;
            }

            if (string.IsNullOrWhiteSpace(job.Path))
            {
                ShowInfo("Elimina archiviazione", "La cartella associata a questa archiviazione non è più disponibile.");
                return;
            }

            bool directoryExists = Directory.Exists(job.Path);
            string confirmationMessage = directoryExists
                ? $"Vuoi eliminare definitivamente la cartella:\n{job.Path}\n\nL'operazione non può essere annullata."
                : $"La cartella associata all'archiviazione non è stata trovata.\nVuoi comunque rimuovere l'archiviazione {job.Id} dalla lista?";

            var confirmation = ShowConfirmation(
                "Conferma eliminazione",
                confirmationMessage,
                DarkDialogButtons.YesNo,
                directoryExists ? DarkDialogIcon.Warning : DarkDialogIcon.Info,
                primaryButtonText: "Elimina",
                secondaryButtonText: "Annulla");

            if (confirmation != DialogResult.Yes)
                return;

            RunWithButtonLoading(sender as Button, "ELIMINO...", () =>
            {
                try
                {
                    bool wasActiveJob = false;
                    lock (_exportLock)
                    {
                        wasActiveJob = _activeExports.Any(active =>
                            string.Equals(active.Id, job.Id, StringComparison.OrdinalIgnoreCase));
                    }

                    if (wasActiveJob && !job.IsHistoricalSnapshot)
                    {
                        CancelArchiviazione(job.Id);
                    }

                    if (directoryExists)
                    {
                        Directory.Delete(job.Path, true);
                    }

                    try
                    {
                        _dataService.DeleteJob(job.Id);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore eliminazione job {job.Id} dal database: {ex.Message}");
                    }

                    LoadInCorsoData();
                    ShowSuccess("Eliminazione completata", "L'archiviazione e la cartella associata sono state rimosse.");
                }
                catch (Exception ex)
                {
                    ShowError("Errore eliminazione", $"Non è stato possibile completare l'eliminazione.\nDettagli: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Gestisce l'evento click del controllo details archiviazione per tenere la UI reattiva.
        /// </summary>
        private void DetailsArchiviazione_Click(object sender, EventArgs e)
        {
            var listView = contentPanel.Controls.Find("TerminateListView", true).FirstOrDefault() as ListView;
            if (listView?.SelectedItems.Count > 0)
            {
                var selectedItem = listView.SelectedItems[0];
                var archiviazione = selectedItem.Tag as ArchiviazioneInfo;
                
                if (archiviazione != null)
                {
                    // Mostra dettagli dell'archiviazione
                    ShowArchiviazioneDetails(archiviazione);
                }
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo open folder per tenere la UI reattiva.
        /// </summary>
        private void OpenFolder_Click(object sender, EventArgs e)
        {
            var listView = contentPanel.Controls.Find("TerminateListView", true).FirstOrDefault() as ListView;
            if (listView?.SelectedItems.Count > 0)
            {
                var selectedItem = listView.SelectedItems[0];
                var archiviazione = selectedItem.Tag as ArchiviazioneInfo;
                
                if (archiviazione != null)
                {
                    try
                    {
                        // Apri la cartella dell'archiviazione
                        System.Diagnostics.Process.Start("explorer.exe", archiviazione.Cartella);
                    }
                    catch (Exception ex)
                    {
                        ShowError("Impossibile aprire la cartella", $"Si è verificato un errore durante l'apertura della cartella.\nDettagli: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Inizializza ialize custom controls con i default.
        /// </summary>
        private void InitializeCustomControls()
        {
            // Inizializza controlli come nell'originale
            // Questo metodo verrà chiamato dopo che tutti i controlli sono stati creati
        }

        // Eventi mantenuti dall'originale
                /// <summary>
                /// Callback WinForms per close.
                /// </summary>
                private void OnClose(object sender, EventArgs e)
                {
            if (_exporter != null)
            {
                _exporter.Cancel();
                _exporter.EndExport();
                _exporter.Close();
            }
            StopButtonLoading();
                    VideoOS.Platform.SDK.Environment.RemoveAllServers();
                    _isServerConnected = false;
                        Close();
                }
        /// <summary>
        /// Gestisce l'evento click del controllo avvia archiviazione per tenere la UI reattiva.
        /// </summary>
        private void AvviaArchiviazione_Click(object sender, EventArgs e)
        {
            RunWithButtonLoading(sender as Button, "AVVIO...", () =>
            {
                // Implementazione mantenuta dall'originale ma con controlli personalizzati
                if (ValidateAllFields())
                {
                    if (_currentLaunchMode == LaunchMode.Archivio)
                    {
                        HandleArchiveLaunch();
                        return;
                    }

                    try
                    {
                        var selectedCameras = GetSelectedCameras();
                        if (selectedCameras == null || selectedCameras.Count == 0)
                        {
                            ShowWarning("Telecamere mancanti", "Seleziona almeno una telecamera prima di avviare l'archiviazione.");
                            return;
                        }

                        bool isMultiLaunch = selectedCameras.Count > 1;
                        var commonData = CollectCommonExportData();
                        if (commonData == null)
                            return;

                        bool usePerInterval = _serverPerIntervalRadio != null &&
                                              _serverPerIntervalRadio.Checked &&
                                              _serverIntervals != null &&
                                              _serverIntervals.Count > 0;

                        if (!usePerInterval)
                        {
                            var preparedJobs = PrepareLaunchJobs(commonData, selectedCameras, isMultiLaunch);
                            if (preparedJobs == null || preparedJobs.Count == 0)
                                return;

                            foreach (var job in preparedJobs)
                            {
                                EnqueueExportJob(job, commonData, isMultiLaunch);
                            }
                        }
                        else
                        {
                            var intervals = _serverIntervals.ToList();
                            if (intervals.Count == 0)
                            {
                                ShowWarning("Intervalli TXT", "Nessun intervallo valido caricato dal file TXT.");
                                return;
                            }

                            foreach (var interval in intervals)
                            {
                                var intervalData = new ExportCommonData
                                {
                                    BaseExportPath = commonData.BaseExportPath,
                                    Procedimento = commonData.Procedimento,
                                    Rit = commonData.Rit,
                                    Magistrato = commonData.Magistrato,
                                    IdLavoro = commonData.IdLavoro,
                                    Target = commonData.Target,
                                    Procura = commonData.Procura,
                                    PasswordForMetadata = commonData.PasswordForMetadata,
                                    EncryptionPassword = commonData.EncryptionPassword,
                                    UseEncryption = commonData.UseEncryption,
                                    StartTime = interval.Start,
                                    EndTime = interval.End
                                };

                                string intervalSuffix = interval.Start.ToString("yyyyMMdd-HHmm");
                                string targetWithInterval = string.IsNullOrWhiteSpace(commonData.Target)
                                    ? intervalSuffix
                                    : $"{commonData.Target} {intervalSuffix}";

                                for (int index = 0; index < selectedCameras.Count; index++)
                                {
                                    var camera = selectedCameras[index];
                                    if (camera == null)
                                        continue;

                                    string destPath = null;
                                    try
                                    {
                                        destPath = BuildExportDestinationPath(
                                            intervalData.BaseExportPath,
                                            intervalData.Procedimento,
                                            intervalData.Rit,
                                            targetWithInterval,
                                            intervalData.IdLavoro,
                                            isMultiLaunch,
                                            index);

                                        Directory.CreateDirectory(destPath);
                                    }
                                    catch (Exception ex)
                                    {
                                        ShowError("Creazione cartella non riuscita", $"Non è stato possibile creare la cartella di destinazione:\n{destPath}\n\nDettagli: {ex.Message}");
                                        return;
                                    }

                                    string jobId = GenerateJobId(intervalData.IdLavoro);
                                    string cameraName = camera.Name ?? $"Telecamera {index + 1}";

                                    var preparedJob = new PreparedLaunchJob
                                    {
                                        Camera = camera,
                                        CameraName = cameraName,
                                        JobId = jobId,
                                        DestPath = destPath,
                                        LaunchIndex = index
                                    };

                                    EnqueueExportJob(preparedJob, intervalData, isMultiLaunch);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowError("Avvio archiviazione", $"Si è verificato un errore durante la preparazione dell'archiviazione.\nDettagli: {ex.Message}");
                    }
                }
            });
        }

        private void HandleArchiveLaunch()
        {
            if (!_currentArchiveSelection.HasPackages)
            {
                ShowWarning("Pacchetti mancanti", "Seleziona almeno un pacchetto prima di avviare l'archiviazione manuale.");
                return;
            }

            var data = CollectCommonExportData();
            if (data == null)
                return;

            bool usePerInterval = _archivePerIntervalRadio != null &&
                                  _archivePerIntervalRadio.Checked &&
                                  _archiveIntervals != null &&
                                  _archiveIntervals.Count > 0;
            bool perGroup = _archivePerGroupRadio != null && _archivePerGroupRadio.Checked;

            if (usePerInterval)
            {
                var intervals = _archiveIntervals.ToList();
                if (intervals.Count == 0)
                {
                    ShowWarning("Intervalli TXT", "Nessun intervallo valido caricato dal file TXT.");
                    return;
                }

                // Usa sempre i gruppi pacchetti selezionati per determinare quali pacchetti
                // includere in ciascun intervallo (e in ciascuna telecamera/gruppo).
                var intervalGroupOptions = GetSelectedBaseNameOptions().ToList();
                bool hasSelectedGroups = intervalGroupOptions.Count > 0;

                if (!hasSelectedGroups && !_currentArchiveSelection.HasPackages)
                {
                    ShowWarning("Pacchetti mancanti", "Nessun pacchetto corrisponde ai filtri attuali.");
                    return;
                }

                foreach (var interval in intervals)
                {
                    var intervalData = new ExportCommonData
                    {
                        BaseExportPath = data.BaseExportPath,
                        Procedimento = data.Procedimento,
                        Rit = data.Rit,
                        Magistrato = data.Magistrato,
                        IdLavoro = data.IdLavoro,
                        Target = data.Target,
                        Procura = data.Procura,
                        PasswordForMetadata = data.PasswordForMetadata,
                        EncryptionPassword = data.EncryptionPassword,
                        UseEncryption = data.UseEncryption,
                        StartTime = interval.Start,
                        EndTime = interval.End
                    };

                    string intervalSuffix = interval.Start.ToString("yyyyMMdd-HHmm");
                    string targetWithInterval = string.IsNullOrWhiteSpace(data.Target)
                        ? intervalSuffix
                        : $"{data.Target} {intervalSuffix}";

                    if (perGroup && hasSelectedGroups)
                    {
                        // Caso combinato: per intervallo + per gruppo pacchetti (per telecamera)
                        // --> crea una cartella/job per ogni combinazione (intervallo, gruppo).
                        foreach (var option in intervalGroupOptions)
                        {
                            if (option == null || string.IsNullOrWhiteSpace(option.BaseName))
                                continue;

                            var selection = BuildArchiveSelection(option.BaseName, interval.Start, interval.End);
                            if (!selection.HasPackages)
                                continue;

                            string baseDestPathForInterval;
                            try
                            {
                                baseDestPathForInterval = BuildExportDestinationPath(
                                    intervalData.BaseExportPath,
                                    intervalData.Procedimento,
                                    intervalData.Rit,
                                    targetWithInterval,
                                    intervalData.IdLavoro,
                                    isMultiLaunch: false,
                                    multiLaunchIndex: 0);
                            }
                            catch (Exception ex)
                            {
                                ShowError("Percorso di destinazione", $"Impossibile creare la cartella di destinazione per l'intervallo {interval.Start:dd/MM/yyyy HH:mm} - {interval.End:dd/MM/yyyy HH:mm}:\n{ex.Message}");
                                continue;
                            }

                            string parentDir = Path.GetDirectoryName(baseDestPathForInterval);
                            if (string.IsNullOrWhiteSpace(parentDir))
                                parentDir = intervalData.BaseExportPath;

                            string baseFolderName = Path.GetFileName(baseDestPathForInterval);
                            string groupSegment = PrepareExportSegment(option.BaseName, replaceSlashWithDash: true, replaceSpacesWithUnderscore: true, fallback: "GRP");
                            string groupFolderName = $"{baseFolderName}_{groupSegment}";
                            groupFolderName = ReplaceInvalidFileNameChars(groupFolderName);
                            groupFolderName = CollapseRepeatedCharacters(groupFolderName, '_').Trim('_');

                            string groupDestPath = Path.Combine(parentDir, groupFolderName);
                            groupDestPath = EnsureUniquePath(groupDestPath, isDirectory: true);

                            string groupPackagesRoot = Path.Combine(groupDestPath, "Client Files", "Data", "Mediadata");
                            try
                            {
                                Directory.CreateDirectory(groupPackagesRoot);
                            }
                            catch (Exception ex)
                            {
                                ShowError("Cartella pacchetti", $"Impossibile preparare \"{groupPackagesRoot}\" per il gruppo '{option.BaseName}' e l'intervallo {interval.Start:dd/MM/yyyy HH:mm} - {interval.End:dd/MM/yyyy HH:mm}:\n{ex.Message}");
                                continue;
                            }

                            var groupPackages = selection.Packages?.ToList() ?? new List<ArchivePackageInfo>();
                            if (groupPackages.Count == 0)
                                continue;

                            StartManualArchiveJob(intervalData, groupDestPath, groupPackagesRoot, groupPackages);
                        }
                    }
                    else
                    {
                        // Solo per intervallo (tutti i gruppi insieme): aggrega i pacchetti per
                        // tutti i gruppi selezionati e filtrali per l'intervallo.
                        var aggregatedPackages = new List<ArchivePackageInfo>();

                        if (hasSelectedGroups)
                        {
                            foreach (var option in intervalGroupOptions)
                            {
                                if (option == null || string.IsNullOrWhiteSpace(option.BaseName))
                                    continue;

                                var selection = BuildArchiveSelection(option.BaseName, interval.Start, interval.End);
                                if (!selection.HasPackages || selection.Packages == null)
                                    continue;

                                aggregatedPackages.AddRange(selection.Packages);
                            }
                        }
                        else if (_currentArchiveSelection.HasPackages && _currentArchiveSelection.Packages != null)
                        {
                            // Fallback: usa la selezione corrente ma filtra i pacchetti per timestamp
                            foreach (var pkg in _currentArchiveSelection.Packages)
                            {
                                if (pkg == null)
                                    continue;

                                if (pkg.Timestamp >= interval.Start && pkg.Timestamp <= interval.End)
                                    aggregatedPackages.Add(pkg);
                            }
                        }

                        if (aggregatedPackages.Count == 0)
                            continue;

                        // Rimuovi duplicati per percorso e ordina per timestamp
                        var unique = aggregatedPackages
                            .OrderBy(p => p.Timestamp)
                            .ThenBy(p => p.FullPath, StringComparer.OrdinalIgnoreCase)
                            .GroupBy(p => p.FullPath, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First())
                            .ToList();

                        string destPath;
                        try
                        {
                            destPath = BuildExportDestinationPath(
                                intervalData.BaseExportPath,
                                intervalData.Procedimento,
                                intervalData.Rit,
                                targetWithInterval,
                                intervalData.IdLavoro,
                                isMultiLaunch: false,
                                multiLaunchIndex: 0);

                            string packagesRoot = Path.Combine(destPath, "Client Files", "Data", "Mediadata");
                            Directory.CreateDirectory(packagesRoot);

                            StartManualArchiveJob(intervalData, destPath, packagesRoot, unique);
                        }
                        catch (Exception ex)
                        {
                            ShowError("Archiviazione per intervallo", $"Impossibile preparare la cartella per l'intervallo {interval.Start:dd/MM/yyyy HH:mm} - {interval.End:dd/MM/yyyy HH:mm}:\n{ex.Message}");
                            continue;
                        }
                    }
                }

                return;
            }

            string baseDestPath;
            try
            {
                baseDestPath = BuildExportDestinationPath(
                    data.BaseExportPath,
                    data.Procedimento,
                    data.Rit,
                    data.Target,
                    data.IdLavoro,
                    isMultiLaunch: false,
                    multiLaunchIndex: 0);
            }
            catch (Exception ex)
            {
                ShowError("Percorso di destinazione", $"Impossibile creare la cartella di destinazione:\n{ex.Message}");
                return;
            }

            perGroup = _archivePerGroupRadio != null && _archivePerGroupRadio.Checked;

            // Se non richiesto il per-gruppo, mantieni il comportamento attuale (un solo job)
            if (!perGroup)
            {
                string packagesRoot = Path.Combine(baseDestPath, "Client Files", "Data", "Mediadata");
                try
                {
                    Directory.CreateDirectory(packagesRoot);
                }
                catch (Exception ex)
                {
                    ShowError("Cartella pacchetti", $"Impossibile preparare \"{packagesRoot}\":\n{ex.Message}");
                    return;
                }

                var packagesToCopy = _currentArchiveSelection.Packages.ToList();
                if (packagesToCopy.Count == 0)
                {
                    ShowWarning("Pacchetti mancanti", "Nessun pacchetto corrisponde ai filtri attuali.");
                    return;
                }

                StartManualArchiveJob(data, baseDestPath, packagesRoot, packagesToCopy);
                return;
            }

            var selectedOptions = GetSelectedBaseNameOptions().ToList();
            if (selectedOptions.Count == 0)
            {
                // Fallback di sicurezza: se per qualche motivo non ci sono opzioni, usa il comportamento standard
                string packagesRoot = Path.Combine(baseDestPath, "Client Files", "Data", "Mediadata");
                try
                {
                    Directory.CreateDirectory(packagesRoot);
                }
                catch (Exception ex)
                {
                    ShowError("Cartella pacchetti", $"Impossibile preparare \"{packagesRoot}\":\n{ex.Message}");
                    return;
                }

                var packagesToCopy = _currentArchiveSelection.Packages.ToList();
                if (packagesToCopy.Count == 0)
                {
                    ShowWarning("Pacchetti mancanti", "Nessun pacchetto corrisponde ai filtri attuali.");
                    return;
                }

                StartManualArchiveJob(data, baseDestPath, packagesRoot, packagesToCopy);
                return;
            }

            DateTime? filterStart = ParseArchiveDate(_archiveStartTextBox);
            DateTime? filterEnd = ParseArchiveDate(_archiveEndTextBox);
            bool multipleGroups = selectedOptions.Count > 1;

            foreach (var option in selectedOptions)
            {
                if (option == null || string.IsNullOrWhiteSpace(option.BaseName))
                    continue;

                var selection = BuildArchiveSelection(option.BaseName, filterStart, filterEnd);
                if (!selection.HasPackages)
                    continue;

                string groupDestPath;
                if (!multipleGroups)
                {
                    groupDestPath = baseDestPath;
                }
                else
                {
                    string parentDir = Path.GetDirectoryName(baseDestPath);
                    if (string.IsNullOrWhiteSpace(parentDir))
                        parentDir = data.BaseExportPath;

                    string baseFolderName = Path.GetFileName(baseDestPath);
                    string groupSegment = PrepareExportSegment(option.BaseName, replaceSlashWithDash: true, replaceSpacesWithUnderscore: true, fallback: "GRP");
                    string groupFolderName = $"{baseFolderName}_{groupSegment}";
                    groupFolderName = ReplaceInvalidFileNameChars(groupFolderName);
                    groupFolderName = CollapseRepeatedCharacters(groupFolderName, '_').Trim('_');

                    groupDestPath = Path.Combine(parentDir, groupFolderName);
                    groupDestPath = EnsureUniquePath(groupDestPath, isDirectory: true);
                }

                string groupPackagesRoot = Path.Combine(groupDestPath, "Client Files", "Data", "Mediadata");
                try
                {
                    Directory.CreateDirectory(groupPackagesRoot);
                }
                catch (Exception ex)
                {
                    ShowError("Cartella pacchetti", $"Impossibile preparare \"{groupPackagesRoot}\" per il gruppo '{option.BaseName}':\n{ex.Message}");
                    continue;
                }

                var groupPackages = selection.Packages?.ToList() ?? new List<ArchivePackageInfo>();
                if (groupPackages.Count == 0)
                    continue;

                StartManualArchiveJob(data, groupDestPath, groupPackagesRoot, groupPackages);
            }
        }

        private void StartManualArchiveJob(ExportCommonData data, string destinationPath, string packagesRoot, IReadOnlyList<ArchivePackageInfo> packages)
        {
            if (data == null || string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(packagesRoot))
                return;
            if (packages == null || packages.Count == 0)
                return;

            string jobId = GenerateJobId(data.IdLavoro);
            string displayName = BuildManualArchiveJobDisplayName();
            string archiveDescriptor = string.IsNullOrWhiteSpace(_archiveRootPath) ? "ARCHIVIO" : _archiveRootPath;
            long selectionBytes = packages.Sum(p => Math.Max(0, p.SizeBytes));

            var job = new ExportJob
            {
                Id = jobId,
                CameraName = displayName,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
                Path = destinationPath,
                Password = data.PasswordForMetadata,
                ProcedimentoPenale = data.Procedimento,
                RitSpec = data.Rit,
                Target = data.Target,
                Magistrato = data.Magistrato,
                IdLavoro = data.IdLavoro,
                Procura = data.Procura,
                Status = "In coda",
                Progress = 0,
                QueuedAtUtc = DateTime.UtcNow,
                LastActivityUtc = DateTime.UtcNow,
                LastProgressUtc = DateTime.UtcNow,
                IsQueued = true,
                IsManualArchive = true,
                ServerAddress = archiveDescriptor
            };

            InitializeJobLog(job, "queued", "Archiviazione archivio accodata");
            AppendJobLogEntry(job, "manual-context", new Dictionary<string, object>
            {
                ["sourceRoot"] = _archiveRootPath,
                ["destination"] = destinationPath,
                ["packages"] = packages.Count,
                ["bytes"] = selectionBytes
            });

            try
            {
                _dataService.RecordJobStarted(new ExportDataService.JobSnapshot
                {
                    JobId = job.Id,
                    IdLavoro = job.IdLavoro,
                    CameraName = job.CameraName,
                    StartTime = DateTime.Now,
                    EndTime = null,
                    RequestedStartTime = job.StartTime,
                    RequestedEndTime = job.EndTime,
                    Status = job.Status,
                    Progress = job.Progress,
                    OutputFolder = job.Path,
                    OutputPassword = job.Password,
                    ProcedimentoPenale = job.ProcedimentoPenale,
                    RitSpec = job.RitSpec,
                    Target = job.Target,
                    Magistrato = job.Magistrato,
                    Procura = job.Procura,
                    ServerAddress = job.ServerAddress,
                    Note = "Lavorazione da archivio manuale",
                    CreatedBy = Environment.UserName
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive] Errore registrazione job archivio: {ex.Message}");
            }

            lock (_exportLock)
            {
                _queuedExports.Add(job);
            }

            LoadInCorsoData();

            var context = new ManualArchiveJobContext
            {
                SourceRoot = _archiveRootPath,
                DestinationPath = destinationPath,
                PackagesRoot = packagesRoot,
                Packages = packages
            };

            Task.Run(() => RunManualArchiveJob(job, context));
            NotifyArchiveStart(job);
        }

        private void RunManualArchiveJob(ExportJob job, ManualArchiveJobContext context)
        {
            if (job == null || context == null)
                return;

            // Prima di iniziare la copia effettiva dei pacchetti, verifica che i
            // percorsi di destinazione previsti non superino il limite di Windows.
            // Questo evita errori a metà archiviazione per path troppo lunghi.
            if (!CheckManualArchivePathLengths(job, context))
            {
                // L'utente ha scelto di annullare a causa di percorsi troppo lunghi.
                return;
            }

            lock (_exportLock)
            {
                if (_queuedExports.Contains(job))
                    _queuedExports.Remove(job);
                if (!_activeExports.Contains(job))
                    _activeExports.Add(job);
            }

            job.IsQueued = false;
            job.Status = "In corso";
            job.StartedAtUtc = DateTime.UtcNow;
            job.LastActivityUtc = job.StartedAtUtc;
            job.LastProgressUtc = job.StartedAtUtc;

            AppendJobLogEntry(job, "started", new Dictionary<string, object>
            {
                ["message"] = "Copia archivi locale avviata",
                ["path"] = context.PackagesRoot
            });
            AppendJobLogEntry(job, "manual-copy-start", new Dictionary<string, object>
            {
                ["sourceRoot"] = context.SourceRoot,
                ["packagesRoot"] = context.PackagesRoot,
                ["destination"] = context.DestinationPath,
                ["packageCount"] = context.Packages?.Count ?? 0
            });

            UpdateJobProgressUI(job);
            ExecuteOnUiThread(LoadInCorsoData);

            var errors = new List<string>();
            int copied = 0;
            int total = Math.Max(context.Packages?.Count ?? 0, 1);

            AppendJobLogEntry(job, "clientpayload-copy-start", new Dictionary<string, object>
            {
                ["destination"] = context.DestinationPath
            });
            try
            {
                CopyClientPayloadToDestination(context.DestinationPath);
                AppendJobLogEntry(job, "clientpayload-copy-complete", new Dictionary<string, object>
                {
                    ["destination"] = context.DestinationPath
                });
            }
            catch (Exception ex)
            {
                errors.Add($"ClientPayload: {ex.Message}");
                AppendJobLogEntry(job, "clientpayload-copy-error", new Dictionary<string, object>
                {
                    ["destination"] = context.DestinationPath,
                    ["error"] = ex.Message
                });
            }

            var metadataErrors = CopyArchiveMetadataFiles(context.SourceRoot, context.PackagesRoot);
            if (metadataErrors.Count > 0)
            {
                errors.AddRange(metadataErrors);
            }
            AppendJobLogEntry(job, "metadata-copy", new Dictionary<string, object>
            {
                ["destination"] = context.PackagesRoot,
                ["errors"] = metadataErrors.Count
            });

            try
            {
                if (context.Packages != null)
                {
                    foreach (var package in context.Packages)
                    {
                        if (package == null)
                            continue;

                        string packageName = package.BaseName ?? Path.GetFileName(package.FullPath) ?? "Pacchetto";
                        AppendJobLogEntry(job, "package-copy-start", new Dictionary<string, object>
                        {
                            ["package"] = packageName,
                            ["timestamp"] = package.Timestamp.ToString("o"),
                            ["size"] = FormatSize(package.SizeBytes)
                        });

                        try
                        {
                            CopyArchivePackage(package, context.PackagesRoot);
                            copied++;
                            AppendJobLogEntry(job, "package-copy-complete", new Dictionary<string, object>
                            {
                                ["package"] = packageName,
                                ["copied"] = copied
                            });
                        }
                        catch (Exception ex)
                        {
                            string name = packageName;
                            errors.Add($"{name}: {ex.Message}");
                            AppendJobLogEntry(job, "package-copy-error", new Dictionary<string, object>
                            {
                                ["package"] = name,
                                ["error"] = ex.Message
                            });
                        }

                        UpdateManualJobProgress(job, copied, total);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Errore di copia: {ex.Message}");
            }

            // Prova a generare il file Project.scp per il player partendo dai pacchetti copiati
            try
            {
                TryCreateManualArchiveProjectFile(job, context);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive][Project] Errore durante la creazione del Project.scp per il job {job?.Id}: {ex.Message}");
                errors.Add($"Project.scp: {ex.Message}");
            }

            job.Progress = 100;
            job.LastProgressUtc = DateTime.UtcNow;
            job.LastActivityUtc = job.LastProgressUtc;
            UpdateJobProgressUI(job);
            AppendJobLogEntry(job, "manual-copy-summary", new Dictionary<string, object>
            {
                ["copiedPackages"] = copied,
                ["totalPackages"] = context.Packages?.Count ?? 0,
                ["errors"] = errors.Count
            });

            if (errors.Count == 0)
            {
                FinalizeJobCompletion(job, null);
            }
            else
            {
                if (job.Errors == null)
                    job.Errors = new List<string>();
                job.Errors.AddRange(errors);
                FinalizeJobCompletion(job, null);
            }

            if (errors != null && errors.Count > 0)
            {
                ShowManualArchiveSummary(context.PackagesRoot, copied, context.Packages?.Count ?? 0, errors);
            }
        }

        /// <summary>
        /// Genera un file Project.scp minimale per le archiviazioni manuali,
        /// in modo che il player trovi subito le telecamere e la vista preconfigurata.
        /// </summary>
        private void TryCreateManualArchiveProjectFile(ExportJob job, ManualArchiveJobContext context)
        {
            if (job == null || string.IsNullOrWhiteSpace(job.Path))
                return;

            string clientFilesRoot = Path.Combine(job.Path, "Client Files");
            if (!Directory.Exists(clientFilesRoot))
            {
                Debug.WriteLine($"[Archive][Project] Client Files non trovato per il job {job?.Id}, skip Project.scp.");
                return;
            }

            string archiveFolderName = new DirectoryInfo(job.Path).Name;
            if (string.IsNullOrWhiteSpace(archiveFolderName))
                archiveFolderName = "Project";

            string templatePath = Path.Combine(clientFilesRoot, "Progetto esportato.scp");
            if (!File.Exists(templatePath))
            {
                templatePath = Path.Combine(ClientFilesDirectory, "Progetto esportato.scp");
                if (!File.Exists(templatePath))
                {
                    Debug.WriteLine($"[Archive][Project] Template Project.scp non trovato per il job {job?.Id}.");
                    return;
                }
            }

            string destinationPath = Path.Combine(clientFilesRoot, $"{archiveFolderName}.scp");

            try
            {
                File.Copy(templatePath, destinationPath, true);

                if (!templatePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase) &&
                    templatePath.StartsWith(clientFilesRoot, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(templatePath); }
                    catch { }
                }

                Debug.WriteLine($"[Archive][Project] Copiato Project.scp in {destinationPath} per il job {job?.Id}.");
                AppendJobLogEntry(job, "project-template", new Dictionary<string, object>
                {
                    ["destination"] = destinationPath
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive][Project] Errore copia Project.scp per il job {job?.Id}: {ex.Message}");
            }
        }

        private void UpdateManualJobProgress(ExportJob job, int completedPackages, int totalPackages)
        {
            if (job == null)
                return;

            totalPackages = Math.Max(1, totalPackages);
            completedPackages = Math.Max(0, completedPackages);

            int progress = (int)Math.Round(completedPackages * 100.0 / totalPackages);
            progress = Math.Max(0, Math.Min(99, progress));

            job.Progress = progress;
            job.Status = "In corso";
            job.LastActivityUtc = DateTime.UtcNow;
            job.LastProgressUtc = job.LastActivityUtc;

            UpdateJobProgressUI(job);
        }

        private void ShowManualArchiveSummary(string destinationRoot, int copied, int total, List<string> errors)
        {
            void Show()
            {
                var builder = new StringBuilder();
                builder.AppendLine($"Copiati {copied}/{Math.Max(total, 0)} pacchetti in \"{destinationRoot}\".");

                if (errors != null && errors.Count > 0)
                {
                    builder.AppendLine().AppendLine("Elementi con problemi:");
                    foreach (var error in errors.Take(5))
                        builder.AppendLine($"- {error}");

                    if (errors.Count > 5)
                        builder.AppendLine($"... altri {errors.Count - 5} elementi non copiati.");
                }

                builder.AppendLine().AppendLine("L'utente:");
                builder.AppendLine("- lancia la Player dal disco dell'archivio,");
                builder.AppendLine("- fa Open Database sulla cartella di archivio,");
                builder.AppendLine("- inserisce la password del DB (se cifrato),");
                builder.AppendLine("- crea/gestisce le viste,");
                builder.AppendLine("- eventualmente imposta una password di progetto.");

                var icon = (errors == null || errors.Count == 0) ? DarkDialogIcon.Success : DarkDialogIcon.Warning;
                ShowDarkDialog("Archiviazione completata", builder.ToString().Trim(), icon, DarkDialogButtons.Ok);
            }

            ExecuteOnUiThread(Show);
        }

        private string BuildManualArchiveJobDisplayName()
        {
            if (_selectedArchiveBaseNames == null || _selectedArchiveBaseNames.Count == 0)
                return "Archivio manuale";

            var preview = _selectedArchiveBaseNames
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            bool hasMore = _selectedArchiveBaseNames.Count > preview.Count;
            string suffix = hasMore ? "…" : string.Empty;

            return $"Archivio: {string.Join(", ", preview)}{suffix}";
        }

        /// <summary>
        /// Costruisce il percorso di destinazione per un singolo pacchetto di archivio,
        /// applicando lo stesso naming usato in CopyArchivePackage.
        /// </summary>
        private string BuildArchivePackageDestinationPath(ArchivePackageInfo package, string destinationRoot)
        {
            if (package == null || string.IsNullOrWhiteSpace(destinationRoot))
                return null;

            string friendlyName = Path.GetFileName(package.FullPath);
            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                string basePart = !string.IsNullOrWhiteSpace(package.BaseName)
                    ? package.BaseName
                    : "Pacchetto";
                string timePart = package.Timestamp != default
                    ? package.Timestamp.ToString("yyyyMMddTHHmmss")
                    : DateTime.Now.ToString("yyyyMMddTHHmmss");
                friendlyName = $"{basePart}_{timePart}";
            }

            friendlyName = ReplaceInvalidFileNameChars(friendlyName);
            string destinationPath = Path.Combine(destinationRoot, friendlyName);
            destinationPath = EnsureUniquePath(destinationPath, package.IsDirectory);
            return destinationPath;
        }

        /// <summary>
        /// Verifica la lunghezza massima dei percorsi che verranno creati da un job
        /// di archiviazione manuale (da archivio), stimando anche i file interni ai
        /// pacchetti di tipo cartella. Se un percorso potenziale supera WindowsMaxPathLength,
        /// mostra un avviso prima di iniziare le copie e permette all'utente di annullare.
        /// </summary>
        private bool CheckManualArchivePathLengths(ExportJob job, ManualArchiveJobContext context)
        {
            try
            {
                if (job == null || context == null)
                    return true;

                var packages = context.Packages;
                if (packages == null || packages.Count == 0)
                    return true;

                // Percorso base: cartella job + "Client Files\\Data\\Mediadata" + nome pacchetto
                string packagesRoot = context.PackagesRoot;
                if (string.IsNullOrWhiteSpace(packagesRoot))
                {
                    packagesRoot = Path.Combine(context.DestinationPath ?? job.Path ?? string.Empty, "Client Files", "Data", "Mediadata");
                }

                int maxLength = 0;
                string worstPath = null;

                foreach (var package in packages)
                {
                    if (package == null || string.IsNullOrWhiteSpace(package.FullPath))
                        continue;

                    string packageDestRoot = BuildArchivePackageDestinationPath(package, packagesRoot);
                    if (string.IsNullOrWhiteSpace(packageDestRoot))
                        continue;

                    // Se il pacchetto è un file singolo, il path finale è già packageDestRoot.
                    if (!package.IsDirectory)
                    {
                        int len = packageDestRoot.Length;
                        if (len > maxLength)
                        {
                            maxLength = len;
                            worstPath = packageDestRoot;
                        }
                        continue;
                    }

                    // Se è una cartella, stimiamo i file che verranno creati sotto.
                    try
                    {
                        if (Directory.Exists(package.FullPath))
                        {
                            foreach (var file in Directory.EnumerateFiles(package.FullPath, "*", SearchOption.AllDirectories))
                            {
                                string relative = file.Substring(package.FullPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                string finalPath = Path.Combine(packageDestRoot, relative);
                                int len = finalPath.Length;
                                if (len > maxLength)
                                {
                                    maxLength = len;
                                    worstPath = finalPath;
                                }

                                if (maxLength > WindowsMaxPathLength + 32)
                                {
                                    // Fermati presto: è chiaramente oltre la soglia.
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // In caso di errore di accesso stimiamo solo il path della cartella radice.
                        int len = packageDestRoot.Length;
                        if (len > maxLength)
                        {
                            maxLength = len;
                            worstPath = packageDestRoot;
                        }
                    }
                }

                // Considera anche i file ClientPayload che verranno copiati nella
                // radice del pacchetto (destinationPath). Questo è spesso il ramo
                // più profondo quando il nome della cartella di archivio è molto lungo.
                string clientPayloadRoot = ClientPayloadDirectory;
                string clientDestRoot = context.DestinationPath ?? job.Path ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(clientPayloadRoot) &&
                    Directory.Exists(clientPayloadRoot) &&
                    !string.IsNullOrWhiteSpace(clientDestRoot))
                {
                    try
                    {
                        // File
                        foreach (var file in Directory.EnumerateFiles(clientPayloadRoot, "*", SearchOption.AllDirectories))
                        {
                            string relative = file.Substring(clientPayloadRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            string finalPath = Path.Combine(clientDestRoot, relative);
                            int len = finalPath.Length;
                            if (len > maxLength)
                            {
                                maxLength = len;
                                worstPath = finalPath;
                            }

                            if (maxLength > WindowsMaxPathLength + 32)
                                break;
                        }

                        // Anche le cartelle, nel caso in cui non ci siano ancora file (o accesso limitato)
                        foreach (var dir in Directory.EnumerateDirectories(clientPayloadRoot, "*", SearchOption.AllDirectories))
                        {
                            string relative = dir.Substring(clientPayloadRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            string finalPath = Path.Combine(clientDestRoot, relative);
                            int len = finalPath.Length;
                            if (len > maxLength)
                            {
                                maxLength = len;
                                worstPath = finalPath;
                            }

                            if (maxLength > WindowsMaxPathLength + 32)
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Archive] Errore durante il controllo percorsi ClientPayload: {ex.Message}");
                    }
                }

                if (maxLength > WindowsMaxPathLength)
                {
                    Debug.WriteLine($"[Archive] Percorso stimato più lungo {maxLength} caratteri (limite {WindowsMaxPathLength}). Esempio: {worstPath}");

                    try
                    {
                        if (job != null)
                        {
                            AppendJobLogEntry(job, "path-length-warning", new Dictionary<string, object>
                            {
                                ["maxLength"] = maxLength,
                                ["limit"] = WindowsMaxPathLength,
                                ["examplePath"] = worstPath ?? string.Empty
                            });
                        }
                    }
                    catch (Exception logEx)
                    {
                        Debug.WriteLine($"[Archive] Errore durante la registrazione del path-length warning: {logEx.Message}");
                    }
                }

                // Non blocchiamo mai il job: l'obiettivo è segnalare il rischio ma
                // lasciare comunque procedere l'archiviazione.
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive] Errore durante il controllo lunghezza percorsi: {ex.Message}");
                // In caso di errore nel controllo, per sicurezza permetti comunque il proseguimento
                // per non bloccare l'utente con un falso positivo.
                return true;
            }
        }

        private void CopyArchivePackage(ArchivePackageInfo package, string destinationRoot)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.FullPath))
                return;

            string destinationPath = BuildArchivePackageDestinationPath(package, destinationRoot);
            if (string.IsNullOrWhiteSpace(destinationPath))
                return;

            if (package.IsDirectory)
            {
                CopyDirectoryRecursive(package.FullPath, destinationPath);
            }
            else
            {
                CopyFileSafe(package.FullPath, destinationPath);
            }
        }

        private static void CopyFileSafe(string sourceFile, string destinationFile)
        {
            string directory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(ToExtendedPath(directory));

            File.Copy(ToExtendedPath(sourceFile), ToExtendedPath(destinationFile), overwrite: false);
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException($"La cartella sorgente \"{sourceDir}\" non esiste.");

            Directory.CreateDirectory(ToExtendedPath(destDir));

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var targetFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(ToExtendedPath(file), ToExtendedPath(targetFile), overwrite: false);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var targetSubDir = Path.Combine(destDir, Path.GetFileName(directory));
                CopyDirectoryRecursive(directory, targetSubDir);
            }
        }

        private void CopyClientPayloadToDestination(string destinationPath)
        {
            // Copia tutto il contenuto di ClientPayload (Client Files, SmartClient-Player.exe, autorun.inf, ecc.)
            // nella radice del pacchetto, come fa DoPostProcessingForJob per i job da server
            if (string.IsNullOrWhiteSpace(ClientPayloadDirectory) || !Directory.Exists(ClientPayloadDirectory))
            {
                Debug.WriteLine("[Archive] ClientPayload non trovato.");
                return;
            }

            try
            {
                // Copia tutti i file e le cartelle dalla radice di ClientPayload
                foreach (var file in Directory.EnumerateFiles(ClientPayloadDirectory))
                {
                    string destFile = Path.Combine(destinationPath, Path.GetFileName(file));
                    File.Copy(ToExtendedPath(file), ToExtendedPath(destFile), overwrite: true);
                }

                foreach (var dir in Directory.EnumerateDirectories(ClientPayloadDirectory))
                {
                    string dirName = Path.GetFileName(dir);
                    string destDir = Path.Combine(destinationPath, dirName);
                    CopyDirectoryRecursive(dir, destDir);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive] Errore copia ClientPayload: {ex.Message}");
                ShowWarning("ClientPayload", $"Impossibile copiare i file client:\n{ex.Message}");
            }
        }

        private IList<string> CopyArchiveMetadataFiles(string sourceRoot, string destinationRoot)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                errors.Add("Cartella sorgente per config.xml/synckey non accessibile.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(destinationRoot))
            {
                errors.Add("Cartella Mediadata non valida.");
                return errors;
            }

            try
            {
                Directory.CreateDirectory(ToExtendedPath(destinationRoot));
            }
            catch (Exception ex)
            {
                errors.Add($"Impossibile preparare Mediadata: {ex.Message}");
                return errors;
            }

            Dictionary<string, string> availableFiles;
            try
            {
                availableFiles = Directory
                    .EnumerateFiles(sourceRoot, "*", SearchOption.TopDirectoryOnly)
                    .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                errors.Add($"Impossibile leggere i file metadata: {ex.Message}");
                return errors;
            }

            foreach (var fileName in ArchiveMetadataRequiredFiles)
            {
                if (!availableFiles.TryGetValue(fileName, out var sourceFile))
                {
                    errors.Add($"File {fileName} non trovato nel percorso selezionato.");
                    continue;
                }

                try
                {
                    string destinationFile = Path.Combine(destinationRoot, fileName);
                    File.Copy(ToExtendedPath(sourceFile), ToExtendedPath(destinationFile), true);
                }
                catch (Exception ex)
                {
                    errors.Add($"Errore copia {fileName}: {ex.Message}");
                }
            }

            return errors;
        }

        private string EnsureUniquePath(string basePath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return basePath;

            string directory = Path.GetDirectoryName(basePath) ?? string.Empty;
            string name = Path.GetFileName(basePath);
            string nameWithoutExt = isDirectory ? name : Path.GetFileNameWithoutExtension(basePath);
            string extension = isDirectory ? string.Empty : Path.GetExtension(basePath);

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

        /// <summary>
        /// Converte un percorso in formato "extended" (\\?\) per supportare path lunghi
        /// nelle API di file system di Windows, inclusi percorsi UNC.
        /// </summary>
        private static string ToExtendedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            // Già in formato esteso
            if (path.StartsWith(@"\\?\"))
                return path;

            // UNC: \\server\share\...
            if (path.StartsWith(@"\\"))
                return @"\\?\UNC\" + path.Substring(2);

            return @"\\?\" + path;
        }

        /// <summary>
        /// Restituisce selected cameras gia pronto.
        /// </summary>
        private List<Item> GetSelectedCameras()
        {
            var result = new List<Item>();

            if (_allCams == null || _allCams.Count == 0)
            {
                _selectedCameras.Clear();
                _selectedCamera = null;
                return result;
            }

            string cameraField = GetFieldValue("TelecameraTextBox");
            if (string.IsNullOrWhiteSpace(cameraField))
            {
                _selectedCameras.Clear();
                _selectedCamera = null;
                return result;
            }

            var tokens = cameraField
                .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var matches = _allCams
                .Where(cam => tokens.Any(token => string.Equals(cam.Name, token, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _selectedCameras.Clear();
            if (matches.Count > 0)
            {
                _selectedCameras.AddRange(matches);
                result.AddRange(matches);
            }

            _selectedCamera = _selectedCameras.FirstOrDefault();
            return result;
        }

        /// <summary>
        /// Restituisce selected camera gia pronto.
        /// </summary>
        private Item GetSelectedCamera()
        {
            return GetSelectedCameras().FirstOrDefault();
        }

        private sealed class ExportCommonData
        {
            public string BaseExportPath;
            public string Procedimento;
            public string Rit;
            public string Magistrato;
            public string IdLavoro;
            public string Target;
            public string Procura;
            public string PasswordForMetadata;
            public string EncryptionPassword;
            public bool UseEncryption;
            public DateTime StartTime;
            public DateTime EndTime;
        }

        private sealed class PreparedLaunchJob
        {
            public Item Camera;
            public string CameraName;
            public string JobId;
            public string DestPath;
            public int LaunchIndex;
        }

        /// <summary>
        /// Raccoglie common export data per lavorarci dopo.
        /// </summary>
        private ExportCommonData CollectCommonExportData()
        {
            string baseExportPath = _path;

            TextBox cartellaBox = contentPanel.Controls.Find("CartellaTextBox", true).FirstOrDefault() as TextBox;
            if (cartellaBox != null)
            {
                string manualPath = GetRealValue(cartellaBox);
                if (!string.IsNullOrWhiteSpace(manualPath))
                {
                    manualPath = manualPath.Trim().Replace('/', Path.DirectorySeparatorChar);
                    baseExportPath = manualPath;
                }
            }

            if (string.IsNullOrWhiteSpace(baseExportPath))
                {
                ShowWarning("Percorso mancante", "Indica una cartella di destinazione per salvare i file esportati.");
                return null;
            }

            if (!Directory.Exists(baseExportPath))
            {
                try
                {
                    Directory.CreateDirectory(baseExportPath);
                }
                catch (Exception ex)
                {
                    ShowWarning("Percorso non valido", $"La cartella indicata non è accessibile e non può essere creata.\nDettagli: {ex.Message}");
                    return null;
                }
            }

            _path = baseExportPath;

                TextBox procedimentoBox = contentPanel.Controls.Find("ProcedimentoTextBox", true).FirstOrDefault() as TextBox;
                TextBox ritSpecBox = contentPanel.Controls.Find("RitSpecTextBox", true).FirstOrDefault() as TextBox;
                TextBox magistratoBox = contentPanel.Controls.Find("MagistratoTextBox", true).FirstOrDefault() as TextBox;
                TextBox idLavoroBox = contentPanel.Controls.Find("IdLavoroTextBox", true).FirstOrDefault() as TextBox;
                TextBox targetBox = contentPanel.Controls.Find("TargetTextBox", true).FirstOrDefault() as TextBox;
                TextBox passwordBox = contentPanel.Controls.Find("PasswordTextBox", true).FirstOrDefault() as TextBox;
                TextBox inizioBox = contentPanel.Controls.Find("InizioTextBox", true).FirstOrDefault() as TextBox;
                TextBox fineBox = contentPanel.Controls.Find("FineTextBox", true).FirstOrDefault() as TextBox;
                DateTimePicker inizioPicker = contentPanel.Controls.Find("InizioPicker", true).FirstOrDefault() as DateTimePicker;
                DateTimePicker finePicker = contentPanel.Controls.Find("FinePicker", true).FirstOrDefault() as DateTimePicker;
                
                string procedimento = GetRealValue(procedimentoBox);
                if (!string.IsNullOrWhiteSpace(procedimento))
                {
                    procedimento = procedimento.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                }
                string rit = GetRealValue(ritSpecBox);
                if (!string.IsNullOrWhiteSpace(rit))
                {
                    rit = rit.Trim().ToUpperInvariant();
                }
                string magistrato = GetRealValue(magistratoBox);
                if (!string.IsNullOrWhiteSpace(magistrato))
                {
                    magistrato = magistrato.Trim();
                }
                string idLavoro = GetRealValue(idLavoroBox);
                if (!string.IsNullOrWhiteSpace(idLavoro))
                {
                    idLavoro = idLavoro.Trim();
                }
                string target = GetRealValue(targetBox);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    target = target.Trim();
                }
                string procura = string.IsNullOrWhiteSpace(_currentProcura) ? string.Empty : _currentProcura;
                
            DateTime startTime;
            DateTime endTime;

            if (_currentLaunchMode == LaunchMode.Archivio)
            {
                if (_currentArchiveSelection.HasPackages)
                {
                    startTime = _currentArchiveSelection.Packages.Min(p => p.Timestamp);
                    endTime = _currentArchiveSelection.Packages.Max(p => p.Timestamp);
                }
                else
                {
                    startTime = DateTime.Now;
                    endTime = startTime;
                }
            }
            else
            {
                if (!TryResolveExportInterval(inizioBox, fineBox, inizioPicker, finePicker, out startTime, out endTime))
                return null;

            if (startTime > endTime)
            {
                ShowWarning("Intervallo non valido", "L'orario di inizio deve essere precedente all'orario di fine.");
                return null;
                }
            }

            string encryptionPassword = GetRealValue(passwordBox).Trim();
            bool useEncryption = !string.IsNullOrWhiteSpace(encryptionPassword);
            string metadataPassword = encryptionPassword;

            return new ExportCommonData
            {
                BaseExportPath = baseExportPath,
                Procedimento = procedimento,
                Rit = rit,
                Magistrato = magistrato,
                IdLavoro = idLavoro,
                Target = target,
                Procura = procura,
                PasswordForMetadata = metadataPassword,
                EncryptionPassword = encryptionPassword,
                UseEncryption = useEncryption,
                StartTime = startTime,
                EndTime = endTime
            };
        }

        /// <summary>
        /// Esegue la logica try resolve export interval senza cambiare il comportamento.
        /// </summary>
        private bool TryResolveExportInterval(TextBox inizioBox, TextBox fineBox, DateTimePicker inizioPicker, DateTimePicker finePicker,
            out DateTime startTime, out DateTime endTime)
        {
            startTime = default;
            endTime = default;

                if (inizioBox != null && fineBox != null)
                {
                    string startText = GetRealValue(inizioBox);
                    string endText = GetRealValue(fineBox);
                    
                if (!TryParseUserDateTime(startText, out startTime))
                        {
                    ShowWarning("Data inizio non valida", "Inserisci la data/ora di inizio nel formato dd/MM/yyyy HH:mm.\nEsempio: 03/11/2025 12:00");
                    return false;
                }

                if (!TryParseUserDateTime(endText, out endTime))
                        {
                    ShowWarning("Data fine non valida", "Inserisci la data/ora di fine nel formato dd/MM/yyyy HH:mm.\nEsempio: 03/11/2025 13:00");
                    return false;
                        }

                return true;
                    }

            if (inizioPicker != null && finePicker != null)
                {
                    startTime = inizioPicker.Value;
                    endTime = finePicker.Value;
                return true;
                }

                    ShowError("Campi data non disponibili", "Non è stato possibile individuare i controlli per inserire le date di inizio e fine.");
            return false;
        }

        /// <summary>
        /// Esegue la logica try parse user date time senza cambiare il comportamento.
        /// </summary>
        private bool TryParseUserDateTime(string text, out DateTime value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim();
            if (DateTime.TryParseExact(normalized, "dd/MM/yyyy HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out value))
                return true;

            if (DateTime.TryParseExact(normalized, "dd/MM/yyyy HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out value))
                return true;

            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
                return true;

            return false;
        }

        /// <summary>
        /// Prepara launch jobs per l'esecuzione.
        /// </summary>
        private List<PreparedLaunchJob> PrepareLaunchJobs(ExportCommonData commonData, IReadOnlyList<Item> cameras, bool isMultiLaunch)
        {
            var prepared = new List<PreparedLaunchJob>();

            for (int index = 0; index < cameras.Count; index++)
            {
                var camera = cameras[index];
                if (camera == null)
                    continue;

                string destPath = BuildExportDestinationPath(
                    commonData.BaseExportPath,
                    commonData.Procedimento,
                    commonData.Rit,
                    commonData.Target,
                    commonData.IdLavoro,
                    isMultiLaunch,
                    index);

                try
                {
                    Directory.CreateDirectory(destPath);
                }
                catch (Exception ex)
                {
                    ShowError("Creazione cartella non riuscita", $"Non è stato possibile creare la cartella di destinazione:\n{destPath}\n\nDettagli: {ex.Message}");
                    return null;
                }

                string jobId = GenerateJobId(commonData.IdLavoro);
                string cameraName = camera.Name ?? $"Telecamera {index + 1}";

                prepared.Add(new PreparedLaunchJob
                {
                    Camera = camera,
                    CameraName = cameraName,
                    JobId = jobId,
                    DestPath = destPath,
                    LaunchIndex = index
                });
            }

            return prepared;
        }

        /// <summary>
        /// Esegue la logica enqueue export job senza cambiare il comportamento.
        /// </summary>
        private void EnqueueExportJob(PreparedLaunchJob preparedJob, ExportCommonData commonData, bool isMultiLaunch)
        {
            if (preparedJob == null || commonData == null || preparedJob.Camera == null)
                return;

            var queuedJob = new ExportJob
            {
                Id = preparedJob.JobId,
                CameraName = preparedJob.CameraName,
                StartTime = commonData.StartTime,
                EndTime = commonData.EndTime,
                Path = preparedJob.DestPath,
                Status = "In coda",
                Password = commonData.PasswordForMetadata,
                ProcedimentoPenale = commonData.Procedimento,
                RitSpec = commonData.Rit,
                Target = commonData.Target,
                Magistrato = commonData.Magistrato,
                IdLavoro = commonData.IdLavoro,
                Procura = commonData.Procura,
                Progress = 0,
                QueuedAtUtc = DateTime.UtcNow,
                LastActivityUtc = DateTime.UtcNow,
                LastProgressUtc = DateTime.UtcNow,
                IsQueued = true,
                ServerAddress = _currentServerAddress
            };

            InitializeJobLog(queuedJob, "queued", "Archiviazione accodata");

            try
            {
                _dataService.RecordJobStarted(new ExportDataService.JobSnapshot
                {
                    JobId = queuedJob.Id,
                    IdLavoro = queuedJob.IdLavoro,
                    CameraName = queuedJob.CameraName,
                    StartTime = DateTime.Now,
                    EndTime = null,
                    RequestedStartTime = queuedJob.StartTime,
                    RequestedEndTime = queuedJob.EndTime,
                    Status = queuedJob.Status,
                    Progress = queuedJob.Progress,
                    OutputFolder = queuedJob.Path,
                    OutputPassword = queuedJob.Password,
                    ProcedimentoPenale = queuedJob.ProcedimentoPenale,
                    RitSpec = queuedJob.RitSpec,
                    Target = queuedJob.Target,
                    Magistrato = queuedJob.Magistrato,
                    Procura = queuedJob.Procura,
                    ServerAddress = queuedJob.ServerAddress,
                    Note = null,
                    CreatedBy = Environment.UserName
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore registrazione job in coda {queuedJob.Id}: {ex.Message}");
            }

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["message"] = "Job accodato",
                    ["path"] = queuedJob.Path,
                    ["status"] = queuedJob.Status
                };
                _dataService.RecordJobEvent(queuedJob.Id, queuedJob.Status, queuedJob.Progress, "queued", payload);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore registrazione evento queue per {queuedJob.Id}: {ex.Message}");
            }

            var request = new ExportJobRequest
            {
                PreparedJob = preparedJob,
                CommonData = commonData,
                IsMultiLaunch = isMultiLaunch,
                Job = queuedJob
            };

            lock (_exportLock)
            {
                _queuedExports.Add(queuedJob);
                _pendingJobRequests.Enqueue(request);
            }

            LoadInCorsoData();
            TryStartNextQueuedJob();
        }

        /// <summary>
        /// Esegue la logica try start next queued job senza cambiare il comportamento.
        /// </summary>
        private void TryStartNextQueuedJob()
        {
            ExportJobRequest nextRequest = null;

            lock (_exportLock)
            {
                bool hasRunningJob = _activeExports.Any(job =>
                    job != null &&
                    !job.IsManualArchive &&
                    !job.IsCanceled &&
                    !IsFinalStatus(job.Status));
                if (hasRunningJob || _pendingJobRequests.Count == 0)
                    return;

                nextRequest = _pendingJobRequests.Dequeue();
                if (nextRequest?.Job != null)
                {
                    _queuedExports.Remove(nextRequest.Job);
                    nextRequest.Job.IsQueued = false;
                }
            }

            if (nextRequest == null)
                return;

            BeginQueuedExport(nextRequest);
        }

        /// <summary>
        /// Esegue la logica complete job with immediate error senza cambiare il comportamento.
        /// </summary>
        private void CompleteJobWithImmediateError(ExportJob job, string message, string detail = null, int? errorCode = null, string eventType = "validation-error", string statusOverride = "Errore")
        {
            if (job == null)
            {
                Debug.WriteLine($"CompleteJobWithImmediateError chiamato senza job. Messaggio: {message}");
                return;
            }

            if (string.IsNullOrEmpty(job.LogFilePath))
            {
                InitializeJobLog(job, "error", string.IsNullOrWhiteSpace(message) ? "Errore archiviazione" : message);
            }

            var payload = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(message))
                payload["message"] = message;
            if (!string.IsNullOrWhiteSpace(detail))
                payload["detail"] = detail;
            if (errorCode.HasValue)
                payload["code"] = errorCode.Value;

            if (payload.Count > 0)
            {
                AppendJobLogEntry(job, eventType, payload);
            }

            FinalizeJobWithError(job, string.IsNullOrWhiteSpace(message) ? "Errore archiviazione" : message, errorCode, detail, statusOverride);
        }

        /// <summary>
        /// Esegue la logica begin queued export senza cambiare il comportamento.
        /// </summary>
        private void BeginQueuedExport(ExportJobRequest request)
        {
            if (request == null)
                return;

            try
            {
                StartLegalArchive(request.PreparedJob.Camera, request.PreparedJob.LaunchIndex, request.IsMultiLaunch, request.CommonData, request.PreparedJob, request.Job);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore avvio job {request.Job?.Id}: {ex.Message}");
                if (request.Job != null)
                {
                    FinalizeJobWithError(request.Job, $"Errore avvio job: {ex.Message}", null, ex.ToString(), "Errore");
                }
            }
        }

        /// <summary>
        /// Avvia legal archive e gli step collegati.
        /// </summary>
        private void StartLegalArchive(Item selectedCamera, int multiLaunchIndex, bool isMultiLaunch, ExportCommonData commonData = null, PreparedLaunchJob preparedJob = null, ExportJob existingJob = null)
        {
            try
            {
                if (selectedCamera == null)
                {
                    const string message = "Nessuna telecamera valida selezionata.";
                    ShowError("Telecamera non valida", "Seleziona una telecamera disponibile prima di avviare l'archiviazione.");
                    CompleteJobWithImmediateError(existingJob, message, "Elemento telecamera assente o non selezionato.", null, "validation-error");
                    return;
                }

                var data = commonData ?? CollectCommonExportData();
                if (data == null)
                {
                    CompleteJobWithImmediateError(existingJob, "Validazione archiviazione non riuscita",
                        "CollectCommonExportData ha restituito null. Verificare i campi obbligatori.", null, "validation-error");
                    return;
                }

                if (!_isServerConnected || EnvironmentManager.Instance?.MasterSite?.ServerId == null)
                {
                    const string message = "Nessuna connessione attiva al server.";
                    ShowError("Connessione assente", "Connettiti al server prima di avviare un'archiviazione.");
                    CompleteJobWithImmediateError(existingJob, message,
                        "EnvironmentManager non segnala una connessione attiva al server.", null, "connection-error");
                    return;
                }

                string destPath = preparedJob?.DestPath;
                if (string.IsNullOrWhiteSpace(destPath))
                {
                    destPath = BuildExportDestinationPath(
                        data.BaseExportPath,
                        data.Procedimento,
                        data.Rit,
                        data.Target,
                        data.IdLavoro,
                        isMultiLaunch,
                        multiLaunchIndex);

                    try
                    {
                        Directory.CreateDirectory(destPath);
                    }
                    catch (Exception ex)
                    {
                        string message = $"Impossibile creare la cartella di destinazione:\n{destPath}\n\nDettagli: {ex.Message}";
                        ShowError("Creazione cartella non riuscita", message);
                        CompleteJobWithImmediateError(existingJob, "Errore creazione cartella di destinazione", ex.ToString(), null, "io-error");
                        return;
                    }
                }

                _currentExportOutputPath = destPath;

                string jobPublicId = preparedJob?.JobId ?? GenerateJobId(data.IdLavoro);
                string cameraName = preparedJob?.CameraName ?? selectedCamera.Name ?? $"Telecamera {multiLaunchIndex + 1}";

                var audioSources = selectedCamera.GetRelated() ?? new List<Item>();
                if (EnvironmentManager.Instance.MasterSite.ServerId.ServerType == ServerId.EnterpriseServerType)
                {
                    foreach (Item item in audioSources.ToList())
                    {
                        if (item.FQID.Kind != Kind.Microphone)
                            audioSources.Remove(item);
                    }
                }

                var newExporter = new VideoOS.Platform.Data.DBExporter(true)
                {
                    Encryption = data.UseEncryption,
                    EncryptionStrength = VideoOS.Platform.Data.EncryptionStrength.AES128,
                    Password = data.EncryptionPassword,
                    SignExport = false,
                    PreventReExport = false,
                    IncludeBookmarks = false
                };
                newExporter.Init();
                newExporter.Path = destPath;
                newExporter.CameraList = new List<Item> { selectedCamera };
                newExporter.AudioList = audioSources;

                bool startresult = newExporter.StartExport(data.StartTime.ToUniversalTime(), data.EndTime.ToUniversalTime());

                                if (startresult)
                                {
                    var exportJob = existingJob ?? new ExportJob
                    {
                        Id = jobPublicId,
                        CameraName = cameraName,
                        StartTime = data.StartTime,
                        EndTime = data.EndTime,
                        Path = destPath,
                        Password = data.PasswordForMetadata,
                        ProcedimentoPenale = data.Procedimento,
                        RitSpec = data.Rit,
                        Target = data.Target,
                        Magistrato = data.Magistrato,
                        IdLavoro = data.IdLavoro,
                        Procura = data.Procura,
                        ServerAddress = _currentServerAddress
                    };

                    if (string.IsNullOrWhiteSpace(exportJob.ServerAddress))
                        exportJob.ServerAddress = _currentServerAddress;

                    exportJob.Status = "In corso";
                    exportJob.Progress = 0;
                    exportJob.Exporter = newExporter;
                    exportJob.IsCanceled = false;
                    exportJob.IsQueued = false;
                    exportJob.StartedAtUtc = DateTime.UtcNow;
                    exportJob.LastActivityUtc = exportJob.StartedAtUtc;
                    exportJob.LastProgressUtc = exportJob.StartedAtUtc;

                    if (exportJob.Timer != null && exportJob.TimerHandler != null)
                    {
                        exportJob.Timer.Tick -= exportJob.TimerHandler;
                        exportJob.TimerHandler = null;
                    }

                    exportJob.Timer?.Stop();
                    exportJob.Timer?.Dispose();
                    exportJob.Timer = new Timer { Interval = 100 };
                    EventHandler tickHandler = (s, ev) => ShowProgressForJob(exportJob);
                    exportJob.TimerHandler = tickHandler;
                    exportJob.Timer.Tick += tickHandler;
                    exportJob.Timer.Start();

                    if (string.IsNullOrEmpty(exportJob.LogFilePath))
                    {
                        InitializeJobLog(exportJob);
                    }
                    else
                    {
                        exportJob.LastLoggedProgress = -1;
                        AppendJobLogEntry(exportJob, "started", new Dictionary<string, object>
                        {
                            ["message"] = "Archiviazione avviata",
                            ["path"] = exportJob.Path
                        });
                    }
                    AppendJobLogEntry(exportJob, "server-context", new Dictionary<string, object>
                    {
                        ["camera"] = exportJob.CameraName,
                        ["destination"] = exportJob.Path,
                        ["startUtc"] = data.StartTime.ToUniversalTime().ToString("o"),
                        ["endUtc"] = data.EndTime.ToUniversalTime().ToString("o"),
                        ["encryption"] = data.UseEncryption,
                        ["server"] = exportJob.ServerAddress
                    });
                    var cameraFqid = selectedCamera?.FQID;
                    string cameraObjectId = cameraFqid == null ? string.Empty : cameraFqid.ObjectId.ToString();
                    AppendJobLogEntry(exportJob, "export-session", new Dictionary<string, object>
                    {
                        ["cameraFqid"] = cameraObjectId,
                        ["audioSources"] = audioSources?.Count ?? 0,
                        ["useEncryption"] = data.UseEncryption
                    });

                    try
                    {
                        _dataService.RecordJobStarted(new ExportDataService.JobSnapshot
                        {
                            JobId = exportJob.Id,
                                IdLavoro = exportJob.IdLavoro,
                            CameraName = exportJob.CameraName,
                            StartTime = exportJob.StartedAtUtc == default(DateTime) ? DateTime.Now : exportJob.StartedAtUtc,
                            EndTime = null,
                            RequestedStartTime = data.StartTime,
                            RequestedEndTime = data.EndTime,
                            Status = exportJob.Status,
                            Progress = exportJob.Progress,
                            OutputFolder = exportJob.Path,
                            OutputPassword = exportJob.Password,
                            ProcedimentoPenale = exportJob.ProcedimentoPenale,
                            RitSpec = exportJob.RitSpec,
                            Target = exportJob.Target,
                            Magistrato = exportJob.Magistrato,
                            Procura = exportJob.Procura,
                            ServerAddress = exportJob.ServerAddress,
                            Note = null,
                            CreatedBy = Environment.UserName
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore registrazione avvio job {exportJob.Id}: {ex.Message}");
                    }

                    try
                    {
                        var payload = new Dictionary<string, object>
                        {
                            ["message"] = "Job avviato",
                            ["path"] = exportJob.Path,
                            ["status"] = exportJob.Status
                        };
                        _dataService.RecordJobEvent(exportJob.Id, exportJob.Status, exportJob.Progress, "started", payload);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore registrazione evento avvio job {exportJob.Id}: {ex.Message}");
                    }
                    
                    lock (_exportLock)
                    {
                        if (!_activeExports.Contains(exportJob))
                        _activeExports.Add(exportJob);
                        _exporter = newExporter;
                        _timer = exportJob.Timer;
                    }

                    ExecuteOnUiThread(ApplyLaunchConnectionState);

                    ShowTab(1);
                    UpdateArchiveStatus($"Archiviazione {data.IdLavoro} avviata...", 0);
                    LoadInCorsoData();
                    NotifyArchiveStart(exportJob);
                }
                else
                                {
                                        int lastError = newExporter.LastError;
                                        string lastErrorString = newExporter.LastErrorString;
                    
                    ShowError("Avvio export non riuscito", $"L'export non è partito.\nDettagli: {lastErrorString}\nCodice errore: {lastError}");
                    
                                        UpdateArchiveStatus($"Errore: {lastErrorString} ({lastError})", 0);
                    newExporter.EndExport();

                    var errorJob = existingJob ?? new ExportJob
                    {
                        Id = jobPublicId,
                        CameraName = cameraName,
                        StartTime = data.StartTime,
                        EndTime = data.EndTime,
                        Path = destPath,
                        Password = data.PasswordForMetadata,
                        ProcedimentoPenale = data.Procedimento,
                        RitSpec = data.Rit,
                        Target = data.Target,
                        Magistrato = data.Magistrato,
                        IdLavoro = data.IdLavoro,
                        Procura = data.Procura,
                        ServerAddress = _currentServerAddress
                    };

                    if (string.IsNullOrWhiteSpace(errorJob.ServerAddress))
                        errorJob.ServerAddress = _currentServerAddress;

                    errorJob.Status = "Errore";
                    errorJob.Progress = 0;
                    errorJob.LastActivityUtc = DateTime.UtcNow;
                    errorJob.LastProgressUtc = errorJob.LastActivityUtc;

                    if (string.IsNullOrEmpty(errorJob.LogFilePath))
                    {
                        InitializeJobLog(errorJob, "error", "Errore in avvio export");
                    }

                    string errorMessage = string.IsNullOrWhiteSpace(lastErrorString) ? "Errore avvio export" : lastErrorString;
                    string errorDetail = $"StartExport ha restituito false (codice {lastError})";

                        var payload = new Dictionary<string, object>
                        {
                        ["message"] = errorMessage,
                            ["code"] = lastError,
                            ["path"] = destPath
                        };

                    AppendJobLogEntry(errorJob, "error", payload);

                    try
                    {
                        _dataService.RecordJobEvent(errorJob.Id, "Errore", 0, "error", payload);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore registrazione evento errore job {errorJob.Id}: {ex.Message}");
                    }

                    FinalizeJobWithError(errorJob, errorMessage, lastError == 0 ? (int?)null : lastError, errorDetail, "Errore");
                                }
            }
            catch (Exception ex)
                        {
                                try
                                {
                                    EnvironmentManager.Instance.ExceptionDialog("Avvio Archiviazione", ex);
                                }
                                catch
                                {
                                    ShowError("Errore avvio archiviazione", $"Si è verificata un'eccezione durante l'avvio dell'archiviazione.\nDettagli: {ex.Message}");
                                }

                                CompleteJobWithImmediateError(existingJob, "Errore durante l'avvio dell'archiviazione", ex.ToString(), null, "exception");
                        }
        }

        /// <summary>
        /// Compone export destination path pronto all'uso.
        /// </summary>
        private string BuildExportDestinationPath(string basePath, string procedimento, string ritSpec, string target, string idLavoro, bool isMultiLaunch, int multiLaunchIndex)
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

            // Limita la lunghezza complessiva del percorso radice dell'archivio per
            // restare a distanza di sicurezza dal limite MAX_PATH di Windows, tenendo
            // conto anche delle sottocartelle (Client Files, Client, Plugin, ecc.).
            // L'idea è: basePath + "\\" + folderName <= (WindowsMaxPathLength - safetyMargin).
            const int safetyMargin = 100; // spazio riservato per sottocartelle e nomi file profondi
            int maxRootLength = WindowsMaxPathLength - safetyMargin;

            string combined = Path.Combine(basePath, folderName);
            if (combined.Length > maxRootLength && maxRootLength > (basePath.Length + 2))
            {
                int allowedFolderNameLength = maxRootLength - (basePath.Length + 1); // +1 per il separatore
                if (allowedFolderNameLength < folderName.Length && allowedFolderNameLength > 0)
                {
                    folderName = folderName.Substring(0, allowedFolderNameLength).TrimEnd('_');
                    if (string.IsNullOrWhiteSpace(folderName))
                        folderName = "EXPORT";
                }
            }

            return Path.Combine(basePath, folderName);
        }

        /// <summary>
        /// Prepara export segment per l'esecuzione.
        /// </summary>
        private string PrepareExportSegment(string value, bool replaceSlashWithDash, bool replaceSpacesWithUnderscore, string fallback)
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

        /// <summary>
        /// Esegue la logica replace invalid file name chars senza cambiare il comportamento.
        /// </summary>
        private string ReplaceInvalidFileNameChars(string value)
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

        /// <summary>
        /// Esegue la logica collapse repeated characters senza cambiare il comportamento.
        /// </summary>
        private string CollapseRepeatedCharacters(string value, char character)
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

        /// <summary>
        /// Esegue la logica parse date time senza cambiare il comportamento.
        /// </summary>
        private DateTime ParseDateTime(string dateTimeText)
        {
            // Prova prima con formato esatto dd/MM/yyyy HH:mm
            if (DateTime.TryParseExact(dateTimeText, "dd/MM/yyyy HH:mm", 
                                     System.Globalization.CultureInfo.InvariantCulture, 
                                     System.Globalization.DateTimeStyles.None, out DateTime result))
            {
                return result;
            }
            
            // Prova con parsing standard per maggiore flessibilità
            if (DateTime.TryParse(dateTimeText, out DateTime result2))
            {
                return result2;
            }
            
            // Se tutto fallisce, mostra errore
            throw new FormatException($"Formato data non valido: '{dateTimeText}'\nUsa il formato: dd/MM/yyyy HH:mm\nEsempio: 18/10/2025 12:00");
        }

        // Metodi per formattare i componenti del nome pacchetto
        /// <summary>
        /// Esegue la logica format procedimento senza cambiare il comportamento.
        /// </summary>
        private string FormatProcedimento(string input)
        {
            // Da "12345/2025/N" a "14343-2023-N"
            // Rimuove caratteri non validi e sostituisce "/" con "-"
            if (string.IsNullOrWhiteSpace(input))
                return "PROC";
                
            return input.Replace("/", "-").Trim();
        }

        /// <summary>
        /// Esegue la logica format rit senza cambiare il comportamento.
        /// </summary>
        private string FormatRit(string input)
        {
            // Da "RIT 1234/2025" a "343/2023"
            // Estrae solo numeri e anno
            if (string.IsNullOrWhiteSpace(input))
                return "N";
                
            // Rimuove "RIT " o "rit " se presente (case-insensitive)
            string cleaned = System.Text.RegularExpressions.Regex.Replace(input, "RIT ", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return cleaned;
        }

        /// <summary>
        /// Esegue la logica format target senza cambiare il comportamento.
        /// </summary>
        private string FormatTarget(string input)
        {
            // Da "Nome Cognome" a "CASA" o altro identificativo
            // Converti in maiuscolo e rimuovi spazi
            if (string.IsNullOrWhiteSpace(input))
                return "TARGET";
                
            return input.Replace(" ", "_").ToUpper().Trim();
        }


        /// <summary>
        /// Esegue la logica format id lavoro senza cambiare il comportamento.
        /// </summary>
        private string FormatIdLavoro(string input)
        {
            // Da "ID-12345" a "929221"
            // Estrae solo i numeri
            if (string.IsNullOrWhiteSpace(input))
                return "000000";
                
            // Rimuove "ID-", "ID ", "id-", "id " se presente (case-insensitive)
            string cleaned = System.Text.RegularExpressions.Regex.Replace(input, "ID-?\\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            
            // Estrai solo numeri
            string numbers = new string(cleaned.Where(char.IsDigit).ToArray());
            
            return string.IsNullOrEmpty(numbers) ? "000000" : numbers;
        }

        /// <summary>
        /// Gestisce l'evento click del controllo genera password per tenere la UI reattiva.
        /// </summary>
        private void GeneraPassword_Click(object sender, EventArgs e)
        {
            TextBox passwordBox = contentPanel.Controls.Find("PasswordTextBox", true).FirstOrDefault() as TextBox;
            if (passwordBox != null)
            {
                string password = GenerateRandomPassword(8);
                passwordBox.Text = password;
                passwordBox.ForeColor = Color.White;
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo telecamera button per tenere la UI reattiva.
        /// </summary>
        private void TelecameraButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_allCams == null || _allCams.Count == 0)
                {
                    ShowInfo("Selezione telecamere", "Nessuna telecamera disponibile. Connettiti al server per recuperare l'elenco.");
                    return;
                }

                using (var selector = new CameraSelectionForm(this, _allCams, _selectedCameras))
                {
                    if (selector.ShowDialog(this) == DialogResult.OK)
                    {
                        var selectedItems = selector.GetSelectedItems();
                        _selectedCameras.Clear();
                        if (selectedItems.Count > 0)
                            _selectedCameras.AddRange(selectedItems);

                        _selectedCamera = _selectedCameras.FirstOrDefault();
                        UpdateSelectedCamerasText();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Errore selezione telecamere", $"Si è verificato un problema durante la selezione delle telecamere.\nDettagli: {ex.Message}");
            }
        }

        /// <summary>
        /// Aggiorna selected cameras text e mantiene lo stato coerente.
        /// </summary>
        private void UpdateSelectedCamerasText()
        {
            var telecameraTextBox = contentPanel.Controls.Find("TelecameraTextBox", true).FirstOrDefault() as TextBox;
            if (telecameraTextBox == null)
                return;

            if (_selectedCameras.Count == 0)
            {
                telecameraTextBox.Text = "Seleziona una o più telecamere...";
                telecameraTextBox.ForeColor = Colors.TextPlaceholder;
            }
            else
            {
                var names = string.Join(", ", _selectedCameras
                    .Where(cam => cam != null)
                    .Select(cam => cam.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                telecameraTextBox.Text = names;
                telecameraTextBox.ForeColor = Colors.TextPrimary;
            }
        }

        /// <summary>
        /// Sincronizza selected cameras from text con lo stato attuale.
        /// </summary>
        private void SyncSelectedCamerasFromText()
        {
            _selectedCameras.Clear();
            _selectedCamera = null;
            if (_allCams == null || _allCams.Count == 0)
                return;

            GetSelectedCameras();
            UpdateSelectedCamerasText();
        }

        private sealed class CameraSelectionForm : Form
        {
            private const string SearchPlaceholder = "Cerca telecamere per nome...";
            private const string DescriptionText = "Seleziona una o più telecamere da associare all'archiviazione. Usa la ricerca per filtrare rapidamente e mantieni l'ordine dei job con i checkbox.";
            private const string MultiCameraWarningText = "ATTENZIONE: Selezionando più telecamere, il sistema eseguirà un'archiviazione separata per ciascuna telecamera selezionata.";
            private const int WindowWidth = 400;
            private const int MinWindowHeight = 320;
            private const int MaxWindowHeight = 760;
            private const int DefaultWindowHeight = 380;
            private const int MinListHeight = 200;
            private const int MaxListHeight = 400;
            private const int SearchBoxHeight = 22;


            private readonly MainForm _owner;
            private readonly TableLayoutPanel _mainLayout;
            private readonly int _contentPaddingLeft;
            private readonly int _contentPaddingRight;
            private readonly int _contentPaddingBottom;
            private readonly CheckedListBox _listBox;
            private readonly TextBox _searchBox;
            private readonly Label _summaryLabel;
            private readonly List<CameraEntry> _allEntries;
            private List<CameraEntry> _visibleEntries;
            private readonly Dictionary<Guid, CameraEntry> _entryLookup;
            private readonly HashSet<Guid> _checkedCameras;
            private readonly List<Guid> _checkedOrder;
            private readonly ToolTip _toolTip;
            private readonly Panel _listBorderPanel;
            private readonly Panel _listHostPanel;
            private int _lastTooltipIndex = -1;
            private bool _isPlaceholderActive;
            private bool _suppressItemCheckEvents;

            /// <summary>
            /// Costruttore di CameraSelectionForm, inizializza il contesto senza effetti collaterali.
            /// </summary>
            public CameraSelectionForm(MainForm owner, IEnumerable<Item> cameras, IEnumerable<Item> preselected)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                DoubleBuffered = true;
                Text = "Seleziona telecamere";
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(WindowWidth, DefaultWindowHeight);
                MinimumSize = new Size(WindowWidth, MinWindowHeight);
                MaximumSize = new Size(WindowWidth, MaxWindowHeight);
                BackColor = Color.FromArgb(18, 18, 22);
                ForeColor = Colors.TextPrimary;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowIcon = false;

                _checkedCameras = new HashSet<Guid>();
                _checkedOrder = new List<Guid>();
                if (preselected != null)
                {
                    foreach (var item in preselected)
                    {
                        if (item?.FQID == null)
                            continue;

                        var id = item.FQID.ObjectId;
                        if (_checkedCameras.Add(id))
                            _checkedOrder.Add(id);
                    }
                }

                _allEntries = BuildCameraEntries(cameras);
                _entryLookup = _allEntries
                    .Where(entry => entry.Item?.FQID != null)
                    .GroupBy(entry => entry.Item.FQID.ObjectId)
                    .ToDictionary(group => group.Key, group => group.First());

                _checkedCameras.RemoveWhere(id => !_entryLookup.ContainsKey(id));
                _checkedOrder.RemoveAll(id => !_entryLookup.ContainsKey(id));

                _visibleEntries = new List<CameraEntry>(_allEntries);

                _contentPaddingLeft = Math.Max(0, Spacing.SM - 2);
                _contentPaddingRight = _contentPaddingLeft + 16; // corridoio a destra leggermente più ampio
                _contentPaddingBottom = 0;

                int contentWidth = WindowWidth - (_contentPaddingLeft + _contentPaddingRight);

                _listBox = new CheckedListBox
                {
                    BorderStyle = BorderStyle.None,
                    CheckOnClick = true,
                    BackColor = Color.FromArgb(26, 26, 30),
                    ForeColor = Colors.TextPrimary,
                    IntegralHeight = false,
                    FormattingEnabled = true,
                    HorizontalScrollbar = true,
                    DisplayMember = nameof(CameraEntry.DisplayLabel),
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                    Dock = DockStyle.Fill,
                    MinimumSize = new Size(contentWidth - 2, MinListHeight),
                    MaximumSize = new Size(contentWidth - 2, MaxListHeight)
                };
                _listBox.ItemCheck += ListBox_ItemCheck;
                _listBox.MouseMove += ListBox_MouseMove;

                _searchBox = _owner.CreateModernTextBox(SearchPlaceholder);
                _searchBox.BackColor = Color.FromArgb(24, 24, 28);
                _searchBox.BorderStyle = BorderStyle.FixedSingle;
                _searchBox.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM);
                _searchBox.Margin = new Padding(0, 0, 0, Spacing.XS);
                _searchBox.MinimumSize = new Size(contentWidth, SearchBoxHeight);
                _searchBox.MaximumSize = new Size(contentWidth, SearchBoxHeight);
                _searchBox.Height = SearchBoxHeight;
                _searchBox.Dock = DockStyle.Fill;
                _searchBox.Enter += SearchBox_Enter;
                _searchBox.Leave += SearchBox_Leave;
                _searchBox.TextChanged += SearchBox_TextChanged;

                _summaryLabel = new Label
                {
                    AutoSize = true,
                    ForeColor = Colors.TextSecondary,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS),
                    Margin = new Padding(0, Spacing.XXS, 0, Spacing.XXS)
                };

                _toolTip = new ToolTip
                {
                    AutoPopDelay = 4000,
                    InitialDelay = 200,
                    ReshowDelay = 100,
                    UseFading = true,
                    UseAnimation = true,
                    BackColor = Color.FromArgb(48, 48, 52),
                    ForeColor = Color.White
                };

                _mainLayout = new TableLayoutPanel
                {
                    AutoSize = false,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    RowCount = 5,
                    Padding = new Padding(_contentPaddingLeft, Spacing.XXS, _contentPaddingRight, _contentPaddingBottom),
                    Dock = DockStyle.Fill
                };
                _mainLayout.ColumnStyles.Clear();
                _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, contentWidth));
                _mainLayout.RowStyles.Clear();
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.MinimumSize = new Size(WindowWidth, MinWindowHeight);

                var descriptionLabel = new Label
                {
                    Text = DescriptionText,
                    ForeColor = Colors.TextSecondary,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                    AutoSize = true,
                    MaximumSize = new Size(contentWidth, 0),
                    Margin = new Padding(0, 0, 0, Spacing.XXS)
                };

                var warningLabel = new Label
                {
                    Text = MultiCameraWarningText,
                    ForeColor = Colors.Error,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Bold),
                    AutoSize = true,
                    MaximumSize = new Size(contentWidth, 0),
                    Margin = new Padding(0, 0, 0, Spacing.XS)
                };

                _listBorderPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(40, 40, 44),
                    Padding = new Padding(1),
                    Margin = new Padding(0, 0, 0, Spacing.XXS),
                    MinimumSize = new Size(contentWidth, MinListHeight + 2),
                    MaximumSize = new Size(contentWidth, MaxListHeight + 2),
                    Width = contentWidth
                };
                _listHostPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(26, 26, 30),
                    MinimumSize = new Size(contentWidth - 2, MinListHeight),
                    MaximumSize = new Size(contentWidth - 2, MaxListHeight)
                };
                _listHostPanel.Controls.Add(_listBox);
                _listBorderPanel.Controls.Add(_listHostPanel);

                var summaryPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Margin = new Padding(0)
                };
                summaryPanel.Controls.Add(_summaryLabel);

                var footerPanel = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0),
                    Padding = new Padding(_contentPaddingLeft, Spacing.XS, _contentPaddingRight, 0),
                    BackColor = Color.Transparent
                };
                footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                footerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var footerSpacer = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    BackColor = Color.Transparent
                };
                footerPanel.Controls.Add(footerSpacer, 0, 0);

                var confirmButton = CreatePrimaryButton("OK");
                confirmButton.DialogResult = DialogResult.OK;
                confirmButton.Margin = new Padding(0);
                confirmButton.Dock = DockStyle.None;
                confirmButton.Anchor = AnchorStyles.Right;
                footerPanel.Controls.Add(confirmButton, 1, 0);

                _mainLayout.Controls.Add(descriptionLabel, 0, 0);
                _mainLayout.Controls.Add(warningLabel, 0, 1);
                _mainLayout.Controls.Add(_searchBox, 0, 2);
                _mainLayout.Controls.Add(_listBorderPanel, 0, 3);
                _mainLayout.Controls.Add(summaryPanel, 0, 4);
                _mainLayout.Controls.Add(footerPanel, 0, 5);

                Controls.Add(_mainLayout);

                AcceptButton = confirmButton;
                CancelButton = null;

                SetSearchPlaceholder();
                ApplyFilter(string.Empty);

                Shown += (s, e) => AdjustFormHeight();
            }

            /// <summary>
            /// Restituisce selected items gia pronto.
            /// </summary>
            public List<Item> GetSelectedItems()
            {
                var result = new List<Item>();
                foreach (var cameraId in _checkedOrder)
                {
                    if (_entryLookup.TryGetValue(cameraId, out var entry) &&
                        entry?.Item != null &&
                        _checkedCameras.Contains(cameraId))
                    {
                        result.Add(entry.Item);
                    }
                }
                return result;
            }

            /// <summary>
            /// Imposta search placeholder usando i parametri passati.
            /// </summary>
            private void SetSearchPlaceholder()
            {
                _isPlaceholderActive = true;
                _searchBox.Text = SearchPlaceholder;
                _searchBox.ForeColor = Colors.TextPlaceholder;
            }

            /// <summary>
            /// Gestisce l'evento enter del controllo search box per tenere la UI reattiva.
            /// </summary>
            private void SearchBox_Enter(object sender, EventArgs e)
            {
                if (_isPlaceholderActive)
                {
                    _isPlaceholderActive = false;
                    _searchBox.Text = string.Empty;
                    _searchBox.ForeColor = Colors.TextPrimary;
                }
            }

            /// <summary>
            /// Gestisce l'evento leave del controllo search box per tenere la UI reattiva.
            /// </summary>
            private void SearchBox_Leave(object sender, EventArgs e)
            {
                if (string.IsNullOrWhiteSpace(_searchBox.Text))
                {
                    SetSearchPlaceholder();
                    ApplyFilter(string.Empty);
                }
            }

            /// <summary>
            /// Gestisce l'evento text changed del controllo search box per tenere la UI reattiva.
            /// </summary>
            private void SearchBox_TextChanged(object sender, EventArgs e)
            {
                if (_isPlaceholderActive)
                    return;

                ApplyFilter(_searchBox.Text);
            }

            /// <summary>
            /// Applica filter alle impostazioni correnti.
            /// </summary>
            private void ApplyFilter(string filterText)
            {
                string filter = (filterText ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(filter) || string.Equals(filter, SearchPlaceholder, StringComparison.Ordinal))
                {
                    _visibleEntries = new List<CameraEntry>(_allEntries);
                }
                else
                {
                    string normalized = filter.ToLowerInvariant();
                    _visibleEntries = _allEntries
                        .Where(entry => entry.SearchKey.Contains(normalized))
                        .ToList();
                }

                PopulateList(_visibleEntries);
            }

            /// <summary>
            /// Esegue la logica populate list senza cambiare il comportamento.
            /// </summary>
            private void PopulateList(List<CameraEntry> entries)
            {
                _suppressItemCheckEvents = true;
                _listBox.BeginUpdate();
                _listBox.Items.Clear();

                foreach (var entry in entries)
                {
                    _listBox.Items.Add(entry);
                }

                for (int i = 0; i < _listBox.Items.Count; i++)
                {
                    if (_listBox.Items[i] is CameraEntry entry &&
                        entry.Item?.FQID != null &&
                        _checkedCameras.Contains(entry.Item.FQID.ObjectId))
                    {
                        _listBox.SetItemChecked(i, true);
                    }
                }

                _listBox.EndUpdate();
                _suppressItemCheckEvents = false;

                UpdateListHeight(entries.Count);
                UpdateSummary();
                _listBox.Invalidate();
                AdjustFormHeight();
            }

            /// <summary>
            /// Gestisce l'evento item check del controllo list box per tenere la UI reattiva.
            /// </summary>
            private void ListBox_ItemCheck(object sender, ItemCheckEventArgs e)
            {
                if (_suppressItemCheckEvents || e.Index < 0 || e.Index >= _listBox.Items.Count)
                    return;

                if (_listBox.Items[e.Index] is CameraEntry entry && entry.Item?.FQID != null)
                {
                    Guid id = entry.Item.FQID.ObjectId;
                    if (e.NewValue == CheckState.Checked)
                    {
                        if (_checkedCameras.Add(id) && !_checkedOrder.Contains(id))
                            _checkedOrder.Add(id);
                    }
                    else
                    {
                        _checkedCameras.Remove(id);
                        _checkedOrder.Remove(id);
                    }

                    BeginInvoke(new Action(() =>
                    {
                        UpdateSummary();
                        _listBox.Invalidate(_listBox.GetItemRectangle(e.Index));
                    }));
                }
            }

            /// <summary>
            /// Gestisce l'evento mouse move del controllo list box per tenere la UI reattiva.
            /// </summary>
            private void ListBox_MouseMove(object sender, MouseEventArgs e)
            {
                int index = _listBox.IndexFromPoint(e.Location);
                if (index >= 0 && index < _listBox.Items.Count && _listBox.Items[index] is CameraEntry entry)
                {
                    string tooltipText = string.IsNullOrEmpty(entry.Path)
                        ? entry.DisplayName
                        : $"{entry.DisplayName}\n{entry.Path}";

                    if (_lastTooltipIndex != index || _toolTip.GetToolTip(_listBox) != tooltipText)
                    {
                        _toolTip.SetToolTip(_listBox, tooltipText);
                        _lastTooltipIndex = index;
                    }
                }
                else if (_lastTooltipIndex != -1)
                {
                    _toolTip.SetToolTip(_listBox, string.Empty);
                    _lastTooltipIndex = -1;
                }
            }

            /// <summary>
            /// Esegue la logica confirm selection senza cambiare il comportamento.
            /// </summary>
            private void ConfirmSelection()
            {
                DialogResult = DialogResult.OK;
                Close();
            }

            /// <summary>
            /// Aggiorna summary e mantiene lo stato coerente.
            /// </summary>
            private void UpdateSummary()
            {
                int total = _allEntries.Count;
                int visible = _listBox.Items.Count;
                int selected = _checkedCameras.Count;

                string filterInfo = visible == total ? string.Empty : $" • Mostrate {visible}";
                _summaryLabel.Text = $"Selezionate {selected}/{total} telecamere{filterInfo}";

            }

            /// <summary>
            /// Aggiorna list height e mantiene lo stato coerente.
            /// </summary>
            private void UpdateListHeight(int itemCount)
            {
                int itemHeight = _listBox.ItemHeight > 0 ? _listBox.ItemHeight : 18;
                int desiredVisibleItems = Math.Max(10, Math.Min(itemCount, 14));
                int desiredHeight = (itemHeight * desiredVisibleItems) + Spacing.SM;
                ApplyListHeight(desiredHeight);
            }

            /// <summary>
            /// Applica list height alle impostazioni correnti.
            /// </summary>
            private void ApplyListHeight(int listHeight)
            {
                int clamped = Math.Max(MinListHeight, Math.Min(listHeight, MaxListHeight));
                int listContentWidth = WindowWidth - (_contentPaddingLeft + _contentPaddingRight) - 2;
                if (listContentWidth < 0)
                    listContentWidth = MinListHeight;

                _listBox.Width = listContentWidth;
                _listBox.Height = clamped;
                _listBox.MinimumSize = new Size(listContentWidth, clamped);
                _listBox.MaximumSize = new Size(listContentWidth, clamped);

                _listHostPanel.Width = listContentWidth;
                _listHostPanel.Height = clamped;
                _listHostPanel.MinimumSize = new Size(listContentWidth, clamped);
                _listHostPanel.MaximumSize = new Size(listContentWidth, clamped);

                int borderHeight = clamped + 2;
                int borderWidth = listContentWidth + 2;
                _listBorderPanel.Width = borderWidth;
                _listBorderPanel.Height = borderHeight;
                _listBorderPanel.MinimumSize = new Size(borderWidth, borderHeight);
                _listBorderPanel.MaximumSize = new Size(borderWidth, borderHeight);
            }

            /// <summary>
            /// Esegue la logica adjust form height senza cambiare il comportamento.
            /// </summary>
            private void AdjustFormHeight()
            {
                if (_mainLayout == null || !IsHandleCreated)
                    return;

                _mainLayout.PerformLayout();

                int availableWidth = ClientSize.Width;
                if (availableWidth <= 0)
                    availableWidth = WindowWidth - (_contentPaddingLeft + _contentPaddingRight);

                var preferredSize = _mainLayout.GetPreferredSize(new Size(availableWidth, int.MaxValue));
                int chrome = Height - ClientSize.Height;
                if (chrome <= 0)
                    chrome = SystemInformation.CaptionHeight + (SystemInformation.FrameBorderSize.Height * 2);

                int desiredHeight = preferredSize.Height + chrome;
                desiredHeight = Math.Max(MinWindowHeight, Math.Min(desiredHeight, MaxWindowHeight));

                Size = new Size(WindowWidth, desiredHeight);
                MinimumSize = new Size(WindowWidth, MinWindowHeight);
                MaximumSize = new Size(WindowWidth, MaxWindowHeight);
            }

            /// <summary>
            /// Compone camera entries pronto all'uso.
            /// </summary>
            private List<CameraEntry> BuildCameraEntries(IEnumerable<Item> cameras)
            {
                var result = new List<CameraEntry>();
                if (cameras == null)
                    return result;

                foreach (var camera in cameras)
                {
                    if (camera?.FQID == null)
                        continue;

                    var segments = GetFolderPathSegments(camera);
                    string path = segments.Length > 0 ? string.Join(" / ", segments) : string.Empty;
                    result.Add(new CameraEntry(camera, path));
                }

                return result
                    .OrderBy(entry => entry.SortKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            /// <summary>
            /// Restituisce folder path segments gia pronto.
            /// </summary>
            private static string[] GetFolderPathSegments(Item item)
            {
                var segments = new List<string>();
                try
                {
                    var current = item?.GetParent();
                    while (current != null && current.FQID != null && current.FQID.Kind != Kind.Server)
                    {
                        if (current.FQID.FolderType != FolderType.No && !string.IsNullOrWhiteSpace(current.Name))
                            segments.Add(current.Name);

                        current = current.GetParent();
                    }
                }
                catch
                {
                }

                segments.Reverse();
                return segments.ToArray();
            }

            /// <summary>
            /// Crea action button al volo.
            /// </summary>
            private Button CreateActionButton(string text, EventHandler handler)
            {
                var button = _owner.CreateModernButton(text, Colors.TextSecondary, handler);
                button.AutoSize = true;
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.MinimumSize = new Size(0, Sizes.ButtonHeight);
                button.MaximumSize = new Size(int.MaxValue, Sizes.ButtonHeight);
                button.Margin = new Padding(0);
                button.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
                button.TextAlign = ContentAlignment.MiddleCenter;
                return button;
            }

            /// <summary>
            /// Crea primary button al volo.
            /// </summary>
            private Button CreatePrimaryButton(string text)
            {
                var button = _owner.CreateModernButton(text, Colors.Primary);
                button.AutoSize = true;
                button.MinimumSize = new Size(0, Sizes.ButtonHeight);
                button.MaximumSize = new Size(int.MaxValue, Sizes.ButtonHeight);
                button.Margin = new Padding(0);
                button.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
                button.TextAlign = ContentAlignment.MiddleCenter;
                return button;
            }

            /// <summary>
            /// Crea secondary button al volo.
            /// </summary>
            private Button CreateSecondaryButton(string text)
                {
                var button = _owner.CreateModernButton(text, Colors.TextSecondary);
                button.AutoSize = false;
                button.MinimumSize = new Size(0, Sizes.ButtonHeight);
                button.MaximumSize = new Size(int.MaxValue, Sizes.ButtonHeight);
                button.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM);
                button.TextAlign = ContentAlignment.MiddleCenter;
                return button;
            }

            private sealed class CameraEntry
            {
                /// <summary>
                /// Costruttore di CameraEntry, inizializza il contesto senza effetti collaterali.
                /// </summary>
                public CameraEntry(Item item, string path)
                {
                    Item = item;
                    Path = path ?? string.Empty;
                    DisplayName = item?.Name ?? "Telecamera";
                    DisplayLabel = string.IsNullOrEmpty(Path)
                        ? DisplayName
                        : $"{DisplayName} — {Path}";
                    SearchKey = $"{DisplayName} {Path}".ToLowerInvariant();
                    SortKey = string.IsNullOrEmpty(Path) ? DisplayName : $"{Path}|{DisplayName}";
                }

                public Item Item { get; }
                public string Path { get; }
                public string DisplayName { get; }
                public string DisplayLabel { get; }
                public string SearchKey { get; }
                public string SortKey { get; }
            }
        }

        private sealed class ArchiveBaseNameSelectionForm : Form
        {
            private const string SearchPlaceholder = "Cerca gruppi pacchetti...";
            private const string DescriptionText = "Seleziona uno o più gruppi pacchetti da utilizzare nell'archivio. Puoi filtrare digitando il nome nella barra di ricerca.";
            private const int WindowWidth = 400;
            private const int MinWindowHeight = 320;
            private const int MaxWindowHeight = 760;
            private const int DefaultWindowHeight = 380;
            private const int MinListHeight = 200;
            private const int MaxListHeight = 400;
            private const int SearchBoxHeight = 22;

            private readonly MainForm _owner;
            private readonly CheckedListBox _listBox;
            private readonly TextBox _searchBox;
            private readonly Label _summaryLabel;
            private readonly Panel _listBorderPanel;
            private readonly Panel _listHostPanel;
            private readonly List<ArchiveBaseNameOption> _allOptions;
            private List<ArchiveBaseNameOption> _visibleOptions;
            private readonly HashSet<string> _checkedNames;
            private readonly List<string> _checkedOrder;
            private bool _isPlaceholderActive;
            private bool _suppressItemCheckEvents;
            private readonly int _contentPaddingLeft;
            private readonly int _contentPaddingRight;
            private readonly int _contentPaddingBottom;
            private readonly TableLayoutPanel _mainLayout;

            public ArchiveBaseNameSelectionForm(MainForm owner, IEnumerable<ArchiveBaseNameOption> options, IEnumerable<string> preselected)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                _allOptions = (options ?? Enumerable.Empty<ArchiveBaseNameOption>())
                    .Where(opt => opt != null && !string.IsNullOrWhiteSpace(opt.BaseName))
                    .OrderBy(opt => opt.BaseName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _visibleOptions = new List<ArchiveBaseNameOption>(_allOptions);
                _checkedNames = new HashSet<string>(preselected ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _checkedOrder = preselected?.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    ?? new List<string>();
                var availableNames = new HashSet<string>(_allOptions.Select(opt => opt.BaseName), StringComparer.OrdinalIgnoreCase);
                _checkedNames.RemoveWhere(name => !availableNames.Contains(name));
                _checkedOrder.RemoveAll(name => !availableNames.Contains(name));

                DoubleBuffered = true;
                Text = "Seleziona gruppi pacchetti";
                StartPosition = FormStartPosition.CenterParent;
                Size = new Size(WindowWidth, DefaultWindowHeight);
                MinimumSize = new Size(WindowWidth, MinWindowHeight);
                MaximumSize = new Size(WindowWidth, MaxWindowHeight);
                BackColor = Color.FromArgb(18, 18, 22);
                ForeColor = Colors.TextPrimary;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowIcon = false;

                _contentPaddingLeft = Math.Max(0, Spacing.SM - 2);
                _contentPaddingRight = _contentPaddingLeft + 16;
                _contentPaddingBottom = 0;
                int contentWidth = WindowWidth - (_contentPaddingLeft + _contentPaddingRight);

                _listBox = new CheckedListBox
                {
                    BorderStyle = BorderStyle.None,
                    CheckOnClick = true,
                    BackColor = Color.FromArgb(26, 26, 30),
                    ForeColor = Colors.TextPrimary,
                    IntegralHeight = false,
                    DisplayMember = nameof(ArchiveBaseNameOption.Display),
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                    Dock = DockStyle.Fill,
                    MinimumSize = new Size(contentWidth - 2, MinListHeight),
                    MaximumSize = new Size(contentWidth - 2, MaxListHeight)
                };
                _listBox.ItemCheck += ListBox_ItemCheck;

                _searchBox = _owner.CreateModernTextBox(SearchPlaceholder);
                _searchBox.BackColor = Color.FromArgb(24, 24, 28);
                _searchBox.BorderStyle = BorderStyle.FixedSingle;
                _searchBox.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM);
                _searchBox.Margin = new Padding(0, 0, 0, Spacing.XS);
                _searchBox.MinimumSize = new Size(contentWidth, SearchBoxHeight);
                _searchBox.MaximumSize = new Size(contentWidth, SearchBoxHeight);
                _searchBox.Height = SearchBoxHeight;
                _searchBox.Dock = DockStyle.Fill;
                _searchBox.Enter += SearchBox_Enter;
                _searchBox.Leave += SearchBox_Leave;
                _searchBox.TextChanged += SearchBox_TextChanged;

                _summaryLabel = new Label
                {
                    AutoSize = true,
                    ForeColor = Colors.TextSecondary,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS),
                    Margin = new Padding(0, Spacing.XXS, 0, Spacing.XXS)
                };

                var descriptionLabel = new Label
                {
                    Text = DescriptionText,
                    ForeColor = Colors.TextSecondary,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM),
                    AutoSize = true,
                    MaximumSize = new Size(contentWidth, 0),
                    Margin = new Padding(0, 0, 0, Spacing.XS)
                };

                _listBorderPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(40, 40, 44),
                    Padding = new Padding(1),
                    Margin = new Padding(0, 0, 0, Spacing.XXS),
                    MinimumSize = new Size(contentWidth, MinListHeight + 2),
                    MaximumSize = new Size(contentWidth, MaxListHeight + 2),
                    Width = contentWidth
                };
                _listHostPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(26, 26, 30),
                    MinimumSize = new Size(contentWidth - 2, MinListHeight),
                    MaximumSize = new Size(contentWidth - 2, MaxListHeight)
                };
                _listHostPanel.Controls.Add(_listBox);
                _listBorderPanel.Controls.Add(_listHostPanel);

                var summaryPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Margin = new Padding(0)
                };
                summaryPanel.Controls.Add(_summaryLabel);

                var footerPanel = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0),
                    Padding = new Padding(_contentPaddingLeft, Spacing.XS, _contentPaddingRight, 0),
                    BackColor = Color.Transparent
                };
                footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                footerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var footerSpacer = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    BackColor = Color.Transparent
                };
                footerPanel.Controls.Add(footerSpacer, 0, 0);

                var confirmButton = CreatePrimaryButton("OK");
                confirmButton.DialogResult = DialogResult.OK;
                confirmButton.Margin = new Padding(0);
                confirmButton.Dock = DockStyle.None;
                confirmButton.Anchor = AnchorStyles.Right;
                footerPanel.Controls.Add(confirmButton, 1, 0);

                _mainLayout = new TableLayoutPanel
                {
                    AutoSize = false,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    RowCount = 5,
                    Padding = new Padding(_contentPaddingLeft, Spacing.XXS, _contentPaddingRight, _contentPaddingBottom),
                    Dock = DockStyle.Fill
                };
                _mainLayout.ColumnStyles.Clear();
                _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, contentWidth));
                _mainLayout.RowStyles.Clear();
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainLayout.MinimumSize = new Size(WindowWidth, MinWindowHeight);

                _mainLayout.Controls.Add(descriptionLabel, 0, 0);
                _mainLayout.Controls.Add(_searchBox, 0, 1);
                _mainLayout.Controls.Add(_listBorderPanel, 0, 2);
                _mainLayout.Controls.Add(summaryPanel, 0, 3);
                _mainLayout.Controls.Add(footerPanel, 0, 4);

                Controls.Add(_mainLayout);

                AcceptButton = confirmButton;
                CancelButton = null;

                SetSearchPlaceholder();
                ApplyFilter(string.Empty);
                Shown += (s, e) => AdjustFormHeight();
            }

            public IReadOnlyCollection<string> GetSelectedBaseNames()
            {
                return _checkedOrder
                    .Where(name => _checkedNames.Contains(name))
                    .ToList();
            }

            private void SearchBox_Enter(object sender, EventArgs e)
            {
                if (_isPlaceholderActive)
                {
                    _isPlaceholderActive = false;
                    _searchBox.Text = string.Empty;
                    _searchBox.ForeColor = Colors.TextPrimary;
                }
            }

            private void SearchBox_Leave(object sender, EventArgs e)
            {
                if (string.IsNullOrWhiteSpace(_searchBox.Text))
                {
                    SetSearchPlaceholder();
                    ApplyFilter(string.Empty);
                }
            }

            private void SearchBox_TextChanged(object sender, EventArgs e)
            {
                if (_isPlaceholderActive)
                    return;

                ApplyFilter(_searchBox.Text);
            }

            private void ApplyFilter(string filterText)
            {
                string filter = (filterText ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(filter) || string.Equals(filter, SearchPlaceholder, StringComparison.OrdinalIgnoreCase))
                {
                    _visibleOptions = new List<ArchiveBaseNameOption>(_allOptions);
                }
                else
                {
                    string normalized = filter.ToLowerInvariant();
                    _visibleOptions = _allOptions
                        .Where(opt => opt.BaseName.ToLowerInvariant().Contains(normalized) ||
                                      opt.Display.ToLowerInvariant().Contains(normalized))
                        .ToList();
                }

                PopulateList(_visibleOptions);
            }

            private void PopulateList(List<ArchiveBaseNameOption> options)
            {
                _suppressItemCheckEvents = true;
                _listBox.BeginUpdate();
                _listBox.Items.Clear();

                foreach (var option in options)
                {
                    _listBox.Items.Add(option, _checkedNames.Contains(option.BaseName));
                }

                _listBox.EndUpdate();
                _suppressItemCheckEvents = false;

                UpdateListHeight(options.Count);
                UpdateSummary();
                _listBox.Invalidate();
                AdjustFormHeight();
            }

            private void ListBox_ItemCheck(object sender, ItemCheckEventArgs e)
            {
                if (_suppressItemCheckEvents || e.Index < 0 || e.Index >= _listBox.Items.Count)
                    return;

                if (_listBox.Items[e.Index] is ArchiveBaseNameOption option)
                {
                    string name = option.BaseName;
                    if (e.NewValue == CheckState.Checked)
                    {
                        if (_checkedNames.Add(name) && !_checkedOrder.Contains(name))
                            _checkedOrder.Add(name);
                    }
                    else
                    {
                        _checkedNames.Remove(name);
                        _checkedOrder.Remove(name);
                    }

                    BeginInvoke(new Action(() =>
                    {
                        UpdateSummary();
                        _listBox.Invalidate(_listBox.GetItemRectangle(e.Index));
                    }));
                }
            }

            private void UpdateSummary()
            {
                int count = _checkedNames.Count;
                _summaryLabel.Text = count == 0
                    ? "Nessun gruppo selezionato"
                    : count == 1
                        ? "1 gruppo selezionato"
                        : $"{count} gruppi selezionati";
            }

            private void UpdateListHeight(int itemCount)
            {
                int height = Math.Min(MaxListHeight, Math.Max(MinListHeight, itemCount * 24));
                _listHostPanel.MinimumSize = new Size(_listHostPanel.MinimumSize.Width, height);
                _listHostPanel.MaximumSize = new Size(_listHostPanel.MaximumSize.Width, height);
                _listBorderPanel.MinimumSize = new Size(_listBorderPanel.MinimumSize.Width, height + 2);
                _listBorderPanel.MaximumSize = new Size(_listBorderPanel.MaximumSize.Width, height + 2);
            }

            private void AdjustFormHeight()
            {
                if (_mainLayout == null || !IsHandleCreated)
                    return;

                _mainLayout.PerformLayout();

                int availableWidth = ClientSize.Width;
                if (availableWidth <= 0)
                    availableWidth = WindowWidth - (_contentPaddingLeft + _contentPaddingRight);

                var preferredSize = _mainLayout.GetPreferredSize(new Size(availableWidth, int.MaxValue));
                int chrome = Height - ClientSize.Height;
                if (chrome <= 0)
                    chrome = SystemInformation.CaptionHeight + (SystemInformation.FrameBorderSize.Height * 2);

                int desiredHeight = preferredSize.Height + chrome;
                desiredHeight = Math.Max(MinWindowHeight, Math.Min(desiredHeight, MaxWindowHeight));

                Size = new Size(WindowWidth, desiredHeight);
                MinimumSize = new Size(WindowWidth, MinWindowHeight);
                MaximumSize = new Size(WindowWidth, MaxWindowHeight);
            }

            private void SetSearchPlaceholder()
            {
                _isPlaceholderActive = true;
                _searchBox.Text = SearchPlaceholder;
                _searchBox.ForeColor = Colors.TextPlaceholder;
            }

            private Button CreatePrimaryButton(string text)
            {
                var button = _owner.CreateModernButton(text, Colors.Primary);
                button.AutoSize = true;
                button.MinimumSize = new Size(0, Sizes.ButtonHeight);
                button.MaximumSize = new Size(int.MaxValue, Sizes.ButtonHeight);
                button.Margin = new Padding(0);
                button.Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold);
                button.TextAlign = ContentAlignment.MiddleCenter;
                return button;
            }
        }

        // ✅ FIX #23: GenerateRandomPassword crypto-secure
        /// <summary>
        /// Esegue la logica generate random password senza cambiare il comportamento.
        /// </summary>
        private string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%&*";
            
            // ✅ Usa RNGCryptoServiceProvider per sicurezza
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] randomBytes = new byte[length];
                rng.GetBytes(randomBytes);
                
                char[] password = new char[length];
                for (int i = 0; i < length; i++)
                {
                    password[i] = chars[randomBytes[i] % chars.Length];
                }
                
                return new string(password);
            }
        }

        // Gestione placeholder nei TextBox
        /// <summary>
        /// Imposta up placeholder usando i parametri passati.
        /// </summary>
        private void SetupPlaceholder(TextBox textBox, string placeholderText)
        {
            // Salva il placeholder come Tag
            textBox.Tag = placeholderText;
            
            // Quando il focus entra nel textbox
            textBox.Enter += (s, e) =>
            {
                if (textBox.Text == placeholderText)
                {
                    textBox.Text = "";
                    textBox.ForeColor = Color.White; // Testo normale bianco
                }
            };
            
            // Quando il testo cambia (anche quando incolli)
            textBox.TextChanged += (s, e) =>
            {
                // Se l'utente sta scrivendo/incollando e il testo non è il placeholder
                if (!string.IsNullOrWhiteSpace(textBox.Text) && textBox.Text != placeholderText)
                {
                    textBox.ForeColor = Color.White; // Testo normale bianco
                }
            };
            
            // Quando il focus esce dal textbox
            textBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = placeholderText;
                    textBox.ForeColor = Color.FromArgb(128, 128, 128); // Placeholder grigio
                }
            };
        }

        // Estrae il valore reale da un TextBox ignorando i placeholder
        /// <summary>
        /// Restituisce real value gia pronto.
        /// </summary>
        private string GetRealValue(TextBox textBox)
        {
            if (textBox == null)
                return "";
                
            string text = textBox.Text?.Trim() ?? "";
            
            // Se il testo è vuoto, ritorna stringa vuota
            if (string.IsNullOrWhiteSpace(text))
                return "";

            if (textBox.Tag is string taggedPlaceholder &&
                string.Equals(text, taggedPlaceholder, StringComparison.OrdinalIgnoreCase))
                return "";
                
            // Se il testo inizia con "[" (placeholder), ritorna stringa vuota
            if (text.StartsWith("["))
                return "";
                
            // Se il testo è uguale a placeholder comuni, ritorna stringa vuota
            string[] commonPlaceholders = {
                "[12345/2025/N]", "[RIT o SPEC 1234/2025]", "[Oggetto o soggetto]", "[ID-12345]",
                "__/__/____ __:__", "[Password]", "[Cartella di esportazione]"
            };
            
            foreach (string placeholder in commonPlaceholders)
            {
                if (string.Equals(text, placeholder, StringComparison.OrdinalIgnoreCase))
                    return "";
            }
                
            // Altrimenti ritorna il testo reale
            return text;
        }

        // Metodo SEMPLICE che FUNZIONA - prende il testo del TextBox
        /// <summary>
        /// Esegue la logica extract text box value senza cambiare il comportamento.
        /// </summary>
        private string ExtractTextBoxValue(TextBox textBox, string defaultValue = "")
        {
            if (textBox == null)
                return defaultValue ?? string.Empty;

            string text = textBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return defaultValue ?? string.Empty;

            if (string.Equals(text, "__/__/____ __:__", StringComparison.OrdinalIgnoreCase))
                return defaultValue ?? string.Empty;

            if (textBox.Tag is string placeholder &&
                string.Equals(text, placeholder, StringComparison.OrdinalIgnoreCase))
                return defaultValue ?? string.Empty;

            return text;
        }

        /// <summary>
        /// Restituisce user preferences gia pronto.
        /// </summary>
        private Dictionary<string, string> GetUserPreferences()
        {
            if (_preferencesCache != null)
                return _preferencesCache;

            try
            {
                _preferencesCache = _dataService.LoadPreferences();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUserPreferences error: {ex.Message}");
                _preferencesCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return _preferencesCache;
        }

        /// <summary>
        /// Esegue la logica persist user preferences senza cambiare il comportamento.
        /// </summary>
        private void PersistUserPreferences()
        {
            try
            {
                _dataService.SavePreferences(GetUserPreferences());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistUserPreferences error: {ex.Message}");
            }
        }

        /// <summary>
        /// Carica user setting e aggiorna la UI senza fronzoli.
        /// </summary>
        private string LoadUserSetting(string key)
        {
            var preferences = GetUserPreferences();
            if (preferences.TryGetValue(key, out var value))
                return value ?? string.Empty;

            try
            {
                var valueFromDb = _dataService.GetPreference(key);
                if (!string.IsNullOrEmpty(valueFromDb))
                {
                    preferences[key] = valueFromDb;
                }
                return valueFromDb ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadUserSetting error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Salva user setting in modo sicuro.
        /// </summary>
        private void SaveUserSetting(string key, string value)
        {
            var preferences = GetUserPreferences();

            if (string.IsNullOrWhiteSpace(value))
            {
                if (preferences.ContainsKey(key))
                {
                    preferences.Remove(key);
                    try
                    {
                        _dataService.SetPreference(key, null);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SaveUserSetting remove error: {ex.Message}");
                    }
                }
                return;
            }

            preferences[key] = value;
            try
            {
                _dataService.SetPreference(key, value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveUserSetting error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gestisce l'evento click del controllo select destination per tenere la UI reattiva.
        /// </summary>
        private void SelectDestination_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                var driveLines = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    .Select(d =>
                    {
                        string name = d.Name.TrimEnd(Path.DirectorySeparatorChar);
                        return $"{name} — {FormatSize(d.TotalSize)} totali ({FormatSize(d.TotalFreeSpace)} liberi)";
                    })
                    .ToArray();

                if (driveLines.Length > 0)
                {
                    dialog.Description = "Seleziona la cartella di destinazione per l'archiviazione.\n\nUnità disponibili:\n" +
                        string.Join(Environment.NewLine, driveLines);
                }
                else
                {
                    dialog.Description = "Seleziona la cartella di destinazione per l'archiviazione";
                }

                dialog.ShowNewFolderButton = true;
                
                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                                _path = dialog.SelectedPath;
                    _currentExportOutputPath = null;
                    TextBox cartellaBox = contentPanel.Controls.Find("CartellaTextBox", true).FirstOrDefault() as TextBox;
                    if (cartellaBox != null)
                    {
                        cartellaBox.Text = _path;
                        cartellaBox.ForeColor = Color.White;
                    }
                }
            }
        }
        /// <summary>
        /// Gestisce l'evento click del controllo pin export folder button per tenere la UI reattiva.
        /// </summary>
        private void PinExportFolderButton_Click(object sender, EventArgs e)
        {
            var cartellaBox = Controls.Find("CartellaTextBox", true).OfType<TextBox>().FirstOrDefault();
            if (cartellaBox == null)
                return;

            string text = cartellaBox.Text?.Trim() ?? string.Empty;
            if (cartellaBox.Tag is string placeholder && string.Equals(text, placeholder, StringComparison.OrdinalIgnoreCase))
                text = string.Empty;

            var value = string.IsNullOrWhiteSpace(text) ? GetRealValue(cartellaBox) : text;
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowInfo("Percorso mancante", "Inserisci un percorso valido da impostare come predefinito.");
                return;
            }

            SaveUserSetting("PinnedExportPath", value);
            _path = value;
            _currentExportOutputPath = null;
            cartellaBox.Text = value;
            cartellaBox.ForeColor = Colors.TextPrimary;
            ShowSuccess("Preferenza salvata", "Il percorso di destinazione è stato impostato come predefinito.");
        }

        /// <summary>
        /// Gestisce l'evento click del controllo pin password button per tenere la UI reattiva.
        /// </summary>
        private void PinPasswordButton_Click(object sender, EventArgs e)
        {
            var passwordBox = Controls.Find("PasswordTextBox", true).OfType<TextBox>().FirstOrDefault();
            if (passwordBox == null)
                return;

            string text = passwordBox.Text?.Trim() ?? string.Empty;
            if (passwordBox.Tag is string placeholder && string.Equals(text, placeholder, StringComparison.OrdinalIgnoreCase))
                text = string.Empty;

            var value = string.IsNullOrWhiteSpace(text) ? GetRealValue(passwordBox) : text;
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowInfo("Password mancante", "Inserisci una password valida da salvare come predefinita.");
                return;
            }

            SaveUserSetting("PinnedPassword", value);
            passwordBox.Text = value;
            passwordBox.ForeColor = Colors.TextPrimary;
            ShowSuccess("Preferenza salvata", "La password è stata impostata come valore predefinito.");
        }

        /// <summary>
        /// Gestisce l'evento click del controllo pin server button per tenere la UI reattiva.
        /// </summary>
        private void PinServerButton_Click(object sender, EventArgs e)
        {
            var serverBox = Controls.Find("ServerAddressTextBox", true).OfType<TextBox>().FirstOrDefault();
            if (serverBox == null)
                return;

            string text = serverBox.Text?.Trim() ?? string.Empty;
            if (serverBox.Tag is string placeholder && string.Equals(text, placeholder, StringComparison.OrdinalIgnoreCase))
                text = string.Empty;

            var value = string.IsNullOrWhiteSpace(text) ? GetRealValue(serverBox) : text;
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowInfo("Indirizzo mancante", "Inserisci un indirizzo server valido da salvare come predefinito.");
                return;
            }

            value = NormalizeServerAddress(value);

            SaveUserSetting("PinnedServerAddress", value);
            _currentServerAddress = value;
            serverBox.Text = value;
            serverBox.ForeColor = Colors.TextPrimary;
            ShowSuccess("Preferenza salvata", "L'indirizzo del server è stato impostato come predefinito.");
        }

        private void PinArchiveRootButton_Click(object sender, EventArgs e)
        {
            if (_archiveRootTextBox == null)
                return;

            string value = GetRealValue(_archiveRootTextBox);
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("Seleziona la cartella", StringComparison.OrdinalIgnoreCase))
            {
                ShowInfo("Percorso mancante", "Inserisci o seleziona un percorso archivi da salvare come predefinito.");
                return;
            }

            SaveUserSetting("PinnedArchiveRoot", value);
            _archiveRootPath = value;
            _archiveRootTextBox.Text = value;
            _archiveRootTextBox.ForeColor = Colors.TextPrimary;
            ShowSuccess("Preferenza salvata", "Il percorso archivi è stato impostato come predefinito.");
        }
        /// <summary>
        /// Gestisce l'evento click del controllo connect button per tenere la UI reattiva.
        /// </summary>
        private void ConnectButton_Click(object sender, EventArgs e)
        {
            var button = sender as Button;

            RunWithButtonLoading(button, "Attendi", () =>
            {
                TextBox serverBox = contentPanel.Controls.Find("ServerAddressTextBox", true).FirstOrDefault() as TextBox;
                Label connectionText = statusPanel.Controls.Find("ConnectionStatusText", true).FirstOrDefault() as Label;
                Label connectionIcon = statusPanel.Controls.Find("ConnectionIcon", true).FirstOrDefault() as Label;
                Button selectCameraBtn = contentPanel.Controls.Find("SelectCameraButton", true).FirstOrDefault() as Button;
                Button connectBtn = contentPanel.Controls.Find("ConnectButton", true).FirstOrDefault() as Button;
                
                string serverAddress = serverBox?.Text?.Trim() ?? "";
                serverAddress = NormalizeServerAddress(serverAddress);
                if (serverBox != null)
                {
                    serverBox.Text = serverAddress;
                    serverBox.ForeColor = Colors.TextPrimary;
                }
                
                // Validazione
                if (string.IsNullOrEmpty(serverAddress))
                {
                    ShowWarning("Indirizzo mancante", "Inserisci l'indirizzo del server prima di connetterti.");
                    return;
                }
                
                // Stato: connessione in corso
                if (connectionIcon != null)
                {
                    connectionIcon.ForeColor = Colors.Warning;
                }
                if (connectionText != null)
                {
                    connectionText.Text = "Connessione in corso...";
                    connectionText.ForeColor = Colors.Warning;
                }
                Application.DoEvents();
                
                _isServerConnected = false;

                try
                {
                    // Rimuovi connessioni precedenti
                    VideoOS.Platform.SDK.Environment.RemoveAllServers();
                    _isServerConnected = false;
                    
                    Uri serverUri = new Uri(serverAddress);
                    
                    // CONNESSIONE REALE con autenticazione Windows (utente corrente)
                    System.Net.NetworkCredential credential = System.Net.CredentialCache.DefaultNetworkCredentials;
                    VideoOS.Platform.SDK.Environment.AddServer(serverUri, credential, true);
                    
                    // VERIFICA APPROFONDITA che la connessione sia REALMENTE avvenuta
                    // 1. Controlla EnvironmentManager
                    if (EnvironmentManager.Instance == null || EnvironmentManager.Instance.MasterSite == null)
                    {
                        throw new Exception("Server non raggiungibile. Verifica l'indirizzo.");
                    }
                    
                    // 2. Prova a caricare la configurazione
                    if (VideoOS.Platform.Configuration.Instance == null || VideoOS.Platform.Configuration.Instance.ServerFQID == null)
                    {
                        throw new Exception("Impossibile ottenere la configurazione dal server.");
                    }
                    
                    // 3. Carica le telecamere disponibili (ora gestite tramite Item Picker)
                    _allCams = FindAllCameras();
                    
                    // 4. VERIFICA che ci siano telecamere (altrimenti non è un server VideoOS valido)
                    if (_allCams == null || _allCams.Count == 0)
                    {
                        VideoOS.Platform.SDK.Environment.RemoveAllServers();
                        _isServerConnected = false;
                        throw new Exception("Server connesso ma nessuna telecamera disponibile.\nVerifica che sia un Management Server VideoOS configurato correttamente.");
                    }
                    
                    // Connessione VERIFICATA e riuscita
                    _currentServerAddress = serverAddress;
                    _isServerConnected = true;
                    
                    // Cambia colore del pulsante Connetti in verde quando connesso
                    if (connectBtn != null)
                    {
                        connectBtn.ForeColor = Colors.Success;
                    }
                    
                    // Abilita il pulsante e il campo di selezione telecamera
                    if (selectCameraBtn != null)
                    {
                        selectCameraBtn.Enabled = true;
                        selectCameraBtn.ForeColor = Colors.Success;
                    }
                    TextBox telecameraTextBox = contentPanel.Controls.Find("TelecameraTextBox", true).FirstOrDefault() as TextBox;
                    if (telecameraTextBox != null && telecameraTextBox.Enabled)
                    {
                        if (string.IsNullOrWhiteSpace(telecameraTextBox.Text) || telecameraTextBox.ForeColor == Colors.TextPlaceholder)
                        {
                            telecameraTextBox.Text = "Seleziona una o più telecamere...";
                            telecameraTextBox.ForeColor = Colors.TextPlaceholder;
                        }
                        else
                        {
                            telecameraTextBox.ForeColor = Colors.TextPrimary;
                        }

                        SyncSelectedCamerasFromText();
                    }
                    if (connectionIcon != null)
                    {
                        connectionIcon.ForeColor = Colors.Success;
                    }
                    if (connectionText != null)
                    {
                        connectionText.Text = $"Connesso a {serverAddress}";
                        connectionText.ForeColor = Colors.TextSecondary;
                    }
                    
                    ShowSuccess("Connessione riuscita", $"Connessione completata. Trovate {_allCams.Count} telecamere disponibili.");
                }
                catch (Exception ex)
                {
                    _isServerConnected = false;
                    // Connessione fallita
                    // Mantiene il pulsante Connetti grigio
                    if (connectBtn != null)
                    {
                        connectBtn.ForeColor = Colors.TextMuted;  // Rimane grigio se fallisce
                    }
                    
                    // Disabilita il pulsante di selezione telecamera
                    if (selectCameraBtn != null)
                    {
                        selectCameraBtn.Enabled = false;
                        selectCameraBtn.ForeColor = Colors.TextPlaceholder;  // Grigio quando disabilitato
                    }
                    TextBox telecameraTextBox = contentPanel.Controls.Find("TelecameraTextBox", true).FirstOrDefault() as TextBox;
                    if (telecameraTextBox != null)
                    {
                        telecameraTextBox.Enabled = true;
                        telecameraTextBox.Text = "Seleziona una o più telecamere...";
                        telecameraTextBox.ForeColor = Colors.TextPlaceholder;
                    }
                    if (connectionIcon != null)
                    {
                        connectionIcon.ForeColor = Colors.Error;
                    }
                    if (connectionText != null)
                    {
                        connectionText.Text = "Connessione fallita";
                        connectionText.ForeColor = Colors.Error;
                    }
                    
                    ShowError("Connessione fallita", $"Non è stato possibile connettersi al server.\nDettagli: {ex.Message}");
                }
            });
        }

        
        /// <summary>
        /// Esegue la logica find all cameras senza cambiare il comportamento.
        /// </summary>
        private List<Item> FindAllCameras()
        {
            List<Item> items = new List<Item>();
            if (VideoOS.Platform.Configuration.Instance != null)
            {
                foreach (Item item in VideoOS.Platform.Configuration.Instance.GetItems(ItemHierarchy.SystemDefined))
                {
                    foreach (Item child in item.GetChildren())
                        AddCameras(child, ref items);
                }
            }
            return items;
        }

        /// <summary>
        /// Esegue la logica add cameras senza cambiare il comportamento.
        /// </summary>
        private void AddCameras(Item item, ref List<Item> cameras)
        {
            if (item.FQID.Kind == Kind.Camera && item.FQID.FolderType == FolderType.No)
                cameras.Add(item);
            else if (item.HasChildren != VideoOS.Platform.HasChildren.No)
            {
                foreach (Item child in item.GetChildren())
                    AddCameras(child, ref cameras);
            }
        }
        
        // Metodi per ottenere i dati reali delle archiviazioni
        // ✅ FIX #5: GetArchiviazioniInCorso con clone per thread-safety
        /// <summary>
        /// Restituisce archiviazioni in corso gia pronto.
        /// </summary>
        private List<ArchiviazioneInfo> GetArchiviazioniInCorso()
        {
            var archiviazioni = new List<ArchiviazioneInfo>();
            
            try
            {
                List<ExportJob> snapshot;
                lock (_exportLock)
                {
                    snapshot = _activeExports
                        .Where(job => !job.IsCanceled && !IsFinalStatus(job.Status))
                        .ToList();
                }

                foreach (var job in snapshot)
                {
                    int progress = Math.Max(0, Math.Min(100, job.Progress));
                    archiviazioni.Add(new ArchiviazioneInfo
                            {
                                Id = job.Id,
                                Telecamera = job.CameraName,
                                PeriodoInizio = job.StartTime,
                                PeriodoFine = job.EndTime,
                                Inizio = job.StartedAtUtc == default(DateTime) ? default(DateTime) : job.StartedAtUtc.ToLocalTime(),
                                Fine = job.CompletedAtUtc == default(DateTime) ? default(DateTime) : job.CompletedAtUtc.ToLocalTime(),
                                DataAvvio = job.StartedAtUtc == default(DateTime) ? default(DateTime) : job.StartedAtUtc.ToLocalTime(),
                                DataCompletamento = job.CompletedAtUtc == default(DateTime) ? default(DateTime) : job.CompletedAtUtc.ToLocalTime(),
                        Progresso = progress,
                        Stato = job.Status,
                                Cartella = job.Path
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore nel caricamento archiviazioni in corso: {ex.Message}");
            }
            
            return archiviazioni;
        }

        /// <summary>
        /// Restituisce archiviazioni completate gia pronto.
        /// </summary>
        private List<ArchiviazioneInfo> GetArchiviazioniCompletate()
        {
            lock (_completedExportsLock)
            {
                return _completedExports
                    .Where(info => info != null)
                    .Select(info => info.Clone())
                    .OrderByDescending(info => info.DataCompletamento)
                    .ToList();
            }
        }
        /// <summary>
        /// Esegue la logica cancel archiviazione senza cambiare il comportamento.
        /// </summary>
        private void CancelArchiviazione(string archiviazioneId)
        {
            if (string.IsNullOrWhiteSpace(archiviazioneId))
                return;

            ExportJob queuedJob = null;
            ExportJob activeJob = null;

            lock (_exportLock)
            {
                queuedJob = _queuedExports.FirstOrDefault(j => string.Equals(j.Id, archiviazioneId, StringComparison.OrdinalIgnoreCase));
                if (queuedJob != null)
                {
                    _queuedExports.Remove(queuedJob);

                    var pending = _pendingJobRequests.ToList();
                    _pendingJobRequests.Clear();
                    foreach (var request in pending)
                    {
                        if (request?.Job != null && string.Equals(request.Job.Id, archiviazioneId, StringComparison.OrdinalIgnoreCase))
                            continue;
                        _pendingJobRequests.Enqueue(request);
                    }
                }
                else
                {
                    activeJob = _activeExports.FirstOrDefault(j => string.Equals(j.Id, archiviazioneId, StringComparison.OrdinalIgnoreCase));
                    if (activeJob != null)
                    {
                        _activeExports.Remove(activeJob);
                    }
                }
            }

            if (queuedJob != null)
            {
                queuedJob.Status = "Annullato";
                queuedJob.IsCanceled = true;

                AppendJobLogEntry(queuedJob, "canceled", new Dictionary<string, object>
                {
                    ["progress"] = queuedJob.Progress,
                    ["scope"] = "queue"
                });

                try
                {
                    _dataService.RecordJobCancellation(queuedJob.Id, queuedJob.Status);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Errore registrazione annullamento job {queuedJob.Id}: {ex.Message}");
                }

                try
                {
                    var payload = new Dictionary<string, object>
                    {
                        ["message"] = "Job annullato in coda"
                    };
                    _dataService.RecordJobEvent(queuedJob.Id, queuedJob.Status, queuedJob.Progress, "canceled", payload);
                        }
                        catch (Exception ex)
                        {
                    Debug.WriteLine($"Errore registrazione evento annullamento job {queuedJob.Id}: {ex.Message}");
                }

                RefreshUiAfterJobCompletion(refetchCompleted: false);
                TryStartNextQueuedJob();
                return;
            }

            if (activeJob != null)
            {
                activeJob.Status = "Annullato";
                activeJob.IsCanceled = true;

                AppendJobLogEntry(activeJob, "canceled", new Dictionary<string, object>
                {
                    ["progress"] = activeJob.Progress,
                    ["scope"] = "active"
                });

                StopJobTimer(activeJob);
                DisposeJobExporter(activeJob, true);

                try
                {
                    _dataService.RecordJobCancellation(activeJob.Id, activeJob.Status);
                    }
                    catch (Exception ex)
                    {
                    Debug.WriteLine($"Errore registrazione annullamento job {activeJob.Id}: {ex.Message}");
                }

                try
                {
                    var payload = new Dictionary<string, object>
                    {
                        ["message"] = "Job annullato manualmente",
                        ["progress"] = activeJob.Progress
                    };
                    _dataService.RecordJobEvent(activeJob.Id, activeJob.Status, activeJob.Progress, "canceled", payload);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Errore registrazione evento annullamento job {activeJob.Id}: {ex.Message}");
                }

                RefreshUiAfterJobCompletion(refetchCompleted: false);
                TryStartNextQueuedJob();
            }
        }

        /// <summary>
        /// Mostra archiviazione details all'utente.
        /// </summary>
        private void ShowArchiviazioneDetails(ArchiviazioneInfo archiviazione)
        {
            string details = $"DETTAGLI ARCHIVIAZIONE\n\n" +
                           $"ID: {archiviazione.Id}\n" +
                           $"Telecamera: {archiviazione.Telecamera}\n" +
                           $"Periodo: {archiviazione.Inizio:dd/MM/yyyy HH:mm} - {archiviazione.Fine:dd/MM/yyyy HH:mm}\n" +
                           $"Durata: {archiviazione.Durata}\n" +
                           $"Dimensione: {archiviazione.Dimensione}\n" +
                           $"Completata il: {archiviazione.DataCompletamento:dd/MM/yyyy HH:mm}\n" +
                           $"Cartella: {archiviazione.Cartella}";
                           
            ShowInfo("Dettagli archiviazione", details);
        }

        // Metodi di supporto
        /// <summary>
        /// Restituisce directory size gia pronto.
        /// </summary>
        private long GetDirectorySize(string directoryPath)
        {
            try
            {
                return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                               .Sum(file => new FileInfo(file).Length);
            }
            catch
            {
                return 0;
            }
        }
        /// <summary>
        /// Esegue la logica format size senza cambiare il comportamento.
        /// </summary>
        private string FormatSize(long bytes)
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
        /// <summary>
        /// Valida all fields prima di continuare.
        /// </summary>
        private bool ValidateAllFields()
        {
            bool isValid = true;
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            // Determina la modalità corrente
            bool isArchiveMode = _currentLaunchMode == LaunchMode.Archivio;

            // Recupera tutti i campi
            TextBox procedimentoBox = contentPanel.Controls.Find("ProcedimentoTextBox", true).FirstOrDefault() as TextBox;
            TextBox ritSpecBox = contentPanel.Controls.Find("RitSpecTextBox", true).FirstOrDefault() as TextBox;
            TextBox targetBox = contentPanel.Controls.Find("TargetTextBox", true).FirstOrDefault() as TextBox;
            TextBox idLavoroBox = contentPanel.Controls.Find("IdLavoroTextBox", true).FirstOrDefault() as TextBox;
            TextBox passwordBox = contentPanel.Controls.Find("PasswordTextBox", true).FirstOrDefault() as TextBox;
            TextBox telecameraTextBox = contentPanel.Controls.Find("TelecameraTextBox", true).FirstOrDefault() as TextBox;
            TextBox inizioBox = contentPanel.Controls.Find("InizioTextBox", true).FirstOrDefault() as TextBox;
            TextBox fineBox = contentPanel.Controls.Find("FineTextBox", true).FirstOrDefault() as TextBox;
            DateTimePicker inizioPicker = contentPanel.Controls.Find("InizioPicker", true).FirstOrDefault() as DateTimePicker;
            DateTimePicker finePicker = contentPanel.Controls.Find("FinePicker", true).FirstOrDefault() as DateTimePicker;
            TextBox cartellaBox = contentPanel.Controls.Find("CartellaTextBox", true).FirstOrDefault() as TextBox;
            TextBox archiveRootBox = contentPanel.Controls.Find("ArchiveRootTextBox", true).FirstOrDefault() as TextBox;
            TextBox archiveStartBox = contentPanel.Controls.Find("ArchiveStartTextBox", true).FirstOrDefault() as TextBox;
            TextBox archiveEndBox = contentPanel.Controls.Find("ArchiveEndTextBox", true).FirstOrDefault() as TextBox;
            TextBox archiveBaseNamesBox = contentPanel.Controls.Find("ArchiveBaseNamesTextBox", true).FirstOrDefault() as TextBox;

            // Helper per verificare se il testo è un placeholder
            Func<TextBox, bool> isPlaceholder = (tb) => {
                if (tb == null) return true;
                string text = tb.Text?.Trim() ?? "";
                string tag = tb.Tag?.ToString() ?? "";
                return string.IsNullOrWhiteSpace(text) || text == tag;
            };

            // Validazione Connessione Server (solo per modalità "Da server")
            if (!isArchiveMode && !_isServerConnected)
            {
                errors.Add("- Connessione al server (non connesso)");
                isValid = false;
            }

            // Validazione Procedimento Penale (formato numeri/anno/lettera)
            if (isPlaceholder(procedimentoBox))
            {
                procedimentoBox.ForeColor = Colors.Error;
                errors.Add("- Procedimento Penale mancante");
                isValid = false;
            }
            else
            {
                string pp = GetRealValue(procedimentoBox);
                if (string.IsNullOrWhiteSpace(pp))
                {
                    procedimentoBox.ForeColor = Colors.Error;
                    errors.Add("- Procedimento Penale mancante");
                    isValid = false;
                }
                else
                {
                    string normalized = pp.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\d+/\d{4}/[NI]$"))
                    {
                        procedimentoBox.ForeColor = Colors.Error;
                        errors.Add("- Procedimento Penale: inserire il suffisso 'N' o 'I' (es. 12345/2025/N)");
                        isValid = false;
                    }
                    else
                    {
                        if (!string.Equals(procedimentoBox.Text?.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            procedimentoBox.Text = normalized;
                        }
                        procedimentoBox.ForeColor = Colors.TextPrimary;
                    }
                }
            }

            // Validazione RIT/SPEC
            if (isPlaceholder(ritSpecBox))
            {
                ritSpecBox.ForeColor = Colors.Error;
                errors.Add("- RIT/SPEC mancante");
                isValid = false;
            }
            else
            {
                string rit = GetRealValue(ritSpecBox);
                if (string.IsNullOrWhiteSpace(rit))
                {
                    ritSpecBox.ForeColor = Colors.Error;
                    errors.Add("- RIT/SPEC mancante");
                    isValid = false;
                }
                else
                {
                    string normalizedRit = rit.Trim().ToUpperInvariant();
                    if (!normalizedRit.StartsWith("RIT", StringComparison.OrdinalIgnoreCase) &&
                        !normalizedRit.StartsWith("SPEC", StringComparison.OrdinalIgnoreCase))
                    {
                        ritSpecBox.ForeColor = Colors.Error;
                        errors.Add("- RIT/SPEC: deve iniziare con 'RIT' o 'SPEC'");
                        isValid = false;
                    }
                    else
                    {
                        if (!string.Equals(ritSpecBox.Text?.Trim(), normalizedRit, StringComparison.OrdinalIgnoreCase))
                        {
                            ritSpecBox.Text = normalizedRit;
                        }
                        ritSpecBox.ForeColor = Colors.TextPrimary;
                    }
                }
            }

            // Validazione Target
            if (isPlaceholder(targetBox))
            {
                targetBox.ForeColor = Colors.Error;
                errors.Add("- Target/Soggetto mancante");
                isValid = false;
            }
            else
            {
                targetBox.ForeColor = Colors.TextPrimary;
            }

            // Validazione ID Lavoro
            if (isPlaceholder(idLavoroBox))
            {
                idLavoroBox.ForeColor = Colors.Error;
                errors.Add("- ID Lavoro mancante");
                isValid = false;
            }
            else
            {
                idLavoroBox.ForeColor = Colors.TextPrimary;
            }

            // Validazione Password (esattamente 8 caratteri)
            string password = GetRealValue(passwordBox);
            if (isPlaceholder(passwordBox) || string.IsNullOrWhiteSpace(password))
            {
                passwordBox.ForeColor = Colors.Error;
                errors.Add("- Password mancante");
                isValid = false;
            }
            else if (password.Length != 8)
            {
                passwordBox.ForeColor = Colors.Warning;
                errors.Add($"- Password deve essere di 8 caratteri (attualmente {password.Length})");
                isValid = false;
            }
            else
            {
                passwordBox.ForeColor = Colors.TextPrimary;
            }

            // Validazione Telecamera (solo per modalità "Da server")
            if (!isArchiveMode)
            {
            GetSelectedCameras();

            if (_selectedCameras.Count == 0)
            {
                if (telecameraTextBox != null)
                    telecameraTextBox.ForeColor = Colors.Error;
                errors.Add("- Telecamere (nessuna selezionata)");
                isValid = false;
            }
            else if (telecameraTextBox != null)
            {
                telecameraTextBox.ForeColor = Colors.TextPrimary;
                }
            }

            // Validazione campi archivio (solo per modalità "Da archivio")
            if (isArchiveMode)
            {
                // Validazione Percorso archivi (pacchetti)
                string archiveRoot = GetRealValue(archiveRootBox);
                if (isPlaceholder(archiveRootBox) || string.IsNullOrWhiteSpace(archiveRoot))
                {
                    if (archiveRootBox != null)
                        archiveRootBox.ForeColor = Colors.Error;
                    errors.Add("- Percorso archivi (pacchetti) mancante");
                    isValid = false;
                }
                else if (!Directory.Exists(archiveRoot.Trim()))
                {
                    if (archiveRootBox != null)
                        archiveRootBox.ForeColor = Colors.Error;
                    errors.Add("- Percorso archivi (pacchetti) non valido o non accessibile");
                    isValid = false;
                }
                else if (archiveRootBox != null)
                {
                    archiveRootBox.ForeColor = Colors.TextPrimary;
                }

                // Validazione Gruppo pacchetti (base-name)
                if (_selectedArchiveBaseNames == null || _selectedArchiveBaseNames.Count == 0)
                {
                    if (archiveBaseNamesBox != null)
                        archiveBaseNamesBox.ForeColor = Colors.Error;
                    errors.Add("- Gruppo pacchetti (base-name): nessun gruppo selezionato");
                    isValid = false;
                }
                else if (archiveBaseNamesBox != null)
                {
                    archiveBaseNamesBox.ForeColor = Colors.TextPrimary;
                }

                // Validazione Intervallo data/ora (archivio) - opzionale ma se presente deve essere valido
                DateTime archiveStartTime = DateTime.MinValue;
                DateTime archiveEndTime = DateTime.MinValue;
                bool archiveDateValid = true;

                if (archiveStartBox != null && archiveEndBox != null)
                {
                    string archiveStartText = NormalizeTimeSeparators(GetRealValue(archiveStartBox));
                    string archiveEndText = NormalizeTimeSeparators(GetRealValue(archiveEndBox));

                    // Se almeno uno dei due è compilato, entrambi devono essere validi
                    bool hasStart = !string.IsNullOrWhiteSpace(archiveStartText);
                    bool hasEnd = !string.IsNullOrWhiteSpace(archiveEndText);

                    if (hasStart || hasEnd)
                    {
                        if (!hasStart)
                        {
                            archiveStartBox.ForeColor = Colors.Error;
                            errors.Add("- Data/Ora Inizio (archivio) obbligatoria se è specificata la data fine");
                            archiveDateValid = false;
                        }
                        else if (!DateTime.TryParseExact(archiveStartText, "dd/MM/yyyy HH:mm",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out archiveStartTime))
                        {
                            if (!DateTime.TryParseExact(archiveStartText, "dd/MM/yyyy HH:mm:ss",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out archiveStartTime))
                            {
                                archiveStartBox.ForeColor = Colors.Error;
                                errors.Add("- Data/Ora Inizio (archivio): formato non valido (dd/MM/yyyy HH:mm)");
                                archiveDateValid = false;
                            }
                            else
                            {
                                archiveStartBox.ForeColor = Colors.TextPrimary;
                            }
                        }
                        else
                        {
                            archiveStartBox.ForeColor = Colors.TextPrimary;
                        }

                        if (!hasEnd)
                        {
                            archiveEndBox.ForeColor = Colors.Error;
                            errors.Add("- Data/Ora Fine (archivio) obbligatoria se è specificata la data inizio");
                            archiveDateValid = false;
                        }
                        else if (!DateTime.TryParseExact(archiveEndText, "dd/MM/yyyy HH:mm",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out archiveEndTime))
                        {
                            if (!DateTime.TryParseExact(archiveEndText, "dd/MM/yyyy HH:mm:ss",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out archiveEndTime))
                            {
                                archiveEndBox.ForeColor = Colors.Error;
                                errors.Add("- Data/Ora Fine (archivio): formato non valido (dd/MM/yyyy HH:mm)");
                                archiveDateValid = false;
                            }
                            else
                            {
                                archiveEndBox.ForeColor = Colors.TextPrimary;
                            }
                        }
                        else
                        {
                            archiveEndBox.ForeColor = Colors.TextPrimary;
                        }

                        if (archiveDateValid && archiveStartTime >= archiveEndTime)
                        {
                            archiveStartBox.ForeColor = Colors.Error;
                            archiveEndBox.ForeColor = Colors.Error;
                            errors.Add("- L'ora di fine (archivio) deve essere successiva all'ora di inizio");
                            isValid = false;
                        }
                    }
                }

                if (!archiveDateValid) isValid = false;
            }

            // Validazione Date/Ore (solo per modalità "Da server")
            DateTime startTime = DateTime.MinValue;
            DateTime endTime = DateTime.MinValue;
            bool dateValid = true;
            
            if (!isArchiveMode && inizioBox != null && fineBox != null)
            {
                string startText = NormalizeTimeSeparators(GetRealValue(inizioBox));
                string endText = NormalizeTimeSeparators(GetRealValue(fineBox));
                
                // Validazione data inizio
                if (string.IsNullOrWhiteSpace(startText))
                {
                    inizioBox.ForeColor = Colors.Error;
                    errors.Add("- Data/Ora Inizio (obbligatoria)");
                    dateValid = false;
                }
                else if (!DateTime.TryParseExact(startText, "dd/MM/yyyy HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out startTime))
                {
                    // Prova anche con i secondi per retrocompatibilità
                    if (!DateTime.TryParseExact(startText, "dd/MM/yyyy HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out startTime))
                    {
                        inizioBox.ForeColor = Colors.Error;
                        errors.Add("- Data/Ora Inizio (formato: dd/MM/yyyy HH:mm)");
                        dateValid = false;
                    }
                    else
                    {
                        if (startTime.Year < 1900)
                        {
                            inizioBox.ForeColor = Colors.Error;
                            errors.Add("- Data/Ora Inizio (obbligatoria)");
                        dateValid = false;
                    }
                    else
                    {
                        inizioBox.ForeColor = Colors.TextPrimary;
                        }
                    }
                }
                else
                {
                    if (startTime.Year < 1900)
                    {
                        inizioBox.ForeColor = Colors.Error;
                        errors.Add("- Data/Ora Inizio (obbligatoria)");
                        dateValid = false;
                }
                else
                {
                    inizioBox.ForeColor = Colors.TextPrimary;
                    }
                }
                
                // Validazione data fine
                if (string.IsNullOrWhiteSpace(endText))
                {
                    fineBox.ForeColor = Colors.Error;
                    errors.Add("- Data/Ora Fine (obbligatoria)");
                    dateValid = false;
                }
                else if (!DateTime.TryParseExact(endText, "dd/MM/yyyy HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out endTime))
                {
                    // Prova anche con i secondi per retrocompatibilità
                    if (!DateTime.TryParseExact(endText, "dd/MM/yyyy HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out endTime))
                    {
                        fineBox.ForeColor = Colors.Error;
                        errors.Add("- Data/Ora Fine (formato: dd/MM/yyyy HH:mm)");
                        dateValid = false;
                    }
                    else
                    {
                        fineBox.ForeColor = Colors.TextPrimary;
                    }
                }
                else
                {
                    fineBox.ForeColor = Colors.TextPrimary;
                }
                
                // Validazione intervallo
                if (dateValid && startTime >= endTime)
                {
                    inizioBox.ForeColor = Colors.Error;
                    fineBox.ForeColor = Colors.Error;
                    errors.Add("- L'ora di fine deve essere successiva all'ora di inizio");
                    isValid = false;
                }
                else if (dateValid && (endTime - startTime).TotalDays > 30)
                {
                    warnings.Add("- Intervallo superiore a 30 giorni, l'export potrebbe richiedere molto tempo");
                }
            }
            
            if (!isArchiveMode && !dateValid) isValid = false;

            // Validazione Cartella Export
            string cartella = GetRealValue(cartellaBox);
            if (isPlaceholder(cartellaBox) || string.IsNullOrWhiteSpace(cartella))
            {
                cartellaBox.ForeColor = Colors.Error;
                errors.Add("- Cartella di Esportazione (obbligatoria)");
                isValid = false;
            }
            else if (!Directory.Exists(cartella))
            {
                // Prova a creare la directory
                try
                {
                    Directory.CreateDirectory(cartella);
                    cartellaBox.ForeColor = Colors.TextPrimary;
                }
                catch
                {
                    cartellaBox.ForeColor = Colors.Error;
                    errors.Add($"- Cartella '{cartella}' non valida o non accessibile");
                    isValid = false;
                }
            }
            else
            {
                // Verifica permessi di scrittura
                try
                {
                    string testFile = Path.Combine(cartella, ".test_write_" + Guid.NewGuid().ToString());
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    cartellaBox.ForeColor = Colors.TextPrimary;
                }
                catch
                {
                    cartellaBox.ForeColor = Colors.Error;
                    errors.Add($"- Cartella '{cartella}' non ha permessi di scrittura");
                    isValid = false;
                }
            }

            // Mostra errori e warning
            if (!isValid || warnings.Count > 0)
            {
                var builder = new StringBuilder();
                
                if (errors.Count > 0)
                {
                    builder.AppendLine("Errori da correggere prima di procedere:");
                    builder.AppendLine(string.Join("\n", errors));
                }
                
                if (warnings.Count > 0)
                {
                    if (builder.Length > 0)
                        builder.AppendLine().AppendLine();
                    builder.AppendLine("Avvisi:");
                    builder.AppendLine(string.Join("\n", warnings));
                }

                var icon = errors.Count > 0 ? DarkDialogIcon.Error : DarkDialogIcon.Warning;
                ShowDarkDialog("Validazione campi", builder.ToString().Trim(), icon, DarkDialogButtons.Ok);
            }

            return isValid;
        }

        /// <summary>
        /// Mostra progress all'utente.
        /// </summary>
        private void ShowProgress(object sender, EventArgs e)
        {
            if (_exporter != null)
            {
                int progress = _exporter.Progress;
                int lastError = _exporter.LastError;
                string lastErrorString = _exporter.LastErrorString;
                if (progress >= 0)
                {
                    ProgressBar progressBar = contentPanel.Controls.Find("ProgressBar", true).FirstOrDefault() as ProgressBar;
                    Label progressLabel = contentPanel.Controls.Find("ProgressText", true).FirstOrDefault() as Label;
                    
                    if (progressBar != null)
                        progressBar.Value = progress;
                    if (progressLabel != null)
                        progressLabel.Text = progress + "%";
                        
                    if (progress == 100)
                    {
                        _timer.Stop();
                        _exporter.EndExport();
                        
                        // Copia file automatica
                        Done();
                        
                        UpdateArchiveStatus("Archiviazione terminata!", 100);
                    }
                }
                if (lastError > 0)
                {
                    ProgressBar progressBar = contentPanel.Controls.Find("ProgressBar", true).FirstOrDefault() as ProgressBar;
                    if (progressBar != null)
                        progressBar.Value = 0;
                    UpdateArchiveStatus($"Errore: {lastErrorString} ({lastError})", 0);
                    if (_exporter != null)
                    {
                        _exporter.EndExport();
                        _exporter = null;
                    }
                }
            }
        }
        // Gestione progresso per job specifico (multi-export)
        // ✅ FIX #1, #2, #6: ShowProgressForJob con cleanup completo
        /// <summary>
        /// Mostra progress for job all'utente.
        /// </summary>
        private void ShowProgressForJob(ExportJob job)
        {
            if (job == null || job.IsCanceled)
                return;
            
            if (job.Exporter == null)
                return;

            try
            {
                int progress = Math.Max(0, Math.Min(100, job.Exporter.Progress));
                int lastError = job.Exporter.LastError;
                string lastErrorString = job.Exporter.LastErrorString ?? string.Empty;
                DateTime nowUtc = DateTime.UtcNow;

                job.LastActivityUtc = nowUtc;

                if (lastError > 0)
                {
                    string errorMessage = string.IsNullOrWhiteSpace(lastErrorString) ? $"Errore {lastError}" : lastErrorString;
                    FinalizeJobWithError(job, errorMessage, lastError, lastErrorString, "Errore");
                    return;
                }

                if (progress >= 100)
                {
                    job.Progress = 100;
                    UpdateJobProgressUI(job);
                    FinalizeJobCompletion(job, null);
                    return;
                }

                if (progress >= 0)
                {
                    if (progress > job.Progress)
                    {
                        job.LastProgressUtc = nowUtc;
                    }

                job.Progress = progress;
                    job.Status = "In corso";
                UpdateJobProgressUI(job);
                
                    try
                    {
                        _dataService.RecordJobProgress(job.Id, job.Progress, job.Status);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore aggiornamento progresso job {job.Id}: {ex.Message}");
                    }

                    if (job.LastLoggedProgress < 0 || progress >= job.LastLoggedProgress + 5)
                    {
                        job.LastLoggedProgress = progress;
                        AppendJobLogEntry(job, "progress", new Dictionary<string, object>
                        {
                            ["value"] = progress,
                            ["status"] = job.Status
                        });
                    }
                }

                if (job.LastProgressUtc != default && JobProgressTimeout > TimeSpan.Zero)
                {
                    if (nowUtc - job.LastProgressUtc > JobProgressTimeout)
                    {
                        string timeoutMessage = $"Nessun progresso registrato negli ultimi {JobProgressTimeout.TotalMinutes:0} minuti.";
                        FinalizeJobWithError(job, "Errore timeout", null, timeoutMessage, "Errore timeout");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore monitoraggio progresso job {job?.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// Esegue la logica finalize job completion senza cambiare il comportamento.
        /// </summary>
        private void FinalizeJobCompletion(ExportJob job, long? finalSizeBytesOverride)
        {
            if (job == null)
                return;

            if (!job.IsCanceled)
                    job.IsCanceled = true;

            StopJobTimer(job);
            DisposeJobExporter(job, false);

            var endedAtUtc = DateTime.UtcNow;
            var endedAtLocal = endedAtUtc.ToLocalTime();
            job.CompletedAtUtc = endedAtUtc;
                    job.Status = "Completato";
                    job.Progress = 100;
            job.LastLoggedProgress = 100;

            long finalSizeBytes = finalSizeBytesOverride ?? GetDirectorySize(job.Path);

                    AppendJobLogEntry(job, "completed", new Dictionary<string, object>
                    {
                        ["value"] = 100,
                        ["path"] = job.Path,
                        ["size"] = FormatFileSize(finalSizeBytes)
                    });

                    _logIndexService?.RegisterCompletion(
                        job.Id,
                        NormalizeStatus(job.Status),
                        job.LogFilePath,
                        finalSizeBytes,
                        job.Path);
                    
                    try
                    {
                        DoPostProcessingForJob(job);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Post-processing errore per job {job.Id}: {ex.Message}");
                    }
                    
            var duration = job.StartedAtUtc != default(DateTime)
                ? endedAtUtc - job.StartedAtUtc
                : TimeSpan.Zero;
            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;

                    var completedInfo = new ArchiviazioneInfo
                    {
                        Id = job.Id,
                        Telecamera = job.CameraName,
                        PeriodoInizio = job.StartTime,
                        PeriodoFine = job.EndTime,
                        Inizio = job.StartedAtUtc == default(DateTime) ? job.StartTime : job.StartedAtUtc.ToLocalTime(),
                        Fine = endedAtLocal,
                        DataAvvio = job.StartedAtUtc == default(DateTime) ? job.StartTime : job.StartedAtUtc.ToLocalTime(),
                Stato = job.Status,
                Durata = duration > TimeSpan.Zero ? duration.ToString(@"hh\:mm\:ss") : "N/D",
                        Dimensione = FormatSize(finalSizeBytes),
                DataCompletamento = endedAtLocal,
                        Cartella = job.Path,
                        Password = job.Password,
                        ServerAddress = !string.IsNullOrWhiteSpace(job.ServerAddress) ? job.ServerAddress : _currentServerAddress,
                        ProcedimentoPenale = job.ProcedimentoPenale,
                        RitSpec = job.RitSpec,
                        Target = job.Target,
                        IdLavoro = job.IdLavoro,
                Magistrato = job.Magistrato,
                Procura = job.Procura,
                Progresso = 100,
                        ErrorLog = job.Errors ?? new List<string>()
                    };
                    
                    try
                    {
                        _dataService.RecordJobCompleted(completedInfo);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore registrazione completamento job {job.Id}: {ex.Message}");
                    }
                    
            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["value"] = 100,
                    ["path"] = job.Path
                };
                _dataService.RecordJobEvent(job.Id, job.Status, job.Progress, "completed", payload);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore registrazione evento completamento job {job.Id}: {ex.Message}");
            }

                    lock (_completedExportsLock)
                    {
                        _completedExports.Add(completedInfo);
                    }
                    
            try
            {
                ExecuteOnUiThread(() => SaveExportHistory());
                    }
                    catch { }
                    
            lock (_exportLock)
            {
                _activeExports.Remove(job);
                _queuedExports.Remove(job);
            }

            string completionExtra = $"Dimensione finale: {FormatSize(finalSizeBytes)}";
            NotifyArchiveCompletion(job, true, completionExtra);
            RefreshUiAfterJobCompletion(refetchCompleted: true);
            TryStartNextQueuedJob();
        }

        /// <summary>
    /// Esegue la logica finalize job with error senza cambiare il comportamento.
    /// </summary>
    private void FinalizeJobWithError(ExportJob job, string errorMessage, int? errorCode, string detail, string statusOverride)
        {
            if (job == null)
                return;

            if (job.Errors == null)
                job.Errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(errorMessage))
                job.Errors.Add(errorMessage);
            if (!string.IsNullOrWhiteSpace(detail))
                job.Errors.Add(detail);

            StopJobTimer(job);
            DisposeJobExporter(job, true);

            job.IsCanceled = true;
            job.Status = string.IsNullOrWhiteSpace(statusOverride) ? "Errore" : statusOverride;
            job.Progress = Math.Max(0, job.Progress);
            job.LastActivityUtc = DateTime.UtcNow;

            long failureSize = 0;
            if (!string.IsNullOrWhiteSpace(job.Path) && Directory.Exists(job.Path))
            {
                try
                {
                    failureSize = GetDirectorySize(job.Path);
                    }
                    catch { }
                }
                
            AppendJobLogEntry(job, "error-final", new Dictionary<string, object>
            {
                ["message"] = errorMessage,
                ["code"] = errorCode,
                ["detail"] = detail
            });

            _logIndexService?.RegisterCompletion(
                job.Id,
                NormalizeStatus(job.Status),
                job.LogFilePath,
                null,
                job.Path);

            try
            {
                _dataService.RecordJobError(job.Id, errorCode ?? 0, errorMessage, detail);
                    }
                    catch (Exception ex)
                    {
                Debug.WriteLine($"Errore registrazione errore job {job.Id}: {ex.Message}");
            }

            try
            {
                var payloadEvent = new Dictionary<string, object>
                {
                    ["message"] = errorMessage,
                    ["detail"] = detail,
                    ["code"] = errorCode
                };
                _dataService.RecordJobEvent(job.Id, job.Status, job.Progress, "error", payloadEvent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore registrazione evento finale job {job.Id}: {ex.Message}");
            }

            var failureEndUtc = DateTime.UtcNow;
            var failureEnd = failureEndUtc.ToLocalTime();
            job.CompletedAtUtc = failureEndUtc;
            var failureDuration = job.StartedAtUtc != default(DateTime) ? failureEndUtc - job.StartedAtUtc : TimeSpan.Zero;
            if (failureDuration < TimeSpan.Zero)
                failureDuration = TimeSpan.Zero;

            var failureInfo = new ArchiviazioneInfo
            {
                Id = job.Id,
                Telecamera = job.CameraName,
                PeriodoInizio = job.StartTime,
                PeriodoFine = job.EndTime,
                Inizio = job.StartedAtUtc == default(DateTime) ? job.StartTime : job.StartedAtUtc.ToLocalTime(),
                Fine = failureEnd,
                DataAvvio = job.StartedAtUtc == default(DateTime) ? job.StartTime : job.StartedAtUtc.ToLocalTime(),
                Stato = job.Status,
                Progresso = job.Progress,
                Durata = failureDuration > TimeSpan.Zero ? failureDuration.ToString(@"hh\:mm\:ss") : "N/D",
                Dimensione = failureSize > 0 ? FormatSize(failureSize) : "N/D",
                DataCompletamento = failureEnd,
                Cartella = job.Path,
                Password = job.Password,
                ServerAddress = !string.IsNullOrWhiteSpace(job.ServerAddress) ? job.ServerAddress : _currentServerAddress,
                ProcedimentoPenale = job.ProcedimentoPenale,
                RitSpec = job.RitSpec,
                Target = job.Target,
                IdLavoro = job.IdLavoro,
                Magistrato = job.Magistrato,
                Procura = job.Procura,
                ErrorLog = job.Errors.ToList()
            };

            try
            {
                _dataService.RecordJobCompleted(failureInfo);
                    }
                    catch (Exception ex)
                    {
                Debug.WriteLine($"Errore aggiornamento stato finale job {job.Id}: {ex.Message}");
            }

            lock (_completedExportsLock)
            {
                _completedExports.Add(failureInfo);
            }

            lock (_exportLock)
            {
                _activeExports.Remove(job);
                _queuedExports.Remove(job);
            }

            var failureSummaryBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(errorMessage))
                failureSummaryBuilder.AppendLine($"Dettagli: {errorMessage}");
            if (!string.IsNullOrWhiteSpace(detail))
                failureSummaryBuilder.AppendLine(detail);
            if (errorCode.HasValue)
                failureSummaryBuilder.AppendLine($"Codice: {errorCode.Value}");

            NotifyArchiveCompletion(job, false, failureSummaryBuilder.ToString());
            RefreshUiAfterJobCompletion(refetchCompleted: true);
            TryStartNextQueuedJob();
        }

        /// <summary>
        /// Ferma job timer e libera le risorse.
        /// </summary>
        private void StopJobTimer(ExportJob job)
        {
            if (job?.Timer == null)
                return;

            try
                    {
                        job.Timer.Stop();
                        if (job.TimerHandler != null)
                        {
                            job.Timer.Tick -= job.TimerHandler;
                            job.TimerHandler = null;
                        }
                        job.Timer.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore stop timer job {job?.Id}: {ex.Message}");
            }
            finally
            {
                        job.Timer = null;
            }
        }

        /// <summary>
        /// Rilascia job exporter e chiude risorse gestite.
        /// </summary>
        private void DisposeJobExporter(ExportJob job, bool cancel)
        {
            if (job?.Exporter == null)
                return;

            try
            {
                if (cancel)
                {
                    try
                    {
                        job.Exporter.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore cancel exporter {job.Id}: {ex.Message}");
                    }
                }
                job.Exporter.EndExport();
                job.Exporter.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore dispose exporter {job?.Id}: {ex.Message}");
            }
            finally
            {
                        if (job.Exporter is IDisposable disposable)
                        {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore dispose exporter disposable {job?.Id}: {ex.Message}");
                    }
                }
                job.Exporter = null;
            }
        }

        /// <summary>
        /// Esegue la logica execute on ui thread senza cambiare il comportamento.
        /// </summary>
        private void ExecuteOnUiThread(Action action)
        {
            if (action == null || IsDisposed)
                return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke((MethodInvoker)(() => action()));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            else
            {
                try
                {
                    action();
                    }
                    catch { }
                }
            }

        /// <summary>
        /// Rinfresca ui after job completion per avere info fresche.
        /// </summary>
        private void RefreshUiAfterJobCompletion(bool refetchCompleted)
        {
            ExecuteOnUiThread(() =>
            {
                LoadInCorsoData();
                if (refetchCompleted)
                {
                    LoadCompletateData();
                }
                ApplyLaunchConnectionState();
            });
        }
        // Post-processing per job specifico - IDENTICO A EXPORTERFABIO Done()
        /// <summary>
        /// Esegue la logica do post processing for job senza cambiare il comportamento.
        /// </summary>
        private void DoPostProcessingForJob(ExportJob job)
        {
            if (job == null)
                return;

            try
            {
                if (string.IsNullOrEmpty(job.Path) || !Directory.Exists(job.Path))
                {
                    return;
                }

                string legacyLogPath = Path.Combine(job.Path, "POST_PROCESSING_LOG.txt");
                try
                {
                    if (File.Exists(legacyLogPath))
                    {
                        File.Delete(legacyLogPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Impossibile eliminare POST_PROCESSING_LOG.txt per il job {job.Id}: {ex.Message}");
                }

                string clientFiles = Path.Combine(job.Path, "Client Files");
                if (!Directory.Exists(clientFiles))
                {
                    Directory.CreateDirectory(clientFiles);
                }

                string sourcePlayer = ResolveSmartClientPlayerSource();
                string destPlayer = Path.Combine(job.Path, "SmartClient-Player.exe");
                if (!string.IsNullOrEmpty(sourcePlayer) && File.Exists(sourcePlayer))
                {
                    File.Copy(sourcePlayer, destPlayer, true);
                }
                else
                {
                    Debug.WriteLine("DoPostProcessingForJob(): impossibile trovare SmartClient-Player.exe da copiare.");
                }

                string sourceData = Path.Combine(job.Path, "Data");
                string destData = Path.Combine(clientFiles, "Data");
                if (Directory.Exists(sourceData))
                {
                    CopyDataPlusProject(sourceData, destData);
                    try
                    {
                        Directory.Delete(sourceData, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Impossibile eliminare la cartella originale Data per il job {job.Id}: {ex.Message}");
                    }
                }

                string sourceProject = Path.Combine(job.Path, "Project.scp");
                string destProject = Path.Combine(clientFiles, "Project.scp");
                if (File.Exists(sourceProject))
                {
                    File.Copy(sourceProject, destProject, true);
                    try
                    {
                        File.Delete(sourceProject);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Impossibile eliminare Project.scp originale per il job {job.Id}: {ex.Message}");
                    }
                }

                string sourceClient = Path.Combine(job.Path, "Client");
                string fallbackClient = ResolveClientPayloadSource();
                string destClient = Path.Combine(clientFiles, "Client");
                if (Directory.Exists(sourceClient))
                {
                    CopyDataPlusProject(sourceClient, destClient);
                    try
                    {
                        Directory.Delete(sourceClient, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Impossibile eliminare la cartella Client originale per il job {job.Id}: {ex.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(fallbackClient) && Directory.Exists(fallbackClient))
                {
                    CopyDataPlusProject(fallbackClient, destClient);
                }
                else
                {
                    Debug.WriteLine("DoPostProcessingForJob(): cartella Client non trovata né nel job né tra le dipendenze.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore post-processing job {job?.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// Aggiorna archive status e mantiene lo stato coerente.
        /// </summary>
        private void UpdateArchiveStatus(string status, int progress)
        {
            Label statusLabel = contentPanel.Controls.Find("ExportStatusText", true).FirstOrDefault() as Label;
            ProgressBar progressBar = contentPanel.Controls.Find("ProgressBar", true).FirstOrDefault() as ProgressBar;
            Label progressLabel = contentPanel.Controls.Find("ProgressText", true).FirstOrDefault() as Label;
            Label detailsLabel = contentPanel.Controls.Find("ExportDetailsText", true).FirstOrDefault() as Label;
            
            if (statusLabel != null)
                statusLabel.Text = status;
            if (progressBar != null)
                progressBar.Value = progress;
            if (progressLabel != null)
                progressLabel.Text = progress + "%";
            if (detailsLabel != null)
            {
                string pathToShow = _currentExportOutputPath ?? _path ?? "N/D";
                detailsLabel.Text = $"Server: {_currentServerAddress}\nTelecamera: {(_item?.Name ?? "N/A")}\nPercorso: {pathToShow}";
            }
        }


        /// <summary>
        /// Esegue la logica copy data plus project senza cambiare il comportamento.
        /// </summary>
        private void CopyDataPlusProject(string sorg, string dest)
        {
            DirectoryInfo dir = new DirectoryInfo(sorg);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sorg);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            if (!Directory.Exists(dest))
            {
                Directory.CreateDirectory(ToExtendedPath(dest));
            }

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(dest, file.Name);
                file.CopyTo(ToExtendedPath(temppath), true);
            }
            
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(dest, subdir.Name);
                CopyDataPlusProject(subdir.FullName, temppath);
            }
        }

        /// <summary>
        /// Esegue la logica enumerate dependency search roots senza cambiare il comportamento.
        /// </summary>
        private IEnumerable<string> EnumerateDependencySearchRoots()
        {
            var roots = new List<string>();
            var comparer = StringComparer.OrdinalIgnoreCase;

            void TryAdd(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                try
                {
                    var full = Path.GetFullPath(path);
                    if (!Directory.Exists(full))
                        return;
                    if (!roots.Contains(full, comparer))
                        roots.Add(full);
                }
                catch
                {
                    // Ignora percorsi non risolvibili
                }
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            TryAdd(baseDir);
            TryAdd(ResourcesDirectory);
            TryAdd(ClientPayloadDirectory);
            TryAdd(ClientFilesDirectory);

            try
            {
                var exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                TryAdd(exeDir);
            }
            catch { }

            var current = baseDir;
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    current = Path.GetFullPath(Path.Combine(current, ".."));
                    TryAdd(current);
                }
                catch
                {
                    break;
                }
            }

            return roots;
        }

        /// <summary>
        /// Esegue la logica combine path segments senza cambiare il comportamento.
        /// </summary>
        private static string CombinePathSegments(string root, string[] segments)
        {
            if (segments == null || segments.Length == 0)
                return root;

            string combined = root;
            foreach (var segment in segments)
            {
                combined = Path.Combine(combined, segment);
            }
            return combined;
        }

        /// <summary>
        /// Risolve client payload source senza interventi manuali.
        /// </summary>
        private string ResolveClientPayloadSource()
        {
            if (Directory.Exists(PrimaryClientFolder))
                return PrimaryClientFolder;

            foreach (var root in EnumerateDependencySearchRoots())
            {
                foreach (var candidate in ClientFolderRelativeCandidates)
                {
                    var path = CombinePathSegments(root, candidate);
                    if (Directory.Exists(path))
                        return path;
                }
            }
            return null;
        }

        /// <summary>
        /// Risolve smart client player source senza interventi manuali.
        /// </summary>
        private string ResolveSmartClientPlayerSource()
        {
            if (File.Exists(PrimaryPlayerExecutable))
                return PrimaryPlayerExecutable;

            foreach (var root in EnumerateDependencySearchRoots())
            {
                foreach (var candidate in PlayerExecutableRelativeCandidates)
                {
                    var path = CombinePathSegments(root, candidate);
                    if (File.Exists(path))
                        return path;
                }
            }
            return null;
        }
        /// <summary>
        /// Esegue la logica done senza cambiare il comportamento.
        /// </summary>
        private void Done()
        {
            try
            {
                string exportPath = _currentExportOutputPath ?? _path;

                if (string.IsNullOrEmpty(exportPath) || !Directory.Exists(exportPath))
                {
                    Debug.WriteLine("Done(): percorso di export non valido o non esistente");
                    return;
                }

                // Crea Client Files se non esiste
                string clientFiles = Path.Combine(exportPath, "Client Files");
                if (!Directory.Exists(clientFiles))
                {
                    Directory.CreateDirectory(clientFiles);
                }

                // Copia SmartClient-Player.exe se esiste
                string sorgPlayer = ResolveSmartClientPlayerSource();
                string destPlayer = Path.Combine(exportPath, "SmartClient-Player.exe");
                if (!string.IsNullOrEmpty(sorgPlayer) && File.Exists(sorgPlayer))
                {
                    File.Copy(sorgPlayer, destPlayer, true);
                }
                else
                {
                    Debug.WriteLine("Done(): SmartClient-Player.exe non trovato tra le dipendenze note");
                }

                // Gestione Data folder - verifica esistenza prima
                string sorgData = Path.Combine(exportPath, "Data");
                string destData = Path.Combine(clientFiles, "Data");
                
                if (Directory.Exists(sorgData))
                {
                    CopyDataPlusProject(sorgData, destData);
                    Directory.Delete(sorgData, true);
                }
                else
                {
                    Debug.WriteLine("Done(): Cartella Data non trovata nel percorso di export");
                }

                // Gestione Project.scp - verifica esistenza prima
                string sorgProjectScp = Path.Combine(exportPath, "Project.scp");
                string destProjectScp = Path.Combine(clientFiles, "Project.scp");
                
                if (File.Exists(sorgProjectScp))
                {
                File.Copy(sorgProjectScp, destProjectScp, true);
                File.Delete(sorgProjectScp);
                }
                else
                {
                    Debug.WriteLine("Done(): Project.scp non trovato nel percorso di export");
                }

                // Copia Client folder se esiste
                string sorgClient = ResolveClientPayloadSource();
                string destClient = Path.Combine(clientFiles, "Client");
                
                if (!string.IsNullOrEmpty(sorgClient) && Directory.Exists(sorgClient))
                {
                    CopyDataPlusProject(sorgClient, destClient);
                }
                else
                {
                    Debug.WriteLine("Done(): Cartella Client non trovata tra le dipendenze note");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Done(): Errore durante post-processing: {ex.Message}");
                // Non bloccare il completamento dell'export per errori di post-processing
            }
        }

        private string _currentServerAddress = null;  // NULL all'avvio = NON CONNESSO

        // Metodo per ottenere un font sicuro che esiste sempre
        // ✅ FIX #4: Metodi helper per cache GDI+
        /// <summary>
        /// Restituisce cached brush gia pronto.
        /// </summary>
        private static SolidBrush GetCachedBrush(Color color)
        {
            if (!_brushCache.ContainsKey(color))
            {
                _brushCache[color] = new SolidBrush(color);
            }
            return _brushCache[color];
        }
        
        /// <summary>
        /// Restituisce cached pen gia pronto.
        /// </summary>
        private static Pen GetCachedPen(Color color, float width = 1f)
        {
            var key = (color, width);
            if (!_penCache.ContainsKey(key))
            {
                _penCache[key] = new Pen(color, width);
            }
            return _penCache[key];
        }
        // ✅ FIX #30: GetSafeFont con cache (evita memory leak)
        /// <summary>
        /// Restituisce cached font gia pronto.
        /// </summary>
        private static Font GetCachedFont(string preferredFont, float size, FontStyle style = FontStyle.Regular)
        {
            var key = (preferredFont, size, style);

            if (_fontCache.TryGetValue(key, out var cachedFont))
            {
                return cachedFont;
            }

            Font createdFont = null;

            if (string.Equals(preferredFont, "Cascadia Mono", StringComparison.OrdinalIgnoreCase))
            {
                createdFont = TryCreateEmbeddedFont(preferredFont, size, style);
                if (createdFont != null)
                {
                    _fontCache[key] = createdFont;
                    return createdFont;
                }
            }

            string[] fallbackFonts = { preferredFont, "Consolas", "Courier New", "Lucida Console", "Monaco", "Menlo" };

            foreach (string fontName in fallbackFonts)
            {
                try
                {
                    Font testFont = new Font(fontName, size, style);
                    if (testFont.Name == fontName)
                    {
                        _fontCache[key] = testFont;
                        return testFont;
                    }
                    testFont.Dispose();
                }
                catch
                {
                    // Continua con il prossimo font
                }
            }

            createdFont = new Font(SystemFonts.DefaultFont.FontFamily, size, style);
            _fontCache[key] = createdFont;
            return createdFont;
        }

        /// <summary>
        /// Esegue la logica try create embedded font senza cambiare il comportamento.
        /// </summary>
        private static Font TryCreateEmbeddedFont(string preferredFont, float size, FontStyle style)
        {
            EnsureEmbeddedFontLoaded();

            lock (_fontCollectionLock)
            {
                try
                {
                    if (_embeddedFontCollection.Families != null && _embeddedFontCollection.Families.Length > 0)
                    {
                        var family = _embeddedFontCollection.Families
                            .FirstOrDefault(f => string.Equals(f.Name, preferredFont, StringComparison.OrdinalIgnoreCase))
                            ?? _embeddedFontCollection.Families.FirstOrDefault();

                        if (family != null)
                        {
                            return new Font(family, size, style);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Errore creazione font embedded: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Si assicura che la parte embedded font loaded sia pronta prima di procedere.
        /// </summary>
        private static void EnsureEmbeddedFontLoaded()
        {
            if (_embeddedCascadiaLoaded)
                return;

            lock (_fontCollectionLock)
            {
                if (_embeddedCascadiaLoaded)
                    return;

                try
                {
                    var assembly = typeof(MainForm).Assembly;
                    using (var fontStream = assembly.GetManifestResourceStream("ExportSample.CascadiaMono.ttf"))
                    {
                        if (fontStream != null)
                        {
                            int fontLength = (int)fontStream.Length;
                            byte[] fontData = new byte[fontLength];
                            int bytesRead = 0;
                            while (bytesRead < fontLength)
                            {
                                int read = fontStream.Read(fontData, bytesRead, fontLength - bytesRead);
                                if (read <= 0)
                                {
                                    break;
                                }
                                bytesRead += read;
                            }

                            if (bytesRead > 0)
                            {
                                IntPtr fontPtr = Marshal.AllocCoTaskMem(bytesRead);
                                try
                                {
                                    Marshal.Copy(fontData, 0, fontPtr, bytesRead);
                                    _embeddedFontCollection.AddMemoryFont(fontPtr, bytesRead);
                                }
                                finally
                                {
                                    Marshal.FreeCoTaskMem(fontPtr);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Errore caricamento font embedded: {ex.Message}");
                }
                finally
                {
                    _embeddedCascadiaLoaded = true;
                }
            }
        }

        // Versione non-statica per retrocompatibilità
        /// <summary>
        /// Restituisce safe font gia pronto.
        /// </summary>
        private Font GetSafeFont(string preferredFont, float size, FontStyle style = FontStyle.Regular)
        {
            return GetCachedFont(preferredFont, size, style);
        }
        
        /// <summary>
        /// Carica database state e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadDatabaseState()
        {
            try
            {
                _preferencesCache = _dataService.LoadPreferences();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento preferenze dal database: {ex.Message}");
                _preferencesCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            if (_preferencesCache == null)
            {
                _preferencesCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var completed = _dataService.GetCompletedExports();
                lock (_completedExportsLock)
                {
                    _completedExports.Clear();
                    if (completed != null)
                    {
                        foreach (var entry in completed)
                        {
                            if (entry != null)
                                _completedExports.Add(entry.Clone());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento archiviazioni dal database: {ex.Message}");
            }

            try
            {
                _lastExportConfiguration = _dataService.LoadLastExportConfiguration();
                if (_lastExportConfiguration != null && !string.IsNullOrWhiteSpace(_lastExportConfiguration.Procura))
                {
                    _currentProcura = NormalizeProcuraName(_lastExportConfiguration.Procura);
                    UpdateProcuraStatusLabel();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento ultima configurazione export: {ex.Message}");
                _lastExportConfiguration = null;
            }
        }

        // ==================== SISTEMA DI PERSISTENZA ====================
        #region Data Persistence
        
        /// <summary>
        /// Salva export history in modo sicuro.
        /// </summary>
        private void SaveExportHistory()
        {
            try
            {
                var lastExport = GetCurrentExportConfiguration();
                _dataService.SaveLastExportConfiguration(lastExport);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore salvataggio dati: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Carica export history e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadExportHistory()
        {
            try
            {
                var completedExports = _dataService.GetCompletedExports();

                lock (_completedExportsLock)
                {
                    _completedExports.Clear();
                    foreach (var info in completedExports)
                    {
                        if (info != null)
                            _completedExports.Add(info.Clone());
                    }
                }

                if (_currentTab == 2)
                    LoadCompletateData(completedExports);
                else
                    UpdateTabNotifications();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento dati: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Esegue la logica parse log timestamp senza cambiare il comportamento.
        /// </summary>
        private DateTime? ParseLogTimestamp(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.ToLocalTime();

            if (DateTime.TryParse(value, out parsed))
                return parsed;

            return null;
        }

        /// <summary>
        /// Applica export metadata alle impostazioni correnti.
        /// </summary>
        private void ApplyExportMetadata(ExportLogMetadata metadata)
        {
            if (metadata == null) return;

            var info = new ArchiviazioneInfo
            {
                ServerAddress = metadata.serverAddress,
                ProcedimentoPenale = metadata.procedimento,
                Magistrato = metadata.magistrato,
                RitSpec = metadata.ritSpec,
                Target = metadata.target,
                IdLavoro = metadata.idLavoro,
                Cartella = metadata.cartella,
                Password = metadata.password,
                Telecamera = metadata.camera
            };

            var start = ParseLogTimestamp(metadata.start);
            if (start.HasValue)
            {
                info.PeriodoInizio = start.Value;
                info.Inizio = start.Value;
            }

            var end = ParseLogTimestamp(metadata.end);
            if (end.HasValue)
            {
                info.PeriodoFine = end.Value;
                info.Fine = end.Value;
            }

            var launch = ParseLogTimestamp(metadata.timestamp);
            if (launch.HasValue)
                info.DataAvvio = launch.Value;

            ApplyConfigurationToLaunch(info, remember: true);
        }

        /// <summary>
        /// Carica last export from log e aggiorna la UI senza fronzoli.
        /// </summary>
        private bool LoadLastExportFromLog()
        {
            try
            {
                if (!Directory.Exists(_logDirectoryPath))
                    return false;

                var latestFile = Directory.EnumerateFiles(_logDirectoryPath, "*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestFile == null)
                    return false;

                using (var reader = new StreamReader(latestFile.FullName))
                {
                    var header = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(header))
                        return false;

                    var metadata = JsonConvert.DeserializeObject<ExportLogMetadata>(header);
                    if (metadata == null)
                        return false;

                    ApplyExportMetadata(metadata);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore lettura log export: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// Applica last export configuration alle impostazioni correnti.
        /// </summary>
        private void ApplyLastExportConfiguration(ArchiviazioneInfo lastExport)
        {
            if (lastExport == null)
                return;

            SetFieldValue("ServerAddressTextBox", lastExport.ServerAddress ?? "");
            SetFieldValue("ProcedimentoTextBox", lastExport.ProcedimentoPenale ?? "");
            SetFieldValue("MagistratoTextBox", lastExport.Magistrato ?? "");
            if (string.IsNullOrWhiteSpace(lastExport.Magistrato) && lastExport.Metadata != null &&
                lastExport.Metadata.TryGetValue("Magistrato", out var magistratoMeta) &&
                magistratoMeta != null)
            {
                SetFieldValue("MagistratoTextBox", Convert.ToString(magistratoMeta, CultureInfo.InvariantCulture) ?? string.Empty);
            }
            SetFieldValue("RitSpecTextBox", lastExport.RitSpec ?? "");
            SetFieldValue("TargetTextBox", lastExport.Target ?? "");
            SetFieldValue("IdLavoroTextBox", lastExport.IdLavoro ?? "");
            SetFieldValue("CartellaTextBox", lastExport.Cartella ?? "");
            SetFieldValue("PasswordTextBox", lastExport.Password ?? "");
            SetFieldValue("TelecameraTextBox", lastExport.Telecamera ?? "");
            SyncSelectedCamerasFromText();

            var periodoInizio = lastExport.PeriodoInizio != default(DateTime) ? lastExport.PeriodoInizio : lastExport.Inizio;
            var periodoFine = lastExport.PeriodoFine != default(DateTime) ? lastExport.PeriodoFine : lastExport.Fine;

            if (periodoInizio != default(DateTime))
                SetFieldValue("InizioTextBox", periodoInizio.ToString("dd/MM/yyyy HH:mm"));

            if (periodoFine != default(DateTime))
                SetFieldValue("FineTextBox", periodoFine.ToString("dd/MM/yyyy HH:mm"));

            if (!string.IsNullOrWhiteSpace(lastExport.Cartella))
            {
                _path = lastExport.Cartella;
                _currentExportOutputPath = null;
            }

            _selectedCamera = null;
            if (!string.IsNullOrWhiteSpace(lastExport.Procura))
            {
                _currentProcura = NormalizeProcuraName(lastExport.Procura);
                UpdateProcuraStatusLabel();
            }
        }

        /// <summary>
        /// Applica configuration to launch alle impostazioni correnti.
        /// </summary>
        private void ApplyConfigurationToLaunch(ArchiviazioneInfo config, bool remember = true, bool switchToLaunch = true)
        {
            if (config == null)
                return;

            var snapshot = config.Clone();

            if (switchToLaunch && _currentTab != 0)
                ShowTab(0);

            ApplyLastExportConfiguration(snapshot);

            if (remember)
                _lastExportConfiguration = snapshot;
        }

        /// <summary>
        /// Carica last export data from database e aggiorna la UI senza fronzoli.
        /// </summary>
        private bool LoadLastExportDataFromDatabase()
        {
            try
            {
                var lastExport = _dataService.GetMostRecentJob();
                if (lastExport == null)
                {
                    lastExport = _lastExportConfiguration ?? _dataService.LoadLastExportConfiguration();
                }
                if (lastExport == null)
                    return false;

                ApplyConfigurationToLaunch(lastExport, remember: true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento ultimo export dal database: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Carica last export data from json e aggiorna la UI senza fronzoli.
        /// </summary>
        private bool LoadLastExportDataFromJson()
        {
            try
            {
                if (!File.Exists(_dataFilePath)) return false;
                
                string json = File.ReadAllText(_dataFilePath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                
                if (data == null || !data.ContainsKey("lastExport")) return false;
                
                var lastExportJson = data["lastExport"] as Newtonsoft.Json.Linq.JObject;
                if (lastExportJson != null)
                {
                    var lastExport = lastExportJson.ToObject<ArchiviazioneInfo>();
                    if (lastExport != null)
                    {
                        ApplyConfigurationToLaunch(lastExport, remember: true);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento dati: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Carica last export data internal e aggiorna la UI senza fronzoli.
        /// </summary>
        private bool LoadLastExportDataInternal()
        {
            if (LoadLastExportDataFromDatabase())
                return true;

            if (LoadLastExportFromLog())
                return true;

            return LoadLastExportDataFromJson();
        }

        /// <summary>
        /// Carica last export data e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadLastExportData()
        {
            LoadLastExportDataInternal();
        }
        
        /// <summary>
        /// Carica completed exports e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadCompletedExports()
        {
            try
            {
                var completedEntries = _dataService.GetCompletedExports();

                lock (_completedExportsLock)
                {
                    _completedExports.Clear();
                    foreach (var entry in completedEntries)
                    {
                        if (entry != null)
                            _completedExports.Add(entry.Clone());
                    }
                }

                if (_currentTab == 2)
                {
                    LoadCompletateData(completedEntries);
                }
                else
                {
                    UpdateTabNotifications();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore caricamento export completati: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Carica active exports e aggiorna la UI senza fronzoli.
        /// </summary>
        private void LoadActiveExports()
        {
            RefreshInCorsoSnapshot(null, maintainSelection: false);
        }
        
        /// <summary>
        /// Restituisce current export configuration gia pronto.
        /// </summary>
        private ArchiviazioneInfo GetCurrentExportConfiguration()
        {
            var periodoInizio = ParseDateField("InizioTextBox");
            var periodoFine = ParseDateField("FineTextBox");

            var info = new ArchiviazioneInfo
            {
                ServerAddress = NormalizeServerAddress(GetFieldValue("ServerAddressTextBox")),
                ProcedimentoPenale = GetFieldValue("ProcedimentoTextBox"),
                RitSpec = GetFieldValue("RitSpecTextBox"),
                Magistrato = GetFieldValue("MagistratoTextBox"),
                Target = GetFieldValue("TargetTextBox"),
                IdLavoro = GetFieldValue("IdLavoroTextBox"),
                Cartella = GetFieldValue("CartellaTextBox"),
                Password = GetFieldValue("PasswordTextBox"),
                Telecamera = GetFieldValue("TelecameraTextBox"),
                PeriodoInizio = periodoInizio,
                PeriodoFine = periodoFine,
                Inizio = periodoInizio,
                Fine = periodoFine,
                Procura = _currentProcura
            };
            return info;
        }
        
        /// <summary>
        /// Restituisce field value gia pronto.
        /// </summary>
        private string GetFieldValue(string fieldName)
        {
            var control = contentPanel.Controls.Find(fieldName, true).FirstOrDefault();
            if (control is TextBox tb)
            {
                // Ignora placeholder
                if (tb.ForeColor == Colors.TextPlaceholder)
                    return "";
                return tb.Text;
            }
            if (control is ComboBox cb)
                return cb.Text;
            if (control is DateTimePicker dtp)
                return dtp.Value.ToString();
            return "";
        }

        /// <summary>
        /// Esegue la logica parse date field senza cambiare il comportamento.
        /// </summary>
        private DateTime ParseDateField(string fieldName)
        {
            var text = GetFieldValue(fieldName);
            return ParseDateText(text);
        }

        /// <summary>
        /// Esegue la logica parse date text senza cambiare il comportamento.
        /// </summary>
        private DateTime ParseDateText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return default(DateTime);

            text = NormalizeTimeSeparators(text.Trim());

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

        /// <summary>
        /// Prova a interpretare una stringa come solo orario (es. "14:00" o "18.30").
        /// Non gestisce la data, solo l'orario nella giornata.
        /// </summary>
        private bool TryParseTimeOfDay(string text, out TimeSpan timeOfDay)
        {
            timeOfDay = default(TimeSpan);

            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = NormalizeTimeSeparators(text.Trim());

            // Prova prima come TimeSpan puro ("HH:mm", "HH:mm:ss", ecc.)
            if (TimeSpan.TryParse(text, CultureInfo.CurrentCulture, out timeOfDay) ||
                TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out timeOfDay))
            {
                return true;
            }

            // Come fallback, prova come DateTime e prendi solo TimeOfDay
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dt) ||
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
            {
                timeOfDay = dt.TimeOfDay;
                return true;
            }

            return false;
        }

        private static readonly char[] TimeSeparatorCandidates = { '.', ',', ';' };

        /// <summary>
        /// Esegue la logica normalize time separators senza cambiare il comportamento.
        /// </summary>
        private static string NormalizeTimeSeparators(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            foreach (var ch in TimeSeparatorCandidates)
                value = value.Replace(ch, ':');

            return value;
        }

        /// <summary>
        /// Esegue la logica normalize date time text box senza cambiare il comportamento.
        /// </summary>
        private void NormalizeDateTimeTextBox(TextBox textBox)
        {
            if (textBox == null)
                return;

            var raw = NormalizeTimeSeparators(GetRealValue(textBox));
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var parsed = ParseDateText(raw);
            if (parsed != default(DateTime))
            {
                textBox.Text = parsed.ToString("dd/MM/yyyy HH:mm");
                textBox.ForeColor = Colors.TextPrimary;
            }
            else
            {
                var normalized = NormalizeTimeSeparators(raw);
                if (!string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
                    textBox.Text = normalized;
                textBox.ForeColor = Colors.TextPrimary;
            }
        }

        /// <summary>
        /// Aggancia date time normalization agli handler necessari.
        /// </summary>
        private void AttachDateTimeNormalization(TextBox textBox)
        {
            if (textBox == null)
                return;
            textBox.TextChanged += HandleDateTimeTextChanged;
            textBox.Leave += (s, e) => NormalizeDateTimeTextBox(textBox);
        }

        /// <summary>
        /// Gestisce date time text changed per evitare casini.
        /// </summary>
        private void HandleDateTimeTextChanged(object sender, EventArgs e)
        {
            if (!(sender is TextBox textBox))
                return;

            if (textBox.Tag is string placeholder &&
                string.Equals(textBox.Text, placeholder, StringComparison.OrdinalIgnoreCase))
                return;

            string current = textBox.Text ?? string.Empty;
            string normalized = NormalizeTimeSeparators(current);

            if (string.Equals(current, normalized, StringComparison.Ordinal))
                return;

            int selectionStart = textBox.SelectionStart;
            int selectionLength = textBox.SelectionLength;

            textBox.TextChanged -= HandleDateTimeTextChanged;
            textBox.Text = normalized;
            textBox.SelectionStart = Math.Min(selectionStart, textBox.Text.Length);
            textBox.SelectionLength = Math.Min(selectionLength, textBox.Text.Length - textBox.SelectionStart);
            textBox.TextChanged += HandleDateTimeTextChanged;
        }
        
        /// <summary>
        /// Imposta field value usando i parametri passati.
        /// </summary>
        private void SetFieldValue(string fieldName, string value)
        {
            var control = contentPanel.Controls.Find(fieldName, true).FirstOrDefault();
            if (control is TextBox tb)
            {
                string finalValue = value ?? string.Empty;
                if (string.Equals(fieldName, "ServerAddressTextBox", StringComparison.Ordinal))
                {
                    finalValue = NormalizeServerAddress(finalValue);
                    _currentServerAddress = finalValue;
                }

                if (string.Equals(fieldName, "InizioTextBox", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fieldName, "FineTextBox", StringComparison.OrdinalIgnoreCase))
                {
                    finalValue = NormalizeTimeSeparators(finalValue);
                    var parsed = ParseDateText(finalValue);
                    if (parsed != default(DateTime))
                        finalValue = parsed.ToString("dd/MM/yyyy HH:mm");
                }

                var placeholderValue = tb.Tag as string;

                if (!string.IsNullOrEmpty(placeholderValue) &&
                    string.Equals(finalValue, placeholderValue, StringComparison.OrdinalIgnoreCase))
                {
                    finalValue = string.Empty;
                }

                if (string.IsNullOrEmpty(finalValue))
                {
                    if (!string.IsNullOrEmpty(placeholderValue))
                    {
                        tb.Text = placeholderValue;
                        tb.ForeColor = Colors.TextPlaceholder;
                    }
                    else
                    {
                        tb.Text = string.Empty;
                        tb.ForeColor = Colors.TextPrimary;
                    }
                }
                else
                {
                    tb.Text = finalValue;
                    tb.ForeColor = Colors.TextPrimary;
                }
            }
        }
        
        /// <summary>
        /// Crea archiviazione info al volo.
        /// </summary>
        private ArchiviazioneInfo CreateArchiviazioneInfo(ExportJob job)
        {
            return new ArchiviazioneInfo
            {
                Id = job.Id,
                        Telecamera = job.CameraName,
                PeriodoInizio = job.StartTime,
                PeriodoFine = job.EndTime,
                Inizio = job.StartedAtUtc == default(DateTime) ? default(DateTime) : job.StartedAtUtc.ToLocalTime(),
                Fine = job.CompletedAtUtc == default(DateTime) ? default(DateTime) : job.CompletedAtUtc.ToLocalTime(),
                DataAvvio = job.StartedAtUtc == default(DateTime) ? default(DateTime) : job.StartedAtUtc.ToLocalTime(),
                DataCompletamento = job.CompletedAtUtc == default(DateTime) ? default(DateTime) : job.CompletedAtUtc.ToLocalTime(),
                        Progresso = job.Progress,
                        Stato = job.Status,
                        Cartella = job.Path,
                        Password = job.Password,
                        ServerAddress = _currentServerAddress,
                ProcedimentoPenale = job.ProcedimentoPenale,
                RitSpec = job.RitSpec,
                Magistrato = job.Magistrato,
                Target = job.Target,
                IdLavoro = job.IdLavoro,
                Procura = job.Procura,
                        ErrorLog = job.Errors ?? new List<string>()
            };
        }

        /// <summary>
        /// Crea snapshot job al volo.
        /// </summary>
        private ExportJob CreateSnapshotJob(ArchiviazioneInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Id))
                return null;

            var periodoInizio = info.PeriodoInizio != default(DateTime) ? info.PeriodoInizio : info.Inizio;
            var periodoFine = info.PeriodoFine != default(DateTime) ? info.PeriodoFine : info.Fine;

            var job = new ExportJob
            {
                Id = info.Id,
                CameraName = info.Telecamera,
                StartTime = periodoInizio != default(DateTime) ? periodoInizio : info.DataCompletamento,
                EndTime = periodoFine != default(DateTime) ? periodoFine : (periodoInizio != default(DateTime) ? periodoInizio : info.DataCompletamento),
                Progress = info.Progresso,
                Status = string.IsNullOrWhiteSpace(info.Stato) ? "In coda" : info.Stato,
                Path = info.Cartella,
                Password = info.Password,
                ProcedimentoPenale = info.ProcedimentoPenale,
                RitSpec = info.RitSpec,
                Target = info.Target,
                IdLavoro = !string.IsNullOrWhiteSpace(info.IdLavoro) ? info.IdLavoro : info.Id,
                Magistrato = info.Magistrato,
                Procura = info.Procura,
                Errors = info.ErrorLog ?? new List<string>(),
                CreatedAt = info.DataAvvio != default(DateTime) ? info.DataAvvio :
                            (periodoInizio != default(DateTime) ? periodoInizio :
                                (info.DataCompletamento != default(DateTime) ? info.DataCompletamento : DateTime.Now)),
                IsHistoricalSnapshot = true,
                ServerAddress = info.ServerAddress
            };

            return job;
        }
        
        // Metodo rimosso - non più necessario con deserializzazione diretta Newtonsoft.Json
        
        // Evento click per RECUPERA ULTIMO LANCIO
        /// <summary>
        /// Gestisce l'evento click del controllo recupera ultimo lancio per tenere la UI reattiva.
        /// </summary>
        private void RecuperaUltimoLancio_Click(object sender, EventArgs e)
        {
            RunWithButtonLoading(sender as Button, "RECUPERO...", () =>
            {
                if (LoadLastExportDataInternal())
                {
                    ShowSuccess("Recupero completato", "I dati dell'ultimo lancio sono stati ripristinati.");
                }
                else
                {
                    ShowInfo("Storico non disponibile", "Non è presente alcun log da cui recuperare l'ultimo lancio.");
                }
            });
        }
        
        // Altri eventi per IN CORSO
        /// <summary>
        /// Gestisce l'evento click del controllo annulla in corso per tenere la UI reattiva.
        /// </summary>
        private void AnnullaInCorso_Click(object sender, EventArgs e)
        {
            CancelArchiviazione_Click(sender, e);
        }
        
        /// <summary>
        /// Gestisce l'evento click del controllo annulla tutto per tenere la UI reattiva.
        /// </summary>
        private void AnnullaTutto_Click(object sender, EventArgs e)
        {
            if (ShowConfirmation(
                    "Annulla tutti gli export",
                    "Vuoi annullare tutti gli export attualmente in corso? I job verranno interrotti immediatamente.",
                    DarkDialogButtons.YesNo,
                    DarkDialogIcon.Warning,
                    primaryButtonText: "Annulla tutti",
                    secondaryButtonText: "Mantieni attivi") == DialogResult.Yes)
            {
                lock (_exportLock)
                {
                    foreach (var job in _activeExports.ToList())
                    {
                        job.IsCanceled = true;
                        if (job.Timer != null)
                        {
                            job.Timer.Stop();
                            job.Timer.Dispose();
                        }
                        if (job.Exporter != null)
                        {
                            job.Exporter.Cancel();
                            job.Exporter.EndExport();
                            job.Exporter.Close();
                        }
                    }
                    _activeExports.Clear();
                }
                LoadInCorsoData();
            }
        }
        
        // Evento per visualizzare dettagli export in corso
        /// <summary>
        /// Gestisce l'evento click del controllo dettagli export per tenere la UI reattiva.
        /// </summary>
        private void DettagliExport_Click(object sender, EventArgs e)
        {
            var listView = contentPanel.Controls.Find("InCorsoList", true).FirstOrDefault() as ListView ?? 
                          contentPanel.Controls.Find("InCorsoListView", true).FirstOrDefault() as ListView;
            if (listView?.SelectedItems.Count > 0)
            {
                var job = listView.SelectedItems[0].Tag as ExportJob;
                if (job != null)
                {
                    var details = $"ID: {job.Id}\n" +
                                 $"Telecamera: {job.CameraName}\n" +
                                 $"Inizio: {job.StartTime}\n" +
                                 $"Fine: {job.EndTime}\n" +
                                 $"Progresso: {job.Progress}%\n" +
                                 $"Stato: {job.Status}\n" +
                                 $"Path: {job.Path}\n" +
                                 $"Procedimento: {job.ProcedimentoPenale ?? "N/A"}\n" +
                                 $"RIT/SPEC: {job.RitSpec ?? "N/A"}\n" +
                                 $"Target: {job.Target ?? "N/A"}";
                    
                    ShowInfo("Dettagli export", details);
                }
            }
        }
        
        // Altri eventi per TERMINATE
        /// <summary>
        /// Gestisce l'evento click del controllo apri cartella per tenere la UI reattiva.
        /// </summary>
        private void ApriCartella_Click(object sender, EventArgs e)
        {
            OpenFolder_Click(sender, e);
        }
        
        /// <summary>
        /// Gestisce l'evento click del controllo elimina completato per tenere la UI reattiva.
        /// </summary>
        private void EliminaCompletato_Click(object sender, EventArgs e)
        {
            var listView = contentPanel.Controls.Find("TerminateListView", true).FirstOrDefault() as ListView;
            if (listView?.SelectedItems.Count > 0)
            {
                var info = listView.SelectedItems[0].Tag as ArchiviazioneInfo;
                if (info != null)
                {
                    if (ShowConfirmation(
                            "Elimina archiviazione completata",
                            $"Vuoi eliminare l'archiviazione {info.Id}?\nQuesta azione rimuoverà anche la cartella:\n{info.Cartella}",
                            DarkDialogButtons.YesNo,
                            DarkDialogIcon.Warning,
                            primaryButtonText: "Elimina",
                            secondaryButtonText: "Annulla") == DialogResult.Yes)
                    {
                        RunWithButtonLoading(sender as Button, "ELIMINO...", () =>
                        {
                            try
                            {
                                if (Directory.Exists(info.Cartella))
                                    Directory.Delete(info.Cartella, true);
                                
                                lock (_completedExportsLock)
                                {
                                    _completedExports.Remove(info);
                                }
                                
                                try
                                {
                                    _dataService.DeleteJob(info.Id);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Errore eliminazione job dal database: {ex.Message}");
                                }
                                
                                LoadCompletateData();
                                RenderTerminateDetails(null);
                                LoadTerminateLogContent(null);
                                SaveExportHistory();
                            }
                            catch (Exception ex)
                            {
                                ShowError("Errore eliminazione", $"Impossibile completare l'eliminazione.\nDettagli: {ex.Message}");
                            }
                        });
                    }
                }
            }
        }
        // Evento per visualizzare log export completato
        /// <summary>
        /// Gestisce l'evento click del controllo visualizza log per tenere la UI reattiva.
        /// </summary>
        private void VisualizzaLog_Click(object sender, EventArgs e)
        {
            var listView = contentPanel.Controls.Find("TerminateListView", true).FirstOrDefault() as ListView;
            if (listView?.SelectedItems.Count > 0)
            {
                var info = listView.SelectedItems[0].Tag as ArchiviazioneInfo;
                if (info != null)
                {
                    var logPath = _currentTerminateLogFile;
                    if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
                        logPath = ResolveCompletedLogPath(info);

                    if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("notepad.exe", logPath);
                        }
                        catch (Exception ex)
                        {
                            ShowError("Impossibile aprire il log", $"Si è verificato un errore durante l'apertura del log.\nDettagli: {ex.Message}");
                        }
                    }
                    else
                    {
                        ShowWarning("Log non trovato", "Non è stato possibile trovare il file di log per questa archiviazione.");
                    }
                }
            }
        }
        
        // Evento per esportare report CSV
        /// <summary>
        /// Gestisce l'evento click del controllo esporta report per tenere la UI reattiva.
        /// </summary>
        private void EsportaReport_Click(object sender, EventArgs e)
        {
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                saveDialog.FileName = $"Report_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    RunWithButtonLoading(sender as Button, "ESPORTO...", () =>
                    {
                        try
                        {
                            var csv = new StringBuilder();
                            csv.AppendLine("ID,Telecamera,Inizio,Fine,Durata,Dimensione,Data Completamento,Procedimento,Path");
                            
                            lock (_completedExportsLock)
                            {
                                foreach (var export in _completedExports)
                                {
                                    csv.AppendLine($"{export.Id},{export.Telecamera},{export.PeriodoInizio},{export.PeriodoFine}," +
                                                 $"{export.Durata},{export.Dimensione},{export.DataCompletamento}," +
                                                 $"{export.ProcedimentoPenale},{export.Cartella}");
                                }
                            }
                            
                            File.WriteAllText(saveDialog.FileName, csv.ToString());
                            ShowSuccess("Report esportato", "Il report CSV è stato generato correttamente.");
                        }
                        catch (Exception ex)
                        {
                            ShowError("Errore esportazione", $"Impossibile creare il report CSV.\nDettagli: {ex.Message}");
                        }
                    });
                }
            }
        }
        
        #endregion

        /// <summary>
        /// Esegue la logica adjust in corso columns senza cambiare il comportamento.
        /// </summary>
        private void AdjustInCorsoColumns(ListView listView)
        {
            if (_isAdjustingInCorsoColumns)
                return;

            if (listView == null || listView.Columns.Count == 0)
                return;

            try
            {
                _isAdjustingInCorsoColumns = true;
                listView.BeginUpdate();

                int columnCount = listView.Columns.Count;
                int clientWidth = Math.Max(0, listView.ClientSize.Width);
                bool hasVerticalScroll = listView.Items.Count * Sizes.ListViewRowHeight > listView.ClientSize.Height;
                if (hasVerticalScroll)
                {
                    clientWidth = Math.Max(0, clientWidth - SystemInformation.VerticalScrollBarWidth);
                }
                if (clientWidth == 0)
                    return;

                var minWidths = new int[columnCount];
                double totalMin = 0;
                for (int i = 0; i < columnCount; i++)
                {
                    minWidths[i] = GetInCorsoMinWidth(i);
                    totalMin += minWidths[i];
                }

                bool compress = clientWidth < totalMin && totalMin > 0;
                int assigned = 0;

                for (int i = 0; i < columnCount; i++)
                {
                    double ratio = totalMin > 0 ? minWidths[i] / totalMin : 0;
                    int suggested = (int)Math.Round(clientWidth * ratio);
                    int minWidth = compress ? Math.Max(40, suggested) : Math.Max(minWidths[i], suggested);

                    if (i == columnCount - 1)
                    {
                        int remaining = clientWidth - assigned;
                        if (remaining > 0)
                            minWidth = Math.Max(minWidth, remaining);
                    }

                    minWidth = Math.Max(40, minWidth);
                    listView.Columns[i].Width = minWidth;
                    assigned += minWidth;
                }

                // Correggi eventuali differenze dovute ad arrotondamenti distribuendo il delta sull'ultima colonna
                int totalAssigned = 0;
                for (int i = 0; i < columnCount; i++)
                    totalAssigned += listView.Columns[i].Width;

                int delta = clientWidth - totalAssigned;
                if (delta != 0 && columnCount > 0)
                {
                    int lastIndex = columnCount - 1;
                    int minLast = GetInCorsoMinWidth(lastIndex);
                    int newWidth = listView.Columns[lastIndex].Width + delta;
                    if (newWidth < minLast)
                        newWidth = minLast;
                    listView.Columns[lastIndex].Width = newWidth;
                }
            }
            finally
            {
                listView.EndUpdate();
                _isAdjustingInCorsoColumns = false;
            }
        }

        /// <summary>
        /// Callback WinForms per in corso column width changing.
        /// </summary>
        private void OnInCorsoColumnWidthChanging(ListView listView, ColumnWidthChangingEventArgs e)
        {
            if (listView == null || e.ColumnIndex < 0 || e.ColumnIndex >= listView.Columns.Count)
                return;

            int lastIndex = listView.Columns.Count - 1;
            int min = GetInCorsoMinWidth(e.ColumnIndex);
            int minLast = GetInCorsoMinWidth(lastIndex);

            if (e.ColumnIndex == lastIndex)
            {
                if (e.NewWidth < minLast)
                    e.NewWidth = minLast;
                return;
            }

            int clientWidth = listView.ClientSize.Width;
            if (clientWidth <= 0)
                return;

            int otherWidth = 0;
            for (int i = 0; i < lastIndex; i++)
            {
                if (i == e.ColumnIndex)
                    continue;
                otherWidth += listView.Columns[i].Width;
            }

            int maxWidth = clientWidth - otherWidth - minLast;
            if (maxWidth < min)
                maxWidth = min;

            if (e.NewWidth < min)
                e.NewWidth = min;
            else if (e.NewWidth > maxWidth)
                e.NewWidth = maxWidth;

            int available = clientWidth - (otherWidth + e.NewWidth);
            int newLastWidth = Math.Max(minLast, available);

            if (newLastWidth != listView.Columns[lastIndex].Width)
            {
                try
                {
                    _isAdjustingInCorsoColumns = true;
                    listView.Columns[lastIndex].Width = newLastWidth;
                }
                finally
                {
                    _isAdjustingInCorsoColumns = false;
                }
            }
        }

        /// <summary>
        /// Applica in corso initial layout alle impostazioni correnti.
        /// </summary>
        private void ApplyInCorsoInitialLayout(ListView listView)
        {
            if (listView == null || listView.Columns.Count == 0)
                return;

            int totalWidth = listView.ClientSize.Width;
            if (totalWidth <= 0)
            {
                AdjustInCorsoColumns(listView);
                return;
            }

            int columnCount = listView.Columns.Count;
            int lastIndex = columnCount - 1;
            int minTotal = 0;

            for (int i = 0; i < columnCount; i++)
            {
                minTotal += GetInCorsoMinWidth(i);
            }

            if (totalWidth <= minTotal)
            {
                double scale = totalWidth / (double)Math.Max(1, minTotal);
                for (int i = 0; i < columnCount; i++)
                {
                    int min = GetInCorsoMinWidth(i);
                    int scaled = (int)Math.Round(min * scale);
                    listView.Columns[i].Width = Math.Max(min, scaled);
                }
            }
            else
            {
                int defaultWidth = totalWidth / columnCount;
            for (int i = 0; i < columnCount; i++)
            {
                    int min = GetInCorsoMinWidth(i);
                    listView.Columns[i].Width = Math.Max(min, defaultWidth);
            }
            }

            AdjustInCorsoColumns(listView);
        }

        /// <summary>
        /// Esegue la logica adjust completate columns senza cambiare il comportamento.
        /// </summary>
        private void AdjustCompletateColumns(ListView listView)
        {
            if (listView == null || listView.Columns.Count < 7)
                return;

            int totalWidth = listView.ClientSize.Width;
            if (totalWidth <= 0)
                return;

            int idWidth = 90;
            int durationWidth = 90;
            int sizeWidth = 120;
            int dateWidth = 160;
            int remaining = Math.Max(totalWidth - idWidth - durationWidth - sizeWidth - dateWidth, 200);
            int share = remaining / 2;

            listView.Columns[0].Width = idWidth;
            listView.Columns[1].Width = share + 60;
            listView.Columns[2].Width = share;
            listView.Columns[3].Width = share;
            listView.Columns[4].Width = durationWidth;
            listView.Columns[5].Width = sizeWidth;
            listView.Columns[6].Width = dateWidth;
        }

        /// <summary>
        /// Esegue la logica parse size to bytes senza cambiare il comportamento.
        /// </summary>
        private long ParseSizeToBytes(string sizeText)
        {
            if (string.IsNullOrWhiteSpace(sizeText))
                return 0;

            var normalized = sizeText.Trim().ToUpperInvariant().Replace(" ", string.Empty);
            double value;
            string numericPart;

            if (normalized.EndsWith("TB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L * 1024L * 1024L * 1024L);
            }
            else if (normalized.EndsWith("GB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L * 1024L * 1024L);
            }
            else if (normalized.EndsWith("MB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L * 1024L);
            }
            else if (normalized.EndsWith("KB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L);
            }
            else if (normalized.EndsWith("B"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 1);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)value;
            }
            else if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return (long)value;
            }

            return 0;
        }
        /// <summary>
        /// Risolve archive size senza interventi manuali.
        /// </summary>
        private long ResolveArchiveSize(ArchiviazioneInfo info)
        {
            if (info == null)
                return 0;

            if (info.Metadata == null)
                info.Metadata = new Dictionary<string, object>();

            if (info.Metadata.TryGetValue("ComputedSizeBytes", out var cached) && cached is long cachedSize)
                return cachedSize;

            long size = ParseSizeToBytes(info.Dimensione);

            if (size <= 0 && !string.IsNullOrWhiteSpace(info.Cartella))
            {
                try
                {
                    if (Directory.Exists(info.Cartella))
                        size = GetDirectorySize(info.Cartella);
                }
                catch
                {
                    size = 0;
                }
            }

            info.Metadata["ComputedSizeBytes"] = size;
            return size;
        }

        /// <summary>
        /// Esegue la logica sanitize for file name senza cambiare il comportamento.
        /// </summary>
        private string SanitizeForFileName(string value)
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
        /// <summary>
        /// Inizializza ialize job log con i default.
        /// </summary>
        private void InitializeJobLog(ExportJob job, string initialEventType = "started", string initialMessage = "Archiviazione avviata")
        {
            if (job == null) return;

            try
            {
                EnsureLogDirectory();

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string sanitizedId = SanitizeForFileName(job.Id);
                string sanitizedCamera = SanitizeForFileName(job.CameraName);
                string fileName = $"ToolArchMilestone_{timestamp}_{sanitizedId}_{sanitizedCamera}.log";
                job.LogFilePath = Path.Combine(_logDirectoryPath, fileName);
                job.LastLoggedProgress = -1;

                var metadata = new Dictionary<string, object>
                {
                    ["tool"] = "ToolArchiviazioniMilestone",
                    ["version"] = ToolVersion,
                    ["jobId"] = job.Id,
                    ["timestamp"] = DateTime.Now.ToString("o"),
                    ["serverAddress"] = _currentServerAddress,
                    ["camera"] = job.CameraName,
                    ["start"] = job.StartTime.ToString("o"),
                    ["end"] = job.EndTime.ToString("o"),
                    ["procedimento"] = job.ProcedimentoPenale,
                    ["ritSpec"] = job.RitSpec,
                    ["target"] = job.Target,
                    ["idLavoro"] = job.IdLavoro,
                    ["cartella"] = job.Path,
                    ["password"] = job.Password,
                    ["status"] = job.Status
                };

                File.WriteAllText(job.LogFilePath, JsonConvert.SerializeObject(metadata) + System.Environment.NewLine, Encoding.UTF8);
                var metadataStrings = metadata
                    .Where(kvp => kvp.Value != null)
                    .ToDictionary(kvp => kvp.Key, kvp => Convert.ToString(kvp.Value, CultureInfo.InvariantCulture));

                _logIndexService?.RegisterOrUpdate(new LogIndexService.LogIndexEntry
                {
                    JobId = job.Id,
                    FilePath = job.LogFilePath,
                    Status = string.IsNullOrWhiteSpace(job.Status) ? "In corso" : job.Status,
                    Progress = job.Progress,
                    LastEvent = initialEventType,
                    Metadata = metadataStrings
                });

                AppendJobLogEntry(job, initialEventType, new Dictionary<string, object>
                {
                    ["message"] = initialMessage
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore creazione log export: {ex.Message}");
            }
        }
        /// <summary>
        /// Esegue la logica append job log entry senza cambiare il comportamento.
        /// </summary>
        private void AppendJobLogEntry(ExportJob job, string eventType, IDictionary<string, object> data = null)
        {
            if (job == null || string.IsNullOrEmpty(job.LogFilePath))
                return;

            try
            {
                var entry = new Dictionary<string, object>
                {
                    ["ts"] = DateTime.Now.ToString("o"),
                    ["event"] = eventType,
                    ["jobId"] = job.Id
                };

                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        entry[kvp.Key] = kvp.Value;
                    }
                }

                _logIndexService?.RegisterEvent(job.Id, eventType, job.Status, job.Progress);
                File.AppendAllText(job.LogFilePath, JsonConvert.SerializeObject(entry) + System.Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore scrittura log export: {ex.Message}");
            }
        }

        #region Dark Dialog Helpers

        /// <summary>
        /// Mostra dark dialog all'utente.
        /// </summary>
        private DialogResult ShowDarkDialog(
            string title,
            string message,
            DarkDialogIcon icon = DarkDialogIcon.Info,
            DarkDialogButtons buttons = DarkDialogButtons.Ok,
            string primaryButtonText = null,
            string secondaryButtonText = null,
            IWin32Window owner = null)
        {
            if (InvokeRequired)
            {
                return (DialogResult)Invoke(new Func<DialogResult>(() =>
                    ShowDarkDialog(title, message, icon, buttons, primaryButtonText, secondaryButtonText, owner)));
            }

            using (var dialog = new DarkDialogForm(
                       title,
                       message,
                       icon,
                       buttons,
                       primaryButtonText,
                       secondaryButtonText))
            {
                return dialog.ShowDialog(owner ?? this);
            }
        }

        /// <summary>
        /// Mostra info all'utente.
        /// </summary>
        private void ShowInfo(string title, string message)
        {
            ShowDarkDialog(title, message, DarkDialogIcon.Info, DarkDialogButtons.Ok);
        }

        /// <summary>
        /// Mostra success all'utente.
        /// </summary>
        private void ShowSuccess(string title, string message)
        {
            ShowDarkDialog(title, message, DarkDialogIcon.Success, DarkDialogButtons.Ok);
        }

        /// <summary>
        /// Mostra warning all'utente.
        /// </summary>
        private void ShowWarning(string title, string message)
        {
            ShowDarkDialog(title, message, DarkDialogIcon.Warning, DarkDialogButtons.Ok);
        }

        /// <summary>
        /// Mostra error all'utente.
        /// </summary>
        private void ShowError(string title, string message)
        {
            ShowDarkDialog(title, message, DarkDialogIcon.Error, DarkDialogButtons.Ok);
        }

        /// <summary>
        /// Mostra confirmation all'utente.
        /// </summary>
        private DialogResult ShowConfirmation(
            string title,
            string message,
            DarkDialogButtons buttons = DarkDialogButtons.YesNo,
            DarkDialogIcon icon = DarkDialogIcon.Warning,
            string primaryButtonText = null,
            string secondaryButtonText = null)
        {
            return ShowDarkDialog(title, message, icon, buttons, primaryButtonText, secondaryButtonText);
        }

        private sealed class DarkDialogForm : Form
        {
            private readonly Button _primaryButton;
            private readonly Button _secondaryButton;

            /// <summary>
            /// Costruttore di DarkDialogForm, inizializza il contesto senza effetti collaterali.
            /// </summary>
            public DarkDialogForm(
                string title,
                string message,
                DarkDialogIcon icon,
                DarkDialogButtons buttons,
                string primaryButtonText,
                string secondaryButtonText)
            {
                DoubleBuffered = true;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                MaximizeBox = false;
                MinimizeBox = false;
                ControlBox = true;
                BackColor = Color.FromArgb(18, 18, 22);
                ForeColor = Colors.TextPrimary;
                // Padding come in origine: un po' più aria sopra e sotto (header/footer)
                Padding = new Padding(Spacing.MD, Spacing.SM, Spacing.MD, Spacing.SM);
                Width = 400;
                // Larghezza minima fissa, altezza determinata interamente dal contenuto
                MinimumSize = new Size(340, 0);
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Text = title ?? "Tool Archiviazioni Milestone";

                var contentLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = Color.Transparent,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink
                };
                contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // header
                contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // messaggio
                contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // pulsanti

                var headerPanel = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Dock = DockStyle.Fill,
                    // Nessun margine sotto: il testo degli intervalli parte subito dopo il titolo
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };
                headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                var iconLabel = new Label
                {
                    Text = GetIconGlyph(icon),
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXL, FontStyle.Bold),
                    ForeColor = GetIconColor(icon),
                    AutoSize = true,
                    Margin = new Padding(0, 0, Spacing.SM, 0),
                    TextAlign = ContentAlignment.MiddleCenter
                };

                var titleLabel = new Label
                {
                    Text = title ?? "Tool Archiviazioni Milestone",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeLG, FontStyle.Bold),
                    ForeColor = Colors.TextPrimary,
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 0)
                };

                headerPanel.Controls.Add(iconLabel, 0, 0);
                headerPanel.Controls.Add(titleLabel, 1, 0);

                // Area messaggio scrollabile per elenchi lunghi di intervalli / errori
                var messagePanel = new Panel
                {
                    Dock = DockStyle.Top,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(18, 18, 22),
                    // Margine minimo solo sotto per non attaccare i pulsanti
                    Margin = new Padding(0, Spacing.XXS, 0, Spacing.XXS),
                    Padding = Padding.Empty,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink
                };

                var messageLabel = new Label
                {
                    Text = message ?? string.Empty,
                    AutoSize = true,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Regular),
                    ForeColor = Colors.TextSecondary,
                    BackColor = Color.FromArgb(18, 18, 22),
                    Margin = Padding.Empty
                };

                // Limita la larghezza del testo per evitare righe eccessivamente lunghe
                int maxWidth = 340 - Padding.Left - Padding.Right - 10;
                if (maxWidth < 120) maxWidth = 120;
                messageLabel.MaximumSize = new Size(maxWidth, 0);

                messagePanel.Controls.Add(messageLabel);

                var buttonsPanel = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    WrapContents = false,
                    Margin = new Padding(0, Spacing.MD, 0, 0),
                    Padding = new Padding(0, 0, 0, 0)
                };

                var buttonConfig = ResolveButtons(buttons);

                string primaryText = primaryButtonText ?? buttonConfig.PrimaryText;
                string secondaryText = secondaryButtonText ?? buttonConfig.SecondaryText;

                _primaryButton = CreateDialogButton(primaryText, true);
                _primaryButton.Click += (s, e) =>
                {
                    DialogResult = buttonConfig.PrimaryResult;
                    Close();
                };

                buttonsPanel.Controls.Add(_primaryButton);

                if (buttonConfig.SecondaryResult != DialogResult.None)
                {
                    _secondaryButton = CreateDialogButton(secondaryText, false);
                    _secondaryButton.Click += (s, e) =>
                    {
                        DialogResult = buttonConfig.SecondaryResult;
                        Close();
                    };
                    buttonsPanel.Controls.Add(_secondaryButton);
                    CancelButton = _secondaryButton;
                }
                else
                {
                    _secondaryButton = null;
                }

                AcceptButton = _primaryButton;

                contentLayout.Controls.Add(headerPanel, 0, 0);
                // Usa il pannello scrollabile con label come area messaggio
                contentLayout.Controls.Add(messagePanel, 0, 1);
                contentLayout.Controls.Add(buttonsPanel, 0, 2);
                Controls.Add(contentLayout);
            }

            /// <summary>
            /// Crea dialog button al volo.
            /// </summary>
            private static Button CreateDialogButton(string text, bool isPrimary)
            {
                var button = new Button
                {
                    Text = text ?? (isPrimary ? "OK" : "Annulla"),
                    AutoSize = false,
                    Width = 110,
                    Height = Sizes.ButtonHeight,
                    Margin = new Padding(Spacing.XS, 0, 0, 0),
                    Padding = new Padding(Spacing.SM, 0, Spacing.SM, 0),
                    FlatStyle = FlatStyle.Flat,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold),
                    ForeColor = isPrimary ? Colors.PrimaryLight : Colors.TextSecondary,
                    BackColor = Color.FromArgb(26, 26, 30),
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 60);

                var baseBack = button.BackColor;
                var hoverBack = Color.FromArgb(32, 32, 36);
                var downBack = Color.FromArgb(22, 22, 26);

                button.MouseEnter += (s, e) =>
                {
                    button.BackColor = hoverBack;
                    if (!isPrimary)
                        button.ForeColor = Colors.TextPrimary;
                };

                button.MouseLeave += (s, e) =>
                {
                    button.BackColor = baseBack;
                    if (!isPrimary)
                        button.ForeColor = Colors.TextSecondary;
                };

                button.MouseDown += (s, e) => button.BackColor = downBack;
                button.MouseUp += (s, e) => button.BackColor = hoverBack;

                return button;
            }

            /// <summary>
            /// Esegue la logica static senza cambiare il comportamento.
            /// </summary>
            private static (string PrimaryText, string SecondaryText, DialogResult PrimaryResult, DialogResult SecondaryResult) ResolveButtons(DarkDialogButtons buttons)
            {
                switch (buttons)
                {
                    case DarkDialogButtons.OkCancel:
                        return ("OK", "Annulla", DialogResult.OK, DialogResult.Cancel);
                    case DarkDialogButtons.YesNo:
                        return ("Sì", "No", DialogResult.Yes, DialogResult.No);
                    default:
                        return ("OK", null, DialogResult.OK, DialogResult.None);
                }
            }

            /// <summary>
            /// Restituisce icon glyph gia pronto.
            /// </summary>
            private static string GetIconGlyph(DarkDialogIcon icon)
            {
                switch (icon)
                {
                    case DarkDialogIcon.Success:
                        return "✔";
                    case DarkDialogIcon.Warning:
                        return "⚠";
                    case DarkDialogIcon.Error:
                        return "✖";
                    case DarkDialogIcon.Info:
                        return "ℹ";
                    default:
                        return string.Empty;
                }
            }

            /// <summary>
            /// Restituisce icon color gia pronto.
            /// </summary>
            private static Color GetIconColor(DarkDialogIcon icon)
            {
                switch (icon)
                {
                    case DarkDialogIcon.Success:
                        return Colors.Success;
                    case DarkDialogIcon.Warning:
                        return Colors.Warning;
                    case DarkDialogIcon.Error:
                        return Colors.Error;
                    case DarkDialogIcon.Info:
                        return Colors.Info;
                    default:
                        return Colors.TextPrimary;
                }
            }
        }

        #endregion

        #region Firma Autoriale

        /// <summary>
        /// Inizializza ialize status bar signature detection con i default.
        /// </summary>
        private void InitializeStatusBarSignatureDetection()
        {
            if (statusPanel == null)
                return;

            statusPanel.ControlAdded -= OnStatusBarControlAdded;
            statusPanel.ControlAdded += OnStatusBarControlAdded;

            AttachStatusBarSignatureHandlers(statusPanel);
        }

        /// <summary>
        /// Callback WinForms per status bar control added.
        /// </summary>
        private void OnStatusBarControlAdded(object sender, ControlEventArgs e)
        {
            AttachStatusBarSignatureHandlers(e.Control);
        }

        /// <summary>
        /// Aggancia status bar signature handlers agli handler necessari.
        /// </summary>
        private void AttachStatusBarSignatureHandlers(Control control)
        {
            if (control == null)
                return;

            control.MouseDown -= OnStatusBarSignatureMouseDown;
            control.MouseDown += OnStatusBarSignatureMouseDown;

            foreach (Control child in control.Controls)
            {
                AttachStatusBarSignatureHandlers(child);
            }
        }

        /// <summary>
        /// Callback WinForms per status bar signature mouse down.
        /// </summary>
        private void OnStatusBarSignatureMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                ResetStatusBarSignatureProgress();
                return;
            }

            var now = DateTime.UtcNow;

            if ((now - _statusBarLastClickUtc).TotalMilliseconds <= SignatureStatusBarClickTimeoutMs)
            {
                _statusBarClickCount++;
            }
            else
            {
                _statusBarClickCount = 1;
            }

            _statusBarLastClickUtc = now;

            if (_statusBarClickCount >= SignatureStatusBarClicksRequired)
            {
                _statusBarClickCount = 0;
                ShowSignaturePopup();
            }
        }

        /// <summary>
        /// Reimposta status bar signature progress ad un valore pulito.
        /// </summary>
        private void ResetStatusBarSignatureProgress()
        {
            _statusBarClickCount = 0;
            _statusBarLastClickUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Mostra signature popup all'utente.
        /// </summary>
        private void ShowSignaturePopup()
        {
            if (_signaturePopupVisible)
                return;

            try
            {
                _signaturePopupVisible = true;
                using (var popup = new SignaturePopupForm("1.0"))
                {
                    popup.ShowDialog(this);
                }
            }
            finally
            {
                _signaturePopupVisible = false;
            }
        }

        private sealed class SignaturePopupForm : Form
        {
            /// <summary>
            /// Costruttore di SignaturePopupForm, inizializza il contesto senza effetti collaterali.
            /// </summary>
            public SignaturePopupForm(string version)
            {
                DoubleBuffered = true;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                MaximizeBox = false;
                MinimizeBox = false;
                ControlBox = true;
                Width = 360;
                Height = 280;
                MinimumSize = new Size(340, 240);
                Padding = new Padding(Spacing.MD);
                BackColor = Color.FromArgb(18, 18, 22);
                ForeColor = Colors.TextPrimary;
                Text = "Firma digitale del tool";
                KeyPreview = true;
                KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape)
                        Close();
                };

                string versionText = string.IsNullOrWhiteSpace(version) ? "1.0" : version.Trim();

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 6,
                    BackColor = Color.Transparent,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var titleLabel = new Label
                {
                    Text = "Tool Archiviazioni Milestone",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeMD, FontStyle.Bold),
                    ForeColor = Colors.TextPrimary,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, Spacing.XXS)
                };

                var subtitleLabel = new Label
                {
                    Text = "Soluzione RCS S.p.A. per archiviazioni legali.",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Regular),
                    ForeColor = Colors.TextSecondary,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, Spacing.SM),
                    MaximumSize = new Size(320, 0)
                };

                var versionLabel = new Label
                {
                    Text = $"Versione {versionText}",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Bold),
                    ForeColor = Colors.TextPrimary,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, Spacing.SM)
                };

                var signaturePanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Padding = new Padding(Spacing.MD),
                    BackColor = Color.FromArgb(24, 24, 28),
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0, 0, 0, Spacing.SM),
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink
                };

                var signatureLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    ColumnCount = 1,
                    RowCount = 4,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink
                };
                signatureLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                signatureLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                signatureLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                signatureLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var signatureIntro = new Label
                {
                    Text = "Ideazione, codice e finiture a cura di",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Italic),
                    ForeColor = Colors.TextSecondary,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, Spacing.XXS)
                };

                var signatureName = new Label
                {
                    Text = "Carmelo Lanzafame",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeMD, FontStyle.Bold),
                    ForeColor = Colors.TextPrimary,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, Spacing.XXS)
                };

                var signatureCompany = new Label
                {
                    Text = "per RCS S.p.A.",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Regular),
                    ForeColor = Colors.TextPrimary,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, Spacing.XS)
                };

                var signatureNote = new Label
                {
                    Text = "Strumento sviluppato per accompagnare i flussi di archiviazione con efficienza e professionalità.",
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeXS, FontStyle.Regular),
                    ForeColor = Colors.TextSecondary,
                    AutoSize = true,
                    Margin = new Padding(0, Spacing.XS, 0, 0),
                    MaximumSize = new Size(320, 0)
                };

                signatureLayout.Controls.Add(signatureIntro, 0, 0);
                signatureLayout.Controls.Add(signatureName, 0, 1);
                signatureLayout.Controls.Add(signatureCompany, 0, 2);
                signatureLayout.Controls.Add(signatureNote, 0, 3);

                signaturePanel.Controls.Add(signatureLayout);

                var buttonsPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    AutoSize = true,
                    WrapContents = false,
                    Margin = new Padding(0, Spacing.MD, 0, 0)
                };

                var closeButton = CreateSignatureButton("Chiudi");
                buttonsPanel.Controls.Add(closeButton);

                layout.Controls.Add(titleLabel, 0, 0);
                layout.Controls.Add(subtitleLabel, 0, 1);
                layout.Controls.Add(versionLabel, 0, 2);
                layout.Controls.Add(signaturePanel, 0, 3);
                var spacer = new Panel { Dock = DockStyle.Fill };
                layout.Controls.Add(spacer, 0, 4);
                layout.Controls.Add(buttonsPanel, 0, 5);

                Controls.Add(layout);

                AcceptButton = closeButton;
                CancelButton = closeButton;
            }

            /// <summary>
            /// Crea signature button al volo.
            /// </summary>
            private Button CreateSignatureButton(string text)
            {
                var button = new Button
                {
                    Text = text,
                    AutoSize = false,
                    Width = 130,
                    Height = Sizes.ButtonHeight,
                    FlatStyle = FlatStyle.Flat,
                    Font = GetCachedFont(Fonts.Primary, Fonts.SizeSM, FontStyle.Bold),
                    ForeColor = Colors.PrimaryLight,
                    BackColor = Color.FromArgb(26, 26, 30),
                    Cursor = Cursors.Hand,
                    DialogResult = DialogResult.OK
                };
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 60);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 32, 36);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(22, 22, 26);
                button.Click += (s, e) => Close();
                return button;
            }
        }

        #endregion

        // WndProc rimosso - non necessario con asInvoker nel manifest

#if DEBUG
        /// <summary>
        /// Esegue la logica debug layout box senza cambiare il comportamento.
        /// </summary>
        private void DebugLayoutBox(string name, Control control)
        {
            if (control == null)
                return;

            Debug.WriteLine($"[Layout] {name}: Padding={FormatPadding(control.Padding)} | Margin={FormatPadding(control.Margin)} | Size={control.Width}x{control.Height}");
        }

        /// <summary>
        /// Logga bounds cosi capiamo cosa succede.
        /// </summary>
        private void LogBounds(string name, Control control)
        {
            if (control == null)
                return;

            Debug.WriteLine($"[Bounds] {name}: L={control.Left}, T={control.Top}, R={control.Right}, B={control.Bottom}");
            DebugLayoutBox(name, control);
        }

        /// <summary>
        /// Esegue la logica format padding senza cambiare il comportamento.
        /// </summary>
        private string FormatPadding(Padding padding)
        {
            return $"L{padding.Left},T{padding.Top},R{padding.Right},B{padding.Bottom}";
        }
#endif
    }
}
