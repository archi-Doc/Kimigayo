// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class LocalRegionCompletionTest
{
    private const string None = "func none() -> Option<ref/i32 during source> => .None\n";
    private const string Slots = None + "var a = none()\nvar b = none()\nvar slot = a@uniq\nslot = b@uniq\nvar n = 2\n";
    private const string Empty = "match a\n    .Some(_) => $abort(\"a\")\n    .None => ()\nmatch b\n    .Some(_) => $abort(\"b\")\n    .None => ()";
    private const string Joined = "let choose = func [] (flag: bool, a: ref/i32, b: ref/i32)\n    var first = a\n    var second = b\n    return if flag => first else => second\n";

    // IndirectReplacement and IndirectLoop expect the old path's strong update: `slot@follow = .None` ended b's Loan on n.
    // A write through a reference ends none (SPEC 15.6, S4), so `n = 3` conflicts; the default pipeline keeps the old rule
    // until R6-check.
    [Theory]
    [Trait("OldRule", "S4")]
    [InlineData("LocalFunctionTuple", "let f: ((ref/i32, i32)) -> i32 = func (pair) => pair.0@follow + pair.1\nlet a = 3\nlet b = 4\nrequire f((a@ref, 2)) + f((b@ref, 1)) == 10 else => $abort(\"tuple\")")]
    [InlineData("LocalFunctionNested", "let f: (ref/(ref/i32)) -> i32 = func (value) => value@follow@follow\nlet n = 7\nlet view = n@ref\nrequire f(view@ref) == 7 else => $abort(\"nested\")")]
    [InlineData("LocalFunctionItem", "func read(pair: (ref/i32 during source, i32)) -> i32 => pair.0@follow + pair.1\nlet f: ((ref/i32, i32)) -> i32 = read\nlet n = 3\nrequire f((n@ref, 2)) == 5 else => $abort(\"item\")")]
    [InlineData("JoinedResult", Joined + "let a = 3\nlet b = 4\nrequire choose(false, a@ref, b@ref)@follow == 4 else => $abort(\"join\")")]
    [InlineData("IndirectReplacement", Slots + "slot@follow = .Some(n@ref)\nslot@follow = .None\nn = 3\n" + Empty)]
    [InlineData("IndirectGeneric", "func put<T>(slot: uniq/T, value: T) => slot@follow = value@move\n" + Slots + "put(slot, .Some(n@ref))\nb = .None\nn = 3\n" + Empty)]
    [InlineData("NestedNamedFinite", "func use() -> i32\n    var a = 1\n    var b = 2\n    let root = a@ref\n    func read(value: ref/i32 during root) -> i32 => value@follow\n    return read(b@ref)\nrequire use() == 2 else => $abort(\"finite\")")]
    [InlineData("IndirectBranch", "func none() -> Option<ref/i32 during source> => .None\nvar a = none()\nvar b = none()\nvar c = none()\nvar slot = c@uniq\nlet flag = true\nif flag\n    slot = a@uniq\nelse\n    slot = b@uniq\nvar n = 2\nslot@follow = .Some(n@ref)\na = .None\nb = .None\nn = 3\nmatch c\n    .Some(_) => $abort(\"old target\")\n    .None => ()")]
    [InlineData("IndirectLoop", "func none() -> Option<ref/i32 during source> => .None\nvar a = none()\nvar b = none()\nvar slot = a@uniq\nvar i = 0\nwhile i < 2\n    slot = b@uniq\n    i += 1\nslot = b@uniq\nvar n = 2\nslot@follow = .Some(n@ref)\nslot@follow = .None\nn = 3\nmatch a\n    .Some(_) => $abort(\"old target\")\n    .None => ()\nmatch b\n    .Some(_) => $abort(\"new target\")\n    .None => ()")]
    public void CompletedRegionsComposeWithoutReopeningTheirType(string name, string source)
    {
        ScalarEmissionTest.EmitFixture("LocalRegionCompletion" + name, source, string.Empty);

        // The MIR side: S4's conflicts. A Function Type annotation's omitted inner Origins form no row of the evaluating body (its
        // signature's premise, proven at each call; PLAN G216, DECISIONS R4-M137), so LocalFunctionNested has no located limit.
        (DiagnosticCode, SyntaxLocation)[] mir = name switch
        {
            "IndirectReplacement" or "IndirectLoop" => [(DiagnosticCode.ComparisonLoanConflict_Kd, new("n = 3"))],
            _ => [],
        };
        MirTargetSide.Check(source, mir);
    }

    [Theory]
    [InlineData(Joined + "var a = 3\nvar b = 4\nlet value = choose(false, a@ref, b@ref)\nb = 5\nrequire value@follow == 4 else => $abort(\"join\")", "b = 5")]
    [InlineData(Slots + "slot@follow = .Some(n@ref)\nn = 3\nmatch b\n    .Some(let value) => require value@follow == 2 else => $abort(\"live\")\n    .None => ()", "n = 3")]
    [InlineData(Slots + "slot@follow = .Some(n@ref)\nlet old = b\nslot@follow = .None\nn = 3\nmatch old\n    .Some(let value) => require value@follow == 2 else => $abort(\"copy\")\n    .None => ()", "n = 3")]
    [InlineData("func first<T>(pair: uniq/(T, T)) -> uniq/T during pair => pair.0@uniq\nvar a = 1\nvar b = 2\nvar pair = (a@ref, a@ref)\nfirst(pair@uniq)@follow = b@ref\na = 3\nrequire pair.1@follow == 1 else => $abort(\"sibling\")", "a = 3")]
    public void CompletionPreservesEveryRemainingLoan(string source, string at)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == at);
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    [Theory]
    [InlineData("var f: ((ref/i32, i32)) -> i32")]
    [InlineData("func use(f: ((ref/i32, i32)) -> i32) => ()\npublic func main() => ()")]
    public void InputOmissionStillNeedsAnInitializedLocalContext(string source)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == nameof(DiagnosticCode.MissingOriginBinding_Kd));
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    // SPEC 15.3.6, 15.4.4 (DECISIONS R4-M112 (c)): a variable of the local's Type that no fit reaches, here the slot of a projection
    // base that the projection's normalization drops (`Item` is i32), has no candidate and is the local's region; no Inference atom of
    // the local stays unresolved, on either pipeline. The unqualified spelling forms the same variable (SPEC 15.4.2).
    [Theory]
    [InlineData("S.(Src).Item")]
    [InlineData("S.Item")]
    public void AnAnnotationVariableTheDeclaredTypeDropsIsTheLocalsRegion(string written)
    {
        var source = "contract Src\n    associate Item\n    func read(self: ref/Self) -> Self.Item\nstruct S {s}\n    Self is Src\n    public let v: ref/i32 during s\n" +
            "    public init(v: ref/i32 during s) => self.v = v\n    public func read(self: ref/Self) -> i32 => 1\nfunc f() -> i32\n    let w: " + written + " = 5\n    return w\n" +
            "public func main() => ()\n";
        Assert.Empty(ContextualCaptureTest.PublishedRecords(source));

        var c = MinimalEmissionTest.Analyze(source);
        var local = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "w");
        var bases = KotoTree.Walk(local.TypeKoto!).Select(static x => x.TypeOf()).Where(static x => x?.Symbol?.Name == "S").ToArray();
        Assert.NotEmpty(bases);
        foreach (var type in bases)
        {
            var slot = Assert.Single(type!.OriginArguments);
            Assert.True(Binding.IsLocalRegion(slot) && ReferenceEquals(slot.Binder, local), slot.Kind + " " + slot.Binder);
        }
    }

    // The first row expects the old path's strong update through `slot`, as above (S4).
    [Theory]
    [InlineData(Slots + "slot@follow = .Some(n@ref)\nslot@follow = .None\nn = 3\n" + Empty, "n = 3")]
    [InlineData("func read(pair: (ref/i32 during source, i32)) -> i32 => pair.0@follow + pair.1\nlet f: ((ref/i32, i32)) -> i32 = read\nlet n = 3\n_=f((n@ref, 2))")]
    [Trait("Purpose", "Allocation")]
    [Trait("OldRule", "S4")]
    public void RepeatedCompletionReusesItsStorage(string source, string? mir = null)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
        MirTargetSide.Check(c, mir is null ? [] : [(DiagnosticCode.ComparisonLoanConflict_Kd, new(mir))]);
    }
}
