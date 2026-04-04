#Requires -Version 5.1
<#
.SYNOPSIS
    Scarica, installa o aggiorna dr-mcp-dbschema nel progetto corrente.

.DESCRIPTION
    Scarica il binario win-x64 self-contained dall'ultima GitHub Release, verifica
    il checksum SHA256, lo estrae in tools/dr-mcp-dbschema/ e aggiorna il file di
    configurazione MCP del client specificato.

.PARAMETER Version
    Versione specifica da scaricare (es. "v1.0.0"). Default: ultima disponibile.

.PARAMETER ToolsDir
    Directory di destinazione. Default: tools/dr-mcp-dbschema relativa alla CWD.

.PARAMETER Client
    Client MCP da configurare: claude (default), vscode, cursor, all.
    - claude : aggiorna .mcp.json          (Claude Code / Claude Desktop)
    - vscode  : aggiorna .vscode/mcp.json  (VS Code con GitHub Copilot >= 1.99)
    - cursor  : aggiorna .cursor/mcp.json  (Cursor)
    - all     : aggiorna tutti i file sopra

.PARAMETER SkipConfig
    Se specificato, non modifica alcun file di configurazione.

.EXAMPLE
    irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1 | iex

.EXAMPLE
    Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Client vscode"

.EXAMPLE
    Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Client all"

.EXAMPLE
    Invoke-Expression "& { $(irm https://raw.githubusercontent.com/davraf-amuro/dr-mcp-dbschema/main/setup.ps1) } -Version v1.2.0"
#>
param(
    [string] $Version    = "",
    [string] $ToolsDir   = "",
    [ValidateSet("claude", "vscode", "cursor", "all")]
    [string] $Client     = "claude",
    [switch] $SkipConfig
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
    try { $previousVersion = & $exePath --version 2>$null } catch { }
}

if ($isUpdate) {
    Write-Host "Aggiornamento: $previousVersion -> $tag"
} else {
    Write-Host "Installazione: $tag"
}

# Asset zip
$asset = $release.assets | Where-Object { $_.name -like "*win-x64*.zip" } | Select-Object -First 1
if (-not $asset) {
    Write-Error "Nessun asset win-x64 trovato nella release $tag."
    exit 1
}

# Checksum SHA256 (verificato se disponibile nella release)
$checksumAsset = $release.assets | Where-Object { $_.name -eq "checksums.sha256" } | Select-Object -First 1

# Scarica zip
$zipPath = Join-Path $env:TEMP "dr-mcp-dbschema-$tag.zip"
Write-Host "Scarico $($asset.name)..."
Invoke-WebRequest $asset.browser_download_url -OutFile $zipPath

# Verifica SHA256
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
    Write-Host "  (checksums.sha256 non disponibile - verifica saltata)" -ForegroundColor Yellow
}

# Estrai
New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $ToolsDir -Force
Remove-Item $zipPath

if (-not (Test-Path $exePath)) {
    Write-Error "Installazione fallita: exe non trovato dopo l'estrazione."
    exit 1
}

Write-Host ""
if ($isUpdate) {
    Write-Host "[OK] dr-mcp-dbschema aggiornato a $tag" -ForegroundColor Green
} else {
    Write-Host "[OK] dr-mcp-dbschema $tag installato" -ForegroundColor Green
}
Write-Host "     $exePath"

# ─── Funzioni di configurazione ──────────────────────────────────────────────

function Write-JsonConfig($path, $config) {
    $config | ConvertTo-Json -Depth 10 | Set-Content $path -Encoding UTF8
}

