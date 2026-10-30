// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 14.8.1, 13.5.5, 15.1.6, 10.2: the layer step of a Pattern position (Binding.Patterns.Layers). A binding selected
/// through several reference layers depends on the Origins those layers grant: a ref layer restarts the dependency, and each uniq
/// layer below it adds its own. A Binding reached with owned access at a qualifying pair layer binds a reference to the layer's
/// selected Place in the mode past it, whose exclusive owner case lends the owned Subject Place itself.</summary>
public class PatternLayerOriginTest
{
    private const string Parameter = "r: ref/(uniq/Option<i32> during b) during a";

    // A getter result of (E, i32), of E and of a pair application, and two helpers.
    private const string Declarations = "struct Holder<E>\n    public computed p: (E, i32)\n        get(self: ref/Self) -> (E, i32)\n            $abort(\"p\")\n" +
        "struct Single<E>\n    public computed v: E\n        get(self: ref/Self) -> E\n            $abort(\"v\")\n" +
        "struct Applied<s/T> {a}\n    s is value or valueborrow\n    public let anchor: ref/i32 during a\n" +
        "    public computed o: s/Option<i32> during a\n        get(self: ref/Self) -> s/Option<i32> during a\n            $abort(\"o\")\n" +
        "func use<T>(value: ref/T) => ()\nfunc same<T>(a: ref/T, b: ref/T) -> bool => true\n";

    [Fact]
    public void AnExclusiveLayerBelowASharedOneKeepsTheOuterDependency()
    {
        var c = MinimalEmissionTest.Analyze("func f(" + Parameter + ") -> ref/i32 during b\n    match r\n        .Some(let v) => return v\n        .None => $abort(\"none\")");
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(DiagnosticCode.UnprovenOriginRelation_Kd, issue.Code);
        Assert.Empty(c.AnalyzeControlFlow().Issues); // The failed return reports no dependent result mismatch.
    }

