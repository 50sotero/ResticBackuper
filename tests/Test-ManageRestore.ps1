$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$managerSource = Join-Path $projectRoot 'src\Manage-Restore.ps1'
$restoreSource = Join-Path $projectRoot 'src\restore.py'
$realPython = (Get-Command python -ErrorAction Stop).Source
$powershell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$encoding = [Text.UTF8Encoding]::new($false)
$planId = '12345678-1234-4abc-8def-1234567890ab'
$generation = [long]7
$snapshotId = 'b' * 64
$legacySnapshotId = 'f' * 64
$repositoryId = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
$assertions = 0
$roots = [Collections.Generic.List[string]]::new()

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
    $script:assertions++
}

function Get-Hash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-BytesHash {
    param([byte[]]$Bytes)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Write-Json {
    param([string]$Path, [object]$Value)
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 30) + "`n"), $encoding)
}

function Get-VolumeSerial {
    param([string]$Path)
    if (-not ('RestoreManagerTests.Volume' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace RestoreManagerTests {
  public static class Volume {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool GetVolumeInformation(string root, System.Text.StringBuilder volume, int volumeSize,
      out uint serial, out uint maximumComponentLength, out uint fileSystemFlags,
      System.Text.StringBuilder fileSystemName, int fileSystemNameSize);
    public static string Serial(string root) {
      uint serial, maximum, flags;
      if (!GetVolumeInformation(root, null, 0, out serial, out maximum, out flags, null, 0))
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
      return serial.ToString("X8");
    }
  }
}
'@
    }
    return [RestoreManagerTests.Volume]::Serial([IO.Path]::GetPathRoot($Path))
}

function New-FileRecord {
    param([string]$Root, [string]$Path)
    return [ordered]@{
        relative_path = $Path.Substring($Root.Length + 1)
        bytes = (Get-Item -LiteralPath $Path).Length
        sha256 = Get-Hash -Path $Path
    }
}

$compilerRoot = Join-Path ([IO.Path]::GetTempPath()) ('RSTR-C-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[void][IO.Directory]::CreateDirectory($compilerRoot)
$fakeRestic = Join-Path $compilerRoot 'restic.exe'
$fakeResticSource = @'
using System;
using System.IO;
using System.Linq;
using System.Text;
public static class FakeRestic {
  static string After(string[] args, string name) {
    int index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length) throw new Exception("missing " + name);
    return args[index + 1];
  }
  static string Escape(string value) {
    return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
  }
  public static int Main(string[] args) {
    string log = Environment.GetEnvironmentVariable("FAKE_RESTIC_LOG");
    if (!String.IsNullOrEmpty(log)) File.AppendAllText(log, String.Join("\t", args) + Environment.NewLine);
    string plan = Environment.GetEnvironmentVariable("FAKE_PLAN_ID");
    string source = Environment.GetEnvironmentVariable("FAKE_SOURCE");
    bool legacy = Environment.GetEnvironmentVariable("FAKE_LEGACY_SNAPSHOT") == "1";
    string snapshot = new String(legacy ? 'f' : 'b', 64);
    if (args.Contains("cat") && args.Contains("config")) {
      Console.WriteLine("{\"id\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"version\":2}"); return 0;
    }
    if (args.Contains("snapshots")) {
      string tags = legacy
        ? "\"scheduled\""
        : "\"scheduled\",\"restic-backuper-plan:" + plan + "\",\"restic-backuper-generation:7\"";
      Console.WriteLine("[{\"id\":\"" + snapshot + "\",\"short_id\":\"" + snapshot.Substring(0, 8) + "\",\"time\":\"2026-07-20T02:00:00Z\",\"hostname\":\"FIXTURE\",\"tags\":[" + tags + "],\"paths\":[\"" + Escape(source) + "\"],\"summary\":{\"total_files_processed\":2,\"total_bytes_processed\":42}}]"); return 0;
    }
    if (args.Contains("ls")) {
      if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RESTIC_PASSWORD"))) {
        string root = Path.GetFullPath(source); string drive = root.Substring(0, 1).ToUpperInvariant();
        string tail = root.Substring(2).Replace('\\', '/').Trim('/'); string prefix = "/" + drive + "/" + tail;
        Console.WriteLine("{\"name\":\"canary.txt\",\"type\":\"file\",\"path\":\"" + Escape(prefix + "/canary.txt") + "\",\"size\":7}");
        Console.WriteLine("{\"name\":\"live.txt\",\"type\":\"file\",\"path\":\"" + Escape(prefix + "/live.txt") + "\",\"size\":21}");
        Console.WriteLine("{\"name\":\"notes.txt\",\"type\":\"file\",\"path\":\"" + Escape(prefix + "/notes.txt") + "\",\"size\":14}");
        return 0;
      }
      Console.WriteLine("{\"name\":\"documents\",\"type\":\"dir\",\"path\":\"/documents\"}");
      Console.WriteLine("{\"name\":\"notes.txt\",\"type\":\"file\",\"path\":\"/notes.txt\",\"size\":42}");
      return 0;
    }
    if (args.Contains("restore")) {
      string target = After(args, "--target"); Directory.CreateDirectory(target);
      if (Environment.GetEnvironmentVariable("FAKE_RESTORE_PARTIAL") == "1") {
        File.WriteAllText(Path.Combine(target, "partial.txt"), "retained partial data"); return 23;
      }
      int includeCount = args.Count(value => value == "--include");
      if (includeCount > 0 && !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RESTIC_PASSWORD"))) {
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--include") {
          string include = args[i + 1]; string[] parts = include.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
          string restored = target; foreach (string part in parts) restored = Path.Combine(restored, part);
          Directory.CreateDirectory(Path.GetDirectoryName(restored));
          string content = include.EndsWith("/canary.txt") ? "canary\n" : include.EndsWith("/live.txt") ? "live source sentinel\n" : "fixture notes\n";
          File.WriteAllText(restored, content, new UTF8Encoding(false));
        }
      } else File.WriteAllText(Path.Combine(target, "restored.txt"), "verified restored data");
      return 0;
    }
    Console.Error.WriteLine("forbidden fake Restic command"); return 91;
  }
}
'@
Add-Type -TypeDefinition $fakeResticSource -OutputAssembly $fakeRestic -OutputType ConsoleApplication

