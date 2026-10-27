// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate): SPEC 4.9.6's
// rows for Array `truncate` ("Destruction of the relevant `T`") and `removeAll` ("Calls of `matching`; destruction of the removed `T`")
// as A4 composes them, the row through the call's substitution, the Callable argument's calls through the actual argument (SPEC 8.11.4,
// 15.6.4), on the MIR (A4's problems) and as the published records of both pipelines.
public sealed partial class MirEffectsTest
{
    // A printing and a quiet predicate over `ref/i32`.
    private const string Predicates = "group H\n    public func noisyTrue(v: ref/i32) -> bool\n        Console.writeLine(\"x\")\n        return true\n" +
        "    public func quietTrue(v: ref/i32) -> bool => true\n";

    // A generic member of H that composes `matching` through its Callable input, whose premise bounds it.
    private const string PruneBound = "group H\n    public func pruneBound<F>(a: uniq/Array<i32>, f: F)\n        F is Callable<(ref/i32) -> bool>\n" +
        "            effect confined\n        a.removeAll(matching: f@move)\n";

    private const string Confined = "IncompatibleContractImplementation_Kd";

    // SPEC 4.9.6, 8.11.4, 15.6.4 (PLAN G264 U0′): `truncate` and `removeAll` compose through their rows at the caller: the elements'
    // destruction, which `Loud`'s drop makes an environment effect (E5, E7; E8 `clear`'s row, the comparison), and `removeAll`'s calls of
    // `matching` through the actual argument, a printing function item (E1) or a Closure calling it (E3), each `confined`'s violation at
    // the conformance on the MIR and at the call in the published records; the quiet twins (E2, E4, E6) are valid. A row without the
    // Callable composition would accept E1 and E3. Guards: each row holds today through the bodies and must hold through the rows.
    [Theory]
    [InlineData("E1", "Array<i32> = []", "        self.table.removeAll(matching: H.noisyTrue)\n", Failed, Confined + " self.table.removeAll(matching: H.noisyTrue)")]
    [InlineData("E2", "Array<i32> = []", "        self.table.removeAll(matching: H.quietTrue)\n", "", "")]
    [InlineData("E3", "Array<i32> = []", "        self.table.removeAll(matching: func (v) => H.noisyTrue(v))\n", Failed, Confined + " self.table.removeAll(matching: func (v) => H.noisyTrue(v))")]
    [InlineData("E4", "Array<i32> = []", "        self.table.removeAll(matching: func (v) => true)\n", "", "")]
    [InlineData("E5", "Array<Loud> = []", "        self.table.truncate(0)\n", Failed, Confined + " self.table.truncate(0)")]
    [InlineData("E6", "Array<i32> = []", "        self.table.truncate(0)\n", "", "")]
    [InlineData("E7", "Array<Loud> = []", "        self.table.removeAll(matching: func (v) => true)\n", Failed, Confined + " self.table.removeAll(matching: func (v) => true)")]
    [InlineData("E8", "Array<Loud> = []", "        self.table.clear()\n", Failed, Confined + " self.table.clear()")]
    public void ATruncateOrRemoveAllRowComposesItsDestructionAndItsCallback(string form, string table, string body, string problems, string records)
    {
        var source = Sink + Loud + Predicates + WithField($"table: {table}") + body + "        return .Ok(())\n";
        AssertRows(form, source, problems, records);
    }

    // SPEC 8.11.4, 4.9.6 (PLAN G264 U0′): a call through an abstract Callable input composes `matching` by the input's premise, so
    // `pruneBound`'s body is valid and a `confined` witness calling it with a quiet function item is valid (pruneWitness); the bound is
    // owed where the input binds: `H.noisyTrue` as `pruneBound`'s argument is `UnsatisfiedEffectBound_Kd` there, `H.quietTrue` valid
    // (pruneUse). Guards: each row holds today through the body and must hold through the row.
    [Theory]
    [InlineData("pruneUse", "func pruneUse()\n    var a: Array<i32> = [1]\n    H.pruneBound(a@uniq, H.noisyTrue)\n    H.pruneBound(a@uniq, H.quietTrue)\n", "UnsatisfiedEffectBound_Kd H.noisyTrue 18446744073709551615", "UnsatisfiedEffectBound_Kd H.noisyTrue")]
    [InlineData("pruneWitness", "        H.pruneBound(self.table@uniq, H.quietTrue)\n        return .Ok(())\n", "", "")]
    public void ACallThroughAnAbstractCallableInputKeepsItsPremisesBound(string form, string body, string problems, string records)
    {
        var source = Sink + Loud + Predicates + PruneBound + (form == "pruneUse" ? body : WithField("table: Array<i32> = []") + body);
        AssertRows(form, source, problems, records);
    }

    // A form's A4 problems on the MIR and its published Error records on both pipelines, each as the form's row.
    private static void AssertRows(string form, string source, string problems, string records)
    {
        Assert.Equal($"{form}: {problems}", $"{form}: {string.Join("|", Problems(source))}");
        foreach (var pipeline in new[] { CompilerPipeline.Legacy, CompilerPipeline.Mir })
        {
            Assert.Equal($"{form} {pipeline}: {records}", $"{form} {pipeline}: {MirBorrowsTest.Records(source + "public func main() => ()\n", pipeline)}");
        }
    }
}
