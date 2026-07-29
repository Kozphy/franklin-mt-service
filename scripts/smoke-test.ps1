param(
    [string]$BaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"

$live = Invoke-RestMethod -Uri "$BaseUrl/health/live"
if ($live.status -ne "healthy") {
    throw "Liveness check failed."
}

$body = @{
    text = "Hello world"
    sourceLanguage = "en"
    targetLanguage = "es"
} | ConvertTo-Json

$first = Invoke-RestMethod `
    -Method Post `
    -Uri "$BaseUrl/api/v1/translations" `
    -ContentType "application/json" `
    -Headers @{ "X-Correlation-ID" = "smoke-test" } `
    -Body $body

if ($first.translatedText -ne "Hola mundo" -or $first.cached) {
    throw "Initial translation response was not correct."
}

$second = Invoke-RestMethod `
    -Method Post `
    -Uri "$BaseUrl/api/v1/translations" `
    -ContentType "application/json" `
    -Body $body

if (-not $second.cached -or $second.id -ne $first.id) {
    throw "Cache behavior was not correct."
}

$retrieved = Invoke-RestMethod `
    -Uri "$BaseUrl/api/v1/translations/$($first.id)"

if ($retrieved.translatedText -ne "Hola mundo") {
    throw "Retrieval response was not correct."
}

Write-Output "Smoke test passed for translation $($first.id)."
