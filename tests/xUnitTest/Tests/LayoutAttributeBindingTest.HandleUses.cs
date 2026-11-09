// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// PLAN G641 (OI-1a): SPEC 8.15.3 checks a collection's own layout, its handle, where it is passed, moved or returned by value; its
// element's only where an element is stored, projected or destroyed (SPEC 16.2.1, 16.3.2: a Drop of a moved-out or never-initialized
// value destroys nothing, on a runtime or an assumed path, SPEC 14.10.3, and a transfer's operand is delivered on its assumed path).
// The valid rows fail on dev 39fd6a942 (InvalidCLayout_Kd at the parameter and each move); the invalid twins destroy, project or move out
// an element and keep the record at that use alone. Rows are AssertUses' (LayoutAttributeBindingTest.CLayoutUses.cs).
public partial class LayoutAttributeBindingTest
{
    private const string HandleBad = "#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n" +
        "    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n";

    private const string HandleBadA = "#Layout(\"C\")\nstruct BadA\n    public var nothing: ()\n    public var value: i32\n";

    public static TheoryData<string, string, string> HandleRows => new()
    {
        { "P_pass", HandleBad + "func f(d: Dictionary<Bad, i32>) -> Dictionary<Bad, i32>\n    return d@move\nlet zz = 1\n", string.Empty },
        { "A_pass", HandleBadA + "func f(v: Array<BadA>) -> Array<BadA>\n    return v@move\nlet zz = 1\n", string.Empty },
        {
            "P_call",
            HandleBad + "func h(d: Dictionary<Bad, i32>) -> Dictionary<Bad, i32>\n    return d@move\n" +
                "func f(d: Dictionary<Bad, i32>) -> Dictionary<Bad, i32>\n    return h(d@move)\nlet zz = 1\n",
            string.Empty
        },
        { "A_local", HandleBadA + "func f(v: Array<BadA>) -> Array<BadA>\n    let w = v@move\n    return w@move\nlet zz = 1\n", string.Empty },
        {
            "V_gen",
            HandleBadA + "func h<T>(v: Array<T>) -> Array<T>\n    return v@move\nfunc f(v: Array<BadA>) -> Array<BadA>\n    return h(v@move)\nlet zz = 1\n",
            string.Empty
        },
        { "V_dMoveOn", HandleBad + "func f(d: Dictionary<Bad, i32>) -> Option<Dictionary<Bad, i32>>\n    return .Some(d@move)\nlet zz = 1\n", string.Empty },
        {
            "V_oRet",
            HandleBad + "func f(d: Dictionary<Bad, i32>) -> DictionaryOwningIterator<Bad, i32>\n    return (d@move).intoIterator()\nlet zz = 1\n",
            string.Empty
        },
        { "A_oRet", HandleBadA + "func f(v: Array<BadA>) -> ArrayOwningIterator<BadA>\n    return (v@move).intoIterator()\nlet zz = 1\n", string.Empty },
        {
            "I_dDrop",
            HandleBad + "func f(d: Dictionary<Bad, i32>) -> isize\n    return 0\nlet zz = 1\n",
            "InvalidCLayout_Kd d: Dictionary<Bad, i32>) -> isize"
        },
        { "I_aDrop", HandleBadA + "func f(v: Array<BadA>) -> isize\n    return 0\nlet zz = 1\n", "InvalidCLayout_Kd v: Array<BadA>) -> isize" },
        {
            "I_aODrop",
            HandleBadA + "func f(v: Array<BadA>) -> isize\n    var it = (v@move).intoIterator()\n    return 0\nlet zz = 1\n",
            "InvalidCLayout_Kd var it = (v@move).intoIterator()"
        },
        { "I_aIndex", HandleBadA + "func f(v: ref/Array<BadA>) -> i32\n    return v[0].value\nlet zz = 1\n", "InvalidCLayout_Kd v[0].value" },
        {
            "I_aONext",
            HandleBadA + "func f(v: Array<BadA>) -> ArrayOwningIterator<BadA>\n    var it = (v@move).intoIterator()\n    _ = it.next()\n" +
                "    return it@move\nlet zz = 1\n",
            "InvalidCLayout_Kd it.next()"
        },
        {
            "I_aFlag",
            HandleBadA + "func h(v: Array<BadA>) -> Array<BadA>\n    return v@move\nfunc f(v: Array<BadA>, c: bool) -> isize\n    if c\n" +
                "        let w = h(v@move)\n        return 1\n    return 0\nlet zz = 1\n",
            "InvalidCLayout_Kd v: Array<BadA>, c: bool|InvalidCLayout_Kd let w = h(v@move)"
        },
        {
            "I_deadLive",
            HandleBadA + "func f(v: Array<BadA>) -> Array<BadA>\n    return v@move\n    let w: Option<Array<BadA>> = .None\nlet zz = 1\n",
            "InvalidCLayout_Kd let w: Option<Array<BadA>> = .None"
        },
        {
            "I_deadLive2",
            HandleBadA + "func f(v: Array<BadA>) -> Array<BadA>\n    return v@move\n    let w: Array<BadA> = []\n    _ = w.length\nlet zz = 1\n",
            "InvalidCLayout_Kd let w: Array<BadA> = []"
        },
    };

    [Theory]
    [MemberData(nameof(HandleRows))]
    public void ACollectionHandleNeedsItsElementOnlyWhereAnElementIsDestroyedOrProjected(string form, string source, string records)
        => AssertUses(form, source, records);
}
