using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ResticBackuper.Dashboard
{
    internal sealed class RestoreSnapshot
    {
        public string Id { get; private set; }
        public string ShortId { get; private set; }
        public string Time { get; private set; }
        public string Hostname { get; private set; }
        public long ConfigGeneration { get; private set; }
        public long FileCount { get; private set; }
        public long ByteCount { get; private set; }
        public string SourceSummary { get; private set; }
        public string BindingState { get; private set; }
        public bool IsLegacyUnbound
        {
            get { return BindingState == "legacy_unbound"; }
        }

        public string WhenDisplay
        {
            get
            {
                DateTime parsed;
                return DateTime.TryParse(
                    Time,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed)
                    ? parsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    : Time;
            }
        }

        public string SizeDisplay
        {
            get { return FormatBytes(ByteCount); }
        }

        public string GenerationDisplay
        {
            get
            {
                return IsLegacyUnbound
                    ? "Legacy / unbound"
                    : "Generation " + ConfigGeneration.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal RestoreSnapshot(
            string id,
            string shortId,
            string time,
            string hostname,
            long configGeneration,
            long fileCount,
            long byteCount,
            string sourceSummary,
            string bindingState)
        {
            Id = id;
            ShortId = shortId;
            Time = time;
            Hostname = hostname;
            ConfigGeneration = configGeneration;
            FileCount = fileCount;
            ByteCount = byteCount;
            SourceSummary = sourceSummary;
            BindingState = bindingState;
        }

        private static string FormatBytes(long value)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double size = Math.Max(0, value);
            int index = 0;
            while (size >= 1024 && index < units.Length - 1)
            {
                size /= 1024;
                index++;
            }
            return size.ToString(index == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[index];
        }
    }

    internal sealed class RestoreTreeEntry
    {
        public string Name { get; private set; }
        public string EntryPath { get; private set; }
        public string EntryType { get; private set; }
        public long Size { get; private set; }

        public string SizeDisplay
        {
            get
            {
                if (EntryType == "dir")
                {
                    return "Folder";
                }
                string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
                double value = Math.Max(0, Size);
                int index = 0;
                while (value >= 1024 && index < units.Length - 1)
                {
                    value /= 1024;
                    index++;
                }
                return value.ToString(index == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[index];
            }
        }

        internal RestoreTreeEntry(string name, string path, string type, long size)
        {
            Name = name;
            EntryPath = path;
            EntryType = type;
            Size = size;
        }
    }

    internal sealed class ProtectedRestoreReport
    {
        public string Result { get; private set; }
        public string SnapshotId { get; private set; }
        public string Target { get; private set; }
        public bool Verified { get; private set; }
        public bool PartialTargetRetained { get; private set; }
        public int ResticExitCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public string SnapshotBinding { get; private set; }

        internal ProtectedRestoreReport(
            string result,
            string snapshotId,
            string target,
            bool verified,
            bool partialTargetRetained,
            int resticExitCode,
            string errorMessage,
            string snapshotBinding)
        {
            Result = result;
            SnapshotId = snapshotId;
            Target = target;
            Verified = verified;
            PartialTargetRetained = partialTargetRetained;
            ResticExitCode = resticExitCode;
            ErrorMessage = errorMessage;
            SnapshotBinding = snapshotBinding;
        }
    }

    internal sealed class RestoreManagerProgress
    {
        public string Stage { get; private set; }
        public string Message { get; private set; }
        public int Percent { get; private set; }

        internal RestoreManagerProgress(string stage, string message, int percent)
        {
            Stage = stage ?? string.Empty;
            Message = message ?? string.Empty;
            Percent = Math.Max(0, Math.Min(100, percent));
        }
    }

    internal sealed class RestoreManagerResult
    {
        public bool Succeeded { get; private set; }
        public bool UserCancelled { get; private set; }
        public bool Partial { get; private set; }
        public int ExitCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public IList<RestoreSnapshot> Snapshots { get; private set; }
        public IList<RestoreTreeEntry> Entries { get; private set; }
        public ProtectedRestoreReport Report { get; private set; }
        public bool? HistoryRecorded { get; private set; }
        public string HistoryError { get; private set; }

        internal static RestoreManagerResult SuccessSnapshots(IList<RestoreSnapshot> snapshots)
        {
            return new RestoreManagerResult
            {
                Succeeded = true,
                ExitCode = 0,
                Snapshots = snapshots ?? new List<RestoreSnapshot>()
            };
        }

        internal static RestoreManagerResult SuccessTree(IList<RestoreTreeEntry> entries)
        {
            return new RestoreManagerResult
            {
                Succeeded = true,
                ExitCode = 0,
                Entries = entries ?? new List<RestoreTreeEntry>()
            };
        }

        internal static RestoreManagerResult SuccessRestore(
            ProtectedRestoreReport report,
            bool? historyRecorded,
            string historyError)
        {
            return new RestoreManagerResult
            {
                Succeeded = true,
                ExitCode = 0,
                Report = report,
                HistoryRecorded = historyRecorded,
                HistoryError = historyError
            };
        }

        internal static RestoreManagerResult PartialRestore(
            ProtectedRestoreReport report,
            string message,
            int exitCode,
            bool? historyRecorded,
            string historyError)
        {
            return new RestoreManagerResult
            {
                Partial = true,
                Report = report,
                ErrorMessage = message,
                ExitCode = exitCode,
                HistoryRecorded = historyRecorded,
                HistoryError = historyError
            };
        }

        internal static RestoreManagerResult EvidenceFailure(
            ProtectedRestoreReport report,
            string message,
            int exitCode,
            string historyError)
        {
            return new RestoreManagerResult
            {
                Report = report,
                ErrorMessage = message,
                ExitCode = exitCode,
                HistoryRecorded = false,
                HistoryError = historyError
            };
        }

        internal static RestoreManagerResult Cancelled()
        {
            return new RestoreManagerResult { UserCancelled = true, ExitCode = 1223 };
        }

        internal static RestoreManagerResult Failure(string message, int exitCode)
        {
            return new RestoreManagerResult
            {
                ErrorMessage = string.IsNullOrWhiteSpace(message)
                    ? "The protected restore operation failed."
                    : message,
                ExitCode = exitCode
            };
        }
    }

    internal static class RestoreManagerLauncher
    {
        private const string RequestDomain = "ResticBackuper.RestoreRequest.v2";
        private const string ResultDirectoryName = "RestoreManagerResults";
        private const int MaximumResultBytes = 8 * 1024 * 1024;

        internal static RestoreManagerResult ListSnapshots(
            SourceConfiguration configuration,
            Action onElevatedProcessStarted,
            Action<RestoreManagerProgress> onProgress)
        {
            return Run(
                configuration,
                "list_snapshots",
                string.Empty,
                string.Empty,
                string.Empty,
                new string[0],
                false,
                onElevatedProcessStarted,
                onProgress);
        }

        internal static RestoreManagerResult ListTree(
            SourceConfiguration configuration,
            string snapshotId,
            string treePath,
            bool allowLegacyUnbound,
            Action onElevatedProcessStarted,
            Action<RestoreManagerProgress> onProgress)
        {
            return Run(
                configuration,
                "list_tree",
                NormalizeSnapshotId(snapshotId),
                NormalizeTreePath(treePath),
                string.Empty,
                new string[0],
                allowLegacyUnbound,
                onElevatedProcessStarted,
                onProgress);
        }

        internal static RestoreManagerResult Restore(
            SourceConfiguration configuration,
            string snapshotId,
            string target,
            IList<string> includes,
            bool allowLegacyUnbound,
            Action onElevatedProcessStarted,
            Action<RestoreManagerProgress> onProgress)
        {
            string normalizedTarget = SourceConfiguration.NormalizePath(target);
            return Run(
                configuration,
                "restore",
                NormalizeSnapshotId(snapshotId),
                string.Empty,
                normalizedTarget,
                includes ?? new string[0],
                allowLegacyUnbound,
                onElevatedProcessStarted,
                onProgress);
        }

        internal static RestoreManagerResult RunRecoveryDrill(
            SourceConfiguration configuration,
            Action onElevatedProcessStarted,
            Action<RestoreManagerProgress> onProgress)
        {
            return Run(
                configuration,
                "restore_drill",
                string.Empty,
                string.Empty,
                string.Empty,
                new string[0],
                false,
                onElevatedProcessStarted,
                onProgress);
        }

        private static RestoreManagerResult Run(
            SourceConfiguration configuration,
            string action,
            string snapshotId,
            string treePath,
            string target,
            IList<string> includes,
            bool allowLegacyUnbound,
            Action onElevatedProcessStarted,
            Action<RestoreManagerProgress> onProgress)
        {
            if (configuration == null)
            {
                return RestoreManagerResult.Failure(
                    "The protected backup configuration is unavailable.",
                    -1);
            }
            string managerPath = Path.Combine(configuration.InstallRoot, "Manage-Restore.ps1");
            if (!IsRegularFile(managerPath) || !IsRegularFile(configuration.ConfigurationPath))
            {
                return RestoreManagerResult.Failure(
                    "The protected restore manager is missing from the installation.",
                    -1);
            }
            if (configuration.ConfigGeneration <= 0 ||
                string.IsNullOrWhiteSpace(configuration.PlanId))
            {
                return RestoreManagerResult.Failure(
                    "The protected backup plan migration is incomplete.",
                    -1);
            }

            try
            {
                ValidateRequestFields(
                    action, snapshotId, treePath, target, includes, allowLegacyUnbound);
                string configHash = ComputeFileSha256(configuration.ConfigurationPath);
                string includesJson = new JavaScriptSerializer().Serialize(includes);
                byte[] includeBytes = new UTF8Encoding(false).GetBytes(includesJson);
                string includesHash = ComputeSha256(includeBytes);
                string includesBase64 = Convert.ToBase64String(includeBytes);
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                string userSid = identity.User == null ? null : identity.User.Value;
                if (string.IsNullOrWhiteSpace(userSid))
                {
                    return RestoreManagerResult.Failure(
                        "The current Windows user SID could not be determined.",
                        -1);
                }
                string nonce = CreateNonce();
                if (action == "restore_drill")
                {
                    target = BuildRecoveryDrillTarget(nonce);
                }
                string digest = ComputeRequestDigest(
                    userSid,
                    action,
                    configHash,
                    configuration.PlanId,
                    configuration.ConfigGeneration,
                    allowLegacyUnbound,
                    snapshotId,
                    treePath,
                    target,
                    includesHash,
                    nonce);
                string resultRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ResticBackuper",
                    ResultDirectoryName);
                string resultPath = Path.Combine(resultRoot, nonce + ".json");
                string progressPath = resultPath + ".progress.json";
                if (File.Exists(resultPath) || Directory.Exists(resultPath) ||
                    File.Exists(progressPath) || Directory.Exists(progressPath))
                {
                    return RestoreManagerResult.Failure(
                        "A protected restore result already uses this request nonce.",
                        -1);
                }
                string powerShell = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe");
                if (!IsRegularFile(powerShell))
                {
                    return RestoreManagerResult.Failure(
                        "Windows PowerShell is unavailable in System32.",
                        -1);
                }

                List<string> arguments = new List<string>(new[]
                {
                    "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                    "-WindowStyle", "Hidden", "-File", managerPath,
                    "-Action", action,
                    "-IncludesBase64", includesBase64,
                    "-ExpectedUserSid", userSid,
                    "-ExpectedConfigSha256", configHash,
                    "-ExpectedPlanId", configuration.PlanId,
                    "-ExpectedConfigGeneration", configuration.ConfigGeneration.ToString(CultureInfo.InvariantCulture),
                    "-AllowLegacyUnbound", allowLegacyUnbound ? "1" : "0",
                    "-ResultPath", resultPath,
                    "-RequestNonce", nonce,
                    "-RequestDigest", digest
                });
                if (snapshotId.Length > 0)
                {
                    arguments.Add("-SnapshotId");
                    arguments.Add(snapshotId);
                }
                if (treePath.Length > 0)
                {
                    arguments.Add("-TreePath");
                    arguments.Add(treePath);
                }
                if (target.Length > 0 && action != "restore_drill")
                {
                    arguments.Add("-Target");
                    arguments.Add(target);
                }

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = powerShell;
                start.Arguments = BuildArgumentString(arguments);
                start.WorkingDirectory = configuration.InstallRoot;
                start.UseShellExecute = true;
                start.Verb = "runas";
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.ErrorDialog = false;
                int exitCode;
                using (Process process = Process.Start(start))
                {
                    if (process == null)
                    {
                        return RestoreManagerResult.Failure(
                            "Windows did not start the protected restore manager.",
                            -1);
                    }
                    InvokeSafely(onElevatedProcessStarted);
                    string lastProgressFingerprint = string.Empty;
                    while (!process.WaitForExit(400))
                    {
                        PublishProgress(
                            progressPath,
                            nonce,
                            digest,
                            userSid,
                            action,
                            configuration.PlanId,
                            configuration.ConfigGeneration,
                            allowLegacyUnbound,
                            snapshotId,
                            target,
                            onProgress,
                            ref lastProgressFingerprint);
                    }
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    PublishProgress(
                        progressPath,
                        nonce,
                        digest,
                        userSid,
                        action,
                        configuration.PlanId,
                        configuration.ConfigGeneration,
                        allowLegacyUnbound,
                        snapshotId,
                        target,
                        onProgress,
                        ref lastProgressFingerprint);
                }
                return ReadBoundResult(
                    resultPath,
                    nonce,
                    digest,
                    userSid,
                    action,
                    configHash,
                    configuration.PlanId,
                    configuration.ConfigGeneration,
                    allowLegacyUnbound,
                    snapshotId,
                    treePath,
                    target,
                    includesHash,
                    exitCode);
            }
            catch (Win32Exception error)
            {
                return error.NativeErrorCode == 1223
                    ? RestoreManagerResult.Cancelled()
                    : RestoreManagerResult.Failure(Sanitize(error.Message), error.NativeErrorCode);
            }
            catch (Exception error)
            {
                return RestoreManagerResult.Failure(Sanitize(error.Message), -1);
            }
        }

        internal static string ComputeRequestDigest(
            string userSid,
            string action,
            string configHash,
            string planId,
            long configGeneration,
            bool allowLegacyUnbound,
            string snapshotId,
            string treePath,
            string target,
            string includesHash,
            string nonce)
        {
            string payload = string.Join("\n", new[]
            {
                RequestDomain,
                userSid,
                action,
                configHash.ToLowerInvariant(),
                planId,
                configGeneration.ToString(CultureInfo.InvariantCulture),
                allowLegacyUnbound ? "1" : "0",
                snapshotId ?? string.Empty,
                treePath ?? string.Empty,
                target ?? string.Empty,
                includesHash.ToLowerInvariant(),
                nonce.ToLowerInvariant()
            });
            return ComputeSha256(new UTF8Encoding(false).GetBytes(payload));
        }

        private static string BuildRecoveryDrillTarget(string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce) || nonce.Length != 64)
            {
                throw new ArgumentException("The recovery-drill nonce is invalid.");
            }
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ResticBackuper-RestoreDrills");
            return SourceConfiguration.NormalizePath(Path.Combine(root, "drill-" + nonce));
        }

        private static RestoreManagerResult ReadBoundResult(
            string resultPath,
            string nonce,
            string digest,
            string userSid,
            string action,
            string configHash,
            string planId,
            long generation,
            bool allowLegacyUnbound,
            string snapshotId,
            string treePath,
            string target,
            string includesHash,
            int exitCode)
        {
            for (int attempt = 0; attempt < 20 && !File.Exists(resultPath); attempt++)
            {
                Thread.Sleep(50);
            }
            string resultRoot = Path.GetDirectoryName(resultPath);
            string stateRoot = string.IsNullOrWhiteSpace(resultRoot)
                ? null
                : Path.GetDirectoryName(resultRoot);
            if (!File.Exists(resultPath) || string.IsNullOrWhiteSpace(stateRoot) ||
                !string.Equals(Path.GetFileName(stateRoot), "ResticBackuper", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(resultRoot), ResultDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(resultPath), nonce + ".json", StringComparison.Ordinal) ||
                !IsRegularFile(resultPath) ||
                !HasProtectedAcl(stateRoot, userSid) || !HasProtectedAcl(resultRoot, userSid))
            {
                return RestoreManagerResult.Failure(
                    "The protected restore manager did not return a trustworthy result.",
                    exitCode);
            }
            try
            {
                using (FileStream stream = new FileStream(
                    resultPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    8192,
                    FileOptions.SequentialScan))
                {
                    if (!HasProtectedAcl(stream.GetAccessControl(), userSid) ||
                        stream.Length <= 0 || stream.Length > MaximumResultBytes)
                    {
                        return RestoreManagerResult.Failure(
                            "The protected restore result file is not trustworthy.",
                            exitCode);
                    }
                    using (StreamReader reader = new StreamReader(
                        stream,
                        new UTF8Encoding(false, true),
                        true,
                        8192,
                        true))
                    {
                        IDictionary<string, object> document =
                            new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as
                                IDictionary<string, object>;
                        return ValidateResult(
                            document,
                            nonce,
                            digest,
                            userSid,
                            action,
                            configHash,
                            planId,
                            generation,
                            allowLegacyUnbound,
                            snapshotId,
                            treePath,
                            target,
                            includesHash,
                            exitCode);
                    }
                }
            }
            catch (Exception error)
            {
                return RestoreManagerResult.Failure(
                    "The protected restore result could not be validated: " + Sanitize(error.Message),
                    exitCode);
            }
        }

        private static RestoreManagerResult ValidateResult(
            IDictionary<string, object> document,
            string nonce,
            string digest,
            string userSid,
            string action,
            string configHash,
            string planId,
            long generation,
            bool allowLegacyUnbound,
            string snapshotId,
            string treePath,
            string target,
            string includesHash,
            int exitCode)
        {
            if (document == null || ReadLong(document, "schema_version") != 1 ||
                !FixedEquals(ReadString(document, "request_nonce"), nonce) ||
                !FixedEquals(ReadString(document, "request_digest"), digest) ||
                !FixedEquals(ReadString(document, "request_user_sid"), userSid) ||
                !FixedEquals(ReadString(document, "action"), action) ||
                !FixedEquals(ReadString(document, "expected_config_sha256"), configHash) ||
                !FixedEquals(ReadString(document, "plan_id"), planId) ||
                ReadLong(document, "config_generation") != generation ||
                ReadBoolean(document, "allow_legacy_unbound") != allowLegacyUnbound ||
                (action != "restore_drill" &&
                    !FixedEquals(NullToEmpty(ReadOptionalString(document, "snapshot_id")), snapshotId)) ||
                !FixedEquals(NullToEmpty(ReadOptionalString(document, "tree_path")), treePath) ||
                !PathOrEmptyEquals(ReadOptionalString(document, "target"), target) ||
                !FixedEquals(ReadString(document, "includes_sha256"), includesHash))
            {
                return RestoreManagerResult.Failure(
                    "The protected restore result did not match this request.",
                    exitCode);
            }
            bool ok = ReadBoolean(document, "ok");
            int backendExitCode = checked((int)ReadLong(document, "backend_exit_code"));
            string error = ReadOptionalString(document, "error");
            IDictionary<string, object> payload = ReadOptionalObject(document, "payload");
            bool? historyRecorded = ReadOptionalBoolean(document, "history_recorded");
            string historyError = ReadOptionalString(document, "history_error");
            if (action == "list_snapshots" && ok && exitCode == 0 && backendExitCode == 0)
            {
                return RestoreManagerResult.SuccessSnapshots(ParseSnapshots(payload, planId));
            }
            if (action == "list_tree" && ok && exitCode == 0 && backendExitCode == 0)
            {
                return RestoreManagerResult.SuccessTree(
                    ParseTree(payload, snapshotId, planId, allowLegacyUnbound));
            }
            if ((action == "restore" || action == "restore_drill") && payload != null)
            {
                string actualSnapshotId = action == "restore_drill"
                    ? NullToEmpty(ReadOptionalString(document, "snapshot_id"))
                    : snapshotId;
                if (!IsExactSnapshotId(actualSnapshotId))
                {
                    return RestoreManagerResult.Failure(
                        "The protected restore result returned an invalid snapshot identity.",
                        exitCode);
                }
                ProtectedRestoreReport report = ParseReport(
                    payload, actualSnapshotId, planId, target, allowLegacyUnbound);
                if (action == "restore_drill" && ok && exitCode == 0 &&
                    backendExitCode == 0 && report.Verified && report.Result == "verified")
                {
                    ValidateRecoveryDrillReport(payload, report, generation);
                }
                if (ok && exitCode == 0 && backendExitCode == 0 && report.Verified &&
                    report.Result == "verified")
                {
                    if (action == "restore_drill" && historyRecorded != true)
                    {
                        return RestoreManagerResult.EvidenceFailure(
                            report,
                            "The sample was restored and verified, but readiness evidence was not recorded.",
                            exitCode,
                            Sanitize(historyError));
                    }
                    return RestoreManagerResult.SuccessRestore(
                        report, historyRecorded, Sanitize(historyError));
                }
                if (action == "restore_drill" && !ok && exitCode != 0 &&
                    backendExitCode == 0 && report.Verified && report.Result == "verified" &&
                    historyRecorded == false)
                {
                    return RestoreManagerResult.EvidenceFailure(
                        report,
                        Sanitize(error),
                        exitCode,
                        Sanitize(historyError));
                }
                if (!ok && exitCode != 0 &&
                    (report.Result == "partial" || report.PartialTargetRetained))
                {
                    return RestoreManagerResult.PartialRestore(
                        report,
                        Sanitize(error ?? report.ErrorMessage),
                        exitCode,
                        historyRecorded,
                        Sanitize(historyError));
                }
            }
            if (!ok && exitCode != 0)
            {
                return RestoreManagerResult.Failure(Sanitize(error), exitCode);
            }
            return RestoreManagerResult.Failure(
                "The protected restore result disagreed with the backend exit status.",
                exitCode);
        }

        private static IList<RestoreSnapshot> ParseSnapshots(
            IDictionary<string, object> payload,
            string planId)
        {
            if (payload == null || ReadString(payload, "schema") !=
                    "ResticBackuper.SnapshotList.v1")
            {
                throw new InvalidDataException("The snapshot-list payload schema is invalid.");
            }
            IDictionary<string, object> binding = ReadObject(payload, "binding");
            if (!FixedEquals(ReadString(binding, "plan_id"), planId) ||
                ReadString(binding, "legacy_match_policy") !=
                    "exact-host-scheduled-tags-and-sources")
            {
                throw new InvalidDataException("The snapshot list belongs to another backup plan.");
            }
            List<RestoreSnapshot> result = new List<RestoreSnapshot>();
            foreach (object item in ReadItems(payload, "snapshots"))
            {
                IDictionary<string, object> snapshot = item as IDictionary<string, object>;
                if (snapshot == null)
                {
                    throw new InvalidDataException("The snapshot list contains a non-object item.");
                }
                string id = ReadString(snapshot, "id");
                string bindingState = ReadString(snapshot, "binding_state");
                long generation;
                if (!IsExactSnapshotId(id))
                {
                    throw new InvalidDataException("The snapshot list contains an invalid immutable ID.");
                }
                if (bindingState == "plan")
                {
                    generation = ReadLong(snapshot, "config_generation");
                    if (!FixedEquals(ReadString(snapshot, "plan_id"), planId) || generation <= 0)
                    {
                        throw new InvalidDataException(
                            "The snapshot list contains invalid plan-binding metadata.");
                    }
                }
                else if (bindingState == "legacy_unbound")
                {
                    generation = 0;
                    if (ReadOptionalString(snapshot, "plan_id") != null ||
                        ReadOptionalLong(snapshot, "config_generation") != null)
                    {
                        throw new InvalidDataException(
                            "A legacy snapshot unexpectedly contains plan-binding metadata.");
                    }
                }
                else
                {
                    throw new InvalidDataException("The snapshot list contains an unknown binding.");
                }
                IDictionary<string, object> summary = ReadOptionalObject(snapshot, "summary");
                long files = summary == null ? 0 : ReadOptionalLong(summary, "total_files_processed") ?? 0;
                long bytes = summary == null ? 0 : ReadOptionalLong(summary, "total_bytes_processed") ?? 0;
                result.Add(new RestoreSnapshot(
                    id,
                    ReadOptionalString(snapshot, "short_id") ?? id.Substring(0, 8),
                    ReadOptionalString(snapshot, "time") ?? string.Empty,
                    ReadOptionalString(snapshot, "hostname") ?? string.Empty,
                    generation,
                    files,
                    bytes,
                    JoinStrings(ReadItems(snapshot, "paths")),
                    bindingState));
            }
            return result;
        }

        private static IList<RestoreTreeEntry> ParseTree(
            IDictionary<string, object> payload,
            string snapshotId,
            string planId,
            bool allowLegacyUnbound)
        {
            if (payload == null || ReadString(payload, "schema") !=
                    "ResticBackuper.SnapshotTree.v1" ||
                !FixedEquals(ReadString(payload, "snapshot_id"), snapshotId))
            {
                throw new InvalidDataException("The snapshot-tree payload binding is invalid.");
            }
            IDictionary<string, object> snapshot = ReadObject(payload, "snapshot");
            string expectedBinding = allowLegacyUnbound ? "legacy_unbound" : "plan";
            if (ReadString(payload, "snapshot_binding") != expectedBinding ||
                ReadString(snapshot, "binding_state") != expectedBinding ||
                (!allowLegacyUnbound && !FixedEquals(ReadString(snapshot, "plan_id"), planId)) ||
                (allowLegacyUnbound &&
                    (ReadOptionalString(snapshot, "plan_id") != null ||
                     ReadOptionalLong(snapshot, "config_generation") != null)))
            {
                throw new InvalidDataException("The snapshot tree belongs to another backup plan.");
            }
            List<RestoreTreeEntry> entries = new List<RestoreTreeEntry>();
            foreach (object item in ReadItems(payload, "entries"))
            {
                IDictionary<string, object> entry = item as IDictionary<string, object>;
                if (entry == null)
                {
                    throw new InvalidDataException("The snapshot tree contains a non-object entry.");
                }
                string type = ReadOptionalString(entry, "type") ?? string.Empty;
                if (type != "file" && type != "dir" && type != "symlink" && type != "socket" &&
                    type != "dev" && type != "fifo")
                {
                    throw new InvalidDataException("The snapshot tree contains an invalid entry type.");
                }
                entries.Add(new RestoreTreeEntry(
                    ReadOptionalString(entry, "name") ?? string.Empty,
                    ReadOptionalString(entry, "path") ?? string.Empty,
                    type,
                    ReadOptionalLong(entry, "size") ?? 0));
            }
            return entries;
        }

        private static ProtectedRestoreReport ParseReport(
            IDictionary<string, object> payload,
            string snapshotId,
            string planId,
            string target,
            bool allowLegacyUnbound)
        {
            string expectedBinding = allowLegacyUnbound ? "legacy_unbound" : "plan";
            if (ReadString(payload, "schema") != "ResticBackuper.RestoreReport.v1" ||
                !FixedEquals(ReadString(payload, "snapshot_id"), snapshotId) ||
                ReadString(payload, "snapshot_binding") != expectedBinding ||
                (!allowLegacyUnbound && !FixedEquals(ReadString(payload, "plan_id"), planId)) ||
                (allowLegacyUnbound &&
                    (ReadOptionalString(payload, "plan_id") != null ||
                     ReadOptionalLong(payload, "snapshot_generation") != null)) ||
                !PathOrEmptyEquals(ReadString(payload, "target"), target))
            {
                throw new InvalidDataException("The protected restore report binding is invalid.");
            }
            string result = ReadString(payload, "result");
            if (result != "verified" && result != "partial" && result != "error")
            {
                throw new InvalidDataException("The protected restore report result is invalid.");
            }
            return new ProtectedRestoreReport(
                result,
                snapshotId,
                target,
                ReadBoolean(payload, "verified"),
                ReadBoolean(payload, "partial_target_retained"),
                checked((int)(ReadOptionalLong(payload, "restic_exit_code") ?? -1)),
                ReadOptionalString(payload, "error"),
                expectedBinding);
        }

        private static void ValidateRecoveryDrillReport(
            IDictionary<string, object> payload,
            ProtectedRestoreReport report,
            long generation)
        {
            long? sampleCount = ReadOptionalLong(payload, "sample_file_count");
            long? sampleBytes = ReadOptionalLong(payload, "sample_bytes");
            long? canaryBytes = ReadOptionalLong(payload, "canary_bytes");
            string canaryHash = ReadOptionalString(payload, "canary_sha256");
            string sampleHash = ReadOptionalString(payload, "sample_paths_sha256");
            if (ReadString(payload, "drill_kind") != "recovery_key_representative" ||
                ReadString(payload, "credential_source") != "recovery_key" ||
                ReadLong(payload, "snapshot_generation") != generation ||
                !ReadBoolean(payload, "canary_verified") ||
                canaryBytes == null || canaryBytes.Value < 0 ||
                !IsLowerHex(canaryHash, 64) ||
                ReadString(payload, "sample_policy") !=
                    "one-or-two-bounded-files-per-source-v1" ||
                sampleCount == null || sampleCount.Value < 2 || sampleCount.Value > 8 ||
                sampleBytes == null || sampleBytes.Value <= 0 ||
                sampleBytes.Value > 32L * 1024L * 1024L ||
                !IsLowerHex(sampleHash, 64) ||
                report.PartialTargetRetained)
            {
                throw new InvalidDataException(
                    "The protected recovery-drill report is incomplete or invalid.");
            }
        }

        private static void PublishProgress(
            string progressPath,
            string nonce,
            string digest,
            string userSid,
            string action,
            string planId,
            long generation,
            bool allowLegacyUnbound,
            string snapshotId,
            string target,
            Action<RestoreManagerProgress> callback,
            ref string lastFingerprint)
        {
            if (callback == null || !File.Exists(progressPath))
            {
                return;
            }
            try
            {
                FileInfo item = new FileInfo(progressPath);
                string fingerprint = item.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                    item.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                if (fingerprint == lastFingerprint || item.Length <= 0 ||
                    item.Length > 256 * 1024 ||
                    (item.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !HasProtectedAcl(progressPath, userSid))
                {
                    return;
                }
                IDictionary<string, object> document =
                    new JavaScriptSerializer().DeserializeObject(
                        File.ReadAllText(progressPath, Encoding.UTF8)) as IDictionary<string, object>;
                if (document == null || ReadLong(document, "schema_version") != 1 ||
                    !FixedEquals(ReadString(document, "request_nonce"), nonce) ||
                    !FixedEquals(ReadString(document, "request_digest"), digest) ||
                    !FixedEquals(ReadString(document, "request_user_sid"), userSid) ||
                    !FixedEquals(ReadString(document, "action"), action) ||
                    !FixedEquals(ReadString(document, "plan_id"), planId) ||
                    ReadLong(document, "config_generation") != generation ||
                    ReadBoolean(document, "allow_legacy_unbound") != allowLegacyUnbound ||
                    !FixedEquals(NullToEmpty(ReadOptionalString(document, "snapshot_id")), snapshotId) ||
                    !PathOrEmptyEquals(ReadOptionalString(document, "target"), target) ||
                    ReadBoolean(document, "cancellable"))
                {
                    return;
                }
                lastFingerprint = fingerprint;
                callback(new RestoreManagerProgress(
                    ReadString(document, "stage"),
                    ReadString(document, "message"),
                    checked((int)ReadLong(document, "percent"))));
            }
            catch
            {
                // Progress is advisory. The protected final result is authoritative.
            }
        }

        private static void ValidateRequestFields(
            string action,
            string snapshotId,
            string treePath,
            string target,
            IList<string> includes,
            bool allowLegacyUnbound)
        {
            if (action != "list_snapshots" && action != "list_tree" &&
                action != "restore" && action != "restore_drill")
            {
                throw new ArgumentException("The protected restore action is invalid.");
            }
            if ((action == "list_tree" || action == "restore") && !IsExactSnapshotId(snapshotId))
            {
                throw new ArgumentException("Choose one exact snapshot before continuing.");
            }
            if ((action == "list_snapshots" || action == "restore_drill") && snapshotId.Length != 0)
            {
                throw new ArgumentException("Snapshot listing must not include a snapshot selector.");
            }
            if ((action == "list_snapshots" || action == "restore_drill") && allowLegacyUnbound)
            {
                throw new ArgumentException(
                    "Legacy snapshot access is not valid for a snapshot-list request.");
            }
            if (action == "list_tree" && treePath.Length > 0 &&
                (!treePath.StartsWith("/", StringComparison.Ordinal) || treePath.IndexOf('\\') >= 0 ||
                 treePath.Length > 1024))
            {
                throw new ArgumentException("The snapshot folder path is invalid.");
            }
            if (action == "restore" && string.IsNullOrWhiteSpace(target))
            {
                throw new ArgumentException("Choose a new or empty restore destination.");
            }
            if (action == "restore_drill" &&
                (treePath.Length != 0 || target.Length != 0 || includes.Count != 0))
            {
                throw new ArgumentException(
                    "The protected manager derives every recovery-drill selector and target.");
            }
            if (includes == null || includes.Count > 64)
            {
                throw new ArgumentException("The restore selection exceeds 64 paths.");
            }
            foreach (string include in includes)
            {
                if (string.IsNullOrWhiteSpace(include) || include.Length > 1024 ||
                    include.IndexOf('\0') >= 0)
                {
                    throw new ArgumentException("A restore include path is invalid.");
                }
            }
            if (action != "restore" && includes.Count != 0)
            {
                throw new ArgumentException("File selections are only valid for restore requests.");
            }
        }

        private static string NormalizeSnapshotId(string value)
        {
            string result = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (!IsExactSnapshotId(result))
            {
                throw new ArgumentException("Choose one exact snapshot before continuing.");
            }
            return result;
        }

        private static string NormalizeTreePath(string value)
        {
            string result = (value ?? string.Empty).Trim();
            return result == "/" ? "/" : result.TrimEnd('/');
        }

        private static bool IsExactSnapshotId(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (char item in value)
            {
                if (!((item >= '0' && item <= '9') || (item >= 'a' && item <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char item in value)
            {
                if (!((item >= '0' && item <= '9') || (item >= 'a' && item <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static string ComputeFileSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                FileOptions.SequentialScan))
            using (SHA256 algorithm = SHA256.Create())
            {
                if (stream.Length <= 0 || stream.Length > 1024 * 1024)
                {
                    throw new InvalidDataException("The protected configuration has an invalid size.");
                }
                return ToLowerHex(algorithm.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return ToLowerHex(algorithm.ComputeHash(bytes));
            }
        }

        private static string CreateNonce()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }
            return ToLowerHex(bytes);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte item in bytes)
            {
                builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static string BuildArgumentString(IEnumerable<string> arguments)
        {
            List<string> values = new List<string>();
            foreach (string argument in arguments)
            {
                values.Add(QuoteWindowsArgument(argument));
            }
            return string.Join(" ", values.ToArray());
        }

        private static string QuoteWindowsArgument(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return value;
            }
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    slashes++;
                    continue;
                }
                if (character == '"')
                {
                    result.Append('\\', slashes * 2 + 1);
                    result.Append('"');
                    slashes = 0;
                    continue;
                }
                if (slashes > 0)
                {
                    result.Append('\\', slashes);
                    slashes = 0;
                }
                result.Append(character);
            }
            if (slashes > 0)
            {
                result.Append('\\', slashes * 2);
            }
            result.Append('"');
            return result.ToString();
        }

        private static bool IsRegularFile(string path)
        {
            try
            {
                return File.Exists(path) &&
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasProtectedAcl(string path, string userSid)
        {
            try
            {
                FileSystemSecurity security = Directory.Exists(path)
                    ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl(
                        AccessControlSections.Owner | AccessControlSections.Access)
                    : (FileSystemSecurity)new FileInfo(path).GetAccessControl(
                        AccessControlSections.Owner | AccessControlSections.Access);
                return HasProtectedAcl(security, userSid);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasProtectedAcl(FileSystemSecurity security, string userSid)
        {
            try
            {
                SecurityIdentifier owner = security.GetOwner(
                    typeof(SecurityIdentifier)) as SecurityIdentifier;
                if (owner == null || owner.Value != "S-1-5-32-544" ||
                    !security.AreAccessRulesProtected)
                {
                    return false;
                }
                HashSet<string> required = new HashSet<string>(
                    new[] { "S-1-5-18", "S-1-5-32-544", userSid },
                    StringComparer.OrdinalIgnoreCase);
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> allowed = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase);
                allowed.Add("S-1-3-4");
                FileSystemRights dangerous = FileSystemRights.WriteData |
                    FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
                    FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete |
                    FileSystemRights.DeleteSubdirectoriesAndFiles |
                    FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
                foreach (FileSystemAccessRule rule in security.GetAccessRules(
                    true,
                    true,
                    typeof(SecurityIdentifier)))
                {
                    SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                    string value = sid == null ? null : sid.Value;
                    if (value == null || rule.IsInherited ||
                        rule.AccessControlType != AccessControlType.Allow ||
                        !allowed.Contains(value))
                    {
                        return false;
                    }
                    if (value != "S-1-5-18" && value != "S-1-5-32-544" &&
                        (rule.FileSystemRights & dangerous) != 0)
                    {
                        return false;
                    }
                    seen.Add(value);
                }
                return required.IsSubsetOf(seen);
            }
            catch
            {
                return false;
            }
        }

        private static void InvokeSafely(Action callback)
        {
            if (callback == null)
            {
                return;
            }
            try { callback(); }
            catch { }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "The protected restore operation failed.";
            }
            StringBuilder result = new StringBuilder();
            foreach (char item in value)
            {
                if (!char.IsControl(item) || item == '\t')
                {
                    result.Append(item);
                }
                if (result.Length >= 2000)
                {
                    break;
                }
            }
            string safe = result.ToString().Trim();
            return safe.Length == 0 ? "The protected restore operation failed." : safe;
        }

        private static bool FixedEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }
            return difference == 0;
        }

        private static bool PathOrEmptyEquals(string left, string right)
        {
            string expected = right ?? string.Empty;
            string actual = left ?? string.Empty;
            if (expected.Length == 0 || actual.Length == 0)
            {
                return expected.Length == actual.Length;
            }
            return string.Equals(
                SourceConfiguration.NormalizePath(actual),
                SourceConfiguration.NormalizePath(expected),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NullToEmpty(string value)
        {
            return value ?? string.Empty;
        }

        private static string ReadString(IDictionary<string, object> document, string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) || !(value is string))
            {
                throw new InvalidDataException("Result field is missing or invalid: " + name);
            }
            return (string)value;
        }

        private static string ReadOptionalString(IDictionary<string, object> document, string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) || value == null)
            {
                return null;
            }
            if (!(value is string))
            {
                throw new InvalidDataException("Result field is invalid: " + name);
            }
            return (string)value;
        }

        private static long ReadLong(IDictionary<string, object> document, string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) ||
                (!(value is int) && !(value is long)))
            {
                throw new InvalidDataException("Result field is missing or invalid: " + name);
            }
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static long? ReadOptionalLong(IDictionary<string, object> document, string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) || value == null)
            {
                return null;
            }
            if (!(value is int) && !(value is long))
            {
                throw new InvalidDataException("Result field is invalid: " + name);
            }
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static bool ReadBoolean(IDictionary<string, object> document, string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) || !(value is bool))
            {
                throw new InvalidDataException("Result field is missing or invalid: " + name);
            }
            return (bool)value;
        }

        private static bool? ReadOptionalBoolean(
            IDictionary<string, object> document,
            string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) || value == null)
            {
                return null;
            }
            if (!(value is bool))
            {
                throw new InvalidDataException("Result field is invalid: " + name);
            }
            return (bool)value;
        }

        private static IDictionary<string, object> ReadObject(
            IDictionary<string, object> document,
            string name)
        {
            IDictionary<string, object> result = ReadOptionalObject(document, name);
            if (result == null)
            {
                throw new InvalidDataException("Result object is missing or invalid: " + name);
            }
            return result;
        }

        private static IDictionary<string, object> ReadOptionalObject(
            IDictionary<string, object> document,
            string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) || value == null)
            {
                return null;
            }
            IDictionary<string, object> result = value as IDictionary<string, object>;
            if (result == null)
            {
                throw new InvalidDataException("Result object is invalid: " + name);
            }
            return result;
        }

        private static IEnumerable ReadItems(IDictionary<string, object> document, string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) || value is string || !(value is IEnumerable))
            {
                throw new InvalidDataException("Result array is missing or invalid: " + name);
            }
            return (IEnumerable)value;
        }

        private static string JoinStrings(IEnumerable values)
        {
            List<string> result = new List<string>();
            foreach (object item in values)
            {
                string text = item as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text);
                }
            }
            return string.Join(", ", result.ToArray());
        }
    }
}
