// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 8.2.3, 8.3.2, 8.5, 8.6.1, 8.9: bound Contract references, projections, requirements and conformances are identified by their
// normalized form under all of a declaration's premises, and a Type identical with a proper part of itself is a premise error.
public partial class TypeIdentityTest
{
    private const string PairStruct = "struct Pair\n    public var a: i32 = 1\n    public init() => ()\n";

    private const string HasItem = "contract HasItem\n    associate Item\nstruct SrcI\n    Self is HasItem\n    associate HasItem.Item is i32\n    public init() => ()\nstruct SrcU\n    Self is HasItem\n    associate HasItem.Item is u8\n    public init() => ()\n";

    private const string Iter = "contract Iter<T>\n    func size(self: ref/Self) -> i32\ncontract Seq<T>: Iter<T>\nfunc use<T>(x: ref/T) -> i32\n    T is Iter<i32>\n    return x.size()\n";

    private const string Tagged = "contract Iter<T>\n    associate Tag\n";

    private const string TagSeq = "contract Iter<T>\n    associate Tag\ncontract Seq<E>: Iter<E>\n";

    private const string Pick = "contract Pick<T>\n    func pick(self: ref/Self, v: T) -> i32\n";

    private const string PickAlone = "contract Pick<T>\n    func pick(self: ref/Self) -> i32\n";

    private const string UsePick = "func use<X, W>(x: ref/X) -> i32\n    X is Pick<W>\n    return x.pick()\n";

    private const string Holders = "contract Holder<A>\n    associate Item\ncontract Sub<A, B>: Holder<A>, Holder<B>\n";

    private const string Shape = "contract P<T>\n    func size(self: ref/Self, v: T) -> i32\ncontract Q<A>: P<A.(HasItem).Item>\n    A is HasItem\n";

    private const string Effect = "contract P<T>\n    func value(self: ref/Self, x: T) -> T\ncontract Safe\n    func run(self: ref/Self) -> i32\n        effect confined\n";

    private const string Run = "    Self is Safe\n    let inner: T\n    public func run(self: ref/Self) -> i32\n        let x: i32 = 7\n        return self.inner.value(x)\n";

    private const string Base = "contract Base\n    func size(self: ref/Self) -> i32\ncontract Sub: Base\nfunc use<X>(x: ref/X) -> i32\n    X is Base\n    return x.size()\n";

    private const string Nest = "struct Outer<A>\n    public struct Inner\n        public init() => ()\n";

    private const string Converge = "contract C<E>\ncontract Q<U>: C<i32>, C<U>\nfunc need<T>(x: ref/T) -> i32\n    T is C<i32>\n    return 1\n";

    private const string Agree = "contract C<E>\n    associate Item\ncontract Q<U>: C<i32>, C<U>\nstruct Gx<A>\n    Self is Q<A>\n";

    private const string UseItem = "    public init() => ()\nfunc f(a: ref/Gx<i32>) -> ()\n    let v: Gx<i32>.(C<i32>).Item = 1\n    return\n";

    private const string Merged = "struct B<S, R>\n    S is HasItem\n    R is HasItem\n    S.(HasItem).Item is i32\n    R.(HasItem).Item is i32\n";

    private const string MergedWhen = "struct B<S, R>\n    S is HasItem\n    R is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool\n    Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item\n";

    private const string UseTag = "func f(a: ref/B<SrcI, SrcI>) -> ()\n    let t: B<SrcI, SrcI>.(Iter<i32>).Tag = true\n    return\n";

    private const string Two = "contract Q<U, V>: Iter<U>, Iter<V>\n";

    private const string Direct = "contract Q2<U>: Iter<U>\n";

    private const string Key = "struct Name\n    Self is Copy\n    public let id: isize\n    public init(id: isize) => self.id = id\n";

    private const string ShortItem = "contract P<T>\n    associate Item\ncontract R\n    associate X\n";

    private const string Box = "struct Box<A>\n    Self is HasItem\n    associate HasItem.Item is A\n    public init() => ()\n";

    private const string Opt = "contract Marker\nstruct Opt<A>\n    Self is HasItem when A is Marker\n        associate HasItem.Item is A\n    public init() => ()\n";

