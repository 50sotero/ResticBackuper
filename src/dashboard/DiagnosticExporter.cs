using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ResticBackuper.Dashboard
{
    internal sealed class DiagnosticExportResult
    {
        public bool Succeeded { get; private set; }
        public string ExportPath { get; private set; }
        public string ErrorMessage { get; private set; }

        internal static DiagnosticExportResult Success(string path)
        {
            return new DiagnosticExportResult { Succeeded = true, ExportPath = path };
        }

        internal static DiagnosticExportResult Failure(string error)
        {
            return new DiagnosticExportResult
            {
                ErrorMessage = string.IsNullOrWhiteSpace(error)
                    ? "The diagnostic bundle could not be exported safely."
                    : error
            };
        }
    }

    internal static class DiagnosticExporter
    {
        private const int MaximumJsonBytes = 4 * 1024 * 1024;
        private const int MaximumLogTailBytes = 2 * 1024 * 1024;
        private const int MaximumLogEvents = 300;
        private const int MaximumArchiveEntries = 24;

        internal static DiagnosticExportResult Export(
            SourceConfiguration configuration,
            TaskSchedule schedule,
            TelemetrySnapshot snapshot,
            string destination)
        {
            if (configuration == null)
                return DiagnosticExportResult.Failure(
                    "The protected configuration is unavailable.");
            string temporary = string.Empty;
            try
            {
                string target = ValidateDestination(configuration, destination);
                if (File.Exists(target) || Directory.Exists(target))
                    return DiagnosticExportResult.Failure(
                        "Choose a new ZIP filename. Existing files are never overwritten by diagnostic export.");

                IDictionary<string, object> rawConfiguration = ReadRequiredObject(
                    configuration.ConfigurationPath);
                DiagnosticRedactor redactor = new DiagnosticRedactor(
                    configuration,
                    rawConfiguration);
                Dictionary<string, object> documents = new Dictionary<string, object>(
                    StringComparer.Ordinal);
                documents["configuration.json"] = redactor.Sanitize(
                    rawConfiguration,
                    "configuration");
                AddProtectedDocument(
                    documents,
                    redactor,
                    "status.json",
                    Path.Combine(configuration.StateDirectory, "status.json"));
                AddProtectedDocument(
                    documents,
                    redactor,
                    "last-success.json",
                    Path.Combine(configuration.StateDirectory, "last-success.json"));
                AddProtectedDocument(
                    documents,
                    redactor,
                    "dry-run-latest.json",
                    Path.Combine(configuration.StateDirectory, "dry-run-latest.json"));
                AddProtectedDocument(
                    documents,
                    redactor,
                    "recovery-health-latest.json",
                    Path.Combine(configuration.StateDirectory, "recovery-health-latest.json"));
                string protectedOffsiteStatus = Path.Combine(
                    configuration.StateDirectory,
                    "google-drive-sync-status.json");
                string commonApplicationData = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData);
                string directCloudVerification = string.IsNullOrWhiteSpace(
                    commonApplicationData)
                    ? string.Empty
                    : Path.Combine(
                        commonApplicationData,
                        "ResticBackuperCloudVerification",
                        "evidence",
                        "latest-verification.json");
                AddProtectedDocument(
                    documents,
                    redactor,
                    "my-drive-latest-verification.json",
                    directCloudVerification);
                AddProtectedDocument(
                    documents,
                    redactor,
                    "legacy-google-drive-sync-status.json",
                    protectedOffsiteStatus);
                AddProtectedDocument(
                    documents,
                    redactor,
                    "runtime-manifest.json",
                    Path.Combine(configuration.InstallRoot, "runtime-manifest.json"));

                documents["schedule.json"] = redactor.Sanitize(
                    BuildSchedule(schedule),
                    "schedule");
                documents["dashboard-state.json"] = redactor.Sanitize(
                    BuildDashboardState(snapshot),
                    "dashboard_state");
                object logEvents = BuildLatestLogEvents(configuration, snapshot);
                if (logEvents != null)
                {
                    redactor.Observe(logEvents);
                    documents["latest-run-log-events.json"] = redactor.Sanitize(
                        logEvents,
                        "log_events");
                }

                Dictionary<string, object> manifest = new Dictionary<string, object>();
                manifest["schema"] = "ResticBackuper.Diagnostics.v1";
                manifest["schema_version"] = 1;
                manifest["created_utc"] = DateTime.UtcNow.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture);
                manifest["dashboard_version"] = Assembly.GetExecutingAssembly()
                    .GetName().Version.ToString();
                manifest["operating_system"] = Environment.OSVersion.VersionString;
                manifest["process_architecture"] = Environment.Is64BitProcess
                    ? "x64"
                    : "x86";
                manifest["timezone"] = TimeZoneInfo.Local.Id;
                manifest["identity_redacted"] = true;
                manifest["paths_redacted"] = true;
                manifest["secrets_included"] = false;
                manifest["repository_modified"] = false;
                manifest["snapshots_modified"] = false;
                manifest["entries"] = documents.Keys.OrderBy(item => item).ToArray();
                documents["manifest.json"] = redactor.Sanitize(manifest, "manifest");
                documents["redaction-report.json"] = redactor.Report();

                if (documents.Count > MaximumArchiveEntries)
                    throw new InvalidDataException("The diagnostic bundle entry limit was exceeded.");

                JavaScriptSerializer serializer = Serializer();
                Dictionary<string, string> payloads = new Dictionary<string, string>(
                    StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> document in documents)
                {
                    string json = serializer.Serialize(document.Value);
                    try
                    {
                        redactor.AssertSafe(json);
                    }
                    catch (InvalidDataException error)
                    {
                        throw new InvalidDataException(
                            document.Key + ": " + error.Message,
                            error);
                    }
                    payloads[document.Key] = PrettyJson(json);
                }

                string directory = Path.GetDirectoryName(target);
                temporary = Path.Combine(
                    directory,
                    "." + Path.GetFileName(target) + ".partial-" +
                    Guid.NewGuid().ToString("N"));
                using (FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None))
                using (ZipArchive archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Create,
                    false,
                    Encoding.UTF8))
                {
                    foreach (KeyValuePair<string, string> payload in payloads.OrderBy(
                        item => item.Key,
                        StringComparer.Ordinal))
                    {
                        WriteEntry(archive, payload.Key, payload.Value);
                    }
                    WriteEntry(
                        archive,
                        "README.txt",
                        "Redacted Restic Backup support bundle\r\n\r\n" +
                        "Personal paths, Windows identities, host names, command lines, and credential-like values are removed. " +
                        "This export is read-only and does not modify the repository or snapshots.\r\n");
                }
                File.Move(temporary, target);
                temporary = string.Empty;
                return DiagnosticExportResult.Success(target);
            }
            catch (Exception error)
            {
                return DiagnosticExportResult.Failure(SanitizeError(error.Message));
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporary))
                {
                    try
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static string ValidateDestination(
            SourceConfiguration configuration,
            string destination)
        {
            if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathRooted(destination))
                throw new InvalidDataException("Choose an absolute diagnostic ZIP path.");
            string target = SourceConfiguration.NormalizePath(destination);
            if (!string.Equals(
                Path.GetExtension(target),
                ".zip",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The diagnostic export must use a .zip filename.");
            string parent = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                throw new DirectoryNotFoundException(
                    "The diagnostic export folder does not exist.");
            DirectoryInfo current = new DirectoryInfo(parent);
            while (current != null)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Diagnostic export refuses reparse-point destination folders.");
                current = current.Parent;
            }

            List<string> protectedPaths = new List<string>
            {
                configuration.InstallRoot,
                configuration.StateDirectory,
                configuration.RepositoryPath
            };
            protectedPaths.AddRange(configuration.Sources.Select(item => item.SourcePath));
            foreach (string protectedPath in protectedPaths)
            {
                if (PathsOverlap(target, protectedPath))
                    throw new InvalidDataException(
                        "Diagnostic export must be outside sources, repository, runtime, and protected state.");
            }
            return target;
        }

        private static bool PathsOverlap(string first, string second)
        {
            string left = SourceConfiguration.NormalizePath(first);
            string right = SourceConfiguration.NormalizePath(second);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return true;
            string leftPrefix = left.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string rightPrefix = right.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return leftPrefix.StartsWith(rightPrefix, StringComparison.OrdinalIgnoreCase) ||
                rightPrefix.StartsWith(leftPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddProtectedDocument(
            IDictionary<string, object> documents,
            DiagnosticRedactor redactor,
            string name,
            string path)
        {
            IDictionary<string, object> document = TryReadObject(path);
            if (document != null)
            {
                redactor.Observe(document);
                documents[name] = redactor.Sanitize(document, name);
            }
        }

        private static IDictionary<string, object> ReadRequiredObject(string path)
        {
            IDictionary<string, object> document = TryReadObject(path);
            if (document == null)
                throw new InvalidDataException(
                    "The protected configuration could not be read for diagnostic export.");
            return document;
        }

        private static IDictionary<string, object> TryReadObject(string path)
        {
            try
            {
                if (!File.Exists(path) || Directory.Exists(path)) return null;
                FileInfo file = new FileInfo(path);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    file.Length <= 0 || file.Length > MaximumJsonBytes) return null;
                return Serializer().DeserializeObject(
                    File.ReadAllText(path, Encoding.UTF8)) as IDictionary<string, object>;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static IDictionary<string, object> BuildSchedule(TaskSchedule schedule)
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            value["available"] = schedule != null;
            if (schedule == null) return value;
            value["cadence"] = schedule.Cadence.ToString();
            value["time_of_day"] = schedule.TimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            value["days"] = schedule.Days.Select(item => item.ToString()).ToArray();
            value["enabled"] = schedule.Enabled;
            value["start_when_available"] = schedule.StartWhenAvailable;
            value["wake_to_run"] = schedule.WakeToRun;
            value["allow_start_on_batteries"] = schedule.AllowStartOnBatteries;
            value["stop_if_going_on_batteries"] = schedule.StopIfGoingOnBatteries;
            value["state"] = schedule.State.ToString();
            value["next_run_local"] = schedule.NextRunTime.HasValue
                ? schedule.NextRunTime.Value.ToString("o", CultureInfo.InvariantCulture)
                : null;
            return value;
        }

        private static IDictionary<string, object> BuildDashboardState(
            TelemetrySnapshot snapshot)
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            value["available"] = snapshot != null;
            if (snapshot == null) return value;
            value["state"] = snapshot.StateKey;
            value["status"] = snapshot.StatusLabel;
            value["detail"] = snapshot.StatusDetail;
            value["active"] = snapshot.IsActive;
            value["success"] = snapshot.IsSuccess;
            value["failure"] = snapshot.IsFailure;
            value["cancelled"] = snapshot.IsCancelled;
            value["maintenance_hold"] = snapshot.HasMaintenanceHold;
            value["anomaly_review_acknowledged"] =
                snapshot.AnomalyReviewAcknowledged;
            OffsiteStatusView offsite = snapshot.OffsiteStatus;
            if (offsite != null)
            {
                value["offsite_copy"] = new Dictionary<string, object>
                {
                    { "kind", offsite.Kind.ToString() },
                    { "status", offsite.StatusLabel },
                    { "detail", offsite.StatusDetail },
                    { "plan_id", offsite.PlanId },
                    { "config_generation", offsite.ConfigGeneration },
                    { "repository_id", offsite.RepositoryId },
                    { "repository_path", offsite.RepositoryPath },
                    { "snapshot_id", offsite.SnapshotId },
                    {
                        "inventory_fingerprint_sha256",
                        offsite.InventoryFingerprintSha256
                    },
                    { "files", offsite.FileCount },
                    { "bytes", offsite.ByteCount },
                    { "provider_upload_confirmed", offsite.ProviderUploadConfirmed },
                    { "restore_verified", offsite.RestoreVerified },
                    {
                        "updated_local",
                        offsite.LastUpdatedLocal.HasValue
                            ? offsite.LastUpdatedLocal.Value.ToString(
                                "o",
                                CultureInfo.InvariantCulture)
                            : null
                    }
                };
            }
            value["run_id"] = snapshot.RunId;
            value["snapshot_id"] = snapshot.SnapshotId;
            value["files"] = snapshot.FilesDone;
            value["processed_bytes"] = snapshot.BytesDone;
            value["stored_bytes"] = snapshot.StoredBytes;
            value["errors"] = snapshot.ErrorCount;
            value["phase"] = snapshot.PhaseLabel;
            value["last_verified_utc"] = snapshot.LastVerifiedFinishedUtc.HasValue
                ? snapshot.LastVerifiedFinishedUtc.Value.ToString("o", CultureInfo.InvariantCulture)
                : null;
            return value;
        }

        private static object BuildLatestLogEvents(
            SourceConfiguration configuration,
            TelemetrySnapshot snapshot)
        {
            if (snapshot == null || !ValidRunId(snapshot.RunId)) return null;
            string path = Path.Combine(
                configuration.StateDirectory,
                "logs",
                "backup-" + snapshot.RunId + ".jsonl.log");
            if (!File.Exists(path) || Directory.Exists(path)) return null;
            FileInfo file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length <= 0)
                return null;
            List<object> events = new List<object>();
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                bool truncated = stream.Length > MaximumLogTailBytes;
                if (truncated) stream.Seek(-MaximumLogTailBytes, SeekOrigin.End);
                using (StreamReader reader = new StreamReader(
                    stream,
                    new UTF8Encoding(false, true),
                    true,
                    4096,
                    false))
                {
                    if (truncated) reader.ReadLine();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length <= 0 || line.Length > 1024 * 1024) continue;
                        IDictionary<string, object> document;
                        try
                        {
                            document = Serializer().DeserializeObject(line)
                                as IDictionary<string, object>;
                        }
                        catch (ArgumentException) { continue; }
                        catch (InvalidOperationException) { continue; }
                        if (document == null) continue;
                        document.Remove("command");
                        document.Remove("traceback");
                        events.Add(document);
                        if (events.Count > MaximumLogEvents)
                            events.RemoveAt(0);
                    }
                }
            }
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["schema_version"] = 1;
            result["tail_truncated"] = file.Length > MaximumLogTailBytes;
            result["events"] = events;
            return result;
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                4096,
                false))
            {
                writer.Write(content ?? string.Empty);
            }
        }

        private static JavaScriptSerializer Serializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaximumJsonBytes;
            serializer.RecursionLimit = 100;
            return serializer;
        }

        private static string PrettyJson(string compact)
        {
            // JavaScriptSerializer has no indented mode. A valid compact payload is
            // deterministic, smaller, and avoids a second untrusted parse.
            return compact + Environment.NewLine;
        }

        private static bool ValidRunId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100) return false;
            foreach (char item in value)
            {
                if (!(char.IsLetterOrDigit(item) || item == '-' || item == '_' ||
                    item == '.' || item == '+')) return false;
            }
            return true;
        }

        private static string SanitizeError(string message)
        {
            string value = string.IsNullOrWhiteSpace(message)
                ? "Diagnostic export failed."
                : message.Trim();
            if (value.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("dpapi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("RESTIC_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Diagnostic export failed without exposing credential details.";
            return value.Length > 1000 ? value.Substring(0, 1000) : value;
        }
    }

    internal sealed class DiagnosticRedactor
    {
        private static readonly Regex DrivePath = new Regex(
            "(?i)[a-z]:\\\\[^\\r\\n\\\";]*",
            RegexOptions.CultureInvariant);
        private static readonly Regex UncPath = new Regex(
            "\\\\\\\\[^\\\\\\s]+\\\\[^\\r\\n\\\";]*",
            RegexOptions.CultureInvariant);
        private static readonly Regex Email = new Regex(
            @"(?i)\b[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}\b",
            RegexOptions.CultureInvariant);
        private static readonly Regex LongToken = new Regex(
            @"\b[A-Za-z0-9+/=]{97,}\b",
            RegexOptions.CultureInvariant);

        private readonly List<KeyValuePair<string, string>> replacements =
            new List<KeyValuePair<string, string>>();
        private readonly HashSet<string> secretValues = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> identityTokens =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int pathRedactions;
        private int secretRedactions;
        private int identityRedactions;
        private int tokenRedactions;

        internal DiagnosticRedactor(
            SourceConfiguration configuration,
            IDictionary<string, object> rawConfiguration)
        {
            AddReplacement(Environment.UserName, "<windows-user>");
            AddReplacement(Environment.MachineName, "<computer>");
            AddReplacement(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), "<user-profile>");
            AddPath(configuration.InstallRoot, "<runtime-path>");
            AddPath(configuration.StateDirectory, "<state-path>");
            AddPath(configuration.RepositoryPath, "<repository-path>");
            AddPath(configuration.ConfigurationPath, "<configuration-path>");
            int source = 0;
            foreach (BackupSourceView item in configuration.Sources)
            {
                source++;
                AddPath(item.SourcePath, "<source-path:" +
                    source.ToString(CultureInfo.InvariantCulture) + ">");
            }
            DiscoverSensitiveValues(rawConfiguration, string.Empty);
            SortReplacements();
        }

        internal void Observe(object value)
        {
            DiscoverSensitiveValues(value, string.Empty);
            SortReplacements();
        }

        internal object Sanitize(object value, string key)
        {
            if (value == null) return null;
            IDictionary<string, object> dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                Dictionary<string, object> clean = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> item in dictionary)
                    clean[item.Key] = SanitizeValue(item.Value, item.Key);
                return clean;
            }
            IEnumerable sequence = value as IEnumerable;
            if (sequence != null && !(value is string))
            {
                List<object> clean = new List<object>();
                foreach (object item in sequence) clean.Add(Sanitize(item, key));
                return clean;
            }
            return SanitizeValue(value, key);
        }

        internal IDictionary<string, object> Report()
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            value["schema_version"] = 1;
            value["paths_redacted"] = pathRedactions;
            value["secrets_redacted"] = secretRedactions;
            value["identities_redacted"] = identityRedactions;
            value["long_tokens_redacted"] = tokenRedactions;
            value["original_values_included"] = false;
            value["command_lines_included"] = false;
            return value;
        }

        internal void AssertSafe(string payload)
        {
            foreach (KeyValuePair<string, string> replacement in replacements)
            {
                if (payload.IndexOf(replacement.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException(
                        "Diagnostic redaction self-check found an original identity or path for " +
                        replacement.Value + ".");
            }
            foreach (string secret in secretValues)
            {
                if (payload.IndexOf(secret, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException(
                        "Diagnostic redaction self-check found a protected value.");
            }
            if (DrivePath.IsMatch(payload) || UncPath.IsMatch(payload) ||
                Email.IsMatch(payload) || LongToken.IsMatch(payload))
                throw new InvalidDataException(
                    "Diagnostic redaction self-check found an unredacted path, identity, or token.");
        }

        private object SanitizeValue(object value, string key)
        {
            if (value == null) return null;
            if (SensitiveKey(key))
            {
                secretRedactions++;
                return "<redacted>";
            }
            if (value is IDictionary<string, object> ||
                (value is IEnumerable && !(value is string)))
            {
                return Sanitize(value, key);
            }
            string text = value as string;
            if (text == null) return value;
            if (IdentityKey(key))
            {
                identityRedactions++;
                return IdentityToken(key, text);
            }
            return SanitizeString(text);
        }

        private string SanitizeString(string value)
        {
            string result = value ?? string.Empty;
            foreach (string secret in secretValues.OrderByDescending(item => item.Length))
            {
                if (secret.Length < 4) continue;
                result = ReplaceIgnoreCase(result, secret, "<redacted>", ref secretRedactions);
            }
            foreach (KeyValuePair<string, string> replacement in replacements)
            {
                int before = result.Length;
                string next = Regex.Replace(
                    result,
                    Regex.Escape(replacement.Key),
                    replacement.Value.Replace("$", "$$"),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!string.Equals(result, next, StringComparison.Ordinal))
                {
                    if (replacement.Value.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                        pathRedactions++;
                    else
                        identityRedactions++;
                }
                result = next;
            }
            result = CountedReplace(DrivePath, result, "<windows-path>", ref pathRedactions);
            result = CountedReplace(UncPath, result, "<network-path>", ref pathRedactions);
            result = CountedReplace(Email, result, "<email>", ref identityRedactions);
            result = CountedReplace(LongToken, result, "<long-token>", ref tokenRedactions);
            return result;
        }

        private void DiscoverSensitiveValues(object value, string key)
        {
            IDictionary<string, object> dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                foreach (KeyValuePair<string, object> item in dictionary)
                    DiscoverSensitiveValues(item.Value, item.Key);
                return;
            }
            IEnumerable sequence = value as IEnumerable;
            if (sequence != null && !(value is string))
            {
                foreach (object item in sequence) DiscoverSensitiveValues(item, key);
                return;
            }
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (SensitiveKey(key)) secretValues.Add(text);
            if (IdentityKey(key)) AddReplacement(text, "<identity>");
            if (LooksLikeRootedPath(text)) AddPath(
                text,
                "<configured-path:" + (replacements.Count + 1).ToString(
                    CultureInfo.InvariantCulture) + ">");
        }

        private static bool LooksLikeRootedPath(string value)
        {
            try
            {
                return Path.IsPathRooted(value);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        private void AddPath(string value, string token)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            AddReplacement(value.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar), token);
        }

        private void AddReplacement(string value, string token)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 3) return;
            if (replacements.Any(item => string.Equals(
                item.Key,
                value,
                StringComparison.OrdinalIgnoreCase))) return;
            replacements.Add(new KeyValuePair<string, string>(value, token));
        }

        private void SortReplacements()
        {
            replacements.RemoveAll(item => string.IsNullOrWhiteSpace(item.Key));
            replacements.Sort(delegate(
                KeyValuePair<string, string> left,
                KeyValuePair<string, string> right)
            {
                return right.Key.Length.CompareTo(left.Key.Length);
            });
        }

        private string IdentityToken(string key, string value)
        {
            string combined = key + "\u001f" + value;
            string token;
            if (!identityTokens.TryGetValue(combined, out token))
            {
                token = "<" + key.Replace('_', '-') + ":" +
                    (identityTokens.Count + 1).ToString(CultureInfo.InvariantCulture) + ">";
                identityTokens[combined] = token;
            }
            return token;
        }

        private static bool SensitiveKey(string key)
        {
            string value = (key ?? string.Empty).ToLowerInvariant();
            if (value == "secrets_included" || value == "command_lines_included")
                return false;
            return value == "command" || value == "arguments" ||
                value.Contains("password") || value.Contains("secret") ||
                value.Contains("credential") || value.Contains("dpapi") ||
                value.Contains("recovery_key") || value.Contains("reviewer_sid") ||
                value == "user_sid" || value == "owner_sid";
        }

        private static bool IdentityKey(string key)
        {
            string value = (key ?? string.Empty).ToLowerInvariant();
            return value == "hostname" || value == "host" ||
                value == "username" || value == "user" ||
                value == "account" || value == "owner" ||
                value == "plan_id" || value == "repository_id" ||
                value == "local_repository_id" ||
                value == "cloud_repository_id" ||
                value == "run_id" || value == "snapshot_id" ||
                value == "inventory_fingerprint_sha256" ||
                value == "cloud_inventory_document_sha256" ||
                value == "backup_config_sha256" ||
                value == "canary_sha256" ||
                value == "immutable_proof_sha256" ||
                value == "cloud_verification_assets_manifest_sha256" ||
                value == "cloud_repository_path" ||
                value == "canary_snapshot_path" ||
                value == "remote_name" ||
                value.EndsWith("_fingerprint", StringComparison.Ordinal) ||
                value == "cancel_channel_id";
        }

        private static string ReplaceIgnoreCase(
            string input,
            string value,
            string replacement,
            ref int counter)
        {
            Regex pattern = new Regex(
                Regex.Escape(value),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            int matches = pattern.Matches(input).Count;
            counter += matches;
            return matches == 0 ? input : pattern.Replace(input, replacement);
        }

        private static string CountedReplace(
            Regex expression,
            string input,
            string replacement,
            ref int counter)
        {
            int matches = expression.Matches(input).Count;
            counter += matches;
            return matches == 0 ? input : expression.Replace(input, replacement);
        }
    }
}
