param(
    [string]$Version = "1.0.0.0",
    [string]$IdentityName = "",
    [string]$Publisher = "",
    [string]$PublisherDisplayName = ""
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot 'src\Eigen\Eigen.csproj'
$stageDir = Join-Path $PSScriptRoot 'stage\win-x64'
$outDir = Join-Path $PSScriptRoot 'out'
$manifestSrc = Join-Path $PSScriptRoot 'Package.appxmanifest'
$assetsSrc = Join-Path $PSScriptRoot 'Assets'

if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "1/4 Publishing Eigen (Release, win-x64, self-contained, ReadyToRun)..." -ForegroundColor Cyan
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $stageDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Remove PDBs from Store MSIX payload
Get-ChildItem -Path $stageDir -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "2/4 Copying AppxManifest.xml and Store PNG assets..." -ForegroundColor Cyan
$manifestDst = Join-Path $stageDir 'AppxManifest.xml'
[xml]$xml = Get-Content -Path $manifestSrc -Raw -Encoding UTF8
$xml.Package.Identity.Version = $Version
if (-not [string]::IsNullOrWhiteSpace($IdentityName)) { $xml.Package.Identity.Name = $IdentityName }
if (-not [string]::IsNullOrWhiteSpace($Publisher)) { $xml.Package.Identity.Publisher = $Publisher }
if (-not [string]::IsNullOrWhiteSpace($PublisherDisplayName)) { $xml.Package.Properties.PublisherDisplayName = $PublisherDisplayName }
$xml.Save($manifestDst)

$assetsDst = Join-Path $stageDir 'Assets'
New-Item -ItemType Directory -Path $assetsDst -Force | Out-Null
Copy-Item -Path (Join-Path $assetsSrc '*.png') -Destination $assetsDst -Force

Write-Host "3/4 Locating makeappx.exe..." -ForegroundColor Cyan
$makeAppx = $null
$kitCandidates = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending
if ($kitCandidates) {
    $makeAppx = $kitCandidates[0].FullName
}

if (-not $makeAppx) {
    $toolsProjDir = Join-Path $PSScriptRoot '.tools'
    New-Item -ItemType Directory -Path $toolsProjDir -Force | Out-Null
    $toolsProj = Join-Path $toolsProjDir 'BuildTools.csproj'
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.22621.3233" />
  </ItemGroup>
</Project>
'@ | Set-Content -Path $toolsProj -Encoding UTF8

    dotnet restore $toolsProj --packages (Join-Path $toolsProjDir 'pkgs') | Out-Null
    $nugetMakeAppx = Get-ChildItem -Path $toolsProjDir -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Select-Object -First 1
    if ($nugetMakeAppx) {
        $makeAppx = $nugetMakeAppx.FullName
    }
}

if (-not $makeAppx) {
    throw "Could not find makeappx.exe. Install the Windows 10/11 SDK or restore Microsoft.Windows.SDK.BuildTools."
}

$msixOut = Join-Path $outDir ("Eigen_" + $Version + "_x64.msix")
if (Test-Path $msixOut) { Remove-Item -Force $msixOut }

Write-Host "4/4 Packing $msixOut..." -ForegroundColor Cyan
& $makeAppx pack /o /d $stageDir /p $msixOut
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE" }

Write-Host "SUCCESS: Built $msixOut ($((Get-Item $msixOut).Length) bytes)" -ForegroundColor Green
