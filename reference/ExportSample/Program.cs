using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ExportSample
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			VideoOS.Platform.SDK.Environment.Initialize();			// General initialize.  Always required
			VideoOS.Platform.SDK.UI.Environment.Initialize();		// Initialize ActiveX references, e.g. usage of ImageViewerActiveX etc
			VideoOS.Platform.SDK.Export.Environment.Initialize();	// Initialize export references
            
			// Avvia MainForm direttamente
			// La connessione verrà verificata/effettuata dall'interfaccia utente
			Application.Run(new MainForm());
		}
	}
}
