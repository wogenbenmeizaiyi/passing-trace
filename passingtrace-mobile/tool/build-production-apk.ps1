param(
    [string]$BuildName = "1.0.0",
    [int]$BuildNumber = 1
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & flutter build apk --release `
        --flavor production `
        --build-name $BuildName `
        --build-number $BuildNumber `
        --dart-define PASSINGTRACE_CHANNEL=production `
        --dart-define PASSINGTRACE_IDENTITY_URL=https://auth.passingtrace.com `
        --dart-define PASSINGTRACE_EVENTS_API_URL=https://passingtrace.com
    if ($LASTEXITCODE -ne 0) { throw "Production APK build failed." }

    $source = Join-Path $projectRoot "build/app/outputs/flutter-apk/app-production-release.apk"
    $outputDirectory = Join-Path $projectRoot "build/releases"
    $output = Join-Path $outputDirectory "PassingTrace-$BuildName-$BuildNumber-production.apk"
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $output -Force
    Write-Output $output
}
finally {
    Pop-Location
}
