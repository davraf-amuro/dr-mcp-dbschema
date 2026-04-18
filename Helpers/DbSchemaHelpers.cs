internal static class DbSchemaHelpers
{
    /// <summary>Estrae il nome dell'oggetto (tabella/vista) da uno statement SQL contenente TABLE &lt;nome&gt;.</summary>
    internal static string? ExtractObjectName(string sql)
    {
        // Pattern: TABLE [schema.]name
        // Gruppo 1 (opzionale): schema con o senza parentesi quadre → es. [dbo] o dbo
        // Gruppo 2: nome oggetto con o senza parentesi quadre → es. [Orders] o Orders
        var match = System.Text.RegularExpressions.Regex.Match(
            sql, @"\bTABLE\s+(?:\[?(\w+)\]?\.)?\[?(\w+)\]?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var schema = match.Groups[1].Value;
        var name = match.Groups[2].Value;
        return string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";
    }

    /// <summary>Analizza il rischio di uno statement ALTER TABLE e restituisce livello e descrizione.</summary>
    internal static (string Level, string Warnings) AnalyzeAlterRisk(string sql)
    {
        var upper = sql.ToUpperInvariant();
        var warnings = new List<string>();

        if (upper.Contains("DROP COLUMN"))
        {
            warnings.Add("- DROP COLUMN: rimozione colonna, i dati nella colonna saranno PERSI DEFINITIVAMENTE.");
        }

        if (upper.Contains("ALTER COLUMN"))
        {
            warnings.Add("- ALTER COLUMN: modifica tipo o vincoli, possibile perdita/troncamento dati esistenti.");
        }

        if (upper.Contains("DROP DEFAULT") || upper.Contains("DROP CONSTRAINT"))
        {
            warnings.Add("- DROP CONSTRAINT/DEFAULT: rimozione vincolo, possibile impatto su integrità referenziale.");
        }

        if (upper.Contains("ADD") && warnings.Count == 0)
        {
            warnings.Add("- ADD: operazione additiva, nessun dato esistente viene modificato.");
        }

        // DANGER se almeno un avviso implica perdita irreversibile di dati o violazione di integrità referenziale
        var level = warnings.Any(w => w.Contains("PERSI") || w.Contains("troncamento") || w.Contains("integrità"))
            ? "DANGER"
            : "WARN";

        return (level, warnings.Count > 0 ? string.Join("\n", warnings) : "- Nessuna operazione distruttiva rilevata.");
    }

    /// <summary>Maschera la password in una connection string per l'output diagnostico.</summary>
    internal static string MaskConnectionString(string cs) =>
        System.Text.RegularExpressions.Regex.Replace(
            cs,
            @"(?i)(password|pwd)\s*=\s*[^;]*",
            "$1=***");
}
