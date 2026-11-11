// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// R7-N1e2-1R (the owner's O-111 (a)): a Type-name use is resolved once, in its position's row, and published on the use
// (HirTables.Lookups); every other reader reads it (SPEC 9.2, 9.5, 9.6, 23.1.2). Each invalid row has a valid counterpart.
// R7-N1e2-2a: the row is SPEC 9.2's Lookup Role, chosen by the use's position alone (Binding.UseRow).
public class UseLookupTest
{
    private const string BoundEffect = "contract P<T>\n    func value(self: ref/Self, x: T) -> T\n";
    private const string CoreX = "struct X\n    public let n: i32 = 1\n";
    private const string NearerContract = "group G\n    contract X\n        func m(self: ref/Self) -> i32\n";
    private const string NearerTake = "contract Take\n    func take(self: ref/Self) -> i32\ngroup G\n    group Take\n        func z() -> i32 => 0\n    contract Q: ::Take\n";
    private const string Projected = "contract C\n    associate Item\nstruct P\n    Self is C\n    associate C.Item is i32\n";
    private const string Taker = "func take<T>(x: T)\n    T is C\n    ()\nfunc use(x: G.S<i32>) => take(x@move)";
    private const string HasItem = "contract HasItem\n    associate Item\n";
    private const string Inherited = HasItem + "open struct Base\n    public struct N\n        Self is HasItem\n        associate HasItem.Item is i32\n";
    private const string Opened = "alias A\nalias B\ncontract HasItem\n    associate Item\ngroup A\n    public group X\n        public func z() -> i32 => 0\n";

    private const string ProjectedP = "contract C<T>\n    associate Item\nstruct P\n    public init() => ()\n    public func make() -> i32 => 1\nstruct S\n    Self is C<i32>\n" +
        "    associate C<i32>.Item is P\n";

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
    // A Type argument is a Type position: a value named `Kimi` beside an opened struct `Kimi` asks Core, never stage 4 (v02).
    [InlineData("alias A\ngroup A\n    public struct Kimi\n        public let n: i32 = 1\nfunc take<T>() -> i32 => 0\nfunc f(Kimi: i32) -> i32\n    return take<Kimi>()", "")]
    // A generic adaptation target takes its Type row: a Contract alone is unresolved there (w01), as a missing Contract parent is (t05).
    [InlineData("contract C<T>\n    func m(self: ref/Self) -> i32\nfunc f(x: i32) -> i32\n    let y = x@C<i32>\n    return 0", "UnresolvedBinding_Kd C")]
    [InlineData("contract D: Missing<i32>\n    func k(self: ref/Self) -> i32", "InvalidConstraint_Kd Missing<i32>|InvalidConstraint_Kd contract D")]
    // An effect selector names its declaration and has the bound ancestor it designates as its Type (SPEC 8.11.1; review 3's v-e1, v-e2,
    // v-e5), below a root-name form too; one without its Type arguments names no reference (EffectBoundDeclarationTest).
    [InlineData(BoundEffect + "contract Q: P<i32>\n    effect ::P<i32>.value confined", "")]
    [InlineData(BoundEffect + "contract Q: P<i32>\n    effect P<i32>.value confined", "")]
    [InlineData(BoundEffect + "contract Q: P<i32>, P<i64>\n    effect P<i64>.value confined", "")]
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

