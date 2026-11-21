// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Testing;

/// <summary>A test case's process (IMPL 20.6.3): a <see cref="JobProcess"/> with the case's environment, no standard input (JobProcess's
/// closed input) and the result channel, a pipe the child finds by its handle in KIMI_TEST_PIPE.</summary>
internal sealed class TestProcess : JobProcess
{
    private TestProcess(string executable, string directory, string environment, nint channel, Stream result)
        : base(executable, directory, environment, null, channel)
        => this.Result = result;

    internal Stream Result { get; }

    public override void Dispose()
    {
        base.Dispose();
        this.Result.Dispose();
    }

    internal static TestProcess Start(string executable, string directory, SortedDictionary<string, string> environment)
    {
        using var channel = Pipe(out var result);
        try
        {
            var handle = channel.SafePipeHandle.DangerousGetHandle();
            environment["KIMI_TEST_PIPE"] = ((ulong)handle).ToString(CultureInfo.InvariantCulture);
            var block = new StringBuilder();
            foreach (var (name, value) in environment)
            {
                block.Append(name).Append('=').Append(value).Append('\0');
            }

            return new(executable, directory, block.Append('\0').ToString(), handle, result);
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
