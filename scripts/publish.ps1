[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$output = Join-Path $artifactsRoot $Runtime
$resolvedOutput = [System.IO.Path]::GetFullPath($output)
$projectPath = Join-Path $repoRoot "src\RSDWSaveConverter.App\RSDWSaveConverter.App.csproj"
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$versionNode = $project.SelectSingleNode("/Project/PropertyGroup/Version")

if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "The app project does not define a release version."
}

$version = $versionNode.InnerText.Trim()
$isWindows = $Runtime.StartsWith("win-")
$exeSuffix = if ($isWindows) { ".exe" } else { "" }
$binaryName = "RSDWSaveConverter$exeSuffix"
$releaseStem = "RSDWSaveConverter-v$version-$Runtime"
$portableBinary = Join-Path $artifactsRoot "$releaseStem$exeSuffix"
$archive = Join-Path $artifactsRoot "$releaseStem.zip"
$checksum = Join-Path $artifactsRoot "$releaseStem.sha256"

if (-not $resolvedOutput.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish output outside $artifactsRoot"
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

foreach ($releaseFile in @($portableBinary, $archive, $checksum)) {
    if (Test-Path -LiteralPath $releaseFile) {
        Remove-Item -LiteralPath $releaseFile -Force
    }
}

if (-not $SkipTests) {
    dotnet test (Join-Path $repoRoot "RSDWSaveConverter.sln") --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed."
    }
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    --output $output
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

foreach ($document in @("README.md", "CHANGELOG.md", "THIRD_PARTY_NOTICES.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination (Join-Path $output $document)
}

$publishedBinary = Join-Path $output $binaryName
if (Test-Path -LiteralPath $publishedBinary) {
    Copy-Item -LiteralPath $publishedBinary -Destination $portableBinary
}
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $archive -CompressionLevel Optimal

$filesToHash = @($archive)
if (Test-Path -LiteralPath $portableBinary) {
    $filesToHash = @($portableBinary, $archive)
}

$hashLines = foreach ($releaseFile in $filesToHash) {
    $hash = Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $releaseFile)
}
[System.IO.File]::WriteAllLines($checksum, $hashLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Published release $version for $Runtime"
Get-Item -LiteralPath ($filesToHash + @($checksum)) |
    Select-Object Name, Length, FullName |
    Format-Table -AutoSize
