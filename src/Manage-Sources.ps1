[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Add')]
    [string]$Add,
    [Parameter(Mandatory = $true, ParameterSetName = 'Remove')]
    [string]$Remove,
    [string]$ExpectedUserSid,
    [string]$TestRoot,
    [ValidateRange(0, 4)]
    [int]$TestFailAfterPublish = 0,
    [ValidateSet('', 'JournalPrepared', 'Publish1', 'Publish2', 'Publish3', 'Publish4', 'NewVerified', 'Undo1', 'Undo2', 'Undo3', 'Undo4', 'JournalDeleted')]
    [string]$TestPauseAfter = '',
    [string]$TestPauseSentinelPath,
    [switch]$TestAllowElevatedProcess
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$trustedModuleRoot = [IO.Path]::Combine([Environment]::SystemDirectory, 'WindowsPowerShell', 'v1.0', 'Modules')
$env:PSModulePath = $trustedModuleRoot

$productName = 'ResticBackuper'
$programFilesRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$programDataRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$defaultInstallRoot = [IO.Path]::GetFullPath((Join-Path $programFilesRoot $productName)).TrimEnd('\')
$isTestMode = -not [string]::IsNullOrWhiteSpace($TestRoot)
if ($isTestMode) {
    $testContainer = [IO.Path]::GetFullPath($TestRoot).TrimEnd('\')
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $temporaryPrefix = $temporaryRoot + '\'
    if ($testContainer -eq $temporaryRoot -or
        -not $testContainer.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The disposable test root must be a child of the current user temporary directory.'
    }
    $installRoot = [IO.Path]::GetFullPath((Join-Path $testContainer "ProgramFiles\$productName")).TrimEnd('\')
    $stateRoot = [IO.Path]::GetFullPath((Join-Path $testContainer "ProgramData\$productName")).TrimEnd('\')
}
else {
    $installRoot = $defaultInstallRoot
    $stateRoot = [IO.Path]::GetFullPath((Join-Path $programDataRoot $productName)).TrimEnd('\')
}
$configPath = Join-Path $installRoot 'backup-config.json'
$runtimeManifestPath = Join-Path $installRoot 'runtime-manifest.json'
$lockPath = Join-Path $stateRoot 'run.lock'
$journalPath = Join-Path $stateRoot 'source-update.journal.json'
$journalSchemaVersion = 1
$journalKind = 'resticbackuper-source-update-undo'
$journalMaximumBytes = 24MB
$journalTargetMaximumBytes = 4MB
$lockStream = $null
$currentSid = $null
$Action = $PSCmdlet.ParameterSetName

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

function New-ProtectedJournalFileSecurity {
    param([string]$UserSid)
    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $allow = [Security.AccessControl.AccessControlType]::Allow
    foreach ($sidText in @('S-1-5-18', 'S-1-5-32-544')) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidText),
            [Security.AccessControl.FileSystemRights]::FullControl,
            $allow
        ))
    }
    foreach ($sidText in @($UserSid, 'S-1-3-4')) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidText),
            [Security.AccessControl.FileSystemRights]::ReadAndExecute,
            $allow
        ))
    }
    return $security
}

function Assert-ProtectedSourceUpdateAcl {
    param([string]$Path, [string]$UserSid, [switch]$AllowInheritedRules)
    $acl = Microsoft.PowerShell.Security\Get-Acl -LiteralPath $Path
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($owner -ne 'S-1-5-32-544' -or
        (-not $AllowInheritedRules -and -not $acl.AreAccessRulesProtected)) {
        throw "Protected source-update path has an unsafe owner or inherited ACL: $Path"
    }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', $UserSid, 'S-1-3-4')
    $dangerous = [Security.AccessControl.FileSystemRights]::WriteData -bor `
        [Security.AccessControl.FileSystemRights]::AppendData -bor `
        [Security.AccessControl.FileSystemRights]::WriteAttributes -bor `
        [Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor `
        [Security.AccessControl.FileSystemRights]::Delete -bor `
        [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor `
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor `
        [Security.AccessControl.FileSystemRights]::TakeOwnership
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        $sid = $rule.IdentityReference.Value
        if ((-not $AllowInheritedRules -and $rule.IsInherited) -or
            $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $sid -notin $allowed) {
            throw "Protected source-update path has an unexpected ACL entry for $sid`: $Path"
        }
        if ($sid -in @('S-1-5-18', 'S-1-5-32-544')) {
            if (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne
                [Security.AccessControl.FileSystemRights]::FullControl) {
                throw "Protected source-update path lacks full control for $sid`: $Path"
            }
        }
        elseif (($rule.FileSystemRights -band $dangerous) -ne 0 -or
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::ReadAndExecute) -ne
                [Security.AccessControl.FileSystemRights]::ReadAndExecute) {
            throw "Protected source-update path grants unsafe or insufficient rights to $sid`: $Path"
        }
        [void]$seen.Add($sid)
    }
    foreach ($requiredSid in $allowed) {
        if (-not $seen.Contains($requiredSid)) {
            throw "Protected source-update path ACL is missing $requiredSid`: $Path"
        }
    }
}

function Assert-ExactProperties {
    param([object]$Object, [string[]]$Names, [string]$Description)
    if ($null -eq $Object -or $Object -isnot [Management.Automation.PSCustomObject]) {
        throw "$Description must be one JSON object."
    }
    $actual = @($Object.PSObject.Properties.Name | Sort-Object)
    $expected = @($Names | Sort-Object)
    if (($actual -join "`n") -ne ($expected -join "`n")) {
        throw "$Description has an invalid schema."
    }
}

