// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.5, 4.7: an Array of a zero-sized element keeps its length and element operations without allocating
/// (stride zero; the buffer is a fixed nonnull substitute) and still runs each element's destruction exactly once.</summary>
public class ZeroSizedArrayTest
{
    private const string UnitOperations =
        "var units: Array<()> = []\n" +
        "require units.length == 0 and units.capacity == 0 else => $abort(\"empty\")\n" +
        "units.append(())\nunits.append(())\nunits.insert(0, ())\nunits.insert(^0, ())\n" +
        "require units.length == 4 and units.capacity >= 4 else => $abort(\"grow\")\n" +
        "units.remove(1)\nunits.remove(^1)\n" +
        "units.swap(first: 0, second: 1)\n" +
        "match units.pop()\n    .Some(_) => ()\n    .None => $abort(\"pop\")\n" +
        "require units.length == 1 else => $abort(\"pop length\")\n" +
        "units.reserve(10)\nunits.shrinkToFit()\n" +
        "require units.length == 1 and units.capacity >= 1 else => $abort(\"shrink\")\n" +
        "let view = units[..]\nrequire view.length == 1 else => $abort(\"slice\")\n" +
        "var count = 0\nfor unit in units => count += 1\nfor unit in units@uniq => count += 1\n" +
        "require count == 2 else => $abort(\"iterate\")\n" +
        "units.clear()\nunits.shrinkToFit()\n" +
        "require units.length == 0 and units.capacity == 0 else => $abort(\"release\")\n" +
        "var reserved = Array<()>.init(capacity: 3)\nreserved.append(())\n" +
        "require reserved.length == 1 and reserved.capacity >= 3 else => $abort(\"init\")\n" +
        "Console.writeLine(\"Units ok.\")";

    private const string Tokens =
        "struct Token\n    public let id: ()\n    public init() => self.id = ()\n    drop => Console.writeLine(\"Token dropped.\")\n" +
        "var tokens: Array<Token> = [Token.init(), Token.init()]\n" +
        "tokens.append(Token.init())\ntokens.insert(1, Token.init())\n" +
        "let taken = tokens.remove(0)\nConsole.writeLine(\"Removed.\")\n" +
        "match tokens.pop()\n    .Some(_) => Console.writeLine(\"Popped.\")\n    .None => $abort(\"pop\")\n" +
        "var spare: Array<Token> = [Token.init()]\nspare.clear()\nConsole.writeLine(\"Cleared.\")\n" +
        "for token in tokens@move => Console.writeLine(\"Iterating.\")\n" +
        "Console.writeLine(\"Done.\")";

    [Fact]
    public void UnitElementsRunEveryOperation()
        => ScalarEmissionTest.EmitFixture("ZeroSizedArrayUnits", UnitOperations, "Units ok.\n");

    [Fact]
    public void ZeroSizedElementsAreDestroyedOnce()
        => ScalarEmissionTest.EmitFixture(
            "ZeroSizedArrayTokens",
            Tokens,
            // The popped Option is a temporary Subject, destroyed after its arm runs.
            "Removed.\nPopped.\nToken dropped.\nToken dropped.\nCleared.\nIterating.\nToken dropped.\nIterating.\nToken dropped.\nDone.\nToken dropped.\n");

    [Theory]
    [InlineData("EmptyStruct", "struct Empty\n    public init() => ()\nvar values: Array<Empty> = [Empty.init()]\nvalues.append(Empty.init())\nrequire values.length == 2 else => $abort(\"empty\")")]
    [InlineData("UnitArray", "var values: Array<[2 of ()]> = [[(), ()]]\nvalues.append([(), ()])\nlet inner = values[1]\nrequire values.length == 2 and inner.length == 2 else => $abort(\"array\")")]
    [InlineData("UnitTuple", "var values: Array<((), ())> = [((), ())]\nvalues.insert(0, ((), ()))\nrequire values.length == 2 else => $abort(\"tuple\")")]
    [InlineData("Generic", "func count<T>(values: Array<T>) -> isize => values.length\nrequire count<()>([(), (), ()]) == 3 else => $abort(\"generic\")")]
    public void ZeroSizedElementShapesExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("ZeroSizedArrayShape" + name, source + "\nConsole.writeLine(\"ok\")", "ok\n");
}
