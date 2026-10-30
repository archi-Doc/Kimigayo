// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A4, effect summaries on MIR (SPEC 8.11.2, 8.11.3, 8.11.4, 15.6.4; docs/dev/DECISIONS.md R4-M22): each rule with a
/// valid program and an invalid one, through the problems <c>Compilation.BuildMir</c> finds (R4 shadow: unpublished), each as its code,
/// the first line of its subject and its views.</summary>
public sealed partial class MirEffectsTest(ITestOutputHelper output)
{
    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n";

    private const string Witness = "struct S\n    Self is Sink\n    var loud: Loud = Loud.init()\n" +
        "    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n";

    private const string Loud = "struct Loud\n    public var n: i32 = 0\n    drop => Console.writeLine(\"drop\")\n";

    private const string Failed = "IncompatibleContractImplementation_Kd Self is Sink 1";

    private const string Apply = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\n";

    private const string Noisy = "struct Noisy\n    Self is Equatable\n    public var n: i32 = 0\n    public func equals(self: ref/Self, other: ref/Self) -> bool\n" +
        "        Console.writeLine(\"eq\")\n        return self.n == other.n\n";

    private const string Fixed = "contract Fixed: Equatable\n    effect Equatable.equals confined\nstruct Key\n    Self is Fixed\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n";

    // G308 E1: the DAG Tuple t4 of depth 4 over the pair t1, compared with itself.
    private const string Dag4 = "        let t2 = (t1, t1)\n        let t3 = (t2, t2)\n        let t4 = (t3, t3)\n        _ = t4 == t4\n";

    // G308 review (R4's N5): Dag4 under an ordering, whose 16 leaves share one compare.
    private const string Dag4Less = "        let t2 = (t1, t1)\n        let t3 = (t2, t2)\n        let t4 = (t3, t3)\n        _ = t4 < t4\n";

    // An ordered struct whose compare reads an environment (Ranked) and a pure one (Level).
    private const string Ordered = "struct Ranked\n    Self is Comparable\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n" +
        "    public func compare(self: ref/Self, other: ref/Self) -> i32\n        _ = State.value\n        return 0\n" +
        "struct Level\n    Self is Comparable\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n" +
        "    public func compare(self: ref/Self, other: ref/Self) -> i32 => 0\n";

    // G308 E2: a generic comparison of a DAG Tuple of depth 3 over its leaf T, under Fixed.
    private const string DeepSame = "    public func deep<T>(a: ref/T, b: ref/T) -> bool\n        T is Fixed\n        let x = ((a, a), (a, a))\n" +
        "        let y = ((b, b), (b, b))\n        return (x, x) == (y, y)\n";

    // SPEC 8.11.2: an access to a mutable static, a raw pointer read from an immutable static, an integer made a pointer and a foreign
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

