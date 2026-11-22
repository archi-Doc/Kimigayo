// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public partial class PropertyBindingTest
{
    private const string StoredWitnessNamesItsConformance = """
        contract C
            property b: ref/Box<Self>
                get(self: ref/Self) -> ref/Box<Self>
        struct Box<T>
            T is C
            public init() => ()
        struct S
            Self is C
            public var b: Box<S>
            public init() => self.b = Box<S>.init()
        """;

    private const string ComputedWitnessNamesItsConformance = """
        contract C
            property b: Box<Self>
                get() -> Box<Self>
        struct Box<T>
            T is C
            public init() => ()
        struct S
            Self is C
            public init() => ()
            public computed b: Box<S>
                get() -> Box<S> => Box<S>.init()
        """;

    private const string CopiedWitnessNamesItsConformance = """
        contract C
            property b: Box<Self> has get, set
        struct Box<T>
            T is C
            Self is Copy
            public init() => ()
        struct S
            Self is C
            public var b: Box<S>
            public init() => self.b = Box<S>.init()
        """;

    private const string WitnessNamesAContractItsTypeLacks = """
        contract K
        contract C
            property b: ref/Box<S>
                get(self: ref/Self) -> ref/Box<S>
        struct Box<T>
            T is K
            public init() => ()
        struct S
            Self is C
            public var b: Box<S>
            public init() => self.b = Box<S>.init()
        """;

    private const string AccessorOfAFailedProperty = """
        contract C
            property w: P
                get() -> P
        private struct P
            public init() => ()
        public struct S
            Self is C
            public init() => ()
            public computed w: P
                get() -> P => P.init()
        func need<T>(t: ref/T) -> i32
            T is C
            return 1
        func use(s: ref/S) -> i32 => need(s)
        """;

    private const string FailedSetterARequirementDoesNotNeed = """
        contract C
            property w: i32 has get
        private struct P
            public init() => ()
        public struct S
            Self is C
            public init() => ()
            public computed w: i32
                get() -> i32 => 1
                set(value: P) -> () => ()
        func need<T>(t: ref/T) -> i32
            T is C
            return 1
        func use(s: ref/S) -> i32 => need(s)
        """;

    // A custom `get` of a projection-typed Field with the standard `set` (AnUpdateReadsItsStandardSideAsTheBodyReadsTheField).
    private const string OutputGetter = """
        struct HQ<S>
            S is Addable<S>
            S.(Addable<S>).Output is Copy
            public var x: S.Output
                get() -> S.Output
                    return storage
            public init(x: S.Output) => self.x = x

        """;

    // A group Property's custom accessors (ABareGroupPropertyUpdateBesideACustomAccessorIsTheLocatedLimit).
    private const string CountGetter = "        get() -> i32\n            return storage\n";
    private const string CountSetter = "        set(value: i32) -> ()\n            storage = value\n";

    // Fields whose stored Type an instance leaves unresolved, beside a custom or the standard `set` (AnUpdateWithoutAStoredTypeAddsNoRecord).
    private const string RejectedField = "struct HS<S>\n    S is Addable<S>\n    public var x: S.Output\n";
    private const string RejectedRest = "    public init(x: S.Output) => self.x = x@move\nfunc f(h: uniq/HS<bool>) -> ()\n";
    private const string RejectedHolder = RejectedField + "        set(value: S.Output) -> ()\n            storage = value@move\n" + RejectedRest;
    private const string RejectedStandard = RejectedField + RejectedRest;
    private const string UnresolvedField = "struct HU<S>\n    public var x: Missing<S>\n";
    private const string UnresolvedRest = "func f(h: uniq/HU<i32>) -> ()\n";
    private const string UnresolvedHolder = UnresolvedField + "        set(value: Missing<S>) -> ()\n            storage = value@move\n" + UnresolvedRest;
    private const string UnresolvedStandard = UnresolvedField + UnresolvedRest;

    // An `x: i32` Field beside a custom accessor of another Type, and its twin (AUseOfAWronglyTypedAccessorIsJudgedAgainstItsPropertysType).
    private const string ClosedField = "struct HW\n    public var x: i32\n";
    private const string ClosedInit = "    public init(x: i32) => self.x = x\n";
    private const string WrongGetter = ClosedField + "        get() -> i64\n            return 1\n" + ClosedInit;
    private const string GenericWrongGetter = "struct HW<S>\n    S is Copy\n    public var x: S\n        get() -> i64\n            return 1\n" +
        "    public init(x: S) => self.x = x\n";

    private const string WrongSetter = ClosedField + "        set(value: i64) -> ()\n            storage = 1\n" + ClosedInit;
    private const string RightGetter = ClosedField + "        get() -> i32\n            return storage\n" + ClosedInit;

    // A computed `get` and a Property requirement's `get` of another Type than the Property's.
    private const string WrongComputedGetter = "struct HG\n    public computed level: i32\n        get() -> i64 => 1\n";
    private const string WrongRequirement = "contract HasX\n    property x: i32\n        get() -> i64\npublic func read<T>(t: T) -> i32\n    T is HasX\n    return t.x\n" +
        "public func readb<T>(t: T) -> bool\n    T is HasX\n    return t.x\n";

    // Accessors of another Type whose receiver Type is missing, or with an access or receiver-shape error of their own, and one inside a
    // failed declaration context (PLAN G322).
    private const string AccessWrongGetter = ClosedField + "        public get() -> i64\n            return 1\n" + ClosedInit;
    private const string ShapeWrongGetter = ClosedField + "        get(self: uniq/Self) -> i64\n            return 1\n" + ClosedInit;
    private const string UnformedPart = "struct NC\n    public var a: i32\n    drop => ()\nstruct W<T>\n    T is Copy\n    public var t: T\n";
    private const string UnformedGetter = UnformedPart + ClosedField + "        get() -> W<NC>\n            return storage\n" + ClosedInit;
    private const string UnformedSetter = UnformedPart + ClosedField + "        set(value: W<NC>) -> ()\n            storage = 1\n" + ClosedInit;
    private const string MissingReceiverGetter = ClosedField + "        get(self: ref/Missing) -> i64\n            return 1\n" + ClosedInit;
    private const string MissingReceiverSetter = ClosedField + "        set(self: uniq/Missing, value: i64) -> ()\n            storage = 1\n" + ClosedInit;
    private const string FailedContext = "struct HW<S>\n    S is Missing\n    public var x: i32\n        get() -> i64\n            return 1\n    public init(x: i32) => self.x = x\n" +
        "    public func bump(self: uniq/Self) -> ()\n";

    // Dev's records beside those accessors, which a formation failure keeps (AUseOfAWronglyTypedAccessorIsJudgedAgainstItsPropertysType).
    private const string UnformedGetterRecords = "UnsatisfiedConstraint_Kd public var x: i32;UnsatisfiedConstraint_Kd get() -> W<NC>;UnsatisfiedConstraint_Kd W<NC>;" +
        "UnsatisfiedConstraint_Kd W<NC>;TypeMismatch_Kd return storage;ArithmeticSelection_Kd h.x += 4;UnsatisfiedConstraint_Kd h.x;TypeMismatch_Kd let v: i32 = h.x;" +
        "UnsatisfiedConstraint_Kd h.x";

    private const string UnformedSetterRecords = "UnsatisfiedConstraint_Kd public var x: i32;UnsatisfiedConstraint_Kd W<NC>;UnsatisfiedConstraint_Kd W<NC>;" +
        "UnsatisfiedConstraint_Kd h.x;TypeMismatch_Kd 5;TypeMismatch_Kd h.x += 4";

    // A group Property beside a custom `get` of another Type, and a bare update inside its group.
    private const string WrongCountGetter = "group Config\n    public var count: i32 = 0\n        get() -> i64\n            return 1\n";
    private const string BareCountUpdate = WrongCountGetter + "    public func bump() -> ()\n        count += 1\n        return\n";

    // A custom `get` of an Addable Type beside the standard `set`, so `+=` takes the user arithmetic path (TryArithmetic).
    private const string Counter = "struct Counter\n    Self is Addable<Counter>\n    associate Output is Counter\n    public let value: i32\n" +
        "    public init(value: i32) => self.value = value\n" +
        "    public func added(self: ref/Self, right: ref/Counter) -> Counter => Counter.init(self.value + right.value)\n    drop => ()\n";

    private const string AddableWrongGetter = Counter + ClosedField + "        get() -> Counter\n            return Counter.init(1)\n" + ClosedInit;

    // Computed Properties whose `set` takes another Type than the `get` result (AnUpdateFitsItsResultToItsPublishedDestination).
    private const string ComputedLevel = "struct HG\n    public computed level: i32\n        get() -> i32 => 1\n        set(value: i64) -> () => ()\n";
    private const string ComputedCounter = Counter + "struct HC\n    public computed c: Counter\n        get() -> Counter => Counter.init(1)\n        set(value: i64) -> () => ()\n";

    // The owner's O-44, SPEC 11.9.1: a Property requirement forms no signature either, and one read keeps a failed declaration out of the evidence
    // (SPEC 8.3.1). DECISIONS Q13: c3b's accessor stops at its failed Property, as one that is no witness does; a setter `has get` does not need is
    // no evidence of the getter.
    [Theory]
    [InlineData(StoredWitnessNamesItsConformance, "clean")]
    [InlineData(ComputedWitnessNamesItsConformance, "clean")]
    [InlineData(CopiedWitnessNamesItsConformance, "1*IncompatibleContractImplementation_Kd 6*InvalidConstraint_Kd 2*UninitializedPlace_Kd 1*UnprovenConstraint_Kd")]
    [InlineData(WitnessNamesAContractItsTypeLacks, "1*IncompatibleContractImplementation_Kd 1*NoApplicableOverload_Kd 2*UninitializedPlace_Kd 9*UnsatisfiedConstraint_Kd")]
    [InlineData(AccessorOfAFailedProperty, "1*InaccessibleBinding_Kd 1*IncompatibleContractImplementation_Kd")] // R7-C3 C3-1b (F's C2 rulings 284 (3), 294; DECISIONS Q13, dependent): the user of the failed witness rests
    [InlineData(FailedSetterARequirementDoesNotNeed, "1*InaccessibleBinding_Kd")]
    public void APropertyWitnessFormsNoSignature(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    [Theory]
    [InlineData("let", false)]
    [InlineData("var", true)]
    public void RetainsImplicitStandardPermissionsWithoutCreatingAccessors(string kind, bool setter)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct S<T>\n    public {kind} item: T");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "S", "item");
        Assert.True(property.IsVerified);
        Assert.True(property.Getter.IsStandard);
        Assert.Null(property.Getter.Receiver);
        Assert.Equal(setter, property.Setter.IsPresent);
        Assert.Empty(property.Declaration.Accessors);
        Assert.Same(property.Type, property.Getter.Result);
    }

    [Theory]
    [InlineData("get(self: ref/Self) -> i32 => storage", true)]
    [InlineData("get(self: ref/Self) -> i64 => 0", false)]
    [InlineData("get(self: ref/Self) -> i32 => true", false)]
    [InlineData("set(self: uniq/Self, value: i32) -> () => storage = value", true)]
    [InlineData("set(self: uniq/Self, value: i64) -> () => ()", false)]
    public void ValidatesStoredSignaturesAndBodyTypes(string accessor, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct S\n    public var item: i32\n        {accessor}");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ContextualStorageNamesTheSlotAndValueNamesTheInput()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    var item: i32\n        set(self: uniq/Self, value: i32) -> () => storage = value");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "S", "item");
        var names = KotoTree.Walk(property.Setter.Declaration!.Body!).OfType<IdentifierNameKoto>().ToArray();
        var storage = Assert.Single(names, x => x.IdentifierName == "storage");
        var value = Assert.Single(names, x => x.IdentifierName == "value");
        Assert.Equal(BindingSymbolKind.Storage, storage.SymbolOf()!.Kind);
        Assert.Same(property.Declaration, storage.SymbolOf()!.Declaration);
        Assert.Equal(BindingSymbolKind.Parameter, value.SymbolOf()!.Kind);
        Assert.NotSame(storage.SymbolOf(), value.SymbolOf());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("    T is Copy\n", true)]
    public void CustomGetterRequiresDefinitionSideCopyEvidence(string constraint, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct S<T>\n{constraint}    var item: T\n        get(self: ref/Self) -> T => storage");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void CustomSetterDoesNotRequireCopy()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S<T>\n    var item: T\n        set(self: uniq/Self, value: T) -> () => storage = value@move");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData(" during source", " during source", true)]
    [InlineData("", " during source", true)]
    [InlineData(" during source", "", true)]
    [InlineData("", "", true)]
    [InlineData(" during static", "", false)]
    [InlineData("", " during static", false)]
    public void StoredAccessorOriginsMustMatchStorage(string getter, string setter, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct S {{source}}\n    var item: ref/i32 during source\n        get(self: ref/Self) -> ref/i32{getter} => storage\n        set(self: uniq/Self, value: ref/i32{setter}) -> () => storage = value");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ReceiverOriginPathsRetainTheAccessorInputIdentity()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S {source}\n    var item: ref/i32 during source\n        get(self: ref/Self) -> ref/i32 during self.source => storage");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var getter = Property(c, "S", "item").Getter;
        var self = KotoTree.Walk(getter.Declaration!.ReturnType!).OfType<IdentifierNameKoto>().Single(x => x.IdentifierName == "self");
        Assert.Equal(BindingSymbolKind.Parameter, self.SymbolOf()!.Kind);
        Assert.Same(getter.Declaration, self.SymbolOf()!.Declaration);
        Assert.Same(Property(c, "S", "item").Type, getter.Result);
    }

    [Fact]
    public void StaticAccessorsHaveNoReceiverAndKeepTheirOwnStorageBinding()
    {
        var c = CompilationTestHelper.ParseSuccess("group G\n    var item: i32 = 0\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "G", "item");
        Assert.Null(property.Getter.Receiver);
        Assert.Null(property.Setter.Receiver);
        Assert.Equal("i32", property.Setter.Input!.Name);
    }

    [Fact]
    public void ComputedGetterAndSetterHaveIndependentOriginCompletion()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    computed item: ref/i32\n        get(self: ref/Self) -> ref/i32 => missing\n        set(self: uniq/Self, value: ref/i32) -> () => ()");
        c.Bind();
        var property = Property(c, "S", "item");
        Assert.Same(property.Getter.Receiver!.Origin, property.Getter.Result!.Origin);
        Assert.Same(property.Type, property.Getter.Result);
        Assert.NotSame(property.Setter.Receiver!.Origin, property.Setter.Input!.Origin);
        Assert.Same(property.Setter.Declaration, property.Setter.Input.Origin!.Binder);
        Assert.Equal(1, property.Setter.Input.Origin.Slot);
    }

    [Fact]
    public void ShorthandRequirementCompletesEachOperationIndependently()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: ref/i32 has get, set");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "C", "item");
        Assert.Same(property.Getter.Receiver!.Origin, property.Type!.Origin);
        Assert.Equal(1, property.Setter.Input!.Origin!.Slot);
        Assert.Same(property.Setter.Declaration, property.Setter.Input.Origin.Binder);
        Assert.NotSame(property.Type, property.Setter.Input);
    }

    [Fact]
    public void ImplicitSetterInputRestoresTheSharedHeaderSyntax()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: ref/i32 has get, set");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "C", "item");
        var header = property.Declaration.TypeKoto!;
        Assert.Equal(BindingState.Resolved, header.StateOf());
        Assert.Same(property.Type, header.TypeOf());
    }

    [Theory]
    [InlineData("public", "private", true)]
    [InlineData("public", "public", false)]
    [InlineData("private", "private", false)]
    [InlineData("internal", "protected", false)]
    [InlineData("protected internal", "internal", true)]
    public void AccessorRestrictionMustBeStrictlyNarrower(string propertyAccess, string accessorAccess, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct S\n    {propertyAccess} var item: i32\n        {accessorAccess} get");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("get", "public var item: i32", true)]
    [InlineData("get, set", "public var item: i32", true)]
    [InlineData("get, set", "public let item: i32", false)]
    [InlineData("get", "public var item: i64", false)]
    [InlineData("get", "public var item: i32\n        private set", true)]
    [InlineData("get, set", "public var item: i32\n        private set", false)]
    [InlineData("get", "public var item: i32\n        private get", false)]
    public void StandardWitnessesCheckOnlyRequiredOperationAccess(string required, string member, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"public contract C\n    property item: i32 has {required}\npublic struct S\n    Self is C\n    {member}");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            var mapping = Conformance(c, "S", "C");
            Assert.Equal(PropertyWitnessKind.StorageCopy, mapping.GetPropertyWitness(new(Property(c, "C", "item").Symbol, mapping.Contract), PropertyAccessorKind.Get)!.Value.Kind);
        }
    }

    [Fact]
    public void RetainsSeparateGetAndSetWitnesses()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: i32 has get, set\nstruct S\n    Self is C\n    public var item: i32");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var mapping = Conformance(c, "S", "C");
        Assert.Equal(2, mapping.PropertyWitnesses.Count);
        Assert.Equal(PropertyWitnessKind.StorageCopy, mapping.PropertyWitnesses[0].Kind);
        Assert.Equal(PropertyWitnessKind.StorageSet, mapping.PropertyWitnesses[1].Kind);
        Assert.Same(mapping.PropertyWitnesses[0].Implementation.Property, mapping.PropertyWitnesses[1].Implementation.Property);
    }

    [Theory]
    [InlineData("E", "", false)]
    [InlineData("E", "    T is Copy\n", true)]
    [InlineData("ref/E", "", true)]
    public void GenericBridgeRequiresOnlyItsSpecifiedEvidence(string result, string constraint, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct Box<T>\n    Self is Copy when T is Copy\n    var value: T\ncontract C\n    associate E\n    property item: {result} has get\nstruct S<T>\n{constraint}    Self is C\n    associate C.E is Box<T>\n    public var item: Box<T>");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    // SPEC 8.6.1, 11.9.1, 11.9.2 (PLAN G278): a projection-typed Field, own or a base's, is a by-value witness only where its Type is
    // proven Copy, as a parameter-typed one: `W<S>`'s unconditional `Self is Holds` storing `S.Element` holds for every S, so its Copy
    // bridge is unproven (UnprovenConstraint_Kd), while the conformance conditional on `S.Element is Copy` and a borrowing getter are valid.
    [Theory]
    [InlineData("E", "", false)]
    [InlineData("E", " when S.Element is Copy", true)]
    [InlineData("ref/E", "", true)]
    [InlineData("E", " when S.Element is Copy", true, true)]
    [InlineData("E", "", false, true)]
    public void AProjectionTypedFieldIsAWitnessUnderItsCopyEvidence(string result, string condition, bool valid, bool inherited = false)
    {
        var associate = condition.Length == 0 ? "\n    associate Holds.E is S.Element" : "\n        associate Holds.E is S.Element";
        var c = CompilationTestHelper.ParseSuccess(
            $"contract HasElem\n    associate Element\ncontract Holds\n    associate E\n    property item: {result} has get\nopen struct Base<T>\n" +
            $"    public var item: T\nstruct W<S>{(inherited ? " : Base<S.Element>" : string.Empty)}\n    S is HasElem\n" +
            $"    Self is Holds{condition}{associate}\n" + (inherited ? string.Empty : "    public var item: S.Element\n"));
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        Assert.True(valid || c.Binding.Issues.Any(static x => x.Code == DiagnosticCode.UnprovenConstraint_Kd), Describe(c));
    }

    [Theory]
    [InlineData("Resource", false)]
    [InlineData("ref/Resource", true)]
    public void NonCopyStorageCanImplementOnlyTheBorrowGetter(string result, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct Resource\ncontract C\n    property item: {result} has get\nstruct S\n    Self is C\n    public var item: Resource");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            var operation = Assert.Single(Conformance(c, "S", "C").PropertyWitnesses);
            Assert.Equal(PropertyWitnessKind.StorageBorrow, operation.Kind);
            Assert.Same(operation.ReceiverType.Origin, operation.ResultType.Origin);
        }
    }

    [Theory]
    [InlineData("get(self: ref/Self) -> i32 => 1", true)]
    [InlineData("get(self: Self) -> i32 => 1", false)]
    public void ComputedWitnessRetainsTheExplicitReceiver(string getter, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    property item: i32 has get\nstruct S\n    Self is C\n    public computed item: i32\n        {getter}");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            Assert.Equal(PropertyWitnessKind.AccessorCall, Assert.Single(Conformance(c, "S", "C").PropertyWitnesses).Kind);
        }
    }

    [Fact]
    public void ExplicitComputedSetterMayTakeADifferentType()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: i32\n        get(self: ref/Self) -> i32\n        set(self: uniq/Self, value: bool) -> ()\nstruct S\n    Self is C\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 1\n        set(self: uniq/Self, value: bool) -> () => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal("bool", Conformance(c, "S", "C").PropertyWitnesses[1].InputType!.Name);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("private", true)]
    public void SetterSignatureAccessibilityUsesItsOwnDomain(string access, bool valid)
    {
        var modifier = access == "public" ? string.Empty : access + " ";
        var c = CompilationTestHelper.ParseSuccess($"struct Hidden\npublic struct S\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 1\n        {modifier}set(self: uniq/Self, value: Hidden) -> () => ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("value.item = 1")]
    [InlineData("value.item += 1")]
    [InlineData("value.item++")]
    [InlineData("++value.item")]
    public void StandardWritesAndUpdatesRequireTheSetterPermission(string expression)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct S\n    public var item: i32\n        private set\nfunc write(value: uniq/S)\n    {expression}");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void SimpleWriteDoesNotRequireTheGetterPermission()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    public var item: i32\n        private get\nfunc write(value: uniq/S)\n    value.item = 1");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    // SPEC 13.7.1, 13.7.2, 8.2.3 (PLAN G292, DECISIONS BU-50): a compound assignment, `++` or `--` with one custom accessor computes in
    // the read Type; its standard `get` reads and its standard `set` writes the Field's Place as the body reads it (MemberRead), as a
    // plain `h.x` does: `x: S` of `HY<i32>`, closed or under `T is PrimitiveInteger`, `x: S.Output` likewise, and a custom `get` with
    // the standard `set`, also of `S.Output` (before, TypeMismatch_Kd "expected S" or "expected S.Output"). Twins: both accessors
    // custom, a non-generic Field of a generic struct, a bool operand, which stays TypeMismatch_Kd, and `++` of a bool Field, which
    // gets its own NonNumericOperand_Kd (before, TypeMismatch_Kd "expected S").
    [Theory]
    [InlineData("", "var h = HY<i32>.init(x: 3)\n    h.x += 4", "")]
    [InlineData("", "var h = HY<i32>.init(x: 3)\n    h.x |= 4\n    h.x++\n    ++h.x", "")]
    [InlineData("func f<T>(h: uniq/HY<T>, a: T) -> T\n    T is PrimitiveInteger\n    h.x += a\n    return h.x\n", "var h = HY<i64>.init(x: 3)\n    let v = f(h@uniq, 4)", "")]
    [InlineData("", "var h = HS<i32>.init(x: 3)\n    h.x += 4\n    --h.x", "")]
    [InlineData("func f<T>(h: uniq/HS<T>, a: T) -> T\n    T is PrimitiveInteger\n    h.x *= a\n    h.x++\n    return h.x\n", "var h = HS<u8>.init(x: 3)\n    let v = f(h@uniq, 4)", "")]
    [InlineData("", "var h = HG<i32>.init(x: 3)\n    h.x |= 4\n    h.x++", "")]
    [InlineData("func f<T>(h: uniq/HG<T>, a: T) -> T\n    T is PrimitiveInteger\n    h.x += a\n    return h.x\n", "var h = HG<i32>.init(x: 3)\n    let v = f(h@uniq, 4)", "")]
    [InlineData(OutputGetter + "func f<T>(h: uniq/HQ<T>, a: T) -> T\n    T is PrimitiveInteger\n    h.x += a\n    h.x |= a\n    --h.x\n    return h.x\n", "var h = HQ<i32>.init(x: 3)\n    let v = f(h@uniq, 4)\n    h.x++", "")]
    [InlineData("", "var h = HB<i32>.init(x: 3)\n    h.x += 4\n    h.x |= 4\n    h.x++", "")]
    [InlineData("", "var h = HN<bool>.init(x: 3, s: true)\n    h.x += 4\n    h.x |= 4", "")]
    [InlineData("", "var h = HY<i32>.init(x: 3)\n    h.x += true", "TypeMismatch_Kd h.x += true")]
    [InlineData("func f<T>(h: uniq/HS<T>, a: T) -> ()\n    T is PrimitiveInteger\n    h.x |= true\n    return\n", "var h = HS<i32>.init(x: 3)\n    f(h@uniq, 4)", "TypeMismatch_Kd h.x |= true")]
    [InlineData("", "var h = HG<i32>.init(x: 3)\n    h.x += true", "TypeMismatch_Kd h.x += true")]
    [InlineData("", "var h = HY<bool>.init(x: true)\n    h.x++", "NonNumericOperand_Kd h.x++")]
    public void AnUpdateReadsItsStandardSideAsTheBodyReadsTheField(string declarations, string use, string record)
    {
        const string Holders = """
            struct HY<S>
                public var x: S
                    set(value: S) -> ()
                        storage = value@move
                public init(x: S) => self.x = x@move
            struct HS<S>
                S is Addable<S>
                public var x: S.Output
                    set(value: S.Output) -> ()
                        storage = value@move
                public init(x: S.Output) => self.x = x@move
            struct HG<S>
                S is Copy
                public var x: S
                    get() -> S
                        return storage
                public init(x: S) => self.x = x
            struct HB<S>
                S is Copy
                public var x: S
                    get() -> S
                        return storage
                    set(value: S) -> ()
                        storage = value
                public init(x: S) => self.x = x
            struct HN<S>
                public var x: i32
                    set(value: i32) -> ()
                        storage = value
                public var s: S
                public init(x: i32, s: S)
                    self.x = x
                    self.s = s@move

            """;
        var c = MinimalEmissionTest.Analyze(Holders + declarations + $"public func main() -> ()\n    {use}\n    return\n");
        Assert.Equal(record.Length == 0 ? [] : [record], c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}".Split('\n')[0]));
        Assert.Empty(c.Problems);
    }

    // SPEC 13.7.1 (PLAN G296): each update fits its result to the destination Type BindPropertyUpdate publishes, so beside a computed
    // `set` of another input Type, `+=`, `|=`, `++` and a user arithmetic `+=` (TryArithmetic) are each a labelled TypeMismatch_Kd at the
    // update. Twin: a custom `get` of the Field's Type, which is valid. Updates beside a wrongly typed custom accessor are judged against
    // the Property's Type (AUseOfAWronglyTypedAccessorIsJudgedAgainstItsPropertysType).
    [Theory]
    [InlineData(ComputedLevel, "var h = HG.init()\n    h.level += 4", "TypeMismatch_Kd h.level += 4")]
    [InlineData(ComputedLevel, "var h = HG.init()\n    h.level |= 4", "TypeMismatch_Kd h.level |= 4")]
    [InlineData(ComputedLevel, "var h = HG.init()\n    h.level++", "TypeMismatch_Kd h.level++")]
    [InlineData(ComputedCounter, "var h = HC.init()\n    h.c += Counter.init(2)", "TypeMismatch_Kd h.c += Counter.init(2)")]
    [InlineData(RightGetter, "var h = HW.init(x: 3)\n    h.x += 4\n    h.x |= 4\n    h.x++", "")]
    public void AnUpdateFitsItsResultToItsPublishedDestination(string holder, string use, string records)
    {
        var c = MinimalEmissionTest.Analyze(holder + $"public func main() -> ()\n    {use}\n    return\n");
        Assert.Equal(records.Length == 0 ? [] : records.Split(';'), c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}".Split('\n')[0]));
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
    }

    // SPEC 11.3, 11.4, 23.1.2 (PLAN G296): a use of an accessor whose `get` result or stored `set` input is not its Property's Type takes
    // the Property's Type, which those rules fix, and after validation rests on the declaration's TypeMismatch_Kd. A consumer that accepts
    // that Type adds no record (updates, a generic Field, a qualified group update, reads and writes, a computed `get`, a requirement);
    // one that rejects it keeps its own record (dev's read the accessor's Type), as do a user arithmetic `+=`, G295's bare group limit, a
    // missing or string operand, and a use through the standard accessor beside a failed one. An accessor resting on its missing receiver
    // Type rests its uses too, which record no InternalInvariant_Kd; one with an access or shape error keeps that record, its mismatch
    // unreported (one failure per node). A `get` result or `set` input whose formation fails is no mismatch, so dev's records stay. The
    // valid twin: a `get` of the Field's Type.
    [Theory]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x += 4", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x |= 4", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x++", "TypeMismatch_Kd get() -> i64")]
    [InlineData(GenericWrongGetter, "var h = HW<i32>.init(x: 3)\n    h.x += 4", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    h.x |= 4", "TypeMismatch_Kd set(value: i64) -> ()")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    --h.x", "TypeMismatch_Kd set(value: i64) -> ()")]
    [InlineData(WrongCountGetter, "Config.count += 1", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let v: i32 = h.x", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    let n: i32 = 4\n    h.x = n", "TypeMismatch_Kd set(value: i64) -> ()")]
    [InlineData(WrongComputedGetter, "var h = HG.init()\n    let w: i32 = h.level", "TypeMismatch_Kd get() -> i64 => 1")]
    [InlineData(WrongRequirement, "let u: i32 = 0", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd return t.x")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let v: bool = h.x", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd let v: bool = h.x")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    h.x = true", "TypeMismatch_Kd set(value: i64) -> ();TypeMismatch_Kd h.x = true")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x += 1.5", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd 1.5")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let b: bool = true\n    h.x += b", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd h.x += b")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let s: string = h.x", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd let s: string = h.x")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let n = h.x.nope", "TypeMismatch_Kd get() -> i64;UnresolvedBinding_Kd h.x.nope")]
    [InlineData(WrongGetter + "public func takesBool(b: bool) -> () => ()\n", "var h = HW.init(x: 3)\n    takesBool(h.x)", "TypeMismatch_Kd get() -> i64;NoApplicableOverload_Kd takesBool(h.x)")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    h.x = missing", "TypeMismatch_Kd set(value: i64) -> ();UnresolvedBinding_Kd missing")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    h.x = \"a\"", "TypeMismatch_Kd set(value: i64) -> ();TypeMismatch_Kd h.x = \"a\"")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let v: i64 = h.x", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd let v: i64 = h.x")]
    [InlineData(WrongComputedGetter, "var h = HG.init()\n    let v: bool = h.level", "TypeMismatch_Kd get() -> i64 => 1;TypeMismatch_Kd let v: bool = h.level")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let v: bool = h.x + 1", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd let v: bool = h.x + 1")]
    [InlineData(AddableWrongGetter, "var h = HW.init(x: 3)\n    h.x += Counter.init(2)", "TypeMismatch_Kd get() -> Counter;ArithmeticSelection_Kd h.x += Counter.init(2)")]
    [InlineData(BareCountUpdate, "Config.bump()", "TypeMismatch_Kd get() -> i64;Unsupported_Kd count")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x += missing", "TypeMismatch_Kd get() -> i64;UnresolvedBinding_Kd missing")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x += \"a\"", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd h.x += \"a\"")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    let v: bool = h.x", "TypeMismatch_Kd set(value: i64) -> ();TypeMismatch_Kd let v: bool = h.x")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x = true", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd h.x = true")]
    [InlineData(MissingReceiverGetter, "var h = HW.init(x: 3)\n    let w: i32 = h.x\n    h.x += 1", "UnresolvedBinding_Kd Missing")]
    [InlineData(MissingReceiverGetter, "var h = HW.init(x: 3)\n    let v: bool = h.x", "UnresolvedBinding_Kd Missing;TypeMismatch_Kd let v: bool = h.x")]
    [InlineData(MissingReceiverSetter, "var h = HW.init(x: 3)\n    let n: i32 = 3\n    h.x = n\n    h.x += 1", "UnresolvedBinding_Kd Missing")]
    [InlineData(AccessWrongGetter, "var h = HW.init(x: 3)\n    let w: i32 = h.x\n    h.x += 1\n    let v: bool = h.x", "InaccessibleBinding_Kd public get() -> i64;TypeMismatch_Kd let v: bool = h.x")]
    [InlineData(ShapeWrongGetter, "var h = HW.init(x: 3)\n    let w: i32 = h.x\n    h.x += 1\n    let v: bool = h.x", "AccessorReceiverShape_Kd get(self: uniq/Self) -> i64;TypeMismatch_Kd let v: bool = h.x")]
    [InlineData(UnformedGetter, "var h = HW.init(x: 3)\n    h.x += 4\n    let v: i32 = h.x", UnformedGetterRecords)]
    [InlineData(UnformedSetter, "var h = HW.init(x: 3)\n    h.x = 5\n    h.x += 4", UnformedSetterRecords)]
    [InlineData(RightGetter, "var h = HW.init(x: 3)\n    h.x += 4\n    h.x |= 4\n    h.x++\n    let v: i32 = h.x", "")]
    public void AUseOfAWronglyTypedAccessorIsJudgedAgainstItsPropertysType(string holder, string use, string records)
    {
        var c = MinimalEmissionTest.Analyze(holder + $"public func main() -> ()\n    {use}\n    return\n");
        c.Binding.ReportDiagnostics();
        Assert.Equal(records.Length == 0 ? [] : records.Split(';'), c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}".Split('\n')[0]));
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    // SPEC 11.3, 23.1.2 (PLAN G322): ValidateAccessor judges an accessor's Type before its declaration context, so inside a failed
    // context the accessor reports its own TypeMismatch_Kd; a use that accepts the Property's Type adds none, one that rejects it keeps
    // its own (before, only the latter).
    [Theory]
    [InlineData("        self.x += 4\n        let v: i32 = self.x\n", "TypeMismatch_Kd get() -> i64")]
    [InlineData("        let v: bool = self.x\n", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd let v: bool = self.x")]
    public void AnAccessorOfAnotherTypeInAFailedContextReportsItsOwnMismatch(string body, string records)
    {
        var c = MinimalEmissionTest.Analyze(FailedContext + body + "        return\npublic func main() -> ()\n    return\n");
        Assert.Equal(records.Split(';'), c.Binding.Issues.Where(static x => x.Code == DiagnosticCode.TypeMismatch_Kd).Select(static x => $"{x.Code} {x.Node}".Split('\n')[0]));
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
    }

    // A warm rebind of uses of a wrongly typed accessor, which rest on its declaration after validation, reuses its storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void UsesOfAWronglyTypedAccessorRebindWithoutAllocating()
    {
        var c = MinimalEmissionTest.Analyze(WrongGetter + "public func main() -> ()\n    var h = HW.init(x: 3)\n    h.x += 4\n    h.x++\n    let v: bool = h.x\n    return\n");
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }

    // SPEC 23.1.2 (PLAN G296): an update beside a custom `set` whose standard side has no Type (a rejected instance, an unresolved Field
    // Type) completes unresolved, a dependent of the reported failure that adds no record (before, a direct TypeMismatch_Kd at `h.x`).
    // Twins: the standard accessors' update, which added none before either. The custom `set` is a call over the receiver (SPEC 11.3,
    // PLAN G214), so over the rejected instance HS<bool> it reports that Type's constraint at the write, as a method call `h.put(1)`
    // reports it at its call (`added`; before, no record).
    [Theory]
    [InlineData(RejectedHolder, "h.x += 1", "UnsatisfiedConstraint_Kd")]
    [InlineData(RejectedHolder, "h.x |= 1", "UnsatisfiedConstraint_Kd")]
    [InlineData(RejectedHolder, "h.x++", "UnsatisfiedConstraint_Kd")]
    [InlineData(UnresolvedHolder, "h.x += 1", "")]
    [InlineData(RejectedStandard, "h.x += 1", "")]
    [InlineData(UnresolvedStandard, "h.x += 1", "")]
    public void AnUpdateWithoutAStoredTypeAddsNoRecord(string holder, string update, string added)
    {
        const string Main = "    return\npublic func main() -> ()\n    return\n";
        Assert.Equal(Errors(holder + Main).Concat(added.Length == 0 ? [] : added.Split(' ')), Errors(holder + $"    {update}\n" + Main));

        static string[] Errors(string source)
        {
            var c = MinimalEmissionTest.Analyze(source);
            c.Binding.ReportDiagnostics();
            return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => x.Code)];
        }
    }

    // Two Fields whose custom getters return a non-Copy stored value: each declaration's one UnsatisfiedConstraint_Kd (SPEC 11.1).
    private const string FailedGetter = "struct Q\n    public var x: i32 = 0\n    public init() => ()\nstruct P\n    public var x: i32 = 0\n    public var inner: Q = Q.init()\n" +
        "    public init() => ()\nstruct W\n    public var pos: P = P.init()\n        get() -> P => storage\n    public var arr: Array<i32> = []\n" +
        "        get() -> Array<i32> => storage\n    public init() => ()\nfunc take(v: i32) -> i32 => v\npublic func main() => ()\n";

    // SPEC 23.1.2 (D-823): a use of a getter whose declaration validation fails rests on it after its body was bound, and so does every
    // Place built on that use, which was bound against the use's Type: the program's records are the declarations' and, each getter's
    // body published whatever its definition's failure (PLAN G1071 gate 1, SPEC 23.1.2), its `storage` read's TransferRequired_Kd, an
    // independent record (`storage`'s Type resolved; F's C2 ruling 286 (i), Q13). Before D-823 the
    // Place stayed Resolved over the untyped use, and MIR met a Field rooted in an Error (InternalInvariant_Kd; a nested Place: a located
    // Unsupported_Kd). The same inside a match Subject that is a call, an arm or a `for` body (no construct takes the Place as its
    // Subject). Twins: the use itself, and a group, which failed nothing before.
    [Theory]
    [InlineData("func run(w: uniq/W) -> i32\n    return w.pos.x\n")]
    [InlineData("func run(w: uniq/W) -> i32\n    return w.arr[0]\n")]
    [InlineData("func run(w: uniq/W) -> i32\n    return w.pos.inner.x\n")]
    [InlineData("func run(w: uniq/W) -> i32\n    return take(w.pos.x)\n")]
    [InlineData("func run(w: uniq/W)\n    let h = func [w] (c: bool) -> i32\n        return w.pos.x\n    let k = h(true)\n")]
    [InlineData("func run(w: uniq/W)\n    let p = w.pos\n")]
    [InlineData("func run(w: uniq/W) -> i32\n    return (w.pos).x\n")]
    [InlineData("func pick(v: i32) -> Option<i32> => .Some(v)\nfunc run(w: uniq/W) -> i32\n    return match pick(w.pos.x)\n        .Some(let n) => n\n        .None => 0\n")]
    [InlineData("func run(w: uniq/W, k: Option<i32>) -> i32\n    return match k\n        .Some(_) => w.pos.x\n        .None => 0\n")]
    [InlineData("func run(w: uniq/W, items: ref/Array<i32>) -> i32\n    var total = 0\n    for v in items\n        total += w.pos.x\n    return total\n")]
    public void APlaceBuiltOnAUseOfAFailedGetterRestsOnIt(string run)
        => Assert.Equal(
            [nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), nameof(DiagnosticCode.TransferRequired_Kd), nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), nameof(DiagnosticCode.TransferRequired_Kd)],
            DiagnosticCorpus.Check(FailedGetter + run).Diagnostics.Select(static x => x.Code));

    // A struct P, non-Copy unless the Copy twin opts in; an owned R for an unrelated local; W's custom getters returning their stored
    // Option<P>, P, Tuple and fixed array.
    private const string NonCopyP = "struct P\n    public var x: i32 = 0\n    public var o: Option<i32> = .None\n    public var items: Array<i32> = []\n    public init() => ()\n";
    private const string OwnedR = "struct R\n    public var n: i32 = 0\n    public init() => ()\nfunc take(r: R) -> i32 => 0\n";
    private const string OptionGetter = "struct W\n    public var opt: Option<P> = .None\n        get() -> Option<P> => storage\n    public init() => ()\n";
    private const string PosGetter = "struct W\n    public var pos: P = P.init()\n        get() -> P => storage\n    public init() => ()\n";
    private const string TupleGetter = "struct W\n    public var t: (P, Option<i32>) = (P.init(), .None)\n        get() -> (P, Option<i32>) => storage\n    public init() => ()\n";
    private const string ArrayGetter = "struct W\n    public var fa: [2 of P] = [P.init(), P.init()]\n        get() -> [2 of P] => storage\n    public init() => ()\n";
    private const string Run = "func run(w: uniq/W) -> i32\n";
    private const string RunWithLocal = "func run(w: uniq/W, a: R) -> i32\n";

    // SPEC 23.1.2, 15.1.6 (D-823, PLAN G1076): a late rest's walk rests nothing of a match, `try` or `for` Subject Place, the use, a group
    // or a Place built on it: the construct's own binding owns a resting Subject, so each form keeps the records it gave before D-823,
    // the independent records in its arms or body included (MovedPlace_Kd, TransferRequired_Kd of an unrelated owned local `a`; a
    // binding `q` of the Subject at a result), except a Field read on the resting use (`w.pos.o`): it is derived from the use's record
    // with no located Unsupported_Kd (PLAN G1073 rule W at MirBuilder.Field, 3e2acf287; F's C2 ruling 312, Q13).
    // The rows marked "open: G1076" pin that InternalInvariant_Kd, which G1076's common fix
    // (the construct's arms and body published with failed-slot Pattern bindings) removes. Twins: a follow Place on a failed handle
    // getter, which no construct takes as its Subject, rests with the use (the declaration's record alone), and over a Copy P the getter
    // is valid. Every failed getter's body is published (PLAN G1071 gate 1), so each row but the valid twin has its `storage` read's
    // TransferRequired_Kd after the declaration's record, independent (`storage`'s Type resolved; F's C2 ruling 286 (i), Q13).
    [Theory]
    // open: G1076 (a.mopt, q.moptnob, q.mtup)
    [InlineData(NonCopyP + OptionGetter + Run + "    return match w.opt\n        .Some(let q) => q.x\n        .None => 0\n", "UnsatisfiedConstraint_Kd var opt: Option<P> = .None; TransferRequired_Kd storage; InternalInvariant_Kd run(w: uniq/W) -> i32; InternalInvariant_Kd .Some(let q); InternalInvariant_Kd let q; InternalInvariant_Kd .None")]
    [InlineData(NonCopyP + OptionGetter + Run + "    return match w.opt\n        .Some(_) => 1\n        .None => 0\n", "UnsatisfiedConstraint_Kd var opt: Option<P> = .None; TransferRequired_Kd storage; InternalInvariant_Kd run(w: uniq/W) -> i32; InternalInvariant_Kd .Some(_); InternalInvariant_Kd .None")]
    [InlineData(NonCopyP + "struct W\n    public var t: (P, i32) = (P.init(), 1)\n        get() -> (P, i32) => storage\n    public init() => ()\n" + Run + "    return match w.t\n        (_, let b) => 1\n", "UnsatisfiedConstraint_Kd var t: (P, i32) = (P.init(), 1); TransferRequired_Kd storage; InternalInvariant_Kd let b")]
    [InlineData(NonCopyP + OptionGetter + Run + "    return match (w.opt)\n        .Some(let q) => q.x\n        .None => 0\n", "UnsatisfiedConstraint_Kd var opt: Option<P> = .None; TransferRequired_Kd storage")]
    [InlineData(NonCopyP + OptionGetter + Run + "    let r = match (w.opt)\n        .Some(let q) => q\n        .None => P.init()\n    return r.x\n", "UnsatisfiedConstraint_Kd var opt: Option<P> = .None; TransferRequired_Kd storage; TransferRequired_Kd q")]
    [InlineData(NonCopyP + TupleGetter + Run + "    return match (w.t.1)\n        .Some(let n) => n\n        .None => 0\n", "UnsatisfiedConstraint_Kd var t: (P, Option<i32>) = (P.init(), .None); TransferRequired_Kd storage")]
    [InlineData(NonCopyP + ArrayGetter + Run + "    return match (w.fa[0])\n        _ => 1\n", "UnsatisfiedConstraint_Kd var fa: [2 of P] = [P.init(), P.init()]; TransferRequired_Kd storage")]
    [InlineData(NonCopyP + PosGetter + Run + "    return match w.pos.o\n        .Some(let n) => 1\n        .None => 0\n", "UnsatisfiedConstraint_Kd var pos: P = P.init(); TransferRequired_Kd storage")]
    [InlineData(NonCopyP + PosGetter + Run + "    return match (w.pos.o)\n        .Some(let n) => n\n        .None => 0\n", "UnsatisfiedConstraint_Kd var pos: P = P.init(); TransferRequired_Kd storage")]
    [InlineData(NonCopyP + PosGetter + Run + "    return match ((w.pos.o))\n        .Some(let n) => n\n        .None => 0\n", "UnsatisfiedConstraint_Kd var pos: P = P.init(); TransferRequired_Kd storage")]
    // open: G1076 (`try` and `for` over a Place built on the use)
    [InlineData(NonCopyP + PosGetter + "func run(w: uniq/W) -> Option<i32>\n    let n = try w.pos.o\n    return .Some(n)\n", "UnsatisfiedConstraint_Kd var pos: P = P.init(); TransferRequired_Kd storage; InternalInvariant_Kd w.pos.o")]
    [InlineData(NonCopyP + PosGetter + "func run(w: uniq/W) -> Option<i32>\n    let n = try (w.pos.o)\n    return .Some(n)\n", "UnsatisfiedConstraint_Kd var pos: P = P.init(); TransferRequired_Kd storage; InternalInvariant_Kd w.pos.o")]
    [InlineData(NonCopyP + PosGetter + "func look(v: ref/i32) -> () => ()\nfunc run(w: uniq/W)\n    for v in (w.pos.items) => look(v)\n", "UnsatisfiedConstraint_Kd var pos: P = P.init(); TransferRequired_Kd storage; InternalInvariant_Kd w.pos.items")]
    [InlineData(NonCopyP + OwnedR + ArrayGetter + RunWithLocal + "    var total = 0\n    for v in w.fa\n        total += take(a@move)\n    return total\n", "UnsatisfiedConstraint_Kd var fa: [2 of P] = [P.init(), P.init()]; TransferRequired_Kd storage; MovedPlace_Kd a")]
    [InlineData(NonCopyP + OwnedR + ArrayGetter + "func run(w: uniq/W, a: R)\n    for v in w.fa\n        let b = a\n", "UnsatisfiedConstraint_Kd var fa: [2 of P] = [P.init(), P.init()]; TransferRequired_Kd storage; TransferRequired_Kd a")]
    [InlineData(NonCopyP + OwnedR + TupleGetter + RunWithLocal + "    return match (w.t.1)\n        .Some(let n) => n\n        .None => take(a@move) + take(a@move)\n", "UnsatisfiedConstraint_Kd var t: (P, Option<i32>) = (P.init(), .None); TransferRequired_Kd storage; MovedPlace_Kd a")]
    [InlineData(NonCopyP + OwnedR + TupleGetter + RunWithLocal + "    let r = match (w.t.1)\n        .Some(let n) => R.init()\n        .None => a\n    return r.n\n", "UnsatisfiedConstraint_Kd var t: (P, Option<i32>) = (P.init(), .None); TransferRequired_Kd storage; TransferRequired_Kd a")]
    [InlineData(NonCopyP + OwnedR + PosGetter + RunWithLocal + "    return match w.pos.o\n        .Some(let n) => n\n        .None => take(a@move) + take(a@move)\n", "UnsatisfiedConstraint_Kd var pos: P = P.init(); TransferRequired_Kd storage; MovedPlace_Kd a")]
    // open: G1076 (the use itself as the Subject, with an unrelated local's record in an arm)
    [InlineData(NonCopyP + OwnedR + OptionGetter + RunWithLocal + "    let r = match w.opt\n        .Some(_) => R.init()\n        .None => a\n    return r.n\n", "UnsatisfiedConstraint_Kd var opt: Option<P> = .None; TransferRequired_Kd storage; InternalInvariant_Kd run(w: uniq/W, a: R) -> i32; InternalInvariant_Kd .Some(_); InternalInvariant_Kd .None; TransferRequired_Kd a")]
    [InlineData(NonCopyP + "struct W\n    public var h: rc/P = P.init()@rc\n        get() -> rc/P => storage\n    public init() => ()\n" + Run + "    return w.h@follow.x\n", "UnsatisfiedConstraint_Kd var h: rc/P = P.init()@rc; TransferRequired_Kd storage")]
    [InlineData("struct P\n    Self is Copy\n    public var x: i32 = 0\n    public init() => ()\n" + OptionGetter + Run + "    return match w.opt\n        .Some(let q) => q.x\n        .None => 0\n", "")]
    public void AMatchTryOrForSubjectIsNotRestedByALateRest(string program, string records)
    {
        var source = "public func main() => ()\n" + program;
        Assert.Equal(records, string.Join("; ", DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {source.Substring(x.Span!.Value.Start, x.Span.Value.Length).Split('\n')[0]}")));
    }

    // SPEC 11.8, 13.7.2, 23.1.1 (PLAN G295): a bare update of a group Property beside a custom accessor, inside its group, completes its
    // sides, then is Binding's located Unsupported_Kd at the target until P42 (before, no record and a PrerequisiteUnavailable_Kd without
    // a root). Twins bind: the standard accessors, the qualified form (the later phases' limit), a bare read and a bare write.
    [Theory]
    [InlineData(CountSetter, "count += 1", "Unsupported_Kd count")]
    [InlineData(CountSetter, "count++", "Unsupported_Kd count")]
    [InlineData(CountGetter, "count |= 1", "Unsupported_Kd count")]
    [InlineData(CountGetter + CountSetter, "--count", "Unsupported_Kd count")]
    [InlineData("", "count += 1", "")]
    [InlineData(CountSetter, "Config.count += 1", "")]
    [InlineData(CountGetter, "let v = count", "")]
    [InlineData(CountSetter, "count = 1", "")]
    public void ABareGroupPropertyUpdateBesideACustomAccessorIsTheLocatedLimit(string accessors, string use, string record)
    {
        var c = MinimalEmissionTest.Analyze($"group Config\n    public var count: i32 = 0\n{accessors}    public func bump() -> ()\n        {use}\n" +
            "        return\npublic func main() -> ()\n    Config.bump()\n    return\n");
        c.Binding.ReportDiagnostics();
        Assert.Equal(record.Length == 0 ? [] : [record], c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}"));
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    [Fact]
    public void SharedStoredGetterCannotWriteItsContextualStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    var item: i32\n        get(self: ref/Self) -> i32\n            storage = 1\n            return storage");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("ref/i32", PropertyWitnessKind.StorageCopy)]
    [InlineData("ref/(ref/i32 during static)", PropertyWitnessKind.StorageBorrow)]
    public void DistinguishesReferenceCopyFromBorrowingTheReferenceSlot(string result, PropertyWitnessKind kind)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    property item: {result} has get\nstruct S\n    Self is C\n    public var item: ref/i32 during static");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(kind, Assert.Single(Conformance(c, "S", "C").PropertyWitnesses).Kind);
    }

    [Fact]
    public void CustomGetterDoesNotExposeHiddenStorageForBorrowWitnesses()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: ref/i32 has get\nstruct S\n    Self is C\n    public var item: i32\n        get(self: ref/Self) -> i32 => storage");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void InheritedStorageWitnessRetainsItsSelectedBasePathAndSubstitution()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: i32 has get, set\nopen struct Base<T>\n    public var item: T\nopen struct Middle<U>: Base<U>\nstruct S: Middle<i32>\n    Self is C");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var witness = Conformance(c, "S", "C").PropertyWitnesses[0];
        Assert.Equal("Base", witness.ImplementationType.Symbol!.Name);
        Assert.Equal("i32", Assert.Single(witness.ImplementationType.Components).Name);
        Assert.NotNull(witness.BasePath!.Parent);
        Assert.Equal("Middle", witness.BasePath.Parent.Type.Symbol!.Name);
        Assert.Same(Property(c, "Base", "item").Getter, witness.Implementation);
    }

    [Theory]
    [InlineData("public var item: bool")]
    [InlineData("private var item: i32")]
    [InlineData("public func item() -> i32 => 0")]
    public void FailedSelectedMemberDoesNotRetryTheBaseProperty(string member)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    property item: i32 has get\nopen struct Base\n    public var item: i32\nstruct S: Base\n    Self is C\n    {member}");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(c.Binding.GetConformance(Container(c, "S").TypeOf()!, Container(c, "C").SymbolOf()!));
    }

    [Theory]
    [InlineData("struct Base\nstruct S: Base")]
    [InlineData("open struct A: B\nopen struct B: A")]
    [InlineData("open struct A<T>: B<T>\nopen struct B<T>: A<(T, T)>")]
    [InlineData("struct S: i32")]
    public void RejectsInvalidBasesBeforePublishingInheritedWitnesses(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void OrdinaryLookupSharesInheritedPropertyTypeSubstitution()
    {
        var c = CompilationTestHelper.ParseSuccess("open struct Base<T>\n    public var item: T\nstruct S: Base<i32>\nfunc read(value: ref/S) -> i32 => value.item");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var member = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single(x => x.Right.ToString() == "item");
        Assert.Same(Property(c, "Base", "item").Symbol, member.SymbolOf());
        Assert.Equal("i32", member.TypeOf()!.Name);
    }

    [Fact]
    public void CopyPropertyUseHasCheckedExpressionOperations()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 0\nfunc read(value: ref/S) -> i32 => value.item");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(Property(c, "S", "item").IsVerified);
    }

    [Theory]
    [InlineData("get(self: ref/Self) -> i32 => storage", PropertyWitnessKind.AccessorCall, true)]
    [InlineData("private get", PropertyWitnessKind.StorageCopy, false)]
    public void ReplacingAnAccessorRebindsSymbolsPermissionsAndWitnesses(string replacement, PropertyWitnessKind expected, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("public contract C\n    property item: i32 has get\npublic struct S\n    Self is C\n    public var item: i32\n        get");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "S", "item");
        var mapping = Conformance(c, "S", "C");
        var fragment = CompilationTestHelper.ParseSuccess($"struct Replacement\n    public var item: i32\n        {replacement}");
        var newProperty = Container(fragment, "Replacement").Members.OfType<PropertyKoto>().Single();
        var newAccessor = newProperty.Accessors.Single();
        var reader = new TokenReader(newAccessor.CodeContext, string.Empty, []);
        ParseTestHelper.Detach(newAccessor, new PropertyAccessorKoto(ref reader, default, default, PropertyAccessorKind.Get, null));
        Assert.True(KotoHelper.Replace(property.Declaration, property.Getter.Declaration!, newAccessor));
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Same(property, Property(c, "S", "item"));
        Assert.Equal(valid, mapping.IsVerified);
        if (valid)
        {
            Assert.Equal(expected, Assert.Single(mapping.PropertyWitnesses).Kind);
            Assert.Same(newAccessor, property.Getter.Declaration);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBorrowRequirementsAndInheritedWitnessesAllocateNothing()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: ref/i32 has get, set\ncontract D\n    property value: i32 has get\nopen struct Base<T>\n    public var value: T\nstruct S: Base<i32>\n    Self is D");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    // SPEC 9.4, 11.3.2, 23.3 (R7-E2 E2-4b): an absent or inaccessible accessor at a use is located at the use and relates the Property
    // (RecordAccess): a member access writing a getter-only Property (u4, the one related line dev's member-access arm gave) and an
    // IdentifierName writing an opened Property's private setter (u5a: dev related nothing). The twin has the setter. Each row lists the
    // Access records alone (a group's Property is Unsupported_Kd on its own, a boundary outside this rule).
    [Theory]
    [InlineData("struct S\n    public computed x: i32\n        get(self: ref/Self) -> i32 => 0\n    public init() => ()\nfunc f(s: uniq/S)\n    s.x = 1\n", "InaccessibleBinding_Kd 6:5 s.x, inaccessible declaration 'x'")] // u4
    [InlineData("struct S\n    public computed x: i32\n        get(self: ref/Self) -> i32 => 0\n        set(self: uniq/Self, value: i32) -> () => ()\n    public init() => ()\nfunc f(s: uniq/S)\n    s.x = 1\n", "")] // u4t
    [InlineData("alias G\ngroup G\n    public var x: i32 = 0\n        private set\nfunc f()\n    x = 1\n", "InaccessibleBinding_Kd 6:5 x, inaccessible declaration 'x'")] // u5a
    public void AnAccessorAtAUseRelatesItsProperty(string source, string records)
        => Assert.Equal(records, string.Join('|', DiagnosticCorpus.LocatedRecords(source + "public func main() => ()\n").Where(static x => x.StartsWith("InaccessibleBinding_Kd", StringComparison.Ordinal))));

    [Fact]
    public void RefinementsRetainPropertyOperationsByRequirementIdentity()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    property item: i32 has get\ncontract B: A\ncontract C: A\ncontract D: B, C\nstruct S\n    Self is D\n    public var item: i32");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Single(Conformance(c, "S", "D").PropertyWitnesses);
    }

    [Fact]
    public void RebindingInvalidatesAFormerlyVerifiedPropertyMapping()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: i32 has get\nstruct S\n    Self is C\n    public var item: i32");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var mapping = Conformance(c, "S", "C");
        var required = Property(c, "C", "item").Symbol;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct S\n    public var item: bool");
        Assert.False(c.Bind().IsComplete);
        Assert.False(mapping.IsVerified);
        Assert.Null(mapping.GetPropertyWitness(new(required, mapping.Contract), PropertyAccessorKind.Get));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(512)]
    public void WarmPropertyBindingReusesMetadataAndAllocatesNothing(int count)
    {
        var source = new System.Text.StringBuilder("contract C\n    property item: i32 has get, set\n    property view: ref/i32 has get\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("struct S").Append(i).Append("\n    Self is C\n    public var view: i32\n    public var item: i32\n        get(self: ref/Self) -> i32 => storage\n        set(self: uniq/Self, value: i32) -> () => storage = value\n");
        }

        var c = CompilationTestHelper.ParseSuccess(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "S0", "item");
        var mapping = Conformance(c, "S0", "C");
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Same(property, Property(c, "S0", "item"));
        Assert.Same(mapping, Conformance(c, "S0", "C"));
        Assert.Equal(0, allocated);
    }

    private static DeclarationContainerKoto Container(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<DeclarationContainerKoto>().Single(x => x.Name == name);

    private static BoundProperty Property(Compilation c, string type, string name) => Container(c, type).Members.OfType<PropertyKoto>().Single(x => x.NameKoto.IdentifierName == name).SymbolOf()!.Property!;

    private static BoundConformance Conformance(Compilation c, string type, string contract) => c.Binding.GetConformance(Container(c, type).TypeOf()!, Container(c, contract).SymbolOf()!)!;

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
