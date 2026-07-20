[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Add')]
    [string]$Add,
    [Parameter(Mandatory = $true, ParameterSetName = 'Remove')]
    [string]$Remove,
    [string]$ExpectedUserSid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$trustedModuleRoot = [IO.Path]::Combine([Environment]::SystemDirectory, 'WindowsPowerShell', 'v1.0', 'Modules')
$env:PSModulePath = $trustedModuleRoot

$productName = 'ResticBackuper'
$programFilesRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$programDataRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$installRoot = [IO.Path]::GetFullPath((Join-Path $programFilesRoot $productName)).TrimEnd('\')
$stateRoot = [IO.Path]::GetFullPath((Join-Path $programDataRoot $productName)).TrimEnd('\')
$configPath = Join-Path $installRoot 'backup-config.json'
$lockPath = Join-Path $stateRoot 'run.lock'
$lockStream = $null

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-PathEqual {
    param([string]$Left, [string]$Right)
    return [string]::Equals($Left, $Right, [StringComparison]::OrdinalIgnoreCase)
}

function Test-IsWithin {
    param([string]$Candidate, [string]$Parent)
    if (Test-PathEqual -Left $Candidate -Right $Parent) {
        return $true
    }
    $prefix = $Parent.TrimEnd('\') + '\'
    return $Candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Get-CanonicalLocalPath {
    param(
        [string]$Value,
        [switch]$RequireDirectory
    )
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw 'A non-empty absolute local path is required.'
    }
    if (-not [IO.Path]::IsPathRooted($Value) -or $Value.StartsWith('\\')) {
        throw "Only absolute local drive-letter paths are supported: $Value"
    }
    $full = [IO.Path]::GetFullPath($Value)
    $root = [IO.Path]::GetPathRoot($full)
    if ($root -notmatch '^[A-Za-z]:\\$' -or $full.Substring(2).Contains(':')) {
        throw "Only normal local drive-letter paths are supported: $Value"
    }
    if (-not (Test-PathEqual -Left $full -Right $root)) {
        $full = $full.TrimEnd('\')
    }
    if ($RequireDirectory) {
        if (-not (Test-Path -LiteralPath $full -PathType Container)) {
            throw "Source directory does not exist: $full"
        }
    }
    return $full
}

function Assert-NormalDirectoryChain {
    param([string]$Directory)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Directory does not exist: $Directory"
    }
    $current = Get-Item -LiteralPath $Directory -Force
    while ($null -ne $current) {
        if (-not $current.PSIsContainer -or ($current.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Directory path contains a reparse point or non-directory component: $($current.FullName)"
        }
        $parent = $current.Parent
        if ($null -eq $parent) {
            break
        }
        $current = Get-Item -LiteralPath $parent.FullName -Force
    }
}

function Assert-NormalFile {
    param([string]$File)
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
        throw "Required regular file is missing: $File"
    }
    $item = Get-Item -LiteralPath $File -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Path is not a normal file: $File"
    }
}

function Get-RequiredProperty {
    param(
        [object]$Object,
        [string]$Name
    )
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Backup configuration is missing required property: $Name"
    }
    return $property.Value
}

function Get-Sha256Hex {
    param([byte[]]$Bytes)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($Bytes)
        return ([BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-FileSha256Hex {
    param([string]$File)
    $stream = [IO.File]::Open($File, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($stream)
        return ([BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function ConvertTo-Utf8JsonBytes {
    param([object]$Value)
    $text = ($Value | ConvertTo-Json -Depth 12) + "`n"
    return [Text.UTF8Encoding]::new($false).GetBytes($text)
}

function Set-AtomicFileBytes {
    param(
        [string]$Target,
        [byte[]]$Bytes
    )
    Assert-NormalFile -File $Target
    $parent = Split-Path -Parent $Target
    Assert-NormalDirectoryChain -Directory $parent
    $temporary = Join-Path $parent ('.{0}.{1}.tmp' -f ([IO.Path]::GetFileName($Target)), [Guid]::NewGuid().ToString('N'))
    try {
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $stream.Write($Bytes, 0, $Bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }
        # Calling this overload directly from Windows PowerShell converts a
        # null backup name to an empty string. Reflection preserves a true
        # null, so ReplaceFile performs one atomic replacement without leaving
        # a backup artifact in the protected directory. The temporary file's
        # contents were flushed to disk immediately above.
        $replaceMethod = [IO.File].GetMethod(
            'Replace',
            [type[]]@([string], [string], [string])
        )
        if ($null -eq $replaceMethod) {
            throw 'The required atomic file replacement API is unavailable.'
        }
        [void]$replaceMethod.Invoke(
            $null,
            [object[]]@($temporary, $Target, $null)
        )
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
    }
}

function Enter-RunLock {
    param([string]$File)
    $parent = Split-Path -Parent $File
    Assert-NormalDirectoryChain -Directory $parent
    if (Test-Path -LiteralPath $File) {
        Assert-NormalFile -File $File
    }
    $stream = [IO.File]::Open($File, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -eq 0) {
            $stream.WriteByte(0)
            $stream.Flush($true)
        }
        $stream.Lock(0, 1)
        return $stream
    }
    catch {
        $stream.Dispose()
        throw 'A backup or another source change is already running. Try again after it finishes.'
    }
}

function Exit-RunLock {
    param([IO.FileStream]$Stream)
    if ($null -eq $Stream) {
        return
    }
    try {
        $Stream.Unlock(0, 1)
    }
    finally {
        $Stream.Dispose()
    }
}

function Assert-NoOverlap {
    param(
        [string]$Candidate,
        [string]$Other,
        [string]$Message
    )
    if ((Test-IsWithin -Candidate $Candidate -Parent $Other) -or (Test-IsWithin -Candidate $Other -Parent $Candidate)) {
        throw "$Message`: $Candidate and $Other"
    }
}

function Get-ValidatedConfiguration {
    param([string]$File)
    Assert-NormalFile -File $File
    $rawBytes = [IO.File]::ReadAllBytes($File)
    $rawText = [Text.UTF8Encoding]::new($false, $true).GetString($rawBytes)
    $configuration = $rawText | ConvertFrom-Json
    if ($null -eq $configuration -or $configuration -isnot [Management.Automation.PSCustomObject]) {
        throw 'Backup configuration must be one JSON object.'
    }
    $schemaVersion = Get-RequiredProperty -Object $configuration -Name 'schema_version'
    if ($schemaVersion -isnot [int] -or $schemaVersion -ne 1) {
        throw 'Unsupported backup configuration schema.'
    }

    foreach ($name in @(
        'repository',
        'repository_volume_serial',
        'restic_executable',
        'recovery_tools_directory',
        'python_executable',
        'state_directory',
        'secret_file',
        'recovery_key_file',
        'exclude_file',
        'canary_file',
        'hostname',
        'scheduled_tag',
        'sources',
        'use_vss'
    )) {
        [void](Get-RequiredProperty -Object $configuration -Name $name)
    }

    $repository = Get-CanonicalLocalPath -Value ([string]$configuration.repository)
    $configuredState = Get-CanonicalLocalPath -Value ([string]$configuration.state_directory)
    $resticExecutable = Get-CanonicalLocalPath -Value ([string]$configuration.restic_executable)
    $pythonExecutable = Get-CanonicalLocalPath -Value ([string]$configuration.python_executable)
    $excludeFile = Get-CanonicalLocalPath -Value ([string]$configuration.exclude_file)
    $secretFile = Get-CanonicalLocalPath -Value ([string]$configuration.secret_file)
    $canaryFile = Get-CanonicalLocalPath -Value ([string]$configuration.canary_file)
    $recoveryTools = Get-CanonicalLocalPath -Value ([string]$configuration.recovery_tools_directory)
    [void](Get-CanonicalLocalPath -Value ([string]$configuration.recovery_key_file))

    if (-not (Test-PathEqual -Left $configuredState -Right $stateRoot)) {
        throw "Configuration names an unexpected state directory: $configuredState"
    }
    $expectedRestic = Join-Path $installRoot 'restic.exe'
    $expectedPython = Join-Path $installRoot 'Python\python.exe'
    $expectedExcludes = Join-Path $installRoot 'excludes.txt'
    $expectedSecret = Join-Path $stateRoot 'repository-password.dpapi.json'
    $expectedCanary = Join-Path $stateRoot 'Canary\backup-canary.txt'
    foreach ($pair in @(
        @($resticExecutable, $expectedRestic, 'Restic executable'),
        @($pythonExecutable, $expectedPython, 'Python executable'),
        @($excludeFile, $expectedExcludes, 'exclude file'),
        @($secretFile, $expectedSecret, 'DPAPI secret file'),
        @($canaryFile, $expectedCanary, 'canary file')
    )) {
        if (-not (Test-PathEqual -Left $pair[0] -Right $pair[1])) {
            throw "$($pair[2]) path is outside the protected installation."
        }
    }

    $repositoryParent = Split-Path -Parent $repository
    if ([string]::IsNullOrWhiteSpace($repositoryParent)) {
        throw 'The repository must not be a drive root.'
    }
    $expectedRecoveryTools = Get-CanonicalLocalPath -Value (Join-Path $repositoryParent 'RecoveryTools')
    if (-not (Test-PathEqual -Left $recoveryTools -Right $expectedRecoveryTools)) {
        throw "Configuration names an unexpected recovery-tools directory: $recoveryTools"
    }
    Assert-NormalDirectoryChain -Directory $repository
    Assert-NormalFile -File (Join-Path $repository 'config')
    Assert-NormalDirectoryChain -Directory $recoveryTools
    Assert-NormalFile -File $resticExecutable
    Assert-NormalFile -File $pythonExecutable
    Assert-NormalFile -File $excludeFile
    Assert-NormalFile -File $canaryFile

    if ($configuration.repository_volume_serial -isnot [string] -or $configuration.repository_volume_serial -notmatch '^[0-9A-Fa-f]{8}$') {
        throw 'Repository volume serial is invalid.'
    }
    if ($configuration.hostname -isnot [string] -or [string]::IsNullOrWhiteSpace($configuration.hostname) -or
        $configuration.scheduled_tag -isnot [string] -or [string]::IsNullOrWhiteSpace($configuration.scheduled_tag)) {
        throw 'Configured hostname or scheduled tag is invalid.'
    }
    if ($configuration.use_vss -isnot [bool]) {
        throw 'Configured use_vss value must be a JSON Boolean.'
    }

    Assert-NoOverlap -Candidate $repository -Other $installRoot -Message 'Repository overlaps the app runtime'
    Assert-NoOverlap -Candidate $repository -Other $stateRoot -Message 'Repository overlaps the app state'
    Assert-NoOverlap -Candidate $repository -Other $recoveryTools -Message 'Repository overlaps recovery tools'

    $sourceValues = @($configuration.sources)
    if ($sourceValues.Count -lt 2) {
        throw 'Configuration must contain a user source and the protected canary source.'
    }
    $sourceSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sources = [Collections.Generic.List[string]]::new()
    foreach ($value in $sourceValues) {
        if ($value -isnot [string]) {
            throw 'Every configured source must be a path string.'
        }
        $source = Get-CanonicalLocalPath -Value $value
        if (-not $sourceSet.Add($source)) {
            throw "Duplicate source in backup configuration: $source"
        }
        foreach ($existing in $sources) {
            Assert-NoOverlap -Candidate $source -Other $existing -Message 'Configured sources contain one another'
        }
        Assert-NoOverlap -Candidate $source -Other $repository -Message 'Source overlaps the repository'
        $sources.Add($source)
    }

    $canarySource = Get-CanonicalLocalPath -Value (Split-Path -Parent $canaryFile)
    $canaryMatches = @($sources | Where-Object { Test-PathEqual -Left $_ -Right $canarySource })
    if ($canaryMatches.Count -ne 1) {
        throw 'The exact protected canary directory must occur once in configured sources.'
    }
    foreach ($source in $sources) {
        if (Test-PathEqual -Left $source -Right $canarySource) {
            continue
        }
        Assert-NoOverlap -Candidate $source -Other $installRoot -Message 'User source overlaps the app runtime'
        Assert-NoOverlap -Candidate $source -Other $stateRoot -Message 'User source overlaps the app state'
        Assert-NoOverlap -Candidate $source -Other $recoveryTools -Message 'User source overlaps recovery tools'
    }

    return [pscustomobject]@{
        Json = $configuration
        RawBytes = $rawBytes
        Repository = $repository
        RecoveryTools = $recoveryTools
        CanarySource = $canarySource
        Sources = @($sources)
        UseVss = [bool]$configuration.use_vss
    }
}

function Assert-RecoveryToolsOwned {
    param([pscustomobject]$Validated)
    $destination = $Validated.RecoveryTools
    Assert-NormalDirectoryChain -Directory $destination
    $expectedNames = @(
        'restic.exe',
        'restore.py',
        'secret_store.py',
        'backup-config.json',
        'RECOVERY.md',
        'restic-release.json'
    )
    $allowedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $expectedNames) { [void]$allowedNames.Add($name) }
    [void]$allowedNames.Add('recovery-manifest.json')
    $entries = @(Get-ChildItem -LiteralPath $destination -Force)
    foreach ($entry in $entries) {
        if ($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or -not $allowedNames.Contains($entry.Name)) {
            throw "Recovery-tools directory contains an unsafe or unexpected entry: $($entry.Name)"
        }
    }
    if ($entries.Count -ne $allowedNames.Count) {
        throw 'Recovery-tools directory is incomplete.'
    }
    foreach ($name in $allowedNames) {
        Assert-NormalFile -File (Join-Path $destination $name)
    }

    $manifestPath = Join-Path $destination 'recovery-manifest.json'
    $manifest = [IO.File]::ReadAllText($manifestPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
    if ($null -eq $manifest -or $manifest -isnot [Management.Automation.PSCustomObject] -or
        $manifest.schema_version -ne 1 -or $manifest.repository -isnot [string]) {
        throw 'Recovery-tools manifest header is invalid.'
    }
    $manifestRepository = Get-CanonicalLocalPath -Value ([string]$manifest.repository)
    if (-not (Test-PathEqual -Left $manifestRepository -Right $Validated.Repository)) {
        throw 'Recovery-tools manifest names another repository.'
    }
    $manifestFiles = @($manifest.files)
    if ($manifestFiles.Count -ne $expectedNames.Count) {
        throw 'Recovery-tools manifest file count is invalid.'
    }
    $manifestNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $manifestFiles) {
        if ($null -eq $record -or $record.name -isnot [string] -or -not $allowedNames.Contains($record.name) -or
            $record.name -eq 'recovery-manifest.json' -or -not $manifestNames.Add($record.name) -or
            $record.sha256 -isnot [string] -or $record.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw 'Recovery-tools manifest contains an invalid or duplicate file record.'
        }
        if ($record.name -ne 'backup-config.json') {
            $recoveryFile = Join-Path $destination $record.name
            $fileItem = Get-Item -LiteralPath $recoveryFile -Force
            $actualHash = Get-FileSha256Hex -File $recoveryFile
            if ([long]$record.bytes -ne $fileItem.Length -or $record.sha256 -ne $actualHash) {
                throw "Recovery tool failed its ownership hash check: $($record.name)"
            }
        }
    }
    foreach ($name in $expectedNames) {
        if (-not $manifestNames.Contains($name)) {
            throw "Recovery-tools manifest is missing a file record: $name"
        }
    }

    return [pscustomobject]@{
        ManifestPath = $manifestPath
        ConfigPath = Join-Path $destination 'backup-config.json'
        ExpectedNames = $expectedNames
    }
}

function New-RecoveryManifestBytes {
    param(
        [pscustomobject]$Validated,
        [pscustomobject]$Recovery,
        [byte[]]$NewConfigBytes
    )
    $records = @(
        foreach ($name in $Recovery.ExpectedNames) {
            if ($name -eq 'backup-config.json') {
                [ordered]@{
                    name = $name
                    bytes = $NewConfigBytes.Length
                    sha256 = Get-Sha256Hex -Bytes $NewConfigBytes
                }
            }
            else {
                $file = Join-Path $Validated.RecoveryTools $name
                $item = Get-Item -LiteralPath $file -Force
                [ordered]@{
                    name = $name
                    bytes = $item.Length
                    sha256 = Get-FileSha256Hex -File $file
                }
            }
        }
    )
    $manifest = [ordered]@{
        schema_version = 1
        created_utc = [DateTime]::UtcNow.ToString('o')
        repository = $Validated.Repository
        files = $records
        note = 'The repository password is intentionally not stored in this bundle.'
    }
    return ConvertTo-Utf8JsonBytes -Value $manifest
}

function Set-ConfigurationAndRecovery {
    param(
        [pscustomobject]$Validated,
        [byte[]]$NewConfigBytes
    )
    $recovery = Assert-RecoveryToolsOwned -Validated $Validated
    $newManifestBytes = New-RecoveryManifestBytes -Validated $Validated -Recovery $recovery -NewConfigBytes $NewConfigBytes
    $oldMainBytes = [IO.File]::ReadAllBytes($configPath)
    $oldRecoveryConfigBytes = [IO.File]::ReadAllBytes($recovery.ConfigPath)
    $oldManifestBytes = [IO.File]::ReadAllBytes($recovery.ManifestPath)
    $mainChanged = $false
    $recoveryConfigChanged = $false
    $manifestChanged = $false
    try {
        Set-AtomicFileBytes -Target $configPath -Bytes $NewConfigBytes
        $mainChanged = $true
        Set-AtomicFileBytes -Target $recovery.ConfigPath -Bytes $NewConfigBytes
        $recoveryConfigChanged = $true
        Set-AtomicFileBytes -Target $recovery.ManifestPath -Bytes $newManifestBytes
        $manifestChanged = $true
    }
    catch {
        $writeError = $_.Exception.Message
        $rollbackErrors = [Collections.Generic.List[string]]::new()
        if ($mainChanged) {
            try { Set-AtomicFileBytes -Target $configPath -Bytes $oldMainBytes }
            catch { $rollbackErrors.Add("main config: $($_.Exception.Message)") }
        }
        if ($recoveryConfigChanged) {
            try { Set-AtomicFileBytes -Target $recovery.ConfigPath -Bytes $oldRecoveryConfigBytes }
            catch { $rollbackErrors.Add("recovery config: $($_.Exception.Message)") }
        }
        if ($manifestChanged) {
            try { Set-AtomicFileBytes -Target $recovery.ManifestPath -Bytes $oldManifestBytes }
            catch { $rollbackErrors.Add("recovery manifest: $($_.Exception.Message)") }
        }
        if ($rollbackErrors.Count -gt 0) {
            throw "Source update failed ($writeError), and rollback needs attention: $($rollbackErrors -join '; ')"
        }
        throw "Source update failed and was rolled back: $writeError"
    }
}

try {
    $Action = $PSCmdlet.ParameterSetName
    $Path = if ($Action -eq 'Add') { $Add } elseif ($Action -eq 'Remove') { $Remove } else { $null }
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
        throw 'ResticBackuper source management requires 64-bit Windows PowerShell.'
    }
    if (-not (Test-Administrator)) {
        throw 'Source changes must be run elevated.'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentSid = $identity.User.Value
    if ([string]::IsNullOrWhiteSpace($ExpectedUserSid) -or
        -not [string]::Equals($ExpectedUserSid, $currentSid, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Approve elevation with the same Windows account that requested this source change.'
    }
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw 'Source management must be executed as an installed script, not dot-sourced.'
    }
    $actualScript = [IO.Path]::GetFullPath($PSCommandPath)
    $expectedScript = Join-Path $installRoot 'Manage-Sources.ps1'
    if (-not (Test-PathEqual -Left $actualScript -Right $expectedScript)) {
        throw "Refusing to manage sources outside the protected installation: $actualScript"
    }
    Assert-NormalDirectoryChain -Directory $installRoot
    Assert-NormalDirectoryChain -Directory $stateRoot
    Assert-NormalFile -File $actualScript

    if ($Action -ne 'List' -and [string]::IsNullOrWhiteSpace($Path)) {
        throw 'The add or remove path must not be empty.'
    }

    $lockStream = Enter-RunLock -File $lockPath
    $validated = Get-ValidatedConfiguration -File $configPath
    $sources = [Collections.Generic.List[string]]::new()
    foreach ($source in $validated.Sources) { $sources.Add($source) }
    $changedPath = $null
    $changed = $false

    if ($Action -eq 'Add') {
        $candidate = Get-CanonicalLocalPath -Value $Path -RequireDirectory
        Assert-NormalDirectoryChain -Directory $candidate
        Assert-NoOverlap -Candidate $candidate -Other $validated.Repository -Message 'Source overlaps the repository'
        Assert-NoOverlap -Candidate $candidate -Other $installRoot -Message 'Source overlaps the app runtime'
        Assert-NoOverlap -Candidate $candidate -Other $stateRoot -Message 'Source overlaps the app state'
        Assert-NoOverlap -Candidate $candidate -Other $validated.RecoveryTools -Message 'Source overlaps recovery tools'
        foreach ($existing in $sources) {
            Assert-NoOverlap -Candidate $candidate -Other $existing -Message 'Source duplicates, contains, or is contained by a configured source'
        }
        $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($candidate))
        if (-not $drive.IsReady -or $drive.DriveType -notin @([IO.DriveType]::Removable, [IO.DriveType]::Fixed)) {
            throw "A new source must be on a ready local fixed or removable drive: $candidate"
        }
        if ($validated.UseVss) {
            if ([string]$drive.DriveFormat -ne 'NTFS') {
                throw "VSS is enabled, so a new source must be on NTFS: $candidate"
            }
        }
        $sources.Insert($sources.Count - 1, $candidate)
        $changedPath = $candidate
        $changed = $true
    }
    elseif ($Action -eq 'Remove') {
        $candidate = Get-CanonicalLocalPath -Value $Path
        $matchIndex = -1
        for ($index = 0; $index -lt $sources.Count; $index++) {
            if (Test-PathEqual -Left $candidate -Right $sources[$index]) {
                $matchIndex = $index
                break
            }
        }
        if ($matchIndex -lt 0) {
            throw "Removal path is not an exact configured source: $candidate"
        }
        if (Test-PathEqual -Left $sources[$matchIndex] -Right $validated.CanarySource) {
            throw 'The protected restore-canary source cannot be removed.'
        }
        $userSourceCount = @($sources | Where-Object { -not (Test-PathEqual -Left $_ -Right $validated.CanarySource) }).Count
        if ($userSourceCount -le 1) {
            throw 'The last user backup source cannot be removed.'
        }
        $changedPath = $sources[$matchIndex]
        $sources.RemoveAt($matchIndex)
        $changed = $true
    }

    if ($changed) {
        $validated.Json.sources = @($sources)
        $newConfigBytes = ConvertTo-Utf8JsonBytes -Value $validated.Json
        Set-ConfigurationAndRecovery -Validated $validated -NewConfigBytes $newConfigBytes
    }

    $userSources = @($sources | Where-Object { -not (Test-PathEqual -Left $_ -Right $validated.CanarySource) })
    [ordered]@{
        ok = $true
        action = $Action.ToLowerInvariant()
        changed = $changed
        path = $changedPath
        user_source_count = $userSources.Count
        source_count = $sources.Count
        canary_source = $validated.CanarySource
        sources = @($sources)
    } | ConvertTo-Json -Compress -Depth 5
}
catch {
    $resultAction = if ($null -ne $Action) { $Action.ToLowerInvariant() } else { $PSCmdlet.ParameterSetName.ToLowerInvariant() }
    [ordered]@{
        ok = $false
        action = $resultAction
        error = $_.Exception.Message
    } | ConvertTo-Json -Compress -Depth 3
    exit 1
}
finally {
    if ($null -ne $lockStream) {
        Exit-RunLock -Stream $lockStream
    }
}
