// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

// The size of the MIR the builder makes where a construct's value-less paths go around an overwrite (SPEC 13.7.1, 14.10.3; the
// Dictionary redesign's step 6): the bounds its design states, on the bodies' block counts.
public class MirBuildScalingTest(ITestOutputHelper output)
{
    private const string Prelude = "func fail() -> Never => $abort(\"no\")\nfunc keepA(a: Array<i32>) -> Array<i32> => a@move\n" +
        "func use(n: i32) -> () => ()\nfunc takeA(a: Array<i32>) -> () => ()\npublic func main() -> ()\n    let y: i32 = 5\n    let c: bool = y > 2\n";

    // The flat shape: N overwrites in one body, each with an arm without a value (`a = if c => keepA(a@move) else => fail()` after a
    // return), add one Apart block and one located target each, so main's blocks grow linearly with N and every overwrite is valid (the
    // value path Moves and re-places a, the value-less path keeps it).
    [Fact]
    public void OverwritesWithAValueLessArmScaleLinearly()
    {
        var (sizes, blocks) = (new[] { 500, 1000, 2000 }, new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder(Prelude).Append("    var a: Array<i32> = [y]\n    return\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append("    a = if c => keepA(a@move) else => fail()\n");
            }

            blocks[i] = Blocks(text.Append("    takeA(a@move)\n").ToString());
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: blocks {string.Join(", ", blocks)}");
        Assert.True(blocks[2] - blocks[1] <= (2 * (blocks[1] - blocks[0])) + 8, $"blocks {string.Join(", ", blocks)}");
    }

    // The nested shape: K overwrites, each a label block's exit with an arm without a value, each in the previous one's defer. A
    // cleanup is built once per route out of its scope, and a wanting target with a value-less delivery adds one route (its Apart) to
    // the two of the base (its Break and the body's end), so main's blocks at depth K + 1 are at most 3 times those at depth K (the
    // base: 2 times), and the body budget (IMPL 21.3.2) bounds the growth: depth 6 is analyzed without a Resource record (the budget is
    // reached at depth 8; STATUS). Every depth is valid.
    [Fact]
    public void NestedCleanupsWithValueLessOverwritesGrowAtMostThreefoldPerLevel()
    {
        var blocks = new long[7];
        for (var depth = 1; depth <= 6; depth++)
        {
            blocks[depth] = Blocks(Nested(depth));
        }

        output.WriteLine($"depth 1-6: blocks {string.Join(", ", blocks[1..])}");
        for (var depth = 2; depth < 6; depth++)
        {
            Assert.True(blocks[depth + 1] <= (3 * blocks[depth]) + 8, $"blocks {string.Join(", ", blocks[1..])}");
        }
    }

    // G5's named move (the design's R3.5; STATUS): the nested shape reaches the body budget (IMPL 21.3.2) at depth 8, so the valid
    // depths 8, 9 and 10, which dev accepts, get only the Resource records ExpansionLimit_Kd and OwnershipStorageLimit_Kd: no Language
    // record and no finding.
    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void NestedCleanupsPastTheBodyBudgetGetOnlyResourceRecords(int depth)
    {
        var program = MirBuildTest.Compile(Nested(depth)).BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(
            [DiagnosticCode.ExpansionLimit_Kd, DiagnosticCode.OwnershipStorageLimit_Kd],
            program.Problems.All.ToArray().Select(static x => x.Code).Distinct().OrderBy(static x => x.ToString()));
    }

    // R7 plan Z23 (SPEC 8.14, 4.7.4; the design's §4.4, B2): a case branch opens only where a decision splits the views of the side being
    // built, so a node is built at most once per view region (at most MirCases.Count, PairBinder.CaseBound). A key chain of one decision
    // (`w1[0][pick(w2[0][pick(...)])]`) has one branch, its keys built once per side; an argument chain of separate takes
    // (`pick2(v1[0], pick2(v2[0], ...))`) one branch per take: both grow linearly with N (10, 20, 30). Six binders of distinct decisions
    // nested in keys (64 views) build level k on at most 2^k sides: 63 branches, within the bound. Each body is valid, with no finding.
    [Fact]
    public void CaseBranchesBuildEachNodeOncePerViewRegion()
    {
        var sizes = new[] { 10, 20, 30 };
        var (keys, arguments) = (new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var n = sizes[i];
            var parameters = string.Join(", ", Enumerable.Range(1, n).Select(static k => $"w{k}: uniq/Two<Two<s/T>>"));
            var chain = $"w{n}[0][0]";
            for (var k = n - 1; k >= 1; k--)
            {
                chain = $"w{k}[0][pick({chain})]";
            }

            (keys[i], var branches) = Branches($"func f<s/T>({parameters}) -> ()\n    s is ref or uniq\n    let x = {chain}\n    _ = x\n");
            Assert.Equal(1, branches);
            var values = string.Join(", ", Enumerable.Range(1, n).Select(static k => $"v{k}: uniq/Two<s/T>"));
            var nested = "0";
            for (var k = n; k >= 1; k--)
            {
                nested = $"pick2(v{k}[0], {nested})";
            }

            (arguments[i], branches) = Branches($"func f<s/T>({values}) -> ()\n    s is ref or uniq\n    let x = {nested}\n    _ = x\n");
            Assert.Equal(n, branches);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: key chain blocks {string.Join(", ", keys)}; argument chain blocks {string.Join(", ", arguments)}");
        Assert.True(keys[2] - keys[1] <= (2 * (keys[1] - keys[0])) + 8, $"key chain blocks {string.Join(", ", keys)}");
        Assert.True(arguments[2] - arguments[1] <= (2 * (arguments[1] - arguments[0])) + 8, $"argument chain blocks {string.Join(", ", arguments)}");

        var binders = string.Join(", ", Enumerable.Range(1, 6).Select(static k => $"s{k}/T{k}"));
        var receivers = string.Join(", ", Enumerable.Range(1, 6).Select(static k => $"w{k}: uniq/Two<Two<s{k}/T{k}>>"));
        var cases = string.Concat(Enumerable.Range(1, 6).Select(static k => $"    s{k} is ref or uniq\n"));
        var distinct = "w6[0][0]";
        for (var k = 5; k >= 1; k--)
        {
            distinct = $"w{k}[0][pick({distinct})]";
        }

        var (blocks, branched) = Branches($"func f<{binders}>({receivers}) -> ()\n{cases}    let x = {distinct}\n    _ = x\n");
        output.WriteLine($"64 views: blocks {blocks}, branches {branched}");
        Assert.Equal(63, branched);
    }

    // R7 plan Z4 (SPEC 7.7.3; the Z23 design's §4.2, §4.4): a value call whose Closure receiver is by case opens a case branch only where
    // its decision splits the views of the side being built, so a call chain of one decision (`h1(h2(...(hN())))`, each Closure taking
    // its own `s/T` capture bare) has one branch, the inner calls built once per side, and grows linearly with N (10, 20, 30); six binders
    // of distinct decisions (64 views) build call k on at most 2^k sides: 63 branches, within the bound. Each body is valid, with no
    // finding.
    [Fact]
    public void ByCaseClosureCallsBuildEachNodeOncePerViewRegion()
    {
        var sizes = new[] { 10, 20, 30 };
        var chains = new long[3];
        for (var i = 0; i < sizes.Length; i++)
        {
            (chains[i], var branches) = Branches(Calls(sizes[i], static _ => ("s/T", "    s is owner or uniq\n    T is Copy\n"), "<s/T>"));
            Assert.Equal(1, branches);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: call chain blocks {string.Join(", ", chains)}");
        Assert.True(chains[2] - chains[1] <= (2 * (chains[1] - chains[0])) + 8, $"call chain blocks {string.Join(", ", chains)}");

        var binders = $"<{string.Join(", ", Enumerable.Range(1, 6).Select(static k => $"s{k}/T{k}"))}>";
        var (blocks, branched) = Branches(Calls(6, static k => ($"s{k}/T{k}", $"    s{k} is owner or uniq\n    T{k} is Copy\n"), binders));
        output.WriteLine($"64 views: blocks {blocks}, branches {branched}");
        Assert.Equal(63, branched);
    }

    // f calling N Closures in one chain, h1 outermost: Closure k captures the parameter vk bare and takes the result of Closure k + 1, so
    // its receiver is by case on vk's binder; `kind` gives vk's Type and its binder's premises.
    private static string Calls(int n, Func<int, (string Type, string Premises)> kind, string binders)
    {
        var text = new StringBuilder($"func f{binders}(");
        text.AppendJoin(", ", Enumerable.Range(1, n).Select(k => $"v{k}: {kind(k).Type}")).Append(") -> ()\n");
        foreach (var premises in Enumerable.Range(1, n).Select(k => kind(k).Premises).Distinct())
        {
            text.Append(premises);
        }

        var chain = $"h{n}()";
        for (var k = n; k >= 1; k--)
        {
            var parameter = k == n ? string.Empty : "x: ()";
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    var h{k} = func [v{k}] ({parameter}) -> ()\n        let l{k} = v{k}\n        _ = l{k}\n");
            chain = k == n ? chain : $"h{k}({chain})";
        }

        return text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    {chain}\n").ToString();
    }

    // The nested shape at a depth: K overwrites, each in the previous one's defer, then each target used.
    private static string Nested(int depth)
    {
        var text = new StringBuilder(Prelude);
        for (var i = 1; i <= depth; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    var a{i}: Array<i32> = [y]\n");
        }

        text.Append("    return\n");
        Level(text, 1, depth, 1);
        for (var i = 1; i <= depth; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    takeA(a{i}@move)\n");
        }

        return text.ToString();
    }

    // One level of the nested shape and the levels inside its defer, at an indentation.
    private static void Level(StringBuilder text, int level, int depth, int indent)
    {
        var pad = new string(' ', 4 * indent);
        if (level > depth)
        {
            text.Append(pad).Append("use(y)\n");
            return;
        }

        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{pad}a{level} = (label w{level}: do\n{pad}    defer\n");
        Level(text, level + 1, depth, indent + 2);
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{pad}    exit to w{level} (if c => keepA(a{level}@move) else => fail())\n{pad})\n");
    }

    // The blocks of main in a program the MIR analyses accept without a finding or a problem.
    private static long Blocks(string source)
    {
        var program = MirBuildTest.Compile(source).BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Problems.All.ToArray());
        return program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "main").Blocks.Length;
    }

    // The blocks and case branches of f in a program the MIR analyses accept without a finding or a problem.
    private static (long Blocks, int Branches) Branches(string source)
    {
        const string Two = "struct Two<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n" +
            "    public init(first: T) => self.first = first@move\n    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq\n" +
            "func pick<X>(value: X) -> isize => 0\nfunc pick2<X>(value: X, next: isize) -> isize => next\n";
        var program = MirBuildTest.Compile(Two + source).BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Problems.All.ToArray());
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        return (body.Blocks.Length, body.CaseSwitches);
    }
}
