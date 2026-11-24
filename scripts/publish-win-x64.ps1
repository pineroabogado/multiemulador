$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$out = Join-Path $root 'publish\win-x64'
dotnet publish src/MultiEmulator.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $out

# Espejo durable: metadatos + soluciones de carga + shards SC (sc-games-prefix-*.json).
# Copia a catalog/ (UI) y a assets/catalog/ (LoadSolutionsStore / ProjectCatalogPaths).
$catalogSrc = Join-Path $root 'assets\catalog'
foreach ($catalogDst in @(
    (Join-Path $out 'catalog'),
    (Join-Path $out 'assets\catalog')
  )) {
  if (Test-Path $catalogSrc) {
    New-Item -ItemType Directory -Force -Path $catalogDst | Out-Null
    Copy-Item -Path (Join-Path $catalogSrc '*') -Destination $catalogDst -Recurse -Force
  }
}

Write-Host "Publicado en $out"

