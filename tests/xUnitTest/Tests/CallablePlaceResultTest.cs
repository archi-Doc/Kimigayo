// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 7.1.1: every callable path retains the published Place and its acquisition contract.</summary>
public class CallablePlaceResultTest
{
    private const string Shared = "func first<T>(values: ref/Array<T>) -> place ref/T during values => values[0]\n";
    private const string Exclusive = "func slot(values: uniq/Array<i32>) -> place uniq/i32 during values => values[0]\n";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "ItemRead", Shared + "let f = first<i32>\nlet a: Array<i32> = [42]\nrequire f(a) == 42 else => $abort(\"read\")" },
        { "ClosureRead", "let f = func (a: ref/Array<i32>) -> place ref/i32 => a[0]\nlet a: Array<i32> = [42]\nrequire f(a) == 42 else => $abort(\"read\")" },
        { "ClosureBlock", "let f = func (a: ref/Array<i32>) -> place ref/i32\n    return a[0]\nlet a: Array<i32> = [42]\nrequire f(a) == 42 else => $abort(\"read\")" },
        { "ItemWrite", Exclusive + "let f = slot\nvar a: Array<i32> = [1]\nf(a@uniq) = 41\nf(a@uniq) += 1\nrequire a[0] == 42 else => $abort(\"write\")" },
        { "ClosureWrite", "let f = func (a: uniq/Array<i32>) -> place uniq/i32 => a[0]\nvar a: Array<i32> = [1]\nf(a@uniq) = 41\nf(a@uniq) += 1\nrequire a[0] == 42 else => $abort(\"write\")" },
        { "CallableRead", Shared + "func read<F>(f: ref/F, a: ref/Array<i32>) -> i32\n    F is Callable<(ref/Array<i32>) -> place ref/i32>\n    return f(a)\nlet a: Array<i32> = [42]\nrequire read(first<i32>, a) == 42 else => $abort(\"read\")" },
        { "CallableWrite", Exclusive + "func write<F>(f: ref/F, a: uniq/Array<i32>)\n    F is Callable<(uniq/Array<i32>) -> place uniq/i32>\n    f(a) = 42\nvar a: Array<i32> = [1]\nwrite(slot, a@uniq)\nrequire a[0] == 42 else => $abort(\"write\")" },
        { "ErasedRead", Shared + "let f: (ref/Array<i32>) -> place ref/i32 = first\nlet a: Array<i32> = [42]\nrequire f(a) == 42 else => $abort(\"read\")" },
        { "ErasedWrite", Exclusive + "let f: (uniq/Array<i32>) -> place uniq/i32 = slot\nvar a: Array<i32> = [1]\nf(a@uniq) = 42\nrequire a[0] == 42 else => $abort(\"write\")" },
        { "ErasedClosure", "let f: (ref/Array<i32>) -> place ref/i32 = func (a) -> place ref/i32 => a[0]\nlet a: Array<i32> = [42]\nrequire f(a) == 42 else => $abort(\"read\")" },
        { "NonCopy", Shared + "let f = first<string>\nlet a: Array<string> = [\"value\"]\nlet view = f(a)@ref\nrequire view == \"value\" else => $abort(\"borrow\")\nlet g: (ref/Array<string>) -> place ref/string = first\nlet other: ref/string = g(a)\nrequire other == \"value\" else => $abort(\"erased borrow\")" },
        { "ZeroSize", Shared + "let f: (ref/Array<()>) -> place ref/() = first\nlet a: Array<()> = [()]\nlet view = f(a)@ref\nrequire view@follow == () else => $abort(\"unit\")" },
        { "NestedReference", Shared + "func run(n: ref/i32 during a) -> i32\n    let values: Array<ref/i32 during a> = [n]\n    let f: (ref/Array<ref/i32 during a>) -> place ref/(ref/i32 during a) = func (v) -> place ref/(ref/i32 during a) => v[0]\n    let item = first<ref/i32 during a>\n    let slot = f(values)@ref\n    let read = f(values)\n    return slot@follow@follow + read@follow + item(values)@follow - n@follow\nlet n = 21\nrequire run(n@ref) == 42 else => $abort(\"nested\")" },
        { "LengthItem", "func first<length N, T>(values: ref/[N of T]) -> place ref/T => values[0]\nlet f = first<2, i32>\nlet g: (ref/[2 of i32]) -> place ref/i32 = first\nlet a: [2 of i32] = [21, 7]\nrequire f(a) + g(a) == 42 else => $abort(\"length\")" },
        { "MultipleContracts", "func read<T, F>(f: ref/F, value: ref/T) -> T\n    T is Copy\n    F is Callable<(ref/T) -> place ref/T>\n    F is Callable<(ref/i32) -> place ref/i32>\n    return f(value)\nfunc slot(n: ref/i32) -> place ref/i32 => n@follow\nlet n = 42\nrequire read(slot, n@ref) == 42 else => $abort(\"contracts\")" },
        { "Projection", "func slot(a: uniq/Array<(i32, i32)>) -> place uniq/(i32, i32) => a[0]\nlet f: (uniq/Array<(i32, i32)>) -> place uniq/(i32, i32) = slot\nvar a: Array<(i32, i32)> = [(1, 7)]\nf(a@uniq).0 = 40\nf(a@uniq).0 += 2\nrequire a[0].0 == 42 else => $abort(\"projection\")" },
        { "BranchReturn", "let f = func (a: ref/Array<i32>, choose: bool) -> place ref/i32\n    if choose\n        return a[0]\n    else\n        return a[1]\nlet a: Array<i32> = [40, 2]\nrequire f(a, true) + f(a, false) == 42 else => $abort(\"branch\")" },
        { "DiscardNonCopy", Shared + "let f: (ref/Array<string>) -> place ref/string = first\nlet a: Array<string> = [\"value\"]\nf(a)\nrequire a[0] == \"value\" else => $abort(\"discard\")" },
        { "ZeroSizeExclusive", "let f: (uniq/Array<()>) -> place uniq/() = func (a) -> place uniq/() => a[0]\nvar a: Array<()> = [()]\nf(a@uniq) = ()\nlet r = f(a@uniq)@uniq\nr@follow = ()" },
        { "NeverBranch", "let f: (ref/Array<i32>, bool) -> place ref/i32 = func (a, fail) -> place ref/i32\n    if fail => $abort(\"fail\")\n    return a[0]\nlet a: Array<i32> = [42]\nrequire f(a, false) == 42 else => $abort(\"read\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("CallablePlace" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData(Shared + "let f: (ref/Array<i32>) -> ref/i32 = first", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("func first(a: ref/Array<i32>) -> ref/i32 => a[0]@ref\nlet f: (ref/Array<i32>) -> place ref/i32 = first", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let f: (ref/Array<i32>) -> place ref/i32 = func (a) => a[0]@ref", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let f = func () -> place ref/i32 during static => 1", DiagnosticCode.PlaceRequired_Kd)]
    [InlineData("let f = func (a: ref/Array<i32>) -> place ref/i32 => if true => a[0] else => a[1]", DiagnosticCode.PlaceRequired_Kd)]
    public void RejectsContractMismatch(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Theory]
    [InlineData("let f = first<string>", "let taken = f(a)@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("let f: (ref/Array<string>) -> place ref/string = first", "f(a) = \"b\"", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("let f: (ref/Array<string>) -> place ref/string = first", "let view = f(a)@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    public void SharedPlacesKeepTheirCapabilities(string declaration, string use, DiagnosticCode code)
    {
        var source = Shared + declaration + "\nvar a: Array<string> = [\"a\"]\n" + use;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("let f = slot")]
    [InlineData("let f: (uniq/Array<i32>) -> place uniq/i32 = slot")]
    [InlineData("let f = func (a: uniq/Array<i32>) -> place uniq/i32 => a[0]")]
    public void AResultKeepsItsInputLoan(string declaration)
    {
        var source = Exclusive + declaration + "\nvar a: Array<i32> = [1]\nlet view = f(a@uniq)@ref\nf(a@uniq) = 42\nConsole.writeLine(\"\\(view@follow)\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void AnUpdateCallsTheFunctionExactlyOnce()
    {
        const string Source = "func slot(a: uniq/Array<i32>) -> place uniq/i32\n    Console.writeLine(\"call\")\n    return a[0]\nfunc rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 2\nlet f: (uniq/Array<i32>) -> place uniq/i32 = slot\nvar a: Array<i32> = [40]\nf(a@uniq) += rhs()\nrequire a[0] == 42 else => $abort(\"once\")";
        ScalarEmissionTest.EmitFixture("CallablePlaceOnce", Source, "rhs\ncall\n");
    }

    [Fact]
    public void ResultModesHaveDistinctCanonicalIdentities()
    {
        var c = MinimalEmissionTest.Analyze("func value(a: ref/i32) -> ref/i32 => a\nfunc place(a: ref/i32) -> place ref/i32 => a@follow\nlet f: (ref/i32) -> ref/i32 = value\nlet g: (ref/i32) -> place ref/i32 = place\nlet h: (ref/i32) -> place ref/i32 = place");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var variables = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Where(x => x.NameKoto.ToString() is "f" or "g" or "h").ToArray();
        Assert.Equal(3, variables.Length);
        Assert.NotSame(variables[0].BoundType, variables[1].BoundType);
        Assert.Equal(FunctionResultMode.Value, variables[0].BoundType!.ResultMode);
        Assert.Equal(FunctionResultMode.PlaceRef, variables[1].BoundType!.ResultMode);
        // Per-call binders belong to their written signature; repeated binding reuses that exact identity.
        var identity = variables[1].BoundType;
        Assert.True(c.Bind().IsComplete);
        Assert.Same(identity, variables[1].BoundType);
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmPlaceCallsReuseAllPhases(bool erased)
    {
        var declaration = erased ? "let f: (uniq/Array<i32>) -> place uniq/i32 = func (a) -> place uniq/i32 => a[0]" : "let f = slot";
        var c = MinimalEmissionTest.Analyze(Exclusive + declaration + "\nvar a: Array<i32> = [1]\nf(a@uniq) += 2");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("let f = func (a: ref/Array<()>) -> place ref/()\n    ()\n", DiagnosticCode.FunctionFallthrough_Kd)]
    [InlineData("let f = func () -> place ref/i32 during static\n    let n = 1\n    return n\n", DiagnosticCode.UnsatisfiedOriginRelation_Kd)]
    [InlineData(Exclusive + "let f = slot\nvar a: Array<i32> = [1]\nlet value = f(a@uniq)@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Shared + "let f = first<string>\nlet a: Array<string> = [\"a\"]\nlet value = f(a)", DiagnosticCode.TransferRequired_Kd)]
    [InlineData(Shared + "let f = first<string>\nlet a: Array<string> = [\"a\"]\n_ = f(a)", DiagnosticCode.TransferRequired_Kd)]
    public void RejectedPlaceUsesKeepTheirLanguageDiagnostics(string source, DiagnosticCode code)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == code.ToString());
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    [Theory]
    [InlineData("let n = 7\nlet f = func [n] () -> place ref/i32 => n\nlet e: () -> place ref/i32 = f")]
    [InlineData("let a: Array<i32> = [7]\nlet f = func [a@move] () -> place ref/i32 => a[0]\nlet e: () -> place ref/i32 = f@move")]
    public void AStaticPlaceResultCannotBorrowTheEnvironment(string source)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), Assert.Single(errors).Code);
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    [Fact]
    public void ResultModeDiagnosticsAreSharedByCliAndLsp()
    {
        const string Source = "func slot(n: ref/i32) -> place ref/i32 => n@follow\nlet f: (ref/i32) -> ref/i32 = slot";
        var path = Path.GetFullPath("CallablePlace.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), record.Code);
        Assert.Equal("slot", Source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("place ref/i32", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        Assert.Equal(record.Code, sent.Code);
        Assert.Equal(record.Display!.Range, sent.Range);
    }

    [Fact]
    public void EditingAResultModeRevokesItsPriorContract()
    {
        const string Source = "func slot(n: ref/i32) -> place ref/i32 => n@follow\nlet f: (ref/i32) -> place ref/i32 = slot";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
        var original = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionTypeKoto>().Single();
        var donor = MinimalEmissionTest.Analyze(Source.Replace("let f: (ref/i32) -> place ref/i32", "let f: (ref/i32) -> ref/i32", StringComparison.Ordinal));
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionTypeKoto>().Single();
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
