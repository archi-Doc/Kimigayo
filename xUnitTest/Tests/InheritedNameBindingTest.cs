// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class InheritedNameBindingTest
{
    [Theory]
    [InlineData("public func f(x?: i32) => ()", "private func f(x?: string) => ()")]
    [InlineData("public var f: i32", "public var f: i32")]
    [InlineData("protected func f(self: ref/Self) => ()", "public func f() => ()")]
    [InlineData("public var f: i32\n        private get", "private func f() => ()")]
    [InlineData("protected var f: i32", "public var f: i32")]
    [InlineData("internal func f() => ()", "public func f() => ()")]
    [InlineData("protected internal func f() => ()", "public func f() => ()")]
    [InlineData("private protected func f() => ()", "public func f() => ()")]
    public void AccessibleBaseNamesCannotBeRedeclared(string parent, string child)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    " + parent + "\nstruct S: Base\n    " + child);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, Type(c)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
        Assert.False(c.Bind().IsComplete);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Contains(restored.Binding.Issues, x => ReferenceEquals(x.Node, Type(restored)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Theory]
    [InlineData("private func f() => ()", "public func f() => ()")]
    [InlineData("private var f: i32", "public var f: i32")]
    [InlineData("public func other() => ()", "public func f(x?: i32) => ()\n    public func f(x?: string) => ()")]
    public void InaccessibleNamesAndSameLayerOverloadsRemainValid(string parent, string child)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    " + parent + "\nstruct S: Base\n    " + child);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransitiveGenericBasesInvalidateDescendantCertificates(bool reverseOrder)
    {
        const string middle = "open struct Middle<U>: Base<U>\n    public func f() => ()\n";
        const string child = "struct S: Middle<i32>\n    Self is C\n";
        var c = MinimalEmissionTest.Analyze("contract C\nopen struct Base<T>\n    public func f(x?: T) => ()\n" + (reverseOrder ? child + middle : middle + child));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Type(c).BindingState);
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(Type(c).BoundType!, contract.BoundSymbol!)!.IsVerified);
    }

    [Fact]
    public void NamesAreCheckedThroughAnEmptyIntermediateBase()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    protected var f: T\nopen struct Middle<U>: Base<U>\nstruct S: Middle<i32>\n    func f() => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, Type(c)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Theory]
    [InlineData("open struct Base<T>\n    Self is C when T is Copy\n        public func f() => ()\nstruct S<T>: Base<T>\n    public func f() => ()")]
    [InlineData("open struct Base\n    public func f() => ()\nstruct S<T>: Base\n    Self is C when T is Copy\n        public func f() => ()")]
    public void ConditionalPremisesDoNotExemptNames(string declarations)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n" + declarations);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, Type(c)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void FragmentsAreCheckedAsOneDeclaration()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public func f() => ()\nstruct S: Base");
        Assert.True(c.Binding.Result.IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("fragment.kimi", "struct S\n    private func f(x?: i32) => ()"));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, Type(c)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void ReplacingBaseAccessRechecksNameAvailability()
    {
        const string source = "open struct Base\n    private func f() => ()\nstruct S: Base\n    public func f() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var parent = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Base");
        var original = parent.Members.OfType<FunctionKoto>().Single();
        var donor = MinimalEmissionTest.Analyze(source.Replace("private func", "public func", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Base").Members.OfType<FunctionKoto>().Single();
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmInheritedNameChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    private func f(x?: T) => ()\nopen struct Middle<U>: Base<U>\nstruct S: Middle<i32>\n    public func f() => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Inherited Name validation failed.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    private static StructKoto Type(Compilation c)
        => Assert.IsType<StructKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S"));
}
