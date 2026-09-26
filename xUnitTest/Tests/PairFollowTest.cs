// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 13.5.5.1 pair layers: a pair s/T whose admitted set lies in value or valueborrow is a safe value-reference
/// layer that may or may not exist. @follow selects its direct target; each operation applies the rule of every admitted
/// Semantics, takes the weakest capability and never offers Take.</summary>
public class PairFollowTest
{
    private const string Box =
        "struct Box<E>\n    public var item: E\n    public init(item: E) => self.item = item@move\n";

    private const string Explicit = Box +
        "func view<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is value or valueborrow\n    return box.item@follow@ref\n" +
        "func viewUniq<s/T>(box: uniq/Box<s/T>) -> uniq/T during box\n    s is owner or uniq\n    return box.item@follow@uniq\n" +
        "func read<s/T>(box: ref/Box<s/T>) -> T\n    s is value or valueborrow\n    T is Copy\n    return box.item@follow\n" +
        "func shared(number: ref/i32)\n" +
        "    let box = Box<ref/i32 during number>.init(item: number)\n" +
        "    let b = view(box)\n    require b == 11 and read(box) == 11 else => $abort(\"shared\")\n" +
        "    Console.writeLine(\"Shared followed.\")\n" +
        "func exclusive(target: uniq/i32)\n" +
        "    var box = Box<uniq/i32 during target>.init(item: target@move)\n" +
        "    let inner = viewUniq(box@uniq)\n    inner@follow = 9\n" +
        "    let c = view(box)\n    require c == 9 and read(box) == 9 else => $abort(\"exclusive\")\n" +
        "public func main()\n" +
        "    var owned = Box<i32>.init(item: 7)\n" +
        "    let a = view(owned)\n    require a == 7 else => $abort(\"view\")\n" +
        "    let slot = viewUniq(owned@uniq)\n    slot@follow = 8\n" +
        "    require owned.item == 8 and read(owned) == 8 else => $abort(\"owned\")\n" +
        "    Console.writeLine(\"Owned followed.\")\n" +
        "    let number: i32 = 11\n    shared(number@ref)\n" +
        "    var target: i32 = 2\n    exclusive(target@uniq)\n" +
        "    require target == 9 else => $abort(\"target\")\n" +
        "    Console.writeLine(\"Exclusive followed.\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Explicit", Explicit, "Owned followed.\nShared followed.\nExclusive followed.\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("PairFollow" + name, source, stdout);

    [Theory]
    [InlineData("func f<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    return box.item@follow@ref", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("func f<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is owner or obj\n    return box.item@follow@ref", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("func f<s/T>(box: uniq/Box<s/T>) -> uniq/T during box\n    s is value or valueborrow\n    return box.item@follow@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func f<s/T>(box: Box<s/T>) -> T\n    s is value\n    return box.item@follow@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData("func f<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is owner or uniq\n    return box.item@follow@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    public void Rejects(string function, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Box + function);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Theory]
    [InlineData("func g<s/T>(box: ref/Box<s/T>) -> ref/(s/T) during box\n    s is value or valueborrow\n    return box.item@ref")]
    [InlineData("func g<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is value or valueborrow\n    return box.item@follow@ref")]
    [InlineData("func g<s/T>(box: ref/Box<s/T>)\n    s is value or valueborrow\n    let r = box.item@ref")]
    public void SlotBorrowsAndFollowsVerify(string source)
    {
        var c = MinimalEmissionTest.Analyze(Box + source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ConcreteOwnedPlaceIsNotFollowed()
    {
        var c = MinimalEmissionTest.Analyze("let x: i32 = 1\nlet y = x@follow");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
