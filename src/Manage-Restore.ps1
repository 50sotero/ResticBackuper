#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('list_snapshots', 'list_tree', 'restore', 'restore_drill')]
    [string]$Action,
    [string]$SnapshotId = '',
    [string]$TreePath = '',
    [string]$Target = '',
    [Parameter(Mandatory = $true)]
    [string]$IncludesBase64,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedUserSid,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$ExpectedConfigSha256,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedPlanId,
    [Parameter(Mandatory = $true)]
    [long]$ExpectedConfigGeneration,
    [Parameter(Mandatory = $true)]
    [ValidateSet('0', '1')]
    [string]$AllowLegacyUnbound,
    [Parameter(Mandatory = $true)]
    [string]$ResultPath,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$RequestNonce,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$RequestDigest,
    [string]$TestRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$trustedModuleRoot = [IO.Path]::Combine([Environment]::SystemDirectory, 'WindowsPowerShell', 'v1.0', 'Modules')
$env:PSModulePath = $trustedModuleRoot

$productName = 'ResticBackuper'
$requestDomain = 'ResticBackuper.RestoreRequest.v2'
$programFilesRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$programDataRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$localAppDataRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$isTestMode = -not [string]::IsNullOrWhiteSpace($TestRoot)
if ($isTestMode) {
    $testContainer = [IO.Path]::GetFullPath($TestRoot).TrimEnd('\')
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ($testContainer -eq $temporaryRoot -or
        -not $testContainer.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The disposable restore-manager test root must be below the current-user temporary directory.'
    }
    $installRoot = [IO.Path]::GetFullPath((Join-Path $testContainer "ProgramFiles\$productName")).TrimEnd('\')
    $stateRoot = [IO.Path]::GetFullPath((Join-Path $testContainer "ProgramData\$productName")).TrimEnd('\')
}
else {
    $installRoot = [IO.Path]::GetFullPath((Join-Path $programFilesRoot $productName)).TrimEnd('\')
    $stateRoot = [IO.Path]::GetFullPath((Join-Path $programDataRoot $productName)).TrimEnd('\')
}
$localNtfsMode = 'local_ntfs'
$driveFsMode = 'google_drivefs_stream'
$expectedDriveFsRoot = if ($isTestMode) {
    [IO.Path]::GetFullPath((Join-Path $testContainer 'DriveFs\My Drive')).TrimEnd('\')
} else { 'G:\My Drive' }
$expectedDriveFsCache = if ($isTestMode) {
    [IO.Path]::GetFullPath((Join-Path $testContainer 'LocalAppData\Google\DriveFS')).TrimEnd('\')
} else {
    [IO.Path]::GetFullPath((Join-Path $localAppDataRoot 'Google\DriveFS')).TrimEnd('\')
}
$driveFsRecoveryRoot = if ($isTestMode) {
    [IO.Path]::GetFullPath((Join-Path $testContainer 'ProgramData\ResticBackuperRecoveryTools')).TrimEnd('\')
} else {
    [IO.Path]::GetFullPath((Join-Path $programDataRoot 'ResticBackuperRecoveryTools')).TrimEnd('\')
}

$managerPath = Join-Path $installRoot 'Manage-Restore.ps1'
$configPath = Join-Path $installRoot 'backup-config.json'
$runtimeManifestPath = Join-Path $installRoot 'runtime-manifest.json'
$restorePath = Join-Path $installRoot 'restore.py'
$pythonPath = Join-Path $installRoot 'Python\python.exe'
$resticPath = Join-Path $installRoot 'restic.exe'
$lockPath = Join-Path $stateRoot 'run.lock'
$resultRoot = Join-Path $stateRoot 'RestoreManagerResults'
$restoreHistoryPath = Join-Path $stateRoot 'restore-history.json'
$lastSuccessPath = Join-Path $stateRoot 'last-success.json'
$drillRoot = if ($isTestMode) {
    [IO.Path]::GetFullPath((Join-Path $testContainer "ProgramData\$productName-RestoreDrills")).TrimEnd('\')
} else {
    [IO.Path]::GetFullPath((Join-Path $programDataRoot "$productName-RestoreDrills")).TrimEnd('\')
}
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$currentSid = $null
$canonicalTarget = ''
$canonicalTreePath = ''
$canonicalSnapshotId = ''
$includes = @()
$includesHash = $null
$historyRecorded = $null
$historyError = $null
$resultChannelReady = $false
$progressPath = $null
$lockStream = $null
$reportPath = $null
$allowLegacy = $AllowLegacyUnbound -ceq '1'

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
    if (Test-PathEqual -Left $Candidate -Right $Parent) { return $true }
    return $Candidate.StartsWith($Parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Get-CanonicalLocalPath {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or -not [IO.Path]::IsPathRooted($Value) -or
        $Value.StartsWith('\\')) {
        throw "Only an absolute local drive-letter path is supported: $Value"
    }
    $full = [IO.Path]::GetFullPath($Value)
    $root = [IO.Path]::GetPathRoot($full)
    if ($root -notmatch '^[A-Za-z]:\\$' -or $full.Substring(2).Contains(':')) {
        throw "Only a normal local drive-letter path is supported: $Value"
    }
    if (-not (Test-PathEqual -Left $full -Right $root)) { $full = $full.TrimEnd('\') }
    return $full
}

function Assert-NormalDirectoryChain {
    param([string]$Directory)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Required directory does not exist: $Directory"
    }
    $current = Get-Item -LiteralPath $Directory -Force
    while ($null -ne $current) {
        if (-not $current.PSIsContainer -or ($current.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Directory path contains a reparse point or non-directory component: $($current.FullName)"
        }
        $parent = $current.Parent
        if ($null -eq $parent) { break }
        $current = Get-Item -LiteralPath $parent.FullName -Force
    }
}

function Assert-NormalFile {
    param([string]$File)
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) { throw "Required regular file is missing: $File" }
    $item = Get-Item -LiteralPath $File -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Path is not a normal file: $File"
    }
    return $item
}

function Assert-RecoveryKeyAcl {
    param([string]$Path)
    $item = Assert-NormalFile -File $Path
    if ($item.Length -le 0 -or $item.Length -gt 64KB) {
        throw 'The recovery-key file has an invalid size.'
    }
    if ($isTestMode) { return }
    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw 'The recovery-key ACL inheritance is not protected.'
    }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', $currentSid)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        $sid = $rule.IdentityReference.Value
        if ($rule.IsInherited -or
            $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $sid -notin $allowed -or
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne
                [Security.AccessControl.FileSystemRights]::FullControl) {
            throw "The recovery-key ACL grants an unexpected principal or rights: $sid"
        }
        [void]$seen.Add($sid)
    }
    foreach ($sid in $allowed) {
        if (-not $seen.Contains($sid)) { throw "The recovery-key ACL is missing $sid" }
    }
}

function Assert-NoPathOverlap {
    param([string]$Candidate, [string]$Protected, [string]$Description)
    if ((Test-IsWithin -Candidate $Candidate -Parent $Protected) -or
        (Test-IsWithin -Candidate $Protected -Parent $Candidate)) {
        throw "Recovery-drill storage overlaps $Description."
    }
}

function Get-DrillTarget {
    param([string]$Nonce)
    return Get-CanonicalLocalPath -Value (Join-Path $drillRoot ("drill-" + $Nonce))
}

function Get-WindowsSnapshotPath {
    param([string]$Value)
    $full = Get-CanonicalLocalPath -Value $Value
    $root = [IO.Path]::GetPathRoot($full)
    if ($root -notmatch '^[A-Za-z]:\\$') { throw 'The configured canary path is not on a supported drive.' }
    $drive = $root.Substring(0, 1).ToUpperInvariant()
    $tail = $full.Substring($root.Length).Replace('\', '/').Trim('/')
    return $(if ($tail) { "/$drive/$tail" } else { "/$drive" })
}

function Get-Sha256Hex {
    param([byte[]]$Bytes)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Get-FileSha256Hex {
    param([string]$File)
    $stream = [IO.File]::Open($File, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}

function Read-BoundedJsonObject {
    param([string]$File, [long]$MaximumBytes, [string]$Description)
    $item = Assert-NormalFile -File $File
    if ($item.Length -le 0 -or $item.Length -gt $MaximumBytes) { throw "$Description has an invalid size." }
    $bytes = [IO.File]::ReadAllBytes($File)
    try { $value = $utf8.GetString($bytes) | ConvertFrom-Json }
    catch { throw "$Description is not valid UTF-8 JSON." }
    if ($null -eq $value -or $value -isnot [Management.Automation.PSCustomObject]) {
        throw "$Description must contain exactly one JSON object."
    }
    return [pscustomobject]@{ Json = $value; Bytes = $bytes }
}

function Get-RequiredProperty {
    param([object]$Object, [string]$Name, [string]$Description)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "$Description is missing required field: $Name" }
    return $property.Value
}

function Get-ConfiguredStorageBinding {
    param([object]$Configuration, [string]$Repository, [string]$RecoveryTools)
    $modeProperty = $Configuration.PSObject.Properties['repository_storage_mode']
    $mode = if ($null -eq $modeProperty) { $localNtfsMode } else { [string]$modeProperty.Value }
    if ($mode -notin @($localNtfsMode, $driveFsMode)) {
        throw 'Configured repository_storage_mode is unsupported.'
    }
    if ((Test-IsWithin -Candidate $RecoveryTools -Parent $stateRoot) -or
        (Test-IsWithin -Candidate $stateRoot -Parent $RecoveryTools)) {
        throw 'RecoveryTools must not overlap protected ProgramData state.'
    }
    $rootProperty = $Configuration.PSObject.Properties['drivefs_my_drive_root']
    $legacyRootProperty = $Configuration.PSObject.Properties['repository_drivefs_root']
    $cacheProperty = $Configuration.PSObject.Properties['drivefs_cache_directory']
    if ($mode -eq $localNtfsMode) {
        if ($null -ne $rootProperty -or $null -ne $legacyRootProperty -or $null -ne $cacheProperty) {
            throw 'DriveFS path bindings are invalid with local_ntfs.'
        }
        $repositoryParent = Split-Path -Parent $Repository
        if ([string]::IsNullOrWhiteSpace($repositoryParent)) {
            throw 'The local_ntfs repository must not be a drive root.'
        }
        $expectedRecovery = Get-CanonicalLocalPath -Value (
            Join-Path $repositoryParent 'RecoveryTools')
        if (-not (Test-PathEqual -Left $RecoveryTools -Right $expectedRecovery)) {
            throw 'The local_ntfs recovery-tools directory is not the repository sibling required by v1.'
        }
        return [pscustomobject]@{ Mode = $mode; DriveFsRoot = $null; DriveFsCache = $null }
    }

    $rawRoot = if ($null -ne $rootProperty) {
        [string]$rootProperty.Value
    }
    elseif ($null -ne $legacyRootProperty) {
        [string]$legacyRootProperty.Value
    }
    else { '' }
    if ($null -ne $rootProperty -and $null -ne $legacyRootProperty) {
        if (-not (Test-PathEqual `
            -Left (Get-CanonicalLocalPath -Value ([string]$rootProperty.Value)) `
            -Right (Get-CanonicalLocalPath -Value ([string]$legacyRootProperty.Value)))) {
            throw 'drivefs_my_drive_root conflicts with legacy repository_drivefs_root.'
        }
    }
    $root = Get-CanonicalLocalPath -Value $rawRoot
    $cache = if ($null -ne $cacheProperty) {
        Get-CanonicalLocalPath -Value ([string]$cacheProperty.Value)
    }
    elseif ($null -ne $legacyRootProperty) { $expectedDriveFsCache }
    else { throw 'google_drivefs_stream requires drivefs_cache_directory.' }
    if (-not (Test-PathEqual -Left $root -Right $expectedDriveFsRoot) -or
        -not (Test-PathEqual -Left $cache -Right $expectedDriveFsCache)) {
        throw 'Google DriveFS root/cache bindings do not match the supported current-user provider.'
    }
    if (-not (Test-IsWithin -Candidate $Repository -Parent $root) -or
        (Test-PathEqual -Left $Repository -Right $root)) {
        throw 'DriveFS repository is not strictly beneath drivefs_my_drive_root.'
    }
    if (-not (Test-PathEqual -Left $RecoveryTools -Right $driveFsRecoveryRoot)) {
        throw 'DriveFS recovery tools must remain in the protected NTFS recovery root.'
    }
    return [pscustomobject]@{ Mode = $mode; DriveFsRoot = $root; DriveFsCache = $cache }
}

function Assert-NtfsLocalPath {
    param([string]$Path, [string]$Label)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Path))
    if (-not $drive.IsReady -or
        $drive.DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable) -or
        -not [string]::Equals($drive.DriveFormat, 'NTFS', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must remain on a ready local NTFS volume."
    }
}

function Get-OptionalProperty {
    param([object]$Object, [string]$Name)
    $property = $Object.PSObject.Properties[$Name]
    return $(if ($null -eq $property) { $null } else { $property.Value })
}

function New-ResultDirectorySecurity {
    param([string]$UserSid)
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $allow = [Security.AccessControl.AccessControlType]::Allow
    foreach ($sidText in @('S-1-5-18', 'S-1-5-32-544')) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidText),
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritance, [Security.AccessControl.PropagationFlags]::None, $allow))
    }
    $read = [Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize
    foreach ($sidText in @($UserSid, 'S-1-3-4')) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidText), $read,
            $inheritance, [Security.AccessControl.PropagationFlags]::None, $allow))
    }
    return $security
}

function New-ResultFileSecurity {
    param([string]$UserSid)
    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $allow = [Security.AccessControl.AccessControlType]::Allow
    foreach ($sidText in @('S-1-5-18', 'S-1-5-32-544')) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidText),
            [Security.AccessControl.FileSystemRights]::FullControl, $allow))
    }
    $read = [Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize
    foreach ($sidText in @($UserSid, 'S-1-3-4')) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidText), $read, $allow))
    }
    return $security
}

