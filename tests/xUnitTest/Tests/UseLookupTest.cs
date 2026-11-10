// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// R7-N1e2-1R (the owner's O-111 (a)): a Type-name use is resolved once, in its position's row, and published on the use
// (HirTables.Lookups); every other reader reads it (SPEC 9.2, 9.5, 9.6, 23.1.2). Each invalid row has a valid counterpart.
public class UseLookupTest
{
    private const string BoundEffect = "contract P<T>\n    func value(self: ref/Self, x: T) -> T\n";
    private const string HasItem = "contract HasItem\n    associate Item\n";
    private const string Inherited = HasItem + "open struct Base\n    public struct N\n        Self is HasItem\n        associate HasItem.Item is i32\n";
    private const string Opened = "alias A\nalias B\ncontract HasItem\n    associate Item\ngroup A\n    public group X\n        public func z() -> i32 => 0\n";

    [Theory]
    // A qualifier is looked up once (SPEC 9.6): an opened group and an opened struct `X` make the receiver ambiguous, which dev's second
    // (NoContainer) lookup hid (t01); the same receiver behind one opened struct is a projection.
    [InlineData(Opened + "group B\n    public struct X\n        Self is HasItem\n        associate HasItem.Item is i32\nfunc f(v: X.(HasItem).Item) -> i32 => 0", "AmbiguousBinding_Kd X")]
    [InlineData("alias B\n" + HasItem + "group B\n    public struct X\n        Self is HasItem\n        associate HasItem.Item is i32\nfunc f(v: X.(HasItem).Item) -> i32 => 0", "")]
    // An inherited `N` needs a qualifier (9.5): one record for one stop, at a projection's receiver (t09, t12) and a requirement (v05).
    [InlineData(Inherited + "struct Derived : Base\n    func f(v: N.Item) -> i32 => 0", "QualificationRequired_Kd N.Item")]
    [InlineData(Inherited + "struct Derived : Base\n    func f(v: N.(HasItem).Item) -> i32 => 0", "QualificationRequired_Kd N.(HasItem).Item")]
    [InlineData(Inherited + "struct Derived : Base\n    func f<T>(x: T) -> i32\n        T is N.Item\n        return 0", "InvalidConstraint_Kd func f<T>(x: T) -> i32\n    T is N.Item\n    return 0|QualificationRequired_Kd N.Item")]
    [InlineData(Inherited + "struct Derived : Base\n    func f(v: Base.N.Item) -> i32 => 0", "")]
    // A pattern qualifier is a qualifier (9.6, 9.7): a nearer group stops it (p14), and so does stage 4's reference name (w02).
    [InlineData("enum E\n    A\n    B\ngroup G\n    group E\n        func z() -> i32 => 0\n    func f(e: E) -> i32\n        let n = match e\n            E.A => 1\n            E.B => 2\n        return n", "InvalidPattern_Kd E.A|InvalidPattern_Kd E.B")]
    [InlineData("enum E\n    A\n    B\ngroup G\n    func f(e: E) -> i32\n        let n = match e\n            E.A => 1\n            E.B => 2\n        return n", "")]
    [InlineData("alias A\ngroup A\n    public enum Kimi\n        X\n        Y\nfunc f(e: Kimi) -> i32\n    let n = match e\n        Kimi.X => 1\n        Kimi.Y => 2\n    return n", "InvalidPattern_Kd Kimi.X|InvalidPattern_Kd Kimi.Y")]
    [InlineData("alias A\ngroup A\n    public enum Pick\n        X\n        Y\nfunc f(e: Pick) -> i32\n    let n = match e\n        Pick.X => 1\n        Pick.Y => 2\n    return n", "")]
    // A Type argument is a Type position: a value named `Kimi` beside an opened struct `Kimi` asks NoContainer, never stage 4 (v02).
    [InlineData("alias A\ngroup A\n    public struct Kimi\n        public let n: i32 = 1\nfunc take<T>() -> i32 => 0\nfunc f(Kimi: i32) -> i32\n    return take<Kimi>()", "")]
    // A generic adaptation target takes its Type row: a Contract alone is unresolved there (w01), as a missing Contract parent is (t05).
    [InlineData("contract C<T>\n    func m(self: ref/Self) -> i32\nfunc f(x: i32) -> i32\n    let y = x@C<i32>\n    return 0", "UnresolvedBinding_Kd C")]
    [InlineData("contract D: Missing<i32>\n    func k(self: ref/Self) -> i32", "InvalidConstraint_Kd Missing<i32>|InvalidConstraint_Kd contract D")]
    // An effect selector names its declaration and has the bound ancestor it designates as its Type (SPEC 8.11.1; review 3's v-e1, v-e2,
    // v-e5); a selector that names two bound ancestors is rejected (i-e4).
    [InlineData(BoundEffect + "contract Q: P<i32>\n    effect P.value confined", "")]
    [InlineData(BoundEffect + "contract Q: P<i32>\n    effect P<i32>.value confined", "")]
    [InlineData(BoundEffect + "contract Q: P<i32>, P<i64>\n    effect P<i64>.value confined", "")]
    [InlineData(BoundEffect + "contract Q: P<i32>, P<i64>\n    effect P.value confined", "InvalidEffectBound_Kd effect P.value confined")]
    // An explicit generic argument is read for the slot it fills (SPEC 4.4.1, 9.5; review 4's v-u1, i-u2): a spelling whose Type reading
    // is ambiguous (two opened structs `Size`) supplies a length slot by its value, and fills no Type slot.
    [InlineData("alias A\nalias B\ngroup A\n    public struct Size\ngroup B\n    public struct Size\nlet Size: isize = 2\nfunc f<length N>() => ()\nf<Size>()", "")]
    [InlineData("alias A\nalias B\ngroup A\n    public struct Size\ngroup B\n    public struct Size\nlet Size: isize = 2\nfunc f<T>() => ()\nf<Size>()", "NoApplicableOverload_Kd f<Size>()")]
    public void AUseIsResolvedOnceInItsRow(string source, string records)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.Equal(records.Length == 0, c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    // The published lookup is written once: an equal publication (a path's walk evaluating the qualifier again) stands, and a
    // disagreeing one is a broken contract (InternalInvariant), never a silent re-row.
    [Fact]
    public void ADisagreeingPublicationIsABrokenContract()
    {
        var (qualifier, published, selected) = PublishedQualifier();
        var publish = typeof(Binding).GetMethod("PublishUse", BindingFlags.NonPublic | BindingFlags.Static)!;
        publish.Invoke(null, [qualifier, published, selected]);
        var thrown = Assert.Throws<TargetInvocationException>(() => publish.Invoke(null, [qualifier, published with { Role = LookupRole.NoContainer }, selected]));
        Assert.Contains("InternalInvariant", Assert.IsType<InvalidOperationException>(thrown.InnerException).Message);
    }

    // A selected use's selection is its Symbols entry, written once: an equal write stands, and another meaning written over it (a
    // republication's other selection or a binder's) is a broken contract.
    [Fact]
    public void ASelectedUsesSymbolIsWrittenOnce()
    {
        var (qualifier, published, selected) = PublishedQualifier();
        HirTables.WriteSymbol(qualifier, selected);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => HirTables.WriteSymbol(qualifier, null)).Message);
        var publish = typeof(Binding).GetMethod("PublishUse", BindingFlags.NonPublic | BindingFlags.Static)!;
        var thrown = Assert.Throws<TargetInvocationException>(() => publish.Invoke(null, [qualifier, published, null]));
        Assert.Contains("InternalInvariant", Assert.IsType<InvalidOperationException>(thrown.InnerException).Message);
        Assert.Same(selected, HirTables.Symbol(qualifier));
    }

    // A bound Contract qualifier (SPEC 8.6.1, 9.7.1): at each level its Symbols entry is the name's selection, the declaration, and its
    // Type is the bound reference its Type arguments form, which a reader takes the reference from (SpecifiesContract).
    [Fact]
    public void ABoundContractQualifiersReferenceIsItsType()
    {
        var c = CompilationTestHelper.Parse("contract C<E>\n    associate Item\nstruct S\n    Self is C<i32>\n    associate C<i32>.Item is i32\n");
        Assert.True(c.Bind().IsComplete);
        var declaration = HirTables.Symbol(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ContractKoto>().First());
        var qualifier = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single().Left;
        Assert.All([qualifier, KotoTree.Walk(qualifier).OfType<GenericsKoto>().Single()], level =>
        {
            Assert.Same(declaration, HirTables.Symbol(level));
            var type = Assert.IsType<BoundType>(HirTables.TypeOf(level));
            Assert.Equal((BoundTypeKind.Constructed, declaration), (type.Kind, type.Symbol));
            Assert.Equal("i32", Assert.Single(type.Components).Name);
        });

        // No node's Symbols entry is a bound reference (HirTables.WriteSymbol, the column's one writer): writing one is a broken contract.
        var bound = typeof(Binding).GetMethod("BoundContractReference", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(c.Binding, [HirTables.TypeOf(qualifier)]);
        Assert.Contains("InternalInvariant", Assert.Throws<InvalidOperationException>(() => HirTables.WriteSymbol(qualifier.Parent!, Assert.IsType<BindingSymbol>(bound))).Message);
    }

    private static (Koto Qualifier, UseLookup Published, BindingSymbol Selected) PublishedQualifier()
    {
        var c = CompilationTestHelper.Parse("struct N\n    public struct Inner\nfunc f(v: N.Inner) -> i32 => 0");
        Assert.True(c.Bind().IsComplete);
        var qualifier = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single().Left;
        var published = HirTables.LookupOf(qualifier);
        Assert.Equal(new UseLookup(PathEnd.Selected, LookupRole.Any), published);
        return (qualifier, published, Assert.IsType<BindingSymbol>(HirTables.Symbol(qualifier)));
    }
}
