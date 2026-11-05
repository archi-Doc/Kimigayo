// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class EnumOwnershipTest
{
    [Theory]
    [InlineData("let x = Option<i32>.Some(42)")]
    [InlineData("let x: Option<i32> = .Some(42)")]
    [InlineData("let x = Option<i32>.None")]
    [InlineData("let x: Option<i32> = .None")]
    [InlineData("let x = Result<i32, string>.Err(\"missing\")")]
    [InlineData("let x = Option<Option<i32>>.Some(Option<i32>.Some(1))")]
    [InlineData("let x: Option<Result<i32, string>> = .Some(.Err(\"error\"))")]
    [InlineData("let x = Option<i32>.Some(1)\nlet y = x\nlet z = x")]
    [InlineData("let x = Result<i32, i32>.Ok(1)\nlet y = x@move")]
    [InlineData("func twice<T, E>(r: Result<T, E>)\n    T is Copy\n    E is Copy\n    let a = r\n    let b = r")]
    [InlineData("func echo(x: Option<string>) -> Option<string> => x@move\nlet y = echo(Option<string>.Some(\"text\"))")]
    [InlineData("func echo(x: Option<string>) -> Option<string>\n    return x@move\nlet y = echo(Option<string>.None)")]
    [InlineData("func choose(c: bool) -> Option<string> => if c => Option<string>.Some(\"a\") else => Option<string>.None")]
    [InlineData("func f(c: bool)\n    let x: Option<string>\n    if c\n        x = Option<string>.None\n    else\n        x = Option<string>.Some(\"a\")\n    let y = x@move")]
    [InlineData("enum E\n    Wrapped(())\nlet x = E.Wrapped(())")]
    [InlineData("let x = Option<i8>.Some(-128)")]
    [InlineData("let x = Option<f32>.Some(1.5)")]
    [InlineData("enum E\n    Value(string)\nfunc echo(x: E) -> E => x@move\nlet x: E = echo(E.Value(\"a\"))")]
    public void SupportedEnumProgramsVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    // SPEC 12.2 (R6-check H8a D10): every designator spelling lowers as the contextual one, with no call. The baselines' own MIR is
    // tests/mir/constructors.kimi `designatorBaselines`.
    [Theory]
    [InlineData("E.Full(127)")]
    [InlineData(".Full(127)")]
    [InlineData("(E.Full(127))")]
    [InlineData("(.Full(127))")]
    public void NongenericDesignatorsShareConstructionOperations(string expression)
        => CheckConstruction("enum E\n    Full(i8)\n", "E", expression, ".Full(127)");

    [Theory]
    [InlineData("E<i8>.Full(127)")]
    [InlineData(".Full(127)")]
    [InlineData("(E<i8>.Full(127))")]
    [InlineData("(.Full(127))")]
    public void GenericDesignatorsShareConstructionOperations(string expression)
        => CheckConstruction("enum E<T>\n    Full(T)\n", "E<i8>", expression, ".Full(127)");

    [Theory]
    [InlineData("E<i32>.Empty")]
    [InlineData(".Empty")]
    [InlineData("(E<i32>.Empty)")]
    [InlineData("(.Empty)")]
    public void EmptyDesignatorsNeedNoRuntimeReceiver(string expression)
        => CheckConstruction("enum E<T>\n    Full(T)\n    Empty\n", "E<i32>", expression, ".Empty");

    [Theory]
    [InlineData("let x = Option<string>.Some(\"a\")\nlet y = x@move\nlet z = x@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("let x = Result<i32, i32>.Ok(1)\nlet y = x@move\nlet z = x@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("let x: Option<string>\nlet y = x@move", DiagnosticCode.UninitializedPlace_Kd)]
    [InlineData("let x = Option<string>.None\nx = Option<string>.Some(\"a\")", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("let x = Option<string>.None\nlet y = x@move\nx = Option<string>.None", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("enum E\n    Both(string, string)\nlet s = \"a\"\nlet x = E.Both(s@move, s@move)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let x = Option<string>.None\n    if c\n        let y = x@move\n    let z = x@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let x = Option<string>.None\n    while c\n        let y = x@move", DiagnosticCode.MovedPlace_Kd)]
    public void EnumStateErrorsAreRejected(string source, DiagnosticCode expected)
    {
        var c = Parse(source);
        Assert.False(c.Analyze().IsVerified);
        Assert.Contains(c.Problems, x => x.Code == expected);
    }

    // SPEC 3.5, 3.5.2: an enum with a Non-Copy payload, or an unproven one, is moved only explicitly; each bare read is Binding's
    // TransferRequired_Kd (R6-check H1, DECISIONS BU-84).
    [Theory]
    [InlineData("let x = Result<i32, string>.Ok(1)\nlet y = x", "x")]
    [InlineData("func twice<T, E>(r: Result<T, E>)\n    T is Copy\n    let a = r\n    let b = r", "r|r")]
    public void BareEnumMovesAreBindingsRecords(string source, string places)
        => Assert.Equal(
            places.Split('|').Select(static x => (DiagnosticCode.TransferRequired_Kd, x)),
            MinimalEmissionTest.Analyze(source).Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));

    [Fact]
    public void RebindingChangedStorageInvalidatesTheSupportedTypeCache()
    {
        var c = Parse("struct S<T>\n    var value: T\nenum E\n    Empty\nlet x = E.Empty");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var originalCases = AdtDef.CaseTypes(TypeOfX(c))!;
        c.Kotonoha.CreateCodeContext().Parse(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<EnumKoto>().Single(), "Again(ref/i32 during static)");
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Single(originalCases);
        var rebuiltCases = AdtDef.CaseTypes(TypeOfX(c))!;
        Assert.NotSame(originalCases, rebuiltCases);
        Assert.Equal(2, rebuiltCases.Length);
        var reference = Assert.Single(rebuiltCases[1]!.Components);
        Assert.Equal(SemanticsKind.Ref, reference.Semantics);
        Assert.Equal(OriginKind.Static, reference.Origin!.Kind);
    }

    // SPEC 12.2, 15.2: complete shared-reference payloads retain their Origin, including Static.
    [Theory]
    [InlineData("func f(x: ref/i32 during static) -> Option<ref/i32 during static> => .Some(x)")]
    [InlineData("func f() -> Option<ref/i32 during static> => .None")]
    public void StaticReferencePayloadsUseCompleteEnumStorage(string source)
    {
        var c = Parse(source);
        var result = c.Analyze();
        Assert.True(result.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // SPEC 10.2, 12.2: implicit borrowed payloads use the ordinary borrow/reborrow acquisition and retain their Loans.
    [Theory]
    [InlineData("enum V<T> {a}\n    Some(ref/T during a)\nfunc f()\n    var n = 1\n    let v = V<i32>.Some(n)")]
    [InlineData("func f(x: uniq/i32) -> Option<uniq/i32 during x> => .Some(x)")]
    public void BorrowedPayloadAcquisitionsVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void CommonFunctionPayloadHasOwnedStorage()
    {
        var c = Parse("struct S<T>\n    var value: T\nenum E\n    Empty\n    Full(S<(i32) -> i32>)\nlet x = E.Empty");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void SymbolicEnumPayloadHasUniversalOwnership()
    {
        var c = Parse("func f<T>(x: T) -> Option<T> => .Some(x@move)");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    // One spelling's top-level MIR body has no call and, without its sites, equals the baseline spelling's (R6-check H8a D10).
    private static void CheckConstruction(string declarations, string type, string expression, string baseline)
        => Assert.Equal(TopLevel(declarations + $"let x: {type} = {baseline}"), TopLevel(declarations + $"let x: {type} = {expression}"));

    // A verified program's top-level MIR body, which has no call, without its sites.
    private static string TopLevel(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var program = c.BuildMir();
        var ordinal = Array.FindIndex(program.Bodies.ToArray(), static x => x.Key.Decl is null && x.Source.Module == 0);
        Assert.DoesNotContain(program.Bodies[ordinal].Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.Call);
        return MirDumpTest.Unsited(program, ordinal);
    }

    // The bound Type of the local `x`.
    private static BoundType TypeOfX(Compilation c)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().Single(static x => x.NameKoto.IdentifierName == "x").TypeOf()!;

    private static Compilation Parse(string source)
        => CompilationTestHelper.BindSuccess(source);

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(32)]
        [InlineData(128)]
        public void WarmEnumAnalysisReusesStateAndPlans(int count)
        {
            var source = "func use(c: bool)\n" + string.Join('\n', Enumerable.Range(0, count).Select(i => $"    var x{i} = Option<Option<string>>.Some(Option<string>.Some(\"a\"))\n    if c\n        let y{i} = x{i}@move"));
            var c = Parse(source);
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Analyze()));
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                c.Bind();
                c.Analyze();
            }));
            c.Bind();
            Assert.False(c.Analysis.IsVerified);
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        }
    }
}
