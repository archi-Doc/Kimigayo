// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 8.11.3, 7.2.3, 8.15: a requirement call whose Self is not abstract runs what its witnesses run; one on an abstract Self keeps
// the caller's premises (X2).
public sealed partial class MirEffectsTest
{
    private const string Unconfined = "IncompatibleContractImplementation_Kd Self is Runner 1";

    private const string Conv = "contract Conv<E>\n    func conv() -> E\n";

    private const string Sized = "contract Sized\n    property size: i32 has get\n";

    private const string Prints = "Console.writeLine(\"loud\")\n";

    private const string Makers = "contract Maker\n    func make() -> Self\ncontract Calm: Maker\n    effect Maker.make confined\n" +
        "struct Box\n    Self is Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func make() -> Self => Box.init(5)\n" +
        "    drop => Console.writeLine(\"drop\")\nstruct Pt\n    Self is Copy and Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n" +
        "    public func make() -> Self => Pt.init(7)\nstruct Shout\n    Self is Copy and Maker\n    public var n: i32\n" +
        "    public init(n: i32) => self.n = n\n    public func make() -> Self\n        Console.writeLine(\"make\")\n        return Shout.init(7)\n" +
        "struct Quiet\n    Self is Maker\n    public var text: string\n    public init(text: string) => self.text = text@move\n" +
        "    public func make() -> Self => Quiet.init(\"q\")\n";

    // An inline default copy (SPEC 7.2.3) keeps `T.make()` as a requirement call on the instance's Self: Pt's, Quiet's and the pair's
    // witnesses are confined; Shout's prints and Box's residual payload runs its destructor (fail-before: the confined three, judged by
    // the default's premises, `T is Maker`). On an abstract Self the call keeps Worker's premises: `U is Calm` declares confined.
    [Theory]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n", "Pt", "")]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n", "Quiet", "")]
    [InlineData("Option<(T, i32)> = .Some((T.make(), 2))", "            .Some((_, let k)) => exit to result k\n", "Pt", "")]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n", "Shout", Unconfined)]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n", "Box", Unconfined)]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n", "U is Copy and Calm", "")]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n", "U is Copy and Maker", Unconfined)]
    public void ARequirementOnAClosedSelfRunsItsWitness(string pending, string arms, string held, string expected)
    {
        var (header, type) = held.StartsWith("U is ", StringComparison.Ordinal) ? ($"struct Worker<U>\n    {held}\n", "U") : ("struct Worker\n", held);
        Assert.Equal(
            Expected(expected),
            Problems(Makers + "contract Runner\n    func run(self: ref/Self) -> i32\n        effect confined\ngroup Helpers\n" +
                "    public func evaluate<T>(sample: ref/T, marker: i32 = label result: do\n        let pending: " + pending + "\n" +
                "        match pending@move\n" + arms + "            .None => exit to result 0\n    ) -> i32\n        T is Maker\n        return marker\n" +
                header + $"    Self is Runner\n    public var held: {type}\n    public init(held: {type}) => self.held = held@move\n" +
                "    public func run(self: ref/Self) -> i32 => Helpers.evaluate(self.held@ref)\n"));
    }

    // SPEC 8.6.1, 11.9.2: the witness is resolved under the Contract reference the call selected (`Conv<i32>`, a bound reference:
    // `Self is Conv<i32>` names it), and a Property requirement's accessor-call witness runs the accessor: a quiet witness is confined, a
    // printing one is the base's independent Language record, never a limit (fail-before: `Unsupported_Kd` on all four).
    [Theory]
    [InlineData(Conv, "Conv<i32>", "    public func conv() -> i32 => 3\n", "T.conv()", "")]
    [InlineData(Conv, "Conv<i32>", "    public func conv() -> i32\n        " + Prints + "        return 3\n", "T.conv()", Unconfined)]
    [InlineData(Sized, "Sized", "    public computed size: i32\n        get() -> i32 => 4\n", "sample.size", "")]
    [InlineData(Sized, "Sized", "    public computed size: i32\n        get() -> i32\n            " + Prints + "            return 4\n", "sample.size", Unconfined)]
    public void AClosedSelfResolvesItsBoundReferenceAndPropertyWitnesses(string contract, string reference, string member, string marker, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(contract + $"struct S\n    Self is {reference}\n    public var n: i32\n    public init(n: i32) => self.n = n\n" + member +
                "contract Runner\n    func run(self: ref/Self) -> i32\n        effect confined\ngroup Helpers\n" +
                $"    public func evaluate<T>(sample: ref/T, marker: i32 = {marker}) -> i32\n        T is {reference}\n        return marker\n" +
                "struct Worker\n    Self is Runner\n    public var held: S\n    public init(held: S) => self.held = held@move\n" +
                "    public func run(self: ref/Self) -> i32 => Helpers.evaluate(self.held@ref)\n"));

    // SPEC 6.4.6 (virtual-dispatch 6): a virtual witness of a closed Self dispatches, so its slot's bound decides, never the original's
    // body: Derived's override may run.
    [Theory]
    [InlineData("", Unconfined)]
    [InlineData("        effect confined\n", "")]
    public void AVirtualWitnessOfAClosedSelfUsesItsSlotsBound(string bound, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems("contract Speaker\n    Self is ObjectPayload\n    func speak(self: objref/Self) -> i32\nopen struct Base\n    Self is Speaker\n" +
                $"    public init() => ()\n    public virtual func speak(self: objref/Self) -> i32\n{bound}        return 1\nstruct Derived : Base\n" +
                "    public init() : base() => ()\n    override func speak(self: objref/Self) -> i32 => 2\ncontract Runner\n" +
                "    func run(self: ref/Self) -> i32\n        effect confined\ngroup Helpers\n" +
                "    public func evaluate<T>(sample: objref/T, marker: i32 = sample.speak()) -> i32\n        T is Speaker\n        return marker\n" +
                "struct Worker\n    Self is Runner\n    public var held: obj/Base\n    public init(held: obj/Base) => self.held = held@move\n" +
                "    public func run(self: ref/Self) -> i32 => Helpers.evaluate(self.held@objref)\n"));
}
