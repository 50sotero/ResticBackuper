using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ResticBackuper.Dashboard
{
    public enum DashboardThemePreference
    {
        System,
        Midnight,
        Daylight
    }

    public enum DashboardThemeKind
    {
        Midnight,
        Daylight,
        WindowsHighContrast
    }

    public sealed class DashboardThemeResolution
    {
        internal DashboardThemeResolution(
            DashboardThemePreference preference,
            DashboardThemeKind effectiveKind,
            DashboardThemePalette palette)
        {
            Preference = preference;
            EffectiveKind = effectiveKind;
            Palette = palette;
        }

        public DashboardThemePreference Preference { get; private set; }

        public DashboardThemeKind EffectiveKind { get; private set; }

        public DashboardThemePalette Palette { get; private set; }

        public bool IsFollowingSystem
        {
            get { return Preference == DashboardThemePreference.System; }
        }

        public bool IsHighContrast
        {
            get { return EffectiveKind == DashboardThemeKind.WindowsHighContrast; }
        }
    }

    /// <summary>
    /// Immutable, semantic color tokens for the dashboard. Each brush is frozen so palettes can
    /// safely be shared by windows and drawing elements on the UI thread.
    /// </summary>
    public sealed class DashboardThemePalette
    {
        private readonly IDictionary<string, SolidColorBrush> legacyBrushes;

        internal DashboardThemePalette(PaletteDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            Id = definition.Id;
            DisplayName = definition.DisplayName;
            IsDark = definition.IsDark;

            BackgroundTop = CreateBrush(definition.BackgroundTop);
            BackgroundBottom = CreateBrush(definition.BackgroundBottom);
            Surface = CreateBrush(definition.Surface);
            SurfaceSoft = CreateBrush(definition.SurfaceSoft);
            Border = CreateBrush(definition.Border);
            TextPrimary = CreateBrush(definition.TextPrimary);
            TextSecondary = CreateBrush(definition.TextSecondary);
            TextTertiary = CreateBrush(definition.TextTertiary);
            TableText = CreateBrush(definition.TableText);
            AccentPrimary = CreateBrush(definition.AccentPrimary);
            AccentInfo = CreateBrush(definition.AccentInfo);
            AccentInfoText = CreateBrush(definition.AccentInfoText);
            AccentPurple = CreateBrush(definition.AccentPurple);
            Success = CreateBrush(definition.Success);
            Danger = CreateBrush(definition.Danger);
            Warning = CreateBrush(definition.Warning);
            ButtonBackground = CreateBrush(definition.ButtonBackground);
            ButtonText = CreateBrush(definition.ButtonText);
            ProgressTrack = CreateBrush(definition.ProgressTrack);
            ScheduleBackground = CreateBrush(definition.ScheduleBackground);
            PhaseInactive = CreateBrush(definition.PhaseInactive);
            TextOnAccent = CreateBrush(definition.TextOnAccent);
            FooterText = CreateBrush(definition.FooterText);
            AddButtonText = CreateBrush(definition.AddButtonText);
            AddButtonBorder = CreateBrush(definition.AddButtonBorder);
            SafetyBackground = CreateBrush(definition.SafetyBackground);
            SafetyBorder = CreateBrush(definition.SafetyBorder);
            SafetyText = CreateBrush(definition.SafetyText);
            RowEven = CreateBrush(definition.RowEven);
            RowOdd = CreateBrush(definition.RowOdd);
            RowBorder = CreateBrush(definition.RowBorder);
            RequiredBackground = CreateBrush(definition.RequiredBackground);
            RequiredBorder = CreateBrush(definition.RequiredBorder);
            RemoveBackground = CreateBrush(definition.RemoveBackground);
            RemoveBorder = CreateBrush(definition.RemoveBorder);
            RemoveText = CreateBrush(definition.RemoveText);
            DangerActionText = CreateBrush(definition.DangerActionText);
            DangerActionBorder = CreateBrush(definition.DangerActionBorder);
            GridLine = CreateBrush(definition.GridLine);
            AlternatingRow = CreateBrush(definition.AlternatingRow);
            Selection = CreateBrush(definition.Selection);
            SelectionText = CreateBrush(definition.SelectionText);
            Focus = CreateBrush(definition.Focus);
            StatusLive = CreateBrush(definition.StatusLive);
            StatusWarning = CreateBrush(definition.StatusWarning);
            StatusDanger = CreateBrush(definition.StatusDanger);
            StatusSuccess = CreateBrush(definition.StatusSuccess);
            StatusReady = CreateBrush(definition.StatusReady);
            ChartMuted = CreateBrush(definition.ChartMuted);
            ChartGrid = CreateBrush(definition.ChartGrid);
            ChartPointOutline = CreateBrush(definition.ChartPointOutline);

            legacyBrushes = BuildLegacyBrushMap();
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public bool IsDark { get; private set; }

        public SolidColorBrush BackgroundTop { get; private set; }

        public SolidColorBrush BackgroundBottom { get; private set; }

        public SolidColorBrush Surface { get; private set; }

        public SolidColorBrush SurfaceSoft { get; private set; }

        public SolidColorBrush Border { get; private set; }

        public SolidColorBrush TextPrimary { get; private set; }

        public SolidColorBrush TextSecondary { get; private set; }

        public SolidColorBrush TextTertiary { get; private set; }

        public SolidColorBrush TableText { get; private set; }

        public SolidColorBrush AccentPrimary { get; private set; }

        public SolidColorBrush AccentInfo { get; private set; }

        public SolidColorBrush AccentInfoText { get; private set; }

        public SolidColorBrush AccentPurple { get; private set; }

        public SolidColorBrush Success { get; private set; }

        public SolidColorBrush Danger { get; private set; }

        public SolidColorBrush Warning { get; private set; }

        public SolidColorBrush ButtonBackground { get; private set; }

        public SolidColorBrush ButtonText { get; private set; }

        public SolidColorBrush ProgressTrack { get; private set; }

        public SolidColorBrush ScheduleBackground { get; private set; }

        public SolidColorBrush PhaseInactive { get; private set; }

        public SolidColorBrush TextOnAccent { get; private set; }

        public SolidColorBrush FooterText { get; private set; }

        public SolidColorBrush AddButtonText { get; private set; }

        public SolidColorBrush AddButtonBorder { get; private set; }

        public SolidColorBrush SafetyBackground { get; private set; }

        public SolidColorBrush SafetyBorder { get; private set; }

        public SolidColorBrush SafetyText { get; private set; }

        public SolidColorBrush RowEven { get; private set; }

        public SolidColorBrush RowOdd { get; private set; }

        public SolidColorBrush RowBorder { get; private set; }

        public SolidColorBrush RequiredBackground { get; private set; }

        public SolidColorBrush RequiredBorder { get; private set; }

        public SolidColorBrush RemoveBackground { get; private set; }

        public SolidColorBrush RemoveBorder { get; private set; }

        public SolidColorBrush RemoveText { get; private set; }

        public SolidColorBrush DangerActionText { get; private set; }

        public SolidColorBrush DangerActionBorder { get; private set; }

        public SolidColorBrush GridLine { get; private set; }

        public SolidColorBrush AlternatingRow { get; private set; }

        public SolidColorBrush Selection { get; private set; }

        public SolidColorBrush SelectionText { get; private set; }

        public SolidColorBrush Focus { get; private set; }

        public SolidColorBrush StatusLive { get; private set; }

        public SolidColorBrush StatusWarning { get; private set; }

        public SolidColorBrush StatusDanger { get; private set; }

        public SolidColorBrush StatusSuccess { get; private set; }

        public SolidColorBrush StatusReady { get; private set; }

        public SolidColorBrush ChartMuted { get; private set; }

        public SolidColorBrush ChartGrid { get; private set; }

        public SolidColorBrush ChartPointOutline { get; private set; }

        /// <summary>
        /// Maps a color literal from the original Midnight-only dashboard to its semantic token.
        /// This keeps incremental UI refactors theme-correct while hard-coded colors are removed.
        /// </summary>
        public SolidColorBrush BrushForLegacy(string midnightHex)
        {
            SolidColorBrush brush;
            if (TryGetLegacyBrush(midnightHex, out brush))
            {
                return brush;
            }

            throw new ArgumentException("The legacy dashboard color is not mapped.", "midnightHex");
        }

        public bool TryGetLegacyBrush(string midnightHex, out SolidColorBrush brush)
        {
            brush = null;
            if (string.IsNullOrWhiteSpace(midnightHex))
            {
                return false;
            }

            return legacyBrushes.TryGetValue(midnightHex.Trim(), out brush);
        }

        private IDictionary<string, SolidColorBrush> BuildLegacyBrushMap()
        {
            Dictionary<string, SolidColorBrush> brushes =
                new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

            brushes["#07131E"] = TextOnAccent;
            brushes["#07151D"] = AddButtonText;
            brushes["#08111F"] = BackgroundTop;
            brushes["#0C1424"] = ChartPointOutline;
            brushes["#0D1728"] = SurfaceSoft;
            brushes["#0D1926"] = RowOdd;
            brushes["#0F1A2B"] = AlternatingRow;
            brushes["#101B30"] = BackgroundBottom;
            brushes["#101E2C"] = RowEven;
            brushes["#111D2E"] = SurfaceSoft;
            brushes["#111D31"] = Surface;
            brushes["#12352F"] = StatusLive;
            brushes["#132638"] = SafetyBackground;
            brushes["#14243A"] = ScheduleBackground;
            brushes["#14352C"] = StatusSuccess;
            brushes["#17243A"] = ButtonBackground;
            brushes["#172B45"] = StatusReady;
            brushes["#172E43"] = RequiredBackground;
            brushes["#201016"] = DangerActionText;
            brushes["#213D59"] = Selection;
            brushes["#223149"] = GridLine;
            brushes["#22384B"] = RowBorder;
            brushes["#243149"] = ChartGrid;
            brushes["#25334A"] = ProgressTrack;
            brushes["#25334B"] = Border;
            brushes["#294157"] = SafetyBorder;
            brushes["#2B1C2A"] = RemoveBackground;
            brushes["#2DD4BF"] = AccentPrimary;
            brushes["#34D399"] = Success;
            brushes["#3A2F18"] = StatusWarning;
            brushes["#3B1D2A"] = StatusDanger;
            brushes["#3B5A70"] = RequiredBorder;
            brushes["#52D6C6"] = AddButtonBorder;
            brushes["#60A5FA"] = AccentInfo;
            brushes["#617089"] = FooterText;
            brushes["#7A3B4A"] = RemoveBorder;
            brushes["#8190A8"] = ChartMuted;
            brushes["#8FA0B8"] = TextSecondary;
            brushes["#A78BFA"] = AccentPurple;
            brushes["#B7C5D1"] = SafetyText;
            brushes["#D7E1EF"] = TableText;
            brushes["#F3F7FC"] = TextPrimary;
            brushes["#FB7185"] = Danger;
            brushes["#FBBF24"] = Warning;
            brushes["#FF9AAA"] = DangerActionBorder;
            brushes["#FFB2BC"] = RemoveText;

            return brushes;
        }

        private static SolidColorBrush CreateBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public static class DashboardThemeManager
    {
        private const int SettingsSchemaVersion = 1;
        private const int MaximumSettingsBytes = 16 * 1024;
        private const string DashboardDirectoryName = "ResticBackuperDashboard";
        private const string SettingsFileName = "settings.json";
        private const string PersonalizeRegistryPath =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private static readonly object SettingsSync = new object();
        internal static string IsolatedSettingsRoot;
        private static readonly DashboardThemePalette MidnightPalette =
            new DashboardThemePalette(CreateMidnightDefinition());
        private static readonly DashboardThemePalette DaylightPalette =
            new DashboardThemePalette(CreateDaylightDefinition());

        public static string SettingsPath
        {
            get { return GetSettingsPath(); }
        }

        public static DashboardThemeResolution LoadAndResolve()
        {
            return Resolve(LoadPreference());
        }

        public static DashboardThemeResolution Resolve(DashboardThemePreference preference)
        {
            if (!Enum.IsDefined(typeof(DashboardThemePreference), preference))
            {
                preference = DashboardThemePreference.System;
            }

            if (SystemParameters.HighContrast)
            {
                DashboardThemePalette highContrast =
                    new DashboardThemePalette(CreateHighContrastDefinition());
                return new DashboardThemeResolution(
                    preference,
                    DashboardThemeKind.WindowsHighContrast,
                    highContrast);
            }

            DashboardThemeKind effective = preference == DashboardThemePreference.Midnight
                ? DashboardThemeKind.Midnight
                : preference == DashboardThemePreference.Daylight
                    ? DashboardThemeKind.Daylight
                    : DetectSystemThemeKind();
            DashboardThemePalette palette = effective == DashboardThemeKind.Daylight
                ? DaylightPalette
                : MidnightPalette;
            return new DashboardThemeResolution(preference, effective, palette);
        }

        public static DashboardThemeKind DetectSystemThemeKind()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    PersonalizeRegistryPath,
                    false))
                {
                    if (key != null)
                    {
                        object raw = key.GetValue("AppsUseLightTheme", null);
                        int value;
                        if (raw != null && int.TryParse(
                            Convert.ToString(raw, CultureInfo.InvariantCulture),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out value))
                        {
                            return value == 0
                                ? DashboardThemeKind.Midnight
                                : DashboardThemeKind.Daylight;
                        }
                    }
                }
            }
            catch (Exception error)
            {
                if (IsFatal(error))
                {
                    throw;
                }
            }

            return IsDarkColor(SystemColors.WindowColor)
                ? DashboardThemeKind.Midnight
                : DashboardThemeKind.Daylight;
        }

        public static string GetPreferenceDisplayName(DashboardThemePreference preference)
        {
            switch (preference)
            {
                case DashboardThemePreference.Midnight:
                    return "Midnight";
                case DashboardThemePreference.Daylight:
                    return "Daylight";
                default:
                    return "System";
            }
        }

        public static bool TryParsePreference(
            string value,
            out DashboardThemePreference preference)
        {
            preference = DashboardThemePreference.System;
            if (string.Equals(value, "System", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(value, "Midnight", StringComparison.OrdinalIgnoreCase))
            {
                preference = DashboardThemePreference.Midnight;
                return true;
            }
            if (string.Equals(value, "Daylight", StringComparison.OrdinalIgnoreCase))
            {
                preference = DashboardThemePreference.Daylight;
                return true;
            }
            return false;
        }

        public static DashboardThemePreference LoadPreference()
        {
            lock (SettingsSync)
            {
                try
                {
                    string path = GetSettingsPath();
                    if (string.IsNullOrEmpty(path)
                        || !File.Exists(path)
                        || !IsSafeSettingsPath(path, false)
                        || IsReparsePoint(path))
                    {
                        return DashboardThemePreference.System;
                    }

                    string json;
                    using (FileStream stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete))
                    {
                        if (stream.Length <= 0 || stream.Length > MaximumSettingsBytes)
                        {
                            return DashboardThemePreference.System;
                        }

                        using (StreamReader reader = new StreamReader(
                            stream,
                            new UTF8Encoding(false, true),
                            true,
                            1024))
                        {
                            json = reader.ReadToEnd();
                        }
                    }

                    if (json.Length > MaximumSettingsBytes)
                    {
                        return DashboardThemePreference.System;
                    }

                    JavaScriptSerializer serializer = CreateSerializer();
                    IDictionary<string, object> document =
                        serializer.DeserializeObject(json) as IDictionary<string, object>;
                    if (document == null
                        || ReadSchemaVersion(document) != SettingsSchemaVersion)
                    {
                        return DashboardThemePreference.System;
                    }

                    object rawTheme;
                    DashboardThemePreference preference;
                    if (!document.TryGetValue("theme", out rawTheme)
                        || rawTheme == null
                        || !TryParsePreference(
                            Convert.ToString(rawTheme, CultureInfo.InvariantCulture),
                            out preference))
                    {
                        return DashboardThemePreference.System;
                    }

                    return preference;
                }
                catch (Exception error)
                {
                    if (IsFatal(error))
                    {
                        throw;
                    }
                    return DashboardThemePreference.System;
                }
            }
        }

        public static bool TrySavePreference(DashboardThemePreference preference)
        {
            if (!Enum.IsDefined(typeof(DashboardThemePreference), preference))
            {
                return false;
            }

            lock (SettingsSync)
            {
                string temporaryPath = null;
                string backupPath = null;
                try
                {
                    string targetPath = GetSettingsPath();
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        return false;
                    }

                    string directory = Path.GetDirectoryName(targetPath);
                    if (string.IsNullOrEmpty(directory))
                    {
                        return false;
                    }

                    Directory.CreateDirectory(directory);
                    if (!IsSafeSettingsPath(targetPath, true)
                        || IsReparsePoint(directory)
                        || (File.Exists(targetPath) && IsReparsePoint(targetPath)))
                    {
                        return false;
                    }

                    Dictionary<string, object> document = new Dictionary<string, object>();
                    document["schema_version"] = SettingsSchemaVersion;
                    document["theme"] = GetPreferenceDisplayName(preference);

                    string json = CreateSerializer().Serialize(document);
                    byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
                    if (bytes.Length > MaximumSettingsBytes)
                    {
                        return false;
                    }

                    temporaryPath = Path.Combine(
                        directory,
                        ".settings." + Guid.NewGuid().ToString("N") + ".tmp");
                    backupPath = Path.Combine(
                        directory,
                        ".settings." + Guid.NewGuid().ToString("N") + ".bak");
                    using (FileStream stream = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.WriteThrough))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }

                    if (File.Exists(targetPath))
                    {
                        if (IsReparsePoint(targetPath))
                        {
                            return false;
                        }
                        File.Replace(temporaryPath, targetPath, backupPath, true);
                        File.Delete(backupPath);
                    }
                    else
                    {
                        try
                        {
                            File.Move(temporaryPath, targetPath);
                        }
                        catch (IOException)
                        {
                            if (!File.Exists(targetPath) || IsReparsePoint(targetPath))
                            {
                                throw;
                            }
                            File.Replace(temporaryPath, targetPath, backupPath, true);
                            File.Delete(backupPath);
                        }
                    }

                    temporaryPath = null;
                    return true;
                }
                catch (Exception error)
                {
                    if (IsFatal(error))
                    {
                        throw;
                    }
                    return false;
                }
                finally
                {
                    if (!string.IsNullOrEmpty(temporaryPath))
                    {
                        try
                        {
                            if (File.Exists(temporaryPath))
                            {
                                File.Delete(temporaryPath);
                            }
                        }
                        catch (Exception error)
                        {
                            if (IsFatal(error))
                            {
                                throw;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(backupPath))
                    {
                        try
                        {
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                        }
                        catch (Exception error)
                        {
                            if (IsFatal(error))
                            {
                                throw;
                            }
                        }
                    }
                }
            }
        }

        private static string GetSettingsPath()
        {
            try
            {
                string localApplicationData = IsolatedSettingsRoot ?? Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localApplicationData))
                {
                    return string.Empty;
                }

                string localRoot = Path.GetFullPath(localApplicationData);
                string directory = Path.Combine(localRoot, DashboardDirectoryName);
                string candidate = Path.GetFullPath(Path.Combine(directory, SettingsFileName));
                return IsPathInside(candidate, localRoot) ? candidate : string.Empty;
            }
            catch (Exception error)
            {
                if (IsFatal(error))
                {
                    throw;
                }
                return string.Empty;
            }
        }

        private static bool IsSafeSettingsPath(string path, bool directoryMustExist)
        {
            try
            {
                string localApplicationData = IsolatedSettingsRoot ?? Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localApplicationData))
                {
                    return false;
                }

                string localRoot = Path.GetFullPath(localApplicationData);
                string expectedDirectory = Path.GetFullPath(
                    Path.Combine(localRoot, DashboardDirectoryName));
                string candidate = Path.GetFullPath(path);
                string candidateDirectory = Path.GetDirectoryName(candidate);
                if (!IsPathInside(candidate, localRoot)
                    || !string.Equals(
                        candidateDirectory,
                        expectedDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (directoryMustExist && !Directory.Exists(expectedDirectory))
                {
                    return false;
                }
                if (Directory.Exists(expectedDirectory) && IsReparsePoint(expectedDirectory))
                {
                    return false;
                }

                return true;
            }
            catch (Exception error)
            {
                if (IsFatal(error))
                {
                    throw;
                }
                return false;
            }
        }

        private static bool IsPathInside(string candidatePath, string parentPath)
        {
            string candidate = Path.GetFullPath(candidatePath);
            string parent = Path.GetFullPath(parentPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return candidate.StartsWith(
                parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static int ReadSchemaVersion(IDictionary<string, object> document)
        {
            object raw;
            int value;
            if (!document.TryGetValue("schema_version", out raw)
                || raw == null
                || !int.TryParse(
                    Convert.ToString(raw, CultureInfo.InvariantCulture),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return 0;
            }
            return value;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaximumSettingsBytes;
            serializer.RecursionLimit = 8;
            return serializer;
        }

        private static bool IsFatal(Exception error)
        {
            return error is OutOfMemoryException
                || error is StackOverflowException
                || error is ThreadAbortException
                || error is AccessViolationException;
        }

        private static bool IsDarkColor(Color color)
        {
            double luminance = (0.2126 * color.R)
                + (0.7152 * color.G)
                + (0.0722 * color.B);
            return luminance < 128;
        }

        private static PaletteDefinition CreateMidnightDefinition()
        {
            return new PaletteDefinition
            {
                Id = "midnight",
                DisplayName = "Midnight",
                IsDark = true,
                BackgroundTop = Hex("#17181A"),
                BackgroundBottom = Hex("#1B1C1E"),
                Surface = Hex("#232427"),
                SurfaceSoft = Hex("#1C1D1F"),
                Border = Hex("#2E3033"),
                TextPrimary = Hex("#F2F3F4"),
                TextSecondary = Hex("#A5A8AD"),
                TextTertiary = Hex("#8A8D93"),
                TableText = Hex("#E4E5E7"),
                AccentPrimary = Hex("#3DBB72"),
                AccentInfo = Hex("#3D9AFF"),
                AccentInfoText = Hex("#7EC0FF"),
                AccentPurple = Hex("#AA7BFA"),
                Success = Hex("#3DBB72"),
                Danger = Hex("#EE5C61"),
                Warning = Hex("#F68F3C"),
                ButtonBackground = Hex("#2B2C2F"),
                ButtonText = Hex("#F2F3F4"),
                ProgressTrack = Hex("#3A3C40"),
                ScheduleBackground = Hex("#1F2022"),
                PhaseInactive = Hex("#3A3C40"),
                TextOnAccent = Hex("#101214"),
                FooterText = Hex("#A5A8AD"),
                AddButtonText = Hex("#101214"),
                AddButtonBorder = Hex("#3DBB72"),
                SafetyBackground = Hex("#1C2221"),
                SafetyBorder = Hex("#36423F"),
                SafetyText = Hex("#C0C5C3"),
                RowEven = Hex("#232427"),
                RowOdd = Hex("#1F2022"),
                RowBorder = Hex("#2E3033"),
                RequiredBackground = Hex("#202A31"),
                RequiredBorder = Hex("#3B4C58"),
                RemoveBackground = Hex("#2B2022"),
                RemoveBorder = Hex("#6A3A3E"),
                RemoveText = Hex("#FFB0B3"),
                DangerActionText = Hex("#161719"),
                DangerActionBorder = Hex("#FF9CA1"),
                GridLine = Hex("#2E3033"),
                AlternatingRow = Hex("#1F2022"),
                Selection = Hex("#34363A"),
                SelectionText = Hex("#F2F3F4"),
                Focus = Hex("#7EC0FF"),
                StatusLive = Hex("#1D3427"),
                StatusWarning = Hex("#3A2B1D"),
                StatusDanger = Hex("#3A2022"),
                StatusSuccess = Hex("#1D3427"),
                StatusReady = Hex("#202A35"),
                ChartMuted = Hex("#A5A8AD"),
                ChartGrid = Hex("#2E3033"),
                ChartPointOutline = Hex("#17181A")
            };
        }

        private static PaletteDefinition CreateDaylightDefinition()
        {
            return new PaletteDefinition
            {
                Id = "daylight",
                DisplayName = "Daylight",
                IsDark = false,
                BackgroundTop = Hex("#F6F6F3"),
                BackgroundBottom = Hex("#EEEFEA"),
                Surface = Hex("#FFFFFF"),
                SurfaceSoft = Hex("#F0F1EE"),
                Border = Hex("#D8D9D5"),
                TextPrimary = Hex("#191A1C"),
                TextSecondary = Hex("#4E5156"),
                TextTertiary = Hex("#63666C"),
                TableText = Hex("#24262A"),
                AccentPrimary = Hex("#1E7043"),
                AccentInfo = Hex("#176FC1"),
                AccentInfoText = Hex("#155E9D"),
                AccentPurple = Hex("#6741B8"),
                Success = Hex("#267647"),
                Danger = Hex("#B33A42"),
                Warning = Hex("#8A4B00"),
                ButtonBackground = Hex("#E9EAE6"),
                ButtonText = Hex("#1E2023"),
                ProgressTrack = Hex("#D5D7D2"),
                ScheduleBackground = Hex("#EDF2F6"),
                PhaseInactive = Hex("#D5D7D2"),
                TextOnAccent = Hex("#FFFFFF"),
                FooterText = Hex("#55585E"),
                AddButtonText = Hex("#FFFFFF"),
                AddButtonBorder = Hex("#1E7043"),
                SafetyBackground = Hex("#EDF5F1"),
                SafetyBorder = Hex("#B7CBC0"),
                SafetyText = Hex("#3B5648"),
                RowEven = Hex("#F9F9F7"),
                RowOdd = Hex("#F3F4F1"),
                RowBorder = Hex("#D8D9D5"),
                RequiredBackground = Hex("#EEF3F6"),
                RequiredBorder = Hex("#B8C8D0"),
                RemoveBackground = Hex("#FAEDEF"),
                RemoveBorder = Hex("#C7797F"),
                RemoveText = Hex("#7D2930"),
                DangerActionText = Hex("#FFFFFF"),
                DangerActionBorder = Hex("#7D2930"),
                GridLine = Hex("#D9DBD7"),
                AlternatingRow = Hex("#F2F3F0"),
                Selection = Hex("#DCEBFA"),
                SelectionText = Hex("#191A1C"),
                Focus = Hex("#075FAF"),
                StatusLive = Hex("#DCEFE3"),
                StatusWarning = Hex("#F8E8D1"),
                StatusDanger = Hex("#F4DFE1"),
                StatusSuccess = Hex("#DCEFE3"),
                StatusReady = Hex("#DFEAF4"),
                ChartMuted = Hex("#5C6269"),
                ChartGrid = Hex("#D9DBD7"),
                ChartPointOutline = Hex("#FFFFFF")
            };
        }

        private static PaletteDefinition CreateHighContrastDefinition()
        {
            Color window = SystemColors.WindowColor;
            Color windowText = SystemColors.WindowTextColor;
            Color control = SystemColors.ControlColor;
            Color controlText = SystemColors.ControlTextColor;
            Color highlight = SystemColors.HighlightColor;
            Color highlightText = SystemColors.HighlightTextColor;

            return new PaletteDefinition
            {
                Id = "windows-high-contrast",
                DisplayName = "Windows High Contrast",
                IsDark = IsDarkColor(window),
                BackgroundTop = window,
                BackgroundBottom = window,
                Surface = window,
                SurfaceSoft = window,
                Border = windowText,
                TextPrimary = windowText,
                TextSecondary = windowText,
                TextTertiary = windowText,
                TableText = windowText,
                AccentPrimary = windowText,
                AccentInfo = windowText,
                AccentInfoText = windowText,
                AccentPurple = windowText,
                Success = windowText,
                Danger = windowText,
                Warning = windowText,
                ButtonBackground = control,
                ButtonText = controlText,
                ProgressTrack = window,
                ScheduleBackground = window,
                PhaseInactive = window,
                TextOnAccent = window,
                FooterText = windowText,
                AddButtonText = window,
                AddButtonBorder = windowText,
                SafetyBackground = window,
                SafetyBorder = windowText,
                SafetyText = windowText,
                RowEven = window,
                RowOdd = window,
                RowBorder = windowText,
                RequiredBackground = window,
                RequiredBorder = windowText,
                RemoveBackground = window,
                RemoveBorder = windowText,
                RemoveText = windowText,
                DangerActionText = window,
                DangerActionBorder = windowText,
                GridLine = windowText,
                AlternatingRow = window,
                Selection = highlight,
                SelectionText = highlightText,
                Focus = highlight,
                StatusLive = window,
                StatusWarning = window,
                StatusDanger = window,
                StatusSuccess = window,
                StatusReady = window,
                ChartMuted = windowText,
                ChartGrid = windowText,
                ChartPointOutline = window
            };
        }

        private static Color Hex(string value)
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
    }

    internal sealed class PaletteDefinition
    {
        public string Id;
        public string DisplayName;
        public bool IsDark;
        public Color BackgroundTop;
        public Color BackgroundBottom;
        public Color Surface;
        public Color SurfaceSoft;
        public Color Border;
        public Color TextPrimary;
        public Color TextSecondary;
        public Color TextTertiary;
        public Color TableText;
        public Color AccentPrimary;
        public Color AccentInfo;
        public Color AccentInfoText;
        public Color AccentPurple;
        public Color Success;
        public Color Danger;
        public Color Warning;
        public Color ButtonBackground;
        public Color ButtonText;
        public Color ProgressTrack;
        public Color ScheduleBackground;
        public Color PhaseInactive;
        public Color TextOnAccent;
        public Color FooterText;
        public Color AddButtonText;
        public Color AddButtonBorder;
        public Color SafetyBackground;
        public Color SafetyBorder;
        public Color SafetyText;
        public Color RowEven;
        public Color RowOdd;
        public Color RowBorder;
        public Color RequiredBackground;
        public Color RequiredBorder;
        public Color RemoveBackground;
        public Color RemoveBorder;
        public Color RemoveText;
        public Color DangerActionText;
        public Color DangerActionBorder;
        public Color GridLine;
        public Color AlternatingRow;
        public Color Selection;
        public Color SelectionText;
        public Color Focus;
        public Color StatusLive;
        public Color StatusWarning;
        public Color StatusDanger;
        public Color StatusSuccess;
        public Color StatusReady;
        public Color ChartMuted;
        public Color ChartGrid;
        public Color ChartPointOutline;
    }
}