function Assert-ResultAcl {
    param([string]$Path, [string]$UserSid)
    if ($isTestMode) { return }
    $acl = Get-Acl -LiteralPath $Path
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne 'S-1-5-32-544' -or
        -not $acl.AreAccessRulesProtected) { throw "Protected restore result path has an unsafe ACL: $Path" }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', 'S-1-3-4', $UserSid)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $dangerous = [Security.AccessControl.FileSystemRights]::WriteData -bor `
        [Security.AccessControl.FileSystemRights]::AppendData -bor `
        [Security.AccessControl.FileSystemRights]::Delete -bor `
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor `
        [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        $sid = $rule.IdentityReference.Value
        if ($rule.IsInherited -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $sid -notin $allowed) { throw "Protected restore result ACL contains unexpected identity: $sid" }
        if ($sid -notin @('S-1-5-18', 'S-1-5-32-544') -and
            ($rule.FileSystemRights -band $dangerous) -ne 0) {
            throw "Protected restore result ACL grants write rights to $sid"
        }
        [void]$seen.Add($sid)
    }
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544', $UserSid)) {
        if (-not $seen.Contains($sid)) { throw "Protected restore result ACL is missing $sid" }
    }
}

function Initialize-DrillRoot {
    param([pscustomobject]$Configuration)
    Assert-NoPathOverlap -Candidate $drillRoot -Protected $Configuration.Repository -Description 'the Restic repository'
    Assert-NoPathOverlap -Candidate $drillRoot -Protected $stateRoot -Description 'protected state'
    Assert-NoPathOverlap -Candidate $drillRoot -Protected $installRoot -Description 'the protected runtime'
    foreach ($name in @('recovery_tools_directory','recovery_key_file','secret_file','canary_file')) {
        $value = [string](Get-OptionalProperty -Object $Configuration.Json -Name $name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $protectedPath = Get-CanonicalLocalPath -Value $value
            if (-not (Test-PathEqual -Left $protectedPath -Right $drillRoot) -and
                -not (Test-IsWithin -Candidate $protectedPath -Parent $drillRoot)) {
                Assert-NoPathOverlap -Candidate $drillRoot -Protected $protectedPath -Description $name
            }
        }
    }
    foreach ($source in @($Configuration.Json.sources)) {
        Assert-NoPathOverlap -Candidate $drillRoot -Protected (Get-CanonicalLocalPath -Value ([string]$source)) -Description 'a configured source'
    }
    if (-not (Test-Path -LiteralPath $drillRoot)) {
        if ($isTestMode) { [void][IO.Directory]::CreateDirectory($drillRoot) }
        else { [void][IO.Directory]::CreateDirectory($drillRoot, (New-ResultDirectorySecurity -UserSid $currentSid)) }
    }
    Assert-NormalDirectoryChain -Directory $drillRoot
    Assert-ResultAcl -Path $drillRoot -UserSid $currentSid
    if ([IO.File]::Exists($canonicalTarget) -or [IO.Directory]::Exists($canonicalTarget)) {
        throw 'The nonce-bound recovery-drill target already exists.'
    }
}

function Protect-RecoveryDrillTarget {
    param([string]$Path)
    Assert-NormalDirectoryChain -Directory $Path
    if ($isTestMode) { return }
    $items = @(Get-ChildItem -LiteralPath $Path -Recurse -Force) + @(Get-Item -LiteralPath $Path -Force)
    foreach ($item in $items) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "The recovered drill target contains a reparse point: $($item.FullName)"
        }
    }
    foreach ($item in $items) {
        if ($item.PSIsContainer) {
            $item.SetAccessControl((New-ResultDirectorySecurity -UserSid $currentSid))
        }
        else {
            $item.SetAccessControl((New-ResultFileSecurity -UserSid $currentSid))
        }
    }
    foreach ($item in $items) { Assert-ResultAcl -Path $item.FullName -UserSid $currentSid }
}

function Write-ProtectedJson {
    param([string]$Path, [object]$Value, [switch]$Replace)
    $bytes = $utf8.GetBytes(($Value | ConvertTo-Json -Depth 40) + "`n")
    if ($bytes.Length -gt 8MB) { throw 'Protected restore result exceeds its size limit.' }
    $parent = Split-Path -Parent $Path
    $temporary = Join-Path $parent ('.restore-result-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $stream = $null
    try {
        if ($isTestMode) {
            $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        }
        else {
            $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew,
                [Security.AccessControl.FileSystemRights]::Write, [IO.FileShare]::None,
                4096, [IO.FileOptions]::WriteThrough, (New-ResultFileSecurity -UserSid $currentSid))
        }
        $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true); $stream.Dispose(); $stream = $null
        if ($Replace -and [IO.File]::Exists($Path)) {
            $replaceMethod = [IO.File].GetMethod('Replace', [type[]]@([string], [string], [string]))
            if ($null -eq $replaceMethod) { throw 'The required atomic result replacement API is unavailable.' }
            [void]$replaceMethod.Invoke($null, [object[]]@([string]$temporary, [string]$Path, $null))
            $temporary = $null
        }
        else {
            if ([IO.File]::Exists($Path) -or [IO.Directory]::Exists($Path)) { throw "Protected result already exists: $Path" }
            [IO.File]::Move($temporary, $Path); $temporary = $null
        }
        Assert-ResultAcl -Path $Path -UserSid $currentSid
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
        if ($null -ne $temporary -and [IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Initialize-ResultChannel {
    Assert-NormalDirectoryChain -Directory $stateRoot
    if (-not (Test-Path -LiteralPath $resultRoot)) {
        if ($isTestMode) { [void][IO.Directory]::CreateDirectory($resultRoot) }
        else { [void][IO.Directory]::CreateDirectory($resultRoot, (New-ResultDirectorySecurity -UserSid $currentSid)) }
    }
    Assert-NormalDirectoryChain -Directory $resultRoot
    Assert-ResultAcl -Path $resultRoot -UserSid $currentSid
    foreach ($entry in Get-ChildItem -LiteralPath $resultRoot -Force) {
        if ($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $entry.Name -notmatch '^[0-9a-f]{64}(\.json|\.json\.progress\.json|\.restore-report\.json)$') {
            throw "Protected restore-result directory contains an unexpected entry: $($entry.Name)"
        }
        Assert-ResultAcl -Path $entry.FullName -UserSid $currentSid
        if ($entry.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-7)) { [IO.File]::Delete($entry.FullName) }
    }
    $expectedResult = Join-Path $resultRoot "$RequestNonce.json"
    if (-not (Test-PathEqual -Left ([IO.Path]::GetFullPath($ResultPath)) -Right $expectedResult)) {
        throw 'The restore-manager result path does not match its request nonce.'
    }
    $script:progressPath = $expectedResult + '.progress.json'
    foreach ($candidate in @($expectedResult, $progressPath)) {
        if ([IO.File]::Exists($candidate) -or [IO.Directory]::Exists($candidate)) {
            throw "Protected restore-result target already exists: $candidate"
        }
    }
    $script:resultChannelReady = $true
}

function Remove-OrphanedRestoreReports {
    # The global run lock proves no backend can still own one of these bounded
    # temporary reports. A terminated process may otherwise block all later
    # restore requests indefinitely.
    foreach ($entry in Get-ChildItem -LiteralPath $resultRoot -File -Force) {
        if ($entry.Name -notmatch '^[0-9a-f]{64}\.restore-report\.json$') { continue }
        Assert-ResultAcl -Path $entry.FullName -UserSid $currentSid
        [IO.File]::Delete($entry.FullName)
    }
}

function Get-SanitizedError {
    param([string]$Message)
    if ([string]::IsNullOrWhiteSpace($Message)) { return 'The protected restore operation failed.' }
    $builder = [Text.StringBuilder]::new()
    foreach ($character in $Message.ToCharArray()) {
        if (-not [char]::IsControl($character) -or $character -eq "`t") { [void]$builder.Append($character) }
        if ($builder.Length -ge 2000) { break }
    }
    $result = $builder.ToString().Trim()
    if ($result -match '(?i)(password-command|RESTIC_PASSWORD|dpapi)') {
        return 'The protected restore operation failed without exposing credential details.'
    }
    return $(if ($result) { $result } else { 'The protected restore operation failed.' })
}

function Write-ProgressResult {
    param([string]$Stage, [string]$Message, [int]$Percent)
    if (-not $resultChannelReady) { return }
    $document = [ordered]@{
        schema_version = 1; request_nonce = $RequestNonce; request_digest = $RequestDigest;
        request_user_sid = $currentSid; action = $Action; plan_id = $ExpectedPlanId;
        config_generation = $ExpectedConfigGeneration; snapshot_id = if ($canonicalSnapshotId) { $canonicalSnapshotId } else { $null };
        allow_legacy_unbound = $allowLegacy;
        target = if ($canonicalTarget) { $canonicalTarget } else { $null };
        stage = $Stage; message = $Message; percent = $Percent; cancellable = $false;
        updated_utc = [DateTime]::UtcNow.ToString('o')
    }
    Write-ProtectedJson -Path $progressPath -Value $document -Replace:([IO.File]::Exists($progressPath))
}

function Write-FinalResult {
    param([bool]$Ok, [object]$Payload, [string]$ErrorMessage, [int]$BackendExitCode)
    if (-not $resultChannelReady) { return }
    $document = [ordered]@{
        schema_version = 1; request_nonce = $RequestNonce; request_digest = $RequestDigest;
        request_user_sid = $currentSid; action = $Action; expected_config_sha256 = $ExpectedConfigSha256;
        plan_id = $ExpectedPlanId; config_generation = $ExpectedConfigGeneration;
        allow_legacy_unbound = $allowLegacy;
        snapshot_id = if ($canonicalSnapshotId) { $canonicalSnapshotId } else { $null };
        tree_path = if ($canonicalTreePath) { $canonicalTreePath } else { $null };
        target = if ($canonicalTarget) { $canonicalTarget } else { $null };
        includes_sha256 = $includesHash; ok = $Ok; backend_exit_code = $BackendExitCode;
        history_recorded = $historyRecorded; history_error = $historyError;
        payload = $Payload; error = if ($Ok) { $null } else { Get-SanitizedError -Message $ErrorMessage };
        finished_utc = [DateTime]::UtcNow.ToString('o')
    }
    Write-ProtectedJson -Path ([IO.Path]::GetFullPath($ResultPath)) -Value $document
    if ($isTestMode) { $document | ConvertTo-Json -Compress -Depth 20 }
}

function Write-RestoreHistory {
    param([object]$Payload, [int]$BackendExitCode)
    $entries = @()
    if ([IO.File]::Exists($restoreHistoryPath)) {
        $history = (Read-BoundedJsonObject -File $restoreHistoryPath -MaximumBytes 1MB -Description 'Protected restore history').Json
        if ($history.schema_version -notin @(1,2) -or $null -eq $history.PSObject.Properties['entries']) {
            throw 'Protected restore history schema is invalid.'
        }
        if ($history.schema_version -eq 2) { $entries = @($history.entries) }
        if ($entries.Count -gt 200) { throw 'Protected restore history exceeds its bounded entry limit.' }
    }
    $entry = [ordered]@{
        finished_utc = [DateTime]::UtcNow.ToString('o')
        kind = if ($Action -eq 'restore_drill') { 'recovery_key_representative_drill' } else { 'manual_restore' }
        credential_source = if ($Action -eq 'restore_drill') { 'recovery_key' } else { 'active_credential' }
        plan_id = $ExpectedPlanId
        config_generation = $ExpectedConfigGeneration
        snapshot_generation = Get-OptionalProperty -Object $Payload -Name 'snapshot_generation'
        repository_id = [string](Get-OptionalProperty -Object $Payload -Name 'repository_id')
        snapshot_id = $canonicalSnapshotId
        snapshot_binding = [string]$Payload.snapshot_binding
        result = [string]$Payload.result
        verified = $Payload.verified -is [bool] -and [bool]$Payload.verified
        partial_target_retained = $Payload.partial_target_retained -is [bool] -and [bool]$Payload.partial_target_retained
        backend_exit_code = $BackendExitCode
        target = $canonicalTarget
        includes_sha256 = $includesHash
        canary_verified = if ($Action -eq 'restore_drill') { [bool]$Payload.canary_verified } else { $false }
        canary_sha256 = if ($Action -eq 'restore_drill') { [string]$Payload.canary_sha256 } else { $null }
        canary_bytes = if ($Action -eq 'restore_drill') { [long]$Payload.canary_bytes } else { $null }
        sample_policy = if ($Action -eq 'restore_drill') { [string]$Payload.sample_policy } else { $null }
        sample_file_count = if ($Action -eq 'restore_drill') { [long]$Payload.sample_file_count } else { $null }
        sample_bytes = if ($Action -eq 'restore_drill') { [long]$Payload.sample_bytes } else { $null }
        sample_paths_sha256 = if ($Action -eq 'restore_drill') { [string]$Payload.sample_paths_sha256 } else { $null }
    }
    $newEntries = @($entries | Select-Object -Last 49) + @($entry)
    $document = [ordered]@{
        schema_version = 2
        plan_id = $ExpectedPlanId
        updated_utc = [DateTime]::UtcNow.ToString('o')
        entries = $newEntries
    }
    Write-ProtectedJson -Path $restoreHistoryPath -Value $document -Replace:([IO.File]::Exists($restoreHistoryPath))
}

function Enter-RunLock {
    $stream = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -eq 0) { $stream.WriteByte(0); $stream.Flush($true) }
        $stream.Lock(0, 1)
        return $stream
    }
    catch { $stream.Dispose(); throw 'A backup or another protected operation already holds the run lock.' }
}

