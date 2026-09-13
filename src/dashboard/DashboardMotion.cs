namespace ResticBackuper.Dashboard
{
    // Keeps the web presentation on the same accessibility and system-motion policy
    // as the existing WPF surface.
    internal static class DashboardMotion
    {
        internal static bool MotionAllowed()
        {
            return DashboardVisualStyle.MotionAllowed();
        }
    }
}
