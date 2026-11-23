// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class DiagnosticPrecisionTest
{
    // The test-only `#Unsupported` library declarations the handed-on rows read (AnUnsupportedDeclarationIsRefusedWhereItIsHandedOn).
    private const string UnsupportedProbe = "#Unsupported\npublic struct ZProbe\n    public let x: i32\n\n    public struct Inner\n        public let y: i32\n\n        public struct Deep\n            public let z: i32\n" +
        "public group ZG\n    #Unsupported\n    public struct ZM\n        public let x: i32\n\n        public struct Inner\n            public let y: i32\n" +
        "public struct ZPlain\n    public let x: i32\n\n    public struct Inner\n        public let y: i32\n" +
        "public open struct ZOpenPlain\n    public let x: i32\n\n    public struct Inner\n        public let y: i32\n" +
        "#Unsupported\npublic struct ZDer : ZOpenPlain\n    public let w: i32\n";

    private const string Payload = "struct P\n    public var v: i32\n    public init(v: i32) => self.v = v\n";

    public static TheoryData<string> MutationNames => [.. DiagnosticCorpus.Mutations.Select(static x => x.Name)];

    [Fact]
    public void IndependentErrorsRemainVisibleAfterAnUnknownIterationSubject()
    {
        const string source = """
            public func main()
                var sum: i32 = 0
                for value in missing
                    sum = sum + value
                let wrong: i32 = true
                absent()
            """;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Issues.Count == 3, string.Join("\n", c.Binding.Issues));
        Assert.Equal(2, c.Binding.Issues.Count(x => x.Code == Kimi.DiagnosticCode.UnresolvedBinding_Kd));
        Assert.Single(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.TypeMismatch_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 23.1.2 (PLAN G260): a loop binding of a failed iterable rests on it, as a pattern binding rests on a failed Subject, so a
    // check of its use publishes nothing more; over a valid iterable the same use's missing member is its own error.
    [Theory]
    [InlineData("missing", "UnresolvedBinding_Kd missing")]
    [InlineData("0..3", "UnresolvedBinding_Kd i.foo")]
    public void ALoopBindingRestsOnItsFailedIterable(string iterable, string records)
        => Assert.Equal(records, string.Join(" | ", CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze($"public func main()\n    for i in {iterable}\n        i.foo()")).Select(static x => $"{x.Code} {x.Text}")));

    // DIAGNOSTICS.md rule 3: an implementation limit is Unsupported where it is met; a check that rests on a failed Type
    // (expected Type, candidate signature, generic Name) is derived and published only through that failure.
    [Theory]
    [InlineData("struct S\n    public let v: [2 of i32]\n    public init() => self.v = [1, 2]\nfunc set(s: uniq/S)\n    s.v[0] = 5\npublic func main() => ()", "InvalidAssignment_Kd", "s.v[0]")]
    [InlineData("func f(w: Weak<i32>) -> i32 => 0\npublic func main() => ()", "Unsupported_Kd", "Weak")]
    [InlineData("public func main()\n    let r = Kimi.Intrinsics.nothing(1)", "UnresolvedBinding_Kd", "Kimi.Intrinsics.nothing")]
    [InlineData("func f(w: Missing<i32>) -> i32 => 0\npublic func main() => ()", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("public func main()\n    let w: Option<Missing> = .None", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("public func main()\n    var w: Missing = .None\n    w = .Some(1)", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func make() -> Missing => .None\npublic func main()\n    let v = make()", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func make() -> Missing\n    return .None\npublic func main()\n    let v = make()", "UnresolvedBinding_Kd", "Missing")]
    [InlineData("func g<T>(x: i32) -> i32 => 0\npublic func main()\n    let v = g<Undefined>(1)", "UnresolvedBinding_Kd", "Undefined")]
    [InlineData("func identity<T>(value: T) -> T => value@move\npublic func main()\n    let values = identity<[_ of i32]>([1])", "ExpectedSyntax_Kd", "_")]
    public void LimitsAndDependentChecksPublishOneSpecificError(string source, string code, string text)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
    }

    // SPEC 3.4.3: G35's array cases now execute; keep the original sources as acceptance regressions.
    [Theory]
    [InlineData("Array<i32>", "a[0] += 5")]
    [InlineData("[3 of i32]", "a[0] = 5")]
    public void BorrowedArrayWriteNoLongerReportsAnImplementationLimit(string type, string update)
    {
        var c = MinimalEmissionTest.Analyze($"func set(a: uniq/{type})\n    {update}\npublic func main() => ()");
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    // SPEC 23.3: a mismatch names both Types, a write names its target, each at the smallest syntax that shows it.
    [Theory]
    [InlineData("func f() -> i32\n    return true\npublic func main() => ()", "true", "expected i32, found bool")]
    [InlineData("public func main()\n    let wrong: i32 = true", "true", "expected i32, found bool")]
    [InlineData("public func main()\n    let sum = 1 + \"a\"", "1 + \"a\"", "string has no +")]
    [InlineData("public func main()\n    if 1 and true\n        ()", "1", "expected bool, found integer literal")]

    // An untyped literal or an anonymous function at a Type it cannot have names that complete Type, Type arguments included.
    [InlineData("struct Box<T>\n    public let v: T\n    public init(v: T) => self.v = v@move\npublic func main()\n    let b: Box<i32> = 5", "5", "expected Box<i32>, found integer literal")]
    [InlineData("public func main()\n    let t: (i32, bool) = 4", "4", "expected (i32, bool), found integer literal")]
    [InlineData("public func main()\n    let o: Option<i32> = 1", "1", "expected Option<i32>, found integer literal")]
    [InlineData("public func main()\n    let f: (i32) -> i32 = 9", "9", "expected (i32) -> i32, found integer literal")]
    [InlineData("public func main()\n    let t: (f64, f64) = 2.5", "2.5", "expected (f64, f64), found floating-point literal")]
    [InlineData("public func main()\n    let x: i32 = func () => 1", "func () => 1", "expected i32, found an anonymous function")]
    [InlineData("public func main()\n    let counter = 1\n    counter += 1", "counter", "counter cannot be written")]

    // A check that compares no Types still names the two sides it holds (R7-E3 E3a-2): a `not` operand, a signed literal, a typed
    // borrow's referent, a pointer conversion's target and a `get` result against its Property's Type.
    [InlineData("public func main()\n    let n = 1\n    let b = not n", "not n", "expected bool, found i32")]
    [InlineData("public func main()\n    let x: i32 = -1.5", "-1.5", "expected i32, found floating-point literal")]
    [InlineData("public func main()\n    let v: i32 = 1\n    let r = v@ref/i64", "v@ref/i64", "expected i64, found i32")]
    [InlineData("unsafe func f(p: raw/i32) -> f64 => p@f64\npublic func main() => ()", "p@f64", "expected f64, found raw/i32")]
    [InlineData("struct S\n    public computed item: i32\n        get(self: ref/Self) -> bool => true\npublic func main() => ()", "get(self: ref/Self) -> bool => true", "expected i32, found bool")]

    // R7-E3 E3c-2: a guard's Type, a construction at its expected Type and a floating-point literal at a generic integer position, plain
    // and signed, name the two sides they compared.
    [InlineData("func f(x: i32) -> i32 => match x\n    _ if x => 0\n    _ => 1\npublic func main() => ()", "x", "expected bool, found i32")]
    [InlineData("enum E<T>\n    A(T)\npublic func main()\n    let x: E<i32> = E<bool>.A(true)", "E<bool>.A(true)", "expected E<i32>, found E<bool>")]
    [InlineData("func f<T>(v: T) -> T\n    T is PrimitiveInteger\n    return 1.5\npublic func main() => ()", "1.5", "expected T, found floating-point literal")]
    [InlineData("func f<T>(v: T) -> T\n    T is PrimitiveInteger\n    return -1.5\npublic func main() => ()", "-1.5", "expected T, found floating-point literal")]
    public void TargetChecksShowTheirLocationAndFacts(string source, string text, string label)
    {
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(source)));
        Assert.Equal(text, error.Text);
        Assert.Equal(label, error.Label);
    }

    // SPEC 23.3: a failed selection counts and relates the candidates it considered.
    [Fact]
    public void AFailedSelectionRelatesItsCandidates()
    {
        var c = MinimalEmissionTest.Analyze("func pick(value: i32) -> i32 => value\nfunc pick(value: string) -> i32 => 0\npublic func main()\n    let x = pick(true)");
        Assert.Single(CompilationTestHelper.PublishedErrors(c));
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics);
        Assert.Equal(nameof(Kimi.DiagnosticCode.NoApplicableOverload_Kd), record.Code);
        Assert.Equal("none of 2 candidates applies", record.Label);
        Assert.Equal(["candidate", "candidate"], record.Related!.Select(static x => x.Role));
        Assert.Equal([0, 1], record.Related!.Select(static x => x.Range!.Value.Start.Line));
    }

    // A recovered chained comparison is not type-checked, a control-flow check that repeats Binding's is derived, and a
    // structural control-flow check stays independent of a Binding failure at the same place. The result a Block body leaves
    // undelivered where it falls through rests on the fallthrough, also in a closure and a getter; other ownership problems, in
    // that function or elsewhere, stay direct.
    [Theory]
    [InlineData("public func main()\n    let ok = 1 < 2 < 3\n    let wrong: i32 = true\n    missing()", "ChainedComparison_Kd,TypeMismatch_Kd,UnresolvedBinding_Kd")]
    [InlineData("func f() -> i32\n    let wrong: i32 = true\npublic func main() => ()", "FunctionFallthrough_Kd,TypeMismatch_Kd")]
    [InlineData("public func main() -> Missing\n    return ()", "UnresolvedBinding_Kd")]
    [InlineData("func f(a: i32) -> i32\n    if a == 1 => return 1\n    else => 2\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(a: i32) -> i32\n    if a == 1 => return 1\n    return 2\npublic func main() => ()", "")]
    [InlineData("func f(a: i32) -> string\n    if a == 1 => return \"x\"\nfunc g()\n    let text = \"a\"\n    let first = text@move\n    let second = text@move\npublic func main() => ()", "FunctionFallthrough_Kd,MovedPlace_Kd")]
    [InlineData("func f(a: i32) -> i32\n    let s: string\n    Console.writeLine(s)\n    if a == 1 => return 1\npublic func main() => ()", "FunctionFallthrough_Kd,UninitializedPlace_Kd")]
    [InlineData("public func main()\n    let g = func (a: i32) -> i32\n        if a == 1 => return 1\n    _ = g", "FunctionFallthrough_Kd")]
    // SPEC 14.9.3: an exit to an outer target leaves the inner target that receives its own exit, in either order, so the outer loop
    // completes and the body falls through; with only a continue to it, the outer loop never completes.
    [InlineData("func f(flag: bool) -> i32\n    label outer: loop\n        label inner: loop\n            if flag => exit to outer\n            exit to inner\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    label outer: loop\n        label inner: loop\n            if flag => exit to inner\n            exit to outer\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    let wrong: i32 = true\n    label outer: loop\n        label inner: loop\n            if flag => exit to inner\n            continue to outer\npublic func main() => ()", "TypeMismatch_Kd")]
    // SPEC 14.1, 14.3, 14.5.2, 14.7, 14.8 (the Front's B6a coverage): an unlabeled exit skips a `do` it is not addressed to; an exit to a
    // `do` and a yield to a selection pass through a loop; a labeled selection or match receives its own yield; an exit to the outer loop
    // leaves a selection receiving its own yield (with a continue instead, the loop never completes); three levels close a target two
    // cells below the chain's head (with a continue to the outermost, it never completes); an unlabeled yield in Discard Context goes to
    // none. Rows UnlabeledExitSkipsDo, DoThroughLoop, IfYield, MatchYield, YieldThroughLoop, ExitThroughSelection,
    // ContinueThroughSelection, ThreeLevels, ThreeLevelsContinue, UnlabeledYieldDiscard.
    [InlineData("func f() -> i32\n    loop\n        do\n            exit\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f() -> i32\n    label d: do\n        loop\n            exit to d\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    label s: if flag\n        yield to s\n    else => return 1\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(value: Option<i32>) -> i32\n    label m: match value\n        .Some(_) => yield to m\n        .None => return 1\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    label s: if flag\n        loop\n            yield to s\n    else => return 1\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    label outer: loop\n        label s: if flag\n            if flag => exit to outer\n            yield to s\n        else => return 1\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    label outer: loop\n        label s: if flag\n            if flag => continue to outer\n            yield to s\n        else => return 1\npublic func main() => ()", "")]
    [InlineData("func f(flag: bool) -> i32\n    label a: loop\n        label b: loop\n            label c: loop\n                if flag => exit to c\n                if flag => exit to a\n                exit to b\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    label a: loop\n        label b: loop\n            label c: loop\n                if flag => exit to c\n                if flag => continue to a\n                exit to b\npublic func main() => ()", "")]
    [InlineData("func f(flag: bool) -> i32\n    if flag\n        yield\n    else => return 1\npublic func main() => ()", "UnlabeledYieldTarget_Kd")]
    [InlineData("struct S\n    public var v: i32 = 0\n    public computed w: i32\n        get(self: ref/Self) -> i32\n            if self.v == 1 => return 1\npublic func main() => ()", "FunctionFallthrough_Kd")]
    // A fallthrough that written syntax reaches beside an unresolved condition is direct; one only past an unresolved expression is
    // derived from it (Binding.GuessedCompletion), so the failure alone is published.
    [InlineData("func f(flag: bool) -> i32\n    if flag\n        if missing => return 1\npublic func main() => ()", "FunctionFallthrough_Kd,UnresolvedBinding_Kd")]
    [InlineData("func f(flag: bool) -> i32\n    if flag => return 1\n    missing()\npublic func main() => ()", "UnresolvedBinding_Kd")]
    // A form Binding rejected completes through its parts (PLAN G47): a match lacking an arm (SPEC 14.8.4) or with a failed arm, and a
    // for over a non-iterable, reach the end through written syntax, so the fallthrough is direct beside their error, as with every
    // arm written; with no arm completing, only the coverage error is published, in a function body and in a require failure body.
    // A call Binding rejected without a Type has no Never evidence: its callee `(i32) -> Never` might not complete, so the fallthrough
    // past it is derived and the call's error alone is published, also past an unvisited part (`k<i32>`).
    [InlineData("func f(b: bool) -> i32\n    match b\n        true => ()\npublic func main() => ()", "FunctionFallthrough_Kd,NonExhaustiveMatch_Kd")]
    [InlineData("func f(b: bool) -> i32\n    match b\n        true => ()\n        false => return 1\npublic func main() => ()", "FunctionFallthrough_Kd")]
    [InlineData("func f(b: bool) -> i32\n    match b\n        true => return 1\npublic func main() => ()", "NonExhaustiveMatch_Kd")]
    [InlineData("func f(ok: bool, b: bool)\n    require ok else\n        match b\n            true => return\npublic func main() => ()", "NonExhaustiveMatch_Kd")]
    [InlineData("func f(b: bool) -> i32\n    match b\n        true => ()\n        false => k()\nfunc k(x: i32) -> Never => $abort(\"x\")\npublic func main() => ()", "FunctionFallthrough_Kd,NoApplicableOverload_Kd")]
    [InlineData("func f(b: i32) -> i32\n    for x in b => ()\npublic func main() => ()", "FunctionFallthrough_Kd,UnsatisfiedConstraint_Kd")]
    [InlineData("func f(k: (i32) -> Never) -> i32\n    k()\npublic func main() => ()", "NoApplicableOverload_Kd")]
    [InlineData("func f(k: (i32) -> Never) -> i32\n    k<i32>(1)\npublic func main() => ()", "NoApplicableOverload_Kd")]
    [InlineData("func g(ok: bool, k: (i32) -> Never)\n    require ok else\n        k()\npublic func main() => ()", "NoApplicableOverload_Kd")]
    // A rejected acquisition states its overlap once; a later use of the rejected Loan meets the same holder again, while an
    // independent access to the root during that holder's Loan is still its own problem.
    [InlineData("public func main()\n    var value = 1\n    let inner = value@ref\n    let other = value@uniq\n    other@follow = 2\n    require inner == 1 else => $abort(\"x\")", "ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var value = 1\n    let p = value@uniq\n    let inner = p@follow@ref\n    let other = p@follow@uniq\n    other@follow = 2\n    require inner == 1 else => $abort(\"x\")", "ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var value = 1\n    let inner = value@ref\n    let other = value@uniq\n    other@follow = 2\n    value = 9\n    require inner == 1 else => $abort(\"x\")", "ComparisonLoanConflict_Kd,ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var a = 1\n    let p = (a@uniq, 2)\n    let q = (a@uniq, 3)\n    p.0@follow = 5\n    q.0@follow = 6", "ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var a = 1\n    var b = 1\n    let p = (a@uniq, 2)\n    let q = (a@uniq, 3)\n    p.0@follow = 5\n    let r = b@ref\n    b = 2\n    require r == 1 else => $abort(\"r\")\n    q.0@follow = 6", "ComparisonLoanConflict_Kd,ComparisonLoanConflict_Kd")]
    [InlineData("func pack<T>(x: T, n: i32) -> (T, i32) => (x@move, n)\npublic func main()\n    var a = 1\n    let p = pack(a@uniq, 2)\n    let q = pack(a@uniq, 3)\n    p.0@follow = 5\n    q.0@follow = 6", "ComparisonLoanConflict_Kd")]
    [InlineData("func pack<T>(x: T, n: i32) -> (T, i32) => (x@move, n)\npublic func main()\n    var a = 1\n    var b = 1\n    let p = pack(a@uniq, 2)\n    let q = pack(a@uniq, 3)\n    p.0@follow = 5\n    let r = b@ref\n    b = 2\n    require r == 1 else => $abort(\"r\")\n    q.0@follow = 6", "ComparisonLoanConflict_Kd,ComparisonLoanConflict_Kd")]
    // A result or binding whose expression already reported why it has no value is not reported again as uninitialized, at the
    // signature or at the binding's later uses. SPEC 3.5, 13.5.5: a bare read needs Copy, and P has not opted in;
    // a selected payload provides no Take. Its one acquisition error must not become uninitialized-result cascades.
    [InlineData(Payload + "func read(o: objref/P) -> P => o@follow\npublic func main() => ()", "TransferRequired_Kd")]
    [InlineData(Payload + "func read(o: objref/P, c: bool) -> P\n    if c => return o@follow\n    return P.init(1)\npublic func main() => ()", "TransferRequired_Kd")]
    [InlineData(Payload + "func read(o: objref/P) -> i32\n    let p = o@follow\n    return p.v\npublic func main() => ()", "TransferRequired_Kd")]
    // The valid twin of a failed explicit Type argument (DECISIONS BU-86): an argument that binds selects the candidate.
    [InlineData("func g<T>(x: i32) -> i32 => 0\npublic func main()\n    let v = g<i32>(1)", "")]
    public void EachProblemPublishesOneErrorAcrossPhases(string source, string codes)
        => Assert.Equal(codes.Split(',', StringSplitOptions.RemoveEmptyEntries), CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(source)).Select(static x => x.Code).Order(StringComparer.Ordinal));

    [Theory]
    [InlineData("Return", "func read(o: objref/P) -> P => o@follow", "read(owner@objref).v")]
    [InlineData("Branch", "func read(o: objref/P, c: bool) -> P\n    if c => return o@follow\n    return P.init(1)", "read(owner@objref, true).v")]
    [InlineData("Local", "func read(o: objref/P) -> i32\n    let p = o@follow\n    return p.v", "read(owner@objref)")]
    public void ExplicitCopyPayloadsNeedNoAcquisitionDiagnostic(string name, string function, string result)
    {
        var source = Payload.Replace("struct P\n", "struct P\n    Self is Copy\n", StringComparison.Ordinal) + function +
            "\nlet owner = Kimi.Intrinsics.makeObj(P.init(7))\nrequire " + result + " == 7 else => $abort(\"copy\")";
        Assert.Empty(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(source)));
        ScalarEmissionTest.EmitFixture("ObjectPayloadPrecision" + name, source, string.Empty);
    }

    // DIAGNOSTICS.md §9.2: the published Errors of a mutation are exactly its expected codes. Every intended problem is
    // reported, including independent ones, nothing depends on another, and no fallback or unexplained derived fact remains.
    [Theory]
    [MemberData(nameof(MutationNames))]
    public void MilestoneFaultsHaveSpecificDiagnosticsWithoutDependentCascades(string name)
    {
        var mutation = DiagnosticCorpus.Mutation(name);
        var c = MinimalEmissionTest.Analyze(DiagnosticCorpus.Apply(mutation));
        var errors = CompilationTestHelper.PublishedErrors(c);
        var detail = string.Join("\n", errors.Select(static x => x.ToString()));
        Assert.True(errors.Length != 0, detail);
        Assert.True(errors.All(x => mutation.Expected.Contains(x.Code)), detail);
        Assert.True(mutation.Expected.All(code => errors.Any(x => x.Code == code)), detail);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 23.1.2: syntax that failed to parse, including a token the lexer rejected, is the prerequisite of every check of
    // its recovered form, so its syntax Error alone explains it: no unexplained derived record, no repeated token report and no
    // ownership record for a body that the syntax Error left missing.
    [Theory]
    [InlineData("func f() ->\n    return 1\nlet a = f()", "MissingReturnType_Kd")]
    [InlineData("let a = 1\nlet v = a + @", "ExpectedSyntax_Kd")]
    [InlineData("let v: i32 = 0x", "InvalidNumericLiteral_Kd")]
    [InlineData("let a = 1\nlet v = a.\nlet w = 2", "MissingSyntax_Kd")]
    [InlineData("func f() -> i32\n\nfunc g()\n\nlet y = 1", "MissingSyntax_Kd,MissingSyntax_Kd")]
    [InlineData("func f() ->\nlet y = 1", "MissingReturnType_Kd")]

    // SPEC 14.2.1, 6.3.1 (PLAN G890): each use of a user function or constructor whose Body is missing rests on that record: a call, a
    // method call, a generic call, a function value, a constructor call, a call from another body, a bodiless function's default and
    // a confined body's call; a use's own failure stays independent (the wrong arity).
    [InlineData("func f() -> i32\nlet x = f()", "MissingSyntax_Kd")]
    [InlineData("struct S\n    public let x: i32\n    public init(x: i32)\n        self.x = x\n    public func get(self: ref/Self) -> i32\nlet s = S.init(x: 1)\nlet y = s.get()", "MissingSyntax_Kd")]
    [InlineData("func f<T>(value: T) -> T\nlet y = f(1)", "MissingSyntax_Kd")]
    [InlineData("func f() -> i32\nlet g = f", "MissingSyntax_Kd")]
    [InlineData("struct S\n    public let x: i32\n    public init(x: i32)\nlet s = S.init(x: 1)\nlet y = s.x", "MissingSyntax_Kd")]
    [InlineData("func f() -> i32\nfunc g() -> i32 => f()\nlet y = g()", "MissingSyntax_Kd")]
    [InlineData("func f() -> i32\nfunc g(a: i32 = f()) -> i32\nlet y = g()", "MissingSyntax_Kd,MissingSyntax_Kd")]
    [InlineData("open struct Base\n    public init()\n        ()\n    public func helper(self: objref/Self) -> i32\n    public virtual func score(self: objref/Self) -> i32\n        effect confined\n        return self.helper()\nlet y = 1", "MissingSyntax_Kd")]
    [InlineData("func f() -> i32\nlet x = f(1)", "MissingSyntax_Kd,NoApplicableOverload_Kd")]

    // The same through a requirement's witness, a VTable slot and a struct's destructor (SPEC 14.2.1: drop has a Body); the confined
    // check's own failure beside a resting callee stays independent.
    [InlineData("contract Describe\n    func describe(self: ref/Self) -> i32\nstruct S\n    Self is Describe\n    public let x: i32\n    public init(x: i32)\n        self.x = x\n    public func describe(self: ref/Self) -> i32\nfunc show<T>(value: ref/T) -> i32\n    T is Describe\n    return value.describe()\nlet s = S.init(x: 1)\nlet y = show(s@ref)", "MissingSyntax_Kd")]
    [InlineData("open struct Base\n    public virtual func score(self: objref/Self) -> i32\nlet b = Base.init()@obj\nlet a = b.score()", "MissingSyntax_Kd")]
    [InlineData("struct S\n    public let x: i32\n    public init(x: i32)\n        self.x = x\n    drop\nlet s = S.init(x: 1)\nlet y = s.x", "MissingSyntax_Kd")]
    [InlineData("open struct Base\n    public init()\n        ()\n    public func helper(self: objref/Self) -> i32\n    public virtual func score(self: objref/Self) -> i32\n        effect confined\n        Console.writeLine(\"x\")\n        return self.helper()\nlet y = 1", "MissingSyntax_Kd,UnsatisfiedEffectBound_Kd")]
    public void RecoveredSyntaxExplainsItsDependents(string source, string codes)
        => Assert.Equal(codes.Split(','), CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(source)).Select(static x => x.Code).Order(StringComparer.Ordinal));

    // SPEC 23.3, 23.1.2: a name missing at the end of a line is an insertion point after the dot, not the next line.
    [Fact]
    public void AMissingMemberNameIsAnInsertionPointAfterTheDot()
    {
        const string Source = "let a = 1\nlet v = a.\nlet w = 2";
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(Source)));
        Assert.Equal(nameof(Kimi.DiagnosticCode.MissingSyntax_Kd), error.Code);
        Assert.Equal(Source.IndexOf("a.\n", StringComparison.Ordinal) + 2, error.Span.Start);
        Assert.Equal(0, error.Span.Length);
    }

    // SPEC 23.1: the synthesized writes of one interpolation share its span; a failed write is a problem at the value it
    // writes, so two failures have a defined order instead of faulting the check.
    [Fact]
    public void FailedInterpolationWritesAreLocatedAtTheirValues()
    {
        var errors = CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze("let a: Option<isize> = Option.None\nlet b: Option<i32> = Option.None\nConsole.writeLine(\"\\(a) \\(b) \\(a)\")"));
        Assert.Equal(3, errors.Length);
        Assert.All(errors, static x => Assert.Equal(nameof(Kimi.DiagnosticCode.NoApplicableOverload_Kd), x.Code));
        Assert.Equal(["a", "b", "a"], errors.Select(static x => x.Text));
    }

    // SPEC 4.7.1, 5.3, 23.1.2: an index key that fits no conformance, Dictionary key or pointer offset is reported once, with
    // both Types at the key or at the index expression, and a receiver without indexing is a Language error; a user index through
    // a pair layer is the located limit (PLAN G179).
    [Theory]
    [InlineData("func g<s/T>(value: s/T, x: s/View)\n    s is owner or uniq\n    let a = x[0]", "Unsupported_Kd", "x[0]", null)]
    [InlineData("let a = v[^1]", "NoApplicableOverload_Kd", "v[^1]", null)]
    [InlineData("let n: i32 = 0\nlet a = v[n]", "NoApplicableOverload_Kd", "v[n]", null)]
    [InlineData("let a = v[0..1]", "NotIndexable_Kd", "v[0..1]", null)]
    [InlineData("let x: i32 = 5\nlet a = x[0]", "NotIndexable_Kd", "x[0]", null)]
    [InlineData("var d: Dictionary<isize, string> = [1: \"x\"]\nlet a = d[^1]", "TypeMismatch_Kd", "^1", "expected isize, found Kimi.FromEnd<i32>")]
    [InlineData("func f(p: raw/i32) -> i32\n    unsafe\n        return p[0..4]", "TypeMismatch_Kd", "0..4", "expected isize, found Kimi.Range<i32, i32>")]
    [InlineData("func f(p: ref/raw/i32) -> i32\n    unsafe\n        return p[0]", "NotIndexable_Kd", "p[0]", null)]
    public void IndexKeysReportOneExplainedError(string statement, string code, string text, string? label)
    {
        const string View = "struct View\n    Self is Indexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n" +
            "    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\nlet v = View.init(value: 7)\n";
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(View + statement)));
        Assert.Equal(code, error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(label, label is null ? null : error.Label);
    }

    // SPEC 15.1.5, 13.5.5: a Take its path does not offer keeps its code, and its Note names the layer that offers no Take, for the
    // implicit and the explicit forms alike; no Message names an exclusive reference (PLAN G181 (1)).
    [Theory]
    [InlineData("func g<s/T>(m: s/T, p: s/P)\n    s is owner or uniq\n    let y = p.inner@move", "ExclusivePathTake_Kd", "a pair layer admitting owner")]
    [InlineData("func g<s/T>(m: s/T, p: s/P)\n    s is owner or uniq\n    let y = p@follow.inner@move", "ExclusivePathTake_Kd", "a pair layer admitting owner")]
    [InlineData("func g<s/T>(m: s/T, p: s/P)\n    s is uniq\n    let y = p.inner@move", "ExclusivePathTake_Kd", "an exclusive reference or view")]
    [InlineData("func g(p: uniq/P)\n    let y = p.inner@move", "ExclusivePathTake_Kd", "an exclusive reference or view")]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    let y = h.inner@move", "InvalidAssignment_Kd", "an object payload")]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    let y = h@follow.inner@move", "InvalidAssignment_Kd", "an object payload")]
    [InlineData("func g()\n    var h = P.init(1)@obj\n    let y = h@follow@move", "InvalidAssignment_Kd", "an object payload")]
    public void DeniedTakesNameTheirLayer(string function, string code, string layer)
    {
        const string Types = "public func main()\n    return\nstruct Q\n    public var w: i32\n    public init(w: i32) => self.w = w\n" +
            "struct P\n    public var v: i32\n    public var inner: Q\n    public init(v: i32)\n        self.v = v\n        self.inner = Q.init(v)\n";
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(Types + function)));
        Assert.Equal(code, error.Code);
        Assert.Contains($"The Place is reached through {layer}", error.Note);
        Assert.DoesNotContain("exclusive reference", error.Message);
    }

    // SPEC 13.5.3: only a numeric conversion changes an owned Core, so converting a position or range is a Type error.
    [Theory]
    [InlineData("let a = (^1)@u8", "(^1)@u8", "expected u8, found Kimi.FromEnd<i32>")]
    [InlineData("let y = ^1\nlet a = y@FromEnd<i64>", "y@FromEnd<i64>", "expected Kimi.FromEnd<i64>, found Kimi.FromEnd<i32>")]
    [InlineData("let n: i32 = 3\nlet a = n@FromEnd<i32>", "n@FromEnd<i32>", "expected Kimi.FromEnd<i32>, found i32")]
    [InlineData("let a = (0..1)@u8", "(0..1)@u8", "expected u8, found Kimi.Range<i32, i32>")]
    public void PositionConversionsAreTypeErrors(string statement, string text, string label)
    {
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(statement)));
        Assert.Equal(nameof(Kimi.DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(label, error.Label);
    }

    // SPEC 23.1.2: a repeated loop binding is one problem at the later name; the loop rests on it.
    [Theory]
    [InlineData("let pairs: [2 of (i32, i32)] = [(1, 2), (3, 4)]\nfor (a, a) in pairs => ()")]
    [InlineData("var entries = [1: 2]\nfor (a, a) in entries => ()")]
    public void ARepeatedLoopBindingIsReportedOnceAtTheLaterName(string source)
    {
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(source)));
        Assert.Equal(nameof(Kimi.DiagnosticCode.DuplicateBinding_Kd), error.Code);
        Assert.Equal(new Kimi.Diagnostics.SourceSpan(source.IndexOf("a, a)", StringComparison.Ordinal) + 3, 1), error.Span);
    }

    // SPEC 23.1.2: a fill literal whose element or length failed rests on that part instead of adding a formation error.
    [Theory]
    [InlineData("let a = [3 of missingValue]", "missingValue")]
    [InlineData("let a = [missingLength of 0]", "missingLength")]
    public void AFillLiteralRestsOnItsFailedPart(string source, string text)
    {
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze(source)));
        Assert.Equal(nameof(Kimi.DiagnosticCode.UnresolvedBinding_Kd), error.Code);
        Assert.Equal(text, error.Text);
    }

    // A closure Type has no written name: a mismatch shows the signature of its anonymous function, never the captured
    // environment as Type arguments.
    [Fact]
    public void AClosureTypeIsShownByItsSignature()
    {
        var error = Assert.Single(CompilationTestHelper.PublishedErrors(MinimalEmissionTest.Analyze("let offset: i32 = 1\nlet f = func [offset] (value: i32) -> i32 => value + offset\nlet n: i32 = f")));
        Assert.Equal(nameof(Kimi.DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal("expected i32, found closure (i32) -> i32", error.Label);
    }

    // SPEC 23.3: a startup main that breaks the startup rules is located at its signature, not over its body.
    [Fact]
    public void AnInvalidStartupMainIsLocatedAtItsSignature()
    {
        const string Source = "public func main() -> i32\n    return 0\n";
        var record = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(Kimi.DiagnosticCode.InvalidStartupMain_Kd), record.Code);
        Assert.Equal(new Kimi.Diagnostics.SourceSpan(Source.IndexOf("main()", StringComparison.Ordinal), "main() -> i32".Length), record.Span);
    }

    // Publishes every front-end phase as a check does and returns its Errors.
    // DECISIONS R5-M32: the pending gate of the old check is gone; Binding's guess rule counts an expression it left unresolved as a
    // guess, so a body that falls through only past one records its fallthrough derived from that failure, not nothing.
    [Fact]
    public void AFallthroughPastAnUnresolvedExpressionIsRecordedDerived()
    {
        var c = MinimalEmissionTest.Analyze("func f(flag: bool) -> i32\n    if flag => return 1\n    missing()\npublic func main() => ()");
        Assert.Contains(c.Binding.LintErrorRecords, static x => x.Code == Kimi.DiagnosticCode.FunctionFallthrough_Kd);
        Assert.Equal(["UnresolvedBinding_Kd"], CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code));
    }

    // SPEC 23.1.2: attribute legality and dependent declaration validity are one cause, in every attribute owner.
    [Theory]
    [InlineData("#Layout(\"bad\")", "InvalidLayoutAttribute_Kd", "#Layout(\"bad\")")]
    [InlineData("#Unknown", "UnresolvedBinding_Kd", "Unknown")]
    [InlineData("#LibraryImport(\"library\", \"symbol\")", "UnresolvedBinding_Kd", "LibraryImport")]
    // SPEC 6.7: outside the Kimi library an implementation Attribute is an unrecognized one (rows A1-A4, A7; no same-named declaration).
    [InlineData("#Lang(\"Option\")", "UnresolvedBinding_Kd", "Lang")]
    [InlineData("#Intrinsic(\"Abort\")", "UnresolvedBinding_Kd", "Intrinsic")]
    [InlineData("#Intrinsic(\"Fail\", \"KIMI_E_INDEX_BOUNDS\")", "UnresolvedBinding_Kd", "Intrinsic")]
    [InlineData("#CallerLocation", "UnresolvedBinding_Kd", "CallerLocation")]
    [InlineData("#Unsupported", "UnresolvedBinding_Kd", "Unsupported")]
    public void RejectedAttributesPublishOnlyTheirOwnFailure(string attribute, string code, string token)
    {
        foreach (var testBuild in new[] { false, true })
        {
            var c = CompilationTestHelper.ParseSuccess(attribute + "\nstruct S");
            c.IsTestBuild = testBuild;
            Assert.False(c.Bind().IsComplete);
            var target = c.Kotonoha.RootKoto.NestedContainers.Single();
            Assert.Equal(BindingState.Invalid, target.StateOf());
            Assert.Equal(BindingFailure.None, target.FailureOf());
            Assert.DoesNotContain(c.Binding.FailureCauses(target)!, key => ReferenceEquals(key.Subject, target));
            var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
            Assert.Equal(code, error.Code);
            Assert.Equal(token, error.Text);
        }
    }

    // SPEC 6.7, 23.1.2: outside the Kimi library the implementation Attributes are unrecognized, each Name failing on its own beside
    // every other independent failure, such as a missing Body (SPEC 14.2.1), and placed as any other Attribute is: a user constructor
    // and a Contract requirement take none (rows A8, A11, A12, B2, B3, B5; Names with no same-named declaration).
    [Theory]
    [InlineData("#Lang(\"Option\")\n#Lang(\"Result\")\nfunc f() => ()\n", "UnresolvedBinding_Kd 1:2", "UnresolvedBinding_Kd 2:2")]
    [InlineData("#Lang(\"Option\")\nenum E\n    A\n#Unsupported\ncontract C\n    func c(self: ref/Self) -> i32\n", "UnresolvedBinding_Kd 1:2", "UnresolvedBinding_Kd 4:2")]
    [InlineData("#Intrinsic(\"Abort\")\nfunc f() -> i32\n", "UnresolvedBinding_Kd 1:2", "MissingSyntax_Kd 2:16")]
    [InlineData("#Unsupported\nfunc f() -> i32\n", "UnresolvedBinding_Kd 1:2", "MissingSyntax_Kd 2:16")]
    [InlineData("struct S\n    public let x: i32\n    #Lang(\"X\")\n    public init(x: i32)\n        self.x = x\n", "MisplacedSyntax_Kd 3:5")]
    [InlineData("struct S\n    public let x: i32\n    #Layout(\"C\")\n    public init(x: i32)\n        self.x = x\n", "MisplacedSyntax_Kd 3:5")]
    [InlineData("contract C\n    #Lang(\"Equatable\")\n    func c(self: ref/Self) -> i32\n", "MisplacedSyntax_Kd 2:5")]
    public void UnrecognizedAttributesPublishTheirOwnRecords(string source, params string[] expected)
        => Assert.Equal(expected.Order(StringComparer.Ordinal), ErrorsAt(source, DiagnosticCorpus.Check(source, "Library")).Order(StringComparer.Ordinal));

    // SPEC 22.1, 13.6.2, 13.6.3, 23.1.1, 23.1.2 (R8-G1-3b's coverage table; each row is named by its program): where a use of a
    // declaration this implementation does not support is reported, and what a program that only names one beside it keeps. The
    // library writes `downgrade`, `upgrade`, `makeRcCyclic`, `makeArcCyclic` and `Weak` with `#Unsupported`, and Binding's use
    // verdict (Supported) refuses each use outside them at its Name.
    [Theory]
    // At their SPEC label on the base: a use by a call, a Type name, a value, a generic argument, a member of an alias, a length.
    [InlineData("e1x", "let r = Kimi.Intrinsics.makeRc(1)\nlet w = Kimi.Intrinsics.downgrade(r)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9")]
    [InlineData("e7x", "let w = Kimi.Intrinsics.upgrade(1)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:9")]
    [InlineData("r08x", "func f(w: ref/Weak<rc/i32>) => ()\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:15")]
    [InlineData("r09x", "let r = Kimi.Intrinsics.makeRc(1)\nlet w = Kimi.Intrinsics.downgrade(r@ref)\nlet u = Kimi.Intrinsics.upgrade(w@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9", "Unsupported_Kd 3:9")]
    [InlineData("r14x", "alias M => Kimi.Intrinsics\nlet r = M.makeRc(1)\nlet w = M.downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 3:9")]
    [InlineData("r17x", "let r = Kimi.Intrinsics.makeRc(1)\nlet w = Kimi.Intrinsics.downgrade(r@ref)\nlet z: i32 = \"s\"\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9", "TypeMismatch_Kd 3:14")]
    [InlineData("r18x", "let w = Kimi.Intrinsics.downgrade(missing@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:9", "UnresolvedBinding_Kd 1:35")]
    [InlineData("r19x", "let r = Kimi.Intrinsics.makeRc(1)\nlet w = Kimi.Intrinsics.downgrade(r@ref)\nlet n: i32 = w\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9")]
    [InlineData("g01x", "func f(a: ref/Array<Weak<rc/i32>>) => ()\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:21")]
    [InlineData("g02x", "let f = Kimi.Intrinsics.downgrade\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:9")]
    [InlineData("g03x", "func keep(w: ref/Weak<rc/i32>) -> i32 => 1\nlet n = keep(1)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:18")]
    [InlineData("g04x", "func make(r: ref/(rc/i32)) -> Weak<rc/i32> => Kimi.Intrinsics.downgrade(r)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:31", "Unsupported_Kd 1:47")]
    [InlineData("g05x", "func g<T>(r: ref/(rc/T)) -> i32\n    T is ObjectPayload\n    let w = Kimi.Intrinsics.downgrade(r)\n    return 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 3:13")]
    [InlineData("g07x", "struct Node\n    public let selfWeak: Weak<rc/Node>\n    public init(selfWeak: Weak<rc/Node>)\n        self.selfWeak = selfWeak@move\n\nlet build = func [] (weak: Weak<rc/Node>) -> Node\n    let before = Kimi.Intrinsics.upgrade(weak)\n    return Node.init(weak@move)\nlet node = Kimi.Intrinsics.makeRcCyclic(build)\nlet after = Kimi.Intrinsics.upgrade(node.selfWeak)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:26", "Unsupported_Kd 3:27", "Unsupported_Kd 6:28", "Unsupported_Kd 7:18", "Unsupported_Kd 9:12", "Unsupported_Kd 10:13")]
    [InlineData("g11x", "func f(w: ref/Weak<rc/i32>) -> i32\n    let c = Kimi.Intrinsics.clone(w)\n    return 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:15")]
    [InlineData("h05x", "let f = Kimi.Intrinsics.downgrade<rc/i32>\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:9")]
    [InlineData("h13x", "func make(w: i32) -> i32 => w\nlet n = Kimi.Intrinsics.makeRcCyclic(make)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9")]
    [InlineData("j01x", "func apply<T>(f: T) -> i32 => 1\nlet n = apply(Kimi.Intrinsics.downgrade)\n", "Unsupported_Kd 2:15")]
    [InlineData("p03x", "func f(w: ref/Weak<rc/i32>) -> i32 => 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:15")]
    [InlineData("p04x", "func f(w: (Weak<rc/i32>)) -> i32 => 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:12")]
    [InlineData("l01x", "func f(a: ref/[Kimi.Intrinsics.downgrade of i32]) => ()\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:16")]
    [InlineData("q04x", "let r = Kimi.Intrinsics.makeRc(1)\nlet w = ::Kimi.Intrinsics.downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9")]
    [InlineData("f02x", "func f(x: Weak<rc/i32>.(Iterator).Item) -> i32 => 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:11")]
    // Valid: no `#Unsupported` declaration is used (a user `Weak`, excluded syntax, the strong `clone`, a mixed group's other winner).
    [InlineData("e1v/g12v", "let r = Kimi.Intrinsics.makeRc(1)\nlet c = Kimi.Intrinsics.clone(r@ref)\nConsole.writeLine(\"ok\")\n")]
    [InlineData("r13v", "struct Weak\n    public let x: i32\n    public init(x: i32)\n        self.x = x\nfunc g(w: ref/Weak) -> i32 => w.x\nlet v = Weak.init(x: 2)\nConsole.writeLine(\"\\(g(v@ref))\")\n")]
    [InlineData("g06v", "#if false\nfunc f(w: ref/Weak<rc/i32>) => ()\nConsole.writeLine(\"ok\")\n")]
    [InlineData("i07v", "alias A\nalias B\ngroup A\n    public func pick(value: bool) -> i32 => 1\ngroup B\n    public func pick(value: i32) -> i32 => value\nlet f: (i32) -> i32 = pick\nConsole.writeLine(\"ok\")\n")]
    [InlineData("i08v", "alias B\nalias A\ngroup A\n    public func pick(value: bool) -> i32 => 1\ngroup B\n    public func pick(value: i32) -> i32 => value\nlet f: (i32) -> i32 = pick\nConsole.writeLine(\"ok\")\n")]
    [InlineData("i10v", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func makeRcCyclic(build: (i32) -> i32) -> i32 => build(1)\nlet n = makeRcCyclic(func [] (x: i32) -> i32 => x)\nConsole.writeLine(\"ok\")\n")]
    [InlineData("i12v", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func makeRcCyclic(build: (i32) -> i32) -> i32 => build(1)\nlet n = makeRcCyclic(func [] (x: i32) -> i32 => x)\nConsole.writeLine(\"ok\")\n")]
    // Invalid by another rule, where no `#Unsupported` declaration is selected: the records are that rule's.
    [InlineData("g10x", "let r = Kimi.Intrinsics.makeRc(1)\nlet w = Kimi.downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "UnresolvedBinding_Kd 2:9")]
    [InlineData("g13x", "alias W => Weak\nfunc f(w: ref/W<rc/i32>) => ()\nConsole.writeLine(\"ok\")\n", "UnresolvedBinding_Kd 1:12 x4", "UnresolvedBinding_Kd 2:15 x1")]
    [InlineData("g14x", "alias D => Kimi.Intrinsics.downgrade\nConsole.writeLine(\"ok\")\n", "UnresolvedBinding_Kd 1:12 x25")]
    // Moved by R8-G1-3b-2 to `ParameterShapeMismatch_Kd` at the use: the library's declared `downgrade<s/T>(value: ref/(s/T))` and
    // Mine's `downgrade(value: i32)` gathered at the alias stage fail SPEC 9.5.1's check (7.4.3: an original s/T overlaps every key;
    // 7.4.4 step 3), so every use is an error and no candidate is selected. R8's mixed groups are i09x-i12v and the hook's u rows.
    [InlineData("h07v", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet n = downgrade(1)\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:9")]
    [InlineData("h09v", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet n = downgrade(1)\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:9")]
    [InlineData("i03v", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet f: (i32) -> i32 = downgrade\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:23")]
    [InlineData("i04v", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet f: (i32) -> i32 = downgrade\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:23")]
    [InlineData("i01x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet n = downgrade(\"x\")\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:9")]
    [InlineData("i02x", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet n = downgrade(\"x\")\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:9")]
    [InlineData("h08x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet r = makeRc(1)\nlet w = downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 6:9")]
    [InlineData("h10x", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet r = makeRc(1)\nlet w = downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 6:9")]
    [InlineData("i05x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet f = downgrade<rc/i32>\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:9")]
    [InlineData("i06x", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nlet f = downgrade<rc/i32>\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 5:9")]
    [InlineData("n01x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nfunc outer<T>(v: T) -> i32 => 1\nlet r = makeRc(1)\nlet n = outer(downgrade(r@ref))\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 7:15")]
    [InlineData("n02x", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: i32) -> i32 => value\nfunc outer<T>(v: T) -> i32 => 1\nlet r = makeRc(1)\nlet n = outer(downgrade(r@ref))\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 7:15")]
    // R8 with the library's `downgrade` (the rev 4 review's twins, N4): a user `downgrade` whose position-0 parameter is absent,
    // Object-keyed or Value-keyed and Shared gathers with `downgrade<s/T>(value: ref/(s/T))` (SPEC 7.4.4 step 1), so selection decides:
    // the library's winner or item is the use (one `Unsupported_Kd`), the user's winner none, also through a GenericsKoto callee
    // (tw11v, tw12v), a nested call (tw10x) and a parenthesized item (tw20x); a Value-keyed Exclusive one fails 9.5.1 (tw07x). tw16v:
    // `upgrade(value: i32)` does not overlap `ref/Weak<s/T>`.
    [InlineData("tw01x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: ref/i32) -> i32 => 1\nlet r = makeRc(1)\nlet w = downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 6:9")]
    [InlineData("tw02x", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: ref/i32) -> i32 => 1\nlet r = makeRc(1)\nlet w = downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 6:9")]
    [InlineData("tw03v", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: ref/i32) -> i32 => 1\nlet k: i32 = 5\nlet n = downgrade(k@ref)\nConsole.writeLine(\"ok\")\n")]
    [InlineData("tw04v", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: ref/i32) -> i32 => 1\nlet k: i32 = 5\nlet n = downgrade(k@ref)\nConsole.writeLine(\"ok\")\n")]
    [InlineData("tw05x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade() -> i32 => 1\nlet r = makeRc(1)\nlet w = downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 6:9")]
    [InlineData("tw06v", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade() -> i32 => 1\nlet n = downgrade()\nConsole.writeLine(\"ok\")\n")]
    [InlineData("tw07x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: uniq/i32) -> i32 => 1\nvar k: i32 = 5\nlet n = downgrade(k@uniq)\nConsole.writeLine(\"ok\")\n", "ParameterShapeMismatch_Kd 6:9")]
    [InlineData("tw08x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: rc/i32) -> i32 => 1\nlet r = makeRc(1)\nlet w = downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 6:9")]
    [InlineData("tw09x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: ref/i32) -> i32 => 1\nlet f = downgrade<rc/i32>\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 5:9")]
    [InlineData("tw10x", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade(value: ref/i32) -> i32 => 1\nfunc outer<T>(v: T) -> i32 => 1\nlet r = makeRc(1)\nlet n = outer(downgrade(r@ref))\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 7:15")]
    [InlineData("tw11v", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade<T>(value: ref/T) -> i32 => 1\nlet k: i32 = 5\nlet n = downgrade<i32>(k@ref)\nConsole.writeLine(\"ok\")\n")]
    [InlineData("tw12v", "alias Mine\nalias Kimi.Intrinsics\ngroup Mine\n    public func downgrade<T>(value: ref/T) -> i32 => 1\nlet k: i32 = 5\nlet n = downgrade<i32>(k@ref)\nConsole.writeLine(\"ok\")\n")]
    [InlineData("tw16v", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func upgrade(value: i32) -> i32 => value\nlet n = upgrade(1)\nConsole.writeLine(\"ok\")\n")]
    [InlineData("tw20x", "alias Kimi.Intrinsics\nalias Mine\ngroup Mine\n    public func downgrade(value: ref/i32) -> i32 => 1\nlet f = (downgrade<rc/i32>)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 5:10")]
    // The class F3 belongs to (N2): programs the base accepted only because the library lacked a required declaration (SPEC 22.1),
    // now rejected by the rule that reads it: 9.5.1's gathered-group check above, or 9.7's Type ambiguity (tw18x: `Weak<i32>` has
    // two arity-1 candidates, Kimi.Weak and Mine.Weak); 9.7's arity filter keeps an arity-0 `Weak` valid (tw19v).
    [InlineData("tw18x", "alias Kimi\nalias Mine\ngroup Mine\n    public struct Weak<T>\n        public let x: i32\nfunc f(w: ref/Weak<i32>) -> i32 => w.x\nConsole.writeLine(\"ok\")\n", "AmbiguousBinding_Kd 6:15")]
    [InlineData("tw19v", "alias Kimi\nalias Mine\ngroup Mine\n    public struct Weak\n        public let x: i32\nfunc f(w: ref/Weak) -> i32 => w.x\nConsole.writeLine(\"ok\")\n")]
    // Moved by R8-G1-3b-2 to `Unsupported_Kd` at the use from a Language record on the miss (q03x at the `Kimi.Weak` member 1:13). An
    // alias target's walk records where it fails, as every Type use's does, and the alias rests on that record (F's rulings N1-5 (4),
    // N1-7 (2), (3)): g15x at the target 1:12, h03x the target's 1:7 alone, g13x and g14x at their missing component.
    [InlineData("r12x", "func f(w: ref/Kimi.Weak<rc/i32>) => ()\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:15")]
    [InlineData("g08x", "struct Weak\n    public let x: i32\nfunc f(w: ref/Kimi.Weak<rc/i32>) => ()\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 3:15")]
    [InlineData("g09x", "func f(w: ref/::Kimi.Weak<rc/i32>) => ()\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:17")]
    [InlineData("g15x", "alias W => Kimi.Weak\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:12 x9")]
    [InlineData("h01x", "alias Kimi.Intrinsics\nlet r = makeRc(1)\nlet w = downgrade(r@ref)\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 3:9")]
    [InlineData("h02x", "alias Kimi.Intrinsics\nlet f = downgrade\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9")]
    [InlineData("h03x", "alias Kimi.Weak<rc/i32>\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:7")]
    [InlineData("h04x", "let w = Weak<rc/i32>.init()\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:9")]
    [InlineData("h11x", "alias Kimi.Intrinsics\nlet f = downgrade<rc/i32>\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 2:9")]
    [InlineData("i09x", "alias Kimi.Intrinsics\nalias Mine\nstruct Node\n    public let v: i32\ngroup Mine\n    public func makeRcCyclic(build: (i32, i32) -> i32) -> i32 => build(1, 2)\nlet node = makeRcCyclic(func [] (weak) -> Node => Node.init(v: 1))\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 7:12")]
    [InlineData("i11x", "alias Mine\nalias Kimi.Intrinsics\nstruct Node\n    public let v: i32\ngroup Mine\n    public func makeRcCyclic(build: (i32, i32) -> i32) -> i32 => build(1, 2)\nlet node = makeRcCyclic(func [] (weak) -> Node => Node.init(v: 1))\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 7:12")]
    [InlineData("j02x", "alias Kimi.Intrinsics\nfunc apply<T>(f: T) -> i32 => 1\nlet n = apply(downgrade)\n", "Unsupported_Kd 3:15")]
    [InlineData("k01x", "alias Kimi.Intrinsics\nlet n = upgrade(1)\n", "Unsupported_Kd 2:9")]
    [InlineData("q02x", "func f(x: Kimi.Weak<rc/i32>.Foo) -> i32 => 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:11")]
    [InlineData("q03x", "func f(x: ::Kimi.Weak<rc/i32>.Foo) -> i32 => 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:13")]
    [InlineData("f01x", "func f(x: Kimi.Weak<rc/i32>.(Iterator).Item) -> i32 => 1\nConsole.writeLine(\"ok\")\n", "Unsupported_Kd 1:11")]
    // Moved by R8-G1-3b-2 to `UnresolvedBinding_Kd`: a function is no Type (SPEC 9.2), so none is used and the Name is missing.
    [InlineData("t01x", "func f(x: Kimi.Intrinsics.downgrade) -> i32 => 1\nConsole.writeLine(\"ok\")\n", "UnresolvedBinding_Kd 1:11")]
    [InlineData("w01x", "func work<length N>() -> i32 => 1\nlet n = work<Kimi.Intrinsics.downgrade>()\nConsole.writeLine(\"ok\")\n", "NoApplicableOverload_Kd 2:9", "UnresolvedBinding_Kd 2:14")]
    [InlineData("w02x", "func work<length N>() -> i32 => 1\nlet f: () -> i32 = work<Kimi.Intrinsics.downgrade>\nConsole.writeLine(\"ok\")\n", "UnresolvedBinding_Kd 2:25")]
    // Recorded as the base has them: h06x's `InvalidConstraint_Kd` pair is an open issue of its own (a constraint over a failed
    // requirement Name); h12x's other records are its own errors.
    [InlineData("h06x", "func f<T>(x: T) -> i32\n    T is Weak<rc/i32>\n    return 1\nConsole.writeLine(\"ok\")\n", "InvalidConstraint_Kd 1:6", "Unsupported_Kd 2:10", "InvalidConstraint_Kd 2:10")]
    [InlineData("h12x", "func ident<T>(x: T) -> T => x\nlet n = Kimi.Intrinsics.makeRcCyclic(ident)\n", "TransferRequired_Kd 1:29", "Unsupported_Kd 2:9", "UnboundTypeArgument_Kd 2:38")]
    public void UnsupportedDeclarationUsesAreLocated(string row, string source, params string[] expected)
    {
        // A row naming a record's span (` xN`, F's ruling N1-13 C1) is compared with it.
        var output = DiagnosticCorpus.Check(source);
        var errors = output.Diagnostics.Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error)
            .Select(x => At(source, x.Span!.Value.Start, x.Code) is var at && expected.Contains($"{at} x{x.Span!.Value.Length}") ? $"{at} x{x.Span!.Value.Length}" : at);
        Assert.Equal(expected.Order(StringComparer.Ordinal), errors.Order(StringComparer.Ordinal));
        Assert.True(output.Accepted == (expected.Length == 0), row);
    }

    // SPEC 22.1, 23.1.1, 23.1.2 (R8-G1-3b's verdict where a selection is handed on; the Front's C-346 C2 and review N1): test-only
    // `#Unsupported` library structs with member Types reach the verdict's refusing branch, which no library declaration reaches before
    // R8-G1-3b-2. Each use outside them is one Unsupported_Kd at the Name that names one, whether the Name ends the path, qualifies a
    // member (no later step republishes it; deeper members, a group member, an inherited member Type) or is a `::` path's root name, and
    // independent uses each have their own; a mention inside an `#Unsupported` declaration is no use. The valid twins open a plain struct
    // (t14v, SPEC 18.2.1) and inherit a member Type from a plain open base (t18v, SPEC 6.2.2, 9.6). A use in the library text is a
    // broken build (ALibraryUseOfAnUnsupportedDeclarationIsABrokenBuild); the rows also pin that the hook adds no record of its own
    // (G1-2 binds library Attributes). The u rows'
    // group ZF is wholly `#Unsupported`, as every library group is: alone it fails the Name (u01x, u02x); gathered beside a user group
    // (SPEC 9.5.1, R8) only its selection is a use, by a call's winner (u05x, u06x) or a function item (u07x), in either alias order.
    [Theory]
    [InlineData("z01x", "", "func f(v: ref/ZProbe) => ()\n", "Unsupported_Kd 1:15")]
    [InlineData("z02x", "", "func f(v: ref/ZProbe.Inner) => ()\n", "Unsupported_Kd 1:15")]
    [InlineData("z03x", "", "func f(v: ref/Kimi.ZProbe.Inner) => ()\n", "Unsupported_Kd 1:15")]
    [InlineData("z04x", "", "func f(v: ref/::Kimi.ZProbe.Inner) => ()\n", "Unsupported_Kd 1:17")]
    [InlineData("z05x", "", "func f(v: ref/::Kimi.ZProbe) => ()\n", "Unsupported_Kd 1:17")]
    [InlineData("z06v", "", "func f(v: ref/i32) => ()\n")]
    [InlineData("z09v", "#Unsupported\npublic group ZOwn\n    public func zown(v: ref/::ZProbe.Inner) => ()\n", "func f(v: ref/i32) => ()\n")]
    [InlineData("t01x", "", "func f(v: ref/ZProbe.Inner.Deep) => ()\n", "Unsupported_Kd 1:15")]
    [InlineData("t02x", "", "func f(v: ref/Kimi.ZProbe.Inner.Deep) => ()\n", "Unsupported_Kd 1:15")]
    [InlineData("t03x", "", "func f(v: ref/::Kimi.ZProbe.Inner.Deep) => ()\n", "Unsupported_Kd 1:17")]
    [InlineData("t05x", "", "func f(v: ref/ZG.ZM.Inner) => ()\n", "Unsupported_Kd 1:15")]
    [InlineData("t12x", "", "func f(a: ref/ZProbe, b: ref/ZProbe.Inner) => ()\n", "Unsupported_Kd 1:15", "Unsupported_Kd 1:30")]
    [InlineData("t14v", "", "alias Kimi.ZPlain\nfunc f(v: ref/Inner) => ()\n")]
    [InlineData("t18v", "", "struct D : ZOpenPlain\n    let a: i32\nfunc f(v: ref/D.Inner) => ()\n")]
    [InlineData("t19x", "", "func f(v: ref/ZDer.Inner) => ()\n", "Unsupported_Kd 1:15")]
    [InlineData("u01x", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "alias Kimi.ZF\nlet n = zf(1)\n", "Unsupported_Kd 2:9")]
    [InlineData("u02x", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "let n = Kimi.ZF.zf(1)\n", "Unsupported_Kd 1:9")]
    [InlineData("u03v", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "alias Kimi.ZF\nalias Mine\ngroup Mine\n    public func zf(v: bool) -> i32 => 1\nlet n = zf(true)\n")]
    [InlineData("u04v", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "alias Mine\nalias Kimi.ZF\ngroup Mine\n    public func zf(v: bool) -> i32 => 1\nlet n = zf(true)\n")]
    [InlineData("u05x", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "alias Kimi.ZF\nalias Mine\ngroup Mine\n    public func zf(v: bool) -> i32 => 1\nlet n = zf(1)\n", "Unsupported_Kd 5:9")]
    [InlineData("u06x", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "alias Mine\nalias Kimi.ZF\ngroup Mine\n    public func zf(v: bool) -> i32 => 1\nlet n = zf(1)\n", "Unsupported_Kd 5:9")]
    [InlineData("u07x", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "alias Kimi.ZF\nalias Mine\ngroup Mine\n    public func zf(v: bool) -> i32 => 1\nlet f: (i32) -> i32 = zf\n", "Unsupported_Kd 5:23")]
    [InlineData("u08v", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n", "alias Kimi.ZF\nalias Mine\ngroup Mine\n    public func zf(v: bool) -> i32 => 1\nlet f: (bool) -> i32 = zf\n")]
    [InlineData("u10v", "public group ZF\n    #Unsupported\n    public func zf(v: i32) -> i32 => v\n    #Unsupported\n    public func zu() -> i32 => zf(1)\n", "func f(v: ref/i32) => ()\n")]
    public void AnUnsupportedDeclarationIsRefusedWhereItIsHandedOn(string row, string library, string source, params string[] expected)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        CompilationTestHelper.UseEditedLibrary(c, x => x.Kotonoha.CreateCodeContext().Parse(x.Kotonoha.RootKoto, UnsupportedProbe + library));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        c.Bind();
        var records = c.Binding.Issues.Select(x => ReferenceEquals(x.Node.Kotonoha, c.Kotonoha) ? At(source, x.Node.Span.Start, x.Code) : $"{x.Code} elsewhere at {x.Node.Span.Start}");
        Assert.True(expected.Order(StringComparer.Ordinal).SequenceEqual(records.Order(StringComparer.Ordinal)), $"{row}: {string.Join(", ", records)}");
    }

    // DECISIONS BU-219 (PLAN G949 S1-1's freeze): the Kimi library is a compiler-build constant, so a library whose pass has a record is a
    // broken build, refused before anything is frozen. From PLAN G949 Stage 2 U2a the refusal is a value, not a thrown fault (B's ruling 6;
    // the Front's C-380 16:55 consent, (d)): the Bind leaves no result and binds no program, and the check publishes the library's record
    // as InternalInvariant_Kd at its library location, `ZProbe` in `zuse`'s `::ZProbe.Inner`, beside the refusal's own InternalInvariant_Kd
    // at the same place, the first record the freeze counted (B's ruling 40 (i)), and nothing else.
    // The former rows z07x, z08x, t23x and u09x (a library declaration naming an `#Unsupported` library declaration, "L Unsupported_Kd")
    // are this row, z07x's library; the same uses in a program keep their located records (z01x-z05x, t01x-t19x and the u rows).
    [Fact]
    public void ALibraryUseOfAnUnsupportedDeclarationIsABrokenBuild()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        CompilationTestHelper.UseEditedLibrary(c, static x => x.Kotonoha.CreateCodeContext().Parse(x.Kotonoha.RootKoto, UnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(v: ref/i32) => ()\n");
        Assert.Equal(default, c.Bind());
        Assert.False(c.Check());
        var library = UnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n";
        var use = new SourceSpan(library.IndexOf("::ZProbe.Inner", StringComparison.Ordinal) + 2, "ZProbe".Length);
        var records = TestDiagnostics.Of(c);
        Assert.Equal(2, records.Length);
        Assert.All(records, x =>
        {
            Assert.Equal(nameof(Kimi.DiagnosticCode.InternalInvariant_Kd), x.Code);
            Assert.Equal(c.Library.Kotonoha.Name, x.Path); // The probe's library document, not the program's.
            Assert.Equal(use, x.Span); // The use's location, as the program rows' Unsupported_Kd (z01x-z05x) has it.
        });
    }

    // SPEC 6.7, 19.4 (R8-G1 row A6): excluded syntax is checked syntactically only, so an implementation Attribute there is not
    // resolved: a user source with one is accepted without a record.
    [Fact]
    public void A6AnExcludedImplementationAttributePublishesNoRecord()
    {
        var output = DiagnosticCorpus.Check("#if false\n#Lang(\"Option\")\nfunc f() => ()\n", "Library");
        Assert.True(output.Accepted);
        Assert.Empty(output.Diagnostics);
    }

    // A check's Errors, each as its code and the 1-based line and column of its start.
    private static IEnumerable<string> ErrorsAt(string source, Kimi.Checking.CheckOutput output)
        => output.Diagnostics.Where(static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error).Select(x => At(source, x.Span!.Value.Start, x.Code));

    // A record as its code and the 1-based line and column of its start in `source`.
    private static string At(string source, int start, object code)
    {
        var line = source.AsSpan(0, start).Count('\n');
        return $"{code} {line + 1}:{start - (start == 0 ? 0 : source.LastIndexOf('\n', start - 1) + 1) + 1}";
    }
}
