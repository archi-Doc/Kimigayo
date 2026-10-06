// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

#pragma warning disable SA1117, SA1118 // Multiline language fixtures.

/// <summary>SPEC 4.3.4: only initial stores are skipped; all subsequent operations use ordinary initialized-array rules.</summary>
public class NoInitTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("i8", "1")]
    [InlineData("u8", "1")]
    [InlineData("i16", "1")]
    [InlineData("u16", "1")]
    [InlineData("i32", "1")]
    [InlineData("u32", "1")]
    [InlineData("i64", "1")]
    [InlineData("u64", "1")]
    [InlineData("i128", "1")]
    [InlineData("u128", "1")]
    [InlineData("isize", "1")]
    [InlineData("usize", "1")]
    [InlineData("Wrapping<i32>", "1")]
    [InlineData("f32", "1.5")]
    [InlineData("f64", "1.5")]
    [InlineData("bool", "true")]
    [InlineData("char", "'a'")]
    public void DirectScalarElementsCanBeWrittenThenRead(string type, string value)
        => ScalarEmissionTest.EmitFixture(
            "NoInit" + type.Replace("<", string.Empty).Replace(">", string.Empty),
            $"unsafe\n    var values: [2 of {type}] = noinit\n    values[0] = {value}\n    values[1] = {value}\n    require values[0] == {value} and values[1] == {value} else => $abort(\"values\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData("Generic", "func make<length N, T>(value: T) -> [N of T]\n    T is PrimitiveInteger\n    unsafe\n        var values: [N of T] = noinit\n        for index in values.indices => values[index] = value\n        return values\nlet result = make<3, i64>(7)\nrequire result[2] == 7 else => $abort(\"generic\")")]
    [InlineData("WrappingGeneric", "func make<T>(value: Wrapping<T>) -> [2 of Wrapping<T>]\n    T is PrimitiveInteger\n    unsafe\n        var values: [2 of Wrapping<T>] = noinit\n        values[0] = value\n        values[1] = value\n        return values\nlet result = make<i32>(7)\nrequire result[1] == 7 else => $abort(\"generic\")")]
    [InlineData("Alias", "alias K => Kimi\nunsafe\n    var values: [1 of K.Wrapping<u8>] = noinit\n    values[0] = 9\n    require values[0] == 9 else => $abort(\"alias\")")]
    [InlineData("Zero", "unsafe\n    var values: [0 of bool] = noinit\n    require values.length == 0 else => $abort(\"zero\")\n    let copy = values\n    require copy.length == 0 else => $abort(\"copy\")")]
    [InlineData("ReferenceWriter", "func set(values: uniq/[2 of bool])\n    values[0] = true\n    values[1] = false\nunsafe\n    var values: [2 of bool] = noinit\n    set(values@uniq)\n    require values[0] and not values[1] else => $abort(\"borrow\")")]
    [InlineData("RawWriter", "unsafe\n    var values: [2 of char] = noinit\n    let pointer = values[0]@raw\n    pointer[0] = 'a'\n    pointer[1] = 'b'\n    require values[1] == 'b' else => $abort(\"raw\")")]
    [InlineData("ParenthesizedName", "let noinit: [2 of i32] = [1, 2]\nlet values = (noinit)\nrequire values[1] == 2 else => $abort(\"name\")")]
    [InlineData("QualifiedName", "group Names\n    public let noinit: i32 = 7\nlet value = Names.noinit\nrequire value == 7 else => $abort(\"name\")")]
    public void OrdinaryLoansCallsAndNamesKeepTheirMeaning(string name, string source)
        => ScalarEmissionTest.EmitFixture("NoInit" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData("unsafe\n    let values: [1 of i32] = noinit", "local var")]
    [InlineData("unsafe\n    var values = noinit", "explicit fixed-array")]
    [InlineData("unsafe\n    var values: Array<i32> = noinit", "explicit fixed-array")]
    [InlineData("unsafe\n    var values: [_ of i32] = noinit", "hole")]
    [InlineData("unsafe\n    var values: [2 of _] = noinit", "hole")]
    [InlineData("unsafe\n    var values: [2 of [2 of i32]] = noinit", "owner Scalar")]
    [InlineData("unsafe\n    var values: [2 of raw/i32] = noinit", "owner Scalar")]
    [InlineData("unsafe\n    var values: [0 of ref/i32 during static] = noinit", "owner Scalar")]
    [InlineData("func f(input: uniq/i32)\n    unsafe\n        var values: [2 of uniq/i32 during input] = noinit", "owner Scalar")]
    [InlineData("unsafe\n    var values: [2 of ()] = noinit", "owner Scalar")]
    [InlineData("unsafe\n    var values: [2 of string] = noinit", "owner Scalar")]
    [InlineData("struct S\n    Self is Copy\nunsafe\n    var values: [2 of S] = noinit", "owner Scalar")]
    [InlineData("func make<T>()\n    T is Copy\n    unsafe\n        var values: [2 of T] = noinit", "owner Scalar")]
    [InlineData("group G\n    public var values: [2 of i32] = noinit", "local var")]
    [InlineData("struct S\n    var values: [2 of i32] = noinit", "local var")]
    [InlineData("let noinit = 7\nlet value = noinit", "local var")]
    public void IneligibleDirectivesHaveOneFactualError(string source, string reason)
    {
        var diagnostics = DiagnosticCorpus.Check(source + "\n()\n").Diagnostics;
        var error = Assert.Single(diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.InvalidNoInit_Kd), error.Code);
        Assert.Equal("noinit", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains(error.Reason!, x => x.Value.Contains(reason, StringComparison.Ordinal));
        Assert.Null(error.Repairs);
        Assert.NotEmpty(error.Related!);
        Assert.DoesNotContain(diagnostics, static x => x.Code == nameof(DiagnosticCode.UnnecessaryUnsafeBlock_Kd));
    }

    [Fact]
    public void TheDirectiveUsesUnsafePermissionWithoutOfferingAnUnconditionalRepair()
    {
        var error = Assert.Single(DiagnosticCorpus.Check("var values: [2 of i32] = noinit").Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsafeBlockRequired_Kd), error.Code);
        Assert.Equal(6, error.Span!.Value.Length);
        Assert.Null(error.Repairs);
        Assert.Empty(DiagnosticCorpus.Check("unsafe\n    var values: [2 of i32] = noinit").Diagnostics);
    }

    [Theory]
    [InlineData("unsafe\n    var values: [2 of string] = noinit", "InvalidNoInit_Kd", "owner Scalar is not proven for element string")]
    [InlineData("var values: [2 of bool] = noinit", "UnsafeBlockRequired_Kd", "Unsafe Block")]
    public void PublicAdaptersKeepTheDirectiveLocationAndReason(string source, string code, string reason)
    {
        var check = DiagnosticCorpus.Check(source);
        var error = Assert.Single(check.Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal("noinit", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Null(error.Repairs);
        DiagnosticOutputTestHelper.Single(check, reason, output);
    }

    [Theory]
    [InlineData("unsafe\n    var values: [2 of Missing] = noinit", "UnresolvedBinding_Kd")]
    [InlineData("unsafe\n    var values: [2 of string] = noinit\nlet wrong: i32 = true", "InvalidNoInit_Kd,TypeMismatch_Kd")]
    public void PrerequisiteAndIndependentErrorsStayDistinct(string source, string codes)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(codes.Split(',').Order(StringComparer.Ordinal), errors.Select(static x => x.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SyntaxSnapshotsRetainTheDirectiveAndResolvedType()
    {
        var c = MinimalEmissionTest.Analyze("unsafe\n    var values: [2 of i32] = noinit");
        var directive = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<NoInitKoto>());
        Assert.Equal("noinit", directive.ToString());
        Assert.Equal(6, directive.Span.Length);
        Assert.Equal(2, directive.BoundType!.Length);
        var copy = CompilationTestHelper.Reload(c);
        Assert.True(copy.Bind().IsComplete);
        Assert.Single(KotoTree.Walk(copy.Kotonoha.RootKoto).OfType<NoInitKoto>());
    }

    [Fact]
    public void UnreadStorageGeneratesNoElementLoadsOrStores()
    {
        var c = MinimalEmissionTest.Analyze("unsafe\n    var values: [4096 of bool] = noinit");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error ?? MinimalEmissionTest.Describe(c, null));
        var instructions = module.GetFunction(0).Instructions;
        Assert.DoesNotContain(instructions, static x => x.Opcode is EmissionOpcode.LoadScalar or EmissionOpcode.StoreScalar or EmissionOpcode.FillArray);
        using var output = new StringWriter();
        module.WriteIr(output);
        Assert.DoesNotContain("@__kimi_fill_array", output.ToString(), StringComparison.Ordinal);
    }

    // These programs deliberately violate the programmer's read-before-write obligation. Check acceptance only;
    // never create executable fixtures or make their undefined results part of a test expectation.
    [Theory]
    [InlineData("let value = values[0]")]
    [InlineData("let copy = values")]
    [InlineData("let moved = values@move")]
    public void ReadBeforeWriteHasNoExtraCompilerState(string use)
    {
        var c = MinimalEmissionTest.Analyze("unsafe\n    var values: [2 of bool] = noinit\n    " + use);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error ?? MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void OrdinaryAnnotationOnlyStorageStillNeedsWholeConstruction()
    {
        var c = MinimalEmissionTest.Analyze("var values: [2 of i32]\nvalues[0] = 1");
        Assert.False(c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void ThePublishedExampleRunsUnchanged()
        => ScalarEmissionTest.EmitFixture("NoInitExample", File.ReadAllText(DiagnosticCorpus.RepositoryPath("docs", "examples", "FixedArrays", "FixedArrays.kimi")), "2 7\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmPhasesAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("unsafe\n    var values: [2 of i32] = noinit\n    values[0] = 1\n    values[1] = 2\n    require values[1] == 2 else => $abort(\"value\")");
        var ok = true;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                ok &= c.Bind().IsComplete;
                c.Binding.CheckStartup(OutputKind.Application);
                ok &= c.Ownership.Analyze().IsVerified && c.Emission.WriteIr(TextWriter.Null, out _);
            }, iterations: 8, warmupIterations: 8);
        Assert.True(ok);
        Assert.Equal(0, bytes);
    }
}
