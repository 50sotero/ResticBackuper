using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ResticBackuper.TaskLauncher
{
    internal static class Program
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateNoWindow = 0x08000000;
        private const uint Infinite = 0xFFFFFFFF;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectExtendedLimitInformationClass = 9;
        private const uint StillActive = 259;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0)
            {
                return 64;
            }

            string runtime = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string expectedRuntime = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ResticBackuper");
            if (!string.Equals(runtime, expectedRuntime, StringComparison.OrdinalIgnoreCase))
            {
                return 65;
            }

            string python = Path.Combine(runtime, "Python", "python.exe");
            string backup = Path.Combine(runtime, "backup.py");
            string config = Path.Combine(runtime, "backup-config.json");
            if (!File.Exists(python) || !File.Exists(backup) || !File.Exists(config))
            {
                return 66;
            }

            IntPtr job = IntPtr.Zero;
            ProcessInformation process = new ProcessInformation();
            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    return 67;
                }

                JobObjectExtendedLimitInformation information =
                    new JobObjectExtendedLimitInformation();
                information.BasicLimitInformation.LimitFlags =
                    JobObjectLimitKillOnJobClose;
                int informationLength = Marshal.SizeOf(information);
                IntPtr informationPointer = Marshal.AllocHGlobal(informationLength);
                try
                {
                    Marshal.StructureToPtr(information, informationPointer, false);
                    if (!SetInformationJobObject(
                        job,
                        JobObjectExtendedLimitInformationClass,
                        informationPointer,
                        (uint)informationLength))
                    {
                        return 68;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(informationPointer);
                }

                string commandLine = Quote(python)
                    + " -I -S -B -u " + Quote(backup)
                    + " --config " + Quote(config)
                    + " --scheduled";
                StartupInfo startup = new StartupInfo();
                startup.Size = (uint)Marshal.SizeOf(startup);
                StringBuilder mutableCommandLine = new StringBuilder(commandLine);
                if (!CreateProcess(
                    python,
                    mutableCommandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateNoWindow | CreateSuspended,
                    IntPtr.Zero,
                    runtime,
                    ref startup,
                    out process))
                {
                    return 69;
                }

                if (!AssignProcessToJobObject(job, process.ProcessHandle))
                {
                    TerminateAndWait(process.ProcessHandle, 70);
                    return 70;
                }
                if (ResumeThread(process.ThreadHandle) == uint.MaxValue)
                {
                    TerminateAndWait(process.ProcessHandle, 71);
                    return 71;
                }

                CloseHandle(process.ThreadHandle);
                process.ThreadHandle = IntPtr.Zero;
                if (WaitForSingleObject(process.ProcessHandle, Infinite) == uint.MaxValue)
                {
                    TerminateAndWait(process.ProcessHandle, 72);
                    return 72;
                }

                uint exitCode;
                if (!GetExitCodeProcess(process.ProcessHandle, out exitCode)
                    || exitCode == StillActive)
                {
                    TerminateAndWait(process.ProcessHandle, 73);
                    return 73;
                }
                return unchecked((int)exitCode);
            }
            catch (Win32Exception)
            {
                return 74;
            }
            catch (IOException)
            {
                return 75;
            }
            catch (UnauthorizedAccessException)
            {
                return 76;
            }
            finally
            {
                if (process.ThreadHandle != IntPtr.Zero)
                {
                    CloseHandle(process.ThreadHandle);
                }
                if (process.ProcessHandle != IntPtr.Zero)
                {
                    CloseHandle(process.ProcessHandle);
                }
                if (job != IntPtr.Zero)
                {
                    // Closing this non-inherited handle kills any descendant that
                    // survived its direct Python supervisor.
                    CloseHandle(job);
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TerminateAndWait(IntPtr process, uint exitCode)
        {
            if (process == IntPtr.Zero)
            {
                return;
            }
            TerminateProcess(process, exitCode);
            WaitForSingleObject(process, 10000);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public uint Size;
            public IntPtr Reserved;
            public IntPtr Desktop;
            public IntPtr Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort Reserved2;
            public IntPtr Reserved2Pointer;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr ProcessHandle;
            public IntPtr ThreadHandle;
            public uint ProcessId;
            public uint ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            IntPtr information,
            uint informationLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
