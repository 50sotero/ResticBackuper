using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ResticBackuper.Dashboard
{
    internal enum BackupFreshnessState
    {
        Unavailable = 0,
        Healthy = 1,
        NoVerifiedBackup = 2,
        Overdue = 3,
        Paused = 4,
        ClockAnomaly = 5
    }

    internal sealed class BackupFreshnessResult
    {
        internal BackupFreshnessResult(
            BackupFreshnessState state,
            DateTime? lastVerifiedUtc,
            DateTime? lastVerifiedLocal,
            DateTime? latestDueLocal,
            DateTime? latestDueUtc,
            DateTime? nextScheduledLocal,
            string statusLabel,
            string detail)
        {
            State = state;
            LastVerifiedUtc = lastVerifiedUtc;
            LastVerifiedLocal = lastVerifiedLocal;
            LatestDueLocal = latestDueLocal;
            LatestDueUtc = latestDueUtc;
            NextScheduledLocal = nextScheduledLocal;
            StatusLabel = statusLabel ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public BackupFreshnessState State { get; private set; }
        public DateTime? LastVerifiedUtc { get; private set; }
        public DateTime? LastVerifiedLocal { get; private set; }
        public DateTime? LatestDueLocal { get; private set; }
        public DateTime? LatestDueUtc { get; private set; }
        public DateTime? NextScheduledLocal { get; private set; }
        public string StatusLabel { get; private set; }
        public string Detail { get; private set; }

        public bool NeedsAttention
        {
            get
            {
                return State == BackupFreshnessState.NoVerifiedBackup ||
                    State == BackupFreshnessState.Overdue ||
                    State == BackupFreshnessState.ClockAnomaly ||
                    State == BackupFreshnessState.Unavailable;
            }
        }
    }

    internal static class BackupFreshnessEvaluator
    {
        internal static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromHours(2);
        internal static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);
        private const int MaximumCalendarLookbackDays = 15;
        private const int MaximumCalendarLookaheadDays = 15;
        private const int MaximumInvalidLocalMinutes = 180;

        public static BackupFreshnessResult Evaluate(
            TaskSchedule schedule,
            DateTime? lastVerifiedFinishedUtc,
            DateTime nowLocal,
            DateTime nowUtc)
        {
            return Evaluate(
                schedule,
                lastVerifiedFinishedUtc,
                nowLocal,
                nowUtc,
                TimeZoneInfo.Local,
                DefaultGracePeriod);
        }

        internal static BackupFreshnessResult Evaluate(
            TaskSchedule schedule,
            DateTime? lastVerifiedFinishedUtc,
            DateTime nowLocal,
            DateTime nowUtc,
            TimeZoneInfo localTimeZone,
            TimeSpan gracePeriod)
        {
            if (localTimeZone == null)
            {
                throw new ArgumentNullException("localTimeZone");
            }
            if (gracePeriod < TimeSpan.Zero || gracePeriod > TimeSpan.FromDays(7))
            {
                throw new ArgumentOutOfRangeException("gracePeriod");
            }

            DateTime localNow = DateTime.SpecifyKind(nowLocal, DateTimeKind.Unspecified);
            DateTime utcNow = NormalizeSuppliedUtc(nowUtc);
            DateTime? verifiedUtc = lastVerifiedFinishedUtc.HasValue
                ? NormalizeSuppliedUtc(lastVerifiedFinishedUtc.Value)
                : (DateTime?)null;
            DateTime? verifiedLocal = verifiedUtc.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(verifiedUtc.Value, localTimeZone)
                : (DateTime?)null;

            if (schedule == null)
            {
                return Result(
                    BackupFreshnessState.Unavailable,
                    verifiedUtc,
                    verifiedLocal,
                    null,
                    null,
                    null,
                    "Schedule unavailable",
                    "The installed backup schedule could not be verified.");
            }

            DateTime? nextScheduledLocal = FindNextScheduledLocal(schedule, localNow, localTimeZone);
            if (!schedule.Enabled || schedule.State == BackupTaskState.Disabled)
            {
                return Result(
                    BackupFreshnessState.Paused,
                    verifiedUtc,
                    verifiedLocal,
                    null,
                    null,
                    nextScheduledLocal,
                    "Paused",
                    verifiedLocal.HasValue
                        ? "Automatic backups are paused. Last verified " + FormatLocal(verifiedLocal.Value) + "."
                        : "Automatic backups are paused and no verified backup has been recorded.");
            }

            DateTime resolvedNowUtc;
            if (!TryResolveLocalToUtc(localNow, localTimeZone, true, out resolvedNowUtc) ||
                AbsoluteDifference(resolvedNowUtc, utcNow) > ClockTolerance)
            {
                return Result(
                    BackupFreshnessState.ClockAnomaly,
                    verifiedUtc,
                    verifiedLocal,
                    null,
                    null,
                    nextScheduledLocal,
                    "Clock needs attention",
                    "The supplied local and UTC clocks do not describe the same moment.");
            }

            if (verifiedUtc.HasValue && verifiedUtc.Value > utcNow.Add(ClockTolerance))
            {
                return Result(
                    BackupFreshnessState.ClockAnomaly,
                    verifiedUtc,
                    verifiedLocal,
                    null,
                    null,
                    nextScheduledLocal,
                    "Clock needs attention",
                    "The last verified backup is timestamped in the future (" +
                        FormatLocal(verifiedLocal.Value) + "). Check the Windows clock.");
            }

            DateTime? latestDueLocal;
            DateTime? latestDueUtc;
            FindLatestDueOccurrence(
                schedule,
                localNow,
                utcNow,
                localTimeZone,
                gracePeriod,
                out latestDueLocal,
                out latestDueUtc);

            if (!verifiedUtc.HasValue)
            {
                string noVerifiedDetail = "No completed, verified backup has been recorded.";
                if (nextScheduledLocal.HasValue)
                {
                    noVerifiedDetail += " Next scheduled " + FormatLocal(nextScheduledLocal.Value) + ".";
                }
                return Result(
                    BackupFreshnessState.NoVerifiedBackup,
                    null,
                    null,
                    latestDueLocal,
                    latestDueUtc,
                    nextScheduledLocal,
                    "No verified backup",
                    noVerifiedDetail);
            }

            if (latestDueUtc.HasValue &&
                verifiedUtc.Value.Add(ClockTolerance) < latestDueUtc.Value)
            {
                return Result(
                    BackupFreshnessState.Overdue,
                    verifiedUtc,
                    verifiedLocal,
                    latestDueLocal,
                    latestDueUtc,
                    nextScheduledLocal,
                    "Overdue",
                    "No verified backup covers the " + FormatLocal(latestDueLocal.Value) +
                        " scheduled run. Last verified " + FormatLocal(verifiedLocal.Value) + ".");
            }

            string healthyDetail = "Last verified " + FormatLocal(verifiedLocal.Value) + ".";
            if (nextScheduledLocal.HasValue)
            {
                healthyDetail += " Next scheduled " + FormatLocal(nextScheduledLocal.Value) + ".";
            }
            return Result(
                BackupFreshnessState.Healthy,
                verifiedUtc,
                verifiedLocal,
                latestDueLocal,
                latestDueUtc,
                nextScheduledLocal,
                "Healthy",
                healthyDetail);
        }

        private static BackupFreshnessResult Result(
            BackupFreshnessState state,
            DateTime? verifiedUtc,
            DateTime? verifiedLocal,
            DateTime? latestDueLocal,
            DateTime? latestDueUtc,
            DateTime? nextScheduledLocal,
            string statusLabel,
            string detail)
        {
            return new BackupFreshnessResult(
                state,
                verifiedUtc,
                verifiedLocal,
                latestDueLocal,
                latestDueUtc,
                nextScheduledLocal,
                statusLabel,
                detail);
        }

        private static void FindLatestDueOccurrence(
            TaskSchedule schedule,
            DateTime localNow,
            DateTime utcNow,
            TimeZoneInfo localTimeZone,
            TimeSpan gracePeriod,
            out DateTime? latestDueLocal,
            out DateTime? latestDueUtc)
        {
            latestDueLocal = null;
            latestDueUtc = null;
            for (int offset = 0; offset <= MaximumCalendarLookbackDays; offset++)
            {
                DateTime date = localNow.Date.AddDays(-offset);
                if (!RunsOnDate(schedule, date.DayOfWeek))
                {
                    continue;
                }
                DateTime scheduledLocal = DateTime.SpecifyKind(
                    date.Add(schedule.TimeOfDay),
                    DateTimeKind.Unspecified);
                DateTime scheduledUtc;
                if (!TryResolveLocalToUtc(
                    scheduledLocal,
                    localTimeZone,
                    true,
                    out scheduledUtc,
                    out scheduledLocal))
                {
                    continue;
                }
                if (scheduledUtc.Add(gracePeriod) <= utcNow)
                {
                    latestDueLocal = scheduledLocal;
                    latestDueUtc = scheduledUtc;
                    return;
                }
            }
        }

        private static DateTime? FindNextScheduledLocal(
            TaskSchedule schedule,
            DateTime localNow,
            TimeZoneInfo localTimeZone)
        {
            if (!schedule.Enabled || schedule.State == BackupTaskState.Disabled)
            {
                return null;
            }
            for (int offset = 0; offset <= MaximumCalendarLookaheadDays; offset++)
            {
                DateTime date = localNow.Date.AddDays(offset);
                if (!RunsOnDate(schedule, date.DayOfWeek))
                {
                    continue;
                }
                DateTime scheduledLocal = DateTime.SpecifyKind(
                    date.Add(schedule.TimeOfDay),
                    DateTimeKind.Unspecified);
                DateTime ignoredUtc;
                if (!TryResolveLocalToUtc(
                    scheduledLocal,
                    localTimeZone,
                    false,
                    out ignoredUtc,
                    out scheduledLocal))
                {
                    continue;
                }
                if (scheduledLocal > localNow)
                {
                    return scheduledLocal;
                }
            }
            return null;
        }

        private static bool RunsOnDate(TaskSchedule schedule, DayOfWeek day)
        {
            return schedule.Cadence == BackupScheduleCadence.Daily ||
                (schedule.Days != null && schedule.Days.Contains(day));
        }

        private static bool TryResolveLocalToUtc(
            DateTime local,
            TimeZoneInfo localTimeZone,
            bool preferLaterAmbiguousOccurrence,
            out DateTime utc)
        {
            DateTime adjusted;
            return TryResolveLocalToUtc(
                local,
                localTimeZone,
                preferLaterAmbiguousOccurrence,
                out utc,
                out adjusted);
        }

        private static bool TryResolveLocalToUtc(
            DateTime local,
            TimeZoneInfo localTimeZone,
            bool preferLaterAmbiguousOccurrence,
            out DateTime utc,
            out DateTime adjustedLocal)
        {
            adjustedLocal = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            utc = DateTime.MinValue;
            int shiftedMinutes = 0;
            while (localTimeZone.IsInvalidTime(adjustedLocal) &&
                shiftedMinutes < MaximumInvalidLocalMinutes)
            {
                adjustedLocal = adjustedLocal.AddMinutes(1);
                shiftedMinutes++;
            }
            if (localTimeZone.IsInvalidTime(adjustedLocal))
            {
                return false;
            }

            if (localTimeZone.IsAmbiguousTime(adjustedLocal))
            {
                TimeSpan[] offsets = localTimeZone.GetAmbiguousTimeOffsets(adjustedLocal);
                if (offsets == null || offsets.Length == 0)
                {
                    return false;
                }
                DateTime resolvedLocal = adjustedLocal;
                IEnumerable<DateTime> candidates = offsets.Select(
                    offset => DateTime.SpecifyKind(resolvedLocal - offset, DateTimeKind.Utc));
                utc = preferLaterAmbiguousOccurrence
                    ? candidates.Max()
                    : candidates.Min();
                return true;
            }

            utc = TimeZoneInfo.ConvertTimeToUtc(adjustedLocal, localTimeZone);
            return true;
        }

        private static DateTime NormalizeSuppliedUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static TimeSpan AbsoluteDifference(DateTime first, DateTime second)
        {
            TimeSpan difference = first - second;
            return difference < TimeSpan.Zero ? difference.Negate() : difference;
        }

        private static string FormatLocal(DateTime value)
        {
            return value.ToString("ddd, d MMM HH:mm", CultureInfo.CurrentCulture);
        }
    }
}
