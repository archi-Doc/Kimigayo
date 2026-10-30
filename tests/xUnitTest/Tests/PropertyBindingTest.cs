// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class PropertyBindingTest
{
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

    // An `x: i32` Field beside a custom accessor of another Type, and its twin (AUseOfAWronglyTypedAccessorRestsOnItsDeclaration).
    private const string ClosedField = "struct HW\n    public var x: i32\n";
    private const string ClosedInit = "    public init(x: i32) => self.x = x\n";
    private const string WrongGetter = ClosedField + "        get() -> i64\n            return 1\n" + ClosedInit;
    private const string GenericWrongGetter = "struct HW<S>\n    S is Copy\n    public var x: S\n        get() -> i64\n            return 1\n" +
        "    public init(x: S) => self.x = x\n";

    private const string WrongSetter = ClosedField + "        set(value: i64) -> ()\n            storage = 1\n" + ClosedInit;
    private const string RightGetter = ClosedField + "        get() -> i32\n            return storage\n" + ClosedInit;

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
        var c = CompilationTestHelper.ParseSuccess("struct S<T>\n    var item: T\n        set(self: uniq/Self, value: T) -> () => storage = value");
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
    // update. Twin: a custom `get` of the Field's Type, which is valid. Updates beside a wrongly typed custom accessor rest on its
    // declaration (AUseOfAWronglyTypedAccessorRestsOnItsDeclaration).
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

    // SPEC 11.3, 11.4, 23.1.2 (PLAN G296): a use of an accessor whose Type is not its Property's takes no Type and rests on the
    // declaration's TypeMismatch_Kd, so updates (`+=`, `|=`, `++`, `--`, a user arithmetic `+=`, a generic Field, a qualified and a bare
    // group form), a read and a write through it add no record (before, a TypeMismatch_Kd at each, G295's Unsupported_Kd at the bare
    // form, and InternalInvariant_Kd beside a missing operand). Independent twins keep theirs: a missing operand, a string operand (its
    // prohibition survives the missing counterpart), a read through the standard `get` beside a failed `set` and a write through the
    // standard `set` beside a failed `get`. The valid twin: a `get` of the Field's Type.
    [Theory]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x += 4", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x |= 4", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x++", "TypeMismatch_Kd get() -> i64")]
    [InlineData(GenericWrongGetter, "var h = HW<i32>.init(x: 3)\n    h.x += 4", "TypeMismatch_Kd get() -> i64")]
    [InlineData(AddableWrongGetter, "var h = HW.init(x: 3)\n    h.x += Counter.init(2)", "TypeMismatch_Kd get() -> Counter")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    h.x |= 4", "TypeMismatch_Kd set(value: i64) -> ()")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    --h.x", "TypeMismatch_Kd set(value: i64) -> ()")]
    [InlineData(WrongCountGetter, "Config.count += 1", "TypeMismatch_Kd get() -> i64")]
    [InlineData(BareCountUpdate, "Config.bump()", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    let v: i32 = h.x", "TypeMismatch_Kd get() -> i64")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    let n: i32 = 4\n    h.x = n", "TypeMismatch_Kd set(value: i64) -> ()")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x += missing", "TypeMismatch_Kd get() -> i64;UnresolvedBinding_Kd missing")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x += \"a\"", "TypeMismatch_Kd get() -> i64;NonNumericOperand_Kd h.x += \"a\"")]
    [InlineData(WrongSetter, "var h = HW.init(x: 3)\n    let v: bool = h.x", "TypeMismatch_Kd set(value: i64) -> ();TypeMismatch_Kd let v: bool = h.x")]
    [InlineData(WrongGetter, "var h = HW.init(x: 3)\n    h.x = true", "TypeMismatch_Kd get() -> i64;TypeMismatch_Kd h.x = true")]
    [InlineData(RightGetter, "var h = HW.init(x: 3)\n    h.x += 4\n    h.x |= 4\n    h.x++\n    let v: i32 = h.x", "")]
    public void AUseOfAWronglyTypedAccessorRestsOnItsDeclaration(string holder, string use, string records)
    {
        var c = MinimalEmissionTest.Analyze(holder + $"public func main() -> ()\n    {use}\n    return\n");
        c.Binding.ReportDiagnostics();
        Assert.Equal(records.Length == 0 ? [] : records.Split(';'), c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}".Split('\n')[0]));
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    // A warm rebind of uses resting on a wrongly typed accessor's declaration reuses the prerequisite storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void UsesRestingOnAWronglyTypedAccessorRebindWithoutAllocating()
    {
        var c = MinimalEmissionTest.Analyze(WrongGetter + "public func main() -> ()\n    var h = HW.init(x: 3)\n    h.x += 4\n    h.x++\n    let v: i32 = h.x\n    return\n");
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }

    // SPEC 23.1.2 (PLAN G296): an update beside a custom `set` whose standard side has no Type (a rejected instance, an unresolved Field
    // Type) completes unresolved, a dependent of the reported failure that adds no record (before, a direct TypeMismatch_Kd at `h.x`).
    // Twins: the standard accessors' update, which added none before either.
    [Theory]
    [InlineData(RejectedHolder, "h.x += 1")]
    [InlineData(RejectedHolder, "h.x |= 1")]
    [InlineData(RejectedHolder, "h.x++")]
    [InlineData(UnresolvedHolder, "h.x += 1")]
    [InlineData(RejectedStandard, "h.x += 1")]
    [InlineData(UnresolvedStandard, "h.x += 1")]
    public void AnUpdateWithoutAStoredTypeAddsNoRecord(string holder, string update)
    {
        const string Main = "    return\npublic func main() -> ()\n    return\n";
        Assert.Equal(Errors(holder + Main), Errors(holder + $"    {update}\n" + Main));

        static string[] Errors(string source)
        {
            var c = MinimalEmissionTest.Analyze(source);
            c.Binding.ReportDiagnostics();
            return [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => x.Code)];
        }
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
