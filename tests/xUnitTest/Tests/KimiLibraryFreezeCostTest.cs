// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler;
using Kimi.Compiler.Target;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U2a, row s10a (design u2-design 3 and 9): the freeze's cost, once per process and target, recorded as information
// (not a gate): its wall time, the bytes it allocates and the bytes the library it returns keeps. Before U2b's detachment the frozen
// library still holds its freeze compilation, so the retained bytes include it; U2b's weak-reference row then shows the drop.
[Collection<AllocationIsolationCollection>]
public sealed class KimiLibraryFreezeCostTest
{
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void TheFreezesCostIsRecorded()
    {
        var target = TargetTriple.Parse(WindowsProfile.Target);
        var kimigayo = Compilation.CreateForTest().Kimigayo;
        var (time, allocated, retained) = Measure(() => KimiLibrary.Freeze(KimiLibrary.Unfrozen(target, kimigayo, new Kimi.Project(kimigayo))));
        Assert.True(allocated > 0 && retained > 0, $"allocated {allocated}, retained {retained}");
        TestContext.Current.TestOutputHelper?.WriteLine($"freeze ({WindowsProfile.Target}): {time.TotalMilliseconds:F1} ms, {allocated} bytes allocated, {retained} bytes retained");
    }

    private static (TimeSpan Time, long Allocated, long Retained) Measure(Func<KimiLibrary> freeze)
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true); // The Bind hops to a compiler thread: the process total.
        var watch = Stopwatch.StartNew();
        var library = freeze();
        watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(library);
        return (watch.Elapsed, allocated, retained);
    }
}
