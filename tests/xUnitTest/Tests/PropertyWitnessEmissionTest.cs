// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class PropertyWitnessEmissionTest
{
    [Theory]
    [InlineData("Stored", "public var item: i32 = 7")]
    [InlineData("Computed", "public computed item: i32\n        get() -> i32 => 7")]
    public void RequiredGetterDispatchesThroughItsVerifiedWitness(string name, string property)
        => ScalarEmissionTest.EmitFixture("PropertyWitness" + name, "contract C\n    property item: i32 has get\nstruct S\n    Self is C\n    " + property + "\nfunc read<T>(s: ref/T) -> i32\n    T is C\n    return s.item\nlet s = S.init()\nrequire read(s@ref) == 7 else => $abort(\"witness\")", string.Empty);

    [Fact]
    public void StoredBorrowWitnessKeepsTheRequirementLoan()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessBorrow", OwnershipPropertyEmissionTest.Resource + "contract C\n    property item: ref/Resource has get\nstruct S\n    Self is C\n    public var item: Resource\n    public init() => self.item = Resource.init(1)\nfunc read<T>(s: ref/T during source) -> ref/Resource during source\n    T is C\n    return s.item\nlet s = S.init()\nrequire read(s@ref).id == 1 else => $abort(\"borrow witness\")", "1\n");

    [Theory]
    [InlineData("Standard", "")]
    [InlineData("Custom", "\n        set(value: Resource) -> () => storage = value@move")]
    public void RequiredSetterPreservesOwnershipAndDestruction(string name, string setter)
        => ScalarEmissionTest.EmitFixture("PropertyWitnessSet" + name, OwnershipPropertyEmissionTest.Resource + "contract C\n    property item: ref/Resource\n        get() -> ref/Resource\n        set(value: Resource) -> ()\nstruct S\n    Self is C\n    public var item: Resource" + setter + "\n    public init() => self.item = Resource.init(1)\nfunc replace<T>(s: uniq/T, value: Resource)\n    T is C\n    s.item = value@move\nvar s = S.init()\nreplace(s@uniq, Resource.init(2))\nrequire s.item.id == 2 else => $abort(\"setter witness\")", "1\n2\n");

    [Theory]
    [InlineData("Implicit", "get() -> ref/Resource => self.value@ref")]
    [InlineData("Explicit", "get(self: ref/Self during source) -> ref/Resource during source => self.value@ref")]
    public void ComputedBorrowWitnessTranslatesItsOwnOrigins(string name, string getter)
        => ScalarEmissionTest.EmitFixture("PropertyWitnessComputedBorrow" + name, OwnershipPropertyEmissionTest.Resource + "contract C\n    property item: ref/Resource has get\nstruct S\n    Self is C\n    var value: Resource\n    public computed item: ref/Resource\n        " + getter + "\n    public init() => self.value = Resource.init(1)\nfunc read<T>(s: ref/T during source) -> ref/Resource during source\n    T is C\n    return s.item\nlet s = S.init()\nrequire read(s@ref).id == 1 else => $abort(\"computed borrow\")", "1\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WitnessBodiesAndCallMappingsAreReused()
    {
        var source = "contract C\n    property item: i32 has get, set\nstruct S\n    Self is C\n    public var item: i32 = 7\nfunc update<T>(s: uniq/T) -> i32\n    T is C\n    s.item = 9\n    return s.item\nvar s = S.init()\n_ = update(s@uniq)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }

    [Fact]
    public void StandardWitnessPreservesNamedRequirementOrigins()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessNamedOrigin", OwnershipPropertyEmissionTest.Resource + "contract C\n    property item: ref/Resource\n        get(self: ref/Self during source) -> ref/Resource during source\nstruct S\n    Self is C\n    public var item: Resource\n    public init() => self.item = Resource.init(1)\nfunc read<T>(s: ref/T during source) -> ref/Resource during source\n    T is C\n    return s.item\nlet s = S.init()\nrequire read(s@ref).id == 1 else => $abort(\"named witness\")", "1\n");

    [Fact]
    public void ComputedOwnedWitnessTransfersTheResult()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessOwned", OwnershipPropertyEmissionTest.Resource + "contract C\n    property item: Resource has get\nstruct S\n    Self is C\n    public computed item: Resource\n        get() -> Resource => Resource.init(3)\nfunc read<T>(s: ref/T) -> Resource\n    T is C\n    return s.item\nlet s = S.init()\nlet value = read(s@ref)\nrequire value.id == 3 else => $abort(\"owned witness\")", "3\n");

    [Fact]
    public void GenericStorageWitnessInstantiatesItsAssociatedResult()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessGeneric", "struct Box<T>\n    public var value: T\n    public init(value: T) => self.value = value@move\ncontract C\n    associate E\n    property item: ref/E has get\nstruct S<T>\n    Self is C\n    associate C.E is Box<T>\n    public var item: Box<T>\n    public init(value: T) => self.item = Box<T>.init(value@move)\nfunc read<T>(s: ref/T during source) -> ref/T.E during source\n    T is C\n    return s.item\nlet s = S<i32>.init(7)\nrequire read(s@ref).value == 7 else => $abort(\"generic witness\")", string.Empty);

    [Fact]
    public void RefinementKeepsTheOriginalWitnessIdentity()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessRefinement", "contract A\n    property item: i32 has get\ncontract B: A\ncontract C: A\ncontract D: B, C\nstruct S\n    Self is D\n    public var item: i32 = 7\nfunc read<T>(s: ref/T) -> i32\n    T is D\n    return s.item\nlet s = S.init()\nrequire read(s@ref) == 7 else => $abort(\"refined witness\")", string.Empty);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequirementBorrowBlocksReceiverUpdatesOnlyWhileLive(bool live)
    {
        var source = OwnershipPropertyEmissionTest.Resource + "contract C\n    property item: ref/Resource has get\nstruct S\n    Self is C\n    public var item: Resource\n    public init() => self.item = Resource.init(1)\nfunc read<T>(s: ref/T during source) -> ref/Resource during source\n    T is C\n    return s.item\nvar s = S.init()\nlet view = read(s@ref)\n" + (live ? "s.item = Resource.init(2)\n_ = view.id" : "_ = view.id\ns.item = Resource.init(2)");
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (live)
        {
            Assert.Equal("ComparisonLoanConflict_Kd", Assert.Single(records).Code);
        }
        else
        {
            Assert.Empty(records);
        }
    }
}
