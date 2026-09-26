// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 3.4.1: a member or Tuple element selected through several reference layers is reached through one
/// reference to its declaring Type, shared when any layer is shared and exclusive otherwise.</summary>
public class MemberThroughLayersTest
{
    private const string P = "struct P\n    public var v: i32\n    public var name: string\n    public init(v: i32, name: string)\n        self.v = v\n        self.name = name@move\n";

    private const string Source =
        P + "let a = P.init(1, \"a\")\nlet views = [a@ref]\nfor v in views\n    require v.v == 1 else => $abort(\"read\")\n    Console.writeLine(v.name)\n" +
        "var b = P.init(1, \"b\")\nvar exclusive: [1 of uniq/P] = [b@uniq]\nfor v in exclusive@uniq\n    v.v += 10\n    v.v = v.v + 1\nfor v in exclusive\n    require v.v == 12 else => $abort(\"write\")\n" +
        "let t: (i32, i32) = (1, 2)\nlet tuples = [t@ref]\nfor r in tuples\n    require r.0 + r.1 == 3 else => $abort(\"tuple\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void FieldsAndTupleElementsAreSelectedThroughLayers()
        => ScalarEmissionTest.EmitFixture("MemberThroughLayers", Source, "a\nok\n");

    [Fact]
    public void ASharedLayerBoundsTheSelectedPathToSharedAccess()
    {
        var c = MinimalEmissionTest.Analyze(P + "var a = P.init(1, \"a\")\nvar views: [1 of uniq/P] = [a@uniq]\nfor v in views\n    v.v = 5");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssignment_Kd);
    }

    [Fact]
    public void TheOuterLoansStayActiveWhileTheSelectionIsUsed()
    {
        var c = MinimalEmissionTest.Analyze(P + "var a = P.init(1, \"a\")\nvar views: [1 of uniq/P] = [a@uniq]\nfor v in views@uniq\n    let name: ref/string = v.name@ref\n    views = [a@uniq]\n    Console.writeLine(name)");
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }
}
