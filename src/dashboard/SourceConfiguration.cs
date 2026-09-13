using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace ResticBackuper.Dashboard
{
    internal sealed class BackupSourceView
    {
        public string SourcePath { get; private set; }
        public bool IsProtectedCanary { get; private set; }
        public string DisplayName { get; private set; }
        public string ShortPath { get; private set; }

        public string RoleLabel
        {
            get { return IsProtectedCanary ? "Required" : "Protected"; }
        }

        public BackupSourceView(string sourcePath, bool isProtectedCanary)
        {
            SourcePath = sourcePath;
            IsProtectedCanary = isProtectedCanary;
            DisplayName = BuildDisplayName(sourcePath, isProtectedCanary);
            ShortPath = BuildShortPath(sourcePath);
        }

        private static string BuildDisplayName(string sourcePath, bool isProtectedCanary)
        {
            if (isProtectedCanary)
            {
                return "Restore verification canary";
            }

            string[,] knownFolders =
            {
                { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop" },
                { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents" },
                { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pictures" },
                { Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music" },
                { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Videos" },
                { Environment.GetFolderPath(Environment.SpecialFolder.Favorites), "Favorites" }
            };
            for (int index = 0; index < knownFolders.GetLength(0); index++)
            {
                string knownPath = knownFolders[index, 0];
                if (!string.IsNullOrWhiteSpace(knownPath) &&
                    string.Equals(sourcePath, knownPath, StringComparison.OrdinalIgnoreCase))
                {
                    return knownFolders[index, 1];
                }
            }

            string root = Path.GetPathRoot(sourcePath);
            if (string.Equals(sourcePath, root, StringComparison.OrdinalIgnoreCase))
            {
                return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + " drive";
            }

            string folderName = new DirectoryInfo(sourcePath).Name;
            if (string.Equals(folderName, ".codex", StringComparison.OrdinalIgnoreCase))
            {
                return "Codex configuration";
            }
            if (string.Equals(folderName, "My Drive", StringComparison.OrdinalIgnoreCase))
            {
                return "Google Drive - My Drive";
            }
            if (string.Equals(folderName, "code", StringComparison.OrdinalIgnoreCase))
            {
                return "Software projects";
            }
            return string.IsNullOrWhiteSpace(folderName) ? sourcePath : folderName;
        }

        private static string BuildShortPath(string sourcePath)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string shortened = ReplacePathRoot(sourcePath, userProfile, "~");
            shortened = ReplacePathRoot(shortened, programData, "%PROGRAMDATA%");
            if (shortened.Length <= 58)
            {
                return shortened;
            }

            string root = Path.GetPathRoot(sourcePath) ?? string.Empty;
            DirectoryInfo directory = new DirectoryInfo(sourcePath);
            string leaf = directory.Name;
            string parent = directory.Parent == null ? string.Empty : directory.Parent.Name;
            string tail = string.IsNullOrWhiteSpace(parent)
                ? leaf
                : parent + Path.DirectorySeparatorChar + leaf;
            return root + "..." + Path.DirectorySeparatorChar + tail;
        }

        private static string ReplacePathRoot(string sourcePath, string rootPath, string replacement)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return sourcePath;
            }
            string normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(sourcePath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return replacement;
            }
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (sourcePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return replacement + Path.DirectorySeparatorChar + sourcePath.Substring(prefix.Length);
            }
            return sourcePath;
        }
    }

    internal sealed class SourceConfiguration
    {
        public string InstallRoot { get; private set; }
        public string ConfigurationPath { get; private set; }
        public string ManagerPath { get; private set; }
        public string RepositoryPath { get; private set; }
        public string StateDirectory { get; private set; }
        public string PlanId { get; private set; }
        public long ConfigGeneration { get; private set; }
        public string CloudPlaceholderPolicy { get; private set; }
        public IList<BackupSourceView> Sources { get; private set; }

        private SourceConfiguration()
        {
        }

        public static SourceConfiguration Load()
        {
            string installRoot = ResolveProtectedInstallRoot();
            string configurationPath = Path.Combine(installRoot, "backup-config.json");
            if (!File.Exists(configurationPath))
            {
                throw new FileNotFoundException(
                    "The installed backup configuration was not found.",
                    configurationPath);
            }

            FileInfo configurationFile = new FileInfo(configurationPath);
            if (configurationFile.Length > 1024 * 1024)
            {
                throw new InvalidDataException("The backup configuration is unexpectedly large.");
            }

            string json = File.ReadAllText(configurationPath, Encoding.UTF8);
            IDictionary<string, object> document =
                new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
            if (document == null)
            {
                throw new InvalidDataException("The backup configuration is not a JSON object.");
            }

            object planValue;
            string planId = document.TryGetValue("plan_id", out planValue)
                ? planValue as string
                : null;
            Guid parsedPlanId;
            if (string.IsNullOrWhiteSpace(planId) ||
                !Guid.TryParseExact(planId, "D", out parsedPlanId) ||
                !string.Equals(planId, parsedPlanId.ToString("D"), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The backup configuration contains an invalid plan identity.");
            }

            long configGeneration = ReadPositiveInteger(document, "config_generation");
            object cloudPolicyValue;
            string cloudPolicy = document.TryGetValue("cloud_placeholder_policy", out cloudPolicyValue)
                ? cloudPolicyValue as string
                : null;
            if (cloudPolicy != "strict" && cloudPolicy != "allow")
            {
                throw new InvalidDataException(
                    "The backup configuration contains an invalid cloud placeholder policy.");
            }

            object repositoryValue;
            string repositoryPath = document.TryGetValue("repository", out repositoryValue)
                ? repositoryValue as string
                : null;
            if (string.IsNullOrWhiteSpace(repositoryPath) || !Path.IsPathRooted(repositoryPath))
            {
                throw new InvalidDataException("The backup configuration contains an invalid repository path.");
            }
            repositoryPath = NormalizePath(repositoryPath);

            object stateDirectoryValue;
            string stateDirectory = document.TryGetValue(
                "state_directory",
                out stateDirectoryValue)
                    ? stateDirectoryValue as string
                    : null;
            if (string.IsNullOrWhiteSpace(stateDirectory) ||
                !Path.IsPathRooted(stateDirectory))
            {
                throw new InvalidDataException(
                    "The backup configuration contains an invalid state directory.");
            }
            stateDirectory = NormalizePath(stateDirectory);

            object sourceValue;
            IEnumerable sourceItems = null;
            if (document.TryGetValue("sources", out sourceValue) && !(sourceValue is string))
            {
                sourceItems = sourceValue as IEnumerable;
            }
            if (sourceItems == null)
            {
                throw new InvalidDataException("The backup configuration has no source folder list.");
            }

            string canaryDirectory = null;
            object canaryValue;
            if (document.TryGetValue("canary_file", out canaryValue) && canaryValue is string)
            {
                string canaryFile = (string)canaryValue;
                if (Path.IsPathRooted(canaryFile))
                {
                    canaryDirectory = Path.GetDirectoryName(Path.GetFullPath(canaryFile));
                }
            }

            List<BackupSourceView> sources = new List<BackupSourceView>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in sourceItems)
            {
                string source = item as string;
                if (string.IsNullOrWhiteSpace(source) || !Path.IsPathRooted(source))
                {
                    throw new InvalidDataException("The backup configuration contains an invalid source path.");
                }
                string absoluteSource = NormalizePath(source);
                if (!seen.Add(absoluteSource))
                {
                    continue;
                }
                bool protectedCanary = canaryDirectory != null &&
                    string.Equals(absoluteSource, NormalizePath(canaryDirectory), StringComparison.OrdinalIgnoreCase);
                sources.Add(new BackupSourceView(absoluteSource, protectedCanary));
            }
            if (sources.Count == 0)
            {
                throw new InvalidDataException("The backup configuration contains no source folders.");
            }

            object identitiesValue;
            IDictionary<string, object> identities =
                document.TryGetValue("source_identities", out identitiesValue)
                    ? identitiesValue as IDictionary<string, object>
                    : null;
            if (identities == null || identities.Count != sources.Count)
            {
                throw new InvalidDataException(
                    "The backup configuration must identify the volume for every source folder.");
            }
            foreach (BackupSourceView source in sources)
            {
                string matchingKey = null;
                int matches = 0;
                foreach (string identityKey in identities.Keys)
                {
                    if (string.Equals(identityKey, source.SourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingKey = identityKey;
                        matches++;
                    }
                }
                if (matches != 1 ||
                    !string.Equals(matchingKey, source.SourcePath, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "A source volume identity is missing, duplicated, or non-canonical.");
                }
                IDictionary<string, object> identity =
                    identities[matchingKey] as IDictionary<string, object>;
                object serialValue;
                string serial = identity != null &&
                    identity.TryGetValue("expected_volume_serial", out serialValue)
                        ? serialValue as string
                        : null;
                if (!IsEightHex(serial))
                {
                    throw new InvalidDataException(
                        "A configured source volume identity is invalid.");
                }
            }

            return new SourceConfiguration
            {
                InstallRoot = installRoot,
                ConfigurationPath = configurationPath,
                ManagerPath = Path.Combine(installRoot, "Manage-Sources.ps1"),
                RepositoryPath = repositoryPath,
                StateDirectory = stateDirectory,
                PlanId = planId,
                ConfigGeneration = configGeneration,
                CloudPlaceholderPolicy = cloudPolicy,
                Sources = sources
            };
        }

        public bool ContainsUserSource(string sourcePath)
        {
            string normalized = NormalizePath(sourcePath);
            foreach (BackupSourceView source in Sources)
            {
                if (!source.IsProtectedCanary &&
                    string.Equals(source.SourcePath, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ResolveProtectedInstallRoot()
        {
            string programFiles = Path.GetFullPath(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            string defaultRoot = Path.Combine(programFiles, "ResticBackuper");
            string executableRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (IsWithin(executableRoot, programFiles) &&
                File.Exists(Path.Combine(executableRoot, "backup-config.json")))
            {
                return executableRoot;
            }
            return defaultRoot;
        }

        private static bool IsWithin(string candidate, string parent)
        {
            string normalizedCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedParent = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizePath(string value)
        {
            string fullPath = Path.GetFullPath(value);
            string root = Path.GetPathRoot(fullPath);
            if (!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return fullPath;
        }

        private static long ReadPositiveInteger(
            IDictionary<string, object> document,
            string name)
        {
            object value;
            if (!document.TryGetValue(name, out value) ||
                (!(value is int) && !(value is long)))
            {
                throw new InvalidDataException(
                    "The backup configuration contains an invalid " + name + ".");
            }
            long parsed = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (parsed <= 0)
            {
                throw new InvalidDataException(
                    "The backup configuration contains an invalid " + name + ".");
            }
            return parsed;
        }

        private static bool IsEightHex(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 8)
            {
                return false;
            }
            foreach (char item in value)
            {
                bool isHex = (item >= '0' && item <= '9') ||
                    (item >= 'a' && item <= 'f') ||
                    (item >= 'A' && item <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }
    }

    internal sealed class SourceManagerResult
    {
        public bool Succeeded { get; private set; }
        public bool UserCancelled { get; private set; }
        public int ExitCode { get; private set; }
        public string PlanId { get; private set; }
        public long PreviousConfigGeneration { get; private set; }
        public long ConfigGeneration { get; private set; }
        public string ChangedPath { get; private set; }
        public string ErrorMessage { get; private set; }

        public static SourceManagerResult Success(
            string planId,
            long previousConfigGeneration,
            long configGeneration,
            string changedPath)
        {
            return new SourceManagerResult
            {
                Succeeded = true,
                ExitCode = 0,
                PlanId = planId,
                PreviousConfigGeneration = previousConfigGeneration,
                ConfigGeneration = configGeneration,
                ChangedPath = changedPath
            };
        }

        public static SourceManagerResult Cancelled()
        {
            return new SourceManagerResult { UserCancelled = true, ExitCode = 1223 };
        }

        public static SourceManagerResult Failure(string message, int exitCode)
        {
            return new SourceManagerResult { ErrorMessage = message, ExitCode = exitCode };
        }
    }

    internal static class SourceManagerLauncher
    {
        public static SourceManagerResult Run(SourceConfiguration configuration, string action, string sourcePath)
        {
            return Run(configuration, action, sourcePath, null);
        }

        public static SourceManagerResult Run(
            SourceConfiguration configuration,
            string action,
            string sourcePath,
            Action onElevatedProcessStarted)
        {
            if (configuration == null)
            {
                return SourceManagerResult.Failure("The protected configuration is unavailable.", -1);
            }
            if (!string.Equals(action, "Add", StringComparison.Ordinal) &&
                !string.Equals(action, "Remove", StringComparison.Ordinal))
            {
                return SourceManagerResult.Failure("The requested source operation is invalid.", -1);
            }
            if (!Path.IsPathRooted(sourcePath))
            {
                return SourceManagerResult.Failure("The selected folder path is not absolute.", -1);
            }
            if (!File.Exists(configuration.ManagerPath))
            {
                return SourceManagerResult.Failure(
                    "The protected source manager is missing from the installation.",
                    -1);
            }
            FileAttributes managerAttributes = File.GetAttributes(configuration.ManagerPath);
            if ((managerAttributes & FileAttributes.ReparsePoint) != 0)
            {
                return SourceManagerResult.Failure("The protected source manager is not a regular file.", -1);
            }

            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string powerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powerShell))
            {
                return SourceManagerResult.Failure("Windows PowerShell is unavailable in System32.", -1);
            }

            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string userSid = identity.User == null ? null : identity.User.Value;
            if (string.IsNullOrEmpty(userSid))
            {
                return SourceManagerResult.Failure("The current Windows user SID could not be determined.", -1);
            }

            string[] arguments =
            {
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-WindowStyle",
                "Hidden",
                "-File",
                configuration.ManagerPath,
                "-" + action,
                SourceConfiguration.NormalizePath(sourcePath),
                "-ExpectedUserSid",
                userSid
            };

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = powerShell;
            startInfo.Arguments = BuildArgumentString(arguments);
            startInfo.WorkingDirectory = configuration.InstallRoot;
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.ErrorDialog = false;

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return SourceManagerResult.Failure("Windows did not start the protected source manager.", -1);
                    }
                    if (onElevatedProcessStarted != null)
                    {
                        try
                        {
                            onElevatedProcessStarted();
                        }
                        catch
                        {
                            // Presentation callbacks must never interrupt the protected manager.
                        }
                    }
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        return SourceManagerResult.Failure(
                            "The protected source manager returned exit code " +
                                process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".",
                            process.ExitCode);
                    }

                    SourceConfiguration updated = SourceConfiguration.Load();
                    return SourceManagerResult.Success(
                        updated.PlanId,
                        configuration.ConfigGeneration,
                        updated.ConfigGeneration,
                        SourceConfiguration.NormalizePath(sourcePath));
                }
            }
            catch (Win32Exception error)
            {
                if (error.NativeErrorCode == 1223)
                {
                    return SourceManagerResult.Cancelled();
                }
                return SourceManagerResult.Failure(error.Message, error.NativeErrorCode);
            }
            catch (Exception error)
            {
                return SourceManagerResult.Failure(error.Message, -1);
            }
        }

        private static string BuildArgumentString(IEnumerable<string> arguments)
        {
            StringBuilder result = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(QuoteWindowsArgument(argument));
            }
            return result.ToString();
        }

        private static string QuoteWindowsArgument(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return value;
            }

            StringBuilder result = new StringBuilder();
            result.Append('"');
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }
                result.Append('\\', backslashes);
                backslashes = 0;
                result.Append(character);
            }
            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }
    }
}
