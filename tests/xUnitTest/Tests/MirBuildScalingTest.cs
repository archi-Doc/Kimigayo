// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
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

            blocks[depth] = Blocks(text.ToString());
        }

        output.WriteLine($"depth 1-6: blocks {string.Join(", ", blocks[1..])}");
        for (var depth = 2; depth < 6; depth++)
        {
            Assert.True(blocks[depth + 1] <= (3 * blocks[depth]) + 8, $"blocks {string.Join(", ", blocks[1..])}");
        }
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
}
