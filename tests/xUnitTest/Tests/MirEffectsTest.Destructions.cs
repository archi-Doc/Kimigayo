// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate): SPEC 4.9.6's
// rows for Array `truncate` ("Destruction of the relevant `T`") and `removeAll` ("Calls of `matching`; destruction of the removed `T`")
// as A4 composes them, the row through the call's substitution, the Callable argument's calls through the actual argument (SPEC 8.11.4,
// 15.6.4), on the MIR (A4's problems) and as the published records, which check publishes (DECISIONS R5-M156).
public sealed partial class MirEffectsTest
{
    // A printing and a quiet predicate over `ref/i32`.
    private const string Predicates = "group H\n    public func noisyTrue(v: ref/i32) -> bool\n        Console.writeLine(\"x\")\n        return true\n" +
        "    public func quietTrue(v: ref/i32) -> bool => true\n";

    // A generic member of H that composes `matching` through its Callable input, whose premise bounds it.
    private const string PruneBound = "group H\n    public func pruneBound<F>(a: uniq/Array<i32>, f: F)\n        F is Callable<(ref/i32) -> bool>\n" +
        "            effect confined\n        a.removeAll(matching: f@move)\n";

    private const string Confined = "IncompatibleContractImplementation_Kd";

    // A4's published record of a `confined` violation: at the conformance subject, the `Self is Sink` clause (SPEC 8.11.3; R6-check H5
    // T-14a's ruling, DIAGNOSTICS' bound-violation paragraph).
    private const string AtSink = Confined + " Self is Sink";

    // A struct without a destructor, the quiet twin of `Loud`, and a local of each that a Closure captures by move.
    private const string Quiet = "struct Quiet\n    public var n: i32 = 0\n";

    private const string LoudLocal = "        let loud = Loud.init()\n";

    private const string QuietLocal = "        let quiet = Quiet.init()\n";

    private const string LocalArray = "        var a: Array<i32> = [1]\n";

    private const string RefLoud = "        let r = self.loud@ref\n";

    // The call moving that `Loud` into `removeAll`'s by-value input, directly and through `pruneBound`.
    private const string RemoveLoud = "removeAll(matching: func [loud@move] (v) => loud.n == 0)";

    private const string PruneLoud = "H.pruneBound(self.table@uniq, func [loud@move] (v) => loud.n == 0)";

    // The confined witness's head before its `put`, and its generic twin.
    private const string Plain = "struct S\n    Self is Sink\n";

    private const string Generic = "struct S<T>\n    Self is Sink\n";

    // A struct whose only Field of a Type with a drop is a `[0 of Loud]`, and its twin over one Loud.
    private const string ZeroField = "struct Wrap\n    public var z: [0 of Loud]\n    public var k: i32\n    public init(k: i32)\n        self.z = []\n" +
        "        self.k = k\n";

    private const string OneField = "struct Wrap\n    public var z: [1 of Loud]\n    public var k: i32\n    public init(k: i32)\n" +
        "        self.z = [Loud.init()]\n        self.k = k\n";

    // A non-regular Type through a handle (SPEC 15.3.5) behind an array without an element: G<T> holds `[0 of rc/G<(T, T)>]`.
    private const string Growing = "struct G<T>\n    public var kids: [0 of rc/G<(T, T)>]\n    public init() => self.kids = []\n";

