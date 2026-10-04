[CmdletBinding()]
param([switch]$SkipOcrModels)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK is required. Install Visual Studio with the ASP.NET workload or the .NET 10 SDK.' }
$sdkVersion = [version]((dotnet --version) -replace '-.*$','')
if ($sdkVersion.Major -lt 10) { throw ".NET 10 SDK is required; found $sdkVersion." }
$tessData = Join-Path $repoRoot 'tools\tessdata'
New-Item -ItemType Directory -Force -Path $tessData | Out-Null
if (-not $SkipOcrModels) {
    foreach ($model in @('eng','ind')) {
        $target = Join-Path $tessData "$model.traineddata"
        if (-not (Test-Path $target)) {
            Write-Host "Downloading Apache-2.0 Tesseract model: $model"
            Invoke-WebRequest "https://github.com/tesseract-ocr/tessdata_fast/raw/main/$model.traineddata" -OutFile $target
        }
    }
}
dotnet restore SalExtract.sln --configfile NuGet.Config
dotnet build SalExtract.sln --no-restore
dotnet test SalExtract.sln --no-build --no-restore
Write-Host 'Setup complete. Open SalExtract.sln, choose the SalExtract launch profile, and press F5.' -ForegroundColor Green
