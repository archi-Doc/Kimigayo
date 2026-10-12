// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A4, effect summaries on MIR (SPEC 8.4.10.2, 8.4.10.4, 8.4.10.7, 15.6.4; docs/dev/DECISIONS.md R4-M22): each rule with a
/// valid program and an invalid one, through the problems <c>Compilation.BuildMir</c> finds (R4 shadow: unpublished), each as its code,
/// the first line of its subject and its views.</summary>
public sealed class MirEffectsTest(ITestOutputHelper output)
{
    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n";

    private const string Witness = "struct S\n    Self is Sink\n    var loud: Loud = Loud.init()\n" +
        "    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n";

    private const string Loud = "struct Loud\n    public var n: i32 = 0\n    drop => Console.writeLine(\"drop\")\n";

    private const string Failed = "IncompatibleContractImplementation_Kd Self is Sink 1";

    private const string Apply = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\n";

    // SPEC 8.4.10.2: an access to a mutable static, a raw pointer read from an immutable static, an integer made a pointer and a foreign
    // call obtain authority from the environment; reading an immutable static (its initializer enters the summary), writing through a raw
    // input and a foreign call where nothing owes confined do not.
    [Theory]
    [InlineData("group G\n    public var count: i32 = 0\n", "        _ = G.count\n", Failed)]
    [InlineData("group G\n    public let count: i32 = 2 + 3\n", "        _ = G.count\n", "")]
    [InlineData("group G\n    public let count: i32 = G.noisy()\n    public func noisy() -> i32\n        Console.writeLine(\"x\")\n        return 2\n", "        _ = G.count\n", Failed)]
    [InlineData("group G\n    public let pointer: raw/i32 = null\n", "        _ = G.pointer\n", Failed)]
    [InlineData("group G\n    public let value: i32 = 4\n", "        _ = G.value\n", "")]
    [InlineData("", "        unsafe\n            let register = 0x40000000@raw/i32\n            *register = value\n", Failed)]
    [InlineData("", "        let output: raw/i32 = null\n        unsafe => *output = value\n", "")]
    [InlineData(
        "group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public unsafe func query(value: raw/i64) -> i32\n",
        "        let output: raw/i64 = null\n        unsafe => _ = Native.query(output)\n",
        Failed)]
    public void ConfinedExcludesEnvironmentAuthority(string declarations, string body, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + declarations + Witness + body + "        return .Ok(())\n"));

    // SPEC 8.4.10.2, 15.6.4: the summary is transitive through callees, also through mutual recursion, whose fixed point terminates.
    [Theory]
    [InlineData("    public func c() => Console.writeLine(\"c\")\n", Failed)]
    [InlineData("    public func c() => ()\n", "")]
    [InlineData("    public func c() => H.a()\n", "")]
    [InlineData("    public func c()\n        H.a()\n        Console.writeLine(\"c\")\n", Failed)]
    public void SummariesAreTransitive(string last, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "group H\n    public func a() => H.b()\n    public func b() => H.c()\n" + last + Witness + "        H.a()\n        return .Ok(())\n"));

    // SPEC 8.10, 8.4.10.4 (plan Appendix B X2): a requirement call in a generic body has the bounds of that body's own premises, never those
    // of an instance: the helper calls take under `S is Source`, which declares no confined bound, though Feed also proves Confined.
    [Theory]
    [InlineData("Source", Failed)]
    [InlineData("Confined", "")]
    public void RequirementCallsUseTheCallersPremises(string premise, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n" +
                "contract Confined: Source\n    effect Source.take confined\nstruct Feed\n    Self is Confined\n    associate Source.Item is i32\n" +
                "    public func take(self: uniq/Self) -> Option<i32> => .None\n" +
                $"group H\n    public func drain<T>(source: uniq/T)\n        T is {premise}\n        _ = source.take()\n" +
                Witness.Replace("Loud = Loud.init()\n", "Loud = Loud.init()\n    var feed: Feed = Feed.init()\n", StringComparison.Ordinal) +
                "        H.drain(self.feed@uniq)\n        return .Ok(())\n"));

    // SPEC 8.4.7: every implementation of a closed Contract (Position, PositionRange) reads only its boundaries and the length and has no
    // other effect, so the library's position operations need no bound of their own; a generic helper over another Contract keeps X2.
    [Theory]
    [InlineData("        var view = values.sliceUniq()\n        view[0] = 7\n", "")]
    [InlineData("        values.insert(0, 5)\n        _ = values.remove(0)\n", "")]
    [InlineData("        _ = values.tryGet(1)\n        _ = values.trySlice(0..1)\n", "")]
    [InlineData("        _ = H.at(3, values.length)\n", "")]
    [InlineData("        _ = H.locate(Spot.init(), values.length)\n", "IncompatibleContractImplementation_Kd Self is Filler 1")]
    [InlineData("        _ = H.atItem(3, values.length)\n", "")]
    [InlineData("        _ = H.locateItem(Spot.init(), values.length)\n", "IncompatibleContractImplementation_Kd Self is Filler 1")]
    public void ClosedContractRequirementsHaveNoEffect(string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("contract Locate\n    func locate(self: ref/Self, length: isize) -> Option<isize>\nstruct Spot\n    Self is Locate\n" +
                "    public func locate(self: ref/Self, length: isize) -> Option<isize> => .None\ngroup H\n" +
                "    public func at<P>(p: P, length: isize) -> Option<isize>\n        P is Position\n        return p.tryResolve(length)\n" +
                "    public func locate<P>(p: P, length: isize) -> Option<isize>\n        P is Locate\n        return p.locate(length)\n" +
                "    public func atItem<P>(p: P, length: isize) -> Option<isize>\n        P is Position\n        let resolve = P.tryResolve\n" +
                "        return resolve(p, length)\n    public func locateItem<P>(p: P, length: isize) -> Option<isize>\n        P is Locate\n" +
                "        let locate = P.locate\n        return locate(p@ref, length)\n" +
                "contract Filler\n    func fill(self: ref/Self, values: uniq/Array<i32>)\n        effect confined\nstruct F\n    Self is Filler\n" +
                "    public func fill(self: ref/Self, values: uniq/Array<i32>)\n" + body));

    // SPEC 8.8, 8.8.3: a call runs the specialization its closed key selects, or the original when none does; the other body never runs.
    // A function item runs its selection too (its valid form waits for fix unit U6c: an item without length slots binds no length slot,
    // so no key matches it and both bodies are admitted).
    [Theory]
    [InlineData("Console.writeLine(\"g\")", "()", "        H.trace(1)\n", "")]
    [InlineData("Console.writeLine(\"g\")", "()", "        H.trace(true)\n", Failed)]
    [InlineData("()", "Console.writeLine(\"s\")", "        H.trace(true)\n", "")]
    [InlineData("()", "Console.writeLine(\"s\")", "        H.trace(1)\n", Failed)]
    [InlineData("()", "Console.writeLine(\"s\")", "        let f = H.trace<i32>\n        f(1)\n", Failed)]
    public void CallsRunTheBodyTheirKeySelects(string original, string specialization, string call, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + $"group H\n    public func trace<T>(v: T) => {original}\n    specialize func trace<i32>(v: i32) => {specialization}\n" +
                Witness + call + "        return .Ok(())\n"));

    // SPEC 8.4: a conformance checks every implementation of its witness, specializations included; a specialization is closed, so its
    // effect is reported in every view.
    [Theory]
    [InlineData("Console.writeLine(\"s\")", "IncompatibleContractImplementation_Kd Self is Show 18446744073709551615")]
    [InlineData("()", "")]
    public void AWitnessesSpecializationsOweItsBound(string specialization, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("contract Show\n    func show<U>(self: ref/Self, u: ref/U)\n        effect confined\nstruct S\n    Self is Show\n" +
                $"    public func show<U>(self: ref/Self, u: ref/U) => ()\n    specialize func show<i32>(self: ref/Self, u: ref/i32) => {specialization}\n"));

    // SPEC 8.8: an omitted argument evaluates the original's default, also where a specialization is called: inline, or as a call of the
    // template where the defaults reach each other (a's default calls b, whose default calls a; b prints).
    [Theory]
    [InlineData("        _ = H.a(1)\n", Failed)]
    [InlineData("        _ = H.a(1, n: 2)\n", "")]
    public void ADefaultIsTheOriginalsUnderASpecialization(string call, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "group H\n    public func a<T>(v: T, n: i32 = H.b(1)) -> i32 => n\n    specialize func a<i32>(v: i32, n: i32) -> i32 => n\n" +
                "    public func b(m: i32, k: i32 = H.a(m)) -> i32\n        Console.writeLine(\"b\")\n        return k\n" + Witness + call + "        return .Ok(())\n"));

    // SPEC 8.4.10.4: a requirement call whose Self is closed runs that Type's implementation, entered like any call.
    [Theory]
    [InlineData("", "")]
    [InlineData("        Console.writeLine(\"equals\")\n", Failed)]
    public void AClosedSelfRequirementCallRunsItsWitness(string effect, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "struct Key\n    Self is Equatable\n    public var n: i32 = 0\n    public func equals(self: ref/Self, other: ref/Self) -> bool\n" +
                effect + "        return self.n == other.n\n" + Witness + "        let key = Key.init()\n        _ = key == key\n        return .Ok(())\n"));

    // SPEC 16.3, 8.4.10.4 rule 1: destroying a value runs its destructor; a generic helper's destruction of its parameter composes through
    // the caller's argument (Loud's drop prints, i32 has none); an abstract value destroyed at the top is unknown unless proven Copy.
    [Theory]
    [InlineData("        H.discard(Loud.init())\n", Failed)]
    [InlineData("        H.discard(value)\n", "")]
    [InlineData("        H.discard(self.loud@ref)\n", "")]
    public void DestructionComposesThroughTheArgument(string body, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + "group H\n    public func discard<T>(item: T) => ()\n" + Witness + body + "        return .Ok(())\n"));

    [Theory]
    [InlineData("", "IncompatibleContractImplementation_Kd Self is Eater 1")]
    [InlineData("        T is Copy\n", "")]
    public void AnAbstractDestructionNeedsCopyAtTheTop(string premise, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems($"contract Eater\n    func eat<T>(self: ref/Self, item: T)\n{premise}        effect confined\nstruct E\n    Self is Eater\n" +
                $"    public func eat<T>(self: ref/Self, item: T)\n{premise}        ()\n"));

    // R4-M6a: a Drop destroys what A2's verdicts leave in the view: a moved value nothing (Elide), a partly moved one its remaining parts
    // (Open: the moved Field's row is Elide, the other Field has none and follows the own bit), a whole one its Type (Keep).
    [Theory]
    [InlineData("        let l = Loud.init()\n        return l@move\n", "")]
    [InlineData("        let l = Loud.init()\n        return Loud.init()\n", "IncompatibleContractImplementation_Kd Self is Make 1")]
    [InlineData("        let p = Pair.init()\n        return p.a@move\n", "")]
    [InlineData("        let p = Twin.init()\n        return p.a@move\n", "IncompatibleContractImplementation_Kd Self is Make 1")]
    public void DropsDestroyWhatA2Leaves(string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Loud + "struct Pair\n    public var a: Loud = Loud.init()\n    public var b: i32 = 0\nstruct Twin\n    public var a: Loud = Loud.init()\n" +
                "    public var b: Loud = Loud.init()\ncontract Make\n    func make(self: ref/Self) -> Loud\n        effect confined\nstruct M\n    Self is Make\n" +
                "    public func make(self: ref/Self) -> Loud\n" + body));

    // R4-M6a: a Drop only the checks reach runs no destructor (drop elaboration applies to runtime paths only, SPEC 14.10.3), while the
    // check-only statements' own effects count.
    [Theory]
    [InlineData("        let l = Loud.init()\n", "")]
    [InlineData("        Console.writeLine(\"x\")\n", Failed)]
    public void ACheckOnlyDropRunsNothing(string checkOnly, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + Witness + "        return .Ok(())\n" + checkOnly));

    // R4-M6b: a struct that declares a drop is destroyed by its destructor body alone, whose field cleanup destroys its Fields; the
    // library's fixed-array remainder, which holds its elements in an InlineStorage, destroys the remaining elements in its drop body.
    [Theory]
    [InlineData("Loud = Loud.init()", "        let o = Outer.init()\n", Failed)]
    [InlineData("i32 = 0", "        let o = Outer.init()\n", "")]
    [InlineData("i32 = 0", "        let values: [2 of Loud] = [Loud.init(), Loud.init()]\n        for item in values@move => ()\n", Failed)]
    [InlineData("i32 = 0", "        let values: [2 of i32] = [1, 2]\n        for item in values@move => ()\n", "")]
    public void AStructThatDeclaresADropIsDestroyedByItsBody(string inner, string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + $"struct Outer\n    public var inner: {inner}\n    drop => ()\n" + Witness + body + "        return .Ok(())\n"));

    // SPEC 8.10: a callee's Semantics case views are those its call selects: a pair binder bound to a reference is the reference case,
    // whose value is not destroyed.
    [Theory]
    [InlineData("        H.keep(Loud.init())\n", Failed)]
    [InlineData("        H.keep(self.loud@ref)\n", "")]
    public void CallsEnterTheViewsTheirSubstitutionSelects(string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "group H\n    public func keep<s/T>(item: s/T)\n        s is owner or ref\n        ()\n" + Witness + body + "        return .Ok(())\n"));

    // SPEC 8.4.10.7: a call through a Callable input composes the actual Closure's or function item's summary at the caller; a bounded
    // input's binding owes its bound after selection, decided by the premises where they can (a missing premise, an erased Function Type)
    // at the argument; an owner call also destroys the remaining environment.
    [Theory]
    [InlineData("42", "")]
    [InlineData("Helpers.noisy()", "IncompatibleContractImplementation_Kd Self is Work 1")]
    public void SummariesComposeCallbacksThroughActualArguments(string expression, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("contract Work\n    func run(self: ref/Self) -> i32\n        effect confined\n" +
                "group Helpers\n    public func invoke<F>(f: ref/F) -> i32\n        F is Callable<() -> i32>\n        return f()\n" +
                "    public func noisy() -> i32\n        Console.writeLine(\"noisy\")\n        return 0\n" +
                $"struct Worker\n    Self is Work\n    public func run(self: ref/Self) -> i32 => Helpers.invoke(func [] () -> i32 => {expression})\n"));

    [Theory]
    [InlineData("func entry() -> i32 => apply(func [] () -> i32 => 42)\n", "")]
    [InlineData("func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\nfunc entry() -> i32 => apply(noisy)\n", "UnsatisfiedEffectBound_Kd noisy 18446744073709551615")]
    [InlineData("func forward<G>(g: ref/G) -> i32\n    G is Callable<() -> i32>\n    return apply(g)\n", "UnsatisfiedEffectBound_Kd g 18446744073709551615")]
    [InlineData("func entry() -> i32\n    let f: () -> i32 = func [] () -> i32 => 5\n    return apply(f@ref)\n", "UnsatisfiedEffectBound_Kd f@ref 18446744073709551615")]
    public void CallableBindingsOweTheirBound(string declarations, string expected)
        => Assert.Equal(Expected(expected), Problems(Apply + declarations));

    // SPEC 8.4.10.7: a binding's remaining atoms are resolved by the premises of the use: consume's destruction of its T is the user's T,
    // which only the user's premise proves Copy.
    [Theory]
    [InlineData("    T is Copy\n", "")]
    [InlineData("", "UnsatisfiedEffectBound_Kd consume<T> 18446744073709551615")]
    public void ACallableBindingIsJudgedUnderTheUsesPremises(string premise, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("func take<U, F>(f: ref/F, value: U)\n    F is Callable<(U) -> ()>\n        effect confined\n    f(value@move)\n" +
                $"func consume<T>(v: T) => ()\nfunc user<T>(v: T)\n{premise}    take(consume<T>, v@move)\n"));

    // SPEC 8.4.10.7: a call through an abstract input takes the bounds of its one Callable signature; with several, only the call's
    // arguments chose among them, which MIR does not keep: a located limit (DECISIONS R4-M23).
    [Theory]
    [InlineData("", "")]
    [InlineData("        F is Callable<(i32) -> i32>\n            effect confined\n", "Unsupported_Kd Self is Work 1")]
    public void SeveralCallableSignaturesAreALimit(string second, string expected)
    {
        var premises = "        F is Callable<() -> i32>\n            effect confined\n" + second;
        Assert.Equal(
            Expected(expected),
            Problems("contract Work\n    func run<F>(self: ref/Self, f: ref/F) -> i32\n" + premises + "        effect confined\nstruct W\n    Self is Work\n" +
                "    public func run<F>(self: ref/Self, f: ref/F) -> i32\n" + premises + "        return f()\n"));
    }

    [Theory]
    [InlineData("ref", "")]
    [InlineData("owner", "UnsatisfiedEffectBound_Kd f@move 18446744073709551615")]
    public void AnOwnerCallDestroysTheRemainingEnvironment(string receiver, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Loud + $"func apply<F>(f: {(receiver == "ref" ? "ref/F" : "F")}) -> i32\n    F is Callable<{receiver}, () -> i32>\n        effect confined\n" +
                $"    return {(receiver == "ref" ? "f()" : "f@move()")}\nfunc entry() -> i32\n    let loud = Loud.init()\n" +
                $"    let f = func [loud@move] () -> i32 => loud.n\n    return apply(f@{(receiver == "ref" ? "ref" : "move")})\n"));

    // virtual-dispatch 6: every original and override owes its slot's bound; destruction counts (Binding's own check does not see it).
    [Theory]
    [InlineData("Loud.init()", "UnsatisfiedEffectBound_Kd override func read(self: objref/Self) -> i32 1")]
    [InlineData("7", "")]
    public void AnOverrideOwesItsSlotsBound(string value, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Loud + "group H\n    public func discard<T>(item: T) => ()\nopen struct Base\n    public virtual func read(self: objref/Self) -> i32\n" +
                $"        effect confined\n        return 1\nstruct Derived : Base\n    override func read(self: objref/Self) -> i32\n        H.discard({value})\n        return 1\n"));

    // virtual-dispatch 6: an override is judged under its premises, the slot's inherited ones included (`T is Copy` makes dropping the item
    // nothing; Owned does not).
    [Theory]
    [InlineData("Copy", "")]
    [InlineData("Owned", "UnsatisfiedEffectBound_Kd public virtual func keep(self: objref/Self, item: T) 1|UnsatisfiedEffectBound_Kd override func keep(self: objref/Self, item: U) 1")]
    public void AnOverrideIsJudgedUnderItsInheritedPremises(string premise, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("open struct Base<T>\n    public init() => ()\n    public virtual func keep(self: objref/Self, item: T)\n" +
                $"        T is {premise}\n        effect confined\n        ()\nstruct Derived<U> : Base<U>\n    override func keep(self: objref/Self, item: U)\n        ()\n"));

    // virtual-dispatch 6, SPEC 8.4.10.7: a virtual witness and a virtual function item dispatch, so their slot's bound decides, never the
    // original's body.
    [Theory]
    [InlineData("", "Self is Getter\n", "IncompatibleContractImplementation_Kd Self is Getter 18446744073709551615|IncompatibleContractImplementation_Kd Self is Reader 1")]
    [InlineData("        effect confined\n", "Self is Getter\n", "")]
    [InlineData("", "", "IncompatibleContractImplementation_Kd Self is Reader 1")]
    [InlineData("        effect confined\n", "", "")]
    public void DispatchedCalleesUseTheirSlotsBound(string bound, string witness, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("contract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\ncontract Reader\n    func read(self: ref/Self, value: objref/Base) -> i32\n" +
                $"        effect confined\nopen struct Base\n    {witness}    public virtual func get(self: objref/Self) -> i32\n{bound}        return 1\n" +
                "struct Probe\n    Self is Reader\n    public func read(self: ref/Self, value: objref/Base) -> i32\n        let operation = Base.get\n" +
                "        return operation(value)\n"));

    // R4-M10: an effect only an Opaque site causes is a dependent of the error that made it; an independent environment effect is not.
    [Theory]
    [InlineData("        _ = missing\n", "")]
    [InlineData("        _ = missing\n        Console.writeLine(\"x\")\n", Failed)]
    public void AnOpaqueSiteCausesNoProblem(string body, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + Witness + body + "        return .Ok(())\n"));

    // A built-in comparison of a composite Type runs witnesses the backend instantiates (until R8): a limit, Unsupported_Kd.
    [Theory]
    [InlineData("        _ = (value, 1) == (value, 2)\n", "Unsupported_Kd Self is Sink 1")]
    [InlineData("        _ = value == 2\n", "")]
    public void ACompositeComparisonIsALimit(string body, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + Witness + body + "        return .Ok(())\n"));

    // V16 (plan "New V-checks"): A4 is linear: an N-deep call chain whose last member prints and an N-member ring of mutual calls (one
    // member prints), each with one problem, grow the evaluations and the cold allocation of a fresh A4 over the program (the library
    // included) linearly; a warm re-analysis of the same program allocates nothing. N is 256/512/1024 so that the program's own bodies,
    // not the library's, fill the pooled arrays, whose doubling makes the allocation of smaller programs step (16/32/64: +336, then +9,504).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("    public func step{0}() => H.{1}()\n")]
    [InlineData("    public func step{0}()\n        if G.flag => H.step{2}()\n        H.{1}()\n")]
    public void SummariesScaleLinearly(string member)
    {
        var (sizes, evaluations, allocated) = (new[] { 256, 512, 1024 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder(Sink + "group G\n    public let flag: bool = false\ngroup H\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                var next = n + 1 == sizes[i] ? "last" : $"step{n + 1}";
                text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, member, n, next, n == 0 ? sizes[i] - 1 : n - 1);
            }

            var source = text.Append("    public func last() => Console.writeLine(\"x\")\n")
                .Append(Witness.Replace("    var loud: Loud = Loud.init()\n", string.Empty, StringComparison.Ordinal)).Append("        H.step0()\n        return .Ok(())\n").ToString();
            var c = MirBuildTest.Compile(source);
            var program = c.BuildMir();
            Assert.Equal([Failed], Problems(c, program));
            var (cases, report) = (new MirCases(c.Types), new MirReport());
            var effects = new MirEffects(c.Types, cases, new MirReach());
            var before = GC.GetAllocatedBytesForCurrentThread();
            Analyze(effects, cases, program, report);
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            evaluations[i] = effects.Evaluations;
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                report.Clear();
                Analyze(effects, cases, program, report);
            }));
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: evaluations {string.Join(", ", evaluations)}, cold A4 allocation {string.Join(", ", allocated)}");
        Assert.True(evaluations[2] - evaluations[1] <= 2 * (evaluations[1] - evaluations[0]), $"evaluations {string.Join(", ", evaluations)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold A4 allocation {string.Join(", ", allocated)}");
    }

    // The driver's A4 rows over a built program and its A2 table: each body's views' local facts, then the run.
    private static void Analyze(MirEffects effects, MirCases cases, MirProgram program, MirReport report)
    {
        foreach (var body in program.Bodies)
        {
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                cases.Select(body, view);
                effects.Local(program, body, view);
            }
        }

        effects.Run(program, report);
    }

    private static string[] Expected(string expected) => expected.Length == 0 ? [] : expected.Split('|');

    private static string[] Problems(string source)
    {
        var c = MirBuildTest.Compile(source + "public func main() => ()\n");
        return Problems(c, c.BuildMir());
    }

    // The A4 problems of module 0: code, the first line of the subject, views.
    private static string[] Problems(Compilation c, MirProgram program)
    {
        Assert.Empty(program.Findings);
        return [.. program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0 && x.Code is DiagnosticCode.IncompatibleContractImplementation_Kd or
            DiagnosticCode.UnsatisfiedEffectBound_Kd or DiagnosticCode.Unsupported_Kd).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()} {x.Views}")];
    }
}
