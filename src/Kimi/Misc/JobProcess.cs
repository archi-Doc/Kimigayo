// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The contract of JobProcess (PLAN G1188), Kimi's launcher for the programs it runs: a child whose tree ends with its job. It is in
// Misc, which Build and Testing both read (neither reads the other); Benchmark's A1b launcher and the backend's VerificationJob keep
// their own contracts.
// Owns: a child created inside a job of its own before its first instruction (PROC_THREAD_ATTRIBUTE_JOB_LIST), so every descendant is
// in the job and none can leave it (no breakaway); the job ends them all at Kill and when its last handle closes
// (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE): at Dispose, and when this process ends, by TerminateProcess too (a second Ctrl+C). The host
// rule of the programs Kimi runs: a Windows x64 host, as WindowsProfile's target (IMPL 20.3, 20.6.1).
// Readers: Testing's TestProcess (a test case, IMPL 20.6.3) and Build's NativeToolchain.RunExecutable (run's program, IMPL 20.3).
// Input: the executable, its working directory, its environment block (null: this process's), its standard input (an open handle of
// this process, of which the child gets an inheritable duplicate, or null: none, whether a reader has one being the reader's decision)
// and the handles it inherits besides (each inheritable). A child with no input gets a closed one, the one form of "no input": the read
// end of an anonymous pipe whose write end closes before the start, so it reads end of input at once, never its hidden console's input.
// Output: the child's standard output and error, each a pipe this process reads; its exit and exit code; whether the job's processes
// have all ended (IsRecovered). A failure is an exception naming the failed call, and for a start the executable, beside the system's
// text: PlatformNotSupportedException ("Running a program requires Windows x64.") off a Windows x64 host, else Win32Exception.
// Invariants: the child inherits exactly its standard handles and the listed ones (PROC_THREAD_ATTRIBUTE_HANDLE_LIST), so concurrent
// launches never hold each other's pipes; it has a hidden console (CREATE_NO_WINDOW) and its quoted path as its command line; an
// unhandled exception in the job ends its process without the error dialog (JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION); a failed
// start ends the job, so nothing it started runs on.
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
    /// <param name="environment">The environment block, or null for this process's environment.</param>
    /// <param name="input">The child's standard input, an open handle of this process, or null for none (a closed input).</param>
    /// <param name="inherited">The inheritable handles the child inherits besides its standard ones.</param>
    internal JobProcess(string executable, string directory, string? environment, SafeHandle? input, params ReadOnlySpan<nint> inherited)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Running a program requires Windows x64.");
        }

        this.job = new SafeFileHandle(CreateJobObjectW(0, null), ownsHandle: true);
        SafeFileHandle? child = null;
        Stream? stdout = null;
        Stream? stderr = null;
        nint attributes = 0;
        var initializedAttributes = false;
        try
        {
            if (this.job.IsInvalid)
            {
                throw StartError(executable, nameof(CreateJobObjectW));
            }

            var limits = new JobLimits { Flags = 0x2400 }; // KILL_ON_JOB_CLOSE | DIE_ON_UNHANDLED_EXCEPTION
            if (!SetInformationJobObject(this.job.DangerousGetHandle(), 9, &limits, (uint)sizeof(JobLimits)))
            {
                throw StartError(executable, nameof(SetInformationJobObject));
            }

            using var outWrite = Pipe(out var outRead);
            stdout = outRead;
            using var errorWrite = Pipe(out var errorRead);
            stderr = errorRead;
            using var closed = input is null ? ClosedInput() : null;
            using var inputCopy = Inheritable(input ?? closed!, executable);
            nuint bytes = 0;
            _ = InitializeProcThreadAttributeList(0, 2, 0, ref bytes);
            attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref bytes))
            {
                throw StartError(executable, nameof(InitializeProcThreadAttributeList));
            }

            initializedAttributes = true;

            var handles = stackalloc nint[3 + inherited.Length];
            var count = 0;
            handles[count++] = outWrite.SafePipeHandle.DangerousGetHandle();
            handles[count++] = errorWrite.SafePipeHandle.DangerousGetHandle();
            handles[count++] = inputCopy.DangerousGetHandle();
            foreach (var handle in inherited)
            {
                handles[count++] = handle;
            }

            var jobHandle = this.job.DangerousGetHandle();
            if (!UpdateProcThreadAttribute(attributes, 0, 0x20002, handles, (nuint)(count * sizeof(nint)), null, null))
            {
                throw StartError(executable, "UpdateProcThreadAttribute(HANDLE_LIST)");
            }

            if (!UpdateProcThreadAttribute(attributes, 0, 0x2000D, &jobHandle, (nuint)sizeof(nint), null, null))
            {
                throw StartError(executable, "UpdateProcThreadAttribute(JOB_LIST)");
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
            // The command line is the quoted path, writable as CreateProcessW requires; the flags are EXTENDED_STARTUPINFO_PRESENT,
            // CREATE_UNICODE_ENVIRONMENT and CREATE_NO_WINDOW.
            var commandLine = ("\"" + executable + "\"\0").ToCharArray();
            fixed (char* line = commandLine)
            {
                fixed (char* env = environment)
                {
                    if (!CreateProcessW(executable, line, 0, 0, inherit: true, 0x80400 | 0x08000000, env, directory, &startup, out var info))
                    {
                        throw StartError(executable, nameof(CreateProcessW));
                    }

                    child = new(info.Process, ownsHandle: true);
                    using var thread = new SafeFileHandle(info.Thread, ownsHandle: true);
                }
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
                throw Error(nameof(GetExitCodeProcess));
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
                throw Error(nameof(QueryInformationJobObject));
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

    // Whether the child exited within the time (Timeout.Infinite: waits for its exit).
    internal bool Wait(int milliseconds) => WaitForSingleObject(this.process.DangerousGetHandle(), (uint)milliseconds) switch
    {
        0 => true,
        258 => false,
        _ => throw Error(nameof(WaitForSingleObject)),
    };

    // Ends every process of the job (exit code 125); those already ended keep theirs.
    internal void Kill()
    {
        if (!TerminateJobObject(this.job.DangerousGetHandle(), 125))
        {
            throw Error(nameof(TerminateJobObject));
        }
    }

    // A pipe: the read end this process reads asynchronously, the write end, inheritable, the child's.
    private protected static NamedPipeClientStream Pipe(out NamedPipeServerStream read)
    {
        var name = "kimi-child-" + Guid.NewGuid().ToString("N");
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

    // A failed call's exception: the call (and, by StartError, the executable it was starting) beside the system's text.
    private static Win32Exception Error(string call)
    {
        var code = Marshal.GetLastPInvokeError();
        return new(code, $"{call} failed: {new Win32Exception(code).Message}");
    }

    private static Win32Exception StartError(string executable, string call) => Error($"Cannot start {executable}: {call}");

    // A closed input: the read end of an anonymous pipe whose write end is closed. The read end is exposed before the server's
    // disposal (which closes the write end), so it survives it.
    private static SafePipeHandle ClosedInput()
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        return pipe.ClientSafePipeHandle;
    }

    // The child's input: an inheritable duplicate of the handle, owned by the caller, with the handle's access (DUPLICATE_SAME_ACCESS);
    // a handle that is no open handle of this process fails the start.
    private static SafeFileHandle Inheritable(SafeHandle handle, string executable)
    {
        if (handle.IsInvalid)
        {
            throw new ArgumentException($"Cannot start {executable}: its standard input is no handle.", nameof(handle));
        }

        var current = GetCurrentProcess();
        if (!DuplicateHandle(current, handle.DangerousGetHandle(), current, out var copy, 0, inherit: true, 2))
        {
            throw StartError(executable, nameof(DuplicateHandle));
        }

        return new SafeFileHandle(copy, ownsHandle: true);
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

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(nint sourceProcess, nint source, nint targetProcess, out nint target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

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
    private static extern bool CreateProcessW(string application, char* command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, char* environment, string directory, StartupInfoEx* startup, out ProcessInfo process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint process, out uint exitCode);
}
