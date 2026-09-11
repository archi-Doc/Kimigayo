// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PropertyBindingTest
{
    [Theory]
    [InlineData("let", false)]
    [InlineData("var", true)]
    public void RetainsImplicitStandardPermissionsWithoutCreatingAccessors(string kind, bool setter)
    {
        var c = Parse($"struct S<T>\n    public {kind} item: T");
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
        var c = Parse($"struct S\n    public var item: i32\n        {accessor}");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ContextualStorageNamesTheSlotAndValueNamesTheInput()
    {
        var c = Parse("struct S\n    var item: i32\n        set(self: uniq/Self, value: i32) -> () => storage = value");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "S", "item");
        var names = Walk(property.Setter.Declaration!.Body!).OfType<IdentifierNameKoto>().ToArray();
        var storage = Assert.Single(names, x => x.IdentifierName == "storage");
        var value = Assert.Single(names, x => x.IdentifierName == "value");
        Assert.Equal(BindingSymbolKind.Storage, storage.BoundSymbol!.Kind);
        Assert.Same(property.Declaration, storage.BoundSymbol.Declaration);
        Assert.Equal(BindingSymbolKind.Parameter, value.BoundSymbol!.Kind);
        Assert.NotSame(storage.BoundSymbol, value.BoundSymbol);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("    T is Copy\n", true)]
    public void CustomGetterRequiresDefinitionSideCopyEvidence(string constraint, bool valid)
    {
        var c = Parse($"struct S<T>\n{constraint}    var item: T\n        get(self: ref/Self) -> T => storage");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void CustomSetterDoesNotRequireCopy()
    {
        var c = Parse("struct S<T>\n    var item: T\n        set(self: uniq/Self, value: T) -> () => storage = value");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("from source", "from source", true)]
    [InlineData("", "from source", false)]
    [InlineData("from source", "", false)]
    public void StoredAccessorOriginsMustMatchStorage(string getter, string setter, bool valid)
    {
        var c = Parse($"struct S origin source\n    var item: ref/i32 from source\n        get(self: ref/Self) -> ref/i32 {getter} => storage\n        set(self: uniq/Self, value: ref/i32 {setter}) -> () => storage = value");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ReceiverOriginPathsRetainTheAccessorInputIdentity()
    {
        var c = Parse("struct S origin source\n    var item: ref/i32 from source\n        get(self: ref/Self) -> ref/i32 from self.source => storage");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var getter = Property(c, "S", "item").Getter;
        var self = Walk(getter.Declaration!.ReturnType!).OfType<IdentifierNameKoto>().Single(x => x.IdentifierName == "self");
        Assert.Equal(BindingSymbolKind.Parameter, self.BoundSymbol!.Kind);
        Assert.Same(getter.Declaration, self.BoundSymbol.Declaration);
        Assert.Same(Property(c, "S", "item").Type, getter.Result);
    }

    [Fact]
    public void StaticAccessorsHaveNoReceiverAndKeepTheirOwnStorageBinding()
    {
        var c = Parse("group G\n    var item: i32 = 0\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "G", "item");
        Assert.Null(property.Getter.Receiver);
        Assert.Null(property.Setter.Receiver);
        Assert.Equal("i32", property.Setter.Input!.Name);
    }

    [Fact]
    public void ComputedGetterAndSetterHaveIndependentOriginCompletion()
    {
        var c = Parse("struct S\n    computed item: ref/i32\n        get(self: ref/Self) -> ref/i32 => missing\n        set(self: uniq/Self, value: ref/i32) -> () => ()");
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
        var c = Parse("contract C\n    property item: ref/i32 has get, set");
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
        var c = Parse("contract C\n    property item: ref/i32 has get, set");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "C", "item");
        var header = property.Declaration.TypeKoto!;
        Assert.Equal(BindingState.Resolved, header.BindingState);
        Assert.Same(property.Type, header.BoundType);
    }

    [Theory]
    [InlineData("public", "private", true)]
    [InlineData("public", "public", false)]
    [InlineData("private", "private", false)]
    [InlineData("internal", "protected", false)]
    [InlineData("protected internal", "internal", true)]
    public void AccessorRestrictionMustBeStrictlyNarrower(string propertyAccess, string accessorAccess, bool valid)
    {
        var c = Parse($"struct S\n    {propertyAccess} var item: i32\n        {accessorAccess} get");
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
        var c = Parse($"public contract C\n    property item: i32 has {required}\npublic struct S\n    Self is C\n    {member}");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            var mapping = Conformance(c, "S", "C");
            Assert.Equal(PropertyWitnessKind.StorageCopy, mapping.GetPropertyWitness(Property(c, "C", "item").Symbol, PropertyAccessorKind.Get)!.Value.Kind);
        }
    }

    [Fact]
    public void RetainsSeparateGetAndSetWitnesses()
    {
        var c = Parse("contract C\n    property item: i32 has get, set\nstruct S\n    Self is C\n    public var item: i32");
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
        var c = Parse($"struct Box<T>\n    Self is Copy when T is Copy\n    var value: T\ncontract C\n    associate E\n    property item: {result} has get\nstruct S<T>\n{constraint}    Self is C\n    associate C.E is Box<T>\n    public var item: Box<T>");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("Resource", false)]
    [InlineData("ref/Resource", true)]
    public void NonCopyStorageCanImplementOnlyTheBorrowGetter(string result, bool valid)
    {
        var c = Parse($"struct Resource\ncontract C\n    property item: {result} has get\nstruct S\n    Self is C\n    public var item: Resource");
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
        var c = Parse($"contract C\n    property item: i32 has get\nstruct S\n    Self is C\n    public computed item: i32\n        {getter}");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            Assert.Equal(PropertyWitnessKind.AccessorCall, Assert.Single(Conformance(c, "S", "C").PropertyWitnesses).Kind);
        }
    }

    [Fact]
    public void ExplicitComputedSetterMayTakeADifferentType()
    {
        var c = Parse("contract C\n    property item: i32\n        get(self: ref/Self) -> i32\n        set(self: uniq/Self, value: bool) -> ()\nstruct S\n    Self is C\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 1\n        set(self: uniq/Self, value: bool) -> () => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal("bool", Conformance(c, "S", "C").PropertyWitnesses[1].InputType!.Name);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("private", true)]
    public void SetterSignatureAccessibilityUsesItsOwnDomain(string access, bool valid)
    {
        var modifier = access == "public" ? string.Empty : access + " ";
        var c = Parse($"struct Hidden\npublic struct S\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 1\n        {modifier}set(self: uniq/Self, value: Hidden) -> () => ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("value.item = 1")]
    [InlineData("value.item += 1")]
    [InlineData("value.item++")]
    [InlineData("++value.item")]
    public void StandardWritesAndUpdatesRequireTheSetterPermission(string expression)
    {
        var c = Parse($"struct S\n    public var item: i32\n        private set\nfunc write(value: uniq/S)\n    {expression}");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void SimpleWriteDoesNotRequireTheGetterPermission()
    {
        var c = Parse("struct S\n    public var item: i32\n        private get\nfunc write(value: uniq/S)\n    value.item = 1");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void SharedStoredGetterCannotWriteItsContextualStorage()
    {
        var c = Parse("struct S\n    var item: i32\n        get(self: ref/Self) -> i32\n            storage = 1\n            return storage");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("ref/i32", PropertyWitnessKind.StorageCopy)]
    [InlineData("ref/(ref/i32 from static)", PropertyWitnessKind.StorageBorrow)]
    public void DistinguishesReferenceCopyFromBorrowingTheReferenceSlot(string result, PropertyWitnessKind kind)
    {
        var c = Parse($"contract C\n    property item: {result} has get\nstruct S\n    Self is C\n    public var item: ref/i32 from static");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(kind, Assert.Single(Conformance(c, "S", "C").PropertyWitnesses).Kind);
    }

    [Fact]
    public void CustomGetterDoesNotExposeHiddenStorageForBorrowWitnesses()
    {
        var c = Parse("contract C\n    property item: ref/i32 has get\nstruct S\n    Self is C\n    public var item: i32\n        get(self: ref/Self) -> i32 => storage");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void InheritedStorageWitnessRetainsItsSelectedBasePathAndSubstitution()
    {
        var c = Parse("contract C\n    property item: i32 has get, set\nopen struct Base<T>\n    public var item: T\nopen struct Middle<U>: Base<U>\nstruct S: Middle<i32>\n    Self is C");
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
        var c = Parse($"contract C\n    property item: i32 has get\nopen struct Base\n    public var item: i32\nstruct S: Base\n    Self is C\n    {member}");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(c.Binding.GetConformance(Container(c, "S").BoundType!, Container(c, "C").BoundSymbol!));
    }

    [Theory]
    [InlineData("struct Base\nstruct S: Base")]
    [InlineData("open struct A: B\nopen struct B: A")]
    [InlineData("open struct A<T>: B<T>\nopen struct B<T>: A<(T, T)>")]
    [InlineData("struct S: i32")]
    public void RejectsInvalidBasesBeforePublishingInheritedWitnesses(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void OrdinaryLookupSharesInheritedPropertyTypeSubstitution()
    {
        var c = Parse("open struct Base<T>\n    public var item: T\nstruct S: Base<i32>\nfunc read(value: ref/S) -> i32 => value.item");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var member = Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single(x => x.Right.ToString() == "item");
        Assert.Same(Property(c, "Base", "item").Symbol, member.BoundSymbol);
        Assert.Equal("i32", member.BoundType!.Name);
    }

    [Fact]
    public void CallablePropertyUseRemainsPendingUntilExpressionOperationChecking()
    {
        var c = Parse("struct S\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 0\nfunc read(value: ref/S) -> i32 => value.item");
        Assert.False(c.Bind().IsComplete);
        Assert.True(Property(c, "S", "item").IsVerified);
    }

    [Theory]
    [InlineData("get(self: ref/Self) -> i32 => storage", PropertyWitnessKind.AccessorCall, true)]
    [InlineData("private get", PropertyWitnessKind.StorageCopy, false)]
    public void ReplacingAnAccessorRebindsSymbolsPermissionsAndWitnesses(string replacement, PropertyWitnessKind expected, bool valid)
    {
        var c = Parse("public contract C\n    property item: i32 has get\npublic struct S\n    Self is C\n    public var item: i32\n        get");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "S", "item");
        var mapping = Conformance(c, "S", "C");
        var fragment = Parse($"struct Replacement\n    public var item: i32\n        {replacement}");
        var newProperty = Container(fragment, "Replacement").Members.OfType<PropertyKoto>().Single();
        var newAccessor = newProperty.Accessors.Single();
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

    [Fact]
    public void WarmBorrowRequirementsAndInheritedWitnessesAllocateNothing()
    {
        var c = Parse("contract C\n    property item: ref/i32 has get, set\ncontract D\n    property value: i32 has get\nopen struct Base<T>\n    public var value: T\nstruct S: Base<i32>\n    Self is D");
        Assert.True(c.Bind().IsComplete, Describe(c));
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RefinementsRetainPropertyOperationsByRequirementIdentity()
    {
        var c = Parse("contract A\n    property item: i32 has get\ncontract B: A\ncontract C: A\ncontract D: B, C\nstruct S\n    Self is D\n    public var item: i32");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Single(Conformance(c, "S", "D").PropertyWitnesses);
    }

    [Fact]
    public void RebindingInvalidatesAFormerlyVerifiedPropertyMapping()
    {
        var c = Parse("contract C\n    property item: i32 has get\nstruct S\n    Self is C\n    public var item: i32");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var mapping = Conformance(c, "S", "C");
        var required = Property(c, "C", "item").Symbol;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct S\n    public var item: bool");
        Assert.False(c.Bind().IsComplete);
        Assert.False(mapping.IsVerified);
        Assert.Null(mapping.GetPropertyWitness(required, PropertyAccessorKind.Get));
    }

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

        var c = Parse(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        var property = Property(c, "S0", "item");
        var mapping = Conformance(c, "S0", "C");
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Same(property, Property(c, "S0", "item"));
        Assert.Same(mapping, Conformance(c, "S0", "C"));
        Assert.Equal(0, allocated);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static DeclarationContainerKoto Container(Compilation c, string name) => Walk(c.Kotonoha.RootKoto).OfType<DeclarationContainerKoto>().Single(x => x.Name == name);

    private static BoundProperty Property(Compilation c, string type, string name) => Container(c, type).Members.OfType<PropertyKoto>().Single(x => x.NameKoto.IdentifierName == name).BoundSymbol!.Property!;

    private static BoundConformance Conformance(Compilation c, string type, string contract) => c.Binding.GetConformance(Container(c, type).BoundType!, Container(c, contract).BoundSymbol!)!;

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
