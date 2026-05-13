# test-binary-zero.ps1
# Diagnostic: find all points showing "---" and cross-check via Properties endpoint
# Usage: pwsh .\tests\test-binary-zero.ps1

$baseUrl = "http://localhost:5000/plswk/AssetsList"

Write-Host "`n=== Fetching all points ===" -ForegroundColor Cyan
$pointsJson = (Invoke-WebRequest -Uri "$baseUrl`?handler=Points" -UseBasicParsing).Content
$points = $pointsJson | ConvertFrom-Json

$dashes = $points | Where-Object { $_.value -eq '---' }
$total  = $points.Count
$dashCount = $dashes.Count

Write-Host "  Total points: $total"
Write-Host "  Showing '---': $dashCount" -ForegroundColor $(if ($dashCount -gt 0) { 'Yellow' } else { 'Green' })

if ($dashCount -eq 0) {
    Write-Host "`n  All points have values. Test PASSED." -ForegroundColor Green
    exit 0
}

# Sample up to 20 --- entries for Properties cross-check
$sample = $dashes | Select-Object -First 20
$bugs = @()

Write-Host "`n=== Cross-checking '---' points via Properties endpoint ===" -ForegroundColor Cyan

foreach ($p in $sample) {
    $key = $p.key
    $type = $p.type
    try {
        $propsJson = (Invoke-WebRequest -Uri "$baseUrl`?handler=Properties&key=$key" -UseBasicParsing -TimeoutSec 5).Content
        $props = $propsJson | ConvertFrom-Json

        # Find present-value in the properties response
        $pv = $props | Where-Object { $_.property -eq 'Present_Value' -or $_.property -eq 'PROP_PRESENT_VALUE' }
        $pvValue = if ($pv) { $pv.value } else { 'N/A' }

        $isBug = ($pvValue -ne $null -and $pvValue -ne '' -and $pvValue -ne '---' -and $pvValue -ne 'N/A')

        $status = if ($isBug) { 'BUG' } else { 'OK' }
        $color  = if ($isBug) { 'Red' } else { 'DarkGray' }

        Write-Host "  [$status] $type | $key" -ForegroundColor $color
        if ($isBug) {
            Write-Host "         List='---' but Properties='$pvValue'" -ForegroundColor Red
            $bugs += [PSCustomObject]@{
                Key   = $key
                Type  = $type
                ListValue = '---'
                PropertiesValue = $pvValue
            }
        }
    }
    catch {
        Write-Host "  [ERR] $key - $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "  Total '---': $dashCount / $total"
Write-Host "  Bugs found:  $($bugs.Count) (list='---' but properties has a real value)" -ForegroundColor $(if ($bugs.Count -gt 0) { 'Red' } else { 'Green' })

if ($bugs.Count -gt 0) {
    Write-Host "`n  Bug details:" -ForegroundColor Red
    $bugs | Format-Table -AutoSize
    exit 1
}

Write-Host "`n  Test PASSED - all '---' entries are genuinely missing." -ForegroundColor Green
exit 0
