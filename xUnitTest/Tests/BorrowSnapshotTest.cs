// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using Xunit;

namespace XunitTest;

public class BorrowSnapshotTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(8, false)]
    [InlineData(16, false)]
    [InlineData(8, true)]
    public void ManyLivePartLoansRetainIndependentInitialization(int count, bool checking)
    {
        var source = new StringBuilder("struct Counter\n    public var value: i32 = 1\nfunc relay(p: uniq/Counter) -> uniq{p}/Counter => p\nfunc check()\n");
        if (checking)
        {
            source.Append("    return\n");
        }

        for (var i = 0; i < count; i++)
        {
            source.Append("    var p").Append(i).Append(" = (Counter.init(), Counter.init())\n    let a").Append(i).Append(" = relay(p").Append(i).Append(".0@uniq)\n    let m").Append(i).Append(" = p").Append(i).Append(".1\n");
        }

        for (var i = 0; i < count; i++)
        {
            source.Append("    a").Append(i).Append(".value += m").Append(i).Append(".value\n    require p").Append(i).Append(".0.value == 2 else => $abort(\"value\")\n");
        }

        source.Append("check()");
        var c = MinimalEmissionTest.Analyze(source.ToString());
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        void Analyze()
        {
            if (!c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Live part Loans failed.");
            }
        }

        for (var i = 0; i < 3; i++)
        {
            Analyze();
        }

        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 5; i++)
        {
            Analyze();
        }

        output.WriteLine($"count={count}; checking={checking}; five analyses ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3}");
        Assert.Equal(0, AllocationMeasurement.Measure(Analyze, 2));
        ScalarEmissionTest.EmitFixture($"BorrowSnapshot{count}{checking}", source.ToString(), string.Empty);
    }
}
