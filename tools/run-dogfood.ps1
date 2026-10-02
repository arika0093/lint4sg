param(
    [Parameter(Mandatory = $true)][string]$FeedPath,
    [Parameter(Mandatory = $true)][string]$ResultsPath,
    [Parameter(Mandatory = $true)][string[]]$GeneratorProjects,
    [int]$MaxBuildSeconds = 90
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Get-Location).Path
New-Item -ItemType Directory -Force -Path $ResultsPath | Out-Null
$resolvedFeed = (Resolve-Path -LiteralPath $FeedPath).Path
$package = Get-ChildItem -LiteralPath $resolvedFeed -Filter 'lint4sg.*.nupkg' | Select-Object -First 1
if ($null -eq $package) { throw "No lint4sg package found in $resolvedFeed" }
$version = [regex]::Match($package.Name, '^lint4sg\.(.+)\.nupkg$').Groups[1].Value
$nugetConfig = Join-Path $ResultsPath 'nuget.config'
$nugetXml = [xml]'<configuration><packageSources><clear/><add key="lint4sg-local" value=""/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>'
$nugetXml.configuration.packageSources.add[0].value = $resolvedFeed
$nugetXml.Save($nugetConfig)
$packageCache = Join-Path $env:TEMP 'lint4sg-dogfood-packages'
$localLint4sgCache = Join-Path $packageCache (Join-Path 'lint4sg' $version)
if (([IO.Path]::GetFullPath($localLint4sgCache)).StartsWith(
        ([IO.Path]::GetFullPath($packageCache) + [IO.Path]::DirectorySeparatorChar),
        [StringComparison]::OrdinalIgnoreCase
    ) -and (Test-Path -LiteralPath $localLint4sgCache)) {
    Remove-Item -LiteralPath $localLint4sgCache -Recurse -Force
}

$summary = [System.Collections.Generic.List[string]]::new()
$failedBuilds = [System.Collections.Generic.List[string]]::new()
$configuredRepositories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($projectRelativePath in $GeneratorProjects) {
    $projectPath = if ([IO.Path]::IsPathRooted($projectRelativePath)) {
        $projectRelativePath
    } else {
        Join-Path $repoRoot $projectRelativePath
    }
    if (-not (Test-Path -LiteralPath $projectPath)) { throw "Missing generator project: $projectPath" }

    $projectRoot = (& git -C (Split-Path -Parent $projectPath) rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not locate repository for $projectPath" }
    if ($configuredRepositories.Add($projectRoot)) {
        $editorConfig = Join-Path $projectRoot '.editorconfig'
        if (-not (Test-Path -LiteralPath $editorConfig)) {
            @('root = true', '') | Set-Content -LiteralPath $editorConfig
        }
        Add-Content -LiteralPath $editorConfig -Value @('', '[*.cs]')
        (1..22 | ForEach-Object { "dotnet_diagnostic.LSG{0:D3}.severity = warning" -f $_ }) |
            Add-Content -LiteralPath $editorConfig
    }

    $existing = Select-String -LiteralPath $projectPath -Pattern 'Include="lint4sg"' -Quiet
    if (-not $existing) {
        dotnet add $projectPath package lint4sg --version $version --source $resolvedFeed --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Could not add the local lint4sg package to $projectPath" }
    }

    $logPath = Join-Path $ResultsPath (([IO.Path]::GetFileNameWithoutExtension($projectPath)) + '.log')
    dotnet restore $projectPath --configfile $nugetConfig -p:RestorePackagesPath=$packageCache
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for $projectPath" }

    $buildTimer = [Diagnostics.Stopwatch]::StartNew()
    dotnet build $projectPath --no-restore --verbosity normal -p:RestorePackagesPath=$packageCache -p:ReportAnalyzer=true -p:GeneratePackageOnBuild=false -p:PackOnBuild=false -p:TreatWarningsAsErrors=false -p:WarningsAsErrors= 2>&1 |
        Tee-Object -FilePath $logPath
    $buildExitCode = $LASTEXITCODE
    $buildTimer.Stop()
    $diagnosticCounts = Select-String -LiteralPath $logPath -Pattern '\bLSG\d{3}\b' -AllMatches |
        ForEach-Object { $_.Matches.Value } | Group-Object | Sort-Object Name |
        ForEach-Object { '{0}: {1}' -f $_.Name, $_.Count }
    $summary.Add("$projectRelativePath (build exit $buildExitCode)")
    $summary.Add(('Build wall time: {0:F2} seconds' -f $buildTimer.Elapsed.TotalSeconds))
    if ($buildExitCode -ne 0) { $failedBuilds.Add("$projectPath exited with $buildExitCode") }
    if ($buildTimer.Elapsed.TotalSeconds -gt $MaxBuildSeconds) {
        $failedBuilds.Add("$projectPath exceeded the $MaxBuildSeconds second analyzer build-time smoke threshold")
    }
    if ($diagnosticCounts) { $summary.AddRange([string[]]$diagnosticCounts) }
    $summary.Add('')
}

$summary | Set-Content -LiteralPath (Join-Path $ResultsPath 'summary.txt')
if ($failedBuilds.Count -gt 0) {
    throw "Dogfood checks failed: $($failedBuilds -join '; ')"
}
