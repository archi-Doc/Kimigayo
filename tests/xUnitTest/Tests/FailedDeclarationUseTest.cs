// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 23.3.6.4: a check that rests on a failed declaration is a derived problem. A call whose candidate or selected target is
// in a failed declaration, and the Property proof of a member whose declaration context failed, name that declaration as
// their prerequisite and are suppressed by its published Error, while independent problems stay visible.
public sealed class FailedDeclarationUseTest
{
    [Theory]
    [InlineData("func add(a: i32) -> i32 => a\nfunc add(a: i32) -> i32 => a\npublic func main()\n    let x = add(1)\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    [InlineData("struct Kimi\n    public var v: i32 = 0\npublic func main()\n    let k = Kimi.init()\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    [InlineData("open struct Base\n    public var a: i32 = 0\n    public struct Node\n        public var v: i32 = 0\nstruct Derived: Base\n    public var b: i32 = 0\n    public struct Node\n        public var w: i32 = 0\npublic func main()\n    let x: i32 = 1\n", nameof(DiagnosticCode.DuplicateBinding_Kd))]
    public void UsesOfAFailedDeclarationRestOnIt(string source, string code)
    {
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.NotEmpty(records);
        Assert.All(records, x => Assert.Equal(code, x.Code));
    }

    [Fact]
    public void AMemberOfAStructWithAnInvalidAttributeAddsNoConstraintRecord()
    {
        var records = DiagnosticCorpus.Check("#Layout(\"c\")\nstruct P\n    public var x: i32 = 0\npublic func main()\n    let p = P.init()\n    let y = p.x\n").Diagnostics;
        Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.InvalidLayoutAttribute_Kd));
        Assert.DoesNotContain(records, x => x.Code == nameof(DiagnosticCode.InvalidConstraint_Kd));
    }

    [Fact]
    public void AnIndependentProblemNextToAFailedDeclarationStaysVisible()
    {
        var records = DiagnosticCorpus.Check("func add(a: i32) -> i32 => a\nfunc add(a: i32) -> i32 => a\npublic func main()\n    let x = add(1)\n    let y: i32 = true\n").Diagnostics;
        Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd));
        Assert.Contains(records, x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        Assert.DoesNotContain(records, x => x.Code == nameof(DiagnosticCode.InvalidConstraint_Kd));
    }
}
