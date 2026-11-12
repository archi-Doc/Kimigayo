// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class InheritedNameBindingTest
{
    [Theory]
    [InlineData("public func f(x: i32) => ()", "private func f(x: string) => ()")]
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
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, Type(c)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
        Assert.False(c.Bind().IsComplete);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Contains(restored.Binding.Issues, x => ReferenceEquals(x.Node, Type(restored)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    // SPEC 6.2.2, 9.6: a lookup that reaches a struct whose clause another lookup is binding is no inheritance cycle (R7-N2c).
    [Theory]
    [InlineData("open struct Z<T>\n    public struct Other\ncontract K<T>\nopen struct E: Z<D.Inner>\n    public struct Inner\nstruct D: E\n    Self is K<D.Other>")]
    [InlineData("contract K<T>\nstruct D: E\n    Self is K<D.Other>\nopen struct E: Z<D.Inner>\n    public struct Inner\nopen struct Z<T>\n    public struct Other")]
    public void AReentryForAnotherNameIsNoCycle(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 6.2.2, 9.6: lookup through a cycle reads every declaration on it, and a miss is one record, wherever the walk starts (R7-N2c).
    [Theory]
    [InlineData("open struct X: Y\n    public func x(self) -> i32 => 1\nopen struct Y: X\n    public func y(self) -> i32 => 1\nfunc f(v: ref/X, w: ref/Y) -> i32 => v.y() + w.x() + v.none()", 1)]
    [InlineData("open struct D: E\n    public func d(self) -> i32 => 1\nopen struct E: F\n    public func e(self) -> i32 => 1\nopen struct F: E\n    public func g(self) -> i32 => 1\nfunc f(v: ref/D, w: ref/F) -> i32 => v.g() + w.e() + w.none()", 1)]
    [InlineData("open struct G<T>: G<Option<T>>\n    public func g(self) -> i32 => 1\nfunc f(v: ref/G<i32>) -> i32 => v.g() + v.none()", 1)]
    public void AWalkThroughACycleReadsEachLayerAndEnds(string source, int misses)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(misses, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.AmbiguousBinding_Kd));
    }

    [Theory]
    [InlineData("private func f() => ()", "public func f() => ()")]
    [InlineData("private var f: i32", "public var f: i32")]
    [InlineData("public func other() => ()", "public func f(x: i32) => ()\n    public func f(x: string) => ()")]
    public void InaccessibleNamesAndSameLayerOverloadsRemainValid(string parent, string child)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    " + parent + "\nstruct S: Base\n    " + child);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransitiveGenericBasesInvalidateDescendantCertificates(bool reverseOrder)
    {
        const string middle = "open struct Middle<U>: Base<U>\n    public func f() => ()\n";
        const string child = "struct S: Middle<i32>\n    Self is C\n";
        var c = MinimalEmissionTest.Analyze("contract C\nopen struct Base<T>\n    public func f(x: T) => ()\n" + (reverseOrder ? child + middle : middle + child));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Type(c).StateOf());
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(Type(c).TypeOf()!, contract.SymbolOf()!)!.IsVerified);
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
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, Type(c)) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void FragmentsAreCheckedAsOneDeclaration()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public func f() => ()\nstruct S: Base");
        Assert.True(c.Binding.Result.IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("fragment.kimi", "struct S\n    private func f(x: i32) => ()"));
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
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    // A warm rebind checks inherited Names without allocating, also for a derived struct with nested Types (PLAN G609: p2 and rx1c
    // allocated 80 B, a boxed enumerator of the nested Types twice a Bind).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("open struct Base<T>\n    private func f(x: T) => ()\nopen struct Middle<U>: Base<U>\nstruct S: Middle<i32>\n    public func f() => ()")]
    [InlineData("open struct X\n    protected open struct Y\n        public init() => ()\n    public init() => ()\nstruct D: X\n    public init() => ()\n    struct E: X.Y\n        public init() => ()")] // p2
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        public init(): base() => ()\n        func h<X>(self, x: ref/X) -> i32\n            X is K<Q.N>\n            return 1")] // rx1c
    public void WarmInheritedNameChecksAllocateNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Inherited Name validation failed.");
            }
        }));
    }

    // SPEC 9.4.2, 9.6 (R7-N2d): a base clause resolves without its own struct's layers, in its name and arguments; these overflowed the
    // stack or were rejected before.
    [Theory]
    [InlineData("open struct D: Q\n    public init() => ()\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct U: D.N\n        public init() => ()\n        func g(self) -> i32 => self.far()")]
    [InlineData("open struct D: Q\n    public init() => ()\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N: Q\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct U: D.N\n        public init() => ()\n        func g(self) -> i32 => self.far()")]
    [InlineData("open struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct U: Q.N\n        public init() => ()\n        func g(self) -> i32 => self.far()")]
    [InlineData("open struct E\n    protected internal open struct M\n        public init() => ()\n    public init() => ()\nopen struct D: E\n    public init() => ()\nstruct U: D.M\n    public init() => ()")]
    [InlineData("open struct E\n    protected internal open struct M\n        public init() => ()\n    public init() => ()\nstruct U: E.M\n    public init() => ()")]
    [InlineData("open struct E\n    protected internal open struct M\n        public func m(self) -> i32 => 1\n        public init() => ()\n    public init() => ()\nopen struct C: E\n    public init() => ()\nopen struct D: C\n    public init() => ()\nstruct U: D.M\n    public init() => ()\n    func g(self) -> i32 => self.m()")]
    public void AClauseResolvesWithoutItsOwnLayers(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 6.2.2, 9.4.2 (R7-N2d): the invalid twins end with their own record; p1 overflowed the stack before, so its parent fails by
    // killing the host. A parameter or a projection through one is no base, and the walk ends at it (rt1, rt8).
    [Theory]
    [InlineData("open struct X\n    protected open struct Y\n        public init() => ()\n    public init() => ()\nstruct D: X.Y\n    public init() => ()", DiagnosticCode.InaccessibleBinding_Kd)]
    [InlineData("open struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Q\n        public init(): base() => ()\n        struct U: Q.N\n            public init() => ()\n            func g(self) -> i32 => self.far()", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("open struct E\n    protected open struct M\n        public init() => ()\n    public init() => ()\nopen struct D: E\n    public init() => ()\nstruct U: D.M\n    public init() => ()", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("open struct W<T>: T\nopen struct V<T>: T\nopen struct X<T>: T\nopen struct Z\n    public func z(self) -> i32 => 1\n    public init() => ()\nfunc f(a: ref/V<W<X<V<Z>>>>) -> i32 => a.z()", DiagnosticCode.InvalidTypeFormation_Kd)]
    [InlineData("contract C\n    associate Next\nopen struct L<T>: T.(C).Next\nstruct A\n    Self is C\n    associate C.Next is L<B>\nstruct B\n    Self is C\n    associate C.Next is L<A>\nfunc f(a: ref/L<A>) -> i32 => a.nope()", DiagnosticCode.InvalidTypeFormation_Kd)]
    public void AClauseThatNeedsItsOwnLayersIsRejected(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    // SPEC 6.2.2 (R7-N2d): a base cycle's published base is cut, so a walk over an instance's layers ends; both overflowed the stack
    // before, so their parent fails by killing the host.
    [Theory]
    [InlineData("open struct X: Y\n    public var a: i32 = 1\n    public init()\n        self.a = 2\nopen struct Y: X\n    public var b: i32 = 2\n    public init()\n        self.a = 3")]
    [InlineData("open struct X {a}: Y during a\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\nopen struct Y {a}: X during a\n    public let s: ref/i32 during a\nfunc make(v: ref/i32) -> X during v => X.init(v)\nfunc use(v: ref/i32) -> i32\n    let x = make(v)\n    let y = x@move\n    return y.r@follow\nfunc view(x: ref/X) -> ref/i32 => x.r")]
    public void ABaseCycleEndsInRecords(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.InvalidTypeFormation_Kd);
    }

    private static StructKoto Type(Compilation c)
        => Assert.IsType<StructKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S"));
}
