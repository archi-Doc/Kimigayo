// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Kimi.Testing;

/// <summary>Creates a child inside its non-breakaway job before its first instruction.</summary>
internal sealed unsafe class TestProcess : IDisposable
{
    private readonly SafeFileHandle job;
    private readonly SafeFileHandle process;

    private TestProcess(SafeFileHandle job, SafeFileHandle process, Stream result, Stream stdout, Stream stderr)
    {
        this.job = job;
        this.process = process;
        this.Result = result;
        this.Stdout = stdout;
        this.Stderr = stderr;
    }

    public void Dispose()
    {
        this.job.Dispose();
        this.process.Dispose();
        this.Result.Dispose();
        this.Stdout.Dispose();
        this.Stderr.Dispose();
    }

    internal Stream Result { get; }

    internal Stream Stdout { get; }

    internal Stream Stderr { get; }

    internal static TestProcess Start(string executable, string directory, SortedDictionary<string, string> environment)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Test execution requires Windows x64.");
        }

        var job = new SafeFileHandle(CreateJobObjectW(0, null), true);
        SafeFileHandle? process = null;
        Stream? result = null;
        Stream? stdout = null;
        Stream? stderr = null;
        nint attributes = 0;
        var initializedAttributes = false;
        try
        {
            if (job.IsInvalid)
            {
                throw Error();
            }

            var limits = new JobLimits { Flags = 0x2400 };
            if (!SetInformationJobObject(job.DangerousGetHandle(), 9, &limits, (uint)sizeof(JobLimits)))
            {
                throw Error();
            }

            using var resultWrite = Pipe(out var resultRead);
            result = resultRead;
            using var outWrite = Pipe(out var outRead);
            stdout = outRead;
            using var errorWrite = Pipe(out var errorRead);
            stderr = errorRead;
            using var inputWrite = InputPipe(out var inputRead);
            using var stdin = inputRead;
            // The input read end, unlike output read ends, belongs to the child.
            if (!SetHandleInformation(stdin.SafeFileHandle.DangerousGetHandle(), 1, 1))
            {
                throw Error();
            }

            environment["KIMI_TEST_PIPE"] = ((ulong)resultWrite.SafePipeHandle.DangerousGetHandle()).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var block = new StringBuilder();
            foreach (var (name, value) in environment)
            {
                block.Append(name).Append('=').Append(value).Append('\0');
            }

            block.Append('\0');
            var environmentBlock = block.ToString();
            nuint bytes = 0;
            _ = InitializeProcThreadAttributeList(0, 2, 0, ref bytes);
            attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref bytes))
            {
                throw Error();
            }

            initializedAttributes = true;

            nint* handles = stackalloc nint[4] { resultWrite.SafePipeHandle.DangerousGetHandle(), outWrite.SafePipeHandle.DangerousGetHandle(), errorWrite.SafePipeHandle.DangerousGetHandle(), stdin.SafeFileHandle.DangerousGetHandle() };
            var jobHandle = job.DangerousGetHandle();
            if (!UpdateProcThreadAttribute(attributes, 0, 0x20002, handles, (nuint)(4 * sizeof(nint)), null, null) ||
                !UpdateProcThreadAttribute(attributes, 0, 0x2000D, &jobHandle, (nuint)sizeof(nint), null, null))
            {
                throw Error();
            }

            var startup = new StartupInfoEx
            {
                Size = (uint)sizeof(StartupInfoEx),
                Flags = 0x100,
                Input = handles[3],
                Output = handles[1],
                Error = handles[2],
                Attributes = attributes,
            };
            fixed (char* env = environmentBlock)
            {
                if (!CreateProcessW(executable, null, 0, 0, true, 0x80400 | 0x08000000, env, directory, &startup, out var info))
                {
                    throw Error();
                }

                process = new(info.Process, true);
                using var thread = new SafeFileHandle(info.Thread, true);
            }

            return new(job, process, result, stdout, stderr);
        }
        catch
        {
            if (!job.IsInvalid)
            {
                _ = TerminateJobObject(job.DangerousGetHandle(), 125);
            }

            process?.Dispose();
            job.Dispose();
            result?.Dispose();
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

    internal bool Wait(int milliseconds) => WaitForSingleObject(this.process.DangerousGetHandle(), (uint)milliseconds) switch
    {
        0 => true,
        258 => false,
        _ => throw Error(),
    };

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

    internal void Kill()
    {
        if (!TerminateJobObject(this.job.DangerousGetHandle(), 125))
        {
            throw Error();
        }
    }

    private static Win32Exception Error() => new(Marshal.GetLastPInvokeError());

    private static NamedPipeClientStream Pipe(out NamedPipeServerStream read)
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

    private static SafeFileHandle InputPipe(out FileStream read)
    {
        var security = new SecurityAttributes { Size = (uint)sizeof(SecurityAttributes), Inherit = 1 };
        if (!CreatePipe(out var reader, out var writer, &security, 65536))
        {
            throw Error();
        }

        var readHandle = new SafeFileHandle(reader, true);
        var writeHandle = new SafeFileHandle(writer, true);
        try
        {
            if (!SetHandleInformation(reader, 1, 0))
            {
                throw Error();
            }

            read = new FileStream(readHandle, FileAccess.Read, 4096, false);
            return writeHandle;
        }
        catch
        {
            readHandle.Dispose();
            writeHandle.Dispose();
            throw;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal uint Size;
        internal nint Descriptor;
        internal int Inherit;
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
    private static extern bool CreatePipe(out nint read, out nint write, SecurityAttributes* security, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(nint handle, uint mask, uint flags);

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
