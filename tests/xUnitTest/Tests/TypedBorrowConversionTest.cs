// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class TypedBorrowConversionTest
{
    private const string Counter = "struct Counter\n    public var value: i32 = 0\n";
    private const string Cell = "struct Cell\n    public let value: i32\n    public init(value: i32) => self.value = value\n";
    private const string Tree = "open struct Base\n    public let legs: i32 = 4\n    protected init() => ()\nstruct Leaf : Base\n    public init() : base() => ()\n" +
        "struct Other : Base\n    public init() : base() => ()\n";

    [Theory]
    [InlineData("Shared", Counter + "func read(c: ref/Counter) -> i32 => c.value\nlet c = Counter.init()\nrequire read(c@ref/Counter) == 0 else => $abort(\"value\")")]
    [InlineData("Exclusive", Counter + "func write(c: uniq/Counter) => c.value = 42\nvar c = Counter.init()\nwrite(c@uniq/Counter)\nrequire c.value == 42 else => $abort(\"value\")")]
    [InlineData("Returned", Counter + "func view(c: ref/Counter) -> ref/Counter during c => c@follow@ref/Counter\nlet c = Counter.init()\nrequire view(c).value == 0 else => $abort(\"value\")")]
    public void Executes(string name, string source)
        => ScalarEmissionTest.EmitFixture("TypedBorrow" + name, source, string.Empty);

    [Theory]
    [InlineData(Counter + "let c = Counter.init()\nlet r = c@ref/i32")]
    [InlineData(Counter + "let c = Counter.init()\nlet r = c@uniq/Counter")]
    [InlineData(Counter + "func invalid(c: ref/Counter) => c@uniq/Counter")]
    [InlineData(Counter + "func invalid() -> ref/Counter\n    let c = Counter.init()\n    return c@ref/Counter")]
    [InlineData(Counter + "var c = Counter.init()\nlet r = c@ref/Counter\nc.value = 42\nlet n = r.value")]
    public void RejectsInvalidBorrow(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }

    // SPEC 8.2.3 with 13.5.6 (PLAN G1240): a written V identical to the stored Type by an identity premise names exactly that Type, for
    // each borrow kind and an object upcast (13.6.1, ObjectUpcastFits); a premise naming another Type is still a mismatch at the borrow,
    // and a Place that cannot lend exclusively keeps its own record.
    [Theory]
    [InlineData("func f<T>() -> i32\n    T is i32\n    let v: i32 = 1\n    let r = v@ref/T\n    return r@follow\n", "")]
    [InlineData("func f<T>() -> i32\n    T is i64\n    let v: i32 = 1\n    let r = v@ref/T\n    return 0\n", "TypeMismatch_Kd@4")]
    [InlineData("func f<T>() -> i32\n    T is i32\n    let v: [2 of i32] = [1, 2]\n    let r = v@ref/[2 of T]\n    return 0\n", "")]
    [InlineData("func f<T>() -> i32\n    T is i32\n    var v: i32 = 1\n    let r = v@uniq/T\n    r@follow = 2\n    return v\n", "")]
    [InlineData("func f<T>() -> i32\n    T is i32\n    let v: i32 = 1\n    let r = v@uniq/T\n    return 0\n", "InvalidAssignment_Kd@4")]
    [InlineData(Cell + "func f<T>(h: obj/Cell) -> i32\n    T is Cell\n    let r = h@objref/T\n    return 0\n", "")]
    [InlineData(Cell + "func f<T>() -> i32\n    T is Cell\n    var h = Cell.init(1)@obj\n    let r = h@objuniq/T\n    return 0\n", "")]
    [InlineData(Tree + "func f<T>(h: obj/Leaf) -> i32\n    T is Base\n    let b = h@objref/T\n    return 0\n", "")]
    [InlineData(Tree + "func f<T>(h: obj/Leaf) -> i32\n    T is Other\n    let b = h@objref/T\n    return 0\n", "TypeMismatch_Kd@10")]
    public void AWrittenTypeIdenticalByAPremiseIsTheStoredType(string source, string records)
        => Assert.Equal(records, TypeIdentityTest.PremiseRecords(source));

    // SPEC 13.5.6 with 15.3.6 (DECISIONS R8-M56; PLAN G1369): the written and the stored Type are read at the borrow, where a region of
    // its own body with a value is that value: `during r` names r's value p (tb_let); a set slot the local's clause equates with x.source
    // names it (OriginRedesignTest's CompletesDeclarationOriginContracts), and an omitted one is inferred (SPEC 15.4.4; x1c_inf).
    [Theory]
    [InlineData("func f(p: ref/i32, x: ref/(ref/i32 during p)) -> i32\n    let r = p\n    let y = x@follow@ref/(ref/i32 during r)\n    return 0\n")] // tb_let
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\nfunc f(x: ref/V<i32>) -> i32\n    let y = x@follow@ref/V<i32>{v}\n    return 0\n")] // x1c_inf
    public void ATypedBorrowReadsARegionAsItsValue(string source)
        => Assert.Equal(string.Empty, TypeIdentityTest.PremiseRecords(source));

    // SPEC 14.9.2 with 8.2.3 (PLAN G1240): a join's conversion source supplies its written target as the conversion forms it, so a
    // Type target or a typed borrow identical by a premise with the other source's Type gives the join one Type.
    [Theory]
    [InlineData("func f<T>(c: bool, a: i64, b: i64) -> i32\n    T is i32\n    let r = if c => a@T else => b@i32\n    return r\n")]
    [InlineData("func f<T>(c: bool) -> i32\n    T is i32\n    let v: i32 = 1\n    let w: i32 = 2\n    let r = if c => v@ref/T else => w@ref/i32\n    return r@follow\n")]
    public void AJoinedConversionSuppliesItsFormedTarget(string source)
        => Assert.Equal(string.Empty, TypeIdentityTest.PremiseRecords(source));

    [Fact]
    public void GenericFieldOriginComesFromReceiver()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\n    public func view(self: ref/Self) -> ref/T during self => self.value@ref/T");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }
}
