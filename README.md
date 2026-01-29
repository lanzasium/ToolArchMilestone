# Tool Arch Milestone

Tool moderno per l'archiviazione di dati sensibili e video da server Milestone.

## Requisiti

*   Windows 10 versione 1809 o successiva / Windows 11
*   .NET 9 SDK (o .NET 8/10 compatibile)
*   Visual Studio 2022 (con carico di lavoro "Sviluppo di app Windows") o VS Code.

## Struttura del Progetto

*   **ToolArchMilestone.Core**: Libreria di classi contenente la logica di business, i modelli, i servizi (Database, Log, Parsing) e i ViewModel.
*   **ToolArchMilestone**: Applicazione Desktop WinUI 3 (Interfaccia Utente).
*   **ToolArchMilestone.Tests**: Unit test per la logica Core.

## Configurazione Milestone

Attualmente il tool utilizza un `MockMilestoneService` che simula la connessione e l'esportazione per scopi di sviluppo e test senza le DLL proprietarie.

Per integrare il vero SDK Milestone:
1.  Ottenere le DLL `VideoOS.Platform.dll` e dipendenze dal Milestone MIP SDK.
2.  Implementare una nuova classe `RealMilestoneService` che eredita da `IMilestoneService`.
3.  Utilizzare i metodi dell'SDK (`VideoOS.Platform.SDK.Export`) all'interno di `ExportVideoAsync`.
4.  In `App.xaml.cs`, sostituire `new MockMilestoneService()` con `new RealMilestoneService()`.

## Come Eseguire

1.  Aprire la soluzione `ToolArchMilestone.sln` in Visual Studio.
2.  Assicurarsi che il progetto di avvio sia `ToolArchMilestone`.
3.  Selezionare la configurazione **x64** (non "Any CPU") dalla barra degli strumenti.
4.  Premere F5 o "Avvia".

## Funzionalità

*   **LANCIO**: Wizard per configurare nuove archiviazioni (Legale/Digitale, Server/Archivio). Supporta importazione multipla da TXT.
*   **IN CORSO**: Monitoraggio dei job attivi con barra di avanzamento, pausa e riordino code.
*   **TERMINATE**: Storico delle archiviazioni completate o fallite, con possibilità di eliminazione.

## Note Tecniche

*   Il database è SQLite locale (`toolarch.db` in `AppData`).
*   I Log sono salvati nel database.
