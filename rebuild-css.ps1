# Rebuild Pulswerk CSS using Tailwind CLI
# This script is meant to be run manually and is not part of the regular build process.

$DashboardDir = "src/Pulswerk.Dashboard"
$InputCss = "$DashboardDir/wwwroot/css/input.css"
$OutputCss = "$DashboardDir/wwwroot/css/pulswerk.css"
$Config = "$DashboardDir/tailwind.config.js"

Write-Host "Rebuilding Pulswerk CSS..." -ForegroundColor Cyan

# Use npm run to execute the build script defined in package.json
Push-Location $DashboardDir
npm run build:css
Pop-Location

if ($LASTEXITCODE -eq 0) {
    Write-Host "Successfully rebuilt $OutputCss" -ForegroundColor Green
} else {
    Write-Host "Failed to rebuild CSS. Make sure Node.js/npm is installed." -ForegroundColor Red
}