function Exit-RunLock {
    param([IO.FileStream]$Stream)
    if ($null -eq $Stream) { return }
    try { $Stream.Unlock(0, 1) } finally { $Stream.Dispose() }
}

function Assert-NoPendingJournal {
    foreach ($name in @('repository-relocation.journal.json', 'plan-migration.journal.json', 'credential-rotation.journal.json')) {
        $path = Join-Path $stateRoot $name
        if ([IO.File]::Exists($path) -or [IO.Directory]::Exists($path)) {
            throw "A pending protected-operation journal ($name) must be repaired before restore operations."
        }
    }
}

function Get-ValidatedConfiguration {
    $loaded = Read-BoundedJsonObject -File $configPath -MaximumBytes 1MB -Description 'Protected backup configuration'
    if ((Get-Sha256Hex -Bytes $loaded.Bytes) -ne $ExpectedConfigSha256) {
        throw 'The protected configuration changed after the restore request was prepared.'
    }
    $config = $loaded.Json
    if ($config.schema_version -ne 1) { throw 'Unsupported protected configuration schema.' }
    $parsedPlan = [Guid]::Empty
    if ($config.plan_id -isnot [string] -or
        -not [Guid]::TryParseExact([string]$config.plan_id, 'D', [ref]$parsedPlan) -or
        $parsedPlan.ToString('D') -cne $ExpectedPlanId -or
        [string]$config.plan_id -cne $ExpectedPlanId) {
        throw 'The protected restore request names another backup plan.'
    }
    if ($config.config_generation.GetType().FullName -notin @('System.Int32', 'System.Int64') -or
        [long]$config.config_generation -ne $ExpectedConfigGeneration -or $ExpectedConfigGeneration -le 0) {
        throw 'The protected restore request names another configuration generation.'
    }
    foreach ($name in @(
        'repository','repository_volume_serial','restic_executable','recovery_tools_directory',
        'python_executable','state_directory','secret_file','recovery_key_file','canary_file','sources','source_identities'
    )) { [void](Get-RequiredProperty -Object $config -Name $name -Description 'Protected backup configuration') }
    $repository = Get-CanonicalLocalPath -Value ([string]$config.repository)
    $recoveryTools = Get-CanonicalLocalPath -Value ([string]$config.recovery_tools_directory)
    $configuredState = Get-CanonicalLocalPath -Value ([string]$config.state_directory)
    $configuredRestic = Get-CanonicalLocalPath -Value ([string]$config.restic_executable)
    $configuredPython = Get-CanonicalLocalPath -Value ([string]$config.python_executable)
    if (-not (Test-PathEqual -Left $configuredState -Right $stateRoot) -or
        -not (Test-PathEqual -Left $configuredRestic -Right $resticPath) -or
        -not (Test-PathEqual -Left $configuredPython -Right $pythonPath)) {
        throw 'The protected configuration names an unexpected runtime or state path.'
    }
    $storage = Get-ConfiguredStorageBinding -Configuration $config `
        -Repository $repository -RecoveryTools $recoveryTools
    foreach ($directory in @($installRoot, $stateRoot, $repository, $recoveryTools)) {
        Assert-NormalDirectoryChain -Directory $directory
    }
    Assert-NtfsLocalPath -Path $stateRoot -Label 'ProgramData state'
    Assert-NtfsLocalPath -Path $recoveryTools -Label 'Recovery tools'
    if ($storage.Mode -eq $localNtfsMode) {
        Assert-NtfsLocalPath -Path $repository -Label 'local_ntfs repository'
    }
    else {
        foreach ($directory in @($storage.DriveFsRoot, $storage.DriveFsCache)) {
            Assert-NormalDirectoryChain -Directory $directory
        }
        Assert-NtfsLocalPath -Path $storage.DriveFsCache -Label 'Google DriveFS cache'
        if (-not $isTestMode) {
            $repositoryDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($repository))
            if ($repositoryDrive.DriveType -ne [IO.DriveType]::Fixed -or
                -not [string]::Equals($repositoryDrive.DriveFormat, 'FAT32', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'The DriveFS repository is not on the supported fixed FAT32 streaming mount.'
            }
        }
    }
    foreach ($file in @($managerPath, $restorePath, $pythonPath, $resticPath, $configPath, (Join-Path $repository 'config'))) {
        [void](Assert-NormalFile -File $file)
    }
    Assert-RecoveryKeyAcl -Path (Get-CanonicalLocalPath -Value ([string]$config.recovery_key_file))
    [void](Assert-NormalFile -File $lastSuccessPath)
    return [pscustomobject]@{
        Json = $config
        Repository = $repository
        RecoveryTools = $recoveryTools
        RepositoryStorageMode = $storage.Mode
        Bytes = $loaded.Bytes
    }
}

function Assert-RuntimeManifest {
    $loaded = Read-BoundedJsonObject -File $runtimeManifestPath -MaximumBytes 4MB -Description 'Protected runtime manifest'
    $manifest = $loaded.Json
    $records = @($manifest.files)
    if ($manifest.schema_version -ne 1 -or [long]$manifest.file_count -ne $records.Count) {
        throw 'Protected runtime manifest header is invalid.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prefix = $installRoot + '\'
    foreach ($record in $records) {
        if ($record.relative_path -isnot [string]) { throw 'Protected runtime manifest contains an invalid record.' }
        $relative = ([string]$record.relative_path).Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
            $relative.Split('\') -contains '..' -or -not $seen.Add($relative)) {
            throw "Protected runtime manifest contains an unsafe path: $relative"
        }
        $file = [IO.Path]::GetFullPath((Join-Path $installRoot $relative))
        if (-not $file.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime manifest path escapes its root.' }
        Assert-NormalDirectoryChain -Directory ([IO.Path]::GetDirectoryName($file))
        $item = Assert-NormalFile -File $file
        if ([long]$record.bytes -ne $item.Length -or
            -not [string]::Equals([string]$record.sha256, (Get-FileSha256Hex -File $file), [StringComparison]::OrdinalIgnoreCase)) {
            throw "Protected runtime manifest mismatch: $relative"
        }
    }
    foreach ($required in @('backup-config.json','Manage-Restore.ps1','restore.py','restic.exe','Python\python.exe')) {
        if (-not $seen.Contains($required)) { throw "Protected runtime manifest is missing: $required" }
    }
    $actualFiles = @(
        Get-ChildItem -LiteralPath $installRoot -Recurse -File -Force |
            Where-Object Name -notin @(
                'runtime-manifest.json',
                'scheduled-task.xml',
                'google-drive-verification-task.xml'
            )
    )
    if ($actualFiles.Count -ne $records.Count) {
        throw 'Protected runtime contains unmanifested or missing files.'
    }
    foreach ($file in $actualFiles) {
        Assert-NormalDirectoryChain -Directory $file.DirectoryName
        $relative = $file.FullName.Substring($installRoot.Length + 1)
        if (-not $seen.Contains($relative)) {
            throw "Protected runtime contains an unmanifested file: $relative"
        }
    }
}

function ConvertTo-WindowsArgument {
    param([string]$Value)
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $builder = [Text.StringBuilder]::new(); [void]$builder.Append('"')
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $slashes++; continue }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * ($slashes * 2 + 1))); [void]$builder.Append('"'); $slashes = 0; continue
        }
        if ($slashes -gt 0) { [void]$builder.Append(('\' * $slashes)); $slashes = 0 }
        [void]$builder.Append($character)
    }
    if ($slashes -gt 0) { [void]$builder.Append(('\' * ($slashes * 2))) }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-RestoreBackend {
    param([string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $pythonPath
    $start.Arguments = (($Arguments | ForEach-Object { ConvertTo-WindowsArgument -Value ([string]$_) }) -join ' ')
    $start.WorkingDirectory = $installRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Windows did not start the protected restore backend.' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        if ($utf8.GetByteCount($stdout) -gt 7MB -or $utf8.GetByteCount($stderr) -gt 1MB) {
            throw 'The protected restore backend returned more output than the manager accepts.'
        }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $stdout; Stderr = $stderr }
    }
    finally { $process.Dispose() }
}

function ConvertFrom-BackendObject {
    param([string]$Text, [string]$Description)
    try { $value = $Text | ConvertFrom-Json }
    catch { throw "$Description returned invalid JSON." }
    if ($null -eq $value -or $value -isnot [Management.Automation.PSCustomObject]) {
        throw "$Description did not return one JSON object."
    }
    return $value
}

function Test-CanonicalPathArraysEqual {
    param([object[]]$Left, [object[]]$Right)
    $leftPaths = @($Left | ForEach-Object { Get-CanonicalLocalPath -Value ([string]$_) } | Sort-Object)
    $rightPaths = @($Right | ForEach-Object { Get-CanonicalLocalPath -Value ([string]$_) } | Sort-Object)
    if ($leftPaths.Count -ne $rightPaths.Count) { return $false }
    for ($index = 0; $index -lt $leftPaths.Count; $index++) {
        if (-not (Test-PathEqual -Left $leftPaths[$index] -Right $rightPaths[$index])) { return $false }
    }
    return $true
}

function Assert-LegacySnapshotBinding {
    param([object]$Snapshot, [pscustomobject]$Configuration)
    if ([string](Get-OptionalProperty -Object $Snapshot -Name 'binding_state') -cne 'legacy_unbound' -or
        $null -ne (Get-OptionalProperty -Object $Snapshot -Name 'plan_id') -or
        $null -ne (Get-OptionalProperty -Object $Snapshot -Name 'config_generation')) {
        throw 'Legacy snapshot response contains plan-binding metadata.'
    }
    $configuredHostname = [string](Get-OptionalProperty -Object $Configuration.Json -Name 'hostname')
    if ([string]::IsNullOrWhiteSpace($configuredHostname) -or
        -not [string]::Equals([string]$Snapshot.hostname, $configuredHostname, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Legacy snapshot response belongs to another computer.'
    }
    $requiredTags = @((Get-OptionalProperty -Object $Configuration.Json -Name 'scheduled_tag'))
    $actualTags = @($Snapshot.tags)
    if ($requiredTags.Count -eq 0) { throw 'Legacy snapshot matching requires a scheduled tag.' }
    foreach ($tag in $actualTags) {
        if ($tag -isnot [string] -or
            ([string]$tag).StartsWith('restic-backuper-plan:', [StringComparison]::OrdinalIgnoreCase) -or
            ([string]$tag).StartsWith('restic-backuper-generation:', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Legacy snapshot response contains invalid or conflicting tags.'
        }
    }
    foreach ($tag in $requiredTags) {
        if ($tag -isnot [string] -or -not ($actualTags -ccontains [string]$tag)) {
            throw 'Legacy snapshot response does not have the configured scheduled tag.'
        }
    }
    if (-not (Test-CanonicalPathArraysEqual -Left @($Snapshot.paths) -Right @($Configuration.Json.sources))) {
        throw 'Legacy snapshot response does not have the configured complete source set.'
    }
}

function Assert-SnapshotListPayload {
    param([object]$Payload, [pscustomobject]$Configuration)
    if ([string]$Payload.schema -cne 'ResticBackuper.SnapshotList.v1' -or $Payload.schema_version -ne 1 -or
        -not (Test-PathEqual -Left (Get-CanonicalLocalPath -Value ([string]$Payload.repository)) -Right $Configuration.Repository) -or
        [string]$Payload.binding.plan_id -cne $ExpectedPlanId -or
        [string]$Payload.binding.legacy_match_policy -cne 'exact-host-scheduled-tags-and-sources') {
        throw 'Snapshot-list response is not bound to the requested repository and plan.'
    }
    $snapshots = @($Payload.snapshots)
    if ($snapshots.Count -gt 10000) { throw 'Snapshot-list response exceeds the protected item limit.' }
    foreach ($snapshot in $snapshots) {
        if ([string]$snapshot.id -cnotmatch '^[0-9a-f]{64}$') {
            throw 'Snapshot-list response contains an invalid immutable snapshot ID.'
        }
        $bindingState = [string](Get-OptionalProperty -Object $snapshot -Name 'binding_state')
        if ($bindingState -ceq 'plan') {
            $snapshotGeneration = Get-OptionalProperty -Object $snapshot -Name 'config_generation'
            if ([string](Get-OptionalProperty -Object $snapshot -Name 'plan_id') -cne $ExpectedPlanId -or
                $null -eq $snapshotGeneration -or
                $snapshotGeneration.GetType().FullName -notin @('System.Int32','System.Int64') -or
                [long]$snapshotGeneration -le 0) {
                throw 'Snapshot-list response contains an invalid plan-bound snapshot.'
            }
        }
        elseif ($bindingState -ceq 'legacy_unbound') {
            Assert-LegacySnapshotBinding -Snapshot $snapshot -Configuration $Configuration
        }
        else {
            throw 'Snapshot-list response contains an unknown snapshot binding.'
        }
    }
}

function Assert-TreePayload {
    param([object]$Payload, [pscustomobject]$Configuration)
    if ([string]$Payload.schema -cne 'ResticBackuper.SnapshotTree.v1' -or $Payload.schema_version -ne 1 -or
        [string]$Payload.snapshot_id -cne $canonicalSnapshotId -or
        -not (Test-PathEqual -Left (Get-CanonicalLocalPath -Value ([string]$Payload.repository)) -Right $Configuration.Repository)) {
        throw 'Snapshot-tree response is not bound to the requested snapshot and plan.'
    }
    if ($allowLegacy) {
        if ([string]$Payload.snapshot_binding -cne 'legacy_unbound') {
            throw 'Snapshot-tree response did not return the explicitly requested legacy snapshot.'
        }
        Assert-LegacySnapshotBinding -Snapshot $Payload.snapshot -Configuration $Configuration
    }
    elseif ([string]$Payload.snapshot_binding -cne 'plan' -or
        [string]$Payload.snapshot.binding_state -cne 'plan' -or
        [string]$Payload.snapshot.plan_id -cne $ExpectedPlanId) {
        throw 'Snapshot-tree response is not bound to the requested backup plan.'
    }
    if (@($Payload.entries).Count -gt 100000) { throw 'Snapshot-tree response exceeds the protected item limit.' }
}

function Assert-RestoreReport {
    param([object]$Payload)
    if ([string]$Payload.schema -cne 'ResticBackuper.RestoreReport.v1' -or $Payload.schema_version -ne 1 -or
        [string]$Payload.snapshot_id -cne $canonicalSnapshotId -or
        -not (Test-PathEqual -Left (Get-CanonicalLocalPath -Value ([string]$Payload.target)) -Right $canonicalTarget) -or
        [string]$Payload.result -notin @('verified','partial','error')) {
        throw 'Restore report is not bound to the requested snapshot, plan, and target.'
    }
    if ($allowLegacy) {
        if ([string]$Payload.snapshot_binding -cne 'legacy_unbound' -or
            $null -ne (Get-OptionalProperty -Object $Payload -Name 'plan_id') -or
            $null -ne (Get-OptionalProperty -Object $Payload -Name 'snapshot_generation')) {
            throw 'Restore report did not return the explicitly requested legacy snapshot.'
        }
    }
    elseif ([string]$Payload.snapshot_binding -cne 'plan' -or [string]$Payload.plan_id -cne $ExpectedPlanId) {
        throw 'Restore report is not bound to the requested backup plan.'
    }
    if ([string]$Payload.result -eq 'verified' -and ($Payload.verified -isnot [bool] -or -not [bool]$Payload.verified)) {
        throw 'Restore report claims success without verification.'
    }
}

function Get-RecoveryDrillEvidence {
    param([pscustomobject]$Configuration)
    $evidence = (Read-BoundedJsonObject -File $lastSuccessPath -MaximumBytes 1MB -Description 'Protected latest-success evidence').Json
    if ($evidence.schema_version -ne 1 -or [string]$evidence.state -notin @('success','success_unchanged') -or
        [string]$evidence.plan_id -cne $ExpectedPlanId -or
        $evidence.config_generation.GetType().FullName -notin @('System.Int32','System.Int64') -or
        [long]$evidence.config_generation -ne $ExpectedConfigGeneration -or
        [string]$evidence.repository_id -cnotmatch '^[0-9a-f]{64}$' -or
        [string]$evidence.snapshot_id -cnotmatch '^[0-9a-f]{64}$' -or
        $evidence.verification_complete -isnot [bool] -or -not [bool]$evidence.verification_complete -or
        $evidence.verification.repository_structure -isnot [bool] -or -not [bool]$evidence.verification.repository_structure -or
        $evidence.verification.canary.verified -isnot [bool] -or -not [bool]$evidence.verification.canary.verified -or
        [string]$evidence.verification.canary.snapshot_path -cne (Get-WindowsSnapshotPath -Value ([string]$Configuration.Json.canary_file)) -or
        [string]$evidence.verification.canary.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $evidence.verification.canary.bytes.GetType().FullName -notin @('System.Int32','System.Int64') -or
        [long]$evidence.verification.canary.bytes -lt 0 -or [long]$evidence.verification.canary.bytes -gt 8MB) {
        throw 'The latest successful backup does not contain a complete bounded recovery-drill proof.'
    }
    return [pscustomobject]@{
        SnapshotId = [string]$evidence.snapshot_id
        RepositoryId = [string]$evidence.repository_id
        CanarySha256 = [string]$evidence.verification.canary.sha256
        CanaryBytes = [long]$evidence.verification.canary.bytes
    }
}

function Assert-RecoveryDrillReport {
    param([object]$Payload, [pscustomobject]$Evidence)
    if ([string]$Payload.drill_kind -cne 'recovery_key_representative' -or
        [string]$Payload.credential_source -cne 'recovery_key' -or
        [string]$Payload.repository_id -cne $Evidence.RepositoryId -or
        [string]$Payload.result -cne 'verified' -or
        $Payload.verified -isnot [bool] -or -not [bool]$Payload.verified -or
        $Payload.partial_target_retained -isnot [bool] -or [bool]$Payload.partial_target_retained -or
        $Payload.canary_verified -isnot [bool] -or -not [bool]$Payload.canary_verified -or
        [string]$Payload.canary_sha256 -cne $Evidence.CanarySha256 -or
        [long]$Payload.canary_bytes -ne $Evidence.CanaryBytes -or
        [string]$Payload.sample_policy -cne 'one-or-two-bounded-files-per-source-v1' -or
        [long]$Payload.sample_file_count -lt 2 -or [long]$Payload.sample_file_count -gt 8 -or
        [long]$Payload.sample_bytes -le 0 -or [long]$Payload.sample_bytes -gt 32MB -or
        [string]$Payload.sample_paths_sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'The recovery-drill report does not contain complete verified sample evidence.'
    }
}

try {
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
        throw 'Protected restore operations require 64-bit Windows PowerShell.'
    }
    if ($isTestMode) {
        if (Test-Administrator) { throw 'Disposable restore-manager tests must run without an elevated token.' }
    }
    elseif (-not (Test-Administrator)) { throw 'Protected restore operations must run elevated.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentSid = $identity.User.Value
    if ([string]::IsNullOrWhiteSpace($ExpectedUserSid) -or
        -not [string]::Equals($ExpectedUserSid, $currentSid, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Approve restore access with the same Windows account that made the request.'
    }
    if ([string]::IsNullOrWhiteSpace($PSCommandPath) -or
        -not (Test-PathEqual -Left ([IO.Path]::GetFullPath($PSCommandPath)) -Right $managerPath)) {
        throw 'Protected restore operations must run from the installed manager script.'
    }
    $parsedPlan = [Guid]::Empty
    if (-not [Guid]::TryParseExact($ExpectedPlanId, 'D', [ref]$parsedPlan) -or
        $parsedPlan.ToString('D') -cne $ExpectedPlanId -or $ExpectedConfigGeneration -le 0) {
        throw 'The restore request has an invalid plan identity or generation.'
    }
    if (($Action -eq 'list_snapshots' -or $Action -eq 'restore_drill') -and $allowLegacy) {
        throw 'Legacy snapshot access is not a snapshot-list request option.'
    }
    if ($Action -in @('list_tree','restore')) {
        if ($SnapshotId -cnotmatch '^[0-9a-f]{64}$') { throw 'Restore operations require one exact lowercase snapshot ID.' }
        $canonicalSnapshotId = $SnapshotId
    }
    elseif ($SnapshotId) { throw 'Snapshot ID is not valid for this request.' }
    if ($TreePath) {
        if ($Action -ne 'list_tree' -or $TreePath.Length -gt 1024 -or -not $TreePath.StartsWith('/') -or
            $TreePath.Contains('\') -or $TreePath.Split('/') -contains '..') {
            throw 'Snapshot tree path must be one bounded absolute in-snapshot path using forward slashes.'
        }
        $canonicalTreePath = $TreePath
    }
    if ($Action -eq 'restore') { $canonicalTarget = Get-CanonicalLocalPath -Value $Target }
    elseif ($Action -eq 'restore_drill') {
        if ($Target) { throw 'A recovery-drill target is derived by the protected manager.' }
        $canonicalTarget = Get-DrillTarget -Nonce $RequestNonce
    }
    elseif ($Target) { throw 'Restore target is only valid for restore requests.' }
    try { $includesBytes = [Convert]::FromBase64String($IncludesBase64) }
    catch { throw 'Restore include selection is not valid Base64.' }
    if ($includesBytes.Length -gt 64KB) { throw 'Restore include selection is too large.' }
    $includesHash = Get-Sha256Hex -Bytes $includesBytes
    try { $parsedIncludes = $utf8.GetString($includesBytes) | ConvertFrom-Json }
    catch { throw 'Restore include selection is not valid UTF-8 JSON.' }
    $includes = @($parsedIncludes)
    if ($includes.Count -gt 64 -or ($Action -ne 'restore' -and $includes.Count -ne 0)) {
        throw 'Restore include selection is invalid for this action.'
    }
    foreach ($include in $includes) {
        if ($include -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$include) -or
            ([string]$include).Length -gt 1024 -or ([string]$include).Contains([char]0)) {
            throw 'Restore include selection contains an invalid pattern.'
        }
    }
    $expectedDigestPayload = @(
        $requestDomain, $currentSid, $Action, $ExpectedConfigSha256, $ExpectedPlanId,
        $ExpectedConfigGeneration.ToString([Globalization.CultureInfo]::InvariantCulture),
        $AllowLegacyUnbound, $canonicalSnapshotId, $canonicalTreePath, $canonicalTarget, $includesHash, $RequestNonce
    ) -join "`n"
    if ((Get-Sha256Hex -Bytes $utf8.GetBytes($expectedDigestPayload)) -cne $RequestDigest) {
        throw 'The restore-manager request digest does not match the requested operation.'
    }

    Assert-NormalDirectoryChain -Directory $installRoot
    Assert-NormalDirectoryChain -Directory $stateRoot
    Initialize-ResultChannel
    Write-ProgressResult -Stage preflight -Message 'Validating the protected backup plan and repository.' -Percent 5
    $lockStream = Enter-RunLock
    Remove-OrphanedRestoreReports
    Assert-NoPendingJournal
    $configuration = Get-ValidatedConfiguration
    Assert-RuntimeManifest
    $drillEvidence = $null
    if ($Action -eq 'restore_drill') {
        Initialize-DrillRoot -Configuration $configuration
        $drillEvidence = Get-RecoveryDrillEvidence -Configuration $configuration
        $canonicalSnapshotId = $drillEvidence.SnapshotId
    }

    $arguments = @('-I','-S','-B',$restorePath,'--config',$configPath,'--restic',$resticPath)
    if ($Action -eq 'list_snapshots') {
        $arguments += '--list-snapshots-json'
        Write-ProgressResult -Stage reading -Message 'Reading plan-bound and safely matched legacy snapshot history.' -Percent 35
    }
    elseif ($Action -eq 'list_tree') {
        $arguments += @('--list-tree-json','--snapshot',$canonicalSnapshotId)
        if ($allowLegacy) { $arguments += '--allow-legacy-unbound' }
        if ($canonicalTreePath) { $arguments += @('--tree-path',$canonicalTreePath) }
        Write-ProgressResult -Stage reading -Message 'Reading the selected snapshot folder.' -Percent 35
    }
    else {
        $reportPath = Join-Path $resultRoot "$RequestNonce.restore-report.json"
        if (Test-Path -LiteralPath $reportPath) { throw 'Protected restore report target already exists.' }
        $arguments += @('--snapshot',$canonicalSnapshotId,'--target',$canonicalTarget,'--report',$reportPath)
        if ($allowLegacy) { $arguments += '--allow-legacy-unbound' }
        if ($Action -eq 'restore_drill') {
            $arguments += @(
                '--recovery-key-file',[string]$configuration.Json.recovery_key_file,
                '--recovery-drill','--drill-canary-sha256',$drillEvidence.CanarySha256,
                '--drill-canary-bytes',$drillEvidence.CanaryBytes.ToString([Globalization.CultureInfo]::InvariantCulture)
            )
            Write-ProgressResult -Stage restoring -Message 'Restoring the canary and a bounded representative sample with the recovery key.' -Percent 25
        }
        else {
            foreach ($include in $includes) { $arguments += @('--include',[string]$include) }
            Write-ProgressResult -Stage restoring -Message 'Restoring to the new location and verifying every restored file.' -Percent 25
        }
    }
    $backend = Invoke-RestoreBackend -Arguments $arguments
    $payload = $null
    if ($Action -eq 'list_snapshots') {
        if ($backend.ExitCode -ne 0) { throw "Snapshot history query failed with exit code $($backend.ExitCode)." }
        $payload = ConvertFrom-BackendObject -Text $backend.Stdout -Description 'Snapshot history query'
        Assert-SnapshotListPayload -Payload $payload -Configuration $configuration
    }
    elseif ($Action -eq 'list_tree') {
        if ($backend.ExitCode -ne 0) { throw "Snapshot tree query failed with exit code $($backend.ExitCode)." }
        $payload = ConvertFrom-BackendObject -Text $backend.Stdout -Description 'Snapshot tree query'
        Assert-TreePayload -Payload $payload -Configuration $configuration
    }
    else {
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            throw "Restore backend returned exit code $($backend.ExitCode) without a protected report."
        }
        $payload = (Read-BoundedJsonObject -File $reportPath -MaximumBytes 64KB -Description 'Protected restore report').Json
        Assert-RestoreReport -Payload $payload
        [IO.File]::Delete($reportPath); $reportPath = $null
        if ($backend.ExitCode -ne 0 -or [string]$payload.result -ne 'verified') {
            if ($Action -eq 'restore') {
                try { Write-RestoreHistory -Payload $payload -BackendExitCode $backend.ExitCode; $historyRecorded = $true }
                catch { $historyRecorded = $false; $historyError = Get-SanitizedError -Message $_.Exception.Message }
            }
            $failureMessage = [string]$payload.error
            if ($Action -eq 'restore_drill' -and [IO.Directory]::Exists($canonicalTarget)) {
                try { Protect-RecoveryDrillTarget -Path $canonicalTarget }
                catch { $failureMessage += ' The retained drill target could not be normalized to its protected review ACL.' }
            }
            Write-ProgressResult -Stage failed -Message 'Restore stopped; any partial alternate target was retained for inspection.' -Percent 0
            Write-FinalResult -Ok $false -Payload $payload -ErrorMessage $failureMessage -BackendExitCode $backend.ExitCode
            exit $(if ($backend.ExitCode -ne 0) { $backend.ExitCode } else { 1 })
        }
        if ($Action -eq 'restore_drill') {
            Assert-RecoveryDrillReport -Payload $payload -Evidence $drillEvidence
            try { Protect-RecoveryDrillTarget -Path $canonicalTarget }
            catch {
                Write-ProgressResult -Stage failed -Message 'The sample was verified, but its retained review ACL could not be protected.' -Percent 0
                Write-FinalResult -Ok $false -Payload $payload -ErrorMessage 'The recovery drill was verified, but its retained sample could not be normalized to the protected read-only ACL; readiness evidence was not recorded.' -BackendExitCode 0
                exit 1
            }
        }
        try { Write-RestoreHistory -Payload $payload -BackendExitCode $backend.ExitCode; $historyRecorded = $true }
        catch { $historyRecorded = $false; $historyError = Get-SanitizedError -Message $_.Exception.Message }
        if ($Action -eq 'restore_drill' -and -not $historyRecorded) {
            Write-ProgressResult -Stage failed -Message 'The sample was restored and verified, but readiness evidence was not recorded.' -Percent 0
            Write-FinalResult -Ok $false -Payload $payload -ErrorMessage 'The recovery drill was verified, but its protected readiness evidence could not be recorded.' -BackendExitCode 0
            exit 1
        }
    }
    Write-ProgressResult -Stage complete -Message $(if ($Action -eq 'restore_drill') {
        'Recovery-key restore drill completed, verified, and was recorded.'
    } elseif ($Action -eq 'restore') {
        'Restore completed and was verified.'
    } else { 'Protected snapshot data loaded.' }) -Percent 100
    Write-FinalResult -Ok $true -Payload $payload -ErrorMessage $null -BackendExitCode $backend.ExitCode
    exit 0
}
catch {
    $failure = $_.Exception.Message
    try { Write-ProgressResult -Stage failed -Message (Get-SanitizedError -Message $failure) -Percent 0 } catch { }
    try { Write-FinalResult -Ok $false -Payload $null -ErrorMessage $failure -BackendExitCode -1 } catch { }
    if ($isTestMode) { [Console]::Error.WriteLine($failure); [Console]::Error.WriteLine($_.ScriptStackTrace) }
    elseif (-not $resultChannelReady) {
        $safeFailure = Get-SanitizedError -Message $failure
        [Console]::Error.WriteLine($safeFailure)
    }
    exit 1
}
finally {
    if ($null -ne $reportPath -and [IO.File]::Exists($reportPath)) { try { [IO.File]::Delete($reportPath) } catch { } }
    if ($null -ne $lockStream) { Exit-RunLock -Stream $lockStream }
}
