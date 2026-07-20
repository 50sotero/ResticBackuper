param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$outputDirectory = Join-Path $project 'dist'
$output = Join-Path $outputDirectory 'ResticBackuperDashboard.exe'
$icon = Join-Path $project 'assets\dashboard-icon.ico'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The .NET Framework 4.8 C# compiler is unavailable: $compiler"
}
if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
    throw "The dashboard application icon is unavailable: $icon"
}
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$references = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
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
    (Join-Path $project 'Telemetry.cs'),
    (Join-Path $project 'RunChart.cs'),
    (Join-Path $project 'DashboardWindow.cs')
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
[pscustomobject]@{
    executable = $item.FullName
    bytes = $item.Length
    sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    file_version = $item.VersionInfo.FileVersion
    configuration = $Configuration
} | ConvertTo-Json -Depth 3
