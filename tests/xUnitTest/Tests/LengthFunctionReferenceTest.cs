// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class LengthFunctionReferenceTest
{
    private const string Keep = "func keep<length N, T>(value: [N of T]) -> [N of T] => value@move\n";

    [Theory]
    [InlineData("Explicit", Keep + "let f = keep<2, i32>\nrequire f([4, 7])[1] == 7 else => $abort(\"explicit\")")]
    [InlineData("Expected", Keep + "let f: ([2 of i32]) -> [2 of i32] = keep\nrequire f([4, 7])[1] == 7 else => $abort(\"expected\")")]
    [InlineData("Zero", Keep + "let f = keep<0, i32>\nlet a: [0 of i32] = []\nrequire f(a).length == 0 else => $abort(\"zero\")")]
    [InlineData("Owned", Keep + "let f: ([2 of string]) -> [2 of string] = keep\nlet a = f([\"a\", \"b\"])\nrequire a[1] == \"b\" else => $abort(\"owned\")")]
    [InlineData("Callable", Keep + "func apply<F>(f: ref/F, a: [2 of i32]) -> [2 of i32]\n    F is Callable<([2 of i32]) -> [2 of i32]>\n    return f(a)\nrequire apply(keep, [4, 7])[1] == 7 else => $abort(\"callable\")")]
    [InlineData("Forward", Keep + "func forward<length N>(a: [N of i32]) -> [N of i32]\n    let f = keep<N, i32>\n    return f(a)\nrequire forward([4, 7])[1] == 7 else => $abort(\"forward\")")]
    [InlineData("LengthOnly", "func keep<length N>(a: [N of i32]) -> [N of i32] => a\nlet f = keep<2>\nlet g: ([3 of i32]) -> [3 of i32] = keep\nrequire f([4, 7])[1] + g([1, 2, 3])[2] == 10 else => $abort(\"length only\")")]
    [InlineData("MultipleLengths", "func last<length N, length M>(a: [N of i32], b: [M of i32]) -> [M of i32] => b\nlet f: ([1 of i32], [2 of i32]) -> [2 of i32] = last\nrequire f([1], [2, 3])[1] == 3 else => $abort(\"lengths\")")]
    [InlineData("ResultOnly", "func create<length N>() -> [N of i32] => $abort(\"unused\")\nlet f: () -> [3 of i32] = create")]
    [InlineData("Specialized", "func pick<length N, T>(a: ref/[N of T]) -> i32 => 0\nspecialize func pick<3, i32>(a: ref/[3 of i32]) -> i32 => 1\nlet f = pick<3, i32>\nlet g: (ref/[2 of i32]) -> i32 = pick\nlet a: [3 of i32] = [1, 2, 3]\nlet b: [2 of i32] = [1, 2]\nrequire f(a@ref) == 1 and g(b@ref) == 0 else => $abort(\"specialization\")")]
    [InlineData("GenericContainer", "struct Box<T>\n    public func keep<length N>(a: [N of T]) -> [N of T] => a@move\nlet f = Box<i32>.keep<2>\nrequire f([4, 7])[1] == 7 else => $abort(\"container\")")]
    [InlineData("KindSelection", "func pick<T>(a: T) -> i32 => 1\nfunc pick<length N>(a: [N of i32]) -> i32 => 2\nlet f = pick<2>\nlet g = pick<i32>\nrequire f([1, 2]) == 2 and g(1) == 1 else => $abort(\"kind\")")]
    [InlineData("ForwardErased", Keep + "func forward<length N>(a: [N of i32]) -> [N of i32]\n    let f: ([N of i32]) -> [N of i32] = keep\n    return f(a)\nrequire forward([4, 7])[1] == 7 else => $abort(\"erased forward\")")]
    [InlineData("Compound", "func keep<length N>(a: [(N * 2) of i32]) -> [(N * 2) of i32] => a\nlet f = keep<2>\nrequire f([1, 2, 3, 4])[3] == 4 else => $abort(\"compound\")")]
    [InlineData("Formation", "func keep<length N>(a: [(N - 1) of i32]) -> [(N - 1) of i32] => a\nlet f = keep<1>\nlet a: [0 of i32] = []\nrequire f(a).length == 0 else => $abort(\"formation\")")]
    [InlineData("Effect", Keep + "func invoke<length N, F>(f: ref/F, a: [N of i32]) -> [N of i32]\n    F is Callable<([N of i32]) -> [N of i32]>\n        effect confined\n    return f(a)\nlet a: [2 of i32] = [4, 7]\nrequire invoke(keep<2, i32>, a)[1] == 7 else => $abort(\"effect\")")]
    public void ReferencesPreserveLengthBindingsThroughCallsAndErasure(string name, string source)
        => ScalarEmissionTest.EmitFixture("LengthFunctionReference" + name, source, string.Empty);

    [Fact]
    public void ItemIdentityIncludesNormalizedLengths()
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let a = keep<2, i32>\nlet b = keep<(1 + 1), i32>\nlet c = keep<3, i32>");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var items = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<GenericsKoto>().Where(static x => x.BoundType?.Kind == BoundTypeKind.FunctionItem).Select(static x => x.BoundType!).ToArray();
        Assert.Equal(3, items.Length);
        Assert.Same(items[0], items[1]);
        Assert.NotSame(items[0], items[2]);
        Assert.False(Binding.FitsType(items[0], items[2]));
        Assert.Equal("function item keep<2, i32>", Binding.DiagnosticTypeName(items[0]));
    }

    [Theory]
    [InlineData(Keep + "let f = keep<2>", "keep<2>", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData(Keep + "let f = keep<i32, 2>", "keep<i32, 2>", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData(Keep + "let f = keep", "keep", nameof(DiagnosticCode.UnboundTypeArgument_Kd))]
    [InlineData(Keep + "let f: ([2 of i32]) -> [3 of i32] = keep", "keep", nameof(DiagnosticCode.TypeMismatch_Kd))]
    [InlineData("func f<length N>(a: [N of i32], b: [N of i32]) => ()\nlet g: ([1 of i32], [2 of i32]) -> () = f", "f", nameof(DiagnosticCode.TypeMismatch_Kd))]
    [InlineData("func f<length N>(a: [(N * 2) of i32]) => ()\nlet g: ([4 of i32]) -> () = f", "f", nameof(DiagnosticCode.TypeMismatch_Kd))]
    [InlineData("func first<length N, T>(a: T, b: [N of i32]) -> T => a@move\nlet f: (ref/i32, [2 of i32]) -> ref/i32 = first", "first", nameof(DiagnosticCode.MissingOriginBinding_Kd))]
    public void InvalidReferencesKeepLocatedPublicDiagnostics(string source, string range, string code)
    {
        var path = Path.GetFullPath("LengthReference.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(code, error.Code);
        Assert.Equal(range, error.Text);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        Assert.Equal(record.Code, sent.Code);
        Assert.Equal(record.Display!.Range, sent.Range);
    }

    [Theory]
    [InlineData("keep<(-1), i32>")]
    [InlineData("keep<(1 / 0), i32>")]
    [InlineData("keep<9223372036854775808, i32>")]
    public void InvalidLengthsStopReferenceFormation(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let f = " + expression);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(nameof(DiagnosticCode.InvalidTypeFormation_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("func f<length N>(a: [(N - 1) of i32]) => ()\nlet g = f<0>")]
    [InlineData("func f<length N>() -> [(N + 1) of i32] => $abort(\"unused\")\nlet g = f<9223372036854775807>")]
    [InlineData("func f<length N>(a: [(N - 1) of i32]) => ()\nfunc forward<length M>()\n    let g = f<M>")]
    public void BoundSignaturesMustHaveProvenLengths(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void EditingALengthRevokesThePreviousCallPlan()
    {
        const string Source = Keep + "let f = keep<2, i32>\n_ = f([4, 7])";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
        var reference = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<GenericsKoto>().Single();
        var donor = MinimalEmissionTest.Analyze(Source.Replace("keep<2, i32>", "keep<3, i32>", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<GenericsKoto>().Single();
        Assert.True(KotoHelper.Replace(reference.Parent!, reference, replacement));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData("let f: ([2 of i32]) -> [3 of i32] = keep")]
    [InlineData("let f = keep<i32, 2>")]
    public void RepeatedReferenceFailuresReuseScratch(string use)
    {
        var c = MinimalEmissionTest.Analyze(Keep + use);
        Assert.False(c.Binding.Result.IsComplete);
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete));
        Assert.True(rejected);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmLengthReferencesReuseBindingOwnershipAndEmission()
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let f: ([2 of i32]) -> [2 of i32] = keep\n_ = f([4, 7])");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
