// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Test verifications (SPEC 17.6): a verification's condition observed at its site, its compared values recorded, and its failure's
// message and Abort.
internal sealed partial class MirBuilder
{
    // SPEC 17.6: a verification's condition secured as a bool and observed at its site (testObserve of the bool and the site's location,
    // which TestCatalog numbers, giving the failure's issue) before its temporaries are cleaned up; on false its message, evaluated and
    // recorded for the issue (testMessage of a shared view; the Never value has nothing to view) with its own temporaries cleaned up, then
    // for $require the case's Abort (testRequireAbort), which has no successor; $expect continues with Unit either way. Its test phases
    // are the backend's (plan 5.6.7).
    private void Verification(MirPlace destination, TestVerificationKoto verification)
    {
        var failed = this.body.AddBlock();
        var after = this.body.AddBlock();
        var site = SourceRef.Of(verification);
        var location = MirOperand.Const(this.body.Const(new(MirConstKind.Location, BoundType.I32, ((long)site.Module << 32) | (uint)site.Id)));
        var issue = this.Hold(BoundType.Primitives["i64"], verification);
        var test = this.Observe(verification.Condition, failed, after, issue, location);
        this.Made(issue);
        this.Enter(failed, test);
        if (verification.Message is { } message)
        {
            var temporaries = this.OpenScope(block: false);
            var text = this.Hold(TypeOf(message), message);
            var start = this.arguments.Count;
            this.Assign(text, message);
            if (!this.IsNever(this.Made(text)))
            {
                this.arguments.Add(MirOperand.Copy(issue));
                this.arguments.Add(this.Borrow(SemanticsKind.Ref, text, message));
                this.Invoke(this.Temp(BoundType.Unit, message), MirCallee.Intrinsic(IntrinsicId.TestMessage), start, message);
            }

            this.CloseScope(temporaries);
        }

        if (verification.IsRequire)
        {
            var start = this.arguments.Count;
            this.arguments.Add(MirOperand.Copy(issue));
            this.arguments.Add(location);
            this.Invoke(this.Unwritten(verification), MirCallee.Intrinsic(IntrinsicId.TestRequireAbort), start, verification, diverges: true);
            this.body.Terminate(this.current, MirTerminator.Unreachable(this.Info(verification)));
        }
        else
        {
            this.Join(after, verification.Message ?? verification.Condition);
        }

        this.Enter(after, test);
        this.Unit(destination, verification);
    }

    // SPEC 17.6.2: observe a verification's bool, its values recorded, before cleaning its temporaries, then branch with the bool's storage
    // live.
    private MirPlace Observe(Koto condition, int whenFalse, int whenTrue, MirPlace issue, MirOperand site)
    {
        var tested = this.OpenScope(block: false);
        var test = this.Hold(BoundType.Boolean, condition);
        var temporaries = this.OpenScope(block: false);
        var mark = this.arguments.Count;
        this.Recorded(test, condition);
        this.arguments.Add(MirOperand.Copy(test));
        this.arguments.Add(site);
        this.Invoke(issue, MirCallee.Intrinsic(IntrinsicId.TestObserve), this.arguments.Count - 2, condition);

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        this.CloseScope(temporaries);
        this.Branch(this.Made(test), condition, whenFalse, whenTrue);
        this.CloseScope(tested, clean: false);
        return test;
    }

    // SPEC 17.6.3 (PD22): a verification's condition into the tested bool. A built-in comparison's Scalar operands, each read once where it
    // is evaluated and secured there (Secure: the records are code built after them), are recorded from their temps with their side (0 the
    // left, 1 the right) for the failure report (testRecordValue) before the comparison moves them. Another condition records nothing.
    private void Recorded(MirPlace test, Koto condition)
    {
        var compared = condition;
        while (InPlace(HirTables.AdjustOf(compared)) && HirTables.Op(compared) == HirOp.Group)
        {
            compared = Child(compared);
        }

        if (!InPlace(HirTables.AdjustOf(compared)) || HirTables.Op(compared) != HirOp.PrimitiveOp || compared is not BinaryKoto binary ||
            BinaryOp(binary.Akind) is not (>= MirBinaryOp.Eq and var op))
        {
            this.Assign(test, condition);
            return;
        }

        var start = this.Operands(binary);
        for (var side = 0; side < 2; side++)
        {
            if (ScalarTypes.Supports(this.TypeOf(this.arguments[start + side])))
            {
                this.arguments[start + side] = this.Secure(this.arguments[start + side], side == 0 ? binary.Left : binary.Right);
            }
        }

        for (var side = 0; side < 2; side++)
        {
            var value = this.arguments[start + side];
            if (ScalarTypes.Supports(this.TypeOf(value)))
            {
                var record = this.arguments.Count;
                this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.I32, side))));
                this.arguments.Add(value.Kind == MirOperandKind.Const ? value : MirOperand.Copy(value.Place));
                this.Invoke(this.Temp(BoundType.Unit, condition), MirCallee.Intrinsic(IntrinsicId.TestRecordValue), record, condition);
            }
        }

        this.Binary(test, binary, op, start, TypeOf(compared));
    }
}