function Invoke-TestPauseBoundary {
    param([string]$Boundary)
    if ([string]::IsNullOrWhiteSpace($TestPauseAfter) -or
        -not [string]::Equals($TestPauseAfter, $Boundary, [StringComparison]::OrdinalIgnoreCase)) {
        return
    }
    if (-not $isTestMode) {
        throw 'Abrupt-boundary pause injection is available only for disposable tests.'
    }
    $sentinel = [IO.Path]::GetFullPath($TestPauseSentinelPath)
    if (-not (Test-IsWithin -Candidate $sentinel -Parent $testContainer) -or
        (Test-PathEqual -Left $sentinel -Right $testContainer)) {
        throw 'The disposable pause sentinel must be a file below the test root.'
    }
    $parent = Split-Path -Parent $sentinel
    Assert-NormalDirectoryChain -Directory $parent
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes("$Boundary`n")
    $stream = [IO.File]::Open($sentinel, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
    while ($true) {
        [Threading.Thread]::Sleep(1000)
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

function Set-JsonProperty {
    param([object]$Object, [string]$Name, [object]$Value)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $property.Value = $Value
    }
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
    $text = ($Value | ConvertTo-Json -Depth 20) + "`n"
    return [Text.UTF8Encoding]::new($false).GetBytes($text)
}

function Read-JsonObject {
    param([string]$File)
    Assert-NormalFile -File $File
    $rawBytes = [IO.File]::ReadAllBytes($File)
    $rawText = [Text.UTF8Encoding]::new($false, $true).GetString($rawBytes)
    $value = $rawText | ConvertFrom-Json
    if ($null -eq $value -or $value -isnot [Management.Automation.PSCustomObject]) {
        throw "Expected one JSON object: $File"
    }
    return [pscustomobject]@{ Json = $value; RawBytes = $rawBytes }
}

function New-StagedFileBytes {
    param([string]$Target, [byte[]]$Bytes, [string]$StagedPath, [string]$UserSid)
    Assert-NormalFile -File $Target
    $parent = Split-Path -Parent $Target
    Assert-NormalDirectoryChain -Directory $parent
    $temporary = [IO.Path]::GetFullPath($StagedPath)
    if (-not (Test-PathEqual -Left (Split-Path -Parent $temporary) -Right $parent) -or
        [IO.File]::Exists($temporary) -or [IO.Directory]::Exists($temporary)) {
        throw "The derived source-update staging path is unsafe or already exists: $temporary"
    }
    $stream = $null
    try {
        if ($isTestMode) {
            $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        }
        else {
            # Give the staged file its final protected owner/DACL before its
            # name can atomically replace a protected target. This avoids even
            # a transient requester-owned target after ReplaceFile.
            $stream = [IO.FileStream]::new(
                $temporary,
                [IO.FileMode]::CreateNew,
                [Security.AccessControl.FileSystemRights]::Write,
                [IO.FileShare]::None,
                4096,
                [IO.FileOptions]::WriteThrough,
                (New-ProtectedJournalFileSecurity -UserSid $UserSid)
            )
        }
        $stream.Write($Bytes, 0, $Bytes.Length)
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null
        if (-not $isTestMode) {
            Assert-ProtectedSourceUpdateAcl -Path $temporary -UserSid $UserSid
        }
        return $temporary
    }
    catch {
        if ($null -ne $stream) { $stream.Dispose() }
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

function Publish-StagedFile {
    param([string]$Staged, [string]$Target)
    $replaceMethod = [IO.File].GetMethod('Replace', [type[]]@([string], [string], [string]))
    if ($null -eq $replaceMethod) {
        throw 'The required atomic file replacement API is unavailable.'
    }
    [void]$replaceMethod.Invoke($null, [object[]]@($Staged, $Target, $null))
}

function Sync-ReplacedFileToDisk {
    param([string]$Target)
    # File.Replace has no supported write-through flag. Flush the resulting
    # target handle before a later journal deletion may commit the transaction,
    # including when RecoveryTools is on a different volume.
    $stream = [IO.File]::Open(
        $Target,
        [IO.FileMode]::Open,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::Read
    )
    try {
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}

function Set-AtomicFileBytes {
    param([string]$Target, [byte[]]$Bytes, [string]$StagedPath, [string]$UserSid)
    $staged = New-StagedFileBytes -Target $Target -Bytes $Bytes -StagedPath $StagedPath -UserSid $UserSid
    try {
        Publish-StagedFile -Staged $staged -Target $Target
        Sync-ReplacedFileToDisk -Target $Target
        if (-not $isTestMode) {
            Assert-ProtectedSourceUpdateAcl -Path $Target -UserSid $UserSid
        }
        $staged = $null
    }
    finally {
        if ($null -ne $staged -and (Test-Path -LiteralPath $staged)) {
            Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-SourceUpdateStagePath {
    param([string]$Target, [string]$TransactionId, [string]$TargetId, [string]$Phase)
    return Join-Path (Split-Path -Parent $Target) `
        ('.{0}.source-update.{1}.{2}.{3}.tmp' -f ([IO.Path]::GetFileName($Target)), $TransactionId, $TargetId, $Phase)
}

function New-JournalByteRecord {
    param([byte[]]$Bytes)
    if ($Bytes.Length -le 0 -or $Bytes.Length -gt $journalTargetMaximumBytes) {
        throw "A source-update target is outside the supported size range: $($Bytes.Length) bytes."
    }
    return [ordered]@{
        length = [long]$Bytes.Length
        sha256 = Get-Sha256Hex -Bytes $Bytes
        bytes_base64 = [Convert]::ToBase64String($Bytes)
    }
}

function ConvertFrom-JournalByteRecord {
    param([object]$Record, [string]$Description)
    Assert-ExactProperties -Object $Record -Names @('length', 'sha256', 'bytes_base64') -Description $Description
    if (($Record.length -isnot [int] -and $Record.length -isnot [long]) -or
        [long]$Record.length -le 0 -or
        [long]$Record.length -gt $journalTargetMaximumBytes) {
        throw "$Description has an invalid byte length."
    }
    if ($Record.sha256 -isnot [string] -or [string]$Record.sha256 -notmatch '^[0-9a-f]{64}$' -or
        $Record.bytes_base64 -isnot [string] -or
        ([string]$Record.bytes_base64).Length % 4 -ne 0 -or
        [string]$Record.bytes_base64 -notmatch '^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$') {
        throw "$Description has invalid hash or Base64 data."
    }
    try {
        [byte[]]$bytes = [Convert]::FromBase64String([string]$Record.bytes_base64)
    }
    catch {
        throw "$Description contains invalid Base64 data."
    }
    if ([Convert]::ToBase64String($bytes) -ne [string]$Record.bytes_base64 -or
        $bytes.Length -ne [long]$Record.length -or
        -not [string]::Equals((Get-Sha256Hex -Bytes $bytes), [string]$Record.sha256, [StringComparison]::Ordinal)) {
        throw "$Description bytes do not match their declared length and SHA-256."
    }
    return $bytes
}

function ConvertFrom-StrictJsonBytes {
    param([byte[]]$Bytes, [string]$Description)
    try {
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
        $value = $text | ConvertFrom-Json
    }
    catch {
        throw "$Description is not valid UTF-8 JSON."
    }
    if ($null -eq $value -or $value -isnot [Management.Automation.PSCustomObject]) {
        throw "$Description must be one JSON object."
    }
    return $value
}

function Get-SourceUpdateTargetPaths {
    param([byte[]]$OldProtectedConfigBytes, [byte[]]$NewProtectedConfigBytes)
    $oldConfig = ConvertFrom-StrictJsonBytes -Bytes $OldProtectedConfigBytes `
        -Description 'Old protected configuration in the source-update journal'
    $newConfig = ConvertFrom-StrictJsonBytes -Bytes $NewProtectedConfigBytes `
        -Description 'New protected configuration in the source-update journal'
    foreach ($configuration in @($oldConfig, $newConfig)) {
        foreach ($name in @('repository', 'repository_volume_serial', 'recovery_tools_directory', 'state_directory')) {
            [void](Get-RequiredProperty -Object $configuration -Name $name)
        }
    }
    Assert-NonSourceFieldsPreserved -Before $oldConfig -After $newConfig `
        -Message 'The source-update journal changes a protected non-source configuration field.'
    $repository = Get-CanonicalLocalPath -Value ([string]$oldConfig.repository)
    $configuredState = Get-CanonicalLocalPath -Value ([string]$oldConfig.state_directory)
    if (-not (Test-PathEqual -Left $configuredState -Right $stateRoot)) {
        throw 'The source-update journal names an unexpected protected state directory.'
    }
    if ($oldConfig.repository_volume_serial -isnot [string] -or
        [string]$oldConfig.repository_volume_serial -notmatch '^[0-9A-Fa-f]{8}$') {
        throw 'The source-update journal contains an invalid repository volume serial.'
    }
    $repositoryParent = Split-Path -Parent $repository
    if ([string]::IsNullOrWhiteSpace($repositoryParent)) {
        throw 'The source-update journal repository must not be a drive root.'
    }
    $recoveryTools = Get-CanonicalLocalPath -Value (Join-Path $repositoryParent 'RecoveryTools')
    $configuredRecovery = Get-CanonicalLocalPath -Value ([string]$oldConfig.recovery_tools_directory)
    if (-not (Test-PathEqual -Left $configuredRecovery -Right $recoveryTools)) {
        throw 'The source-update journal does not name the repository-sibling RecoveryTools directory.'
    }
    foreach ($directory in @($repository, $recoveryTools)) {
        Assert-NormalDirectoryChain -Directory $directory
    }
    Assert-NormalFile -File (Join-Path $repository 'config')
    $repositoryDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($repository))
    $recoveryDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($recoveryTools))
    if (-not $repositoryDrive.IsReady -or
        $repositoryDrive.DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable) -or
        -not $recoveryDrive.IsReady -or
        -not (Test-PathEqual -Left $repositoryDrive.RootDirectory.FullName -Right $recoveryDrive.RootDirectory.FullName)) {
        throw 'The source-update journal RecoveryTools target is not on the ready local repository volume.'
    }
    if (-not $isTestMode) {
        $actualSerial = Get-LocalVolumeSerialHex -Directory $repository
        if (-not [string]::Equals($actualSerial, [string]$oldConfig.repository_volume_serial, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The source-update journal repository volume serial does not match the currently mounted volume.'
        }
    }
    return [ordered]@{
        protected_config = $configPath
        runtime_manifest = $runtimeManifestPath
        recovery_config = Join-Path $recoveryTools 'backup-config.json'
        recovery_manifest = Join-Path $recoveryTools 'recovery-manifest.json'
    }
}

function Assert-FileMatchesBytes {
    param([string]$File, [byte[]]$Bytes, [string]$Description)
    Assert-NormalFile -File $File
    $item = Get-Item -LiteralPath $File -Force
    if ($item.Length -ne $Bytes.Length -or
        -not [string]::Equals((Get-FileSha256Hex -File $File), (Get-Sha256Hex -Bytes $Bytes), [StringComparison]::Ordinal)) {
        throw "$Description does not match its journaled bytes: $File"
    }
}

function Write-SourceUpdateJournal {
    param([byte[]]$Bytes, [string]$UserSid)
    if ($Bytes.Length -le 0 -or $Bytes.Length -gt $journalMaximumBytes) {
        throw "The source-update journal is outside its supported size range: $($Bytes.Length) bytes."
    }
    if ([IO.File]::Exists($journalPath) -or [IO.Directory]::Exists($journalPath)) {
        throw 'A source-update recovery journal already exists.'
    }
    $stream = $null
    try {
        if ($isTestMode) {
            $stream = [IO.File]::Open($journalPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        }
        else {
            $stream = [IO.FileStream]::new(
                $journalPath,
                [IO.FileMode]::CreateNew,
                [Security.AccessControl.FileSystemRights]::Write,
                [IO.FileShare]::Read,
                4096,
                [IO.FileOptions]::WriteThrough,
                (New-ProtectedJournalFileSecurity -UserSid $UserSid)
            )
        }
        $stream.Write($Bytes, 0, $Bytes.Length)
        $stream.Flush($true)
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
    Assert-NormalFile -File $journalPath
    if (-not $isTestMode) {
        Assert-ProtectedSourceUpdateAcl -Path $journalPath -UserSid $UserSid
    }
}

function Get-ValidatedSourceUpdateJournal {
    param([string]$UserSid)
    Assert-NormalFile -File $journalPath
    if (-not $isTestMode) {
        Assert-ProtectedSourceUpdateAcl -Path $stateRoot -UserSid $UserSid
        Assert-ProtectedSourceUpdateAcl -Path $journalPath -UserSid $UserSid
    }
    $journalItem = Get-Item -LiteralPath $journalPath -Force
    if ($journalItem.Length -le 0 -or $journalItem.Length -gt $journalMaximumBytes) {
        throw 'The source-update recovery journal has an invalid size.'
    }
    [byte[]]$rawBytes = [IO.File]::ReadAllBytes($journalPath)
    $journal = ConvertFrom-StrictJsonBytes -Bytes $rawBytes -Description 'Source-update recovery journal'
    Assert-ExactProperties -Object $journal `
        -Names @('schema_version', 'transaction_kind', 'transaction_id', 'requester_sid', 'targets') `
        -Description 'Source-update recovery journal'
    if (($journal.schema_version -isnot [int] -and $journal.schema_version -isnot [long]) -or
        [long]$journal.schema_version -ne $journalSchemaVersion -or
        $journal.transaction_kind -isnot [string] -or
        -not [string]::Equals([string]$journal.transaction_kind, $journalKind, [StringComparison]::Ordinal) -or
        $journal.transaction_id -isnot [string] -or
        [string]$journal.transaction_id -notmatch '^[0-9a-f]{32}$' -or
        $journal.requester_sid -isnot [string] -or
        -not [string]::Equals([string]$journal.requester_sid, $UserSid, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The source-update recovery journal header is invalid.'
    }
    $targetIds = @('protected_config', 'runtime_manifest', 'recovery_config', 'recovery_manifest')
    $records = @($journal.targets)
    if ($records.Count -ne $targetIds.Count) {
        throw 'The source-update recovery journal must contain exactly four fixed targets.'
    }
    $entries = [ordered]@{}
    for ($index = 0; $index -lt $targetIds.Count; $index++) {
        $record = $records[$index]
        Assert-ExactProperties -Object $record -Names @('id', 'old', 'new') `
            -Description "Source-update target record $($index + 1)"
        $expectedId = $targetIds[$index]
        if ($record.id -isnot [string] -or
            -not [string]::Equals([string]$record.id, $expectedId, [StringComparison]::Ordinal)) {
            throw "The source-update recovery journal target order or ID is invalid at position $($index + 1)."
        }
        [byte[]]$oldBytes = ConvertFrom-JournalByteRecord -Record $record.old -Description "$expectedId old record"
        [byte[]]$newBytes = ConvertFrom-JournalByteRecord -Record $record.new -Description "$expectedId new record"
        $entries[$expectedId] = [pscustomobject]@{ OldBytes = $oldBytes; NewBytes = $newBytes }
    }
    $paths = Get-SourceUpdateTargetPaths `
        -OldProtectedConfigBytes ([byte[]]$entries['protected_config'].OldBytes) `
        -NewProtectedConfigBytes ([byte[]]$entries['protected_config'].NewBytes)
    foreach ($targetId in $targetIds) {
        $target = [string]$paths[$targetId]
        Assert-NormalDirectoryChain -Directory (Split-Path -Parent $target)
        Assert-NormalFile -File $target
        if (-not $isTestMode) {
            Assert-ProtectedSourceUpdateAcl -Path $target -UserSid $UserSid -AllowInheritedRules
        }
        $currentLength = (Get-Item -LiteralPath $target -Force).Length
        $currentHash = Get-FileSha256Hex -File $target
        $oldBytes = [byte[]]$entries[$targetId].OldBytes
        $newBytes = [byte[]]$entries[$targetId].NewBytes
        $matchesOld = $currentLength -eq $oldBytes.Length -and
            [string]::Equals($currentHash, (Get-Sha256Hex -Bytes $oldBytes), [StringComparison]::Ordinal)
        $matchesNew = $currentLength -eq $newBytes.Length -and
            [string]::Equals($currentHash, (Get-Sha256Hex -Bytes $newBytes), [StringComparison]::Ordinal)
        if (-not $matchesOld -and -not $matchesNew) {
            throw "Source-update target is neither its exact OLD nor NEW version: $targetId"
        }
        $entries[$targetId] | Add-Member -NotePropertyName Path -NotePropertyValue $target
    }
    return [pscustomobject]@{
        TransactionId = [string]$journal.transaction_id
        Entries = $entries
        TargetIds = $targetIds
    }
}

function Remove-DerivedTransactionStagingFiles {
    param([pscustomobject]$Journal)
    foreach ($targetId in $Journal.TargetIds) {
        $target = [string]$Journal.Entries[$targetId].Path
        foreach ($phase in @('forward', 'undo')) {
            $staged = Get-SourceUpdateStagePath -Target $target -TransactionId $Journal.TransactionId `
                -TargetId $targetId -Phase $phase
            if ([IO.Directory]::Exists($staged)) {
                throw "A derived source-update staging path is unexpectedly a directory: $staged"
            }
            if ([IO.File]::Exists($staged)) {
                Assert-NormalFile -File $staged
                [IO.File]::Delete($staged)
            }
        }
    }
}

function Invoke-SourceUpdateRecovery {
    param([string]$UserSid)
    if (-not [IO.File]::Exists($journalPath)) {
        if ([IO.Directory]::Exists($journalPath)) {
            throw 'The fixed source-update journal path is unexpectedly a directory.'
        }
        return $false
    }
    $journal = Get-ValidatedSourceUpdateJournal -UserSid $UserSid
    $undoCount = 0
    foreach ($targetId in $journal.TargetIds) {
        $entry = $journal.Entries[$targetId]
        $staged = Get-SourceUpdateStagePath -Target $entry.Path -TransactionId $journal.TransactionId `
            -TargetId $targetId -Phase 'undo'
        if ([IO.File]::Exists($staged)) {
            Assert-NormalFile -File $staged
            [IO.File]::Delete($staged)
        }
        elseif ([IO.Directory]::Exists($staged)) {
            throw "A derived undo staging path is unexpectedly a directory: $staged"
        }
        Set-AtomicFileBytes -Target $entry.Path -Bytes ([byte[]]$entry.OldBytes) -StagedPath $staged -UserSid $UserSid
        Assert-FileMatchesBytes -File $entry.Path -Bytes ([byte[]]$entry.OldBytes) `
            -Description "Restored OLD $targetId"
        $undoCount++
        Invoke-TestPauseBoundary -Boundary "Undo$undoCount"
    }
    Remove-DerivedTransactionStagingFiles -Journal $journal
    foreach ($targetId in $journal.TargetIds) {
        $entry = $journal.Entries[$targetId]
        Assert-FileMatchesBytes -File $entry.Path -Bytes ([byte[]]$entry.OldBytes) `
            -Description "Fully restored OLD $targetId"
    }
    $restored = Get-ValidatedConfiguration -File $configPath -AllowOfflineNonCanary
    [void](Get-ValidatedRuntimeManifest -Validated $restored)
    [void](Get-ValidatedRecoveryBundle -Validated $restored)
    if (-not $isTestMode) {
        Assert-ProtectedSourceUpdateAcl -Path $journalPath -UserSid $UserSid
    }
    [IO.File]::Delete($journalPath)
    if ([IO.File]::Exists($journalPath) -or [IO.Directory]::Exists($journalPath)) {
        throw 'The source-update recovery journal could not be deleted after full OLD-state verification.'
    }
    return $true
}

function Invoke-AtomicFileSet {
    param(
        [Collections.IDictionary]$Updates,
        [scriptblock]$Verify,
        [int]$FailAfterPublish = 0,
        [string]$UserSid
    )
    $targetIds = @('protected_config', 'runtime_manifest', 'recovery_config', 'recovery_manifest')
    if ($Updates.Count -ne $targetIds.Count -or
        (($Updates.Keys | ForEach-Object { [string]$_ }) -join "`n") -ne ($targetIds -join "`n")) {
        throw 'A source update must provide exactly the four fixed targets in canonical order.'
    }
    [byte[]]$oldProtectedConfig = [IO.File]::ReadAllBytes($configPath)
    $paths = Get-SourceUpdateTargetPaths -OldProtectedConfigBytes $oldProtectedConfig `
        -NewProtectedConfigBytes ([byte[]]$Updates['protected_config'])
    $transactionId = [Guid]::NewGuid().ToString('N')
    $records = [Collections.Generic.List[object]]::new()
    foreach ($targetId in $targetIds) {
        $target = [string]$paths[$targetId]
        Assert-NormalFile -File $target
        if (-not $isTestMode) {
            Assert-ProtectedSourceUpdateAcl -Path $target -UserSid $UserSid -AllowInheritedRules
        }
        [byte[]]$oldBytes = [IO.File]::ReadAllBytes($target)
        [byte[]]$newBytes = [byte[]]$Updates[$targetId]
        $records.Add([ordered]@{
            id = $targetId
            old = New-JournalByteRecord -Bytes $oldBytes
            new = New-JournalByteRecord -Bytes $newBytes
        })
    }
    $journalDocument = [ordered]@{
        schema_version = $journalSchemaVersion
        transaction_kind = $journalKind
        transaction_id = $transactionId
        requester_sid = $UserSid
        targets = @($records)
    }
    [byte[]]$journalBytes = ConvertTo-Utf8JsonBytes -Value $journalDocument
    Write-SourceUpdateJournal -Bytes $journalBytes -UserSid $UserSid
    [void](Get-ValidatedSourceUpdateJournal -UserSid $UserSid)
    Invoke-TestPauseBoundary -Boundary 'JournalPrepared'
    try {
        $publishCount = 0
        foreach ($targetId in $targetIds) {
            $target = [string]$paths[$targetId]
            $staged = Get-SourceUpdateStagePath -Target $target -TransactionId $transactionId `
                -TargetId $targetId -Phase 'forward'
            Set-AtomicFileBytes -Target $target -Bytes ([byte[]]$Updates[$targetId]) -StagedPath $staged -UserSid $UserSid
            Assert-FileMatchesBytes -File $target -Bytes ([byte[]]$Updates[$targetId]) `
                -Description "Published NEW $targetId"
            $publishCount++
            Invoke-TestPauseBoundary -Boundary "Publish$publishCount"
            if ($FailAfterPublish -gt 0 -and $publishCount -eq $FailAfterPublish) {
                throw "Injected disposable-test failure after publishing $publishCount files."
            }
        }
        & $Verify
        Invoke-TestPauseBoundary -Boundary 'NewVerified'
        [IO.File]::Delete($journalPath)
        if ([IO.File]::Exists($journalPath) -or [IO.Directory]::Exists($journalPath)) {
            throw 'The source-update journal could not be deleted to commit the verified NEW state.'
        }
        Invoke-TestPauseBoundary -Boundary 'JournalDeleted'
    }
    finally {
        foreach ($targetId in $targetIds) {
            $target = [string]$paths[$targetId]
            foreach ($phase in @('forward', 'undo')) {
                $staged = Get-SourceUpdateStagePath -Target $target -TransactionId $transactionId `
                    -TargetId $targetId -Phase $phase
                if ([IO.File]::Exists($staged)) {
                    [IO.File]::Delete($staged)
                }
            }
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

function Assert-SourceDrive {
    param([string]$Directory, [bool]$UseVss)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Directory))
    if (-not $drive.IsReady -or $drive.DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable)) {
        throw "A source must be on a ready local fixed or removable drive: $Directory"
    }
    if ($UseVss -and ($drive.DriveType -ne [IO.DriveType]::Fixed -or
        -not [string]::Equals($drive.DriveFormat, 'NTFS', [StringComparison]::OrdinalIgnoreCase))) {
        throw "VSS is enabled, so every source must be on a local fixed NTFS volume: $Directory"
    }
}

function Get-LocalVolumeSerialHex {
    param([string]$Directory)
    $root = [IO.Path]::GetPathRoot($Directory)
    if ($root -notmatch '^[A-Za-z]:\\$') {
        throw "Cannot read a volume serial for a non-drive-letter path: $Directory"
    }
    $deviceId = $root.Substring(0, 2).ToUpperInvariant()
    $disk = [Management.ManagementObject]::new("Win32_LogicalDisk.DeviceID='$deviceId'")
    try {
        $disk.Get()
        $serial = [string]$disk['VolumeSerialNumber']
    }
    finally {
        $disk.Dispose()
    }
    if ($serial -notmatch '^[0-9A-Fa-f]{8}$') {
        throw "Windows returned an invalid volume serial for $root"
    }
    return $serial.ToUpperInvariant()
}

function Assert-SourceListsEqual {
    param([string[]]$Expected, [object[]]$Actual, [string]$Message)
    if ($Expected.Count -ne $Actual.Count) { throw $Message }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ($Actual[$index] -isnot [string] -or
            -not (Test-PathEqual -Left $Expected[$index] -Right ([string]$Actual[$index]))) {
            throw $Message
        }
    }
}

function Get-ValidatedConfiguration {
    param(
        [string]$File,
        [string]$RemovalCandidate,
        [switch]$AllowOfflineNonCanary
    )
    $loaded = Read-JsonObject -File $File
    $configuration = $loaded.Json
    if ((Get-RequiredProperty -Object $configuration -Name 'schema_version') -ne 1) {
        throw 'Unsupported backup configuration schema.'
    }
    foreach ($name in @(
        'repository', 'repository_volume_serial', 'restic_executable', 'recovery_tools_directory',
        'python_executable', 'state_directory', 'secret_file', 'recovery_key_file', 'exclude_file',
        'canary_file', 'hostname', 'scheduled_tag', 'sources', 'use_vss'
    )) { [void](Get-RequiredProperty -Object $configuration -Name $name) }

    $repository = Get-CanonicalLocalPath -Value ([string]$configuration.repository)
    $configuredState = Get-CanonicalLocalPath -Value ([string]$configuration.state_directory)
    $resticExecutable = Get-CanonicalLocalPath -Value ([string]$configuration.restic_executable)
    $pythonExecutable = Get-CanonicalLocalPath -Value ([string]$configuration.python_executable)
    $excludeFile = Get-CanonicalLocalPath -Value ([string]$configuration.exclude_file)
    $secretFile = Get-CanonicalLocalPath -Value ([string]$configuration.secret_file)
    $canaryFile = Get-CanonicalLocalPath -Value ([string]$configuration.canary_file)
    $recoveryTools = Get-CanonicalLocalPath -Value ([string]$configuration.recovery_tools_directory)
    [void](Get-CanonicalLocalPath -Value ([string]$configuration.recovery_key_file))

    $expectedCanary = Join-Path $stateRoot 'Canary\backup-canary.txt'
    foreach ($pair in @(
        @($configuredState, $stateRoot, 'state directory'),
        @($resticExecutable, (Join-Path $installRoot 'restic.exe'), 'Restic executable'),
        @($pythonExecutable, (Join-Path $installRoot 'Python\python.exe'), 'Python executable'),
        @($excludeFile, (Join-Path $installRoot 'excludes.txt'), 'exclude file'),
        @($secretFile, (Join-Path $stateRoot 'repository-password.dpapi.json'), 'DPAPI secret file'),
        @($canaryFile, $expectedCanary, 'canary file')
    )) {
        if (-not (Test-PathEqual -Left $pair[0] -Right $pair[1])) {
            throw "Configuration names an unexpected protected $($pair[2]): $($pair[0])"
        }
    }
    if ($configuration.repository_volume_serial -isnot [string] -or
        $configuration.repository_volume_serial -notmatch '^[0-9A-Fa-f]{8}$') {
        throw 'Repository volume serial is invalid.'
    }
    if ($configuration.hostname -isnot [string] -or [string]::IsNullOrWhiteSpace($configuration.hostname) -or
        $configuration.scheduled_tag -isnot [string] -or [string]::IsNullOrWhiteSpace($configuration.scheduled_tag)) {
        throw 'Configured hostname or scheduled tag is invalid.'
    }
    if ($configuration.use_vss -isnot [bool]) {
        throw 'Configured use_vss value must be a JSON Boolean.'
    }

    $repositoryParent = Split-Path -Parent $repository
    if ([string]::IsNullOrWhiteSpace($repositoryParent)) { throw 'The repository must not be a drive root.' }
    $expectedRecoveryTools = Get-CanonicalLocalPath -Value (Join-Path $repositoryParent 'RecoveryTools')
    if (-not (Test-PathEqual -Left $recoveryTools -Right $expectedRecoveryTools)) {
        throw "Configuration names an unexpected recovery-tools directory: $recoveryTools"
    }
    foreach ($directory in @($installRoot, $stateRoot, $repository, $recoveryTools)) {
        Assert-NormalDirectoryChain -Directory $directory
    }
    foreach ($normalFile in @(
        (Join-Path $repository 'config'), $resticExecutable, $pythonExecutable, $excludeFile, $canaryFile
    )) { Assert-NormalFile -File $normalFile }
    Assert-NormalDirectoryChain -Directory (Split-Path -Parent $canaryFile)

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
        if ($value -isnot [string]) { throw 'Every configured source must be a path string.' }
        $source = Get-CanonicalLocalPath -Value ([string]$value)
        if (-not $sourceSet.Add($source)) { throw "Duplicate source in backup configuration: $source" }
        $sources.Add($source)
    }

    $canarySource = Get-CanonicalLocalPath -Value (Split-Path -Parent $canaryFile)
    $canaryMatches = @($sources | Where-Object { Test-PathEqual -Left $_ -Right $canarySource })
    if ($canaryMatches.Count -ne 1) {
        throw 'The exact protected canary directory must occur once in configured sources.'
    }
    if (-not [string]::IsNullOrWhiteSpace($RemovalCandidate)) {
        $removalMatches = @($sources | Where-Object { Test-PathEqual -Left $_ -Right $RemovalCandidate })
        if ($removalMatches.Count -ne 1) {
            throw "Removal path is not an exact configured source: $RemovalCandidate"
        }
    }

    $remaining = @(
        $sources | Where-Object {
            [string]::IsNullOrWhiteSpace($RemovalCandidate) -or
            -not (Test-PathEqual -Left $_ -Right $RemovalCandidate)
        }
    )
    $validatedRemaining = [Collections.Generic.List[string]]::new()
    foreach ($source in $remaining) {
        $isCanarySource = Test-PathEqual -Left $source -Right $canarySource
        $sourceIsOnline = Test-Path -LiteralPath $source -PathType Container
        if (-not $sourceIsOnline -and ($isCanarySource -or -not $AllowOfflineNonCanary)) {
            throw "Configured source directory does not exist or is offline: $source"
        }
        if ($sourceIsOnline) {
            Assert-NormalDirectoryChain -Directory $source
            Assert-SourceDrive -Directory $source -UseVss ([bool]$configuration.use_vss)
        }
        foreach ($existing in $validatedRemaining) {
            Assert-NoOverlap -Candidate $source -Other $existing -Message 'Configured sources contain one another'
        }
        Assert-NoOverlap -Candidate $source -Other $repository -Message 'Source overlaps the repository'
        if (-not $isCanarySource) {
            foreach ($protected in @($installRoot, $stateRoot, $recoveryTools)) {
                Assert-NoOverlap -Candidate $source -Other $protected -Message 'User source overlaps a protected backup location'
            }
        }
        $validatedRemaining.Add($source)
    }
    return [pscustomobject]@{
        Json = $configuration
        RawBytes = $loaded.RawBytes
        Repository = $repository
        RecoveryTools = $recoveryTools
        CanarySource = $canarySource
        Sources = @($sources)
        UseVss = [bool]$configuration.use_vss
    }
}

function Get-ValidatedRuntimeManifest {
    param([pscustomobject]$Validated)
    $loaded = Read-JsonObject -File $runtimeManifestPath
    $manifest = $loaded.Json
    if ($manifest.schema_version -ne 1) { throw 'Runtime manifest schema is invalid.' }
    $records = @($manifest.files)
    if ([long]$manifest.file_count -ne $records.Count) { throw 'Runtime manifest file count is invalid.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prefix = $installRoot + '\'
    foreach ($record in $records) {
        if ($null -eq $record -or $record.relative_path -isnot [string]) {
            throw 'Runtime manifest contains an invalid file record.'
        }
        $relative = ([string]$record.relative_path).Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
            $relative.Split('\') -contains '..' -or -not $seen.Add($relative)) {
            throw "Runtime manifest contains an unsafe or duplicate path: $relative"
        }
        $file = [IO.Path]::GetFullPath((Join-Path $installRoot $relative))
        if (-not $file.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Runtime manifest path escapes the install root: $relative"
        }
        Assert-NormalFile -File $file
        $item = Get-Item -LiteralPath $file -Force
        $hash = Get-FileSha256Hex -File $file
        if ([long]$record.bytes -ne $item.Length -or
            -not [string]::Equals([string]$record.sha256, $hash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Runtime manifest mismatch: $relative"
        }
    }
    foreach ($required in @('backup-config.json', 'Manage-Sources.ps1')) {
        if (-not $seen.Contains($required)) { throw "Runtime manifest is missing required file: $required" }
    }
    $actualFiles = @(
        Get-ChildItem -LiteralPath $installRoot -Recurse -File -Force |
            Where-Object Name -ne 'runtime-manifest.json'
    )
    if ($actualFiles.Count -ne $records.Count) { throw 'Runtime contains unmanifested or missing files.' }
    foreach ($file in $actualFiles) {
        $relative = $file.FullName.Substring($prefix.Length)
        if (-not $seen.Contains($relative)) { throw "Runtime contains an unmanifested file: $relative" }
    }
    return [pscustomobject]@{ Json = $manifest; RawBytes = $loaded.RawBytes; Path = $runtimeManifestPath }
}

function Get-ValidatedRecoveryBundle {
    param([pscustomobject]$Validated)
    $payloadNames = @('restic.exe', 'restore.py', 'secret_store.py', 'backup-config.json', 'RECOVERY.md', 'restic-release.json')
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $payloadNames) { [void]$allowed.Add($name) }
    [void]$allowed.Add('recovery-manifest.json')
    $entries = @(Get-ChildItem -LiteralPath $Validated.RecoveryTools -Force)
    if ($entries.Count -ne $allowed.Count) { throw 'Recovery-tools directory is incomplete or contains unexpected entries.' }
    foreach ($entry in $entries) {
        if ($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            -not $allowed.Contains($entry.Name)) {
            throw "Recovery-tools directory contains an unsafe or unexpected entry: $($entry.Name)"
        }
    }
    foreach ($name in $allowed) { Assert-NormalFile -File (Join-Path $Validated.RecoveryTools $name) }

    $manifestPath = Join-Path $Validated.RecoveryTools 'recovery-manifest.json'
    $manifestLoaded = Read-JsonObject -File $manifestPath
    $manifest = $manifestLoaded.Json
    if ($manifest.schema_version -ne 1 -or $manifest.repository -isnot [string] -or
        -not (Test-PathEqual -Left (Get-CanonicalLocalPath -Value ([string]$manifest.repository)) -Right $Validated.Repository)) {
        throw 'Recovery manifest header is invalid or names another repository.'
    }
    $records = @($manifest.files)
    if ($records.Count -ne $payloadNames.Count) { throw 'Recovery manifest payload count is invalid.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $records) {
        if ($null -eq $record -or $record.name -isnot [string] -or
            -not $allowed.Contains([string]$record.name) -or [string]$record.name -eq 'recovery-manifest.json' -or
            -not $seen.Add([string]$record.name)) {
            throw 'Recovery manifest contains an invalid or duplicate payload record.'
        }
        $file = Join-Path $Validated.RecoveryTools ([string]$record.name)
        $item = Get-Item -LiteralPath $file -Force
        $hash = Get-FileSha256Hex -File $file
        if ([long]$record.bytes -ne $item.Length -or
            -not [string]::Equals([string]$record.sha256, $hash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Recovery manifest mismatch: $($record.name)"
        }
    }
    foreach ($name in $payloadNames) {
        if (-not $seen.Contains($name)) { throw "Recovery manifest is missing payload: $name" }
    }

    $recoveryConfigPath = Join-Path $Validated.RecoveryTools 'backup-config.json'
    $configLoaded = Read-JsonObject -File $recoveryConfigPath
    $recoveryConfig = $configLoaded.Json
    if ($recoveryConfig.schema_version -ne 1 -or $recoveryConfig.repository -isnot [string] -or
        -not (Test-PathEqual -Left (Get-CanonicalLocalPath -Value ([string]$recoveryConfig.repository)) -Right $Validated.Repository) -or
        $recoveryConfig.repository_volume_serial -isnot [string] -or
        -not [string]::Equals([string]$recoveryConfig.repository_volume_serial, [string]$Validated.Json.repository_volume_serial, [StringComparison]::OrdinalIgnoreCase) -or
        $recoveryConfig.use_vss -isnot [bool]) {
        throw 'Recovery configuration does not match the protected repository.'
    }
    Assert-SourceListsEqual -Expected $Validated.Sources -Actual @($recoveryConfig.sources) -Message 'Recovery source list is not synchronized with the protected configuration.'
    if ([bool]$recoveryConfig.use_vss -ne $Validated.UseVss) {
        throw 'Recovery VSS setting is not synchronized with the protected configuration.'
    }
    return [pscustomobject]@{
        ConfigPath = $recoveryConfigPath
        Config = $recoveryConfig
        RawConfigBytes = $configLoaded.RawBytes
        ManifestPath = $manifestPath
        Manifest = $manifest
        RawManifestBytes = $manifestLoaded.RawBytes
    }
}

function New-ManifestBytesForReplacement {
    param(
        [object]$Manifest,
        [string]$PathProperty,
        [string]$ReplacementName,
        [byte[]]$ReplacementBytes,
        [string]$Reason
    )
    $found = 0
    foreach ($record in @($Manifest.files)) {
        if ([string]::Equals([string]$record.$PathProperty, $ReplacementName, [StringComparison]::OrdinalIgnoreCase)) {
            $record.bytes = $ReplacementBytes.Length
            $record.sha256 = Get-Sha256Hex -Bytes $ReplacementBytes
            $found++
        }
    }
    if ($found -ne 1) { throw "Manifest does not contain exactly one $ReplacementName record." }
    Set-JsonProperty -Object $Manifest -Name 'created_utc' -Value ([DateTime]::UtcNow.ToString('o'))
    Set-JsonProperty -Object $Manifest -Name 'last_update_reason' -Value $Reason
    return ConvertTo-Utf8JsonBytes -Value $Manifest
}

function Assert-NonSourceFieldsPreserved {
    param([object]$Before, [object]$After, [string]$Message)
    $beforeNames = @($Before.PSObject.Properties.Name | Where-Object { $_ -ne 'sources' } | Sort-Object)
    $afterNames = @($After.PSObject.Properties.Name | Where-Object { $_ -ne 'sources' } | Sort-Object)
    if (($beforeNames -join "`n") -ne ($afterNames -join "`n")) { throw $Message }
    foreach ($name in $beforeNames) {
        $beforeText = $Before.$name | ConvertTo-Json -Depth 20 -Compress
        $afterText = $After.$name | ConvertTo-Json -Depth 20 -Compress
        if ($beforeText -ne $afterText) { throw "$Message Field: $name" }
    }
}

function Set-ConfigurationAndRecovery {
    param(
        [pscustomobject]$Validated,
        [pscustomobject]$Runtime,
        [pscustomobject]$Recovery,
        [string[]]$NewSources,
        [byte[]]$NewConfigBytes,
        [int]$FailAfterPublish
    )
    $beforeLive = ([Text.UTF8Encoding]::new($false, $true).GetString($Validated.RawBytes) | ConvertFrom-Json)
    $beforeRecovery = ([Text.UTF8Encoding]::new($false, $true).GetString($Recovery.RawConfigBytes) | ConvertFrom-Json)
    $Recovery.Config.sources = @($NewSources)
    $newRecoveryConfigBytes = ConvertTo-Utf8JsonBytes -Value $Recovery.Config
    $newRuntimeManifestBytes = New-ManifestBytesForReplacement `
        -Manifest $Runtime.Json -PathProperty 'relative_path' -ReplacementName 'backup-config.json' `
        -ReplacementBytes $NewConfigBytes -Reason 'Protected source configuration changed'
    $newRecoveryManifestBytes = New-ManifestBytesForReplacement `
        -Manifest $Recovery.Manifest -PathProperty 'name' -ReplacementName 'backup-config.json' `
        -ReplacementBytes $newRecoveryConfigBytes -Reason 'Protected source configuration changed'

    $updates = [ordered]@{
        protected_config = $NewConfigBytes
        runtime_manifest = $newRuntimeManifestBytes
        recovery_config = $newRecoveryConfigBytes
        recovery_manifest = $newRecoveryManifestBytes
    }

    $verify = {
        $finalLive = Get-ValidatedConfiguration -File $configPath -AllowOfflineNonCanary
        Assert-SourceListsEqual -Expected $NewSources -Actual $finalLive.Sources -Message 'Published protected source list differs from staged data.'
        Assert-NonSourceFieldsPreserved -Before $beforeLive -After $finalLive.Json -Message 'A non-source protected configuration field changed.'
        [void](Get-ValidatedRuntimeManifest -Validated $finalLive)
        $finalRecovery = Get-ValidatedRecoveryBundle -Validated $finalLive
        Assert-NonSourceFieldsPreserved -Before $beforeRecovery -After $finalRecovery.Config -Message 'A recovery standalone configuration field changed.'
    }
    Invoke-AtomicFileSet -Updates $updates -Verify $verify -FailAfterPublish $FailAfterPublish -UserSid $currentSid
}

try {
    $Path = if ($Action -eq 'Add') { $Add } elseif ($Action -eq 'Remove') { $Remove } else { $null }
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
        throw 'ResticBackuper source management requires 64-bit Windows PowerShell.'
    }
    if ($isTestMode) {
        if ((Test-Administrator) -and -not $TestAllowElevatedProcess) {
            throw 'Elevated disposable tests require the explicit TestAllowElevatedProcess acknowledgement.'
        }
    }
    elseif (-not (Test-Administrator)) {
        throw 'Source changes must be run elevated.'
    }
    if (-not $isTestMode -and
        ($TestFailAfterPublish -ne 0 -or -not [string]::IsNullOrWhiteSpace($TestPauseAfter) -or
            -not [string]::IsNullOrWhiteSpace($TestPauseSentinelPath) -or $TestAllowElevatedProcess)) {
        throw 'Failure and abrupt-boundary injection are available only for disposable tests.'
    }
    if ([string]::IsNullOrWhiteSpace($TestPauseAfter) -ne
        [string]::IsNullOrWhiteSpace($TestPauseSentinelPath)) {
        throw 'Disposable abrupt-boundary tests require both a pause boundary and sentinel path.'
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
    foreach ($directory in @($installRoot, $stateRoot)) { Assert-NormalDirectoryChain -Directory $directory }
    Assert-NormalFile -File $actualScript
    if (-not $isTestMode) {
        Assert-ProtectedSourceUpdateAcl -Path $stateRoot -UserSid $currentSid
    }

    if ($Action -ne 'List' -and [string]::IsNullOrWhiteSpace($Path)) {
        throw 'The add or remove path must not be empty.'
    }

    $lockStream = Enter-RunLock -File $lockPath
    [void](Invoke-SourceUpdateRecovery -UserSid $currentSid)
    $removalCandidate = if ($Action -eq 'Remove') { Get-CanonicalLocalPath -Value $Path } else { $null }
    $validated = Get-ValidatedConfiguration -File $configPath -RemovalCandidate $removalCandidate `
        -AllowOfflineNonCanary:($Action -in @('Add', 'Remove'))
    $runtime = Get-ValidatedRuntimeManifest -Validated $validated
    $recovery = Get-ValidatedRecoveryBundle -Validated $validated
    $sources = [Collections.Generic.List[string]]::new()
    foreach ($source in $validated.Sources) { $sources.Add($source) }
    $changedPath = $null
    $changed = $false

    if ($Action -eq 'Add') {
        $candidate = Get-CanonicalLocalPath -Value $Path -RequireDirectory
        Assert-NormalDirectoryChain -Directory $candidate
        Assert-SourceDrive -Directory $candidate -UseVss $validated.UseVss
        Assert-NoOverlap -Candidate $candidate -Other $validated.Repository -Message 'Source overlaps the repository'
        Assert-NoOverlap -Candidate $candidate -Other $installRoot -Message 'Source overlaps the app runtime'
        Assert-NoOverlap -Candidate $candidate -Other $stateRoot -Message 'Source overlaps the app state'
        Assert-NoOverlap -Candidate $candidate -Other $validated.RecoveryTools -Message 'Source overlaps recovery tools'
        foreach ($existing in $sources) {
            Assert-NoOverlap -Candidate $candidate -Other $existing -Message 'Source duplicates, contains, or is contained by a configured source'
        }
        $canaryIndex = -1
        for ($index = 0; $index -lt $sources.Count; $index++) {
            if (Test-PathEqual -Left $sources[$index] -Right $validated.CanarySource) {
                $canaryIndex = $index
                break
            }
        }
        if ($canaryIndex -lt 0) { throw 'The protected canary source disappeared during validation.' }
        $sources.Insert($canaryIndex, $candidate)
        $changedPath = $candidate
        $changed = $true
    }
    elseif ($Action -eq 'Remove') {
        $candidate = $removalCandidate
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
        Set-ConfigurationAndRecovery -Validated $validated -Runtime $runtime -Recovery $recovery `
            -NewSources @($sources) -NewConfigBytes $newConfigBytes -FailAfterPublish $TestFailAfterPublish
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
        required_sources = @($validated.CanarySource)
        sources = @($sources)
    } | ConvertTo-Json -Compress -Depth 5
}
catch {
    $failureMessage = $_.Exception.Message
    if ($null -ne $lockStream -and -not [string]::IsNullOrWhiteSpace($currentSid)) {
        try {
            if (Invoke-SourceUpdateRecovery -UserSid $currentSid) {
                $failureMessage = "$failureMessage The interrupted source update was rolled back and reconciled to its exact OLD state."
            }
        }
        catch {
            $failureMessage = "$failureMessage Source-update recovery also failed and the protected journal was retained: $($_.Exception.Message)"
        }
    }
    $resultAction = if ($null -ne $Action) { $Action.ToLowerInvariant() } else { $PSCmdlet.ParameterSetName.ToLowerInvariant() }
    [ordered]@{
        ok = $false
        action = $resultAction
        error = $failureMessage
    } | ConvertTo-Json -Compress -Depth 3
    exit 1
}
finally {
    if ($null -ne $lockStream) {
        Exit-RunLock -Stream $lockStream
    }
}
