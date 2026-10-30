// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 8.15.3 with DECISIONS R5-M144 and R5-M149 (the owner's LayoutNeed): a form whose generated code reads a layout past its root's
// storage is a use of the Type it reads, InvalidCLayout_Kd at the form for an unavailable C layout on every pipeline. A Field, Case payload
// or element projected through a borrow, raw pointer, object handle or a sequence's storage needs its container's offsets or its stride; a
// raw pointer's displacement, a whole-value update through references and a Dictionary operation need their Types' sizes; a comparison of a
// Tuple's parts through a reference needs its offsets (SPEC 13.4). The twins, the same forms without the unavailable layout, are accepted.
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
        ("dictionary", "func f(d: ref/Dictionary<i32, X>) -> i32\n    let r = d[1]@ref\n    return 0\n", "InvalidCLayout_Kd d[1]@ref"),
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
}
