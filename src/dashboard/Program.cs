using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ResticBackuper.Dashboard
{
    internal static class Program
    {
        private const string MutexName = @"Local\ResticBackuperDashboard.Instance";
        private const string ShowEventName = @"Local\ResticBackuperDashboard.Show";
        private static Mutex instanceMutex;
        private static EventWaitHandle showEvent;

        [STAThread]
        private static int Main(string[] args)
        {
            AppOptions options;
            try
            {
                options = AppOptions.Parse(args);
            }
            catch (ArgumentException error)
            {
                Console.Error.WriteLine(error.Message);
                return 2;
            }

            if (options.SelfTest)
            {
                try
                {
                    TelemetryReader reader = new TelemetryReader(options.StateDirectory, false);
                    string selfTestJson = reader.SelfTestJson();
                    if (!string.IsNullOrEmpty(options.SelfTestOutput))
                    {
                        File.WriteAllText(options.SelfTestOutput, selfTestJson);
                    }
                    else
                    {
                        Console.WriteLine(selfTestJson);
                    }
                    return 0;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(error.ToString());
                    return 1;
                }
            }

            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show(
                    "This read-only dashboard intentionally runs without administrator privileges. " +
                    "Please launch it normally instead of using Run as administrator.",
                    "ResticBackuper Dashboard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return 3;
            }

            bool createdNew;
            instanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                try
                {
                    using (EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        existing.Set();
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }
                return 0;
            }

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            application.DispatcherUnhandledException += OnDispatcherUnhandledException;

            DashboardWindow window = new DashboardWindow(options, showEvent);
            application.Run(window);

            showEvent.Dispose();
            instanceMutex.ReleaseMutex();
            instanceMutex.Dispose();
            return 0;
        }

        private static void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs args)
        {
            MessageBox.Show(
                "The backup dashboard hit an unexpected display error.\n\n" + args.Exception.Message,
                "ResticBackuper Dashboard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        }
    }

    public sealed class AppOptions
    {
        public string StateDirectory { get; private set; }
        public bool StartMinimized { get; private set; }
        public bool Preview { get; private set; }
        public bool SelfTest { get; private set; }
        public string SelfTestOutput { get; private set; }

        private AppOptions()
        {
            StateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ResticBackuper");
        }

        public static AppOptions Parse(string[] args)
        {
            AppOptions result = new AppOptions();
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase))
                {
                    result.StartMinimized = true;
                }
                else if (string.Equals(argument, "--preview", StringComparison.OrdinalIgnoreCase))
                {
                    result.Preview = true;
                }
                else if (string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    result.SelfTest = true;
                }
                else if (string.Equals(argument, "--self-test-output", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--self-test-output requires an absolute file path");
                    }
                    string path = args[++index];
                    if (!Path.IsPathRooted(path))
                    {
                        throw new ArgumentException("--self-test-output must be absolute");
                    }
                    result.SelfTestOutput = Path.GetFullPath(path);
                }
                else if (string.Equals(argument, "--state-dir", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--state-dir requires an absolute directory path");
                    }
                    string path = args[++index];
                    if (!Path.IsPathRooted(path))
                    {
                        throw new ArgumentException("--state-dir must be absolute");
                    }
                    result.StateDirectory = Path.GetFullPath(path);
                }
                else
                {
                    throw new ArgumentException("Unknown dashboard argument: " + argument);
                }
            }
            return result;
        }
    }
}
