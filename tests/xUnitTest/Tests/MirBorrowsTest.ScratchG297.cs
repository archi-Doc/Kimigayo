// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// TEMP (bu83 probe, never committed): prints the G297 rows' records.
public sealed partial class MirBorrowsTest
{
    [Fact]
    public void ScratchG297()
    {
        foreach (var row in new[] { "rawArg", "rawReq" })
        {
            output.WriteLine($"ROW {row}: {string.Join(" | ", WkRecords(row))}");
        }
    }
}
