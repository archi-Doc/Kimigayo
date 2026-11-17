// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 8.15.3 with DECISIONS R5-M144 and R5-M149 (the owner's LayoutNeed): a form whose generated code reads a layout past its root's
// storage is a use of the Type it reads, InvalidCLayout_Kd at the form for an unavailable C layout. A Field, Case payload
// or element projected through a borrow, raw pointer, object handle or a sequence's storage needs its container's offsets or its stride; a
// raw pointer's displacement, a whole-value update through references (swap, replace, exchange; a value given or taken is a by-value use too)
// and a Dictionary operation need their Types' sizes; a comparison of a Tuple's parts through a reference needs its offsets (SPEC 13.4). The
// twins, the same forms without the unavailable layout, are accepted.
public partial class LayoutAttributeBindingTest
{
    private const string EquatableP = "#Layout(\"C\")\nstruct P<T>\n    Self is Equatable\n    public var n: i32\n    public var value: T\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n" +
        "func same<T>(x: ref/T, y: ref/T) -> bool\n    T is Equatable\n    return x == y\n";

    private const string Peek = "func peek<T>(flag: bool) -> i32\n    let q: raw/P<T> = null\n    var r = 0\n    if flag\n        unsafe\n" +
        "            r = (*q).n\n    return r\n";

    private const string GenericPublicP = "#Layout(\"C\")\nstruct P<T>\n    public var n: i32\n    public var value: T\n";

    private const string Compared = "func cmp(flag: bool) -> bool\n    let q: raw/(X, i32) = null\n    var r = false\n    if flag\n        unsafe\n" +
        "            let a = ((*q)@ref, 1)\n            r = same(a@ref, a@ref)\n    return r\nlet z = cmp(false)\n";

    private const string TwoSites = "func two(p: ref/(X, i32), q: ref/(X, i32)) -> bool\n    let first = p == q\n    let second = p != q\n" +
        "    return first and second\n";

    // The pair-layer forms over S (the review of G157 (47)): a ref/uniq pair layer selected by a pattern below a Tuple or Option Subject; a
    // twin is the form without UnitS's attribute, whose S has its layout.
    private const string UnitS = "#Layout(\"C\")\n";

    private const string PairCase = "struct S\n    public var n: i32\n    public var u: ()\nfunc f<s/T>(a: s/T, e: s/Option<S>) -> i32\n" +
        "    s is ref or uniq\n    let t = (e, 1)\n    match t\n        (.Some(let x), _) => return 1\n        _ => return 0\n" +
        "func g(x: ref/i32, y: ref/Option<S>) -> i32 => f(x, y)\nlet zz = 1\n";

    private const string PairNested = "struct S\n    public var n: i32\n    public var u: ()\nfunc f<s/T>(a: s/T, e: s/Option<S>) -> i32\n" +
        "    s is ref or uniq\n    let o = Option.Some(e)\n    match o\n        .Some(.Some(let x)) => return 1\n        _ => return 0\n" +
        "func g(x: ref/i32, y: ref/Option<S>) -> i32 => f(x, y)\nlet zz = 1\n";

    private const string PairPart = "struct S\n    public var n: i32\n    public var u: ()\nfunc f<s/T>(a: s/T, e: s/(S, i32)) -> i32\n" +
        "    s is ref or uniq\n    let t = (e, 1)\n    match t\n        ((_, let k), _) => return k\n" +
        "func g(x: ref/i32, y: ref/(S, i32)) -> i32 => f(x, y)\nlet zz = 1\n";

    // The key selections over X whose key is decided in an instance (PLAN G597): a key of an open Type, a start boundary of one, a
    // default and a closure reading the key, and a caller passing its own key on, to h or to the library's slice.
    private const string ByKey = "func h<R>(a: ref/Array<X>, r: R) -> isize\n    R is PositionRange\n    return a[r].length\n";

    private const string ByBoundary = "func h<K>(a: ref/Array<X>, r: Range<K, End>) -> isize\n    K is Position\n    return a[r].length\n";

    private const string ByDefault = "func h<R>(a: ref/Array<X>, r: R, n: isize = a[r].length) -> isize\n    R is PositionRange\n    return n\n";

