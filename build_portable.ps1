# build_portable.ps1
param(
    [string]$OutputDir = "./WelingtonII_Portable"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " BUILD PORTÁTIL - WELINGTON II" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# 1. Limpa output anterior
Write-Host "[1/6] Limpando builds anteriores..." -ForegroundColor Yellow
Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "./publish" -Recurse -Force -ErrorAction SilentlyContinue

# 2. Publica o projeto como pasta (não single-file)
Write-Host "[2/6] Publicando executável..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -o "./publish"

# 3. Cria a estrutura de pastas
Write-Host "[3/6] Criando estrutura de pastas..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
New-Item -ItemType Directory -Path "$OutputDir\Navegadores" -Force | Out-Null

# 4. Copia todos os arquivos da publicação
Write-Host "[4/6] Copiando arquivos do sistema..." -ForegroundColor Yellow
Copy-Item -Path "./publish/*" -Destination "$OutputDir\" -Recurse -Force

# 5. Copia o Chromium funcional
Write-Host "[5/6] Copiando Chromium (isso pode levar alguns minutos)..." -ForegroundColor Yellow
$chromiumSource = "C:\Users\meyri\AppData\Local\ms-playwright\chromium-1194"
$chromiumDest = "$OutputDir\Navegadores\chromium-1194"

if (Test-Path $chromiumSource) {
    Copy-Item -Path $chromiumSource -Destination $chromiumDest -Recurse -Force
    Write-Host "   ✅ Chromium copiado!" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Chromium não encontrado!" -ForegroundColor Red
}

# 6. Cria script de inicialização
Write-Host "[6/6] Criando scripts de inicialização..." -ForegroundColor Yellow

# Script de inicialização (iniciar.bat)
$batContent = @'
@echo off
title Welington II - Automação PNCP
color 0A
echo ========================================
echo  WELINGTON II - AUTOMACAO PNCP
echo ========================================
echo.

echo [OK] Iniciando sistema...
echo.

:: Executa o programa diretamente
"%~dp0Welington II.exe"

echo.
echo Sistema finalizado.
pause
'@

$batContent | Out-File -FilePath "$OutputDir\iniciar.bat" -Encoding ASCII

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " BUILD CONCLUÍDO!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "📁 Pasta gerada: $OutputDir" -ForegroundColor White

$totalSize = (Get-ChildItem -Path $OutputDir -Recurse | Measure-Object -Property Length -Sum).Sum
Write-Host "📦 Tamanho total: $([math]::Round($totalSize / 1MB, 2)) MB" -ForegroundColor Yellow

Write-Host "`n✅ Para distribuir, envie a pasta COMPLETA '$OutputDir'" -ForegroundColor Green