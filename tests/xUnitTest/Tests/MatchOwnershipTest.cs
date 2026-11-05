// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class MatchOwnershipTest
{
    [Theory]
    [InlineData("func f(x: (i32, i32)) => match x\n    (let a, _) => ()")]
    [InlineData("func f(x: Option<(i32, i32)>) => match x\n    .Some((let a, _)) => ()\n    .None => ()")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(let n) => n\n    .None => 0")]
    [InlineData("func f(x: Option<i32>) -> i32\n    return match x\n        .Some(let n)\n            yield n\n        .None\n            yield 0")]
    [InlineData("func f(x: Option<string>) => match x@move\n    .Some(let text) => Console.writeLine(text)\n    .None => ()")]
    [InlineData("func f(x: Option<Option<string>>) => match x@move\n    .Some(.Some(let text)) => Console.writeLine(text)\n    .Some(_) => ()\n    .None => ()")]
    [InlineData("func f(x: Option<string>) -> string => match x@move\n    .Some(let s) => s@move\n    .None => \"empty\"")]
    [InlineData("func f(x: Option<string>) -> string => match x@move\n    .Some(let s)\n        return s@move\n    .None\n        return \"empty\"")]
    [InlineData("func f(x: Option<Option<i32>>) -> i32 => match x\n    .Some(let inner) => match inner\n        .Some(let n) => n\n        .None => 0\n    .None => 0")]
    [InlineData("func f(x: bool) -> i32 => match x\n    true => 1\n    false => 0")]
    [InlineData("func f(x: ()) -> i32 => match x\n    () => 1")]
    [InlineData("func f(x: string) -> i32 => match x@move\n    \"yes\" => 1\n    _ => 0")]
    [InlineData("func f(x: u128) -> i32 => match x\n    340282366920938463463374607431768211455 => 1\n    _ => 0")]
    [InlineData("func f(x: char) -> i32 => match x\n    'A' => 1\n    _ => 0")]
    [InlineData("func f(x: Option<i8>) -> i8 => match x\n    (Option<i8>.Some(let n,)) => n\n    (::Kimi.Option<i8>.None) => -128")]
    [InlineData("func f(x: Option<i32>) -> i32 => match x@move\n    .Some(var n)\n        n = 2\n        yield n\n    .None\n        yield 0")]
    [InlineData("func f()\n    match Option<string>.Some(\"text\")@move\n        .None\n            ()\n        .Some(_) => ()")]
    [InlineData("func f(c: bool)\n    while c\n        match Option<string>.Some(\"text\")@move\n            .Some(let s)\n                Console.writeLine(s)\n                continue\n            .None\n                exit")]
    [InlineData("func f(c: bool, x: Option<string>)\n    match x@move\n        .Some(let s)\n            while c\n                continue\n            Console.writeLine(s)\n        .None\n            ()")]
    [InlineData("func f(x: string) => match x@move\n    let s => Console.writeLine(s)")]
    [InlineData("func f(x: Option<i32>)\n    match x\n        let copy\n            ()\n    let again = x")]
    [InlineData("enum E\n    One(string)\n    Two(string, string)\nfunc f(x: E) -> string => match x@move\n    .One(let a) => a@move\n    .Two(_, let b) => b@move")]
    [InlineData("func f(x: Option<string>) -> Option<string> => match x@move\n    .Some(let s) => .Some(s@move)\n    .None => .None")]
    [InlineData("func f(x: bool) -> i32 => match x\n    true => match (yield 1)\n        _ => 2\n    false => 0")]
    [InlineData("func f<T>(x: ref/T) => match x\n    let r => ()")]
    [InlineData("func f(x: ref/i32 during static) => match x\n    let r => ()")]
    // SPEC 15.1.6: a bare owned Subject is shared-borrowed, even a generic one, so no unknown Copy/Move acquisition is needed.
    [InlineData("func f<T>(x: T) => match x\n    let value => ()")]
    [InlineData("func f<T>(x: T) => match x\n    _ => ()")]
    public void SupportedMatchesVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("func f(s: string)\n    match s@move\n        _\n            ()\n    Console.writeLine(s)")]
    [InlineData("func f(s: string)\n    match s@move\n        \"yes\"\n            ()\n        _ => ()\n    Console.writeLine(s)")]
    [InlineData("func f(x: Option<string>)\n    match x@move\n        .Some(let s)\n            _ = s@move\n            Console.writeLine(s)\n        .None\n            ()")]
    public void AcquisitionStillRejectsMovedUses(string source)
    {
        var c = Parse(source);
        Assert.False(c.Analyze().IsVerified);
        Assert.Contains(c.Problems, i => i.Code == DiagnosticCode.MovedPlace_Kd);
    }

    // An arm that a warning covers still runs, so its Move joins the moved state: the later Move is the MIR's MovedPlace_Kd at `s`
    // (H5, DECISIONS R5-M185), beside Binding's warning.
    [Fact]
    public void WarningCoveredArmParticipatesInMovedStateJoin()
    {
        const string Source = "func f(x: i32)\n    let s = \"text\"\n    match x\n        _\n            ()\n        0\n            _ = s@move\n    let again = s@move";
        var c = Parse(Source);
        Assert.Single(c.Binding.PatternWarnings);
        Assert.False(c.Analyze().IsVerified);
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal((nameof(DiagnosticCode.MovedPlace_Kd), MirTargetSide.At("again = s@move", "s").Resolve(Source)), (record.Code, record.Span));
    }

    [Fact]
    public void DiscardedMatchStillRequiresExhaustiveness()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: Option<string>)\n    match x@move\n        .Some(let s) => Console.writeLine(s)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, i => i.Code == DiagnosticCode.NonExhaustiveMatch_Kd);
        c.Analyze();
        Assert.False(c.Accepts); // Since the MIR decides acceptance (R6-check H5), Binding's record rejects the program.
    }

    [Fact]
    public void YieldToIfIsNotRedirectedIntoTheMatchFrame()
    {
        var c = Parse("func f(x: bool) -> i32 => match x\n    true => (if x\n        yield 1\n    else\n        yield 2\n    )\n    false => 0");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Empty(c.Problems);
    }

    // A match may precede another call argument (its MIR: tests/mir/matches.kimi `precedes`), whatever the newlines.
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void MatchMayPrecedeAnotherCallArgument(string newline)
    {
        const string Source = "func consume(a: i32, b: i32) => ()\nfunc f(x: bool)\n    consume(\n        match x\n            true => 1\n            false => 0\n        , 2\n    )";
        var c = Parse(Source.Replace("\n", newline, StringComparison.Ordinal));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Equal(MirDumpTest.Dump(Parse(Source)), MirDumpTest.Dump(c));
    }

    [Fact]
    public void DedentDoesNotReplaceTheRequiredArgumentComma()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: bool)\n    consume(\n        match x\n            true => 1\n            false => 0\n        2\n    )");
        Assert.NotEmpty(TestDiagnostics.Of(c));
    }

    [Fact]
    public void ChangedPayloadStorageRebuildsMatchAndDecompositionPlans()
    {
        var c = Parse("enum E\n    One(i32)\nfunc f(x: E) => match x@move\n    .One(let a) => ()\n    _ => ()");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var originalCases = AdtDef.CaseTypes(SubjectType(c))!;
        c.Kotonoha.CreateCodeContext().Parse(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<EnumKoto>().Single(), "Borrowed(ref/i32 during static)");
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var rebuilt = SubjectType(c);
        Assert.Single(originalCases);
        Assert.NotSame(originalCases, AdtDef.CaseTypes(rebuilt));
        Assert.Equal(2, AdtDef.CaseTypes(rebuilt)!.Length);
    }

    // DECISIONS BU-14 (SPEC 13.5.5, 15.1.6): a Binding that may borrow a part of the owned Subject or item past a pair layer admitting
    // owner, or that selects a pair layer, was the old path's located Unsupported_Kd at its match or loop, although Binding accepts it and
    // the MIR builds it. Since the MIR decides acceptance (R6-check H5; 1a, the owner 2026-11-03), check accepts it with no record and
    // generation emits `f`, as the instance `main` calls.
    [Theory]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is owner or uniq\n    match pair@move\n        (let x, _) => use(x)", "f((1, 2))")]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is uniq\n    match pair@move\n        (let x, _) => use(x)", "var n: i32 = 1\n    f((n@uniq, 2))")]
    [InlineData("func f<s/T>(o: s/Option<i32>, marker: s/T)\n    s is owner or uniq\n    match o@move\n        .Some(let n) => n@follow = 5\n        .None => ()", "f(Option<i32>.Some(1), 0)")]
    [InlineData("func f<s/T>(arr: Array<(s/T, i32)>)\n    s is owner or uniq\n    for (x, _) in arr@move => use(x)", "var a: Array<(i32, i32)> = [(1, 7)]\n    f(a@move)")]
    [InlineData("func f<s/T>(items: Array<s/(i32, i32) during a>, marker: s/T)\n    s is owner or uniq\n    for (x, y) in items@move => use(x)", "var a: Array<(i32, i32)> = [(1, 7)]\n    f(a@move, 0)")]
    public void APairLayerBorrowIntoAnOwnedSubjectIsGenerated(string source, string call)
    {
        var c = MinimalEmissionTest.Analyze("func use<T>(value: ref/T) => ()\n" + source + "\npublic func main()\n    " + call + "\n");
        Assert.True(c.Accepts, MinimalEmissionTest.Describe(c));
        Assert.Empty(c.Problems);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var set = c.Instances!;
        Assert.Contains(Enumerable.Range(0, set.Items.Length), x => set.Items[x].Key.Kind == MonoKind.Body && set.IsGenerated(x) && set.Items[x].Key.Body.Def.Decl?.Name == "f");
    }

    // A borrowed path's pair layer, a Wildcard (a for slot `_` included, SPEC 14.8.1) and a structural position that only tests were never
    // the old path's limit (BU-14).
    [Theory]
    [InlineData("func f<s/T>(items: Array<s/(i32, i32) during a>, marker: s/T)\n    s is owner or uniq\n    for (x, y) in items => use(x)")]
    [InlineData("func f<s/T>(o: s/Option<i32>, marker: s/T)\n    s is uniq\n    match o@move\n        .Some(let n) => n@follow = 5\n        .None => ()")]
    [InlineData("func f<s/T>(pair: (s/T, i32)) -> i32\n    s is owner or uniq\n    match pair@move\n        (_, let n) => return n")]
    [InlineData("func f<s/T>(pair: (s/T, i32)) -> i32\n    s is owner\n    match pair@move\n        (_, let n) => return n")]
    [InlineData("func f<s/T>(o: s/Option<T>, marker: s/T) -> bool\n    s is owner or uniq\n    match o@move\n        .Some(_) => return true\n        .None => return false")]
    [InlineData("func f<s/T>(value: s/T) -> i32\n    s is owner or uniq\n    match value@move\n        _ => return 3")]
    [InlineData("struct Holder<E>\n    public computed p: (E, i32)\n        get(self: ref/Self) -> (E, i32)\n            $abort(\"p\")\nfunc f<s/T>(holder: ref/Holder<s/T>)\n    s is owner or uniq\n    match holder.p\n        (_, let n) => use(n)")]
    [InlineData("func f<s/T>(arr: Array<(s/T, i32)>) -> i32\n    s is owner or uniq\n    var total: i32 = 0\n    for (_, n) in arr@move => total += n\n    return total")]
    public void APairLayerThatOnlyTestsOrBorrowsIsNoLimit(string source)
    {
        var c = MinimalEmissionTest.Analyze("func use<T>(value: ref/T) => ()\n" + source + "\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.Unsupported_Kd);
    }

    // The bound Type of the match Subject `x@move`.
    private static BoundType SubjectType(Compilation c)
        => KotoTree.Walk(c.Kotonoha.RootKoto).Single(static x => x.ToString() == "x@move").TypeOf()!;

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
        public void WarmMatchOwnershipAndBindingReuseStorage(int count)
        {
            var source = new System.Text.StringBuilder("func f()\n");
            for (var i = 0; i < count; i++)
            {
                source.Append("    match Option<Option<string>>.Some(.Some(\"text\"))\n        .Some(.Some(let s))\n            Console.writeLine(s)\n        .Some(_)\n            ()\n        .None\n            ()\n");
            }

            var c = Parse(source.ToString());
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
