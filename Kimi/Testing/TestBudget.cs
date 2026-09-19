// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Testing;

internal sealed class TestBudget(TestOptions options)
{
    private int diagnostics = options.Number("--run-diagnostic-count", 100000);
    private int bytes = options.Number("--run-diagnostic-bytes", 67108864);
    private int logs = options.Number("--run-log-bytes", 268435456);

    internal bool Detail(int length)
    {
        lock (this)
        {
            if (this.diagnostics == 0 || length > this.bytes)
            {
                return false;
            }

            this.diagnostics--;
            this.bytes -= length;
            return true;
        }
    }

    internal int Bytes(int wanted, bool log)
    {
        lock (this)
        {
            ref var budget = ref log ? ref this.logs : ref this.bytes;
            var take = Math.Min(wanted, budget);
            budget -= take;
            return take;
        }
    }
}