    // SPEC 8.11.2, 15.6.4: the summary is transitive through callees, also through mutual recursion, whose fixed point terminates.
    [Theory]
    [InlineData("    public func c() => Console.writeLine(\"c\")\n", Failed)]
    [InlineData("    public func c() => ()\n", "")]
    [InlineData("    public func c() => H.a()\n", "")]
    [InlineData("    public func c()\n        H.a()\n        Console.writeLine(\"c\")\n", Failed)]
    public void SummariesAreTransitive(string last, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "group H\n    public func a() => H.b()\n    public func b() => H.c()\n" + last + Witness + "        H.a()\n        return .Ok(())\n"));

    // SPEC 8.15, 8.11.3 (plan Appendix B X2): a requirement call in a generic body has the bounds of that body's own premises, never those
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

    // SPEC 8.7: every implementation of a closed Contract (Position, PositionRange) reads only its boundaries and the length and has no
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

    // SPEC 8.13, 8.13.3: a call runs the specialization its closed key selects, or the original when none does; the other body never runs.
    // A function item runs its selection too.
    [Theory]
    [InlineData("Console.writeLine(\"g\")", "()", "        H.trace(1)\n", "")]
    [InlineData("Console.writeLine(\"g\")", "()", "        H.trace(true)\n", Failed)]
    [InlineData("()", "Console.writeLine(\"s\")", "        H.trace(true)\n", "")]
    [InlineData("()", "Console.writeLine(\"s\")", "        H.trace(1)\n", Failed)]
    [InlineData("Console.writeLine(\"g\")", "()", "        let f = H.trace<i32>\n        f(1)\n", "")]
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

    // SPEC 8.13: an omitted argument evaluates the original's default, also where a specialization is called: inline, or as a call of the
    // template where the defaults reach each other (a's default calls b, whose default calls a; b prints).
    [Theory]
    [InlineData("        _ = H.a(1)\n", Failed)]
    [InlineData("        _ = H.a(1, n: 2)\n", "")]
    public void ADefaultIsTheOriginalsUnderASpecialization(string call, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "group H\n    public func a<T>(v: T, n: i32 = H.b(1)) -> i32 => n\n    specialize func a<i32>(v: i32, n: i32) -> i32 => n\n" +
                "    public func b(m: i32, k: i32 = H.a(m)) -> i32\n        Console.writeLine(\"b\")\n        return k\n" + Witness + call + "        return .Ok(())\n"));

    // SPEC 8.11.3: a requirement call whose Self is closed runs that Type's implementation, entered like any call.
    [Theory]
    [InlineData("", "")]
    [InlineData("        Console.writeLine(\"equals\")\n", Failed)]
    public void AClosedSelfRequirementCallRunsItsWitness(string effect, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "struct Key\n    Self is Equatable\n    public var n: i32 = 0\n    public func equals(self: ref/Self, other: ref/Self) -> bool\n" +
                effect + "        return self.n == other.n\n" + Witness + "        let key = Key.init()\n        _ = key == key\n        return .Ok(())\n"));

    // SPEC 16.3, 8.11.3 rule 1: destroying a value runs its destructor; a generic helper's destruction of its parameter composes through
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

    // SPEC 8.15: a callee's Semantics case views are those its call selects: a pair binder bound to a reference is the reference case,
    // whose value is not destroyed.
    [Theory]
    [InlineData("        H.keep(Loud.init())\n", Failed)]
    [InlineData("        H.keep(self.loud@ref)\n", "")]
    public void CallsEnterTheViewsTheirSubstitutionSelects(string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "group H\n    public func keep<s/T>(item: s/T)\n        s is owner or ref\n        ()\n" + Witness + body + "        return .Ok(())\n"));

    // SPEC 8.11.4: a call through a Callable input composes the actual Closure's or function item's summary at the caller; a bounded
    // input's binding owes its bound after selection, decided by the premises where they can (a missing premise, an erased Function Type)
    // at the argument; an owner call also destroys the remaining environment. Declared before the helpers, the worker is evaluated before
    // invoke has its atom, so the Closure's body is a read only a later evaluation makes: a dependency recorded only at a node's first
    // evaluation would lose the failure (the 2b-7 review's mutant).
    [Theory]
    [InlineData("42", false, "")]
    [InlineData("Helpers.noisy()", false, "IncompatibleContractImplementation_Kd Self is Work 1")]
    [InlineData("Helpers.noisy()", true, "IncompatibleContractImplementation_Kd Self is Work 1")]
    public void SummariesComposeCallbacksThroughActualArguments(string expression, bool workerFirst, string expected)
    {
        const string Helpers = "group Helpers\n    public func invoke<F>(f: ref/F) -> i32\n        F is Callable<() -> i32>\n        return f()\n" +
            "    public func noisy() -> i32\n        Console.writeLine(\"noisy\")\n        return 0\n";
        var worker = $"struct Worker\n    Self is Work\n    public func run(self: ref/Self) -> i32 => Helpers.invoke(func [] () -> i32 => {expression})\n";
        Assert.Equal(
            Expected(expected),
            Problems("contract Work\n    func run(self: ref/Self) -> i32\n        effect confined\n" + (workerFirst ? worker + Helpers : Helpers + worker)));
    }

    [Theory]
    [InlineData("func entry() -> i32 => apply(func [] () -> i32 => 42)\n", "")]
    [InlineData("func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\nfunc entry() -> i32 => apply(noisy)\n", "UnsatisfiedEffectBound_Kd noisy 18446744073709551615")]
    [InlineData("func forward<G>(g: ref/G) -> i32\n    G is Callable<() -> i32>\n    return apply(g)\n", "UnsatisfiedEffectBound_Kd g 18446744073709551615")]
    [InlineData("func entry() -> i32\n    let f: () -> i32 = func [] () -> i32 => 5\n    return apply(f@ref)\n", "UnsatisfiedEffectBound_Kd f@ref 18446744073709551615")]
    public void CallableBindingsOweTheirBound(string declarations, string expected)
        => Assert.Equal(Expected(expected), Problems(Apply + declarations));

    // SPEC 8.11.4: a binding's remaining atoms are resolved by the premises of the use: consume's destruction of its T is the user's T,
    // which only the user's premise proves Copy.
    [Theory]
    [InlineData("    T is Copy\n", "")]
    [InlineData("", "UnsatisfiedEffectBound_Kd consume<T> 18446744073709551615")]
    public void ACallableBindingIsJudgedUnderTheUsesPremises(string premise, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("func take<U, F>(f: ref/F, value: U)\n    F is Callable<(U) -> ()>\n        effect confined\n    f(value@move)\n" +
                $"func consume<T>(v: T) => ()\nfunc user<T>(v: T)\n{premise}    take(consume<T>, v@move)\n"));

    // SPEC 8.11.4: a call through an abstract input takes the bounds of its one Callable signature; with several, only the call's
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

    // SPEC 23.1.1 (DECISIONS R4-M153, implementation review 1's N3): beside the limit of several signatures, the same witness destroys a
    // Tuple nested past the destruction walk's bound: the guard's one ranking decides, the unsupported part over the part past the bound,
    // so one record; with one signature the depth limit alone.
    [Theory]
    [InlineData("", "OwnershipDepthLimit_Kd Self is Work 1")]
    [InlineData("        F is Callable<(i32) -> i32>\n            effect confined\n", "Unsupported_Kd Self is Work 1")]
    public void AnUnsupportedPartOutranksAPartPastTheBound(string second, string expected)
    {
        var premises = "        F is Callable<() -> i32>\n            effect confined\n" + second;
        var deep = new string('(', 40) + "0" + string.Concat(Enumerable.Range(0, 40).Select(static i => $", {i})"));
        Assert.Equal(
            Expected(expected),
            Problems("contract Work\n    func run<F>(self: ref/Self, f: ref/F) -> i32\n" + premises + "        effect confined\nstruct W\n    Self is Work\n" +
                "    public func run<F>(self: ref/Self, f: ref/F) -> i32\n" + premises + $"        _ = {deep}\n        return f()\n"));
    }

    // SPEC 23.1.1 (DECISIONS R4-M153's revision 3): a destruction walk enters a Type at the shallowest depth it meets it, so a destroyed
    // Loud, 41 levels down a Tuple and 9 levels down beside it (one Type at both depths), gives its environment effect, which fails the
    // contract's `confined`, in either order of the components, never the depth limit for the part past the bound.
    [Theory]
    [InlineData("deep first")]
    [InlineData("shallow first")]
    public void APartsTypeEnteredWithinTheBoundIsNoDeepInEitherOrder(string order)
    {
        var (deep, shallow) = (Nested(40), Nested(8));
        var premises = "        F is Callable<() -> i32>\n            effect confined\n";
        Assert.Equal(
            Expected("IncompatibleContractImplementation_Kd Self is Work 1"),
            Problems(Loud + "contract Work\n    func run<F>(self: ref/Self, f: ref/F) -> i32\n" + premises + "        effect confined\nstruct W\n" +
                "    Self is Work\n    public func run<F>(self: ref/Self, f: ref/F) -> i32\n" + premises +
                $"        _ = ({(order == "deep first" ? $"{deep}, {shallow}" : $"{shallow}, {deep}")})\n        return f()\n"));

        static string Nested(int levels) => new string('(', levels) + "Loud.init()" + string.Concat(Enumerable.Range(0, levels).Select(static i => $", {i})"));
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

    // virtual-dispatch 3, 6.3 (R4-M28): an override whose declaring Type refutes its slot's condition is never dispatched, but its body is
    // checked against the slot's bound (fail-before: each, a limit without a published body).
    [Theory]
    [InlineData("=> 2\n", "")]
    [InlineData("\n        H.discard(Loud.init())\n        return 2\n", "UnsatisfiedEffectBound_Kd override func read(self: objref/Self) -> i32 1")]
    public void AnInapplicableOverrideIsChecked(string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Loud + "group H\n    public func discard<T>(item: T) => ()\nopen struct Base<T>\n    public init() => ()\n" +
                "    public virtual func read(self: objref/Self) -> i32\n        T is Copy\n        effect confined\n        return 1\n" +
                $"struct Derived : Base<string>\n    public init() => ()\n    override func read(self: objref/Self) -> i32 {body}"));

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

    // virtual-dispatch 6, SPEC 8.11.4: a virtual witness and a virtual function item dispatch, so their slot's bound decides, never the
    // original's body.
    [Theory]
    [InlineData(
        "",
        "    Self is Getter\n",
        "IncompatibleContractImplementation_Kd Self is Getter 18446744073709551615|IncompatibleContractImplementation_Kd Self is Reader 1")]
    [InlineData("        effect confined\n", "    Self is Getter\n", "")]
    [InlineData("", "", "IncompatibleContractImplementation_Kd Self is Reader 1")]
    [InlineData("        effect confined\n", "", "")]
    public void DispatchedCalleesUseTheirSlotsBound(string bound, string witness, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("contract Getter\n    func get(self: objref/Self) -> i32\n        effect confined\ncontract Reader\n    func read(self: ref/Self, value: objref/Base) -> i32\n" +
                $"        effect confined\nopen struct Base\n{witness}    public virtual func get(self: objref/Self) -> i32\n{bound}        return 1\n" +
                "struct Probe\n    Self is Reader\n    public func read(self: ref/Self, value: objref/Base) -> i32\n        let operation = Base.get\n" +
                "        return operation(value)\n"));

    // R4-M10: an effect only an Opaque site causes is a dependent of the error that made it; an independent environment effect is not.
    [Theory]
    [InlineData("        _ = missing\n", "")]
    [InlineData("        _ = missing\n        Console.writeLine(\"x\")\n", Failed)]
    public void AnOpaqueSiteCausesNoProblem(string body, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + Witness + body + "        return .Ok(())\n"));

    // SPEC 4.9.6, 12.4.3 (R4-M28): a published operation adds its row through the call's substitution, never its private body: K equality
    // for a Dictionary lookup, update or literal key check, which runs i32's built-in witness and Noisy's printing one (fail-before: the
    // first and third, through the private Storage helper's requirement call under its own premises).
    [Theory]
    [InlineData(
        "        var seen: Dictionary<i32, i32> = [:]\n        seen.reserve(1)\n        _ = seen.tryInsert(value, value)\n        _ = seen.insertOrReplace(value, 2)\n" +
        "        _ = seen.tryGet(value)\n        _ = seen.remove(value)\n        seen.shrinkToFit()\n        seen[value] = 3\n        _ = seen[value]\n",
        "")]
    [InlineData("        let seen: Dictionary<Noisy, i32> = [:]\n        _ = seen[Noisy.init()]\n", Failed)]
    [InlineData("        let seen: Dictionary<i32, i32> = [1: 2, 3: 4]\n", "")]
    [InlineData("        let seen: Dictionary<Noisy, i32> = [Noisy.init(): 2, Noisy.init(): 4]\n", Failed)]
    public void PublishedOperationsAddTheirRows(string body, string expected)
        => Assert.Equal(Expected(expected), Problems(Sink + Loud + Noisy + Witness + body + "        return .Ok(())\n"));

    // SPEC 4.9.6: a row's destruction is of the Type below its parameter: Dictionary clear destroys the values, insertOrReplace the
    // unused key, also through a function item, Array clear the elements, Intrinsics.replace the old value (fail-before: the first,
    // through insertOrReplace's private body).
    [Theory]
    [InlineData("Dictionary<i32, i32> = [:]", "        self.table.clear()\n        _ = self.table.insertOrReplace(1, 2)\n", "")]
    [InlineData("Dictionary<i32, Loud> = [:]", "        self.table.clear()\n", Failed)]
    [InlineData("Dictionary<LoudKey, i32> = [:]", "        _ = self.table.insertOrReplace(LoudKey.init(), 1)\n", Failed)]
    [InlineData("Dictionary<i32, i32> = [:]", "        let clear = Dictionary<i32, i32>.clear\n        clear(self.table@uniq)\n", "")]
    [InlineData("Dictionary<i32, Loud> = [:]", "        let clear = Dictionary<i32, Loud>.clear\n        clear(self.table@uniq)\n", Failed)]
    [InlineData("Array<i32> = []", "        self.table.clear()\n", "")]
    [InlineData("Array<Loud> = []", "        self.table.clear()\n", Failed)]
    [InlineData("i32 = 0", "        Kimi.Intrinsics.replace(self.table@uniq, with: 3)\n", "")]
    [InlineData("i32 = 0", "        Kimi.Intrinsics.replace(self.loud@uniq, with: Loud.init())\n", Failed)]
    public void ARowDestroysBelowItsParameter(string table, string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "struct LoudKey\n    Self is Equatable\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n" +
                "    drop => Console.writeLine(\"drop\")\n" + WithField($"table: {table}") + body + "        return .Ok(())\n"));

    // SPEC 4.9.6, 8.11.3 (R4-M28): a row's requirement on an abstract Type composes like its destruction, resolved where the Type closes;
    // a requirement call written in a generic body keeps the bounds of that body's premises (X2), whatever its caller's Type (fail-before:
    // the first, through the private Storage helper's requirement call).
    [Theory]
    [InlineData("i32", "value", "Equatable", "_ = table.remove(key)", "")]
    [InlineData("Noisy", "Noisy.init()", "Equatable", "_ = table.remove(key)", Failed)]
    [InlineData("i32", "value", "Equatable", "_ = key == key", Failed)]
    [InlineData("Key", "Key.init()", "Fixed", "_ = key == key", "")]
    public void ARowsRequirementComposesAtTheCaller(string key, string argument, string premise, string statement, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + Noisy + Fixed + $"group H\n    public func put<K>(table: uniq/Dictionary<K, i32>, key: ref/K)\n        K is {premise}\n" +
                $"        {statement}\n" + WithField($"table: Dictionary<{key}, i32> = [:]") + $"        H.put(self.table@uniq, {argument})\n        return .Ok(())\n"));

    // SPEC 4.9.6, 8.11.3: at the top, a row's requirement on an abstract Type is judged by the premises (fail-before: the first).
    [Theory]
    [InlineData("Fixed", "")]
    [InlineData("Equatable", Failed)]
    public void ARowsRequirementIsJudgedByThePremisesAtTheTop(string premise, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Fixed + $"struct G<K>\n    K is {premise}\n    Self is Sink\n    var table: Dictionary<K, i32> = [:]\n    var key: K\n" +
                "    public init(key: K) => self.key = key@move\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n" +
                "        _ = self.table.tryGet(self.key)\n        return .Ok(())\n"));

    // SPEC 4.9.6, 8.11.3: a formatting function's row runs the value's Utf8Format witness: built-in for i32, Shown's printing one, also in
    // an interpolation (fail-before: each, a limit).
    [Theory]
    [InlineData("        _ = Text.toString(42)\n        _ = \"\\(value)\"\n", "")]
    [InlineData("        _ = Text.toString(Shown.init())\n", Failed)]
    [InlineData("        let shown = Shown.init()\n        _ = \"\\(shown)\"\n", Failed)]
    public void FormattingRunsTheValuesWitness(string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "struct Shown\n    Self is Utf8Format\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>\n" +
                "        Console.writeLine(\"format\")\n        return .Ok(())\n" + Witness + body + "        return .Ok(())\n"));

    // SPEC 8.11.3, 13.4: a tuple operator runs its leaves' witnesses (its expansion, DECISIONS R5-M75): built-in for i32, Compared's
    // environment read, also nested; a leaf with an abstract part runs its requirement under the caller's premises: Fixed's confined
    // equals, and Equatable's, which bounds no effect (fail-before: the first three, a limit; the fourth was a limit until R5-M75).
    [Theory]
    [InlineData("        _ = (value, 1) == (value, 2)\n        _ = value == 2\n", "")]
    [InlineData("        let first = Compared.init()\n        _ = (first@ref, 1) == (first@ref, 2)\n", Failed)]
    [InlineData("        let first = Compared.init()\n        _ = ((first@ref, 1), 3) == ((first@ref, 2), 3)\n", Failed)]
    [InlineData("        let key = Key.init()\n        _ = H.same(key, key)\n", "")]
    [InlineData("        let key = Key.init()\n        _ = H.loose(key, key)\n", Failed)]
    // G308 E1: a DAG Type of depth 4, whose 16 leaves share one witness: Compared's environment read fails, Key's pure equals does not.
    [InlineData("        let first = Compared.init()\n        let t1 = (first@ref, first@ref)\n" + Dag4, Failed)]
    [InlineData("        let first = Key.init()\n        let t1 = (first@ref, first@ref)\n" + Dag4, "")]
    [InlineData("        let first = Ranked.init()\n        let t1 = (first@ref, first@ref)\n" + Dag4Less, Failed)]
    [InlineData("        let first = Level.init()\n        let t1 = (first@ref, first@ref)\n" + Dag4Less, "")]
    // G308 E2: a generic body comparing a DAG Self over its leaf T, Fixed's confined equals and Equatable's, which bounds no effect.
    [InlineData("        let key = Key.init()\n        _ = H.deep(key, key)\n", "")]
    [InlineData("        let key = Key.init()\n        _ = H.deepLoose(key, key)\n", Failed)]
    public void ACompositeComparisonRunsItsPartsWitnesses(string body, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + Fixed + Ordered + "group State\n    public var value: i32 = 0\nstruct Compared\n    Self is Equatable\n" +
                "    public func equals(self: ref/Self, other: ref/Self) -> bool\n        _ = State.value\n        return true\n" +
                "group H\n    public func same<T>(a: ref/T, b: ref/T) -> bool\n        T is Fixed\n        return (a, 1) == (b, 1)\n" +
                "    public func loose<T>(a: ref/T, b: ref/T) -> bool\n        T is Equatable\n        return (a, 1) == (b, 1)\n" +
                DeepSame.Replace("Fixed", "Equatable", StringComparison.Ordinal).Replace("deep", "deepLoose", StringComparison.Ordinal) + DeepSame +
                Witness + body + "        return .Ok(())\n"));

    // SPEC 8.11.3: a requirement on an open Type that is not abstract runs the witness its conformance maps it to, entered through the
    // instance (fail-before: the first, judged by the premises).
    [Theory]
    [InlineData(" => true\n", "")]
    [InlineData("\n        Console.writeLine(\"eq\")\n        return true\n", Failed)]
    public void AnOpenSelfRunsItsWitness(string equals, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + $"struct Box<T>\n    Self is Equatable\n    public func equals(self: ref/Self, other: ref/Self) -> bool{equals}" +
                "group H\n    public func find<K>(table: ref/Dictionary<Box<K>, i32>, key: ref/Box<K>)\n        _ = table.tryGet(key)\n" +
                WithField("boxes: Dictionary<Box<i32>, i32> = [:]") + "        H.find(self.boxes@ref, Box<i32>.init())\n        return .Ok(())\n"));

    // SPEC 8.11.3: a requirement on a Type whose conformance maps it to a base's function runs that inherited witness, entered through
    // the base's instance (the base path ConformanceWitness resolves; fail-before: each, a limit of the formatting function).
    [Theory]
    [InlineData(" => .Ok(())\n", "")]
    [InlineData("\n        Console.writeLine(\"format\")\n        return .Ok(())\n", Failed)]
    public void AnInheritedWitnessRunsThroughItsBase(string format, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + "open struct Base\n    public init() => ()\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>" +
                $"{format}struct Leaf : Base\n    Self is Utf8Format\n    public init() => ()\n" + Witness + "        _ = Text.toString(Leaf.init())\n" +
                "        return .Ok(())\n"));

    // SPEC 8.11.3: a witness entered for a row runs its own body, whose requirement call on its parameter keeps the witness's premises
    // (X2) whatever the instance: `Box<Key>`'s equality is confined under `T is Fixed` and unknown under `T is Equatable`, reached
    // directly and through a generic helper (fail-before: the confined ones, through the private Storage helper).
    [Theory]
    [InlineData("Fixed", "_ = self.boxes.tryGet(Box<Key>.init(Key.init()))", "")]
    [InlineData("Equatable", "_ = self.boxes.tryGet(Box<Key>.init(Key.init()))", Failed)]
    [InlineData("Fixed", "H.find(self.boxes@ref, Box<Key>.init(Key.init()))", "")]
    [InlineData("Equatable", "H.find(self.boxes@ref, Box<Key>.init(Key.init()))", Failed)]
    public void AWitnessKeepsItsOwnPremises(string premise, string statement, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(Sink + Loud + Fixed + $"struct Box<T>\n    T is {premise}\n    Self is Equatable\n    public var value: T\n" +
                "    public init(value: T) => self.value = value@move\n    public func equals(self: ref/Self, other: ref/Self) -> bool => self.value == other.value\n" +
                $"group H\n    public func find<K>(table: ref/Dictionary<Box<K>, i32>, key: ref/Box<K>)\n        K is {premise}\n        _ = table.tryGet(key)\n" +
                WithField("boxes: Dictionary<Box<Key>, i32> = [:]") + $"        {statement}\n        return .Ok(())\n"));

    // SPEC 15.3.5, 16.3.3, 8.15: a growing recursive Type, Nest<(T, T)> below a Sealed rc payload, ends at the destruction walk's bound,
    // since the payload's walk goes on below its handle: a body destroying one is analyzed (it hung before), and where it owes confined
    // that destruction is a limit, OwnershipDepthLimit_Kd (the owner's 「G272: (1)」, R4-M153).
    [Fact]
    public void AGrowingRecursiveDestructionEndsAtTheBound()
    {
        const string Nest = "struct Nest<T>\n    var value: T\n    var next: Option<rc/Nest<(T, T)>>\n    public init(value: T)\n" +
            "        self.value = value@move\n        self.next = .None\n";
        Assert.Empty(Problems(Nest + "func release(nest: Nest<i32>) => ()\n"));
        Assert.Equal(
            ["OwnershipDepthLimit_Kd Self is Sink 1"],
            Problems(Sink + Loud + Nest + Witness + "        _ = Nest<i32>.init(value: 1)\n        return .Ok(())\n"));
    }

    // SPEC 16.3, 15.3.5 (DECISIONS R4-M58): the destruction walk enters each Type once per query, so a recursive Type that repeats through
    // an Array ends where it repeats: a confined witness destroying one has no limit (before, the walk reached its bound).
    [Fact]
    public void ARepeatingRecursiveDestructionEndsWhereItRepeats()
    {
        const string Tree = "struct Tree\n    var children: Array<Tree>\n    public init()\n        self.children = []\n";
        Assert.Empty(Problems(Sink + Loud + Tree + Witness + "        _ = Tree.init()\n        return .Ok(())\n"));
    }

    // SPEC 16.3, 8.11.2 (R4-M22): an Open Drop destroys each part its own bit covers, which no child row covers: a struct whose part 64
    // is partly moved still destroys its part 63, whose destructor writes a mutable static, so a confined witness fails.
    [Fact]
    public void AnOpenDropDestroysEveryUncoveredPart()
    {
        var wide = new StringBuilder("group Metrics\n    public var n: i32 = 0\nstruct Counted\n    public var k: i32 = 0\n    drop => Metrics.n += 1\nstruct Wide\n");
        for (var i = 0; i < 63; i++)
        {
            wide.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var x{i}: i32 = 0\n");
        }

        wide.Append("    public var x63: Counted = Counted.init()\n    public var x64: (string, i32) = (\"a\", 1)\n    public init() => ()\n");
        Assert.Equal([Failed], Problems(Sink + Loud + wide + Witness + "        var w = Wide.init()\n        let s = w.x64.0@move\n        return .Ok(())\n"));
    }

    // SPEC 15.3.5, 8.15 (DECISIONS R4-M58): a chain that grows through a destructor's composed atom, D's T as G<(U, U)>, terminates:
    // Binding's storage instantiation bounds the growing instance (TyCtxt.Apply gives none), so the first composition is a limit, the
    // located Unsupported_Kd where it owes confined.
    [Fact]
    public void AChainGrowingThroughADestructorIsALimit()
    {
        const string Chain = "struct D<T>\n    var value: T\n    drop => ()\nstruct G<U>\n    var next: Option<rc/D<G<(U, U)>>>\n    public init()\n" +
            "        self.next = .None\n";
        Assert.Empty(Problems(Chain + "func release(g: G<i32>) => ()\n"));
        Assert.Equal(["Unsupported_Kd Self is Sink 1"], Problems(Sink + Loud + Chain + Witness + "        _ = G<i32>.init()\n        return .Ok(())\n"));
    }

    // R4-M1 (G126): A4 records the program version MirProgram.Version names, whatever body comes first: a fresh A4 recording the bodies
    // last first, so that body 0's first view comes last and must not start a version, finds the witness's failure, which composes its
    // call of G.noisy (before, that view restarted the recording and lost every other body); the next version starts empty, so recording
    // it in the driver's order takes a fresh A4's evaluations; and a version whose bodies Local never recorded, here one without bodies,
    // solves no node (before, Run evaluated the previous version's nodes against it).
    [Fact]
    public void LocalRecordsTheProgramsVersion()
    {
        var c = MirBuildTest.Compile(Sink + Loud + "group G\n    public func noisy() => Console.writeLine(\"x\")\n" + Witness + "        G.noisy()\n" +
            "        return .Ok(())\npublic func main() => ()\n");
        var program = c.BuildMir();
        var (cases, flow) = (new MirCases(c.Types), new MirDataflow(new MirReach()));
        var effects = new MirEffects(c.Types, cases, flow);
        Assert.Equal([Failed], Record(lastFirst: true));
        program.Version++;
        Assert.Equal([Failed], Record(lastFirst: false));
        var fresh = new MirEffects(c.Types, cases, flow);
        Analyze(fresh, flow, cases, program, new MirReport());
        Assert.Equal(fresh.Evaluations, effects.Evaluations);
        effects.Run(new MirProgram { Version = program.Version + 1 }, new MirReport());
        Assert.Equal(0, effects.Evaluations);

        string[] Record(bool lastFirst)
        {
            var bodies = program.Bodies.ToArray();
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[lastFirst ? bodies.Length - 1 - i : i];
                flow.Begin(body);
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    cases.Select(body, view);
                    effects.Local(program, body, view);
                }
            }

            var report = new MirReport();
            effects.Run(program, report);
            return Problems(c, report);
        }
    }

    // V16 (plan "New V-checks"): A4 is linear: an N-deep call chain whose last member prints and an N-member ring of mutual calls (one
    // member prints), each with one problem, grow the evaluations and the cold allocation of a fresh A4 over the program (the library
    // included) linearly; a warm re-analysis of the same bodies in a new version, which Local starts, allocates nothing and takes the same
    // evaluations. N is 256/512/1024 so that the program's own bodies, not the library's, fill the pooled arrays, whose doubling makes the
    // allocation of smaller programs step (16/32/64: +336, then +9,504).
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
            var flow = new MirDataflow(new MirReach());
            var effects = new MirEffects(c.Types, cases, flow);
            var before = GC.GetAllocatedBytesForCurrentThread();
            Analyze(effects, flow, cases, program, report);
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            evaluations[i] = effects.Evaluations;
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                program.Version++; // A new version of the same bodies, which Local starts.
                report.Clear();
                Analyze(effects, flow, cases, program, report);
            }));
            Assert.Equal(evaluations[i], effects.Evaluations);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: evaluations {string.Join(", ", evaluations)}, cold A4 allocation {string.Join(", ", allocated)}");
        Assert.True(evaluations[2] - evaluations[1] <= 2 * (evaluations[1] - evaluations[0]), $"evaluations {string.Join(", ", evaluations)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold A4 allocation {string.Join(", ", allocated)}");
    }

    // The driver's A4 rows over a built program and its A2 table: each body begun, its views' local facts, then the run.
    private static void Analyze(MirEffects effects, MirDataflow flow, MirCases cases, MirProgram program, MirReport report)
    {
        foreach (var body in program.Bodies)
        {
            flow.Begin(body);
            for (var view = 0L; view < MirCases.Count(body); view++)
            {
                cases.Select(body, view);
                effects.Local(program, body, view);
            }
        }

        effects.Run(program, report);
    }

    private static string[] Expected(string expected) => expected.Length == 0 ? [] : expected.Split('|');

    // The witness S with one more Field.
    private static string WithField(string field) => Witness.Replace("Loud.init()\n", $"Loud.init()\n    var {field}\n", StringComparison.Ordinal);

    private static string[] Problems(string source)
    {
        var c = MirBuildTest.Compile(source + "public func main() => ()\n");
        return Problems(c, c.BuildMir());
    }

    // The A4 problems of module 0: code, the first line of the subject, views.
    private static string[] Problems(Compilation c, MirProgram program)
    {
        Assert.Empty(program.Findings);
        return Problems(c, program.Problems);
    }

    private static string[] Problems(Compilation c, MirReport report)
        => [.. report.All.ToArray().Where(static x => x.Subject.Module == 0 && x.Code is DiagnosticCode.IncompatibleContractImplementation_Kd or
            DiagnosticCode.UnsatisfiedEffectBound_Kd or DiagnosticCode.Unsupported_Kd or DiagnosticCode.OwnershipDepthLimit_Kd).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()} {x.Views}")];
}
