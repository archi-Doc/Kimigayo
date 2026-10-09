// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class PropertyWitnessEmissionTest
{
    private const string Conditional = "contract C\n    associate E\n    property item: E has get\nstruct S<T>\n    Self is C when T is Copy\n        associate C.E is T\n" +
        "    public var item: T\n    public init(item: T) => self.item = item@move\nfunc read<X>(x: ref/X) -> X.E\n    X is C\n    return x.item\n";

    private const string Counted = "contract Counted\n    property count: i32 has get, set\npublic open struct Base\n    public var count: i32 = 1\npublic struct Leaf : Base\n    Self is Counted\n    public init() => ()\n";

    private const string Viewed = "contract Viewed\n    property count: ref/i32\n        get(self: ref/Self) -> ref/i32\npublic open struct Base\n    public var count: i32 = 1\npublic struct Leaf : Base\n    Self is Viewed\n    public init() => ()\n" +
        "func view<T>(x: ref/T) -> ref/i32\n    T is Viewed\n    return x.count\nvar x = Leaf.init()\nlet r = view(x@ref)\n";

    // SPEC 11.4.2: an inherited standard bridge reaches the Field from the requirement receiver over the conforming Type, as a direct
    // access does, in every storage kind (it failed generation with "Concrete requirement call lacks a verified implementation mapping").
    public static TheoryData<string, string, string> InheritedBridges => new()
    {
        { "Copy", Counted + "func read<T>(x: ref/T) -> i32\n    T is Counted\n    return x.count\nlet x = Leaf.init()\nConsole.writeLine(\"\\(read(x@ref))\")", "1\n" },
        { "Set", Counted + "func write<T>(x: uniq/T) -> ()\n    T is Counted\n    x.count = 5\nvar x = Leaf.init()\nwrite(x@uniq)\nConsole.writeLine(\"\\(x.count)\")", "5\n" },
        { "Compound", Counted + "func bump<T>(x: uniq/T) -> ()\n    T is Counted\n    x.count += 1\nvar x = Leaf.init()\nbump(x@uniq)\nConsole.writeLine(\"\\(x.count)\")", "2\n" },
        { "Increment", Counted + "func bump<T>(x: uniq/T) -> ()\n    T is Counted\n    x.count++\nvar x = Leaf.init()\nbump(x@uniq)\nConsole.writeLine(\"\\(x.count)\")", "2\n" },
        { "Borrow", Viewed + "Console.writeLine(\"\\(r)\")\nx.count = 2\nConsole.writeLine(\"\\(x.count)\")", "1\n2\n" },
        { "Conformance", "contract Counted\n    property count: i32 has get, set\npublic open struct Base\n    Self is Counted\n    public var count: i32 = 1\n    public init() => ()\npublic struct Leaf : Base\n    public init() : base() => ()\nfunc read<T>(x: ref/T) -> i32\n    T is Counted\n    return x.count\nfunc write<T>(x: uniq/T) -> ()\n    T is Counted\n    x.count = 5\nvar b = Base.init()\nwrite(b@uniq)\nConsole.writeLine(\"\\(read(b@ref))\")\nvar x = Leaf.init()\nwrite(x@uniq)\nConsole.writeLine(\"\\(read(x@ref))\")", "5\n5\n" },
        { "GenericBase", "contract Holds\n    associate E\n    property item: ref/E\n        get(self: ref/Self) -> ref/E\npublic open struct Base<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\npublic open struct Middle<V> : Base<V>\n    public init(item: V) : base(item@move) => ()\npublic struct Leaf<U> : Middle<U>\n    Self is Holds\n    associate Holds.E is U\n    public init(item: U) : base(item@move) => ()\nfunc view<X>(x: ref/X) -> ref/X.E\n    X is Holds\n    return x.item\nlet x = Leaf<string>.init(\"hi\")\nConsole.writeLine(view(x@ref))", "hi\n" },
        { "Conditional", "contract C\n    associate E\n    property item: E has get\npublic open struct Base<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\npublic struct S<U> : Base<U>\n    Self is C when U is Copy\n        associate C.E is U\n    public init(item: U) : base(item@move) => ()\nfunc read<X>(x: ref/X) -> X.E\n    X is C\n    return x.item\nlet s = S<i32>.init(3)\nConsole.writeLine(\"\\(read(s@ref))\")", "3\n" },
        { "CustomGet", "contract Leveled\n    property level: i32 has get, set\npublic open struct Base\n    public var level: i32 = 1\n        get(self: ref/Self) -> i32 => storage\npublic struct Leaf : Base\n    Self is Leveled\n    public init() => ()\nfunc bump<T>(x: uniq/T) -> ()\n    T is Leveled\n    x.level += 1\nvar x = Leaf.init()\nbump(x@uniq)\nConsole.writeLine(\"\\(x.level)\")", "2\n" },
        { "OriginField", "contract Points\n    associate E\n    property r: E has get\npublic open struct Base {a}\n    public var r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\npublic struct Leaf {b}: Base during b\n    Self is Points\n    associate Points.E is ref/i32 during b\n    public init(r: ref/i32 during b) : base(r) => ()\nfunc read<T>(x: ref/T) -> T.E\n    T is Points\n    return x.r\nlet n: i32 = 4\nlet x = Leaf.init(n@ref)\nConsole.writeLine(\"\\(read(x@ref))\")", "4\n" },
        { "Resource", "public " + OwnershipPropertyEmissionTest.Resource + "contract C\n    property item: ref/Resource\n        get() -> ref/Resource\n        set(value: Resource) -> ()\npublic open struct Base\n    public var item: Resource\n    public init() => self.item = Resource.init(1)\npublic struct Leaf : Base\n    Self is C\n    public init() : base() => ()\nfunc replace<T>(s: uniq/T, value: Resource)\n    T is C\n    s.item = value@move\nvar s = Leaf.init()\nreplace(s@uniq, Resource.init(2))\nrequire s.item.id == 2 else => $abort(\"set\")", "1\n2\n" },
    };

    [Theory]
    [MemberData(nameof(InheritedBridges))]
    public void InheritedStandardBridgesExecute(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("PropertyWitnessInherited" + name, source, stdout);

    // The bridge keeps its borrowed result's Loan, and a conformance whose inherited storage cannot serve the requirement is refuted.
    [Theory]
    [InlineData(Viewed + "x.count = 2\nConsole.writeLine(\"\\(r)\")", nameof(DiagnosticCode.ComparisonLoanConflict_Kd))]
    [InlineData("contract Counted\n    property count: i32 has get, set\npublic open struct Base\n    public let count: i32 = 1\npublic struct Leaf : Base\n    Self is Counted\n    public init() => ()\n", nameof(DiagnosticCode.IncompatibleContractImplementation_Kd))]
    [InlineData("contract Counted\n    property count: i32 has get\npublic open struct Base\n    var count: i32 = 1\npublic struct Leaf : Base\n    Self is Counted\n    public init() => ()\n", nameof(DiagnosticCode.IncompatibleContractImplementation_Kd))]
    public void InheritedBridgesKeepTheirChecks(string source, string code)
        => Assert.Contains(code, DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code));

    // SPEC 11.4.2: an inherited bridge is a verified ordinary body that warm passes reuse, also when no call reaches it.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InheritedBridgeBodiesAreReused()
    {
        var c = MinimalEmissionTest.Analyze(Counted + "func bump<T>(x: uniq/T) -> i32\n    T is Counted\n    x.count += 1\n    return x.count\nvar x = Leaf.init()\n_ = bump(x@uniq)");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
        var uncalled = MinimalEmissionTest.Analyze(Counted + "Console.writeLine(\"unused\")");
        Assert.True(uncalled.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(uncalled, error));
    }

    // SPEC 11.4.2: a bridge is checked and executed under its conformance path's premises, so the Copy that `when T is Copy` grants
    // holds in its body (it was TransferRequired_Kd at `has get`).
    [Fact]
    public void AConditionalStorageCopyBridgeUsesItsConformancePremises()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessConditional", Conditional + "let s = S.init(3)\nConsole.writeLine(\"\\(read(s@ref))\")", "3\n");

    // Where the premise is refuted the conformance does not hold, so the call has no candidate and nothing is generated.
    [Fact]
    public void ARefutedConditionalConformanceIsNoCandidate()
    {
        var error = Assert.Single(DiagnosticCorpus.Check(Conditional + "let s = S.init(\"x\")\nConsole.writeLine(\"\\(read(s@ref))\")").Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        Assert.Contains("refuted for S<string>", error.Note, StringComparison.Ordinal);
    }

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
        => ScalarEmissionTest.EmitFixture("PropertyWitnessGeneric", "struct Box<T>\n    public var value: T\n    public init(value: T) => self.value = value@move\ncontract C\n    associate E\n    property item: ref/E has get\nstruct S<T>\n    Self is C\n    associate C.E is Box<T>\n    public var item: Box<T>\n    public init(value: T) => self.item = Box<T>.init(value@move)\nfunc read<T>(s: ref/T during source) -> ref/T.E during source\n    T is C\n    return s.item\nlet s = S<i32>.init(7)\nrequire read(s@ref).value == 7 else => $abort(\"generic witness\")\nlet flags = S<bool>.init(true)\nrequire read(flags@ref).value == true else => $abort(\"second witness instance\")", string.Empty);

    [Fact]
    public void RefinementKeepsTheOriginalWitnessIdentity()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessRefinement", "contract A\n    property item: i32 has get\ncontract B: A\ncontract C: A\ncontract D: B, C\nstruct S\n    Self is D\n    public var item: i32 = 7\nfunc read<T>(s: ref/T) -> i32\n    T is D\n    return s.item\nlet s = S.init()\nrequire read(s@ref) == 7 else => $abort(\"refined witness\")", string.Empty);

    [Theory]
    [InlineData("Assign", "s.item = 9")]
    [InlineData("Compound", "s.item += 2")]
    [InlineData("Increment", "s.item++\n    s.item++")]
    public void BoundContractSetterUsesItsSubstitutedInput(string name, string update)
        => ScalarEmissionTest.EmitFixture("PropertyWitnessBound" + name, "contract C<E>\n    E is Copy\n    property item: E has get, set\nstruct S\n    Self is C<i32>\n    public var item: i32 = 7\nfunc update<T>(s: uniq/T) -> i32\n    T is C<i32>\n    " + update + "\n    return s.item\nvar s = S.init()\nrequire update(s@uniq) == 9 else => $abort(\"bound setter\")", string.Empty);

    [Fact]
    public void GenericCustomSetterUsesTheSameInputSubstitution()
        => ScalarEmissionTest.EmitFixture("PropertyWitnessGenericSetter", "struct S<T>\n    T is Copy\n    public var item: T\n        set(value: T) -> () => storage = value\n    public init(value: T) => self.item = value\nvar s = S<i32>.init(7)\ns.item = 9\nrequire s.item == 9 else => $abort(\"generic setter\")", string.Empty);

    [Fact]
    public void GenericSetterCannotAssumeAnUnpublishedCopyGuarantee()
    {
        const string source = "struct S<T>\n    public var item: T\n        set(value: T) -> () => storage = value\n    public init(value: T) => self.item = value@move\n()";
        Assert.Equal("TransferRequired_Kd", Assert.Single(DiagnosticCorpus.Check(source).Diagnostics).Code);
    }

    [Fact]
    public void BoundSetterRejectsAnInputOfTheWrongType()
    {
        const string source = "contract C<E>\n    E is Copy\n    property item: E has get, set\nfunc update<T>(s: uniq/T)\n    T is C<i32>\n    s.item = true\n()";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("TypeMismatch_Kd", record.Code);
        Assert.Equal(source.IndexOf("true", StringComparison.Ordinal), record.Span!.Value.Start);
    }

    [Theory]
    [InlineData("_ = s.item@uniq", "InvalidAssignment_Kd")]
    [InlineData("s.item = 4", "InaccessibleBinding_Kd")]
    public void GetterOnlyRequirementNeverExposesImplementationStorage(string expression, string code)
    {
        var source = "contract C\n    property item: i32 has get\nstruct S\n    Self is C\n    public var item: i32 = 7\nfunc use<T>(s: uniq/T)\n    T is C\n    " + expression + "\nvar s = S.init()\nuse(s@uniq)";
        Assert.Equal(code, Assert.Single(DiagnosticCorpus.Check(source).Diagnostics).Code);
    }

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
