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

    /// <summary>
    /// Parole chiave vietate in una query read-only. La presenza di una qualsiasi (word-boundary,
    /// case-insensitive) rende lo statement non eseguibile da <c>RunSelect</c>.
    /// <c>INTO</c> blocca <c>SELECT ... INTO</c> (creazione tabella); <c>sp_</c>/<c>xp_</c> bloccano le stored procedure.
    /// </summary>
    private static readonly string[] _forbiddenSelectKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "CREATE", "ALTER",
        "TRUNCATE", "EXEC", "EXECUTE", "GRANT", "REVOKE", "DENY", "INTO",
        "BACKUP", "RESTORE", "sp_", "xp_"
    ];

    /// <summary>
    /// Verifica che lo statement sia una query di sola lettura (SELECT o CTE WITH ... SELECT),
    /// con un solo statement e senza parole chiave di scrittura/DDL/esecuzione.
    /// Difesa in profondità: whitelist del primo token + blacklist keyword + blocco multi-statement.
    /// </summary>
    /// <param name="sql">Statement SQL da verificare.</param>
    /// <param name="reason">Motivo del rifiuto se il metodo ritorna <c>false</c>; stringa vuota se valido.</param>
    /// <returns><c>true</c> se lo statement è una SELECT read-only sicura.</returns>
    internal static bool IsReadOnlySelect(string sql, out string reason)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            reason = "Statement vuoto.";
            return false;
        }

        // Rimuove i ';' finali (con eventuali spazi), poi verifica che non ne restino di interni
        var trimmed = sql.Trim().TrimEnd(';', ' ', '\t', '\r', '\n');
        if (trimmed.Contains(';'))
        {
            reason = "Statement multipli non consentiti: usa una sola query SELECT.";
            return false;
        }

        // Il primo token deve essere SELECT o WITH (CTE)
        var firstToken = System.Text.RegularExpressions.Regex.Match(trimmed, @"^\s*([A-Za-z_]+)");
        var keyword = firstToken.Success ? firstToken.Groups[1].Value.ToUpperInvariant() : string.Empty;
        if (keyword is not ("SELECT" or "WITH"))
        {
            reason = "Solo query SELECT (o CTE WITH ... SELECT) sono consentite in lettura.";
            return false;
        }

        foreach (var forbidden in _forbiddenSelectKeywords)
        {
            // sp_/xp_ sono prefissi: match come inizio identificatore; gli altri come parola intera
            var pattern = forbidden.EndsWith('_')
                ? $@"\b{forbidden}\w*"
                : $@"\b{System.Text.RegularExpressions.Regex.Escape(forbidden)}\b";

            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                reason = $"Parola chiave non consentita in una query read-only: '{forbidden}'.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Restituisce lo statement con ogni riga prefissata da <c>-- </c>, così da non poter essere
    /// eseguito immediatamente dal chiamante. Gestisce sia separatori <c>\r\n</c> sia <c>\n</c>.
    /// </summary>
    internal static string CommentOutSql(string sql)
    {
        var lines = (sql ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(line => $"-- {line}"));
    }

    /// <summary>Maschera la password in una connection string per l'output diagnostico.</summary>
    internal static string MaskConnectionString(string cs) =>
        System.Text.RegularExpressions.Regex.Replace(
            cs,
            @"(?i)(password|pwd)\s*=\s*[^;]*",
            "$1=***");
}
