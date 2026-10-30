// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.HirColumnsTest;

namespace XunitTest;

/// <summary>The HIR rows each form publishes (docs/dev/COMPILER_ARCHITECTURE.md, R2b), as SPEC states them: a parameter read from a later
/// default, an omitted default, a virtual body, completions, jump targets, Adjusts, Place shapes, expansion plans, a user index's
/// selection, accessors, constructors and built-in forms; those of the nodes Binding rejected or left unresolved are in
/// HirFormsTest.Errors.cs. Each program also satisfies the corpus contract (HirColumnsTest).</summary>
public sealed partial class HirFormsTest
{
    // The programs AnAcceptedProgramLeavesNoDiscardRowUnset sweeps: every SPEC example variant without an expected record that the old
    // path accepts too (Binding records none), the library and the milestone programs.
    public static TheoryData<string> AcceptedPrograms
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var example in DiagnosticCorpus.SpecCases)
            {
                for (var i = 0; i < example.Variants.Length; i++)
                {
                    if (example.Variants[i] is { Expected.Length: 0 } variant && (variant.Old?.Records.Length ?? 0) == 0)
                    {
                        data.Add($"spec/{example.Name}/{i}");
                    }
                }
            }

            foreach (var name in SemanticSnapshotTest.CaseNames())
            {
                if (name == "library" || name.StartsWith("milestone/", StringComparison.Ordinal))
                {
                    data.Add(name);
                }
            }

            return data;
        }
    }

    // SPEC 7.2.3: a later default reads the slot its call prepared for an earlier parameter; a closure there reads its capture.
    [Fact]
    public void ParametersReadInLaterDefaultsAreThePreparedSlots()
    {
        var c = MinimalEmissionTest.Analyze("func run(k: i32, m: i32 = k + 1, action: (i32) -> i32 = func [k] (v) => v + k, other: (i32) -> i32 = func (v) => v + m) -> i32 => action(m) + other(k)\n" +
            "require run(1) == 6 else => $abort(\"run\")\n");
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var function = Reachable(c.Kotonoha).OfType<FunctionKoto>().Single(static x => x.Name == "run");
        var reads = Reachable(c.Kotonoha).Where(x => x is IdentifierNameKoto && HirTables.Op(x) == HirOp.Name && IsWithin(x, function)).Select(static x => HirTables.ResOf(x));

        // k in the default of m; v, the capture k; v, the capture m; then action, m, other and k in the body.
        Assert.Equal([HirRes.PreparedParam, HirRes.Param, HirRes.Capture, HirRes.Param, HirRes.Capture, HirRes.Param, HirRes.Param, HirRes.Param, HirRes.Param], reads);
    }

    // Plan 5.6.2: a call omitting a default evaluates its selected declaration's default, whose earlier parameters are prepared slots and
    // whose Types the call's substitution instantiates.
    [Fact]
    public void OmittedDefaultsAreTheirDeclarationsDefaults()
    {
        var c = MinimalEmissionTest.Analyze("func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = copy(x)) -> T\n    T is Copy\n    return y\n" +
            "require f(7) == 7 and f(true) else => $abort(\"call\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var f = Reachable(c.Kotonoha).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var calls = Reachable(c.Kotonoha).OfType<InvocationKoto>().Select(static x => CallPlan.Of(x)).Where(x => ReferenceEquals(x?.Target.Declaration, f)).ToArray();
        Assert.Equal([BoundType.I32, BoundType.Boolean], calls.Select(x => c.Types.Apply(DefaultDecl.Of(x!.DefaultArguments[0]).Parameter(1), c.Types.SubstOf(x)!)));
        var declaration = DefaultDecl.Of(calls[0]!.DefaultArguments[0]);
        Assert.Equal((f, 1, HirOp.Call), (declaration.Function, declaration.Index, HirTables.Op(declaration.Expression)));
        Assert.Same(declaration.Parameter(0), declaration.Parameter(1));
        Assert.Equal(HirRes.PreparedParam, HirTables.ResOf(((InvocationKoto)declaration.Expression).ArgumentNodes[0]));
    }

    // SPEC 17.4.2, 22.5.1: the macro owns the call and its message acquisition; its inner invocation is covered syntax.
    [Theory]
    [InlineData("$abort(\"body\")")]
    [InlineData("require false else => $abort(\"require\")")]
    [InlineData("func f(message: string = $abort(\"default\")) => ()\nf()")]
    [InlineData("$abort($abort(\"inner\"))")]
    public void AbortCallsArePublishedAtTheirMacros(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var macros = Reachable(c.Kotonoha).OfType<MacroKoto>().ToArray();
        Assert.NotEmpty(macros);
        foreach (var macro in macros)
        {
            var invocation = Assert.IsType<InvocationKoto>(macro.Operand);
            Assert.Equal((HirOp.Call, HirOp.Syntax), (HirTables.Op(macro), HirTables.Op(invocation)));
            Assert.Same(invocation.CallOf(), CallPlan.Of(macro));
            Assert.Null(CallPlan.Of(macro, 1));
            Assert.Equal(HirCompletion.Diverges, HirTables.CompletionOf(macro));
            Assert.Equal(HirCompletion.Unset, HirTables.CompletionOf(invocation));
            Assert.Equal(HirAcquire.Unset, HirTables.AdjustOf(invocation).Acquire);
            Assert.NotEqual(HirAcquire.Unset, HirTables.AdjustOf(invocation.ArgumentNodes[0]).Acquire);
        }
    }

    // A virtual or override body Binding bound is built, the conditional one too; the corpus has none.
    [Fact]
    public void VirtualBodiesArePublished()
    {
        var c = MinimalEmissionTest.Analyze(VirtualSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var functions = Reachable(c.Kotonoha).OfType<FunctionKoto>().Where(static x => x.IsVirtual || x.IsOverride).ToArray();
        Assert.Equal(7, functions.Length);
        Assert.All(functions, static x => Assert.Equal(HirOp.Body, HirTables.Op(x)));
    }

    // SPEC 14.10: a return, an exit, a call of a Never function and a loop no exit leaves diverge, and so does an if or a match
    // whose every arm diverges; a loop an exit leaves completes.
    [Fact]
    public void CompletionFollowsTheNeverRule()
    {
        var c = MinimalEmissionTest.Analyze("func choose(c: bool) -> i32\n    if c\n        return 1\n    else\n        return 2\n" +
            "func pick(x: i32) -> i32\n    match x\n        0 => return 1\n        _ => $abort(\"pick\")\n" +
            "func spin() -> i32\n    var i: i32 = 0\n    loop\n        i = i + 1\n" +
            "func count() -> i32\n    var i: i32 = 0\n    loop\n        require i < 3 else => exit\n        i = i + 1\n    return i\n" +
            "require choose(true) == 1 and pick(0) == 1 and count() == 3 else => $abort(\"completion\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var nodes = Reachable(c.Kotonoha);
        var functions = nodes.OfType<FunctionKoto>().Where(static x => x.Name is "choose" or "pick" or "spin" or "count").ToDictionary(static x => x.Name);
        HirCompletion First<T>(string function)
            where T : Koto => HirTables.CompletionOf(nodes.OfType<T>().First(x => IsWithin(x, functions[function])));

        Assert.Equal(HirCompletion.Diverges, First<IfKoto>("choose"));
        Assert.Equal(HirCompletion.Diverges, First<JumpKoto>("choose"));
        Assert.Equal(HirCompletion.Diverges, First<MatchKoto>("pick"));
        Assert.Equal(HirCompletion.Diverges, First<MacroKoto>("pick"));
        Assert.Equal(HirCompletion.Diverges, First<LoopKoto>("spin"));
        Assert.Equal(HirCompletion.Completes, First<LoopKoto>("count"));
        Assert.Equal(HirCompletion.Diverges, First<JumpKoto>("count"));
        Assert.Equal(HirCompletion.Completes, First<RequireKoto>("count"));
    }

    // SPEC 7.2.3, 14.1: a default's transfers target only constructs inside it, so its completion is its own: a default that never
    // completes leaves the body of its function completing.
    [Theory]
    [InlineData("do => 1", true)]
    [InlineData("loop => exit 1", true)]
    [InlineData("label choice: do => exit to choice 1", true)]
    [InlineData("if true => 1 else => 2", true)]
    [InlineData("loop => continue", false)]
    public void ADefaultHasItsOwnCompletion(string expression, bool completes)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32 = (" + expression + ")) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var function = Reachable(c.Kotonoha).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        Assert.Equal(completes ? HirCompletion.Completes : HirCompletion.Diverges, HirTables.CompletionOf(function.Parameters[0].DefaultValue!));
        Assert.Equal(HirCompletion.Completes, HirTables.CompletionOf(function.ExpressionBody!));
    }

    // SPEC 14.7: a yield in a selection inside a match arm ends that selection, not the match.
    [Fact]
    public void AYieldInAMatchArmTargetsItsSelection()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: bool) -> i32 => match x\n    true => (if x\n        yield 1\n    else\n        yield 2\n    )\n    false => 0");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var f = Reachable(c.Kotonoha).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var jumps = Reachable(c.Kotonoha).Where(x => HirTables.Op(x) is HirOp.Jump or HirOp.Continue && IsWithin(x, f)).ToArray();
        Assert.Equal(2, jumps.Length);
        Assert.All(jumps, static x => Assert.IsType<IfKoto>(HirTables.PlanOf(Assert.IsType<YieldKoto>(x)).Plan));
    }

    // SPEC 14.5.2, 14.7: the jump binding writes a transfer's target row only when the transfer may go there; with no target (none
    // in scope, behind a defer or a default) or an unlabeled yield whose selection discards its result the row is null, and Binding
    // records row 9 or 10 at the jump, from which the MIR's jump is derived, with no record of its own (DECISIONS R5-M42).
    [Theory]
    [InlineData("loop\n    yield 1", "InvalidJumpTarget_Kd")]
    [InlineData("func f()\n    defer => return", "InvalidJumpTarget_Kd")]
    [InlineData("func f(x: i32 = (return 1)) => ()", "InvalidJumpTarget_Kd")]
    [InlineData("if true\n    yield 1", "UnlabeledYieldTarget_Kd")]
    public void AnInvalidTransferHasANullTargetRow(string source, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        HirTables.Publish(c);
        var jump = Assert.Single(Reachable(c.Kotonoha).OfType<JumpKoto>());
        Assert.True(HirTables.Op(jump) is HirOp.Jump or HirOp.Continue, MinimalEmissionTest.Describe(c));
        Assert.Null(HirTables.PlanOf(jump).Plan);
        Assert.True(HoldsItsTarget(jump));
        Assert.Contains(CompilationTestHelper.BindingErrors(c), x => x.Code == code && x.Span == jump.Span);
        Assert.DoesNotContain(c.BuildMir().Records, x => x.Subject == SourceRef.Of(jump));
    }

    // SPEC 3.5, 10.2, 15.1.5, 15.1.6: a Copy Place is Copied, a stored exclusive reference Reborrowed, a Place a ref parameter takes
    // shared-borrowed in place, an @move operand moved, a Subject borrowed in place, and a value taken as a Temporary.
    [Fact]
    public void AdjustStatesTheRecordedOperations()
    {
        const string Source = "func read(p: ref/i32) -> i32 => p@follow\nfunc bump(target: uniq/i32) => target@follow += 1\nfunc count(text: string) -> i32 => 1\n" +
            "func twice(slot: uniq/i32)\n    bump(slot)\nfunc pick(value: Option<i32>) -> i32\n    match value\n        .Some(let inner) => return inner\n        .None => return 0\n" +
            "var number: i32 = 1\nlet owned = \"owned\"\ntwice(number@uniq)\nlet copied = number\n" +
            "require read(number) == 2 and count(owned@move) == 1 and copied == 2 and pick(.Some(5)) == 5 else => $abort(\"adjust\")\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var adjusted = Reachable(c.Kotonoha).Where(static x => HirTables.AdjustOf(x).Acquire != HirAcquire.Unset)
            .Select(x => $"{Source.Substring(x.Span.Start, x.Span.Length)} {HirTables.AdjustOf(x).Acquire}/{HirTables.AdjustOf(x).Adaptation}");
        // The require's condition operands and the abort message are Temporaries.
        string[] expected =
        [
            "p@follow Copy/None", "p None/None", "target@follow None/None", "target None/None", "1 Temporary/None", "1 Temporary/None",
            "slot Reborrow/None", "value None/SharedBorrow", "let inner None/SharedBorrow", "inner None/ReferentRead", "0 Temporary/None",
            "1 Temporary/None", "\"owned\" Temporary/None", "number@uniq Temporary/None", "number None/None", "number Copy/None",
            "read(number) == 2 and count(owned@move) == 1 and copied == 2 and pick(.Some(5)) == 5 Temporary/None",
            "read(number) == 2 and count(owned@move) == 1 and copied == 2 Temporary/None", "read(number) == 2 and count(owned@move) == 1 Temporary/None",
            "read(number) == 2 Temporary/None", "read(number) Temporary/None", "number None/SharedBorrow", "2 Temporary/None",
            "count(owned@move) == 1 Temporary/None", "count(owned@move) Temporary/None", "owned@move Temporary/None", "owned Move/None",
            "1 Temporary/None", "copied == 2 Temporary/None", "copied Copy/None", "2 Temporary/None", "pick(.Some(5)) == 5 Temporary/None",
            "pick(.Some(5)) Temporary/None", ".Some(5) Temporary/None", "5 Temporary/None", "5 Temporary/None",
            "\"adjust\" Temporary/None",
        ];
        Assert.Equal(expected, adjusted);
    }

    // SPEC 3.5, 8.14: a bare Place whose Type's Copy is unproven needs a Move (the ownership analysis reports TransferRequired), and a pair
    // layer whose every admitted case Copies or Reborrows is acquired by case.
    [Fact]
    public void ABarePlaceIsAcquiredAsBindingDecides()
    {
        var c = MinimalEmissionTest.Analyze("func keep<T>(x: T) -> T\n    T is Owned\n    return x\nfunc pass<s/T>(value: s/T) -> s/T\n    s is owner or uniq\n    T is Copy\n" +
            "    return value\nrequire keep(1) == 1 else => $abort(\"bare\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        Assert.Equal([HirAcquire.Move, HirAcquire.ByCase], Reachable(c.Kotonoha).OfType<JumpKoto>().Select(static x => HirTables.AdjustOf(x.Expression!).Acquire));
    }

    // SPEC 3.4.3, 4.7.1, 15.1.3, 15.1.6: a Field through two reference layers follows both, a literal fixed-array position is a
    // ConstIndex and a runtime one an Index, a user index designates the referent of its selected call's reference, which an update through its Field takes by indexUniq
    // (its receiver borrowed exclusively) and a read by index, and a borrowed Case payload is its Downcast.
    [Fact]
    public void PlaceShapeStatesThePlaceForms()
    {
        const string Source = "struct Point\n    public var x: i32\n    public init(x: i32) => self.x = x\n" +
            "struct Pair<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    var second: T\n" +
            "    public init(first: T, second: T)\n        self.first = first@move\n        self.second = second@move\n" +
            "    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq\n" +
            "func through(p: ref/(ref/Point during a)) -> i32 => p.x\nfunc pick(value: Option<Point>) -> i32\n    match value\n        .Some(let inner) => return inner.x\n        .None => return 0\n" +
            "var pts = Pair<Point>.init(Point.init(1), Point.init(2))\npts[0].x = 5\nlet fixed: [3 of i32] = [1, 2, 3]\nlet i: isize = 2\nlet p = Point.init(3)\nlet r = p@ref\n" +
            "require through(r@ref) == 3 and pick(.Some(Point.init(4))) == 4 and pts[1].x == 5 and fixed[1] + (fixed)[i] == 5 else => $abort(\"place\")\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var start = Source.IndexOf("func through", StringComparison.Ordinal);
        var places = Reachable(c.Kotonoha).Where(x => x.Span.Start >= start && HirTables.PlaceOf(x).Kind is not (HirPlaceKind.Unset or HirPlaceKind.Value))
            .Select(x => $"{Source.Substring(x.Span.Start, x.Span.Length)} {HirTables.PlaceOf(x).Kind}+{HirTables.PlaceOf(x).Layers} {HirTables.AdjustOf(x).Adaptation}");
        // A Field's receiver keeps the access Binding recorded for it (SPEC 3.4.3: one reference through its layers, a shared access of a
        // user index's element); its layers are followed from its own Type.
        string[] expected =
        [
            "p.x Field+0 None", "p Local+2 ReferenceRead", "value Local+0 SharedBorrow", "let inner Downcast+0 SharedBorrow", "inner.x Field+0 None",
            "inner Local+1 None", "pts[0].x Field+0 None", "pts[0] CallResult+0 SharedBorrow", "pts Local+0 ExclusiveBorrow", "p Local+0 None",
            "r Local+0 None", "pts[1].x Field+0 None", "pts[1] CallResult+0 SharedBorrow", "pts Local+0 SharedBorrow", "fixed[1] ConstIndex+0 None",
            "fixed Local+0 None", "(fixed)[i] Index+0 None", "(fixed) Local+0 None", "fixed Local+0 None", "i Local+0 None",
        ];
        Assert.Equal(expected, places);
    }

    // SPEC 3.2, 15.1.3 (Q8 package §3.5 V2): a Tuple element and a fixed-array element at a literal position keep their position on their
    // own plan row (Folded), at which the builder projects; a stored Field keeps none.
    [Fact]
    public void ElementPositionsAreOnTheirOwnRows()
    {
        const string Source = "struct Point\n    public var x: i32\n    public init(x: i32) => self.x = x\n" +
            "let t = (1, 3)\nlet fixed: [3 of i32] = [1, 2, 3]\nlet p = Point.init(3)\nrequire t.1 + fixed[2] + p.x + t.0 == 10 else => $abort(\"rows\")\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var start = Source.IndexOf("require", StringComparison.Ordinal);
        var rows = Reachable(c.Kotonoha).Where(x => x.Span.Start >= start && HirTables.PlaceOf(x).Kind is HirPlaceKind.Field or HirPlaceKind.ConstIndex)
            .Select(x => $"{Source.Substring(x.Span.Start, x.Span.Length)} {HirTables.PlanOf(x).Folded?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        Assert.Equal(["t.1 1", "fixed[2] 2", "p.x -", "t.0 0"], rows);
    }

    // SPEC 4.7.1, 11.6, 13.4, SPEC 22.7: a Property update names its getter and its setter, which takes the computed value; a user
    // index its selected index or indexUniq call, a view its element or slice call; a key resolution reads the evaluated receiver's length;
    // an interpolation passed directly to writeLine is marked; a ResolvedRange loop Copies its positions.
    [Fact]
    public void ExpansionPlansNameTheirCalls()
    {
        const string Source = "struct Pair<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    var second: T\n" +
            "    public init(first: T, second: T)\n        self.first = first@move\n        self.second = second@move\n" +
            "    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq\n" + CopyPropertyEmissionTest.Meter +
            "var m = Meter.init()\nm.level += 1\nm.doubled -= 2\nvar pts = Pair<i32>.init(1, 2)\npts[0] += 5\nvar values: [2 of i32] = [1, 2]\nvar view = values.sliceUniq()\n" +
            "view[0] = 7\nlet range = (0..2).resolve(2)\nlet tail = ^1\nvar total: isize = 0\nfor i in values.indices\n    total += i\n" +
            "Console.writeLine(\"n=\\(total)\")\nlet text = \"\\(pts[1])\"\n" +
            "require m.level == 4 and view[range][1] == 2 and values[tail] == 2 and text == \"6\" else => $abort(\"expansion\")\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var start = Source.IndexOf("var m = ", StringComparison.Ordinal);
        var plans = Reachable(c.Kotonoha).Where(x => x.Span.Start >= start && ExpansionPlan.Of(x) is not null).Select(x => (Node: x, Plan: ExpansionPlan.Of(x)!.Value))
            .Select(x => $"{Source.Substring(x.Node.Span.Start, x.Node.Span.Length)} {x.Plan.Kind} {x.Plan.First?.Target.Name}/{x.Plan.Second?.Target.Name} {x.Plan.Evaluated} {x.Plan.Direct}");
        string[] expected =
        [
            "m.level += 1 CompoundAssign level.Get/level.Set 0 False", "m.doubled -= 2 CompoundAssign doubled.Get/doubled.Set 0 False", "pts[0] += 5 CompoundAssign / -1 False",
            "pts[0] Index /indexUniq -1 False", "view[0] Index /indexPositionUniq -1 False", "for i in values.indices\n    total += i Iteration / -1 False",
            "total += i CompoundAssign / -1 False", "\"n=\\(total)\" Format / -1 True", "\"\\(pts[1])\" Format / -1 False", "pts[1] Index /index -1 False",
            "view[range][1] Index / -1 False", "view[range] Index /slice -1 False", "values[tail] Index resolved/ 1 False",
        ];
        Assert.Equal(expected, plans);
        var loop = Reachable(c.Kotonoha).OfType<ForKoto>().Single();
        Assert.Null(BoundMatch.Of(loop));
        Assert.Equal(new HirAdjust(HirAcquire.Copy, HirAdaptation.None), HirTables.AdjustOf(loop.Bindings[0]));
    }

    // SPEC 4.7.1, 4.7.2, 5.2, 7.3, 10.2, 13.5.5, 15.6.2 (DECISIONS BU-8): a user index whose element Place's use needs write or exclusive
    // capability selects indexUniq: a write, an update or an exclusive borrow through a follow of the stored reference, an exclusive
    // Reborrow of it (an argument, a bare acquisition, a Subject) and an exclusive receiver of a method or a setter, by its parameter Type
    // also through an object handle's payload, and below another element; a read, a shared borrow, a shared receiver (a base's
    // included), a getter and a raw pointer's pointee or element select index, and an rc payload offers no exclusive receiver. An
    // exclusive receiver over Binding's in-place read states its own borrow.
    [Fact]
    public void AnExclusiveUseOfTheElementSelectsIndexUniq()
    {
        const string Source = """
            struct Point
                public var x: i32 = 0
                public init() => ()
                public func mutate(self: uniq/Self) => self.x = 7
                public func look(self) -> i32 => self.x
            struct Meter
                public var raw: i32 = 2
                public computed doubled: i32
                    get() -> i32 => self.raw * 2
                    set(value: i32) -> () => self.raw = value / 2
                public init() => ()
                public func bump(self: uniq/Self) => self.raw += 1
                public func level(self) -> i32 => self.raw
            open struct Base
                public var b: i32 = 0
                public init() => ()
                public func peek(self) -> i32 => self.b
            struct Derived : Base
                public var d: i32 = 0
                public init() => ()
            struct Pair<T>
                Self is UniqIndexable<isize>
                associate Element is T
                var first: T
                var second: T
                public init(first: T, second: T)
                    self.first = first@move
                    self.second = second@move
                public func index(self, key: ref/isize) -> ref/T during self => self.first@ref
                public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq
            func change(value: uniq/i32) => value@follow = 99
            func write(value: uniq/i32 during a)
                var entries: Dictionary<i32, uniq/i32 during a> = [1: value@move]
                entries[1]@follow = 99
                entries[1]@follow += 1
                let child = entries[1]@follow@uniq
                child@follow = 3
            func reborrow(value: uniq/i32 during a)
                var entries: Dictionary<i32, uniq/i32 during a> = [1: value@move]
                change(entries[1])
                let c = entries[1]
                c@follow = 4
                match entries[1]
                    let r => r@follow = 5
            func receivers(value: uniq/Meter during a)
                var entries: Dictionary<i32, uniq/Meter during a> = [1: value@move]
                entries[1].bump()
                var pts = Pair<Point>.init(Point.init(), Point.init())
                pts[0].mutate()
                require pts[1].look() == 0 else => $abort("look")
                var meters = Pair<Meter>.init(Meter.init(), Meter.init())
                meters[0].doubled = 8
                meters[1].doubled += 2
                let d = meters[1].doubled
                var grid = Pair<Pair<Point>>.init(Pair<Point>.init(Point.init(), Point.init()), Pair<Point>.init(Point.init(), Point.init()))
                grid[0][1].mutate()
                var outer = Pair<Array<Point>>.init([Point.init()], [Point.init()])
                let i: isize = 0
                outer[0][i].mutate()
            func handles(value: objuniq/Meter during a)
                var objs = Pair<obj/Meter>.init(Meter.init()@obj, Meter.init()@obj)
                objs[0].bump()
                var entries: Dictionary<i32, objuniq/Meter during a> = [1: value@move]
                entries[1].bump()
                var rcs = Pair<rc/Meter>.init(Meter.init()@rc, Meter.init()@rc)
                require rcs[0].level() == 2 else => $abort("rc")
                var ds = Pair<Derived>.init(Derived.init(), Derived.init())
                require ds[1].peek() == 0 else => $abort("base")
            func reads(value: uniq/i32 during a)
                var entries: Dictionary<i32, uniq/i32 during a> = [1: value@move]
                let r: i32 = entries[1]@follow
                let s = entries[1]@follow@ref
                let slot = entries[1]@uniq
                slot@follow@follow = 6
            func pointers()
                let p: raw/i32 = Raw.allocate(1)
                unsafe => Raw.initialize(p, 1)
                var ptrs = Pair<raw/i32>.init(p, p)
                unsafe => *ptrs[0] = 5
                unsafe => ptrs[1][0] = 6

            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var start = Source.IndexOf("func write", StringComparison.Ordinal);
        var selections = Reachable(c.Kotonoha).Where(x => x is IndexKoto && x.Span.Start >= start && ExpansionPlan.Of(x)?.Second is not null)
            .Select(x => $"{Line(x.Span.Start)} {ExpansionPlan.Of(x)!.Value.Second!.Target.Name}");
        string[] expected =
        [
            "entries[1]@follow = 99 indexUniq", "entries[1]@follow += 1 indexUniq", "let child = entries[1]@follow@uniq indexUniq", "change(entries[1]) indexUniq",
            "let c = entries[1] indexUniq", "match entries[1] indexUniq", "entries[1].bump() indexUniq", "pts[0].mutate() indexUniq",
            "require pts[1].look() == 0 else => $abort(\"look\") index", "meters[0].doubled = 8 indexUniq", "meters[1].doubled += 2 indexUniq",
            "let d = meters[1].doubled index", "grid[0][1].mutate() indexUniq", "grid[0][1].mutate() indexUniq", "outer[0][i].mutate() indexUniq",
            "objs[0].bump() indexUniq", "entries[1].bump() indexUniq", "require rcs[0].level() == 2 else => $abort(\"rc\") index",
            "require ds[1].peek() == 0 else => $abort(\"base\") index", "let r: i32 = entries[1]@follow index", "let s = entries[1]@follow@ref index",
            "let slot = entries[1]@uniq indexUniq", "unsafe => *ptrs[0] = 5 index", "unsafe => ptrs[1][0] = 6 index",
        ];
        Assert.Equal(expected, selections);
        var receiver = Reachable(c.Kotonoha).Single(x => x is IndexKoto && Line(x.Span.Start) == "pts[0].mutate()");
        Assert.Equal(new HirAdjust(HirAcquire.None, HirAdaptation.ExclusiveBorrow), HirTables.AdjustOf(receiver));
        var shared = MinimalEmissionTest.Analyze(
            Source.Replace("require rcs[0].level() == 2 else => $abort(\"rc\")", "rcs[0].bump()", StringComparison.Ordinal));
        var issue = Assert.Single(shared.Binding.Issues);
        Assert.Equal("rcs[0].bump() NoApplicableOverload_Kd", $"{issue.Node} {issue.Code}");

        static string Line(int offset) => Source[(Source.LastIndexOf('\n', offset) + 1)..Source.IndexOf('\n', offset)].Trim();
    }

    // SPEC 3.4.3, 4.7.1, 7.3, 15.1.6 (PLAN G147 (2b)): a part below an element storing reference layers is a Place reached through those
    // layers, as below a local of the element's Type: a receiver borrows it in place in the receiver's mode, and a Subject or a compared
    // operand (SPEC 13.4) in place.
    [Fact]
    public void APartBelowAStoredReferenceIsAPlace()
    {
        const string Source = """
            struct Inner
                public var x: i32 = 0
                public var name: string = ""
                public init() => ()
                public func mutate(self: uniq/Self) => self.x = 7
                public func look(self) -> i32 => self.x
            struct Point
                public var inner: Inner = Inner.init()
                public var opt: Option<string> = .None
                public var name: string = ""
                public init() => ()
                public func bump(self: uniq/Self) => self.inner.x += 1
            func parts(value: uniq/Point during a) -> i32
                var entries: Dictionary<i32, uniq/Point during a> = [1: value@move]
                entries[1].inner.mutate()
                entries[1].bump()
                match entries[1].opt
                    .Some(let s) => return 0
                    .None => return entries[1].inner.look()
            func layers(value: uniq/(uniq/Point during a) during b)
                var entries: Dictionary<i32, uniq/(uniq/Point during a) during b> = [1: value@move]
                entries[1].inner.mutate()
            func tuples(value: uniq/(Inner, i32) during a)
                var entries: Dictionary<i32, uniq/(Inner, i32) during a> = [1: value@move]
                entries[1].0.mutate()
            func local(p: uniq/Point) => p.inner.mutate()
            func compared(value: uniq/Point during a) -> bool
                var entries: Dictionary<i32, uniq/Point during a> = [1: value@move]
                return entries[1].name == "a" and entries[1].inner.name == "b"
            func pairs(value: uniq/(string, i32) during a) -> bool
                var entries: Dictionary<i32, uniq/(string, i32) during a> = [1: value@move]
                return entries[1].0 == "a"
            func shared(value: ref/Point during a) -> bool
                var entries: Dictionary<i32, ref/Point during a> = [1: value]
                return entries[1].name == "a"

            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        string[] parts = ["entries[1].inner", "entries[1].opt", "entries[1].0", "p.inner", "entries[1].name", "entries[1].inner.name"];
        var adjusted = Reachable(c.Kotonoha).Where(x => x.Span.Start >= Source.IndexOf("func parts", StringComparison.Ordinal) && parts.Contains(Text(x)))
            .Select(x => $"{Line(x.Span.Start)}: {Text(x)} {HirTables.AdjustOf(x).Acquire}/{HirTables.AdjustOf(x).Adaptation}");
        string[] expected =
        [
            "entries[1].inner.mutate(): entries[1].inner None/ExclusiveBorrow", "match entries[1].opt: entries[1].opt None/SharedBorrow",
            ".None => return entries[1].inner.look(): entries[1].inner None/SharedBorrow", "entries[1].inner.mutate(): entries[1].inner None/ExclusiveBorrow",
            "entries[1].0.mutate(): entries[1].0 None/ExclusiveBorrow", "func local(p: uniq/Point) => p.inner.mutate(): p.inner None/ExclusiveBorrow",
            "return entries[1].name == \"a\" and entries[1].inner.name == \"b\": entries[1].name None/SharedBorrow",
            "return entries[1].name == \"a\" and entries[1].inner.name == \"b\": entries[1].inner.name None/SharedBorrow",
            "return entries[1].name == \"a\" and entries[1].inner.name == \"b\": entries[1].inner None/SharedBorrow",
            "return entries[1].0 == \"a\": entries[1].0 None/SharedBorrow", "return entries[1].name == \"a\": entries[1].name None/SharedBorrow",
        ];
        Assert.Equal(expected, adjusted);
        var element = Reachable(c.Kotonoha).Single(x => Text(x) == "entries[1]" && Line(x.Span.Start) == "entries[1].bump()");
        Assert.Equal(new HirAdjust(HirAcquire.Reborrow, HirAdaptation.None), HirTables.AdjustOf(element)); // SPEC 3.4.3: the stored uniq/Point.

        static string Text(Koto node) => Source.Substring(node.Span.Start, node.Span.Length);

        static string Line(int offset) => Source[(Source.LastIndexOf('\n', offset) + 1)..Source.IndexOf('\n', offset)].Trim();
    }

    // SPEC 4.7, 4.9.5, 6.3.6, 11.3: an accessor's executing function takes its receiver and its input (a static setter only its input),
    // an implicit constructor of a derived structure initializes its Fields and calls the base constructor its omitted base clause selects,
    // and each sequence metadata member is published with its form.
    [Fact]
    public void AccessorsConstructorsAndBuiltinFormsArePublished()
    {
        const string Source = "open struct Counter\n    protected var count: i32 = 0\n    protected init() => ()\nstruct Tally : Counter\n    var extra: i32 = 1\n" +
            "group Api\n    public computed item: i32\n        get() -> i32 => 1\n        set(value: i32) -> ()\n            _ = value\n" + CopyPropertyEmissionTest.Meter +
            "let tally = Tally.init()\nApi.item = 5\nvar meter = Meter.init()\nmeter.level = Api.item\nvar values: Array<i32> = [1, 2, 3]\nlet empty = values.isEmpty\n" +
            "let capacity = values.capacity\nlet span = values[0..2]\nvar total: isize = 0\nfor i in values.indices\n    total += i\nConsole.writeLine(\"n=\\(capacity)\")\n" +
            "let some: Option<i32> = .Some(5)\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var nodes = Reachable(c.Kotonoha);
        var accessors = nodes.OfType<PropertyAccessorKoto>().Where(static x => HirTables.Op(x) == HirOp.Body).Select(static x => CallPlan.Accessor(x))
            .Select(static x => x.Name + ":" + string.Join(',', ((FunctionKoto)x.Declaration).Parameters.Select((_, i) => CallPlan.Parameter(x, i)!.Name)));
        Assert.Equal(["item.Get:", "item.Set:value", "level.Get:self", "level.Set:self,value", "doubled.Get:self", "doubled.Set:self,value"], accessors);
        var tally = nodes.OfType<FunctionKoto>().Single(static x => x.IsImplicitConstructor);
        Assert.Equal(("Counter", 1), (CallPlan.Of(tally.BaseInitializer!)!.Target.Scope.Owner.SymbolOf()!.Name, AdtDef.Count(AdtDef.ReceiverType(tally)!)));
        Assert.Equal(HirOp.Literal, HirTables.Op(AdtDef.Field(AdtDef.ReceiverType(tally)!, 0).InitializerKoto!));
        BuiltinForm[] forms = [BuiltinForm.IsEmpty, BuiltinForm.Capacity, BuiltinForm.Indices];
        Assert.Equal(forms, nodes.Select(static x => IntrinsicDisposition.FormOf(x)).Where(static x => x != BuiltinForm.None));
        Assert.Equal("Some", BoundEnumConstruction.Of(nodes.OfType<InvocationKoto>().Last())!.Case.Symbol.Name);
    }

    // SPEC 23.1.2 (DECISIONS R5-M63): a closure whose definition Binding completed keeps its parts as a Closure wherever its creation
    // stands: inside a parent Binding rejects for its own reason (a let whose Type annotation fails, the parent replaced), or the
    // kept operand of a failed Index, whose creation is evaluated there (a Closure, in place); a closure whose capture list failed was
    // never bound, so its body stays replaced. An inferred result over the call's own storage keeps the Closure's
    // Type and fails only the returned value (DECISIONS R4-M152): the `return` statement, or an expression body, which its own Consume
    // takes.
    [Fact]
    public void AClosureDefinitionIsPublishedWhereverItsCreationStands()
    {
        const string Source = "func take(a: string, b: string) -> i32 => 1\nfunc f(m: i32)\n" +
            "    _ = func (n: i32)\n        let t = \"x\"\n        _ = take(t@move, t@move)\n        return n@ref\n" +
            "    let g = func (k: i32)\n        return k@ref\n    let h: Missing = func (k: i32)\n        return k\n" +
            "    _ = func (s: string) => s@ref\n    let w = \"w\"\n    _ = func [w] () => 1\n" +
            "    _ = m[func (k: i32) -> i32 => k]\n";
        var c = MinimalEmissionTest.Analyze(Source);
        HirTables.Publish(c);
        var closures = Reachable(c.Kotonoha).OfType<FunctionKoto>().Where(static x => x.IsAnonymous).ToArray();
        var rows = closures.Select(static x => (Koto?)x.Body ?? x.ExpressionBody!)
            .Select(static x => $"{HirTables.Op(x.Parent!)} {HirTables.Op(x)} {HirTables.AdjustOf(x).Acquire}");
        Assert.Equal(["Closure Block Unset", "Closure Block Unset", "Syntax Block Unset", "Closure Error Temporary", "Error Syntax Unset", "Closure Name Copy"], rows);
        Assert.Equal(new HirAdjust(HirAcquire.None, HirAdaptation.None), HirTables.AdjustOf(closures[^1]));
        var statements = Reachable(c.Kotonoha).Where(x => x.Parent is CodeBlockKoto && IsWithin(x, closures[0])).Select(static x => HirTables.Op(x));
        Assert.Equal([HirOp.Let, HirOp.Discard, HirOp.Error], statements);
    }

    // SPEC 14.2.2, 3.5 (DECISIONS R4-M145): a Place in Discard Context is acquired by bare acquisition, as at a value position, and
    // discarded. Its plan row's Adjust is that acquisition (a pair layer by case, a Copy, a stored exclusive reference's Reborrow, the
    // Move a bare Non-Copy Place requires, TransferRequired_Kd at it), while its Adjust column keeps its reading: unset, the
    // container discarding its item, or a group's in-place take of its operand. A Place a consumer takes (an initializer, `_ = k`'s
    // operand) has the take in its column and no such row. Each row: the node, its column, its plan row's Adjust.
    [Fact]
    public void ADiscardedPlaceIsTakenByItsBareAcquisitionInItsPlanRow()
    {
        const string Source = "func f<s/T>(value: s/T, count: i32, r: uniq/i32, items: [2 of i32], title: string, c: bool)\n    s is owner or uniq\n" +
            "    T is Copy\n    value\n    count\n    r\n    (r)\n    items[1]\n    title\n    if c\n        count\n    let k = count\n    _ = k\n" +
            "public func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        HirTables.Publish(c);
        var f = Reachable(c.Kotonoha).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var rows = Reachable(c.Kotonoha)
            .Where(x => IsWithin(x, f) && x.Parent is CodeBlockKoto or ParenthesizedKoto or VariableKoto or DiscardKoto &&
                HirTables.PlaceOf(x).Kind is not (HirPlaceKind.Unset or HirPlaceKind.Value))
            .Select(static x => $"{Source.Substring(x.Span.Start, x.Span.Length)} {Adjust(HirTables.AdjustOf(x))} {Adjust(HirTables.PlanOf(x).Adjust)}");
        Assert.Equal(
            [
                "value Unset/None ByCase/None", "count Unset/None Copy/None", "r Unset/None Reborrow/None", "(r) Unset/None Reborrow/None",
                "r None/None Reborrow/None", "items[1] Unset/None Copy/None", "title Unset/None Move/None", "count Unset/None Copy/None",
                "count Copy/None Unset/None", "k Copy/None Unset/None",
            ],
            rows);

        static string Adjust(HirAdjust adjust) => $"{adjust.Acquire}/{adjust.Adaptation}";
    }

    // SPEC 3.5.1, 8.14, 8.15.2 (PLAN G262, DECISIONS BU-45): a bare acquisition Reborrows a stored exclusive reference, or a pair layer in
    // its exclusive case, only where the path grants exclusive access in that case; through a shared layer (a `ref` follow, a Slice
    // element, a `let`-owned Array element, a Field through `ref`, another binder's pair layer) it has no plan, the Move the old path's
    // TransferRequired_Kd rejects. An exclusive path, a `var` Array element and the acquiring binder's own layers keep the Reborrow or the
    // acquisition by case. Each row: the Place, its Adjust (the plan row's for the discarded one).
    [Fact]
    public void ABareAcquisitionReborrowsOnlyThroughAnExclusivePath()
    {
        const string Source = "struct Holder {source}\n    public var link: uniq/i32 during source\nstruct Box<s/T>\n    public var v: s/T\n" +
            "func f(r: ref/(uniq/i32 during a) during b, u: uniq/(uniq/i32 during c) during d, s: Slice<uniq/i32 during e> during g, h: ref/(Holder during k) during m, n: uniq/i32, o: uniq/i32)\n" +
            "    let shared = r@follow\n    _ = r@follow\n    r@follow\n    let element = s[0]\n    let field = h.link\n    let exclusive = u@follow\n" +
            "    let values: Array<uniq/i32> = [n@move]\n    let denied = values[0]\n    var owned: Array<uniq/i32> = [o@move]\n    let granted = owned[0]\n" +
            "func p<s/T, t/U>(x: ref/(s/T during a) during b, w: t/(s/T during c) during d, z: s/(s/T during e) during g, bx: s/Box<s/T>, q: uniq/(s/T during h) during k)\n" +
            "    s is ref or uniq\n    t is ref or uniq\n    let pair = x@follow\n    let other = w@follow\n    let own = z@follow\n    let part = bx.v\n    let through = q@follow\n" +
            "public func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        var rows = Reachable(c.Kotonoha)
            .Where(static x => (x.Parent is VariableKoto { InitializerKoto: { } initializer } && ReferenceEquals(initializer, x) && x is not ArrayLiteralKoto) ||
                x.Parent is DiscardKoto || (x.Parent is CodeBlockKoto && HirTables.PlaceOf(x).Kind is not (HirPlaceKind.Unset or HirPlaceKind.Value)))
            .Select(static x => $"{Source.Substring(x.Span.Start, x.Span.Length)} {(x.Parent is CodeBlockKoto ? HirTables.PlanOf(x).Adjust : HirTables.AdjustOf(x)).Acquire}");
        Assert.Equal(
            [
                "r@follow Move", "r@follow Move", "r@follow Move", "s[0] Move", "h.link Move", "u@follow Reborrow", "values[0] Move", "owned[0] Reborrow",
                "x@follow Move", "w@follow Move", "z@follow ByCase", "bx.v ByCase", "q@follow ByCase",
            ],
            rows);
    }

    // SPEC 14.2.2, 23.1.2 (DECISIONS R4-M145): in a program Binding accepts, every Place the MIR builder takes as its value by its plan
    // row (a Place no consumer's Adjust takes, or a group's operand in Discard Context) has its bare acquisition there, so the builder's
    // derived reading of an unset row arises only beside a Binding error (MirBuildTest.AnUnsetDiscardRowIsDerivedFromABindingError).
    // The forms below each discard a Place; a group's operand is a parenthesized one (a label wraps only a control-flow construct, never
    // a Place: Parser's label rule).
    [Theory]
    [InlineData("func f(count: i32, r: uniq/i32, items: [2 of i32])\n    count\n    r\n    (r)\n    ((count))\n    items[1]\n")]
    [InlineData("func f<s/T>(value: s/T, c: bool)\n    s is owner or uniq\n    T is Copy\n    value\n    if c\n        value\n    match c\n        true => value\n        false => ()\n")]
    [InlineData("struct P\n    public var x: i32 = 0\n    public init() => ()\nfunc f(p: ref/P, q: uniq/(uniq/i32 during a) during b, d: ref/Dictionary<i32, i32>)\n    p.x\n    q@follow\n    d[1]\n")]
    [InlineData("func f(value: uniq/i32)\n    var h = func [value] () -> ()\n        value\n        (value)\n    h()\n")]
    public void EveryDiscardedPlaceHasItsAcquisition(string source)
    {
        var c = MinimalEmissionTest.Analyze(source + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        HirTables.Publish(c);
        var built = BuiltByTheirRow(c.Kotonoha).ToArray();
        Assert.NotEmpty(built);
        Assert.All(built, static x => Assert.NotEqual(HirAcquire.Unset, HirTables.PlanOf(x).Adjust.Acquire));
    }

    // The same over the accepted SPEC examples and the corpus (R5's sweep): no Place the builder reads by its row has it unset.
    [Theory]
    [MemberData(nameof(AcceptedPrograms))]
    public void AnAcceptedProgramLeavesNoDiscardRowUnset(string name)
    {
        Kotonoha module;
        if (name.StartsWith("spec/", StringComparison.Ordinal))
        {
            var example = DiagnosticCorpus.SpecCases.Single(x => x.Name == name[5..name.LastIndexOf('/')]);
            var variant = example.Variants[int.Parse(name[(name.LastIndexOf('/') + 1)..], CultureInfo.InvariantCulture)];
            var c = VerifiedSpecExampleTest.Bind(DiagnosticCorpus.Source(example, variant));
            Assert.True(c.Binding.Result.IsComplete, name);
            module = c.Kotonoha;
        }
        else
        {
            module = SemanticSnapshotTest.Bind(name).Module;
        }

        HirTables.Publish(module.Compilation);
        var unset = BuiltByTheirRow(module).Where(static x => HirTables.PlanOf(x).Adjust.Acquire == HirAcquire.Unset);
        Assert.Empty(unset.Select(static x => $"{x.Span.Start} {x}"));
    }

    // The Places the MIR builder acquires by their plan row (MirBuilder.Into's Place case): a Place no consumer's Adjust takes (a
    // discarded item, or an item no consumer took), or a group's operand in Discard Context, as the publisher writes them.
    private static IEnumerable<Koto> BuiltByTheirRow(Kotonoha module) => Reachable(module).Where(static x =>
        HirTables.Op(x) is HirOp.Name or HirOp.Field or HirOp.Index or HirOp.Deref or HirOp.Call &&
        HirTables.PlaceOf(x).Kind is not (HirPlaceKind.Unset or HirPlaceKind.Value) && (HirTables.AdjustOf(x).Acquire == HirAcquire.Unset ||
        (x.Parent is { } parent && HirTables.Op(parent) == HirOp.Group && !KotoHelper.IsValueContext(x))));
}
