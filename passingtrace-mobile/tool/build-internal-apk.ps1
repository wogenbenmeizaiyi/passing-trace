param(
    [string]$IdentityUrl = "http://localhost:56229",
    [string]$EventsApiUrl = "http://localhost:54934",
    [string]$BuildName = "1.0.0-internal",
    [int]$BuildNumber = 1
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & flutter build apk --release `
        --flavor internal `
        --build-name $BuildName `
        --build-number $BuildNumber `
        --dart-define PASSINGTRACE_CHANNEL=internal `
        --dart-define "PASSINGTRACE_IDENTITY_URL=$IdentityUrl" `
        --dart-define "PASSINGTRACE_EVENTS_API_URL=$EventsApiUrl"
    if ($LASTEXITCODE -ne 0) { throw "Internal APK build failed." }

    $source = Join-Path $projectRoot "build/app/outputs/flutter-apk/app-internal-release.apk"
    $outputDirectory = Join-Path $projectRoot "build/releases"
    $output = Join-Path $outputDirectory "PassingTrace-$BuildName-$BuildNumber-internal.apk"
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $output -Force
    Write-Output $output
}
finally {
    Pop-Location
}
