// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Kimi.Testing;

/// <summary>A test case's process (IMPL 20.6.3): a <see cref="JobProcess"/> with the case's environment, a closed standard input and the
/// result channel, a pipe the child finds by its handle in KIMI_TEST_PIPE.</summary>
internal sealed unsafe class TestProcess : JobProcess
{
    private TestProcess(string executable, string directory, string environment, SafeHandle input, nint channel, Stream result)
        : base(executable, directory, environment, input, channel)
        => this.Result = result;

    internal Stream Result { get; }

    public override void Dispose()
    {
        base.Dispose();
        this.Result.Dispose();
    }

    internal static TestProcess Start(string executable, string directory, SortedDictionary<string, string> environment)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Test execution requires Windows x64.");
        }

        using var resultWrite = Pipe(out var result);
        try
        {
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
            return new(executable, directory, block.ToString(), stdin.SafeFileHandle, resultWrite.SafePipeHandle.DangerousGetHandle(), result);
        }
        catch
        {
            result.Dispose();
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out nint read, out nint write, SecurityAttributes* security, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(nint handle, uint mask, uint flags);
}
