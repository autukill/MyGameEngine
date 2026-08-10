[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/playtest-package",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$NoRestore,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../.."))
$projectPath = Join-Path $repositoryRoot "games/TheGodTheyMade/src/TheGodTheyMade.Game/TheGodTheyMade.Game.csproj"
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$packageName = "TheGodTheyMade-Mingzhong-$RuntimeIdentifier"
$publishDirectory = Join-Path $outputRoot $packageName
$archivePath = Join-Path $outputRoot "$packageName.zip"
$hashPath = "$archivePath.sha256"
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "Unable to read the source commit." }
$workingTreeChanges = @(& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the source working tree." }
$isDirty = $workingTreeChanges.Count -gt 0
if ($isDirty -and !$AllowDirty) {
    throw "Refusing to create an external playtest package from a dirty working tree. Commit first or use -AllowDirty for a local probe."
}

function Assert-ChildPath([string]$Path, [string]$Parent) {
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (!$resolvedPath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the package output root: $resolvedPath"
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
Assert-ChildPath $publishDirectory $outputRoot
Assert-ChildPath $archivePath $outputRoot
Assert-ChildPath $hashPath $outputRoot
if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
if (Test-Path -LiteralPath $hashPath) { Remove-Item -LiteralPath $hashPath -Force }

if (!$NoRestore) {
    & dotnet restore $projectPath -r $RuntimeIdentifier --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
}

& dotnet publish $projectPath `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    --no-restore `
    --nologo `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$executablePath = Join-Path $publishDirectory "TheGodTheyMade.exe"
$requiredFiles = @(
    $executablePath,
    (Join-Path $publishDirectory "Start-Playtest.cmd"),
    (Join-Path $publishDirectory "PLAYTEST_README.txt"),
    (Join-Path $publishDirectory "AssetsCompiled/assets.json"),
    (Join-Path $publishDirectory "AssetsCompiled/.mygame-assets.json")
)
foreach ($requiredFile in $requiredFiles) {
    if (!(Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Published playtest package is missing: $requiredFile"
    }
}

& $executablePath --smoke
if ($LASTEXITCODE -ne 0) { throw "Published executable smoke test failed with exit code $LASTEXITCODE." }

$buildInfo = @(
    "game=the-god-they-made.mingzhong",
    "slice=gate-4",
    "runtime=$RuntimeIdentifier",
    "commit=$commit",
    "dirty=$($isDirty.ToString().ToLowerInvariant())"
) -join [Environment]::NewLine
[IO.File]::WriteAllText(
    (Join-Path $publishDirectory "BUILD_INFO.txt"),
    $buildInfo + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $hashPath,
    "$hash  $([IO.Path]::GetFileName($archivePath))$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))

$archive = Get-Item -LiteralPath $archivePath
Write-Host "Playtest package: $($archive.FullName)"
Write-Host "SHA-256: $hash"
Write-Host ("Size: {0:N2} MiB" -f ($archive.Length / 1MB))
