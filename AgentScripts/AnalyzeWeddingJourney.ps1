param([string[]]$Labels = @('01-school','02-library','03-home','06-hospital','07-boss-arrival','boss-assisted-combat','market-no-alarm','market-dedup-valid'))
$ErrorActionPreference = 'Stop'
foreach ($label in $Labels) {
    $prefix = Join-Path 'Logs/WeddingJourney' $label
    if (!(Test-Path "$prefix.data")) { continue }
    & unity command diagnostics profiler_session --caller plugin --skill unity-pipeline --capture "$prefix.data" --confirm true --format json > "$prefix-session.json"
    if ($LASTEXITCODE -ne 0) { throw "Cannot open $label" }
    $session = Get-Content "$prefix-session.json" -Raw | ConvertFrom-Json
    & unity command diagnostics profiler_summary --caller plugin --skill unity-pipeline --session $session.sessionId --format json > "$prefix-summary.json"
    if ($LASTEXITCODE -ne 0) { throw "Cannot summarize $label" }
    & unity command diagnostics profiler_frame --caller plugin --skill unity-pipeline --session $session.sessionId --limit 8 --format json > "$prefix-worst.json"
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect $label" }
    $summary = Get-Content "$prefix-summary.json" -Raw | ConvertFrom-Json
    $worst = Get-Content "$prefix-worst.json" -Raw | ConvertFrom-Json
    [pscustomobject]@{label=$label; frames=$summary.analyzedRange.count; medianMs=$summary.cpuFrameMs.median; p95Ms=$summary.cpuFrameMs.p95; maxMs=$summary.cpuFrameMs.max; hot=($worst.hotPaths.steps | Select-Object -Last 4 | Select-Object name,totalMs)} | ConvertTo-Json -Depth 5 -Compress
}
