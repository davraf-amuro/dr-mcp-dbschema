#Requires -Version 5.1
<#
.SYNOPSIS
    Scarica l'ultima release di dr-mcp-dbschema e la installa nella cartella tools/.

.DESCRIPTION
    Scarica il binario win-x64 self-contained dall'ultima GitHub Release e lo
    posiziona in tools/dr-mcp-dbschema/ relativa alla directory corrente.
    Crea anche lo snippet .mcp.json se non esiste ancora.

.PARAMETER Version
    Versione specifica da scaricare (es. "v1.0.0"). Default: ultima disponibile.

.PARAMETER ToolsDir
    Directory di destinazione. Default: tools/dr-mcp-dbschema relativa alla CWD.

.EXAMPLE
    # Ultima versione
    irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1 | iex

.EXAMPLE
    # Versione specifica
    & .\setup.ps1 -Version v1.2.0
#>
param(
    [string] $Version   = "",
    [string] $ToolsDir  = ""
)

$ErrorActionPreference = "Stop"
$Repo    = "davraf-amuro/dr-mcp-dbschema"
$ExeName = "dr-mcp-dbschema.exe"

# Risolvi directory destinazione
if (-not $ToolsDir) {
    $ToolsDir = Join-Path (Get-Location) "tools\dr-mcp-dbschema"
}

# Recupera release
if ($Version) {
    $apiUrl  = "https://api.github.com/repos/$Repo/releases/tags/$Version"
} else {
    $apiUrl  = "https://api.github.com/repos/$Repo/releases/latest"
}

Write-Host "Recupero release da $apiUrl..."
$release = Invoke-RestMethod $apiUrl -Headers @{ "User-Agent" = "setup.ps1" }
$tag     = $release.tag_name

$asset = $release.assets | Where-Object { $_.name -like "*win-x64*.zip" } | Select-Object -First 1
if (-not $asset) {
    Write-Error "Nessun asset win-x64 trovato nella release $tag."
    exit 1
}

# Scarica e decomprimi
$zipPath = Join-Path $env:TEMP "dr-mcp-dbschema-$tag.zip"
Write-Host "Scarico $($asset.name) ($tag)..."
Invoke-WebRequest $asset.browser_download_url -OutFile $zipPath

New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $ToolsDir -Force
Remove-Item $zipPath

$exePath = Join-Path $ToolsDir $ExeName
if (-not (Test-Path $exePath)) {
    Write-Error "Installazione fallita: $exePath non trovato dopo l'estrazione."
    exit 1
}

Write-Host ""
Write-Host "[OK] dr-mcp-dbschema $tag installato in:" -ForegroundColor Green
Write-Host "     $exePath"

# Mostra snippet .mcp.json
$relPath = "tools/dr-mcp-dbschema/$ExeName"
Write-Host ""
Write-Host "Aggiungi al file .mcp.json del progetto:" -ForegroundColor Cyan
Write-Host @"
{
  "mcpServers": {
    "db-schema": {
      "type": "stdio",
      "command": "$relPath"
    }
  }
}
"@
