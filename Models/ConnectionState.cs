/// <summary>Stato globale delle connection string disponibili e di quella attualmente selezionata.</summary>
public class ConnectionState
{
    /// <summary>Tutte le connection string trovate negli appsettings, indicizzate per nome.</summary>
    public Dictionary<string, string> Available { get; set; } = new();
    /// <summary>File appsettings sorgente per ogni CS (chiave = nome CS, valore = path assoluto del file).</summary>
    public Dictionary<string, string> AvailableSources { get; set; } = new();
    /// <summary>Nome della connection string attualmente attiva (null se nessuna selezionata).</summary>
    public string? ActiveName { get; set; }
    /// <summary>Valore della connection string attualmente attiva.</summary>
    public string? ActiveConnectionString { get; set; }
    /// <summary>Directory di lavoro del processo host (base per la risoluzione di tutti i path relativi).</summary>
    public string WorkDir { get; set; } = string.Empty;
    /// <summary>Radice di scansione degli appsettings (DB_SCHEMA_ROOT, src/ o WorkDir).</summary>
    public string SearchRoot { get; set; } = string.Empty;
    /// <summary>Lista dei file appsettings*.json trovati durante la scansione iniziale.</summary>
    public List<string> ScannedFiles { get; set; } = new();
}
