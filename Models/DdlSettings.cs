/// <summary>
/// Permessi per le operazioni DDL distruttive.
/// Tutti disabilitati per default: abilitare solo negli ambienti in cui è esplicitamente necessario
/// (es. development, staging). Non abilitare in produzione salvo casi controllati.
/// </summary>
public class DdlSettings
{
    /// <summary>
    /// Abilita il tool read-only <c>RunSelect</c> (esecuzione di query SELECT). Default: false.
    /// Pur essendo di sola lettura, espone i dati: abilitare solo dove necessario.
    /// </summary>
    public bool AllowSelect { get; set; } = false;
    /// <summary>Abilita <c>PreviewCreate</c> / <c>ExecuteCreate</c>. Default: false.</summary>
    public bool AllowCreate { get; set; } = false;
    /// <summary>Abilita <c>PreviewAlter</c> / <c>ExecuteAlter</c>. Default: false.</summary>
    public bool AllowAlter { get; set; } = false;
    /// <summary>Abilita <c>PreviewDrop</c> / <c>ExecuteDrop</c>. Default: false.</summary>
    public bool AllowDrop { get; set; } = false;
}