    private const string ByClosure = "func h<R>(a: ref/Array<X>, r: R) -> isize\n    R is PositionRange\n    R is Copy\n" +
        "    let c = func () -> isize => a[r].length\n    return c()\n";

    private const string Forward = "func g<Q>(a: ref/Array<X>, q: Q) -> isize\n    Q is PositionRange\n    return h(a, q)\n";

    private const string ForwardSlice = "func g<Q>(a: ref/Array<X>, q: Q) -> isize\n    Q is PositionRange\n    return a.slice(q).length\n";

    private const string KeyElement = "func h<R>(a: ref/Array<X>, r: R) -> i32\n    R is PositionRange\n    let s = a[r]\n    return s[0].n\n" +
        "func f(a: ref/Array<X>) -> i32 => h(a, ..)\n";

    // The declarations whose C layout is unavailable with a public Field n, the Type text X of their uses, and their twins.
    private static readonly (string Name, string Declaration, string Type, string TwinDeclaration, string TwinType)[] Projected =
    [
        ("unit", "#Layout(\"C\")\nstruct S\n    public var n: i32\n    public var u: ()\n", "S", "struct S\n    public var n: i32\n    public var u: ()\n", "S"),
        ("open", "#Layout(\"C\")\nopen struct S\n    public var n: i32\n", "S", "open struct S\n    public var n: i32\n", "S"),
        (
            "derived",
            "open struct B\n    public var k: i32\n#Layout(\"C\")\nstruct S: B\n    public var n: i32\n",
            "S",
            "open struct B\n    public var k: i32\nstruct S: B\n    public var n: i32\n",
            "S"),
        ("w-unit", GenericPublicP, "P<()>", GenericPublicP, "P<i32>"),
    ];

