<#
.SYNOPSIS
    Publica o Nordxcel Desktop self-contained, em executável único, para as
    plataformas do roadmap (commit 17 — empacotamento).

.DESCRIPTION
    Cada RID sai em publish/<rid>/ (a pasta já está no .gitignore). O .NET SDK
    não precisa do sistema operacional de destino para PUBLICAR — dá para
    gerar os quatro a partir deste Windows — só para RODAR o binário depois.

    Assinatura de código do macOS fica fora deste script de propósito: exige
    conta Apple Developer e rodar codesign/notarytool numa máquina Mac de
    verdade, nenhum dos dois disponível aqui. O binário osx-x64/osx-arm64
    sai sem assinatura — roda local via Terminal, mas o Gatekeeper bloqueia
    distribuição até alguém assinar num Mac.

.EXAMPLE
    .\scripts\publish.ps1
    Publica os quatro RIDs padrão em Release.

.EXAMPLE
    .\scripts\publish.ps1 -Runtimes win-x64
    Publica só Windows, para testar mais rápido.
#>

param(
    [string[]]$Runtimes = @('win-x64', 'osx-x64', 'osx-arm64', 'linux-x64'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Nordxcel.Desktop\Nordxcel.Desktop.csproj'

foreach ($rid in $Runtimes) {
    $output = Join-Path $root "publish\$rid"
    Write-Host "==> Publicando $rid em $output"

    dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -o $output

    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao publicar $rid (código $LASTEXITCODE)."
    }
}

Write-Host ""
Write-Host "Publicação concluída para: $($Runtimes -join ', ')"
