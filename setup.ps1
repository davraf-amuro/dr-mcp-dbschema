#Requires -Version 5.1
<#
.SYNOPSIS
    Scarica, installa o aggiorna dr-mcp-dbschema nel progetto corrente.

.DESCRIPTION
    Scarica il binario win-x64 self-contained dall'ultima GitHub Release, verifica
    il checksum SHA256, lo estrae in tools/dr-mcp-dbschema/ e aggiorna .mcp.json.

.PARAMETER Version
    Versione specifica da scaricare (es. "v1.0.0"). Default: ultima disponibile.

.PARAMETER ToolsDir
    Directory di destinazione. Default: tools/dr-mcp-dbschema relativa alla CWD.

.PARAMETER SkipMcpJson
    Se specificato, non modifica .mcp.json.

.EXAMPLE
    # Ultima versione
    irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1 | iex

.EXAMPLE
    # Versione specifica
    Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Version v1.2.0"
#>
param(
    [string] $Version      = "",
    [string] $ToolsDir     = "",
    [switch] $SkipMcpJson
)

$ErrorActionPreference = "Stop"
$Repo    = "davraf-amuro/dr-mcp-dbschema"
$ExeName = "dr-mcp-dbschema.exe"

# Risolvi directory destinazione
if (-not $ToolsDir) {
    $ToolsDir = Join-Path (Get-Location) "tools\dr-mcp-dbschema"
}

# Recupera release metadata
if ($Version) {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/tags/$Version"
} else {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
}

Write-Host "Recupero release da GitHub..."
$release = Invoke-RestMethod $apiUrl -Headers @{ "User-Agent" = "setup.ps1" }
$tag     = $release.tag_name

# Rileva versione installata (se esiste)
$exePath         = Join-Path $ToolsDir $ExeName
$isUpdate        = Test-Path $exePath
$previousVersion = $null
if ($isUpdate) {
    try {
        $previousVersion = & $exePath --version 2>$null
    } catch { }
}

if ($isUpdate) {
    Write-Host "Aggiornamento: $previousVersion → $tag"
} else {
    Write-Host "Installazione: $tag"
}

# Asset zip
$asset = $release.assets | Where-Object { $_.name -like "*win-x64*.zip" } | Select-Object -First 1
if (-not $asset) {
    Write-Error "Nessun asset win-x64 trovato nella release $tag."
    exit 1
}

# Checksum SHA256 (opzionale ma verificato se disponibile)
$checksumAsset = $release.assets | Where-Object { $_.name -eq "checksums.sha256" } | Select-Object -First 1

# Scarica zip
$zipPath = Join-Path $env:TEMP "dr-mcp-dbschema-$tag.zip"
Write-Host "Scarico $($asset.name)..."
Invoke-WebRequest $asset.browser_download_url -OutFile $zipPath

# Verifica SHA256 se il file checksum è disponibile nella release
if ($checksumAsset) {
    $checksumFile = Join-Path $env:TEMP "dr-mcp-dbschema-$tag.sha256"
    Invoke-WebRequest $checksumAsset.browser_download_url -OutFile $checksumFile

    $expectedHash = (Get-Content $checksumFile | Where-Object { $_ -like "*win-x64*" }) -split '\s+' | Select-Object -First 1
    if ($expectedHash) {
        $actualHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
        if ($actualHash -ne $expectedHash.ToLower()) {
            Remove-Item $zipPath -Force
            Write-Error "Checksum SHA256 non valido. Download corrotto o compromesso. Interrotto."
            exit 1
        }
        Write-Host "  SHA256 verificato." -ForegroundColor Green
    }
    Remove-Item $checksumFile -Force
} else {
    Write-Host "  (checksums.sha256 non disponibile in questa release — verifica saltata)" -ForegroundColor Yellow
}

# Estrai
New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $ToolsDir -Force
Remove-Item $zipPath

if (-not (Test-Path $exePath)) {
    Write-Error "Installazione fallita: $exePath non trovato dopo l'estrazione."
    exit 1
}

# Risultato
Write-Host ""
if ($isUpdate) {
    Write-Host "[OK] dr-mcp-dbschema aggiornato a $tag" -ForegroundColor Green
} else {
    Write-Host "[OK] dr-mcp-dbschema $tag installato" -ForegroundColor Green
}
Write-Host "     $exePath"

# Patch .mcp.json
if (-not $SkipMcpJson) {
    $mcpJsonPath = Join-Path (Get-Location) ".mcp.json"
    $relExePath  = "tools/dr-mcp-dbschema/$ExeName"
    $serverEntry = @{
        type    = "stdio"
        command = $relExePath
    }

    if (Test-Path $mcpJsonPath) {
        try {
            $mcpConfig = Get-Content $mcpJsonPath -Raw | ConvertFrom-Json
            if (-not $mcpConfig.mcpServers) {
                $mcpConfig | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value ([PSCustomObject]@{})
            }
            $mcpConfig.mcpServers | Add-Member -MemberType NoteProperty -Name "db-schema" -Value ([PSCustomObject]$serverEntry) -Force
            $mcpConfig | ConvertTo-Json -Depth 10 | Set-Content $mcpJsonPath -Encoding UTF8
            Write-Host ""
            Write-Host ".mcp.json aggiornato con la voce db-schema." -ForegroundColor Cyan
        } catch {
            Write-Host ""
            Write-Host ".mcp.json esistente non modificabile automaticamente. Aggiungi manualmente:" -ForegroundColor Yellow
            Write-Host @"
  "db-schema": {
    "type": "stdio",
    "command": "$relExePath"
  }
"@
        }
    } else {
        $newConfig = @{
            mcpServers = @{
                "db-schema" = $serverEntry
            }
        }
        $newConfig | ConvertTo-Json -Depth 10 | Set-Content $mcpJsonPath -Encoding UTF8
        Write-Host ""
        Write-Host ".mcp.json creato con la voce db-schema." -ForegroundColor Cyan
        Write-Host "  ATTENZIONE: .mcp.json non va committato (aggiungilo a .gitignore)." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Riavvia Claude Code per caricare il server MCP." -ForegroundColor Cyan