    // The forms over X with the records SPEC 8.15.3 gives them.
    private static readonly (string Name, string Body, string Records)[] Needs =
    [
        ("ref", "func f(s: ref/X) -> i32 => s.n\n", "InvalidCLayout_Kd s.n"),
        ("uniq", "func f(s: uniq/X) -> i32 => s.n\n", "InvalidCLayout_Kd s.n"),
        ("raw", "func f(p: raw/X) -> i32\n    var v = 0\n    unsafe\n        v = (*p).n\n    return v\n", "InvalidCLayout_Kd (*p).n"),
        ("obj", "func f(o: obj/X) -> i32 => o.n\n", "InvalidCLayout_Kd o.n"),
        ("nested", "struct H\n    public var k: i32\n    public var s: X\nfunc f(h: ref/H) -> i32 => h.k\n", "InvalidCLayout_Kd h.k"),
        (
            "option",
            "func f(e: ref/Option<X>) -> i32\n    match e\n        .Some(let x) => return x.n\n        _ => return 0\n",
            "InvalidCLayout_Kd let x|InvalidCLayout_Kd x.n"),
        ("farray", "func f(a: ref/[2 of X]) -> i32 => a[1].n\n", "InvalidCLayout_Kd a[1].n"),
        ("slice", "func f(s: Slice<X>) -> i32 => s[0].n\n", "InvalidCLayout_Kd s[0].n"),
        ("array", "func f(a: ref/Array<X>) -> i32 => a[0].n\n", "InvalidCLayout_Kd a[0].n"),
        ("displace", "func f(p: raw/X) -> raw/X\n    var q = p\n    unsafe\n        q = p + 1\n    return q\n", "InvalidCLayout_Kd p + 1"),
        ("swap", "func f(a: uniq/X, b: uniq/X)\n    Kimi.Intrinsics.swap(a, b)\n", "InvalidCLayout_Kd Kimi.Intrinsics.swap(a, b)"),
        (
            "replace",
            "func f(a: uniq/Option<X>)\n    Kimi.Intrinsics.replace(a, with: .None)\n",
            "InvalidCLayout_Kd .None|InvalidCLayout_Kd Kimi.Intrinsics.replace(a, with: .None)"),
        (
            "exchange",
            "func f(a: uniq/Option<X>)\n    let old = Kimi.Intrinsics.exchange(a, with: .None)\n",
            "InvalidCLayout_Kd let old|InvalidCLayout_Kd .None|InvalidCLayout_Kd Kimi.Intrinsics.exchange(a, with: .None)"),
        ("dictionary", "func f(d: ref/Dictionary<i32, X>) -> i32\n    let r = d[1]@ref\n    return 0\n", "InvalidCLayout_Kd d[1]@ref"),
        // SPEC 4.6.1, 8.15.3 (G597): a view whose start boundary is Start starts at the storage, projecting no element; a written
        // start, whatever its value, is that element's offset.
        ("view-whole", "func f(a: ref/Array<X>) -> isize => a[..].length\n", string.Empty),
        ("view-prefix", "func f(a: ref/Array<X>, k: isize) -> isize => a[..k].length\n", string.Empty),
        ("view-from-end", "func f(a: ref/Array<X>) -> isize => a[..^1].length\n", string.Empty),
        ("view-fixed", "func f(a: ref/[2 of X]) -> isize => a[..=0].length\n", string.Empty),
        ("view-slice", "func f(s: Slice<X>) -> isize => s[..1].length\n", string.Empty),
        ("view-value", "func f(a: ref/Array<X>) -> isize\n    let r = ..\n    return a[r].length\n", string.Empty),
        ("view-uniq", "func f(a: uniq/Array<X>) -> isize => a.sliceUniq().length\n", string.Empty),
        ("view-start", "func f(a: ref/Array<X>) -> isize => a[0..].length\n", "InvalidCLayout_Kd a[0..]"),
        ("view-start-value", "func f(a: ref/Array<X>) -> isize\n    let r = 1..\n    return a[r].length\n", "InvalidCLayout_Kd a[r]"),
        // PLAN G597: a key whose start an instance decides. The library's slice and a generic body's selection read the stride only in
        // the instance whose key displaces, the one use, at the selection (a library instance's at its call); the instance whose key
        // starts at the storage, a default's copy and a closure's too, reads none.
        ("slice-whole", Over("a.slice(..).length"), string.Empty),
        ("slice-prefix", Over("a.slice(..2).length"), string.Empty),
        ("slice-closed", Over("a.slice(..=1).length"), string.Empty),
        ("slice-fixed", "func f(a: ref/[2 of X]) -> isize => a.slice(..).length\n", string.Empty),
        ("slice-slice", "func f(s: Slice<X>) -> isize => s.slice(..).length\n", string.Empty),
        ("slice-start", Over("a.slice(1..).length"), "InvalidCLayout_Kd a.slice(1..)"),
        ("slice-zero", Over("a.slice(0..).length"), "InvalidCLayout_Kd a.slice(0..)"),
        ("slice-from-end", Over("a.slice(^1..).length"), "InvalidCLayout_Kd a.slice(^1..)"),
        ("slice-fixed-start", "func f(a: ref/[2 of X]) -> isize => a.slice(1..).length\n", "InvalidCLayout_Kd a.slice(1..)"),
        ("key-whole", ByKey + Over("h(a, ..)"), string.Empty),
        ("key-end", ByKey + Over("h(a, ..^1)"), string.Empty),
        ("key-start", ByKey + Over("h(a, 1..)"), "InvalidCLayout_Kd a[r]"),
        ("key-both", ByKey + Over("h(a, ..) + h(a, 1..)"), "InvalidCLayout_Kd a[r]"),
        ("boundary-whole", ByBoundary + Over("h(a, ..)"), string.Empty),
        ("boundary-start", ByBoundary + Over("h(a, 1..)"), "InvalidCLayout_Kd a[r]"),
        ("forward-whole", ByKey + Forward + Over("g(a, ..)"), string.Empty),
        ("forward-start", ByKey + Forward + Over("g(a, 1..)"), "InvalidCLayout_Kd a[r]"),
        ("forward-slice", ForwardSlice + Over("g(a, ..) + g(a, ..2)"), string.Empty),
        ("forward-slice-start", ForwardSlice + Over("g(a, ..) + g(a, 1..)"), "InvalidCLayout_Kd a.slice(q)"),
        ("closure-whole", ByClosure + Over("h(a, ..)"), string.Empty),
        ("closure-start", ByClosure + Over("h(a, 1..)"), "InvalidCLayout_Kd a[r]"),
        ("default-whole", ByDefault + Over("h(a, ..)"), string.Empty),
        ("default-start", ByDefault + Over("h(a, 1..)"), "InvalidCLayout_Kd a[r]"),
        ("default-forward", ByDefault + Forward + Over("g(a, ..)"), string.Empty),
        ("default-forward-start", ByDefault + Forward + Over("g(a, 1..)"), "InvalidCLayout_Kd a[r]"),
        // DECISIONS Q13: an element projected past a whole view is the one use; the view, which reads no stride, is none, in another
        // function's displacing instance and in a generic body's instance alike.
        ("slice-element", "func f(a: ref/Array<X>) -> i32\n    let s = a.slice(..)\n    return s[0].n\n", "InvalidCLayout_Kd s[0].n"),
        ("slice-sites", Over("a.slice(..).length") + "func k(a: ref/Array<X>) -> isize => a.slice(1..).length\n", "InvalidCLayout_Kd a.slice(1..)"),
        ("key-element", KeyElement, "InvalidCLayout_Kd s[0].n"),
    ];