    [Theory]
    // Core (SPEC 9.2): a nearer Contract is not eligible in a Type position, a base clause or a formation's Type (q1, n12, e02, e04),
    // nor is one of two opened declarations (p11) or a pair binder (b02); a Contract or a binder alone is unresolved (p06, e03, b03).
    [InlineData(CoreX + NearerContract + "    func f(v: ref/X) -> i32 => v.n", "")]
    [InlineData(NearerContract + "    func f(v: ref/X) -> i32 => 0", "UnresolvedBinding_Kd X")]
    [InlineData("alias A\nalias B\ngroup A\n    public contract X\n        func m(self: ref/Self) -> i32\ngroup B\n    public struct X\n        public let n: i32 = 1\nfunc f(v: ref/X) -> i32 => v.n", "")]
    [InlineData("public open " + CoreX + NearerContract + "    struct Y : X\n        let k: i32 = 0", "")]
    [InlineData(CoreX + NearerContract + "    contract L\n        associate Item(step) is Copy for X", "")]
    [InlineData(CoreX + NearerContract + "    contract L\n        associate Item(step) for X", "")]
    [InlineData(NearerContract + "    contract L\n        associate Item(step) is Copy for X", "UnresolvedBinding_Kd X")]
    [InlineData("struct s\n    public let n: i32 = 1\nfunc g<s/T>(x: s/T, y: ref/s) -> i32 => y.n", "")]
    [InlineData("func g<s/T>(x: s/T, y: ref/s) -> i32 => 0", "UnresolvedBinding_Kd s")]
    // Qualifier (9.2, 9.5): a pair binder is not eligible, so an outer group is the receiver (n01); the binder alone is unresolved (b01).
    [InlineData("group s\n    public func f() -> i32 => 1\nfunc g<s/T>(x: s/T) -> i32 => s.f()", "")]
    [InlineData("func g<s/T>(x: s/T) -> i32 => s.f()", "UnresolvedBinding_Kd s.f")]
    // Contract Reference (9.2): a Contract parent, a projection's Contract, a conditional conformance's Contract and a parenthesized
    // effect selector pass a nearer group, struct or Type Parameter (p19, p21, p23, n02c, a11, a12, n04) and never reach stage 4, so
    // an opened `Kimi` is selected (n10, n11); a Core declaration alone is not one (p24, p26, n02bx).
    [InlineData("contract P\n    func m(self: ref/Self) -> i32\ngroup G\n    group P\n        func z() -> i32 => 0\n    contract C: P\n        func k(self: ref/Self) -> i32", "")]
    [InlineData("struct S\ncontract C: S\n    func k(self: ref/Self) -> i32", "InvalidConstraint_Kd S|InvalidConstraint_Kd contract C")]
    [InlineData("contract C\n    associate Item\ngroup G\n    group C\n        func z() -> i32 => 0\n    func f<T>(x: T.(C).Item) -> i32\n        T is C\n        return 0", "")]
    [InlineData("contract C\n    associate Item\ngroup G\n    struct C\n    func f<T>(x: T.(C).Item) -> i32\n        T is ::C\n        return 0", "")]
    [InlineData("struct S\nfunc f<T>(x: T.(S).Item) -> i32 => 0", "UnresolvedBinding_Kd T.(S)")]
    [InlineData("contract C\ngroup G\n    struct C\n    public struct S<T>\n        Self is C when T is Copy\n" + Taker, "")]
    [InlineData("group G\n    struct C\n    struct S<T>\n        Self is C when T is Copy", "InvalidConstraint_Kd Self is C|InvalidConstraint_Kd Self is C when T is Copy|InvalidConstraint_Kd struct S|UnresolvedBinding_Kd C")]
    [InlineData("contract C\ngroup G\n    public struct S<C>\n        Self is C when C is Copy\n" + Taker, "")]
    [InlineData("contract P\n    func m(self: ref/Self) -> i32\ncontract C<P>: P\n    func k(self: ref/Self) -> P", "")]
    [InlineData("alias A\ngroup A\n    public contract Kimi\n        func m(self: ref/Self) -> i32\ncontract D: Kimi\n    func k(self: ref/Self) -> i32", "")]
    [InlineData("alias A\ngroup A\n    public contract Kimi\n        associate Item\nfunc f<T>(x: T.(Kimi).Item) -> i32\n    T is Kimi\n    return 0", "")]
    [InlineData(NearerTake + "        effect (Take).take confined", "")]
    [InlineData(NearerTake + "        effect Take.take confined", "InvalidEffectBound_Kd effect Take.take confined")]
    // Requirement (9.2, 8.2.1): never stage 4 (p08), and a nearer struct is the requirement's Type (n08).
    [InlineData("func f<T>(x: ref/T) -> i32\n    T is Kimi\n    return 0", "InvalidConstraint_Kd func f<T>(x: ref/T) -> i32\n    T is Kimi\n    return 0|UnresolvedBinding_Kd Kimi")]
    [InlineData("contract C\n    func m(self: ref/Self) -> i32\ngroup G\n    struct C\n    struct S\n        Self is C\n        func m(self: ref/Self) -> i32 => 0", "UnsatisfiedConstraint_Kd Self is C")]
    // Object View Target (9.2): every Contract is eligible, so the nearer Contract is selected and Type legality rejects it (n06).
    [InlineData(CoreX + NearerContract + "    func f(v: obj/X) -> i32 => 0", "InvalidTypeFormation_Kd X")]
    // A parenthesized name before `.` at a parameter Type forms no member access (the parser stops there), so `(G)` and `(C)` are
    // asked in Core's row, where a group or a Contract alone is unresolved (d01, d02). A projection's parenthesized left is a Contract
    // Reference (9.2, 8.5.2): a struct there is not the projection's base (h01, h02), which the unparenthesized spelling is (h03).
    [InlineData("group G\n    public struct S\n        public let n: i32 = 0\nfunc f(v: (G).S) -> i32 => 0", "UnresolvedBinding_Kd G")]
    [InlineData("contract C\n    associate Item\nfunc f(v: (C).Item) -> i32 => 0", "UnresolvedBinding_Kd C")]
    [InlineData(Projected + "func f(v: (P).(C).Item) -> i32 => v", "UnresolvedBinding_Kd P")]
    [InlineData("contract C\n    associate Item\ncontract P\n    associate Item\ngroup G\n    struct P\n        Self is C\n        associate C.Item is i32\n    func f(v: (P).(C).Item) -> i32 => v", "UnresolvedBinding_Kd (P).(C)")]
    [InlineData(Projected + "func f(v: P.(C).Item) -> i32 => v", "")]
    public void ARoleFollowsItsPosition(string source, string records) => this.AUseIsResolvedOnceInItsRow(source, records);

