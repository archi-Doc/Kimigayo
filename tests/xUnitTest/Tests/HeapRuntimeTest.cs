// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Build;
using Xunit;

namespace XunitTest;

/// <summary>Kimi's allocator (IMPL 21.4.3; the Tatara plan's decision 23, B7r2) in executables: blocks of every class, their reuse
/// after release, a region change, large runs, the alignment of every result, and the failures of the platform layer's pages.</summary>
public class HeapRuntimeTest
{
    // The allocator program of design §5 row 14 (BlocksOfEveryClassAreAlignedAndReused, below), which the Linux module rows emit
    // and link too (TataraExecutorTest).
    internal const string AllocatorProgram = Helpers + """
        var block: isize = 32
        while block <= 262144
            let small = block / 2 - 15
            let large = block - 16
            let a = Raw.allocate<u8>(small)
            let b = Raw.allocate<u8>(large)
            require aligned(a) and aligned(b) and a != b else => $abort("class")
            fill(a, small, 1)
            fill(b, large, 2)
            require holds(a, small, 1) and holds(b, large, 2) else => $abort("bytes")
            unsafe
                Raw.release(a)
                Raw.release(b)
            let c = Raw.allocate<u8>(large)
            let d = Raw.allocate<u8>(small)
            require c == b and d == a else => $abort("reuse")
            unsafe
                Raw.release(c)
                Raw.release(d)
            let above = Raw.allocate<u8>(large + 1)
            let below = Raw.allocate<u8>(small - 1)
            require aligned(above) and above != c and above != d and (block == 32 or (below != c and below != d)) else => $abort("boundary")
            fill(above, large + 1, 3)
            require holds(above, large + 1, 3) else => $abort("boundary bytes")
            unsafe
                Raw.release(above)
                Raw.release(below)
            block *= 2
        let r1 = Raw.allocate<u8>(262128)
        let r2 = Raw.allocate<u8>(262128)
        let r3 = Raw.allocate<u8>(262128)
        let r4 = Raw.allocate<u8>(262128)
        let r5 = Raw.allocate<u8>(262128)
        require aligned(r1) and aligned(r2) and aligned(r3) and aligned(r4) and aligned(r5) else => $abort("region aligned")
        fill(r1, 262128, 11)
        fill(r2, 262128, 12)
        fill(r3, 262128, 13)
        fill(r4, 262128, 14)
        fill(r5, 262128, 15)
        require holds(r1, 262128, 11) and holds(r2, 262128, 12) and holds(r3, 262128, 13) and holds(r4, 262128, 14) and holds(r5, 262128, 15) else => $abort("region bytes")
        unsafe
            Raw.release(r1)
            Raw.release(r2)
            Raw.release(r3)
            Raw.release(r4)
            Raw.release(r5)
        let big = Raw.allocate<u8>(307200)
        let huge = Raw.allocate<u8>(1048576)
        require aligned(big) and aligned(huge) and big != huge else => $abort("large")
        fill(big, 307200, 21)
        fill(huge, 1048576, 22)
        require holds(big, 307200, 21) and holds(huge, 1048576, 22) else => $abort("large bytes")
        unsafe
            Raw.release(big)
            Raw.release(huge)
        var n: isize = 0
        while n <= 40
            let words = Raw.allocate<u64>(n)
            require words != null and aligned(words) else => $abort("words")
            unsafe => Raw.release(words)
            n += 1
        var i: isize = 0
        var total: isize = 0
        while i < 100000
            let values = Array<u8>.init(repeating: 1, count: i % 300 + 1)
            total += values.length
            i += 1
        require total == 15040000 else => $abort("arrays")
        Console.writeLine("heap ok")
        """;

    // Whether an address is 16-aligned, and a block's first and last bytes written and read back, so overlapping blocks would fail.
    private const string Helpers = """
        func aligned<T>(p: raw/T) -> bool => p@usize % 16 == 0
        func fill(p: raw/u8, length: isize, value: u8)
            unsafe
                Raw.initialize(p, value)
                if length > 1 => Raw.initialize(p + (length - 1), value)
        func holds(p: raw/u8, length: isize, value: u8) -> bool
            unsafe => return *p == value and p[length - 1] == value

        """;

    // Every class (2^k bytes with the 16-byte header, k = 5 to 18) by its smallest and largest payload, aligned and apart; their release
    // and the class's LIFO reuse (the last released block first); each boundary pinned by the payloads one byte past it, which take another
    // class and so neither released block (above the largest, the next class or, past 256 KiB, the smallest large run of 262,129 bytes;
    // below the smallest, the class before). The classes carve 1,048,512 bytes of the first region, so five more
    // 256 KiB blocks take two from their list and three from a new region. Large runs of 300 KiB and 1 MiB, Raw.allocate<u64>(n) for n =
    // 0 to 40, and 10^5 Arrays of 1 to 300 bytes built and dropped (design §5 row 14).
    [Fact]
    public void BlocksOfEveryClassAreAlignedAndReused()
    {
        ScalarEmissionTest.EmitFixture("HeapRuntimeBlocks", AllocatorProgram, "heap ok\n");
    }

