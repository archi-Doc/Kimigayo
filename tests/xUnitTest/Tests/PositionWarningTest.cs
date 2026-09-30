// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 17.4.4: a position or range key built only from literal-only expressions, `^` applied to one and omitted
/// boundaries is warned about when it fails to resolve for every length, or for the length of the fixed array it indexes;
/// the warning changes neither Binding nor execution.</summary>
public class PositionWarningTest
{
    private const string Prelude = "let values: Array<i32> = [1, 2, 3]\nlet fixed: [3 of i32] = [1, 2, 3]\n";

    [Theory]
    [InlineData("let a = values[-1]", "The element position -1 fails to resolve for every length")]
    [InlineData("let a = values[^0]", "The element position ^0 fails to resolve for every length")]
    [InlineData("let a = values[(^(-2))]", "fails to resolve for every length")]
    [InlineData("let a = values[..=^0]", "The range ..=^0 fails to resolve for every length")]
    [InlineData("let a = values[2..=1]", "The range 2..=1 fails to resolve for every length")]
    [InlineData("let a = values[^3..^5]", "The range ^3..^5 fails to resolve for every length")]
    [InlineData("let a = values[(2 * 3 - 7)..]", "fails to resolve for every length")]
    [InlineData("let a = fixed[3]", "The element position 3 fails to resolve for the fixed array's length 3")]
    [InlineData("let a = fixed[^4]", "The element position ^4 fails to resolve for the fixed array's length 3")]
    [InlineData("let a = fixed[1..5]", "The range 1..5 fails to resolve for the fixed array's length 3")]
    [InlineData("let a = fixed[5..2]", "The range 5..2 fails to resolve for every length")]
    [InlineData("let a = fixed[..=3]", "The range ..=3 fails to resolve for the fixed array's length 3")]
    [InlineData("let s = values[..]\nlet a = s[^0..=^0]", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet(^0)", "The element position ^0 fails to resolve for every length")]
    [InlineData("let a = fixed.tryGet(3)", "The element position 3 fails to resolve for the fixed array's length 3")]
    [InlineData("let a = fixed.trySlice(1..5)", "The range 1..5 fails to resolve for the fixed array's length 3")]
    [InlineData("let a = fixed.splitAt(^4)", "The position ^4 fails to resolve for the fixed array's length 3")]
    [InlineData("var v: Array<i32> = [1]\nlet r = v.remove(^0)", "The element position ^0 fails to resolve for every length")]
    [InlineData("var v: Array<i32> = [1]\nv.insert(^(-1), 2)", "The position ^(-1) fails to resolve for every length")]
    [InlineData("var v: Array<i32> = [1, 2]\nv.swap(0, -1)", "The element position -1 fails to resolve for every length")]
    [InlineData("let s = values[..]\nlet p = s.trySplitAt(-1)", "The position -1 fails to resolve for every length")]
    [InlineData("let r = (2..=1).tryResolve(5)", "fails to resolve for every length")]
    [InlineData("let p = (^(-1)).resolve(3)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i64>(1 << 63)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i8>(127 << 1)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i128>(1 << 127)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<u128>(340282366920938463463374607431768211455)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i128>(-170141183460469231731687303715884105728)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i8>((-16) * 8)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i8>((-128) >> 7)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i8>((-127) / 2)", "fails to resolve for every length")]
    [InlineData("let a = values.tryGet<i8>((-127) % 2)", "fails to resolve for every length")]
    public void CertainFailuresAreWarned(string statement, string message)
    {
        var c = MinimalEmissionTest.Analyze(Prelude + statement);
        c.Binding.ReportDiagnostics();
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var warning = Assert.Single(TestDiagnostics.Of(c, "Hello.kimi"), x => x.Code == nameof(DiagnosticCode.PositionAlwaysFails_Kd));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);

        // The kind is in the message, the position is the underlined text, and a fixed length is in the label.
        const string Suffix = " fails to resolve for every length it can take";
        Assert.EndsWith(Suffix, warning.Message, StringComparison.Ordinal);
        var kind = warning.Message["The ".Length..^Suffix.Length];
        var described = warning.Label is { } label
            ? $"The {kind} {warning.Text} fails to resolve for the fixed array's length {label["the fixed length is ".Length..]}"
            : $"The {kind} {warning.Text} fails to resolve for every length";
        Assert.Contains(message, described, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let a = values[0]")]
    [InlineData("let a = values[^1]")]
    [InlineData("let a = values[3..]")]
    [InlineData("let a = values[1..^1]")]
    [InlineData("let a = values[^0..]")]
    [InlineData("let a = values[5..=5]")]
    [InlineData("let a = fixed[2]")]
    [InlineData("let a = fixed[^3]")]
    [InlineData("let a = fixed[3..]")]
    [InlineData("let n = -1\nlet a = values[n]")]
    [InlineData("let a = values[(-1)@isize]")]
    [InlineData("let r = 2..=1\nlet a = values[r]")]
    [InlineData("let n: i32 = 5\nlet a = values[n..2]")]
    [InlineData("let a = values[(1 << 40)..]")]
    [InlineData("let a = values.tryGet(^1)")]
    [InlineData("let a = fixed.tryGet(2)")]
    [InlineData("let a = fixed.splitAt(3)")]
    [InlineData("let a = fixed.trySlice(^3..)")]
    [InlineData("var v: Array<i32> = [1]\nv.insert(^0, 2)")]
    [InlineData("let r = (1..^1).tryResolve(5)")]
    [InlineData("let p = (^3).tryResolve(2)")]
    [InlineData("let n = 0\nlet p = values.tryGet(n - 1)")]
    [InlineData("let a = values.tryGet<u8>(1 << 8)")]
    [InlineData("let a = values.tryGet<u8>(255 + 1)")]
    [InlineData("let a = values.tryGet<u8>(255 << 1)")]
    [InlineData("let a = values.tryGet<u8>(0 - 1)")]
    [InlineData("let a = values.tryGet<i8>(-(-128))")]
    [InlineData("let a = values.tryGet<i8>((-128) / (-1))")]
    [InlineData("let a = values.tryGet<i128>((-170141183460469231731687303715884105728) / (-1))")]
    [InlineData("let a = values.tryGet<i128>((-170141183460469231731687303715884105728) % (-1))")]
    [InlineData("let a = values.tryGet<i8>((-128) - 1)")]
    [InlineData("let a = values.tryGet<i8>(127 + 1)")]
    [InlineData("let a = values.tryGet<i8>(-(127 + 1))")]
    [InlineData("let a = values.tryGet<i8>((-16) * 9)")]
    [InlineData("let a = values.tryGet<i128>(170141183460469231731687303715884105728)")]
    [InlineData("let a = values.tryGet<u128>(340282366920938463463374607431768211455 + 1)")]
    [InlineData("func info<length N>(items: [N of i32]) -> i32 => items[1..^1].length@i32 + items[^1] + items[1]")]
    [InlineData("func tail<length N>(items: ref/[N of i32]) -> isize => items[3..].length")]
    public void ResolvableOrNonLiteralKeysAreNotWarned(string statement)
    {
        var c = MinimalEmissionTest.Analyze(Prelude + statement);
        c.Binding.ReportDiagnostics();
        Assert.DoesNotContain(TestDiagnostics.Of(c, "Hello.kimi"), x => x.Code == nameof(DiagnosticCode.PositionAlwaysFails_Kd));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WideLiteralWarningsReuseTheirStorage()
    {
        var c = CompilationTestHelper.ParseSuccess(Prelude + "let a = values.tryGet<i128>(1 << 127)");
        for (var i = 0; i < 4; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // SPEC 17.4.4: the warned access still compiles and Aborts only when executed.
    [Fact]
    public void WarnedAccessAbortsOnlyWhenExecuted()
        => ScalarEmissionTest.EmitFixture(
            "PositionWarningExecution",
            "let values: [3 of i32] = [1, 2, 3]\nlet run = false\nif run => Console.writeLine(\"\\(values[3])\")\nConsole.writeLine(\"skipped\")\nlet a = values[^0]\nConsole.writeLine(\"after\")",
            "skipped\n",
            1,
            "Hello.kimi:5:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
}