function Set-ClaudeConfig {
    $cfgPath    = Join-Path (Get-Location) ".mcp.json"
    $relExePath = "tools/dr-mcp-dbschema/$ExeName"
    $entry      = [PSCustomObject]@{ type = "stdio"; command = $relExePath }

    if (Test-Path $cfgPath) {
        try {
            $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
            if (-not $cfg.mcpServers) {
                $cfg | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value ([PSCustomObject]@{})
            }
            $cfg.mcpServers | Add-Member -MemberType NoteProperty -Name "db-schema" -Value $entry -Force
            Write-JsonConfig $cfgPath $cfg
            Write-Host "  .mcp.json aggiornato." -ForegroundColor Cyan
        } catch {
            Write-Host "  .mcp.json esiste ma non e modificabile. Aggiungi manualmente la voce db-schema." -ForegroundColor Yellow
        }
    } else {
        Write-JsonConfig $cfgPath @{ mcpServers = @{ "db-schema" = $entry } }
        Write-Host "  .mcp.json creato." -ForegroundColor Cyan
        Write-Host "  ATTENZIONE: aggiungi .mcp.json al .gitignore." -ForegroundColor Yellow
    }
}

function Set-VsCodeConfig {
    $dir        = Join-Path (Get-Location) ".vscode"
    $cfgPath    = Join-Path $dir "mcp.json"
    # VS Code risolve ${workspaceFolder} a runtime — path portabile tra macchine
    $relExePath = '${workspaceFolder}/tools/dr-mcp-dbschema/' + $ExeName
    $entry      = [PSCustomObject]@{ type = "stdio"; command = $relExePath }

    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    if (Test-Path $cfgPath) {
        try {
            $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
            if (-not $cfg.servers) {
                $cfg | Add-Member -MemberType NoteProperty -Name "servers" -Value ([PSCustomObject]@{})
            }
            $cfg.servers | Add-Member -MemberType NoteProperty -Name "db-schema" -Value $entry -Force
            Write-JsonConfig $cfgPath $cfg
            Write-Host "  .vscode/mcp.json aggiornato." -ForegroundColor Cyan
        } catch {
            Write-Host "  .vscode/mcp.json esiste ma non e modificabile. Aggiungi manualmente la voce db-schema." -ForegroundColor Yellow
        }
    } else {
        Write-JsonConfig $cfgPath @{ servers = @{ "db-schema" = $entry } }
        Write-Host "  .vscode/mcp.json creato (VS Code >= 1.99 con GitHub Copilot)." -ForegroundColor Cyan
    }
}

function Set-CursorConfig {
    $dir        = Join-Path (Get-Location) ".cursor"
    $cfgPath    = Join-Path $dir "mcp.json"
    $relExePath = "tools/dr-mcp-dbschema/$ExeName"
    $entry      = [PSCustomObject]@{ type = "stdio"; command = $relExePath }

    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    if (Test-Path $cfgPath) {
        try {
            $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
            if (-not $cfg.mcpServers) {
                $cfg | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value ([PSCustomObject]@{})
            }
            $cfg.mcpServers | Add-Member -MemberType NoteProperty -Name "db-schema" -Value $entry -Force
            Write-JsonConfig $cfgPath $cfg
            Write-Host "  .cursor/mcp.json aggiornato." -ForegroundColor Cyan
        } catch {
            Write-Host "  .cursor/mcp.json esiste ma non e modificabile. Aggiungi manualmente la voce db-schema." -ForegroundColor Yellow
        }
    } else {
        Write-JsonConfig $cfgPath @{ mcpServers = @{ "db-schema" = $entry } }
        Write-Host "  .cursor/mcp.json creato." -ForegroundColor Cyan
    }
}

# ─── Esecuzione ──────────────────────────────────────────────────────────────

if (-not $SkipConfig) {
    Write-Host ""
    Write-Host "Configurazione client MCP ($Client):" -ForegroundColor White
    switch ($Client) {
        "claude" { Set-ClaudeConfig }
        "vscode" { Set-VsCodeConfig }
        "cursor" { Set-CursorConfig }
        "all"    { Set-ClaudeConfig; Set-VsCodeConfig; Set-CursorConfig }
    }
}

Write-Host ""
Write-Host "Riavvia il tuo IDE per caricare il server MCP." -ForegroundColor Cyan
