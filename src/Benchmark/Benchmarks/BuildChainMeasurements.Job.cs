// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Benchmark;

/// <summary>The series' own job launcher (design C7): a root process created inside a job object from its first instruction.</summary>
internal static partial class BuildChainMeasurements
{
    // Starts `dotnet <arguments>` in a new job (PROC_THREAD_ATTRIBUTE_JOB_LIST, as TestProcess.Start), stdout and stderr to the log,
    // and reads the job's accounting after the root and every process it started have exited.
    private static unsafe Round Measure(string dotnet, string[] arguments, string directory, string log)
    {
        using var job = new SafeFileHandle(CreateJobObjectW(0, null), true);
        using var output = File.OpenHandle(log, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var input = File.OpenHandle("NUL", FileMode.Open, FileAccess.Read);
        var limits = new ExtendedLimits { Flags = 0x2000 }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (job.IsInvalid || !SetInformationJobObject(job.DangerousGetHandle(), 9, &limits, (uint)sizeof(ExtendedLimits)) ||
            !SetHandleInformation(output.DangerousGetHandle(), 1, 1) || !SetHandleInformation(input.DangerousGetHandle(), 1, 1))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        nuint bytes = 0;
        _ = InitializeProcThreadAttributeList(0, 2, 0, ref bytes);
        var attributes = Marshal.AllocHGlobal(checked((nint)bytes));
        if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref bytes))
        {
            Marshal.FreeHGlobal(attributes);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            nint* handles = stackalloc nint[2] { input.DangerousGetHandle(), output.DangerousGetHandle() };
            var jobHandle = job.DangerousGetHandle();
            if (!UpdateProcThreadAttribute(attributes, 0, 0x20002, handles, (nuint)(2 * sizeof(nint)), null, null) ||
                !UpdateProcThreadAttribute(attributes, 0, 0x2000D, &jobHandle, (nuint)sizeof(nint), null, null))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var startup = new StartupInfoEx { Size = (uint)sizeof(StartupInfoEx), Flags = 0x100, Input = handles[0], Output = handles[1], Error = handles[1], Attributes = attributes };
            var commandLine = string.Join(' ', arguments.Prepend(dotnet).Select(a => $"\"{a}\"")) + "\0";
            var watch = Stopwatch.StartNew();
            ProcessInfo info;
            fixed (char* line = commandLine)
            {
                // EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW; the environment is inherited.
                if (!CreateProcessW(dotnet, line, 0, 0, true, 0x80000 | 0x08000000, null, directory, &startup, out info))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }

            using var process = new SafeFileHandle(info.Process, true);
            using var thread = new SafeFileHandle(info.Thread, true);
            if (WaitForSingleObject(info.Process, 600_000) != 0)
            {
                throw new TimeoutException($"No exit within 10 minutes: {commandLine}");
            }

            var wall = watch.Elapsed.TotalMilliseconds;
            Accounting accounting = default;
            for (var waited = Stopwatch.StartNew(); Query(job, &accounting) && accounting.Active != 0; Thread.Sleep(1))
            {
                if (waited.ElapsedMilliseconds > 10_000)
                {
                    throw new TimeoutException($"Processes still active in the job 10 s after the root's exit: {commandLine}");
                }
            }

            if (!QueryInformationJobObject(job.DangerousGetHandle(), 9, &limits, (uint)sizeof(ExtendedLimits), null) || !GetExitCodeProcess(info.Process, out var exitCode))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return new(wall, accounting.User / 10_000.0, accounting.Kernel / 10_000.0, limits.PeakJobMemoryUsed, accounting.TotalProcesses, exitCode);
        }
        finally
        {
            DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);
        }
    }

    private static unsafe bool Query(SafeFileHandle job, Accounting* accounting) =>
        QueryInformationJobObject(job.DangerousGetHandle(), 1, accounting, (uint)sizeof(Accounting), null) ? true : throw new Win32Exception(Marshal.GetLastPInvokeError());

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint security, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool SetInformationJobObject(nint job, int kind, void* info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool QueryInformationJobObject(nint job, int kind, void* info, uint size, uint* returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(nint handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, void* value, nuint size, void* previous, void* returned);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool CreateProcessW(string application, char* command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, char* environment, string directory, StartupInfoEx* startup, out ProcessInfo process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    /// <summary>One run: wall time from the root's creation to its exit; the job's CPU time of every process it held, its peak
    /// committed memory and the number of processes it held.</summary>
    private readonly record struct Round(double WallMilliseconds, double UserMilliseconds, double KernelMilliseconds, ulong PeakCommitBytes, uint TotalProcesses, uint ExitCode);

    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION.
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct ExtendedLimits
    {
        [FieldOffset(16)]
        internal uint Flags;

        [FieldOffset(136)]
        internal ulong PeakJobMemoryUsed;
    }

    // JOBOBJECT_BASIC_ACCOUNTING_INFORMATION; times in 100 ns units.
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct Accounting
    {
        [FieldOffset(0)]
        internal long User;

        [FieldOffset(8)]
        internal long Kernel;

        [FieldOffset(36)]
        internal uint TotalProcesses;

        [FieldOffset(40)]
        internal uint Active;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        internal nint Process;
        internal nint Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        internal uint Size;
        internal nint Reserved;
        internal nint Desktop;
        internal nint Title;
        internal uint X;
        internal uint Y;
        internal uint XSize;
        internal uint YSize;
        internal uint XChars;
        internal uint YChars;
        internal uint Fill;
        internal uint Flags;
        internal ushort Show;
        internal ushort ReservedSize;
        internal nint ReservedData;
        internal nint Input;
        internal nint Output;
        internal nint Error;
        internal nint Attributes;
    }
}