    // SPEC 22.5.2: the layer has no pages, for a new region or for a large run: KIMI_E_ALLOC at the allocation, without an OS error. The
    // adapter suite's mode 15 fakes the first region away under the runtime alone; these rows add the large run's path and the report at a
    // program's allocating source location.
    [Theory]
    [InlineData("Region", 10)]
    [InlineData("Run", 307200)]
    public void NoPagesIsAnAllocationFailure(string name, int bytes)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("let p = Raw.allocate<u8>(" + bytes + ")"));
        Assert.Contains("call ptr @VirtualAlloc(", ir, StringComparison.Ordinal);
        ir = ir.Replace("call ptr @VirtualAlloc(", "call ptr @fail_pages(", StringComparison.Ordinal) +
            "\ndefine internal ptr @fail_pages(ptr %address, i64 %bytes, i32 %kind, i32 %protect) {\nentry:\n  ret ptr null\n}\n";
        ScalarEmissionTest.WriteFixture("HeapRuntimeNoPages" + name, ir, string.Empty, 1, "Hello.kimi:1:9: abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    // IMPL 21.4.3 as amended (PLAN G1311): a released large run of at most 1 MiB is kept, and the next block of its length takes it
    // without the layer. The wrapper refuses a second mapping of the last run's length (a region's 1 MiB passes and is not remembered).
    // Three 300 KiB Arrays (the take and the keep), three 600 KiB ones (the first release hands the kept 300 KiB run back, the
    // replacement), then a 300 KiB one (a new length after it). Fail-before: the second 300 KiB Array asks the layer again and Aborts
    // KIMI_E_ALLOC.
    [Fact]
    public void AReleasedRunIsTakenAgainWithoutTheLayer()
    {
        const string Source = """
            var sum: i64 = 0
            for _ in 0..3
                let ones = Array<u8>.init(repeating: 1, count: 307200)
                sum += ones[0]@i64
            for _ in 0..3
                let twos = Array<u8>.init(repeating: 2, count: 614400)
                sum += twos[0]@i64
            let threes = Array<u8>.init(repeating: 3, count: 307200)
            sum += threes[0]@i64
            require sum == 12 else => $abort("sum")
            Console.writeLine("runs ok")
            """;
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(Source));
        Assert.Contains("call ptr @VirtualAlloc(", ir, StringComparison.Ordinal);
        ir = ir.Replace("call ptr @VirtualAlloc(", "call ptr @map_once(", StringComparison.Ordinal) + """

            @last_run = internal global i64 0
            define internal ptr @map_once(ptr %address, i64 %bytes, i32 %kind, i32 %protect) {
            entry:
              %last = load i64, ptr @last_run
              %again = icmp eq i64 %bytes, %last
              br i1 %again, label %refuse, label %map
            refuse:
              ret ptr null
            map:
              %region = icmp eq i64 %bytes, 1048576
              br i1 %region, label %call, label %remember
            remember:
              store i64 %bytes, ptr @last_run
              br label %call
            call:
              %pages = call ptr @VirtualAlloc(ptr %address, i64 %bytes, i32 %kind, i32 %protect)
              ret ptr %pages
            }

            """;
        ScalarEmissionTest.WriteFixture("HeapRuntimeRunTakenAgain", ir, "runs ok\n");
    }

    // SPEC 22.5.2: a large run's release that the system refuses (VirtualFree given a size with MEM_RELEASE, ERROR_INVALID_PARAMETER)
    // Aborts KIMI_E_FREE with the OS error at the release that hands a run to the layer (PLAN G1311): a run above the spare's bound goes
    // back at its own release, and a kept run at the release that replaces it. A run of exactly 1 MiB is kept, and a smaller run never
    // takes a larger spare (it maps its own, whose release replaces the spare). The adapter suite's mode 17 fakes the refusal and its
    // error; these rows have the real system refuse it and report at a program's releasing source location.
    [Theory]
    [InlineData("Above", "let big = Raw.allocate<u8>(1048576)\nunsafe => Raw.release(big)", 2)]
    [InlineData("AtTheBound", "let a = Raw.allocate<u8>(1048560)\nunsafe => Raw.release(a)\nlet b = Raw.allocate<u8>(307200)\nunsafe => Raw.release(b)", 4)]
    [InlineData("Replaced", "let a = Raw.allocate<u8>(307200)\nunsafe => Raw.release(a)\nlet b = Raw.allocate<u8>(614400)\nunsafe => Raw.release(b)", 4)]
    [InlineData("Smaller", "let a = Raw.allocate<u8>(614400)\nunsafe => Raw.release(a)\nlet b = Raw.allocate<u8>(307200)\nunsafe => Raw.release(b)", 4)]
    public void ARefusedReleaseIsAFreeFailure(string name, string source, int line)
    {
        const string Release = "call i32 @VirtualFree(ptr %pages, i64 0, i32 32768)";
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source));
        Assert.Contains(Release, ir, StringComparison.Ordinal);
        ir = ir.Replace(Release, "call i32 @VirtualFree(ptr %pages, i64 1, i32 32768)", StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture(
            "HeapRuntimeRefusedRelease" + name, ir, string.Empty, 1, $"Hello.kimi:{line}:11: abort KIMI_E_FREE: Failed to free memory (win32=87)\n");
    }

    // PLAN G1311 Revision 1: every path that calls the layer is unlikely. At O2 each inlined allocation and release still tests the
    // template's `%small` (a class's block, not a large run) and the allocation its `%fits` (the current region, not a new one), and each
    // such branch weights the small path's edge over the edge toward the layer, which has weight 1. The edge is read from the condition,
    // since jump threading renames the blocks: the small path is the true edge, or the false edge when instcombine inverted the compare
    // (`.not`) and swapped the weights. A loop allocates and releases blocks of 4^0 to 4^10 bytes, so no site's size is constant.
    // Fail-before: the branches carry no weights.
    [Fact]
    public void PathsIntoTheLayerAreUnlikelyAtO2()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Tatara runs on Windows.");
        var text = Optimized("var n: isize = 1\nwhile n < 2000000\n    let p = Raw.allocate<u8>(n)\n    unsafe => Raw.release(p)\n    n *= 4");
        var weights = Regex.Matches(text, @"^!(\d+) = !\{!""branch_weights""(?:, !""expected"")?, i32 (\d+), i32 (\d+)\}", RegexOptions.Multiline)
            .ToDictionary(static x => x.Groups[1].Value, static x => (First: long.Parse(x.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), Second: long.Parse(x.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)));
        var branches = Regex.Matches(text, @"\bbr i1 %((small|fits)(?:\.[\w.]*)?), label %[\w.$-]+, label %[\w.$-]+(.*)$", RegexOptions.Multiline);
        Assert.True(branches.Count(static x => x.Groups[2].Value == "small") >= 2, "the allocation's and the release's small tests");
        Assert.True(branches.Any(static x => x.Groups[2].Value == "fits"), "the allocation's region test");
        Assert.All(branches, x =>
        {
            var profile = Regex.Match(x.Groups[3].Value, @"!prof !(\d+)");
            Assert.True(profile.Success, x.Value);
            var (first, second) = weights[profile.Groups[1].Value];
            var (hot, cold) = x.Groups[1].Value.Contains(".not", StringComparison.Ordinal) ? (second, first) : (first, second);
            Assert.True(cold == 1 && hot > cold, x.Value);
        });
    }

    // IMPL 21.4.3 as amended (PLAN G1311): the run path is two noinline routines, so at O2 the inlined allocation and release sites keep
    // the small path only. A small and a large block's program through the product's O2: both routines stay defined and are called from
    // another definition, the spare's globals appear in them alone, the pages' release (the inlined platform routine) is called in the
    // return routine alone, and every other page request is a region's 1 MiB (the allocation's region path). Without noinline both fold
    // into every site.
    [Fact]
    public void TheRunPathStaysOutOfLineAtO2()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Tatara runs on Windows.");
        const string Source = "let small = Raw.allocate<u8>(48)\nlet large = Raw.allocate<u8>(307200)\nunsafe => Raw.release(small)\nunsafe => Raw.release(large)";
        const string Take = "__kimi_heap_take_run";
        const string Return = "__kimi_heap_return_run";
        var definitions = Definitions(Optimized(Source));
        foreach (var routine in (string[])[Take, Return])
        {
            Assert.Contains(routine, definitions.Keys);
            Assert.Contains(definitions, x => x.Key != routine && x.Value.Contains("@" + routine + "(", StringComparison.Ordinal));
        }

        Assert.All(definitions.Where(static x => x.Key is not (Take or Return)), static x => Assert.DoesNotContain("@__kimi_heap_spare", x.Value, StringComparison.Ordinal));
        Assert.Contains("@VirtualFree(", definitions[Return], StringComparison.Ordinal);
        Assert.All(definitions.Where(static x => x.Key != Return), static x => Assert.DoesNotContain("@VirtualFree(", x.Value, StringComparison.Ordinal));
        var requests = definitions.Where(static x => x.Key != Take).SelectMany(static x => x.Value.Split('\n')).Where(static x => x.Contains("@VirtualAlloc(", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(requests);
        Assert.All(requests, static x => Assert.Contains("i64 1048576,", x, StringComparison.Ordinal));
    }

    // A program's module after the product's O2 (Tatara's optimize, as NativeBuild calls it).
    private static string Optimized(string source)
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-heap-o2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ir = Path.Combine(directory, "Hello.ll");
            File.WriteAllText(ir, CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source)));
            return Tatara.Instance.Optimize(TataraExecutorTest.Context, ir);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // An optimized module's definitions by name, each its define line to its closing brace.
    private static Dictionary<string, string> Definitions(string text)
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("define ", StringComparison.Ordinal))
            {
                start = i;
            }
            else if (start >= 0 && lines[i].TrimEnd() == "}")
            {
                var header = lines[start];
                var at = header.IndexOf('@', StringComparison.Ordinal);
                definitions.Add(header[(at + 1)..header.IndexOf('(', at)], string.Join('\n', lines[start..(i + 1)]));
                start = -1;
            }
        }

        return definitions;
    }
}