$pythonWrapper = Join-Path $compilerRoot 'python.exe'
$escapedPython = $realPython.Replace('\', '\\').Replace('"', '\"')
$pythonWrapperSource = @"
using System;
using System.Diagnostics;
using System.Text;
public static class PythonWrapper {
  static string Quote(string value) {
    if (value.Length > 0 && value.IndexOfAny(new[] {' ', '\t', '"'}) < 0) return value;
    StringBuilder result = new StringBuilder("\""); int slashes = 0;
    foreach (char item in value) {
      if (item == '\\') { slashes++; continue; }
      if (item == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
      if (slashes > 0) { result.Append('\\', slashes); slashes = 0; }
      result.Append(item);
    }
    if (slashes > 0) result.Append('\\', slashes * 2);
    return result.Append('"').ToString();
  }
  public static int Main(string[] args) {
    ProcessStartInfo start = new ProcessStartInfo(); start.FileName = "$escapedPython";
    start.Arguments = String.Join(" ", Array.ConvertAll(args, Quote)); start.UseShellExecute = false;
    using (Process process = Process.Start(start)) { process.WaitForExit(); return process.ExitCode; }
  }
}
"@
Add-Type -TypeDefinition $pythonWrapperSource -OutputAssembly $pythonWrapper -OutputType ConsoleApplication

function New-Fixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('RSTR-T-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $roots.Add($root)
    $install = Join-Path $root 'ProgramFiles\ResticBackuper'
    $state = Join-Path $root 'ProgramData\ResticBackuper'
    $repository = Join-Path $root 'Repository\Personal'
    $recovery = Join-Path $root 'Repository\RecoveryTools'
    $source = Join-Path $root 'Source'
    foreach ($directory in @($install, (Join-Path $install 'Python'), $state, $repository, $recovery, $source)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
    Copy-Item -LiteralPath $managerSource -Destination (Join-Path $install 'Manage-Restore.ps1')
    Copy-Item -LiteralPath $restoreSource -Destination (Join-Path $install 'restore.py')
    Copy-Item -LiteralPath $fakeRestic -Destination (Join-Path $install 'restic.exe')
    Copy-Item -LiteralPath $pythonWrapper -Destination (Join-Path $install 'Python\python.exe')
    [IO.File]::WriteAllText((Join-Path $install 'secret_store.py'), "# intentionally unused fixture helper`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $install 'excludes.txt'), "node_modules`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $source 'canary.txt'), "canary`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $source 'live.txt'), "live source sentinel`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $source 'notes.txt'), "fixture notes`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $root 'RecoveryKey.txt'), "Password: fixture-recovery-password-material-that-is-long-enough-12345`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $repository 'config'), "repository config sentinel`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $repository 'pack'), "encrypted repository sentinel`n", $encoding)
    [IO.File]::WriteAllBytes((Join-Path $state 'run.lock'), [byte[]](0))
    $serial = Get-VolumeSerial -Path $repository
    $config = [ordered]@{
        schema_version = 1; plan_id = $planId; config_generation = $generation
        repository = $repository; repository_volume_serial = $serial
        restic_executable = Join-Path $install 'restic.exe'; recovery_tools_directory = $recovery
        python_executable = Join-Path $install 'Python\python.exe'; state_directory = $state
        secret_file = Join-Path $state 'missing-secret.json'; recovery_key_file = Join-Path $root 'RecoveryKey.txt'
        exclude_file = Join-Path $install 'excludes.txt'; canary_file = Join-Path $source 'canary.txt'
        hostname = 'FIXTURE'; scheduled_tag = 'scheduled'; use_vss = $false
        cloud_placeholder_policy = 'strict'; sources = @($source)
        source_identities = [ordered]@{ $source = [ordered]@{ expected_volume_serial = $serial } }
    }
    $configPath = Join-Path $install 'backup-config.json'
    Write-Json -Path $configPath -Value $config
    Write-Json -Path (Join-Path $state 'last-success.json') -Value ([ordered]@{
        schema_version = 1; plan_id = $planId; config_generation = $generation
        state = 'success'; repository_id = $repositoryId; snapshot_id = $snapshotId
        verification_complete = $true
        verification = [ordered]@{
            repository_structure = $true
            canary = [ordered]@{
                verified = $true; snapshot_path = ('/' + $source.Substring(0,1).ToUpperInvariant() + '/' + $source.Substring(3).Replace('\','/') + '/canary.txt')
                bytes = 7; sha256 = (Get-Hash -Path (Join-Path $source 'canary.txt'))
            }
        }
    })
    $manifestFiles = @(Get-ChildItem -LiteralPath $install -Recurse -File -Force | Sort-Object FullName)
    Write-Json -Path (Join-Path $install 'runtime-manifest.json') -Value ([ordered]@{
        schema_version = 1; file_count = $manifestFiles.Count
        files = @($manifestFiles | ForEach-Object { New-FileRecord -Root $install -Path $_.FullName })
    })
    [IO.File]::WriteAllText(
        (Join-Path $install 'scheduled-task.xml'),
        "<primary-task-fixture />`n",
        $encoding
    )
    [IO.File]::WriteAllText(
        (Join-Path $install 'google-drive-verification-task.xml'),
        "<verification-task-fixture />`n",
        $encoding
    )
    $log = Join-Path $root 'restic.log'
    return [pscustomobject]@{
        Root=$root; Install=$install; State=$state; Repository=$repository; Recovery=$recovery;
        Source=$source; ConfigPath=$configPath; Log=$log
    }
}

function Invoke-Manager {
    param(
        [pscustomobject]$Fixture,
        [string]$Action,
        [string]$Snapshot = '',
        [string]$TreePath = '',
        [string]$Target = '',
        [string[]]$Includes = @(),
        [bool]$AllowLegacy = $false,
        [switch]$LegacyFixture,
        [string]$Plan = $planId,
        [long]$ConfigGeneration = $generation,
        [string]$DigestOverride = ''
    )
    $nonce = ([Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')).ToLowerInvariant()
    $configHash = Get-Hash -Path $Fixture.ConfigPath
    $includesJson = ConvertTo-Json -InputObject @($Includes) -Compress
    [byte[]]$includeBytes = $encoding.GetBytes($includesJson)
    $includeHash = Get-BytesHash -Bytes $includeBytes
    $canonicalTarget = if ($Target) {
        [IO.Path]::GetFullPath($Target).TrimEnd('\')
    } elseif ($Action -eq 'restore_drill') {
        [IO.Path]::GetFullPath((Join-Path $Fixture.Root "ProgramData\ResticBackuper-RestoreDrills\drill-$nonce")).TrimEnd('\')
    } else { '' }
    $payload = @(
        'ResticBackuper.RestoreRequest.v2', $sid, $Action, $configHash, $Plan,
        $ConfigGeneration.ToString([Globalization.CultureInfo]::InvariantCulture),
        $(if ($AllowLegacy) { '1' } else { '0' }),
        $Snapshot, $TreePath, $canonicalTarget, $includeHash, $nonce
    ) -join "`n"
    $digest = if ($DigestOverride) { $DigestOverride } else { Get-BytesHash -Bytes $encoding.GetBytes($payload) }
    $resultPath = Join-Path $Fixture.State "RestoreManagerResults\$nonce.json"
    $arguments = @(
        '-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $Fixture.Install 'Manage-Restore.ps1'),
        '-Action',$Action,
        '-IncludesBase64',[Convert]::ToBase64String($includeBytes),
        '-ExpectedUserSid',$sid,'-ExpectedConfigSha256',$configHash,
        '-ExpectedPlanId',$Plan,'-ExpectedConfigGeneration',$ConfigGeneration,
        '-AllowLegacyUnbound',$(if ($AllowLegacy) { '1' } else { '0' }),
        '-ResultPath',$resultPath,'-RequestNonce',$nonce,'-RequestDigest',$digest,
        '-TestRoot',$Fixture.Root
    )
    if ($Snapshot) { $arguments += @('-SnapshotId',$Snapshot) }
    if ($TreePath) { $arguments += @('-TreePath',$TreePath) }
    if ($Target) { $arguments += @('-Target',$Target) }
    $env:FAKE_RESTIC_LOG = $Fixture.Log
    $env:FAKE_PLAN_ID = $planId
    $env:FAKE_SOURCE = $Fixture.Source
    if ($LegacyFixture) { $env:FAKE_LEGACY_SNAPSHOT = '1' }
    else { Remove-Item Env:FAKE_LEGACY_SNAPSHOT -ErrorAction SilentlyContinue }
    $priorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $powershell @arguments 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorPreference
        Remove-Item Env:FAKE_LEGACY_SNAPSHOT -ErrorAction SilentlyContinue
    }
    $result = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } else { $null }
    $progress = if (Test-Path -LiteralPath ($resultPath + '.progress.json')) {
        Get-Content -LiteralPath ($resultPath + '.progress.json') -Raw | ConvertFrom-Json
    } else { $null }
    return [pscustomobject]@{ ExitCode=$exitCode; Result=$result; Progress=$progress; Output=$output; Nonce=$nonce; Digest=$digest; ConfigHash=$configHash }
}

$productionConfig = Join-Path $env:ProgramFiles 'ResticBackuper\backup-config.json'
$productionHashBefore = if (Test-Path -LiteralPath $productionConfig) { Get-Hash -Path $productionConfig } else { $null }
$taskXmlBefore = try { (Export-ScheduledTask -TaskName 'ResticBackuper' -ErrorAction Stop).Replace("`r`n", "`n") } catch { $null }

try {
    $fixture = New-Fixture
    $repositoryBefore = Get-Hash -Path (Join-Path $fixture.Repository 'pack')
    $sourceBefore = Get-Hash -Path (Join-Path $fixture.Source 'live.txt')

    $list = Invoke-Manager -Fixture $fixture -Action list_snapshots
    if ($list.ExitCode -ne 0) {
        Write-Output ("snapshot-list output: " + ($list.Output -join ' | '))
        if ($null -ne $list.Result) { Write-Output ("snapshot-list result: " + ($list.Result | ConvertTo-Json -Compress -Depth 20)) }
    }
    Assert-True ($list.ExitCode -eq 0 -and $list.Result.ok -eq $true) 'snapshot listing succeeds'
    Assert-True ([string]$list.Result.request_digest -ceq $list.Digest -and [string]$list.Result.expected_config_sha256 -ceq $list.ConfigHash) 'snapshot result request/config binding'
    Assert-True ([string]$list.Result.plan_id -ceq $planId -and [long]$list.Result.config_generation -eq $generation) 'snapshot result plan binding'
    Assert-True ($list.Result.allow_legacy_unbound -eq $false) 'snapshot result binds legacy opt-in off'
    Assert-True ([string]$list.Result.payload.schema -ceq 'ResticBackuper.SnapshotList.v1') 'snapshot payload schema'
    Assert-True (@($list.Result.payload.snapshots).Count -eq 1 -and [string]$list.Result.payload.snapshots[0].id -ceq $snapshotId) 'snapshot payload exact item'
    Assert-True ([string]$list.Progress.stage -ceq 'complete' -and [int]$list.Progress.percent -eq 100) 'snapshot progress completes'

    $tree = Invoke-Manager -Fixture $fixture -Action list_tree -Snapshot $snapshotId -TreePath '/'
    Assert-True ($tree.ExitCode -eq 0 -and $tree.Result.ok -eq $true) 'tree listing succeeds'
    Assert-True ([string]$tree.Result.payload.snapshot_id -ceq $snapshotId -and @($tree.Result.payload.entries).Count -eq 2) 'tree result exact snapshot and entries'

    $legacyFixture = New-Fixture
    $legacyList = Invoke-Manager -Fixture $legacyFixture -Action list_snapshots -LegacyFixture
    Assert-True ($legacyList.ExitCode -eq 0 -and $legacyList.Result.ok -eq $true) 'matching legacy snapshot listing succeeds'
    Assert-True (@($legacyList.Result.payload.snapshots).Count -eq 1 -and
        [string]$legacyList.Result.payload.snapshots[0].id -ceq $legacySnapshotId -and
        [string]$legacyList.Result.payload.snapshots[0].binding_state -ceq 'legacy_unbound') 'matching legacy snapshot is clearly classified'
    $legacyWithoutOptIn = Invoke-Manager -Fixture $legacyFixture -Action list_tree -Snapshot $legacySnapshotId -LegacyFixture
    Assert-True ($legacyWithoutOptIn.ExitCode -ne 0 -and $legacyWithoutOptIn.Result.ok -eq $false) 'legacy tree access fails without explicit opt-in'
    $legacyTree = Invoke-Manager -Fixture $legacyFixture -Action list_tree -Snapshot $legacySnapshotId -AllowLegacy $true -LegacyFixture
    Assert-True ($legacyTree.ExitCode -eq 0 -and $legacyTree.Result.ok -eq $true -and
        $legacyTree.Result.allow_legacy_unbound -eq $true -and
        [string]$legacyTree.Result.payload.snapshot_binding -ceq 'legacy_unbound') 'exact legacy tree access succeeds with bound opt-in'
    $legacyTarget = Join-Path $legacyFixture.Root 'Restored\Legacy'
    $legacyRestore = Invoke-Manager -Fixture $legacyFixture -Action restore -Snapshot $legacySnapshotId -Target $legacyTarget -AllowLegacy $true -LegacyFixture
    Assert-True ($legacyRestore.ExitCode -eq 0 -and $legacyRestore.Result.ok -eq $true -and
        [string]$legacyRestore.Result.payload.snapshot_binding -ceq 'legacy_unbound') 'exact legacy restore succeeds with bound opt-in'
    $legacyHistory = Get-Content -LiteralPath (Join-Path $legacyFixture.State 'restore-history.json') -Raw | ConvertFrom-Json
    Assert-True ([string]$legacyHistory.entries[0].snapshot_binding -ceq 'legacy_unbound') 'legacy restore classification is recorded in protected history'

    $target = Join-Path $fixture.Root 'Restored\Verified'
    $restore = Invoke-Manager -Fixture $fixture -Action restore -Snapshot $snapshotId -Target $target -Includes @('/notes.txt')
    Assert-True ($restore.ExitCode -eq 0 -and $restore.Result.ok -eq $true) 'verified restore succeeds'
    Assert-True ([string]$restore.Result.payload.result -ceq 'verified' -and $restore.Result.payload.verified -eq $true) 'verified restore report accepted'
    Assert-True (Test-Path -LiteralPath (Join-Path $target 'restored.txt') -PathType Leaf) 'verified restore target created'
    $verifiedHistory = Get-Content -LiteralPath (Join-Path $fixture.State 'restore-history.json') -Raw | ConvertFrom-Json
    Assert-True ($restore.Result.history_recorded -eq $true -and @($verifiedHistory.entries).Count -eq 1 -and $verifiedHistory.entries[0].verified -eq $true) 'verified restore is recorded in bounded protected history'

    $drillFixture = New-Fixture
    $drill = Invoke-Manager -Fixture $drillFixture -Action restore_drill
    if ($drill.ExitCode -ne 0) {
        Write-Output ("drill output: " + ($drill.Output -join ' | '))
        if ($null -ne $drill.Result) { Write-Output ("drill result: " + ($drill.Result | ConvertTo-Json -Compress -Depth 20)) }
    }
    Assert-True ($drill.ExitCode -eq 0 -and $drill.Result.ok -eq $true -and $drill.Result.history_recorded -eq $true) 'recovery-key representative drill succeeds'
    Assert-True ([string]$drill.Result.payload.drill_kind -ceq 'recovery_key_representative' -and
        [string]$drill.Result.payload.credential_source -ceq 'recovery_key' -and
        [bool]$drill.Result.payload.canary_verified -and [long]$drill.Result.payload.sample_file_count -eq 2) 'drill report contains verified canary and bounded sample evidence'
    $expectedDrillTarget = [IO.Path]::GetFullPath((Join-Path $drillFixture.Root "ProgramData\ResticBackuper-RestoreDrills\drill-$($drill.Nonce)")).TrimEnd('\')
    Assert-True ([string]$drill.Result.target -ceq $expectedDrillTarget) 'drill target is nonce-bound and manager-derived'
    $drillHistory = Get-Content -LiteralPath (Join-Path $drillFixture.State 'restore-history.json') -Raw | ConvertFrom-Json
    Assert-True ([int]$drillHistory.schema_version -eq 2 -and @($drillHistory.entries).Count -eq 1 -and
        [string]$drillHistory.entries[0].kind -ceq 'recovery_key_representative_drill' -and
        [string]$drillHistory.entries[0].repository_id -ceq $repositoryId -and
        [long]$drillHistory.entries[0].snapshot_generation -eq $generation) 'only complete drill evidence is durably recorded'

    $partialDrillFixture = New-Fixture
    $env:FAKE_RESTORE_PARTIAL = '1'
    try { $partialDrill = Invoke-Manager -Fixture $partialDrillFixture -Action restore_drill }
    finally { Remove-Item Env:FAKE_RESTORE_PARTIAL -ErrorAction SilentlyContinue }
    $expectedPartialDrillTarget = [IO.Path]::GetFullPath((Join-Path $partialDrillFixture.Root "ProgramData\ResticBackuper-RestoreDrills\drill-$($partialDrill.Nonce)")).TrimEnd('\')
    Assert-True ($partialDrill.ExitCode -eq 23 -and $partialDrill.Result.ok -eq $false -and
        [string]$partialDrill.Result.payload.result -ceq 'partial' -and
        $partialDrill.Result.payload.partial_target_retained -eq $true -and
        [string]$partialDrill.Result.payload.target -ceq $expectedPartialDrillTarget) 'failed drill retains and reports its manager-derived target'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $partialDrillFixture.State 'restore-history.json'))) 'failed drill never records passing readiness history'

    $partialFixture = New-Fixture
    $partialTarget = Join-Path $partialFixture.Root 'Restored\Partial'
    $env:FAKE_RESTORE_PARTIAL = '1'
    try { $partial = Invoke-Manager -Fixture $partialFixture -Action restore -Snapshot $snapshotId -Target $partialTarget }
    finally { Remove-Item Env:FAKE_RESTORE_PARTIAL -ErrorAction SilentlyContinue }
    Assert-True ($partial.ExitCode -eq 23 -and $partial.Result.ok -eq $false) 'partial restore reports backend failure'
    Assert-True ([string]$partial.Result.payload.result -ceq 'partial' -and $partial.Result.payload.partial_target_retained -eq $true) 'partial target retention reported'
    Assert-True (Test-Path -LiteralPath (Join-Path $partialTarget 'partial.txt') -PathType Leaf) 'partial target retained'
    $partialHistory = Get-Content -LiteralPath (Join-Path $partialFixture.State 'restore-history.json') -Raw | ConvertFrom-Json
    Assert-True ($partial.Result.history_recorded -eq $true -and @($partialHistory.entries).Count -eq 1 -and $partialHistory.entries[0].partial_target_retained -eq $true) 'partial restore is retained in protected history'

    $unsafeFixture = New-Fixture
    $unsafe = Invoke-Manager -Fixture $unsafeFixture -Action restore -Snapshot $snapshotId -Target $unsafeFixture.Source
    Assert-True ($unsafe.ExitCode -ne 0 -and $unsafe.Result.ok -eq $false -and $unsafe.Result.error -match 'must not overlap') 'live source restore target rejected'
    Assert-True (-not ((Get-Content -LiteralPath $unsafeFixture.Log -Raw) -match '(?m)(^|\t)restore(\t|$)')) 'unsafe target rejected before Restic restore'

    $digestFixture = New-Fixture
    $badDigest = Invoke-Manager -Fixture $digestFixture -Action list_snapshots -DigestOverride ('0' * 64)
    Assert-True ($badDigest.ExitCode -ne 0 -and $null -eq $badDigest.Result) 'forged digest rejected before result channel'

    $planFixture = New-Fixture
    $wrongPlan = Invoke-Manager -Fixture $planFixture -Action list_snapshots -Plan 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee'
    Assert-True ($wrongPlan.ExitCode -ne 0 -and $wrongPlan.Result.ok -eq $false -and $wrongPlan.Result.error -match 'another backup plan') 'wrong requested plan rejected'

    $journalFixture = New-Fixture
    [IO.File]::WriteAllText((Join-Path $journalFixture.State 'plan-migration.journal.json'), "{}`n", $encoding)
    $journal = Invoke-Manager -Fixture $journalFixture -Action list_snapshots
    Assert-True ($journal.ExitCode -ne 0 -and $journal.Result.error -match 'pending protected-operation journal') 'pending migration journal gates restore'

    $lockFixture = New-Fixture
    $held = [IO.File]::Open((Join-Path $lockFixture.State 'run.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    try { $held.Lock(0,1); $locked = Invoke-Manager -Fixture $lockFixture -Action list_snapshots }
    finally { try { $held.Unlock(0,1) } finally { $held.Dispose() } }
    Assert-True ($locked.ExitCode -ne 0 -and $locked.Result.error -match 'run lock') 'shared backup lock gates restore'

    $orphanFixture = New-Fixture
    $orphanDirectory = Join-Path $orphanFixture.State 'RestoreManagerResults'
    [void][IO.Directory]::CreateDirectory($orphanDirectory)
    $orphanPath = Join-Path $orphanDirectory (('a' * 64) + '.restore-report.json')
    [IO.File]::WriteAllText($orphanPath, "{}`n", $encoding)
    $orphanRecovery = Invoke-Manager -Fixture $orphanFixture -Action list_snapshots
    Assert-True ($orphanRecovery.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $orphanPath)) 'orphaned backend report is recovered under the run lock'

    $unmanifestedFixture = New-Fixture
    [IO.File]::WriteAllText((Join-Path $unmanifestedFixture.Install 'injected.txt'), "unexpected`n", $encoding)
    $unmanifested = Invoke-Manager -Fixture $unmanifestedFixture -Action list_snapshots
    Assert-True ($unmanifested.ExitCode -ne 0 -and $unmanifested.Result.error -match 'unmanifested or missing files') 'unmanifested protected runtime file is rejected'

    $logText = Get-Content -LiteralPath $fixture.Log -Raw
    Assert-True ($logText -match '\tcat\tconfig' -and $logText -match '\tsnapshots\t--json' -and $logText -match '\tls\t--json') 'read-only Restic metadata commands used'
    Assert-True ($logText -match '\trestore\t' -and $logText -match '\t--overwrite\tnever\t' -and $logText -match '\t--verify') 'restore uses non-overwrite verification contract'
    Assert-True ($logText -notmatch '(?im)(^|\t)(backup|init|forget|prune|delete)(\t|$)') 'manager never invokes destructive/history-changing Restic commands'
    Assert-True ((Get-Hash -Path (Join-Path $fixture.Repository 'pack')) -eq $repositoryBefore) 'repository content unchanged'
    Assert-True ((Get-Hash -Path (Join-Path $fixture.Source 'live.txt')) -eq $sourceBefore) 'live source unchanged'

    $productionHashAfter = if (Test-Path -LiteralPath $productionConfig) { Get-Hash -Path $productionConfig } else { $null }
    $taskXmlAfter = try { (Export-ScheduledTask -TaskName 'ResticBackuper' -ErrorAction Stop).Replace("`r`n", "`n") } catch { $null }
    Assert-True ($productionHashAfter -eq $productionHashBefore) 'production protected config untouched'
    Assert-True ($taskXmlAfter -ceq $taskXmlBefore) 'production task untouched'

    [ordered]@{
        ok = $true; assertions = $assertions; disposable_roots = $roots.Count
        snapshot_list = 'passed'; tree_list = 'passed'; verified_restore = 'passed'
        partial_restore_retained = 'passed'; unsafe_target = 'passed'; plan_and_digest_binding = 'passed'; legacy_unbound_opt_in = 'passed'
        journal_and_lock_gates = 'passed'; orphan_recovery_and_manifest_parity = 'passed'; no_repository_or_source_mutation = 'passed'
        production_untouched = 'passed'
    } | ConvertTo-Json -Depth 3
}
finally {
    foreach ($name in @('FAKE_RESTIC_LOG','FAKE_PLAN_ID','FAKE_SOURCE','FAKE_RESTORE_PARTIAL','FAKE_LEGACY_SNAPSHOT')) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    foreach ($root in $roots) {
        $full = [IO.Path]::GetFullPath($root).TrimEnd('\')
        $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if ($full.StartsWith($temp + '\RSTR-T-', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $full)) {
            Remove-Item -LiteralPath $full -Recurse -Force
        }
    }
    if ((Test-Path -LiteralPath $compilerRoot) -and
        $compilerRoot.StartsWith(([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\RSTR-C-'), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $compilerRoot -Recurse -Force
    }
}