    // SPEC 4.9.6, 8.11.4, 15.6.4 (PLAN G264 U0′): `truncate` and `removeAll` compose through their rows at the caller: the elements'
    // destruction, which `Loud`'s drop makes an environment effect (E5, E7; E8 `clear`'s row, the comparison), and `removeAll`'s calls of
    // `matching` through the actual argument, a printing function item (E1) or a Closure calling it (E3), each `confined`'s violation at
    // the conformance on the MIR and in the published records (`Self is Sink`, T-14a's ruling since the MIR decides acceptance); the
    // quiet twins (E2, E4, E6) are valid. A row without the Callable composition would accept E1 and E3. The by-value input's destruction after the last call (SPEC 15.6.4, 16.3.3) runs the
    // `drop` of a `Loud` its Closure owns (RmLoud; RmLoudLocal on a local Array), a `Quiet` nothing (RmQuiet), nor a Closure that only
    // borrows a `Loud` (RmRefLoudLocal); a row without that destruction would accept RmLoud and RmLoudLocal.
    // Guards: each row holds today through the bodies and must hold through the rows.
    [Theory]
    [InlineData("E1", "Array<i32> = []", "        self.table.removeAll(matching: H.noisyTrue)\n", Failed, AtSink)]
    [InlineData("E2", "Array<i32> = []", "        self.table.removeAll(matching: H.quietTrue)\n", "", "")]
    [InlineData("E3", "Array<i32> = []", "        self.table.removeAll(matching: func (v) => H.noisyTrue(v))\n", Failed, AtSink)]
    [InlineData("E4", "Array<i32> = []", "        self.table.removeAll(matching: func (v) => true)\n", "", "")]
    [InlineData("E5", "Array<Loud> = []", "        self.table.truncate(0)\n", Failed, AtSink)]
    [InlineData("E6", "Array<i32> = []", "        self.table.truncate(0)\n", "", "")]
    [InlineData("E7", "Array<Loud> = []", "        self.table.removeAll(matching: func (v) => true)\n", Failed, AtSink)]
    [InlineData("E8", "Array<Loud> = []", "        self.table.clear()\n", Failed, AtSink)]
    [InlineData("RmLoud", "Array<i32> = []", LoudLocal + "        self.table." + RemoveLoud + "\n", Failed, AtSink)]
    [InlineData("RmQuiet", "Array<i32> = []", QuietLocal + "        self.table.removeAll(matching: func [quiet@move] (v) => quiet.n == 0)\n", "", "")]
    [InlineData("RmLoudLocal", "Array<i32> = []", LocalArray + LoudLocal + "        a." + RemoveLoud + "\n", Failed, AtSink)]
    [InlineData("RmRefLoudLocal", "Array<i32> = []", LocalArray + RefLoud + "        a.removeAll(matching: func [r] (v) => r.n == 0)\n", "", "")]
    public void ATruncateOrRemoveAllRowComposesItsDestructionAndItsCallback(string form, string table, string body, string problems, string records)
    {
        var source = Sink + Loud + Quiet + Predicates + WithField($"table: {table}") + body + "        return .Ok(())\n";
        AssertRows(form, source, problems, records);
    }

    // SPEC 8.11.4, 4.9.6 (PLAN G264 U0′): a call through an abstract Callable input composes `matching` by the input's premise, so
    // `pruneBound`'s body is valid and a `confined` witness calling it with a quiet function item is valid (pruneWitness); the bound is
    // owed where the input binds: `H.noisyTrue` as `pruneBound`'s argument is `UnsatisfiedEffectBound_Kd` there, `H.quietTrue` valid
    // (pruneUse). The bound covers only the call: `removeAll`'s destruction of the input it receives is the witness's effect (SPEC 8.11.4,
    // 15.6.4), a moved `Loud`'s `drop` (PruneLoud, published at `Self is Sink`), a `Quiet` nothing (PruneQuiet). Guards: each row holds
    // today through the body and must hold through the row.
    [Theory]
    [InlineData("pruneUse", "func pruneUse()\n    var a: Array<i32> = [1]\n    H.pruneBound(a@uniq, H.noisyTrue)\n    H.pruneBound(a@uniq, H.quietTrue)\n", "UnsatisfiedEffectBound_Kd H.noisyTrue 18446744073709551615", "UnsatisfiedEffectBound_Kd H.noisyTrue")]
    [InlineData("pruneWitness", "        H.pruneBound(self.table@uniq, H.quietTrue)\n        return .Ok(())\n", "", "")]
    [InlineData("PruneLoud", LoudLocal + "        " + PruneLoud + "\n        return .Ok(())\n", Failed, AtSink)]
    [InlineData("PruneQuiet", QuietLocal + "        H.pruneBound(self.table@uniq, func [quiet@move] (v) => quiet.n == 0)\n        return .Ok(())\n", "", "")]
    public void ACallThroughAnAbstractCallableInputKeepsItsPremisesBound(string form, string body, string problems, string records)
    {
        var source = Sink + Loud + Quiet + Predicates + PruneBound + (form == "pruneUse" ? body : WithField("table: Array<i32> = []") + body);
        AssertRows(form, source, problems, records);
    }

