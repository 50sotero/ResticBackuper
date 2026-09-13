param(
    [string]$Configuration = 'Release',
    [switch]$SkipWeb
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$outputDirectory = Join-Path $project 'dist'
$output = Join-Path $outputDirectory 'ResticBackuperDashboard.exe'
$icon = Join-Path $project 'assets\dashboard-icon.ico'
$webViewVersion = '1.0.4191.47'
$webViewPackage = Join-Path $project ('.packages\webview2.' + $webViewVersion)
$webViewAssembly = Join-Path $webViewPackage 'lib\net462\Microsoft.Web.WebView2.Wpf.dll'
$webRoot = Join-Path $project 'web'
$webOutput = Join-Path $outputDirectory 'web'
$licenseDirectory = Join-Path $outputDirectory 'licenses'

if (-not (Test-Path -LiteralPath $webViewAssembly)) {
    $archive = $webViewPackage + '.zip'
    New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$webViewVersion/microsoft.web.webview2.$webViewVersion.nupkg" `
        -OutFile $archive
    $expectedHash = 'F492BBF547D0DA329553B6727435B677579B1E9F91CC9E4A1AD029366D5F23D0'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'WebView2 package checksum does not match the pinned SDK.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $webViewPackage -Force
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The .NET Framework 4.8 C# compiler is unavailable: $compiler"
}
if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
    throw "The dashboard application icon is unavailable: $icon"
}
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

if (-not $SkipWeb) {
    if (-not (Test-Path -LiteralPath (Join-Path $webRoot 'package.json') -PathType Leaf)) {
        throw "Dashboard web source is unavailable: $webRoot"
    }
    Push-Location $webRoot
    try {
        & npm.cmd ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) {
            throw 'Proofhold web dependencies could not be installed.'
        }
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) {
            throw 'Proofhold web build failed.'
        }
    }
    finally {
        Pop-Location
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $webOutput 'index.html') -PathType Leaf)) {
    throw "Built Proofhold web assets are unavailable: $webOutput"
}

$references = @(
    $webViewAssembly,
    (Join-Path $webViewPackage 'lib\net462\Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Xml.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
    (Join-Path $framework 'System.IO.Compression.dll'),
    (Join-Path $framework 'System.IO.Compression.FileSystem.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'WPF\WindowsBase.dll'),
    (Join-Path $framework 'WPF\PresentationCore.dll'),
    (Join-Path $framework 'WPF\PresentationFramework.dll'),
    (Join-Path $env:SystemRoot 'Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Required .NET Framework assembly is unavailable: $reference"
    }
}

$sources = @(
    (Join-Path $project 'AssemblyInfo.cs'),
    (Join-Path $project 'Program.cs'),
    (Join-Path $project 'SourceConfiguration.cs'),
    (Join-Path $project 'RepositoryManager.cs'),
    (Join-Path $project 'RepositoryLocationWindow.cs'),
    (Join-Path $project 'RestoreManager.cs'),
    (Join-Path $project 'RestoreWindow.cs'),
    (Join-Path $project 'RecoveryHealthManager.cs'),
    (Join-Path $project 'RecoveryReadinessWindow.cs'),
    (Join-Path $project 'RunDetailsWindow.cs'),
    (Join-Path $project 'DiagnosticExporter.cs'),
    (Join-Path $project 'Telemetry.cs'),
    (Join-Path $project 'TaskSchedule.cs'),
    (Join-Path $project 'BackupFreshness.cs'),
    (Join-Path $project 'ScheduleManagerLauncher.cs'),
    (Join-Path $project 'ScheduleEditorWindow.cs'),
    (Join-Path $project 'BackupTaskController.cs'),
    (Join-Path $project 'BackupCancellationController.cs'),
    (Join-Path $project 'DashboardTheme.cs'),
    (Join-Path $project 'DashboardMotion.cs'),
    (Join-Path $project 'DashboardVisualStyle.cs'),
    (Join-Path $project 'RunChart.cs'),
    (Join-Path $project 'DashboardWindow.cs'),
    (Join-Path $project 'DashboardWindow.Web.cs')
)
foreach ($source in $sources) {
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Dashboard source file is unavailable: $source"
    }
}

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/warn:4',
    ('/out:' + $output),
    ('/win32icon:' + $icon),
    ('/win32manifest:' + (Join-Path $project 'app.manifest'))
)
if ($Configuration -eq 'Debug') {
    $arguments += @('/debug+', '/optimize-')
}
foreach ($reference in $references) {
    $arguments += '/reference:' + $reference
}
$arguments += $sources

& $compiler @arguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) {
    throw "Dashboard compilation failed with exit code $LASTEXITCODE."
}

$item = Get-Item -LiteralPath $output
$coreAssembly = Join-Path $webViewPackage 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$loader = Join-Path $webViewPackage 'runtimes\win-x64\native\WebView2Loader.dll'
Copy-Item -LiteralPath $webViewAssembly -Destination $outputDirectory -Force
Copy-Item -LiteralPath $coreAssembly -Destination $outputDirectory -Force
Copy-Item -LiteralPath $loader -Destination $outputDirectory -Force
if (Test-Path -LiteralPath $licenseDirectory) {
    Remove-Item -LiteralPath $licenseDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
$beautifulUiLicense = Join-Path $webRoot 'vendor\BEAUTIFULUI-MIT-LICENSE.txt'
if (Test-Path -LiteralPath $beautifulUiLicense -PathType Leaf) {
    Copy-Item -LiteralPath $beautifulUiLicense -Destination $licenseDirectory -Force
}
Copy-Item -LiteralPath (Join-Path $webViewPackage 'LICENSE.txt') -Destination (Join-Path $licenseDirectory 'WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $webViewPackage 'NOTICE.txt') -Destination (Join-Path $licenseDirectory 'WebView2-NOTICE.txt') -Force
$assetFiles = @(
    Get-ChildItem -LiteralPath $webOutput -Recurse -File -Force
    Get-ChildItem -LiteralPath $licenseDirectory -Recurse -File -Force
    Get-Item -LiteralPath (Join-Path $outputDirectory 'Microsoft.Web.WebView2.Core.dll')
    Get-Item -LiteralPath (Join-Path $outputDirectory 'Microsoft.Web.WebView2.Wpf.dll')
    Get-Item -LiteralPath (Join-Path $outputDirectory 'WebView2Loader.dll')
)
$assetManifest = [ordered]@{
    schema_version = 1
    files = @(
        foreach ($asset in $assetFiles | Sort-Object FullName) {
            [ordered]@{
                relative_path = $asset.FullName.Substring($outputDirectory.Length + 1).Replace('\', '/')
                bytes = $asset.Length
                sha256 = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )
}
$assetManifestPath = Join-Path $outputDirectory 'dashboard-assets.json'
[IO.File]::WriteAllText(
    $assetManifestPath,
    ($assetManifest | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false)
)
$assetManifestHash = (Get-FileHash -LiteralPath $assetManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
[pscustomobject]@{
    executable = $item.FullName
    bytes = $item.Length
    sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    file_version = $item.VersionInfo.FileVersion
    assets_manifest = $assetManifestPath
    assets_manifest_sha256 = $assetManifestHash
    asset_file_count = $assetManifest.files.Count
    configuration = $Configuration
} | ConvertTo-Json -Depth 3