    // The inferred and compared forms: P<()> projected in a generic body's instance, and a comparison shim's Tuple compared through a
    // reference (the review's R1), each with its P<i32> twin; an uninstantiated generic body's projection needs nothing.
    private static readonly (string Name, string Source, string Records, string Twin)[] InferredNeeds =
    [
        ("i-proj", GenericPublicP + Peek + "let z = peek<()>(false)\n", "InvalidCLayout_Kd (*q).n", GenericPublicP + Peek + "let z = peek<i32>(false)\n"),
        ("i-unused", GenericPublicP + Peek + "let z = 1\n", string.Empty, GenericPublicP + Peek + "let z = peek<i32>(false)\n"),
        (
            "compare",
            EquatableP + Compared.Replace("X", "P<()>", StringComparison.Ordinal),
            "InvalidCLayout_Kd x == y",
            EquatableP + Compared.Replace("X", "P<i32>", StringComparison.Ordinal)),
        // G308 D1: tuple operators at two sites, whose operands' Tuple lies past a ref layer, each a use of its Tuple's offsets (SPEC 23.1.2).
        (
            "compare-sites",
            EquatableP + TwoSites.Replace("X", "P<()>", StringComparison.Ordinal),
            "InvalidCLayout_Kd p == q|InvalidCLayout_Kd p != q",
            EquatableP + TwoSites.Replace("X", "P<i32>", StringComparison.Ordinal)),
        // G157 (47): a structural pattern selects a qualifying pair layer (SPEC 14.8.1, 13.5.5), so a case or part past it in a borrow case
        // reads its container's layout there.
        ("pair-case", UnitS + PairCase, "InvalidCLayout_Kd let x", PairCase),
        ("pair-nested", UnitS + PairNested, "InvalidCLayout_Kd let x", PairNested),
        ("pair-part", UnitS + PairPart, "InvalidCLayout_Kd let k", PairPart),
    ];

    // SPEC 8.15.3 (DECISIONS R5-M149): each form that reads a layout past its root's storage is one record at the form.
    public static TheoryData<string, string, string> NeedRows
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var shape in Projected)
            {
                foreach (var need in Needs)
                {
                    rows.Add($"{shape.Name}.{need.Name}", Program(shape.Declaration, need.Body, shape.Type), need.Records);
                    rows.Add($"{shape.Name}.{need.Name}.twin", Program(shape.TwinDeclaration, need.Body, shape.TwinType), string.Empty);
                }
            }

            foreach (var inferred in InferredNeeds)
            {
                rows.Add(inferred.Name, inferred.Source, inferred.Records);
                rows.Add($"{inferred.Name}.twin", inferred.Twin, string.Empty);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(NeedRows))]
    public void AFormReadingALayoutPastItsRootsStorageIsAUse(string form, string source, string records)
        => AssertUses(form, source, records);

    // A function f over an Array of X whose value is the expression.
    private static string Over(string expression) => $"func f(a: ref/Array<X>) -> isize => {expression}\n";
}