    // A row is (unit, form, program, records). Its verdict comes from SPEC, never from a build: records is empty for a valid program, which
    // also generates, and otherwise lists each expected Error as `Code: source line` (see CompilationTestHelper.AssertRecordsAtLines). unit
    // guard: the row runs today and must keep passing. Any other unit is the plan's unit that makes the row pass: the row is Explicit
    // until that unit lands and removes the marker. `dotnet xUnitTest.dll -class <class> -explicit on` runs all of them.
    [Theory]
    // SPEC 8.2.3, 8.3.1: identity is closed under replacement inside Contract arguments and propositions compare normalized Types, so a premise's
    // reference proves the reference it is identical with, also for a subject identical with a constructed Type (s5). Without the identity the
    // requirement is Unknown (the twins).
    [InlineData("guard", "s1", HasItem + Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<U.(HasItem).Item>\n    return use(x)\n", "")]
    [InlineData("guard", "s1n", HasItem + Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    return use(x)\n", "UnprovenConstraint_Kd: return use(x)")]
    [InlineData("guard", "s2", HasItem + Iter + "func useU<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    return x.size()\nfunc g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<i32>\n    return useU(x, u)\n", "")]
    [InlineData("guard", "s2n", HasItem + Iter + "func useU<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    return x.size()\nfunc g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    T is Iter<i32>\n    return useU(x, u)\n", "UnprovenConstraint_Kd: return useU(x, u)")]
    [InlineData("guard", "s3", HasItem + Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Seq<U.(HasItem).Item>\n    return use(x)\n", "")]
    [InlineData("guard", "s3n", HasItem + Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is HasItem\n    T is Seq<U.(HasItem).Item>\n    return use(x)\n", "UnprovenConstraint_Kd: return use(x)")]
    [InlineData("guard", "s4", Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is i32\n    T is Iter<U>\n    return use(x)\n", "")]
    [InlineData("guard", "s4n", Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is u8\n    T is Iter<U>\n    return use(x)\n", "UnprovenConstraint_Kd: return use(x)")]
    [InlineData("guard", "s5", HasItem + Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is SrcI\n    T is Iter<U.(HasItem).Item>\n    return use(x)\n", "")]
    [InlineData("guard", "s5n", HasItem + Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is SrcU\n    T is Iter<U.(HasItem).Item>\n    return use(x)\n", "UnprovenConstraint_Kd: return use(x)")]
    [InlineData("guard", "s5s", HasItem + Iter + "func g<T, U>(x: ref/T, u: ref/U) -> i32\n    U is SrcI\n    T is Seq<U.(HasItem).Item>\n    return use(x)\n", "")]

    // SPEC 8.5.2, 8.9, 8.9.2: an associated Type is identified by its declaration and normalized bindings, so equal paths count once and the short name
    // is unique; paths with different bindings stay distinct and the short name is ambiguous.
    [InlineData("guard", "fa1", HasItem + Tagged + "func f<T, U>(x: ref/T, t: T.Tag) -> ()\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<i32>\n    T is Iter<U.(HasItem).Item>\n    ()\n", "")]
    [InlineData("guard", "fa2", HasItem + Tagged + "func f<T, U>(x: ref/T, t: T.Tag) -> ()\n    U is HasItem\n    T is Iter<i32>\n    T is Iter<U.(HasItem).Item>\n    ()\n", "AmbiguousBinding_Kd: func f<T, U>(x: ref/T, t: T.Tag) -> ()")]
    [InlineData("guard", "fa4", Tagged + "func f<T>(x: ref/T, t: T.Tag) -> ()\n    T is Iter<i32>\n    T is Iter<i32>\n    ()\n", "")]
    [InlineData("guard", "fa5", HasItem + TagSeq + "func f<T, U>(x: ref/T, t: T.Tag) -> ()\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Seq<U.(HasItem).Item>\n    T is Iter<i32>\n    ()\n", "")]
    [InlineData("guard", "fa6", HasItem + TagSeq + "func f<T, U>(x: ref/T, t: T.Tag) -> ()\n    U is HasItem\n    T is Seq<U.(HasItem).Item>\n    T is Iter<i32>\n    ()\n", "AmbiguousBinding_Kd: func f<T, U>(x: ref/T, t: T.Tag) -> ()")]
    [InlineData("guard", "ar15", HasItem + "func f<T, U>(a: T, b: i32) -> T.Output\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Addable<i32>\n    T is Addable<U.(HasItem).Item>\n    return a + b\nlet sum = f<i32, SrcI>(1, 2)\nrequire sum == 3 else => $abort(\"add\")\n", "")]
    [InlineData("guard", "ar16", HasItem + "func f<T, U>(a: T, b: i32) -> T.Output\n    U is HasItem\n    T is Addable<i32>\n    T is Addable<U.(HasItem).Item>\n    return a + b\n", "AmbiguousBinding_Kd: func f<T, U>(a: T, b: i32) -> T.Output")]
    [InlineData("guard", "an1", HasItem + Holders + "func f<T, U>(x: ref/T, t: T.Item) -> ()\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Sub<i32, U.(HasItem).Item>\n    ()\n", "")]
    [InlineData("guard", "an1n", HasItem + Holders + "func f<T, U>(x: ref/T, t: T.Item) -> ()\n    U is HasItem\n    T is Sub<i32, U.(HasItem).Item>\n    ()\n", "AmbiguousBinding_Kd: func f<T, U>(x: ref/T, t: T.Item) -> ()")]
    [InlineData("C3", "an1c", HasItem + Holders + "func f<T>(x: ref/T, t: T.Item) -> ()\n    T is Sub<i32, SrcI.(HasItem).Item>\n    ()\n", "")]
    [InlineData("guard", "an1cu", HasItem + Holders + "func f<T>(x: ref/T, t: T.Item) -> ()\n    T is Sub<i32, SrcU.(HasItem).Item>\n    ()\n", "AmbiguousBinding_Kd: func f<T>(x: ref/T, t: T.Item) -> ()")]

    // SPEC 8.6.4, 8.9.2, 10.2.1, 12.3.1: equal bound references give one requirement. Distinct ones are distinct candidates: a literal adopts no
    // abstract Type, so rq1n has one candidate; it fits two integer Types (rq1u), and a call with no argument matches both (rq2n).
    [InlineData("guard", "rq1", HasItem + Pick + "func f<T, U>(x: ref/T) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    return x.pick(1)\n", "")]
    [InlineData("guard", "rq1n", HasItem + Pick + "func f<T, U>(x: ref/T) -> i32\n    U is HasItem\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    return x.pick(1)\n", "")]
    [InlineData("guard", "rq1u", HasItem + Pick + "func f<T, U>(x: ref/T) -> i32\n    U is HasItem\n    U.(HasItem).Item is u8\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    return x.pick(1)\n", "AmbiguousBinding_Kd: return x.pick(1)")]
    [InlineData("guard", "rq2", HasItem + PickAlone + "func f<T, U>(x: ref/T) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    return x.pick()\n", "")]
    [InlineData("guard", "rq2n", HasItem + PickAlone + "func f<T, U>(x: ref/T) -> i32\n    U is HasItem\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    return x.pick()\n", "AmbiguousBinding_Kd: return x.pick()")]
    [InlineData("guard", "ad1", HasItem + "contract Bump<T>\n    func bump(self: ref/Self, v: T) -> i32\nstruct Bumper\n    Self is Bump<i32>\n    public init() => ()\n    public func bump(self: ref/Self, v: i32) -> i32 => v\nfunc f<T, U>(x: ref/T, v: U.(HasItem).Item) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Bump<i32>\n    T is Bump<U.(HasItem).Item>\n    return x.bump(v)\nlet n = Bumper.init()\nrequire f<Bumper, SrcI>(n, 4) == 4 else => $abort(\"bump\")\n", "")]

    // SPEC 8.2.3: an available completed binding is a ground of identity, also through a refinement's ancestor (pr1s, s5s).
    [InlineData("guard", "pr1", HasItem + Iter + "func g<T>(x: ref/T) -> i32\n    T is Iter<SrcI.(HasItem).Item>\n    return use(x)\n", "")]
    [InlineData("guard", "pr1u", HasItem + Iter + "func g<T>(x: ref/T) -> i32\n    T is Iter<SrcU.(HasItem).Item>\n    return use(x)\n", "UnprovenConstraint_Kd: return use(x)")]
    [InlineData("guard", "pr1s", HasItem + Iter + "func g<T>(x: ref/T) -> i32\n    T is Seq<SrcI.(HasItem).Item>\n    return use(x)\n", "")]

    // SPEC 8.2.3: premises are judged together, never in written order.
    [InlineData("guard", "o1", HasItem + Pick + "func f<T, U>(x: ref/T) -> i32\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    U.(HasItem).Item is i32\n    U is HasItem\n    return x.pick(1)\n", "")]
    [InlineData("guard", "o1n", HasItem + Pick + "func f<T, U>(x: ref/T) -> i32\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    U.(HasItem).Item is u8\n    U is HasItem\n    return x.pick(1)\n", "AmbiguousBinding_Kd: return x.pick(1)")]
    [InlineData("guard", "o2", HasItem + Iter + "func g<T, U>(x: ref/T) -> i32\n    T is Iter<U.(HasItem).Item>\n    U.(HasItem).Item is i32\n    U is HasItem\n    return use(x)\n", "")]
    [InlineData("guard", "o2n", HasItem + Iter + "func g<T, U>(x: ref/T) -> i32\n    T is Iter<U.(HasItem).Item>\n    U is HasItem\n    return use(x)\n", "UnprovenConstraint_Kd: return use(x)")]

    // SPEC 7.5: a member's clause on its Type's parameter is a conditional member's premise; a literal adopts no abstract Type (oi1n).
    [InlineData("guard", "oi1", HasItem + Pick + "struct W<T, U>\n    U is HasItem\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    let t: T\n    public func run(self: ref/Self) -> i32\n        U.(HasItem).Item is i32\n        return self.t.pick(1)\n", "")]
    [InlineData("guard", "oi1n", HasItem + Pick + "struct W<T, U>\n    U is HasItem\n    T is Pick<i32>\n    T is Pick<U.(HasItem).Item>\n    let t: T\n    public func run(self: ref/Self) -> i32\n        return self.t.pick(1)\n", "")]
    [InlineData("guard", "oi2", HasItem + Iter + "group G\n    public func use<T>(x: ref/T) -> i32\n        T is Iter<i32>\n        return x.size()\nstruct W<T, U>\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    let t: T\n    public func run(self: ref/Self) -> i32\n        U.(HasItem).Item is i32\n        return G.use(self.t)\n", "")]
    [InlineData("guard", "oi2n", HasItem + Iter + "struct W<T, U>\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    let t: T\n    public func run(self: ref/Self) -> i32\n        return use(self.t)\n", "*: return use(self.t)")]

    // SPEC 8.2.3: a chain of premises reaches a Contract argument; 8.3.2 Closed world: without it a distinct parameter is Unknown, never Refuted.
    [InlineData("guard", "pi1", PickAlone + "struct B<S>\n    Self is Pick<S>\n    public init() => ()\n    public func pick(self: ref/Self) -> i32 => 1\n" + UsePick + "func f<U, V, W>(b: ref/B<U>) -> i32\n    V is U\n    V is W\n    return use<B<U>, W>(b)\n", "")]
    [InlineData("guard", "pi1n", PickAlone + "struct B<S>\n    Self is Pick<S>\n    public init() => ()\n    public func pick(self: ref/Self) -> i32 => 1\n" + UsePick + "func f<U, V, W>(b: ref/B<U>) -> i32\n    V is U\n    return use<B<U>, W>(b)\n", "UnprovenConstraint_Kd: return use<B<U>, W>(b)")]
    [InlineData("guard", "pi2", PickAlone + UsePick + "func f<T, U, V>(x: ref/T) -> i32\n    V is U\n    T is Pick<V>\n    return use<T, U>(x)\n", "")]
    [InlineData("guard", "pi3", PickAlone + UsePick + "func f<T, U, V, W>(x: ref/T) -> i32\n    V is U\n    V is W\n    T is Pick<U>\n    return use<T, W>(x)\n", "")]
    [InlineData("guard", "pi3n", PickAlone + UsePick + "func f<T, U, V, W>(x: ref/T) -> i32\n    V is U\n    T is Pick<U>\n    return use<T, W>(x)\n", "UnprovenConstraint_Kd: return use<T, W>(x)")]
    [InlineData("guard", "pi4", Pick + "func f<T, U, V, W>(x: ref/T, w: W) -> i32\n    V is U\n    V is W\n    T is Pick<U>\n    T is Pick<W>\n    return x.pick(w@move)\n", "")]

    // SPEC 8.2.3 Projections, 8.5.2: equal projections of identical subjects and Contract arguments are identical. A qualifier needs conformance
    // evidence (k1n; its `return x` fails on its own, SPEC 23.1.2: the premise alone would not make the Tag u8).
    [InlineData("guard", "k1", HasItem + Tagged + "func f<T, U>(x: T.(Iter<i32>).Tag) -> u8\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is u8\n    return x\n", "")]
    [InlineData("guard", "k1n", HasItem + Tagged + "func f<T, U>(x: T.(Iter<i32>).Tag) -> u8\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is u8\n    return x\n", "UnprovenConstraint_Kd: func f<T, U>(x: T.(Iter<i32>).Tag) -> u8 | TypeMismatch_Kd: return x")]
    [InlineData("guard", "k1c", HasItem + Tagged + "func f<T>(x: T.(Iter<SrcI.(HasItem).Item>).Tag) -> u8\n    T is Iter<SrcI.(HasItem).Item>\n    T.(Iter<SrcI.(HasItem).Item>).Tag is u8\n    return x\n", "")]
    [InlineData("guard", "k2", HasItem + Tagged + "func f<T, U>(x: T.(Iter<U.(HasItem).Item>).Tag) -> u8\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is u8\n    return x\n", "")]
    [InlineData("guard", "k2d", HasItem + Tagged + "func f<T, U>(x: T.(Iter<U.(HasItem).Item>).Tag) -> u8\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is u8\n    return x\n", "")]
    [InlineData("guard", "k3", HasItem + Tagged + Base + "func f<T, U>(x: ref/T.(Iter<U.(HasItem).Item>).Tag) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is Sub\n    return use(x)\n", "")]
    [InlineData("guard", "k3d", HasItem + Tagged + Base + "func f<T, U>(x: ref/T.(Iter<U.(HasItem).Item>).Tag) -> i32\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is Sub\n    return use(x)\n", "")]
    [InlineData("guard", "k4", HasItem + Tagged + Base + "func f<T, U>(x: ref/T.(Iter<U.(HasItem).Item>).Tag) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is Base\n    return x.size()\n", "")]
    [InlineData("c2-5c", "k4c", HasItem + Tagged + Base + "func f<T>(x: ref/T.(Iter<SrcI.(HasItem).Item>).Tag) -> i32\n    T is Iter<SrcI.(HasItem).Item>\n    T.(Iter<SrcI.(HasItem).Item>).Tag is Base\n    return x.size()\n", "")]
    [InlineData("guard", "k4d", HasItem + Tagged + Base + "func f<T, U>(x: ref/T.(Iter<U.(HasItem).Item>).Tag) -> i32\n    U is HasItem\n    T is Iter<U.(HasItem).Item>\n    T.(Iter<U.(HasItem).Item>).Tag is Base\n    return x.size()\n", "")]

    // SPEC 8.2.3, 8.3.1, 8.3.2, 8.5.2, 8.8.2 (R7-C2c2 c2-5c): a fact's Contract argument is read by its completed binding wherever a
    // proof reads it, whatever other premises the declaration has (k4c-w: an unrelated identity premise); without the binding (a
    // distinct Type, a distinct parameter, a refinement in the wrong direction or an unavailable conditional binding) the requirement
    // is Unknown.
    [InlineData("guard", "k4c-w", HasItem + Tagged + Base + "func f<T, W>(x: ref/T.(Iter<SrcI.(HasItem).Item>).Tag) -> i32\n    T is Iter<SrcI.(HasItem).Item>\n    T.(Iter<SrcI.(HasItem).Item>).Tag is Base\n    W is i32\n    return x.size()\n", "")]
    [InlineData("guard", "k4cu", HasItem + Tagged + Base + "func f<T>(x: ref/T.(Iter<SrcI.(HasItem).Item>).Tag) -> i32\n    T is Iter<SrcU.(HasItem).Item>\n    T.(Iter<SrcU.(HasItem).Item>).Tag is Base\n    return x.size()\n", "UnprovenConstraint_Kd: func f<T>(x: ref/T.(Iter<SrcI.(HasItem).Item>).Tag) -> i32 | UnresolvedBinding_Kd: return x.size()")]
    [InlineData("c2-5c", "kq1", HasItem + TagSeq + "func f<T>(x: ref/T, t: T.(Iter<i32>).Tag) -> ()\n    T is Seq<SrcI.(HasItem).Item>\n    ()\n", "")]
    [InlineData("guard", "kq1n", HasItem + TagSeq + "func f<T>(x: ref/T, t: T.(Iter<i32>).Tag) -> ()\n    T is Seq<SrcU.(HasItem).Item>\n    ()\n", "UnprovenConstraint_Kd: func f<T>(x: ref/T, t: T.(Iter<i32>).Tag) -> ()")]
    [InlineData("guard", "kq1r", HasItem + TagSeq + "func f<T>(x: ref/T, t: T.(Seq<i32>).Tag) -> ()\n    T is Iter<SrcI.(HasItem).Item>\n    ()\n", "UnprovenConstraint_Kd: func f<T>(x: ref/T, t: T.(Seq<i32>).Tag) -> ()")]
    [InlineData("c2-5c", "kq2", HasItem + Box + Tagged + "func f<T, U>(x: ref/T, t: T.(Iter<U>).Tag) -> ()\n    T is Iter<Box<U>.(HasItem).Item>\n    ()\n", "")]
    [InlineData("guard", "kq2n", HasItem + Box + Tagged + "func f<T, U>(x: ref/T, t: T.(Iter<U>).Tag) -> ()\n    T is Iter<Box<i64>.(HasItem).Item>\n    ()\n", "UnprovenConstraint_Kd: func f<T, U>(x: ref/T, t: T.(Iter<U>).Tag) -> ()")]
    [InlineData("c2-5c", "kq3", HasItem + Opt + Tagged + "func f<T, V>(x: ref/T, t: T.(Iter<V>).Tag) -> ()\n    V is Marker\n    T is Iter<Opt<V>.(HasItem).Item>\n    ()\n", "")]
    [InlineData("guard", "kq3n", HasItem + Opt + Tagged + "func f<T, V>(x: ref/T, t: T.(Iter<V>).Tag) -> ()\n    T is Iter<Opt<V>.(HasItem).Item>\n    ()\n", "UnprovenConstraint_Kd: func f<T, V>(x: ref/T, t: T.(Iter<V>).Tag) -> () | UnprovenConstraint_Kd: T is Iter<Opt<V>.(HasItem).Item>")]

    // SPEC 8.2.3, 8.5.2 (R7-C2c2 c2-5c): a qualifier's demand inside a premise clause is read by its completed binding too. SPEC 23.1.2
    // (F's CC-33, CC-34): one unproven demand in a premise clause is one record at the clause; G-decl's rows (k4mu, k4ms, k4mc) stay
    // Explicit while a second record of the same demand stands at the whole declaration (ValidateDeclarationProjectionInputs). kq3n above
    // guards its labelled lines: the helper groups records by line, so its header line hides that second record.
    [InlineData("c2-5c", "k4m", HasItem + Tagged + Base + "func f<T>(x: ref/T) -> i32\n    T is Iter<i32>\n    T.(Iter<SrcI.(HasItem).Item>).Tag is Base\n    return 0\n", "")]
    [InlineData("G-decl", "k4mu", HasItem + Tagged + Base + "func f<T>(x: ref/T) -> i32\n    T is Iter<u8>\n    T.(Iter<SrcI.(HasItem).Item>).Tag is Base\n    return 0\n", "UnprovenConstraint_Kd: T.(Iter<SrcI.(HasItem).Item>).Tag is Base", Explicit = true)]
    [InlineData("G-decl", "k4ms", HasItem + Tagged + Base + "func f<T>(x: ref/T) -> i32\n    T is Iter<i32>\n    T.(Iter<SrcU.(HasItem).Item>).Tag is Base\n    return 0\n", "UnprovenConstraint_Kd: T.(Iter<SrcU.(HasItem).Item>).Tag is Base", Explicit = true)]
    [InlineData("G-decl", "k4mc", HasItem + Opt + Tagged + Base + "func f<T, V>(x: ref/T) -> i32\n    T is Iter<V>\n    T.(Iter<Opt<V>.(HasItem).Item>).Tag is Base\n    return 0\n", "UnprovenConstraint_Kd: T.(Iter<Opt<V>.(HasItem).Item>).Tag is Base", Explicit = true)]
    // SPEC 8.2.3 (R7-C2c2 c2-5a; c2-5c's kq1x): `T is Seq<SrcI.(HasItem).Item>` supplies Iter<i32> through the completed binding, so
    // `T is not Iter<i32>` is contradictory evidence, the function's premise error.
    [InlineData("guard", "kq1x", HasItem + TagSeq + "func f<T>(x: ref/T) -> ()\n    T is Seq<SrcI.(HasItem).Item>\n    T is not Iter<i32>\n    ()\n", "InvalidConstraint_Kd: func f<T>(x: ref/T) -> ()")]

    // SPEC 8.9, 12.3.1: a refinement substitutes its bindings, so the requirement takes the substituted Type.
    [InlineData("guard", "sh1", HasItem + Shape + "func g<T>(x: ref/T) -> i32\n    T is Q<SrcI>\n    return x.size(1)\n", "")]
    [InlineData("guard", "sh1n", HasItem + Shape + "func g<T, U>(x: ref/T) -> i32\n    U is HasItem\n    T is Q<U>\n    return x.size(1)\n", "*: return x.size(1)")]
    [InlineData("guard", "sh1u", HasItem + Shape + "func g<T>(x: ref/T) -> i32\n    T is Q<SrcU>\n    let v: i32 = 1\n    return x.size(v)\n", "*: return x.size(v)")]
    [InlineData("guard", "sh3", HasItem + Shape + "func g<T, U>(x: ref/T) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Q<U>\n    return x.size(1)\n", "")]

    // SPEC 8.11.3, DIAGNOSTICS Bound violations: a requirement call uses the bounds of the Contracts its premises prove; without one the conformance is
    // rejected at its Self clause.
    [InlineData("guard", "ef1", HasItem + Effect + "contract Q<T>: P<T>\n    effect (P<T>).value confined\nstruct Wrapper<T, U>\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Q<U.(HasItem).Item>\n" + Run, "")]
    [InlineData("guard", "ef1c", Effect + "contract Q<T>: P<T>\n    effect (P<T>).value confined\nstruct Wrapper<T>\n    T is Q<i32>\n" + Run, "")]
    [InlineData("guard", "ef1n", HasItem + Effect + "contract Q<T>: P<T>\n    effect (P<T>).value confined\nstruct Wrapper<T, U>\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is P<U.(HasItem).Item>\n" + Run, "IncompatibleContractImplementation_Kd: Self is Safe")]
    [InlineData("guard", "ef2", HasItem + Effect + "contract Q<T>: P<T>\n    effect (P<T>).value confined\nstruct Wrapper<T>\n    T is Q<SrcI.(HasItem).Item>\n" + Run, "")]
    [InlineData("guard", "ef3", HasItem + Effect + "contract Q<A>: P<A.(HasItem).Item>\n    A is HasItem\n    effect (P<A.(HasItem).Item>).value confined\nstruct Wrapper<T>\n    T is Q<SrcI>\n" + Run, "")]
    [InlineData("guard", "ef3n", HasItem + Effect + "contract Q<A>: P<A.(HasItem).Item>\n    A is HasItem\nstruct Wrapper<T>\n    T is Q<SrcI>\n" + Run, "IncompatibleContractImplementation_Kd: Self is Safe")]

    // SPEC 8.2.3, 8.6.1 (R7-C2c2 c2-5c, F's CC-35): a completed binding is a ground of identity with no premise, so a requirement reached
    // through `P<SrcI.(HasItem).Item>` and `P<i32>` is one candidate, whatever other premises the declaration has (-w: an unrelated
    // identity premise) and whether the references are written or inherited (sh1-p: P written; sh1-2: both written).
    [InlineData("c2-5c", "ef2-w", HasItem + Effect + "contract Q<T>: P<T>\n    effect (P<T>).value confined\nstruct Wrapper<T, W>\n    T is Q<SrcI.(HasItem).Item>\n    W is i32\n" + Run, "")]
    [InlineData("c2-5c", "sh1-w", HasItem + Shape + "func g<T, W>(x: ref/T) -> i32\n    T is Q<SrcI>\n    W is i32\n    return x.size(1)\n", "")]
    [InlineData("guard", "sh1-p", HasItem + Shape + "func g<T>(x: ref/T) -> i32\n    T is P<SrcI.(HasItem).Item>\n    return x.size(1)\n", "")]
    [InlineData("c2-5c", "sh1-pw", HasItem + Shape + "func g<T, W>(x: ref/T) -> i32\n    T is P<SrcI.(HasItem).Item>\n    W is i32\n    return x.size(1)\n", "")]
    [InlineData("c2-5c", "sh1-2", HasItem + Shape + "func g<T>(x: ref/T) -> i32\n    T is P<SrcI.(HasItem).Item>\n    T is P<i32>\n    return x.size(1)\n", "")]

    // SPEC 8.2.3, 8.6.1, 8.6.4 (R7-C2c2 c2-5c, F's CC-36 C2): the same ground through a deeper inherited chain (dA-1: R<SrcI>, Q<SrcI>,
    // P<SrcI.(HasItem).Item>), a constructed subject (dA-3: Q<Box<i32>>, P<Box<i32>.(HasItem).Item>), a premise's class in either
    // premise order (cB-1, cB-1r) and a premise beside a completed binding (cB-2: three spellings of P<i32>). P<u8> beside P<i32> stays
    // two candidates the literal fits (-n).
    [InlineData("c2-5c", "dA-1", HasItem + Shape + "contract R<B>: Q<B>\n    B is HasItem\nfunc g<T>(x: ref/T) -> i32\n    T is R<SrcI>\n    T is P<i32>\n    return x.size(1)\n", "")]
    [InlineData("guard", "dA-n", HasItem + Shape + "contract R<B>: Q<B>\n    B is HasItem\nfunc g<T>(x: ref/T) -> i32\n    T is R<SrcU>\n    T is P<i32>\n    return x.size(1)\n", "AmbiguousBinding_Kd: return x.size(1)")]
    [InlineData("c2-5c", "dA-3", HasItem + Shape + Box + "contract R2<B>: Q<Box<B>>\nfunc g<T>(x: ref/T) -> i32\n    T is R2<i32>\n    T is P<i32>\n    return x.size(1)\n", "")]
    [InlineData("c2-5c", "cB-1", HasItem + Shape + "func g<T, U>(x: ref/T) -> i32\n    U is SrcI\n    T is P<U.(HasItem).Item>\n    T is P<i32>\n    return x.size(1)\n", "")]
    [InlineData("c2-5c", "cB-1r", HasItem + Shape + "func g<T, U>(x: ref/T) -> i32\n    U is SrcI\n    T is P<i32>\n    T is P<U.(HasItem).Item>\n    return x.size(1)\n", "")]
    [InlineData("guard", "cB-n", HasItem + Shape + "func g<T, U>(x: ref/T) -> i32\n    U is SrcU\n    T is P<U.(HasItem).Item>\n    T is P<i32>\n    return x.size(1)\n", "AmbiguousBinding_Kd: return x.size(1)")]
    [InlineData("c2-5c", "cB-2", HasItem + Shape + "func g<T, U>(x: ref/T) -> i32\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is P<U.(HasItem).Item>\n    T is Q<SrcI>\n    return x.size(1)\n", "")]
    [InlineData("guard", "cB-2n", HasItem + Shape + "func g<T, U>(x: ref/T) -> i32\n    U is HasItem\n    U.(HasItem).Item is u8\n    T is P<U.(HasItem).Item>\n    T is Q<SrcI>\n    return x.size(1)\n", "AmbiguousBinding_Kd: return x.size(1)")]

    // SPEC 8.5.1, 8.5.5, 8.6.1: a specification's qualifier is a direct conformance, normalized; a Tag left unspecified and uninferred fails at the
    // conformance on its own.
    [InlineData("C3", "sp2", HasItem + Tagged + "struct B<S>\n    S is HasItem\n    S.(HasItem).Item is i32\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\nfunc f(a: ref/B<SrcI>) -> ()\n    let t: B<SrcI>.(Iter<i32>).Tag = true\n    return\n", "")]
    [InlineData("guard", "sp2n", HasItem + Tagged + "struct B<S>\n    S is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\n", "AssociatedTypeInferenceFailed_Kd: Self is Iter<S.(HasItem).Item> | InvalidAssociatedType_Kd: associate Iter<i32>.Tag is bool")]
    [InlineData("guard", "sp2u", HasItem + Tagged + "struct B<S>\n    S is HasItem\n    S.(HasItem).Item is i32\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<u8>.Tag is bool\n", "AssociatedTypeInferenceFailed_Kd: Self is Iter<S.(HasItem).Item> | InvalidAssociatedType_Kd: associate Iter<u8>.Tag is bool")]
    [InlineData("C3", "sp3", HasItem + TagSeq + "struct B<S>\n    S is HasItem\n    S.(HasItem).Item is i32\n    Self is Seq<S.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\nfunc f(a: ref/B<SrcI>) -> ()\n    let t: B<SrcI>.(Iter<i32>).Tag = true\n    return\n", "")]
    [InlineData("guard", "sp3u", HasItem + TagSeq + "struct B<S>\n    S is HasItem\n    S.(HasItem).Item is i32\n    Self is Seq<S.(HasItem).Item>\n    associate Iter<u8>.Tag is bool\n", "AssociatedTypeInferenceFailed_Kd: Self is Seq<S.(HasItem).Item> | InvalidAssociatedType_Kd: associate Iter<u8>.Tag is bool")]

    // SPEC 8.6.1, 8.9.2 (PLAN G798): conformances naming one bound reference at an instance are one conformance, explicit or implied by
    // refinement, through one direct conformance or two; two direct conformances that collide are an error at the later clause (8.9.1).
    [InlineData("guard", "cv1", Converge + "struct Gx<A>\n    Self is Q<A>\n    public init() => ()\nlet g = Gx<i32>.init()\nlet n = need(g@ref)\n", "")]
    [InlineData("guard", "cv2", Converge + "struct G<A>\n    Self is Q<A>\n    Self is C<i32>\n    public init() => ()\nlet g = G<i32>.init()\nlet n = need(g@ref)\n", "")]
    [InlineData("guard", "cv3", "contract C<E>\ncontract Q1<U>: C<U>\ncontract Q2: C<i32>\nfunc need<T>(x: ref/T) -> i32\n    T is C<i32>\n    return 1\nstruct H<A>\n    Self is Q1<A>\n    Self is Q2\n    public init() => ()\nlet h = H<i32>.init()\nlet n = need(h@ref)\n", "")]
    [InlineData("guard", "cv4", "contract C<E>\nfunc need<T>(x: ref/T) -> i32\n    T is C<i32>\n    return 1\nstruct M<S>\n    Self is C<S>\n    Self is C<i32>\n    public init() => ()\nlet m = M<i32>.init()\nlet n = need(m@ref)\n", "DuplicateBinding_Kd: Self is C<i32> | UnprovenConstraint_Kd: let n = need(m@ref)")]
    [InlineData("guard", "cv5", "contract C<E>\ncontract Q<U, V>: C<U>, C<V>\nfunc need<T>(x: ref/T) -> i32\n    T is C<i32>\n    return 1\nstruct G<A, B>\n    Self is Q<A, B>\n    public init() => ()\nlet g = G<i32, i32>.init()\nlet n = need(g@ref)\n", "")]

    // SPEC 8.9.2: one conformance's paths at an instance agree, so a use selects it through any of them; a disagreement is the declaration's error.
    [InlineData("C3", "cv6", Agree + "    associate C<i32>.Item is i32\n    associate C<A>.Item is i32\n" + UseItem, "")]
    [InlineData("guard", "cv6n", Agree + "    associate C<i32>.Item is i32\n    associate C<A>.Item is i64\n" + UseItem, "IncompatibleContractImplementation_Kd: Self is Q<A> | InvalidConstraint_Kd: let v: Gx<i32>.(C<i32>).Item = 1")]

    // SPEC 8.6.1, 8.9.2, 8.5.1 (PLAN G1300): a specification whose qualifier names one conformance through several members (merging paths
    // under the premises) specifies the set's one associated Type, whatever member it names and whatever the members' order, and an
    // unqualified name counts the set once (paths with equal bindings count once); a use reads it through any member. The set's
    // specifications must agree: a disagreement is the conformance's error at its declaring clause, as two specifications of one member
    // are (InvalidAssociatedType_Kd), once per clause declaring the conformance (mg3d: two).
    [InlineData("guard", "mg1", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\n" + UseTag, "")]
    [InlineData("guard", "mg2", HasItem + Tagged + Direct + Merged + "    Self is Iter<S.(HasItem).Item>\n    Self is Q2<R.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\n" + UseTag, "")]
    [InlineData("guard", "mg2r", HasItem + Tagged + Direct + Merged + "    Self is Q2<R.(HasItem).Item>\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\n" + UseTag, "")]
    [InlineData("guard", "mg1b", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool\n    associate Iter<R.(HasItem).Item>.Tag is bool\n" + UseTag, "")]
    [InlineData("guard", "mg2b", HasItem + Tagged + Direct + Merged + "    Self is Iter<S.(HasItem).Item>\n    Self is Q2<R.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool\n    associate Iter<R.(HasItem).Item>.Tag is bool\n" + UseTag, "")]
    [InlineData("guard", "mg2bm", HasItem + Tagged + Direct + Merged + "    Self is Iter<S.(HasItem).Item>\n    Self is Q2<R.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool\n    associate Iter<R.(HasItem).Item>.Tag is bool\nfunc f(a: ref/B<SrcI, SrcI>, t: B<SrcI, SrcI>.(Iter<i32>).Tag) -> ()\n    return\n", "")]
    [InlineData("guard", "mg1c", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Iter<i32>.Tag is bool and Copy\n" + UseTag, "")]
    [InlineData("guard", "mg1u", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Tag is bool\n" + UseTag, "")]
    [InlineData("guard", "mg1uu", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\nfunc f(a: ref/B<SrcI, SrcI>) -> ()\n    let t: B<SrcI, SrcI>.Tag = true\n    return\n", "")]
    [InlineData("guard", "mg1us", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Tag is bool\n    public func g(self: ref/Self, t: Self.Tag) -> bool => t\n", "")]
    [InlineData("guard", "mg1i", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\nfunc f(a: ref/B<SrcI, SrcI>) -> ()\n    let t: B<SrcI, SrcI>.(Iter<i32>).Tag = 1\n    return\n", "TypeMismatch_Kd: let t: B<SrcI, SrcI>.(Iter<i32>).Tag = 1")]
    [InlineData("guard", "mg3", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool\n    associate Iter<R.(HasItem).Item>.Tag is i64\n", "InvalidAssociatedType_Kd: Self is Q<S.(HasItem).Item, R.(HasItem).Item>")]
    [InlineData("guard", "mg3x", HasItem + Tagged + Two + Merged + "    Self is Q<S.(HasItem).Item, R.(HasItem).Item>\n    associate Iter<i32>.Tag is bool\n    associate Iter<i32>.Tag is i64\n", "InvalidAssociatedType_Kd: Self is Q<S.(HasItem).Item, R.(HasItem).Item>")]
    [InlineData("guard", "mg3d", HasItem + Tagged + Direct + Merged + "    Self is Iter<S.(HasItem).Item>\n    Self is Q2<R.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool\n    associate Iter<R.(HasItem).Item>.Tag is i64\n", "InvalidAssociatedType_Kd: Self is Iter<S.(HasItem).Item> | InvalidAssociatedType_Kd: Self is Q2<R.(HasItem).Item>")]

    // SPEC 8.8.2, 8.6.1, 8.9.2 (PLAN G1300): a conditional block's specification selects under D and P, where its conformance is one with a
    // member declared outside the block, which then represents it; it specifies the block conformance's associated Type all the same, through
    // either member's name, and its obligation is the block's (mg4m); a disagreement with the member outside lies within the block
    // conformance's root path, whose one binding both specify, so it is that path's error at its declaring clause (mg4n).
    [InlineData("guard", "mg4", HasItem + Tagged + Direct + MergedWhen + "        associate Iter<R.(HasItem).Item>.Tag is bool\n", "")]
    [InlineData("guard", "mg4c", HasItem + Tagged + Direct + MergedWhen + "        associate Iter<R.(HasItem).Item>.Tag is bool and Copy\n", "")]
    [InlineData("guard", "mg4o", HasItem + Tagged + Direct + MergedWhen + "        associate Iter<S.(HasItem).Item>.Tag is bool\n", "")]
    [InlineData("guard", "mg4n", HasItem + Tagged + Direct + MergedWhen + "        associate Iter<R.(HasItem).Item>.Tag is i64\n", "InvalidAssociatedType_Kd: Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item")]
    [InlineData("guard", "mg4m", HasItem + Tagged + Direct + "contract Marker\n" + MergedWhen + "        associate Iter<R.(HasItem).Item>.Tag is bool and Marker\n", "UnsatisfiedConstraint_Kd: Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item")]

    // SPEC 8.8.1, 8.8.2, 8.5.5 Completion, 8.6.1, 8.9.2 (PLAN G1385): without a block, the Type-level specification of the member outside
    // specifies the block conformance's associated Type too, where P makes them one conformance (mg5, mg5c: the root path's premises prove
    // the clause's), and its obligation is that conformance's (mg5m); where P does not identify them, the block conformance's Tag is
    // undetermined (mg5f, mg5cf).
    [InlineData("guard", "mg5", HasItem + Tagged + Direct + MergedWhen, "")]
    [InlineData("guard", "mg5c", Tagged + Direct + "struct B<T>\n    Self is Iter<i32>\n    associate Iter<i32>.Tag is bool\n    Self is Q2<T> when T is i32\n", "")]
    [InlineData("guard", "mg5f", HasItem + Tagged + Direct + "struct B<S, R>\n    S is HasItem\n    R is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool\n    Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is Copy\n", "AssociatedTypeInferenceFailed_Kd: Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is Copy")]
    [InlineData("guard", "mg5cf", Tagged + Direct + "struct B<T>\n    Self is Iter<i32>\n    associate Iter<i32>.Tag is bool\n    Self is Q2<T> when T is i64\n", "AssociatedTypeInferenceFailed_Kd: Self is Q2<T> when T is i64")]
    // SPEC 6.1.4 (R7-C2c2 c2-5a): forming `Q2<..>` judges only its reference input conditions, and Q2's clause names its Self, so the
    // conformance's unsatisfied requirement is the one record (PLAN G1260's UnprovenConstraint_Kd at `Q2<..>` is gone), and a Tag that
    // meets it is accepted (mg5mv); the same for a direct conformance (mg6, mg6m). B's G1384 probes c2, c7, c8 and c8i.
    [InlineData("guard", "mg5m", HasItem + Tagged + "contract Marker\ncontract Q2<U>: Iter<U>\n    Self.(Iter<U>).Tag is Marker\n" + MergedWhen, "UnsatisfiedConstraint_Kd: Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item")]
    [InlineData("guard", "mg5mv", HasItem + Tagged + "contract Marker\nstruct Tg\n    Self is Marker\ncontract Q2<U>: Iter<U>\n    Self.(Iter<U>).Tag is Marker\nstruct B<S, R>\n    S is HasItem\n    R is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is Tg\n    Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item\n", "")]
    [InlineData("guard", "mg6", Tagged + "contract Marker\nstruct Tg\n    Self is Marker\ncontract Q3<U>: Iter<U>\n    Self.(Iter<U>).Tag is Marker\nstruct B<T>\n    Self is Q3<T>\n    associate Iter<T>.Tag is Tg\n", "")]
    [InlineData("guard", "mg6m", Tagged + "contract Marker\ncontract Q3<U>: Iter<U>\n    Self.(Iter<U>).Tag is Marker\nstruct B<T>\n    Self is Q3<T>\n    associate Iter<T>.Tag is bool\n", "UnsatisfiedConstraint_Kd: Self is Q3<T>")]
    // A Type-level obligation is each root path's, once per clause declaring a conformance it specifies (mg5b, as mg3d; PLAN G1384's c0m).
    [InlineData("guard", "mg5b", HasItem + Tagged + Direct + "contract Marker\nstruct B<S, R>\n    S is HasItem\n    R is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is bool and Marker\n    Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item\n", "UnsatisfiedConstraint_Kd: Self is Iter<S.(HasItem).Item> | UnsatisfiedConstraint_Kd: Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item")]
    // SPEC 23.1.2 (PLAN G1405): a conformance resting on a specification whose Type is an unknown name rests on the name's record, alone
    // (mg5us) or with the block conformance it specifies (mg5u); a block conformance P does not identify keeps its own record (mg5uf).
    [InlineData("guard", "mg5us", HasItem + Tagged + "struct B<S>\n    S is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is Undefined\n", "UnresolvedBinding_Kd: associate Iter<S.(HasItem).Item>.Tag is Undefined")]
    [InlineData("guard", "mg5u", HasItem + Tagged + Direct + "struct B<S, R>\n    S is HasItem\n    R is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is Undefined\n    Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is S.(HasItem).Item\n", "UnresolvedBinding_Kd: associate Iter<S.(HasItem).Item>.Tag is Undefined")]
    [InlineData("guard", "mg5uf", HasItem + Tagged + Direct + "struct B<S, R>\n    S is HasItem\n    R is HasItem\n    Self is Iter<S.(HasItem).Item>\n    associate Iter<S.(HasItem).Item>.Tag is Undefined\n    Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is Copy\n", "AssociatedTypeInferenceFailed_Kd: Self is Q2<R.(HasItem).Item> when R.(HasItem).Item is Copy | UnresolvedBinding_Kd: associate Iter<S.(HasItem).Item>.Tag is Undefined")]

    // SPEC 8.5.2, 8.5.1, 8.4.3, 8.2.3 (PLAN G722): inside a Contract a short name means its own or an inherited associated Type, and paths to one
    // declaration whose bindings are one under the Contract's premises count once, in either parent order; distinct bindings stay two (a01i).
    [InlineData("guard", "a01", ShortItem + "contract Q: R, P<i32>, P<Self.(R).X>\n    Self.(R).X is i32\n    func get(self: ref/Self) -> Item\n", "")]
    [InlineData("guard", "a01r", ShortItem + "contract Q: R, P<Self.(R).X>, P<i32>\n    Self.(R).X is i32\n    func get(self: ref/Self) -> Item\n", "")]
    [InlineData("guard", "a01i", ShortItem + "contract Q: R, P<i32>, P<Self.(R).X>\n    Self.(R).X is i64\n    func get(self: ref/Self) -> Item\n", "AmbiguousBinding_Kd: func get(self: ref/Self) -> Item")]

    // SPEC 8.9.1: conformances that can collide are an error at the later clause, an unresolved projection included (step 4); the use is ambiguous.
    [InlineData("guard", "x2", "struct M<S>\n    Self is Indexable<S>\n    Self is Indexable<i32>\n    associate Indexable<S>.Element is i32\n    associate Indexable<i32>.Element is i64\n    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n    public func index(self, key: ref/S) -> ref/i32 during self => self.small@ref\n    public func index(self, key: ref/i32) -> ref/i64 during self => self.large@ref\nlet m = M<i32>.init(3, 4)\nlet k: i32 = 1\nlet b = m[k]\n", "DuplicateBinding_Kd: Self is Indexable<i32> | AmbiguousBinding_Kd: let b = m[k]")]
    [InlineData("guard", "x4", HasItem + "struct M<S>\n    S is HasItem\n    Self is Indexable<S.(HasItem).Item>\n    Self is Indexable<i32>\n    associate Indexable<S.(HasItem).Item>.Element is i32\n    associate Indexable<i32>.Element is i64\n    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n    public func index(self, key: ref/S.(HasItem).Item) -> ref/i32 during self => self.small@ref\n    public func index(self, key: ref/i32) -> ref/i64 during self => self.large@ref\nlet m = M<SrcI>.init(3, 4)\nlet k: i32 = 1\nlet b = m[k]\n", "DuplicateBinding_Kd: Self is Indexable<i32> | AmbiguousBinding_Kd: let b = m[k]")]
    [InlineData("guard", "x11", HasItem + Key + "struct M<S>\n    S is HasItem\n    Self is Indexable<Option<S.(HasItem).Item>>\n    Self is UniqIndexable<Name>\n    associate Indexable<Option<S.(HasItem).Item>>.Element is i32\n    associate Indexable<Name>.Element is i64\n    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n    public func index(self, key: ref/Option<S.(HasItem).Item>) -> ref/i32 during self => self.small@ref\n    public func index(self, key: ref/Name) -> ref/i64 during self => self.large@ref\n    public func indexUniq(self: uniq/Self, key: ref/Name) -> uniq/i64 during self => self.large@uniq\nvar m = M<SrcI>.init(3, 4)\nlet k: Option<i32> = .Some(1)\nm[k] = 5\n", "SharedPathAccess_Kd: m[k] = 5")]

    // SPEC 8.2.3 Premise errors and Cycles: a Type identical with a proper part of itself is InvalidConstraint_Kd (tests/diagnostics/spec-cases.json,
    // 8.2.3-identity-premises) where the premises meet, at a Contract for what its Constraints alone cause and never again at uses. `T is U` with `U is
    // T` identifies them, and a projection is no proper part.
    [InlineData("C3", "cyclic", "func cyclic<T, U>(value: T) -> i32\n    T is Option<U>\n    U is Option<T>\n    return 0\n", "InvalidConstraint_Kd: U is Option<T>")]
    [InlineData("guard", "cyc-swap", "func same<T, U>(value: T) -> U\n    T is U\n    U is T\n    return value@move\n", "")]
    [InlineData("C3", "cyc-self", "func f<T>(value: T) -> i32\n    T is Option<T>\n    return 0\n", "InvalidConstraint_Kd: T is Option<T>")]
    [InlineData("guard", "cyc-proj", "contract C\n    associate X\nfunc f<T>(value: T) -> i32\n    T is C\n    T is T.(C).X\n    return 0\n", "")]
    [InlineData("guard", "cyc-part", "contract C\n    associate X\nfunc f<T>(value: T) -> i32\n    T is C\n    T is Option<T.(C).X>\n    return 0\n", "InvalidConstraint_Kd: T is Option<T.(C).X> | InvalidConstraint_Kd: func f<T>(value: T) -> i32")] // O-344 (a): Option declares no C
    [InlineData("C3", "cyc-both", "contract C\n    associate X\nfunc f<T>(value: T) -> i32\n    T is C\n    T is Option<T.(C).X>\n    T.(C).X is T\n    return 0\n", "InvalidConstraint_Kd: T.(C).X is T")]
    [InlineData("C3", "cyc-contract", "contract C\n    associate X\n    associate Y\n    Self.X is Option<Self.Y>\n    Self.Y is Option<Self.X>\nfunc f<T>(value: T) -> i32\n    T is C\n    return 0\n", "InvalidConstraint_Kd: Self.Y is Option<Self.X>")]
    // R7-C3 C3d (PLAN G830, G797): one record per error group at the last own premise clause with a Type in it: a cycle through a nested
    // Type's enclosing argument, at a Type declaration, through a supplied Constraint joined by the declaration's own premise, and the Proof
    // row's cycle through an owner-admitting pair; each with its valid twin.
    [InlineData("C3", "cyc-nest", Nest + "func f<T>(value: T) -> i32\n    T is Outer<T>.Inner\n    return 0\n", "InvalidConstraint_Kd: T is Outer<T>.Inner")]
    [InlineData("C3", "cyc-nest-t", Nest + "func f<T>(value: T) -> i32\n    T is Outer<i32>.Inner\n    return 0\n", "")]
    [InlineData("C3", "cyc-nest2", Nest + "func f<T, U>(value: T) -> i32\n    T is Outer<U>.Inner\n    U is Option<T>\n    return 0\n", "InvalidConstraint_Kd: U is Option<T>")]
    [InlineData("C3", "cyc-type", "struct S<T, U>\n    T is Option<U>\n    U is Option<T>\n", "InvalidConstraint_Kd: U is Option<T>")]
    [InlineData("C3", "cyc-type-t", "struct S<T, U>\n    T is Option<U>\n    U is Option<i32>\n", "")]
    [InlineData("C3", "cyc-use", "func cyclic<T, U>(value: T) -> i32\n    T is Option<U>\n    U is Option<T>\n    let y: Option<U> = value@move\n    return 0\n", "InvalidConstraint_Kd: U is Option<T>")]
    [InlineData("C3", "cyc-contract-t", "contract C\n    associate X\n    associate Y\n    Self.X is Option<Self.Y>\n    Self.Y is Option<i32>\nfunc f<T>(value: T) -> i32\n    T is C\n    return 0\n", "")]
    [InlineData("C3", "cyc-supply", "contract C\n    associate X\n    associate Y\n    Self.X is Option<Self.Y>\nfunc f<T>(value: T) -> i32\n    T is C\n    T.(C).Y is T.(C).X\n    return 0\n", "InvalidConstraint_Kd: T.(C).Y is T.(C).X")]
    [InlineData("C3", "cyc-supply-t", "contract C\n    associate X\n    associate Y\n    Self.X is Option<Self.Y>\nfunc f<T>(value: T) -> i32\n    T is C\n    T.(C).Y is i32\n    return 0\n", "")]
    [InlineData("C3", "cyc-pair", "func f<s/A>(x: ref/A) -> ()\n    s is owner or ref\n    A is s/A\n    ()\n", "UnprovenConstraint_Kd: A is s/A")]
    [InlineData("C3", "cyc-pair-t", "func f<s/A>(x: ref/A) -> ()\n    s is owner\n    A is s/A\n    ()\n", "")]
    public void ContractReferencesAreIdentifiedByTheirNormalizedForm(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-C3 C3d (F's C2/C3 ruling 331 (B)): a declaration whose premise error stands at its clause, or at its supplying Contract's clause, is
    // Invalid with no failure and no part, and its published cause is that record: the cyclic owner's is its clause's, f's under a
    // Contract-only cycle the Contract's clause's (through the Contract, which rests on its own clause).
    [Theory]
    [InlineData("func cyclic<T, U>(value: T) -> i32\n    T is Option<U>\n    U is Option<T>\n    return 0\n", "cyclic", "U is Option<T>")]
    [InlineData("contract C\n    associate X\n    associate Y\n    Self.X is Option<Self.Y>\n    Self.Y is Option<Self.X>\nfunc f<T>(value: T) -> i32\n    T is C\n    return 0\n", "f", "Self.Y is Option<Self.X>")]
    public void ADeclarationRestingOnItsPremiseErrorsClausePublishesItsRecordAsItsCause(string program, string owner, string clause)
    {
        var c = MinimalEmissionTest.Analyze(program + "()");
        var declaration = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.FunctionKoto>().Single(x => x.Name == owner);
        Assert.Equal((Kimi.Compiler.BindingState.Invalid, Kimi.Compiler.BindingFailure.None), (declaration.StateOf(c), declaration.FailureOf(c)));
        var cause = Assert.IsType<Kimi.Compiler.Parsing.IsKoto>(Assert.Single(c.Binding.FailureCauses(declaration)!).Subject);
        Assert.Equal(program.IndexOf(clause, StringComparison.Ordinal), cause.Span.Start);
    }

    // Rows as CompilationTestHelper.AssertRecordsAtLines reads them: (unit, form, program, records), the verdict from SPEC. A unit other than
    // guard is the plan's unit that makes the row pass: the row is Explicit until that unit lands and removes the marker.
    [Theory]
    // SPEC 8.2.3: a member or element access resolves through the identity of its receiver's Type, from a premise on a projection, a completed binding
    // or decomposition; a Type parameter with no identity has no member. Binding's records only: the MIR builder's row is the Middle's.
    [InlineData("guard", "ip-proj", PairStruct + "contract C\n    associate X\nfunc f<T>(s: ref/T.(C).X) -> i32\n    T is C\n    T.(C).X is Pair\n    return s.a\n", "")]
    [InlineData("guard", "ip-bind", PairStruct + "contract C\n    associate X\nstruct Cfg\n    Self is C\n    associate C.X is Pair\nfunc f(s: ref/Cfg.(C).X) -> i32\n    return s.a\n", "")]
    [InlineData("guard", "ip-dec", PairStruct + "func f<T, U>(s: ref/U) -> i32\n    T is Option<U>\n    T is Option<Pair>\n    return s.a\n", "")]
    [InlineData("guard", "ip-idx", PairStruct + "func f<T, U>(s: ref/U, i: isize) -> i32\n    T is Option<U>\n    T is Option<Array<Pair>>\n    return s[i].a\n", "")]
    [InlineData("guard", "ip-none", PairStruct + "func f<T>(s: ref/T) -> i32\n    return s.a\n", "*: return s.a")]
    [InlineData("guard", "ip-twoc", PairStruct + "func f<T, U>(s: ref/U) -> i32\n    T is Option<Pair>\n    return s.a\n", "*: return s.a")]
    public void AnAccessResolvesThroughItsReceiversIdentity(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertBindingRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 8.9.1, 4.7.1, 23.1.2: an index key's record relates each conformance its selection by the key weighed, labelled by the instance
    // it stands for (`related`, in order; f-x2w's Indexable<Name> is no member of the selection by Indexable<i32>), and a Note states only
    // what the check found (`note`, its start): M<SrcI> does conform to UniqIndexable, for Name.
    [Theory]
    [InlineData("C3", "x2", "struct M<S>\n    Self is Indexable<S>\n    Self is Indexable<i32>\n    associate Indexable<S>.Element is i32\n    associate Indexable<i32>.Element is i64\n    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n    public func index(self, key: ref/S) -> ref/i32 during self => self.small@ref\n    public func index(self, key: ref/i32) -> ref/i64 during self => self.large@ref\nlet m = M<i32>.init(3, 4)\nlet k: i32 = 1\nlet b = m[k]\n", "AmbiguousBinding_Kd", "Indexable<S> | Indexable<i32>", null)]
    [InlineData("C3", "x4", HasItem + "struct M<S>\n    S is HasItem\n    Self is Indexable<S.(HasItem).Item>\n    Self is Indexable<i32>\n    associate Indexable<S.(HasItem).Item>.Element is i32\n    associate Indexable<i32>.Element is i64\n    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n    public func index(self, key: ref/S.(HasItem).Item) -> ref/i32 during self => self.small@ref\n    public func index(self, key: ref/i32) -> ref/i64 during self => self.large@ref\nlet m = M<SrcI>.init(3, 4)\nlet k: i32 = 1\nlet b = m[k]\n", "AmbiguousBinding_Kd", "Indexable<S.Item> | Indexable<i32>", null)]
    [InlineData("C3", "f-x2w", Key + "struct M<S>\n    Self is Indexable<S>\n    Self is Indexable<i32>\n    Self is Indexable<Name>\n    associate Indexable<S>.Element is i32\n    associate Indexable<i32>.Element is i64\n    associate Indexable<Name>.Element is i64\n    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n    public func index(self, key: ref/S) -> ref/i32 during self => self.small@ref\n    public func index(self, key: ref/i32) -> ref/i64 during self => self.large@ref\n    public func index(self, key: ref/Name) -> ref/i64 during self => self.large@ref\nlet m = M<i32>.init(3, 4)\nlet k: i32 = 1\nlet b = m[k]\n", "AmbiguousBinding_Kd", "Indexable<S> | Indexable<i32>", null)]
    [InlineData("C3", "x11", HasItem + Key + "struct M<S>\n    S is HasItem\n    Self is Indexable<Option<S.(HasItem).Item>>\n    Self is UniqIndexable<Name>\n    associate Indexable<Option<S.(HasItem).Item>>.Element is i32\n    associate Indexable<Name>.Element is i64\n    var small: i32\n    var large: i64\n    public init(small: i32, large: i64)\n        self.small = small\n        self.large = large\n    public func index(self, key: ref/Option<S.(HasItem).Item>) -> ref/i32 during self => self.small@ref\n    public func index(self, key: ref/Name) -> ref/i64 during self => self.large@ref\n    public func indexUniq(self: uniq/Self, key: ref/Name) -> uniq/i64 during self => self.large@uniq\nvar m = M<SrcI>.init(3, 4)\nlet k: Option<i32> = .Some(1)\nm[k] = 5\n", "SharedPathAccess_Kd", null, "M<SrcI> conforms to UniqIndexable<Name> but not to UniqIndexable<Option<i32>>")]
    public void AKeysRecordStatesWhatTheCheckFound(string unit, string form, string program, string code, string? related, string? note)
    {
        var c = MinimalEmissionTest.Analyze(program + "()");
        var record = CompilationTestHelper.PublishedErrors(c).Single(x => x.Code == code);
        var labels = string.Join(" | ", (record.Related ?? []).Select(static x => x.Label));
        Assert.True(related is null || labels == related, $"{form} [{unit}]: {labels}");
        Assert.True(note is null || record.Note?.StartsWith(note, StringComparison.Ordinal) == true, $"{form} [{unit}]: {record.Note}");
    }
}
