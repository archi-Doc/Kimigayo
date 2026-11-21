// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The contract of JobProcess (PLAN G1188), Kimi's launcher for the programs it runs: a child whose tree ends with its job. It is in
// Misc, which Build and Testing both read (neither reads the other); Benchmark's A1b launcher and the backend's VerificationJob keep
// their own contracts.
// Owns: a child created inside a job of its own before its first instruction (PROC_THREAD_ATTRIBUTE_JOB_LIST), so every descendant is
// in the job and none can leave it (no breakaway); the job ends them all at Kill and when its last handle closes
// (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE): at Dispose, and when this process ends.
// Readers: Testing's TestProcess (a test case, IMPL 20.6.3), which checks its host (Windows x64) before the start.
// Input: the executable, its working directory, its environment block, its standard input and the handles it inherits besides (each
// inheritable, the input included).
// Output: the child's standard output and error, each a pipe this process reads; its exit and exit code; whether the job's processes
// have all ended (IsRecovered).
// Invariants: the child inherits exactly its standard handles and the listed ones (PROC_THREAD_ATTRIBUTE_HANDLE_LIST), so concurrent
// launches never hold each other's pipes; it has a hidden console (CREATE_NO_WINDOW); an unhandled exception in the job ends its process
// without the error dialog (JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION); a failed start ends the job, so nothing it started runs on.
// Must not: decide what the child's streams carry or when its tree ends; its readers do.

using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Kimi;

/// <summary>A child process created inside its own kill-on-close job, which every descendant joins.</summary>
internal unsafe class JobProcess : IDisposable
{
    private readonly SafeFileHandle job;
    private readonly SafeFileHandle process;

    /// <summary>Initializes a new instance of the <see cref="JobProcess"/> class: starts the child.</summary>
    /// <param name="executable">The executable's full path.</param>
    /// <param name="directory">The working directory.</param>
    /// <param name="environment">The environment block.</param>
    /// <param name="input">The child's standard input, an inheritable handle.</param>
    /// <param name="inherited">The inheritable handles the child inherits besides its standard ones.</param>
    internal JobProcess(string executable, string directory, string environment, SafeHandle input, params ReadOnlySpan<nint> inherited)
    {
        this.job = new SafeFileHandle(CreateJobObjectW(0, null), true);
        SafeFileHandle? child = null;
        Stream? stdout = null;
        Stream? stderr = null;
        nint attributes = 0;
        var initializedAttributes = false;
        try
        {
            if (this.job.IsInvalid)
            {
                throw Error();
            }

            var limits = new JobLimits { Flags = 0x2400 }; // KILL_ON_JOB_CLOSE | DIE_ON_UNHANDLED_EXCEPTION
            if (!SetInformationJobObject(this.job.DangerousGetHandle(), 9, &limits, (uint)sizeof(JobLimits)))
            {
                throw Error();
            }

            using var outWrite = Pipe(out var outRead);
            stdout = outRead;
            using var errorWrite = Pipe(out var errorRead);
            stderr = errorRead;
            nuint bytes = 0;
            _ = InitializeProcThreadAttributeList(0, 2, 0, ref bytes);
            attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref bytes))
            {
                throw Error();
            }

            initializedAttributes = true;

            var count = 3 + inherited.Length;
            var handles = stackalloc nint[count];
            handles[0] = outWrite.SafePipeHandle.DangerousGetHandle();
            handles[1] = errorWrite.SafePipeHandle.DangerousGetHandle();
            handles[2] = input.DangerousGetHandle();
            inherited.CopyTo(new Span<nint>(handles + 3, inherited.Length));
            var jobHandle = this.job.DangerousGetHandle();
            if (!UpdateProcThreadAttribute(attributes, 0, 0x20002, handles, (nuint)(count * sizeof(nint)), null, null) ||
                !UpdateProcThreadAttribute(attributes, 0, 0x2000D, &jobHandle, (nuint)sizeof(nint), null, null))
            {
                throw Error();
            }

            var startup = new StartupInfoEx
            {
                Size = (uint)sizeof(StartupInfoEx),
                Flags = 0x100, // STARTF_USESTDHANDLES
                Input = handles[2],
                Output = handles[0],
                Error = handles[1],
                Attributes = attributes,
            };
            // The flags are EXTENDED_STARTUPINFO_PRESENT, CREATE_UNICODE_ENVIRONMENT and CREATE_NO_WINDOW.
            fixed (char* env = environment)
            {
                if (!CreateProcessW(executable, null, 0, 0, true, 0x80400 | 0x08000000, env, directory, &startup, out var info))
                {
                    throw Error();
                }

                child = new(info.Process, true);
                using var thread = new SafeFileHandle(info.Thread, true);
            }

            this.process = child;
            this.Stdout = stdout;
            this.Stderr = stderr;
        }
        catch
        {
            if (!this.job.IsInvalid)
            {
                _ = TerminateJobObject(this.job.DangerousGetHandle(), 125);
            }

            child?.Dispose();
            this.job.Dispose();
            stdout?.Dispose();
            stderr?.Dispose();
            throw;
        }
        finally
        {
            if (attributes != 0)
            {
                if (initializedAttributes)
                {
                    DeleteProcThreadAttributeList(attributes);
                }

                Marshal.FreeHGlobal(attributes);
            }
        }
    }

    internal Stream Stdout { get; }

    internal Stream Stderr { get; }

    internal uint ExitCode
    {
        get
        {
            if (!GetExitCodeProcess(this.process.DangerousGetHandle(), out var code))
            {
                throw Error();
            }

            return code;
        }
    }

    // Whether every process of the job has ended.
    internal bool IsRecovered
    {
        get
        {
            JobAccounting accounting = default;
            if (!QueryInformationJobObject(this.job.DangerousGetHandle(), 1, &accounting, (uint)sizeof(JobAccounting), null))
            {
                throw Error();
            }

            return accounting.Active == 0;
        }
    }

    public virtual void Dispose()
    {
        this.job.Dispose();
        this.process.Dispose();
        this.Stdout.Dispose();
        this.Stderr.Dispose();
    }

    // Whether the child exited within the time.
    internal bool Wait(int milliseconds) => WaitForSingleObject(this.process.DangerousGetHandle(), (uint)milliseconds) switch
    {
        0 => true,
        258 => false,
        _ => throw Error(),
    };

    // Ends every process of the job (exit code 125); those already ended keep theirs.
    internal void Kill()
    {
        if (!TerminateJobObject(this.job.DangerousGetHandle(), 125))
        {
            throw Error();
        }
    }

    private protected static Win32Exception Error() => new(Marshal.GetLastPInvokeError());

    // A pipe: the read end this process reads asynchronously, the write end, inheritable, the child's.
    private protected static NamedPipeClientStream Pipe(out NamedPipeServerStream read)
    {
        var name = "kimi-test-" + Guid.NewGuid().ToString("N");
        read = new(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
        var write = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.Identification, HandleInheritability.Inheritable);
        try
        {
            write.Connect(1000);
            read.WaitForConnection();
            return write;
        }
        catch
        {
            read.Dispose();
            write.Dispose();
            throw;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct JobLimits
    {
        [FieldOffset(16)]
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct JobAccounting
    {
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint security, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int kind, void* info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(nint job, int kind, void* info, uint size, uint* returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(nint job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, void* value, nuint size, void* previous, void* returned);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, string? command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, char* environment, string directory, StartupInfoEx* startup, out ProcessInfo process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);
}