    // SPEC 4.1, 8.11.2, 16.3.2 (R8-X1 U3; O-486 part 4 (a)): a fixed array of constant length 0 destroys nothing whatever its element is,
    // so a `confined` witness may create, replace and drop one of Loud (each V form valid): as a local (e12, e12asg), of an evaluated
    // constant length (tconst, texpr), nested either way (tnest20, tnest02), in a Tuple, an Option payload, a struct's Field, also under a
    // declared drop (e12tup, e12opt, e12field, e12fieldd), as an Array's element (e12arr), over an abstract element (e12genT, tgenopt0: the
    // destruction depends on no abstract Type, so 8.11.2's abstract row does not apply), as its own Field replaced (tself), a callee's
    // parameter (tcallee) and a discarded result (ttemp). A non-regular Type behind one is never walked (tgrow: no OwnershipDepthLimit_Kd
    // at the conformance; its GenerationResourceLimit_Kd leaves too, SPEC 8.15.3: no instance is required). Each I twin runs Loud's drop
    // (one element: e12one, ttup1, topt1, tfield1, tself1, tcallee1, tmix; an abstract one: tgenopt1) or another environment effect
    // (tplus, one record), and a generic callee's summary keeps the destruction of its Type parameter (SPEC 15.6.4: once per declaration),
    // composed at the call as Loud's whatever the length (e12gen0).
    [Theory]
    [InlineData("e12", "", Plain, "        let none: [0 of Loud] = []\n", true)]
    [InlineData("e12tup", "", Plain, "        let t: ([0 of Loud], i32) = ([], 1)\n", true)]
    [InlineData("e12opt", "", Plain, "        let o: Option<[0 of Loud]> = .Some([])\n", true)]
    [InlineData("e12field", ZeroField, Plain, "        let w = Wrap.init(k: 1)\n", true)]
    [InlineData("e12fieldd", ZeroField + "    drop => ()\n", Plain, "        let w = Wrap.init(k: 1)\n", true)]
    [InlineData("e12arr", "", Plain, "        let a: Array<[0 of Loud]> = []\n", true)]
    [InlineData("e12asg", "", Plain, "        var none: [0 of Loud] = []\n        none = []\n", true)]
    [InlineData("e12genT", "", Generic, "        let none: [0 of T] = []\n", true)]
    [InlineData("tconst", "group Dim\n    public let Zero: isize = 0\n", Plain, "        let none: [(Dim.Zero) of Loud] = []\n", true)]
    [InlineData("texpr", "", Plain, "        let none: [(1 - 1) of Loud] = []\n", true)]
    [InlineData("tnest20", "", Plain, "        let n: [2 of [0 of Loud]] = [[], []]\n", true)]
    [InlineData("tnest02", "", Plain, "        let n: [0 of [2 of Loud]] = []\n", true)]
    [InlineData("tself", "", Plain + "    var z: [0 of Loud] = []\n", "        self.z = []\n", true)]
    [InlineData("tgenopt0", "", Generic + "    var item: Option<[0 of T]> = .None\n", "        self.item = .None\n", true)]
    [InlineData("tcallee", "group Hold\n    public func take(a: [0 of Loud]) => ()\n", Plain, "        let none: [0 of Loud] = []\n        Hold.take(none@move)\n", true)]
    [InlineData("ttemp", "group Make\n    public func empty() -> [0 of Loud] => []\n", Plain, "        _ = Make.empty()\n", true)]
    [InlineData("tgrow", Growing, Plain, "        let g = G<i32>.init()\n", true)]
    [InlineData("e12one", "", Plain, "        let one: [1 of Loud] = [Loud.init()]\n", false)]
    [InlineData("ttup1", "", Plain, "        let t: ([1 of Loud], i32) = ([Loud.init()], 1)\n", false)]
    [InlineData("topt1", "", Plain, "        let o: Option<[1 of Loud]> = .Some([Loud.init()])\n", false)]
    [InlineData("tfield1", OneField, Plain, "        let w = Wrap.init(k: 1)\n", false)]
    [InlineData("tself1", "", Plain + "    var z: [1 of Loud] = [Loud.init()]\n", "        self.z = [Loud.init()]\n", false)]
    [InlineData("tcallee1", "group Hold\n    public func take(a: [1 of Loud]) => ()\n", Plain, "        let one: [1 of Loud] = [Loud.init()]\n        Hold.take(one@move)\n", false)]
    [InlineData("tgenopt1", "", Generic + "    var item: Option<[1 of T]> = .None\n", "        self.item = .None\n", false)]
    [InlineData("tmix", "", Plain, "        let t: ([0 of Loud], [1 of Loud]) = ([], [Loud.init()])\n", false)]
    [InlineData("tplus", "", Plain, "        let none: [0 of Loud] = []\n        Console.writeLine(\"x\")\n", false)]
    [InlineData("e12gen0", "group Hold\n    public func sink<length N, T>(a: [N of T]) => ()\n", Plain, "        let none: [0 of Loud] = []\n        Hold.sink(none@move)\n", false)]
    public void AConfinedWitnessMayDestroyAFixedArrayWithoutAnElement(string form, string declarations, string witness, string body, bool valid)
        => AssertRows(
            form, Sink + Loud + declarations + witness + Put + body + "        return .Ok(())\n", valid ? string.Empty : Failed, valid ? string.Empty : AtSink);

    // A form's A4 problems on the MIR and its published Error records, each as the form's row. Check publishes them
    // (DECISIONS R5-M156, PLAN G314).
    private static void AssertRows(string form, string source, string problems, string records)
    {
        Assert.Equal($"{form}: {problems}", $"{form}: {string.Join("|", Problems(source))}");
        Assert.Equal($"{form} published: {records}", $"{form} published: {MirBorrowsTest.Records(source + "public func main() => ()\n")}");
    }
}
