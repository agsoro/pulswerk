# tools/agent-check.ps1
# Automated Codebase Health Check & Diagnostic Tool for AI Agents and Developers

$ErrorActionPreference = "SilentlyContinue"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Agent Codebase Diagnostic Tool" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$reportPath = "agent-health-report.md"
$startTime = Get-Date

$results = @{
    "Dotnet Build" = "Skipped"
    "Dotnet Tests" = "Skipped"
    "Frontend Build" = "Skipped"
    "Frontend Unit Tests" = "Skipped"
    "Server Reachable" = "Skipped"
    "Playwright E2E Tests" = "Skipped"
}

$details = @{
    "Dotnet Build" = ""
    "Dotnet Tests" = ""
    "Frontend Build" = ""
    "Frontend Unit Tests" = ""
    "Server Reachable" = ""
    "Playwright E2E Tests" = ""
}

# --- Phase 1: Dotnet Build ---
Write-Host "[1/6] Running dotnet build..." -ForegroundColor Yellow
$buildOutput = dotnet build 2>&1
if ($LASTEXITCODE -eq 0) {
    $results["Dotnet Build"] = "Passed"
    Write-Host "-> Success!" -ForegroundColor Green
} else {
    $results["Dotnet Build"] = "Failed"
    $details["Dotnet Build"] = $buildOutput | Out-String
    Write-Host "-> Failed!" -ForegroundColor Red
}

# --- Phase 2: Dotnet Tests ---
Write-Host "[2/6] Running backend tests (dotnet test)..." -ForegroundColor Yellow
$testOutput = dotnet test 2>&1
if ($LASTEXITCODE -eq 0) {
    $results["Dotnet Tests"] = "Passed"
    Write-Host "-> Success!" -ForegroundColor Green
} else {
    $results["Dotnet Tests"] = "Failed"
    $details["Dotnet Tests"] = $testOutput | Out-String
    Write-Host "-> Failed!" -ForegroundColor Red
}

# --- Phase 3: Frontend Build ---
Write-Host "[3/6] Running frontend build (npm run build:js)..." -ForegroundColor Yellow
Push-Location "src/Pulswerk.Dashboard"
$feBuildOutput = npm run build:js 2>&1
if ($LASTEXITCODE -eq 0) {
    $results["Frontend Build"] = "Passed"
    Write-Host "-> Success!" -ForegroundColor Green
} else {
    $results["Frontend Build"] = "Failed"
    $details["Frontend Build"] = $feBuildOutput | Out-String
    Write-Host "-> Failed!" -ForegroundColor Red
}

# --- Phase 4: Frontend Unit Tests ---
Write-Host "[4/6] Running Vitest (npm run test)..." -ForegroundColor Yellow
$feTestOutput = npm run test 2>&1
if ($LASTEXITCODE -eq 0) {
    $results["Frontend Unit Tests"] = "Passed"
    Write-Host "-> Success!" -ForegroundColor Green
} else {
    $results["Frontend Unit Tests"] = "Failed"
    $details["Frontend Unit Tests"] = $feTestOutput | Out-String
    Write-Host "-> Failed!" -ForegroundColor Red
}

# --- Phase 5: Server Reachable Check ---
Write-Host "[5/6] Checking local server status on port 5000..." -ForegroundColor Yellow
$tcpClient = New-Object System.Net.Sockets.TcpClient
$connect = $tcpClient.BeginConnect("localhost", 5000, $null, $null)
$success = $connect.AsyncWaitHandle.WaitOne(2000, $true)
if ($success -and $tcpClient.Connected) {
    $results["Server Reachable"] = "Yes (Port 5000 Active)"
    Write-Host "-> Server is up!" -ForegroundColor Green
    $tcpClient.Close()
} else {
    $results["Server Reachable"] = "No (Port 5000 Offline)"
    $details["Server Reachable"] = "Local server is not running on port 5000. Start it manually via dotnet or host to run E2E."
    Write-Host "-> Server is offline!" -ForegroundColor Red
}

# --- Phase 6: Playwright E2E Tests ---
if ($results["Server Reachable"] -like "Yes*") {
    Write-Host "[6/6] Running Playwright E2E tests (npm run test:e2e)..." -ForegroundColor Yellow
    $e2eOutput = npm run test:e2e 2>&1
    if ($LASTEXITCODE -eq 0) {
        $results["Playwright E2E Tests"] = "Passed"
        Write-Host "-> Success!" -ForegroundColor Green
    } else {
        $results["Playwright E2E Tests"] = "Failed"
        $details["Playwright E2E Tests"] = $e2eOutput | Out-String
        Write-Host "-> Failed!" -ForegroundColor Red
    }
} else {
    $results["Playwright E2E Tests"] = "Skipped (Server Offline)"
    Write-Host "-> Skipped (requires running server)" -ForegroundColor Gray
}
Pop-Location

# --- Build Markdown Report ---
$endTime = Get-Date
$duration = $endTime - $startTime
$durationStr = "{0:N2}s" -f $duration.TotalSeconds

if (Test-Path $reportPath) { Remove-Item $reportPath -Force }

Add-Content -Path $reportPath -Value "# Codebase Health & Diagnostic Report"
Add-Content -Path $reportPath -Value "Generated: $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))"
Add-Content -Path $reportPath -Value "Duration: $durationStr"
Add-Content -Path $reportPath -Value ""
Add-Content -Path $reportPath -Value "## Summary"
Add-Content -Path $reportPath -Value ""
Add-Content -Path $reportPath -Value "| Phase | Status |"
Add-Content -Path $reportPath -Value "| :--- | :--- |"
Add-Content -Path $reportPath -Value "| **Dotnet Build** | $($results['Dotnet Build']) |"
Add-Content -Path $reportPath -Value "| **Dotnet Tests** | $($results['Dotnet Tests']) |"
Add-Content -Path $reportPath -Value "| **Frontend Build** | $($results['Frontend Build']) |"
Add-Content -Path $reportPath -Value "| **Frontend Unit Tests** | $($results['Frontend Unit Tests']) |"
Add-Content -Path $reportPath -Value "| **Server Connection** | $($results['Server Reachable']) |"
Add-Content -Path $reportPath -Value "| **Playwright E2E Tests** | $($results['Playwright E2E Tests']) |"

foreach ($key in $results.Keys) {
    if ($details[$key] -ne "") {
        Add-Content -Path $reportPath -Value ""
        Add-Content -Path $reportPath -Value "### Details for: $key"
        Add-Content -Path $reportPath -Value "````text"
        Add-Content -Path $reportPath -Value $details[$key]
        Add-Content -Path $reportPath -Value "````"
    }
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Report written to: $reportPath" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
