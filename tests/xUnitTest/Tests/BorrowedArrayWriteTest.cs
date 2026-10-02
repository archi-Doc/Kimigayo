// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class BorrowedArrayWriteTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Scalar", "func put(a: uniq/Array<i32>) => a[0] = 42\nvar a: Array<i32> = [1]\nput(a@uniq)\nrequire a[0] == 42 else => $abort(\"value\")", "")]
    [InlineData("String", "func put(a: uniq/Array<string>) => a[^1] = \"new\"\nvar a: Array<string> = [\"old\"]\nput(a@uniq)\nConsole.writeLine(a[0])", "new\n")]
    [InlineData("Generic", "func put<T>(a: uniq/Array<T>, value: T) => a[0] = value@move\nvar a: Array<i32> = [1]\nput(a@uniq, 42)\nrequire a[0] == 42 else => $abort(\"value\")\nvar words: Array<string> = [\"old\"]\nput(words@uniq, \"new\")\nConsole.writeLine(words[0])", "new\n")]
    [InlineData("Drop", "struct Item\n    let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\nfunc put(a: uniq/Array<Item>) => a[0] = Item.init(\"new\")\nvar a: Array<Item> = [Item.init(\"old\")]\nput(a@uniq)\nConsole.writeLine(\"after\")", "old\nafter\nnew\n")]
    [InlineData("Order", "func key() -> isize\n    Console.writeLine(\"key\")\n    return 0\nfunc rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 42\nfunc put(a: uniq/Array<i32>) => a[key()] = rhs()\nvar a: Array<i32> = [1]\nput(a@uniq)\nrequire a[0] == 42 else => $abort(\"value\")", "rhs\nkey\n")]
    [InlineData("Update", "func put(a: uniq/Array<i32>)\n    a[0] += 40\n    let old = a[0]++\n    require old == 41 and --a[0] == 41 else => $abort(\"update\")\n    a[0] += 1\nvar a: Array<i32> = [1]\nput(a@uniq)\nrequire a[0] == 42 else => $abort(\"value\")", "")]
    [InlineData("EndedLoan", "var a: Array<i32> = [1]\nlet r = a@uniq\nlet old = r[0]@ref\nrequire old == 1 else => $abort(\"read\")\nr[0] = 42\nrequire r[0] == 42 else => $abort(\"value\")", "")]
    [InlineData("Unit", "func put(a: uniq/Array<()>) => a[0] = ()\nvar a: Array<()> = [()]\nput(a@uniq)\nrequire a.length == 1 else => $abort(\"unit\")", "")]
    [InlineData("AbruptRhs", "func key() -> isize\n    Console.writeLine(\"wrong\")\n    return 0\nfunc put(a: uniq/Array<i32>)\n    a[key()] = (do => return)\nvar a: Array<i32> = [1]\nput(a@uniq)\nrequire a[0] == 1 else => $abort(\"unchanged\")", "")]
    [InlineData("ReceiverOnce", "func receiver(a: uniq/Array<i32>) -> uniq/Array<i32> during a\n    Console.writeLine(\"receiver\")\n    return a\nvar a: Array<i32> = [1]\nreceiver(a@uniq)[0] += 41\nrequire a[0] == 42 else => $abort(\"value\")", "receiver\n")]
    public void Executes(string name, string source, string stdout)
    {
        ScalarEmissionTest.EmitFixture("BorrowedArrayWrite" + name, source, stdout);
        ScalarEmissionTest.EmitFixture("BorrowedFixedArrayWrite" + name, FixedArray(source), stdout);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBorrowedFixedArrayUpdatesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("func edit(a: uniq/[2 of i32])\n    a[0] = 2\n    a[^1] += a[0]\nvar a: [2 of i32] = [1, 2]\nedit(a@uniq)");
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.Validate(out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Borrowed array Binding failed.");
            }

            c.Binding.CheckStartup(OutputKind.Application);
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.Validate(out _))
            {
                throw new InvalidOperationException("Borrowed array pipeline failed.");
            }
        }));
    }

    [Theory]
    [InlineData("func put(a: ref/Array<i32>) => a[0] = 42", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("var a: Array<i32> = [1]\nlet r = a@uniq\nlet old = r[0]@ref\nr[0] = 42\nlet n = old + 1", DiagnosticCode.ComparisonLoanConflict_Kd)]
    [InlineData("var a: Array<i32> = [1]\nlet r = a@uniq\nlet old = r[0]@ref\nlet edit = r[0]@uniq\nedit@follow = 42\nlet n = old + 1", DiagnosticCode.ComparisonLoanConflict_Kd)]
    [InlineData("func put(a: uniq/Array<i32>)\n    let old = a[0]@ref\n    a[0] = 42\n    let n = old + 1", DiagnosticCode.ComparisonLoanConflict_Kd)]
    public void RejectsInvalidCapability(string source, DiagnosticCode code)
    {
        foreach (var input in new[] { source, FixedArray(source) })
        {
            var c = MinimalEmissionTest.Analyze(input);
            c.Binding.ReportDiagnostics();
            c.Ownership.ReportDiagnostics();
            Assert.Contains(c.Diagnostics.Finalize(rejected: true).Diagnostics, x => x.Code == code.ToString());
            Assert.False(c.Emission.Validate(out _));
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("^0")]
    public void RetainsBoundsChecks(string key)
    {
        ScalarEmissionTest.EmitFixture(
            "BorrowedArrayWriteBounds" + key.Replace("-", "Minus").Replace("^", "End"),
            $"func put(a: uniq/Array<i32>) => a[{key}] = 2\nvar a: Array<i32> = [1]\nput(a@uniq)",
            string.Empty,
            1,
            "Hello.kimi:1:33: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
        ScalarEmissionTest.EmitFixture(
            "BorrowedFixedArrayWriteBounds" + key.Replace("-", "Minus").Replace("^", "End"),
            $"func put(a: uniq/[1 of i32]) => a[{key}] = 2\nvar a: [1 of i32] = [1]\nput(a@uniq)",
            string.Empty,
            1,
            "Hello.kimi:1:33: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    [Fact]
    public void ConflictOutputIdentifiesTheExclusiveElementAccess()
    {
        const string source = "var a: Array<i32> = [1]\nlet r = a@uniq\nlet old = r[0]@ref\nr[0] = 42\nlet n = old + 1";
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(new SourceSpan(source.IndexOf("r[0] =", StringComparison.Ordinal), 4), error.Span);
        Assert.Equal("This operation conflicts with an active loan", error.Message);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(error.Message, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(Path.GetFullPath("Hello.kimi"));
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static string FixedArray(string source)
        => System.Text.RegularExpressions.Regex.Replace(source, @"Array<([^<>]+)>", "[1 of $1]");
}