    // PLAN G724 (SPEC 8.5.2, 6.3.2, 9.6): a path's projection is its walk's end, formed by BindAssociatedProjection. Construction through a
    // projection qualifier, written or short, constructs the normal form (b1, n01); another Type-side member through it is a located
    // Unsupported_Kd at the member (b2, a Type function; r16, Cases); a `.(C)` whose reference fails forms nothing, so its record is the
    // reference's alone (k25); a Name after a projection that names a member Type of the normal form is a located Unsupported_Kd (x08);
    // an unresolved walk records at its failing step (x09: the head, a middle step, a parenthesized head).
    [Theory]
    [InlineData(ProjectedP + "func g() -> P\n    return S.(C<i32>).Item.init()", "")]
    [InlineData(ProjectedP + "func g() -> P\n    return S.Item.init()", "")]
    [InlineData(ProjectedP + "func g() -> i32\n    return S.(C<i32>).Item.make()", "Unsupported_Kd S.(C<i32>).Item.make")]
    [InlineData(
        "contract C<T>\n    associate Item\nenum M\n    A\n    B(i32)\nstruct S\n    Self is C<i32>\n    associate C<i32>.Item is M\nfunc g() -> M\n    return S.(C<i32>).Item.B(1)\n" +
            "func h() -> M\n    return S.Item.A",
        "Unsupported_Kd S.(C<i32>).Item.B|Unsupported_Kd S.Item.A")]
    [InlineData(ProjectedP + "func g() -> P\n    return S.(C).Item.init()", "TypeMismatch_Kd C")]
    [InlineData(
        "contract C<T>\n    associate Item\nstruct P\n    public struct Inner\n        public init() => ()\nstruct S\n    Self is C<i32>\n    associate C<i32>.Item is P\n" +
            "func g(x: S.(C<i32>).Item.Inner) -> i32 => 0",
        "Unsupported_Kd S.(C<i32>).Item.Inner")]
    [InlineData(
        "struct S\n    public struct In\n        public init() => ()\nfunc f(v: Missing.X) -> i32 => 0\nfunc g(v: S.Missing.X) -> i32 => 0\nfunc h(v: (Missing).X) -> i32 => 0",
        "UnresolvedBinding_Kd Missing|UnresolvedBinding_Kd Missing|UnresolvedBinding_Kd S.Missing")]
    public void AProjectionIsItsPathsEnd(string source, string records) => this.AUseIsResolvedOnceInItsRow(source, records);

