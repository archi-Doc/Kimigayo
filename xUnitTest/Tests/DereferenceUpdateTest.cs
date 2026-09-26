// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>SPEC 13.7.2, 13.5.5.1: a compound update through `E@deref` evaluates `E` once, after its right-hand side, and
/// reads and writes the one referent Place that evaluation designates.</summary>
public class DereferenceUpdateTest
{
    private const string Source =
        "func pick(r: uniq/i32, c: uniq/i32) -> uniq/i32 during r\n    c@deref += 1\n    return r@move\n" +
        "var x: i32 = 1\nvar count: i32 = 0\npick(x@uniq, count@uniq)@deref += 10\n" +
        "require x == 11 and count == 1 else => $abort(\"once\")\nlet target = x@uniq\ntarget@deref *= 2\ntarget@deref++\n" +
        "require x == 23 else => $abort(\"local\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void TheReferenceIsEvaluatedOnce()
        => ScalarEmissionTest.EmitFixture("DereferenceUpdateOnce", Source, "ok\n");
}