    [Theory]
    [InlineData("func f(" + Parameter + ") -> ref/i32 during (a and b)\n    match r\n        .Some(let v) => return v\n        .None => $abort(\"none\")")]
    [InlineData("func f(r: ref/(ref/Option<i32> during b) during a) -> ref/i32 during b\n    match r\n        .Some(let v) => return v@follow@ref\n        .None => $abort(\"none\")")]
    public void TheMeetOfTheGrantingLayersIsAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 13.5.5 ("a Pattern never binds by value past a pair layer"), 14.8.1, 15.1.6, 14.6.2: reached with owned access, a Binding
    // at a qualifying pair layer binds ref/T or uniq/T of the layer's selected Place, also a whole Subject, a for slot of an owned item
    // and its guard candidate; elsewhere a Binding selects nothing (a shared path, a try's value, SPEC 17.3, and an admitted set
    // with Object Semantics, DECISIONS BU-14). The rows marked dev failed before: an owned Subject's pair step had no Origin, and
    // the for decomposition selected no pair layer.
    [Theory]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is owner or uniq\n    match pair@move\n        (let x, _) => use(x)", "uniq/T")]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is owner or ref\n    match pair@move\n        (let x, _) => use(x)", "ref/T")]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is owner\n    match pair@move\n        (let x, _) => use(x)", "uniq/T")]
    [InlineData("func f<s/T>(value: s/T)\n    s is owner or uniq\n    match value@move\n        let x => use(x)", "uniq/T")]
    [InlineData("func f<s/T>(holder: ref/Holder<s/T>)\n    s is uniq\n    match holder.p\n        (let x, _) => use(x)", "uniq/T")]
    [InlineData("func f<s/T>(holder: ref/Holder<s/T>)\n    s is owner or ref\n    match holder.p\n        (let x, _) => use(x)", "ref/T")]
    [InlineData("func f<s/T>(pair: (s/T, i32), value: T)\n    s is owner or uniq\n    T is Copy\n    match pair@move\n        (let x, _) => x@follow = value", "uniq/T")] // dev
    [InlineData("func f<s/T>(pair: (s/T, i32), probe: T)\n    s is owner or uniq\n    match pair@move\n        (let x, _) if same(x, probe@ref) => use(x)\n        _ => ()", "uniq/T")] // dev
    [InlineData("func f<s/T>(o: s/Option<i32>, marker: s/T)\n    s is owner or uniq\n    match o@move\n        .Some(let x) => use(x)\n        .None => ()", "uniq/i32")] // dev
    [InlineData("func f<s/T>(arr: Array<(s/T, i32)>)\n    s is owner or uniq\n    for (x, _) in arr@move => use(x)", "uniq/T")]
    [InlineData("func f<s/T>(items: Array<s/(i32, i32) during a>, marker: s/T)\n    s is owner or uniq\n    for (x, y) in items => use(x)", "ref/i32")] // dev
    [InlineData("func f<s/T>(items: uniq/Array<s/(i32, i32) during a>, marker: s/T)\n    s is owner or uniq\n    for (x, y) in items => x@follow += 1", "uniq/i32")] // dev
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is owner or uniq\n    match pair\n        (let x, _) => use(x)", "ref/s/T")]
    [InlineData("func f<s/T>(arr: Array<s/T>)\n    s is owner or uniq\n    for x in arr@move => use(x@ref)", "s/T")]
    [InlineData("func f<s/T>(o: Option<s/T>) -> Option<s/T>\n    s is owner or uniq\n    let x = try o@move\n    return .Some(x@move)", "s/T")]
    [InlineData("func f<s/T>(pair: (s/T, i32))\n    s is owner or obj\n    match pair@move\n        (let x, _) => ()", "s/T")]
    public void APairLayerBindingOfAnOwnedPlaceIsAReference(string source, string type)
    {
        var c = MinimalEmissionTest.Analyze(Declarations + source + "\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var symbol = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => BoundMatch.Of(x)).OfType<BoundMatch>()
            .SelectMany(static x => x.Positions).Select(static x => x.BodySymbol).First(static x => x?.Name is "x" or "$try.value")!;
        Assert.Equal(type, Binding.HoverTypeName(symbol.Type!).Split(" during ")[0]);
    }

    // SPEC 15.1.6, 15.1.5, 11.5: exclusive access past a pair layer admitting owner lends the Subject Place itself, which a getter
    // result cannot lend exclusively: the binding needing it is an error, never a shared binding (the SPEC 15.1.6 example and its
    // forms: below a Tuple element, a whole Subject, a payload, a structural follow, a parenthesized getter and a guarded arm).
    [Theory]
    [InlineData("func f<s/T>(holder: ref/Holder<s/T>)\n    s is owner or uniq\n    match holder.p\n        (let x, _) => use(x)")]
    [InlineData("func f<s/T>(holder: ref/Holder<s/T>)\n    s is owner\n    match holder.p\n        (let x, _) => use(x)")]
    [InlineData("func f<s/T>(holder: ref/Holder<s/T>)\n    s is owner or uniq\n    match (holder.p)\n        (let x, _) => use(x)")]
    [InlineData("func f<s/T>(holder: ref/Holder<s/T>)\n    s is owner or uniq\n    match holder.p\n        (let x, _) if true => ()\n        _ => ()")]
    [InlineData("func f<s/T>(holder: ref/Single<s/T>)\n    s is owner or uniq\n    match holder.v\n        let x => use(x)")]
    [InlineData("func f<s/T>(holder: ref/Single<Option<s/T>>)\n    s is owner or uniq\n    match holder.v\n        .Some(let x) => use(x)\n        .None => ()")]
    [InlineData("func f<s/T>(holder: ref/Applied<s/T>)\n    s is owner or uniq\n    match holder.o\n        .Some(let x) => use(x)\n        .None => ()")]
    public void AnExclusiveBindingIntoAGetterResultIsAnError(string source)
    {
        var c = MinimalEmissionTest.Analyze(Declarations + source + "\n");
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal((DiagnosticCode.InvalidAssignment_Kd, "let x"), (issue.Code, issue.Node.ToString()));
    }

    // SPEC 13.5.5: the binding is a reference, so code that used it as the pair's value no longer fits (valid before as s/T).
    [Fact]
    public void APairLayerBindingIsNoValue()
    {
        var c = MinimalEmissionTest.Analyze("func f<s/T>(pair: (s/T, i32)) -> s/T\n    s is owner or uniq\n    match pair@move\n        (let x, _) => return x@move\n");
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(DiagnosticCode.TypeMismatch_Kd, issue.Code);
    }
}