    // PLAN G701 (SPEC 6.1.5, 8.4, 9.1, 9.2, 9.7): what a written `Self` selects is decided where names resolve. A Contract's `Self` is
    // its Self Type parameter, the conforming-Type binder, so it is no Contract reference at a projection's Contract, a parent or a
    // requirement; a struct's or enum's is the declared Type with all its bindings; and `Self` has no Type parameters, so none remains
    // for a `Self` written with Type arguments (the declaration's own included). It is no generic parameter either: a function
    // requirement's clauses take subjects rooted in its generic parameters (8.4.2). Each invalid row has a valid counterpart.
    [Theory]
    [InlineData("contract C\n    associate E\n    func get(self: ref/Self) -> Self.(Self).E", "UnresolvedBinding_Kd")]
    [InlineData("contract C\n    associate E\n    func get(self: ref/Self) -> Self.(C).E", "")]
    [InlineData("contract B\n    associate E\ncontract C: B\n    func get(self: ref/Self) -> Self.(Self).E", "UnresolvedBinding_Kd")]
    [InlineData("contract B\n    associate E\ncontract C: B\n    func get(self: ref/Self) -> Self.(B).E", "")]
    [InlineData("struct S\n    contract C\n        associate E\n        func get(self: ref/Self) -> Self.(Self).E", "UnresolvedBinding_Kd")]
    [InlineData("struct S\n    contract C\n        associate E\n        func get(self: ref/Self) -> Self.(C).E", "")]
    [InlineData("contract C\n    associate E\n    func get<U>(self: ref/Self, u: U)\n        U is C\n        U.(Self).E is i32", "InvalidConstraint_Kd UnresolvedBinding_Kd")]
    [InlineData("contract C\n    associate E\n    func get<U>(self: ref/Self, u: U)\n        U is C\n        U.(C).E is i32", "")]
    [InlineData("contract C: Self\n    func get(self: ref/Self) -> i32", "InvalidConstraint_Kd")]
    [InlineData("contract B\ncontract A\n    func f(self: ref/Self)\n        Self is B", "InvalidConstraint_Kd")]
    [InlineData("contract A\n    func f(self: ref/Self)\n        Self is Copy", "InvalidConstraint_Kd")]
    [InlineData("contract A\n    func f(self: ref/Self)\n        Self is Self", "InvalidConstraint_Kd")]
    [InlineData("contract A\n    associate E\n    func f<U>(self: ref/Self, u: U)\n        U is Self.E", "")]
    [InlineData("contract A\n    Self is Copy\nstruct S\n    Self is Copy\n    Self is A\n    public var n: i32 = 0", "")]
    [InlineData("contract Put<X>\n    func put<U>(self: uniq/Self, u: U)\n        U is Self<i32>", "InvalidConstraint_Kd UnresolvedBinding_Kd")]
    [InlineData("contract Put<X>\n    func put<U>(self: uniq/Self, u: U)\n        U is Self<X>", "InvalidConstraint_Kd UnresolvedBinding_Kd")]
    [InlineData("contract Put<X>\n    func put<U>(self: uniq/Self, u: U)\n        U is Self", "")]
    [InlineData("contract K<X>\n    associate E\n    func get(self: ref/Self) -> Self.(Self<X>).E", "UnresolvedBinding_Kd")]
    [InlineData("contract K<X>\n    associate E\n    func get(self: ref/Self) -> Self.(K<X>).E", "")]
    [InlineData("contract K<X>\n    func take(self: ref/Self, v: Self<X>)", "UnresolvedBinding_Kd")]
    [InlineData("contract K<X>\n    func take(self: ref/Self, v: Self)", "")]
    [InlineData("struct S\n    public func take(self: ref/Self, v: Self<i32>) => ()", "UnresolvedBinding_Kd")]
    [InlineData("struct Box<T>\n    public func take(self: ref/Self, v: Self<i32>) => ()", "UnresolvedBinding_Kd")]
    [InlineData("struct Box<T>\n    public func take(self: ref/Self, v: Self<T>) => ()", "UnresolvedBinding_Kd")]
    [InlineData("struct Box<T>\n    public func take(self: ref/Self, v: Option<Self<i32>>) => ()", "UnresolvedBinding_Kd")]
    [InlineData("struct Box<T>\n    public func take(self: ref/Self, v: Option<Self>) => ()", "")]
    [InlineData("struct Box<T>\n    public func take<U>(self: ref/Self, u: U)\n        U is Self<i32>\n        return", "InvalidConstraint_Kd UnresolvedBinding_Kd")]
    [InlineData("struct Box<T>\n    public func take<U>(self: ref/Self, u: U)\n        U is Self\n        return", "")]
    [InlineData("enum E<T>\n    A(T)\n    B\n    public func take(self: ref/Self, v: Self<i32>) => ()", "UnresolvedBinding_Kd")]
    [InlineData("enum E<T>\n    A(T)\n    B\n    public func take(self: ref/Self, v: Self) => ()", "")]
    public void SelfSelectsItsContextsType(string source, string codes)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.Equal(codes.Length == 0, c.Bind().IsComplete);
        Assert.Equal(codes, string.Join(" ", c.Binding.Issues.Select(static x => x.Code.ToString()).Distinct().Order()));
    }

    // The published lookup is written once: an equal publication (a path's walk evaluating the qualifier again) stands, and a
    // disagreeing one is a broken contract (InternalInvariant), never a silent re-row.
    [Fact]
    public void ADisagreeingPublicationIsABrokenContract()
    {
        var (qualifier, published, selected) = PublishedQualifier();
        var publish = typeof(Binding).GetMethod("PublishUse", BindingFlags.NonPublic | BindingFlags.Static)!;
        publish.Invoke(null, [qualifier, published, selected]);
        var thrown = Assert.Throws<TargetInvocationException>(() => publish.Invoke(null, [qualifier, PathEnd.Unresolved, selected]));
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
    // PLAN G704 (SPEC 3.1.3, 22.1, 9.5, 18.2.1): `Never` is a Kimi declaration, so a Type position names it by ordinary lookup: a nearer
    // declaration shadows it (dev: the intrinsic won), `Kimi.Never` (stage 4) and `::Kimi.Never` name the declaration (dev:
    // UnresolvedBinding_Kd), `::Kimi` stays unshadowed where `Kimi` is, and an opening alias may open it. Invalid twins follow, each rejected by
    // its own rule; 3.8's Never fitting is why only the struct-to-Never direction is one.
    [Theory]
    [InlineData("struct Never\n    public var v: i32\nfunc h(a: ref/Never) -> i32 => a.v", "clean")]
    [InlineData("group G\n    struct Never\n        public var v: i32\n    func h(a: ref/Never) -> i32 => a.v", "clean")]
    [InlineData("func h(a: ::Kimi.Never) -> () => ()\nfunc g() -> ::Kimi.Never => $abort(\"x\")", "clean")]
    [InlineData("func h(a: Kimi.Never) -> () => ()", "clean")]
    [InlineData("group G\n    struct Kimi\n        var v: i32\n    func h(a: ::Kimi.Never) -> () => ()", "clean")]
    [InlineData("struct Never\n    public var v: i32\nfunc g() -> ::Kimi.Never => $abort(\"x\")\nfunc h(a: ref/Never) -> i32 => a.v", "clean")]
    [InlineData("func g(a: Never) -> () => ()\nfunc h(a: ::Kimi.Never) -> () => g(a@move)", "clean")]
    [InlineData("alias Kimi.Never\nfunc f() -> () => ()", "clean")]
    [InlineData("struct Never\n    public var v: i32\nfunc g(a: ::Kimi.Never) -> () => ()\nfunc h(a: Never) -> () => g(a@move)", "1*NoApplicableOverload_Kd")]
    [InlineData("group G\n    struct Kimi\n        var v: i32\n    func h(a: Kimi.Never) -> () => ()", "1*UnresolvedBinding_Kd")]
    [InlineData("func h(a: ref/::Kimi.Never) -> i32 => a.v", "1*UnresolvedBinding_Kd")]
    [InlineData("func use() -> ()\n    let a = Never()\n    ()", "1*UnresolvedBinding_Kd")]
    [InlineData("func h(a: Never<i32>) -> () => ()", "1*InvalidTypeFormation_Kd")]
    [InlineData("alias N => Kimi.Never\nfunc f() -> () => ()", "1*InvalidTypeFormation_Kd")]
    public void NeverIsAKimiDeclarationFoundByOrdinaryLookup(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

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

    private static (Koto Qualifier, PathEnd Published, BindingSymbol Selected) PublishedQualifier()
    {
        var c = CompilationTestHelper.Parse("struct N\n    public struct Inner\nfunc f(v: N.Inner) -> i32 => 0");
        Assert.True(c.Bind().IsComplete);
        var qualifier = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single().Left;
        var published = HirTables.LookupOf(qualifier);
        Assert.Equal(PathEnd.Selected, published);
        return (qualifier, published, Assert.IsType<BindingSymbol>(HirTables.Symbol(qualifier)));
    }
}
