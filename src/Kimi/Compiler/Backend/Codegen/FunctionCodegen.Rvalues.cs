// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The rvalues of FunctionCodegen (R5 package C 3.2, plan C2b; C0c brings Ref forward, DECISIONS R5-M78).
internal sealed partial class FunctionCodegen
{
    // A Use: its operand in the local's storage representation, nothing for a zero-sized local; a string constant written whole; a copy
    // of a slot Type is C2b's (memcpy).
    private void Use(in MirStatement statement)
    {
        var local = statement.Place.Root;
        var operand = this.body.Operands[statement.Rvalue.Start];
        if (operand.Kind == MirOperandKind.Const && this.body.Consts[operand.Arg] is { Kind: MirConstKind.String, Value: string text } &&
            ReferenceEquals(this.types[local], BoundType.String))
        {
            this.Text(local, text);
            return;
        }

        if (Abi.HasResultSlot(this.types[local], this.slots[local].Value) || !this.Operand(operand, out var value))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        if (this.slots[local].Home != Home.None)
        {
            this.Store(local, value);
        }
    }

    // A string constant stored into a string local (the string representation, implementation-defined by IMPL 21.1.3: RuntimeAbi.String):
    // its UTF-8 bytes a pooled constant, the Static release kind, so never released; the empty string has no bytes (null, length 0).
    private void Text(int local, string text)
    {
        var output = this.output;
        output.Write("  store ");
        output.Write(this.slots[local].Value.Layout.StorageType);
        output.Write(" { ptr ");
        if (text.Length == 0)
        {
            output.Write("null, i64 0");
        }
        else
        {
            var constant = this.module.Constants[this.module.Constants.Intern(text, LlvmConstantKind.Text)];
            output.Write('@');
            output.Write(constant.Name);
            output.Write(", i64 ");
            WriteNumber(output, constant.ByteLength);
        }

        output.Write(", i8 ");
        WriteNumber(output, RuntimeAbi.StaticReleaseKind);
        output.Write(" }");
        this.WriteAccess(local);
    }

    // Ref (IMPL 21.1.3: a shared, exclusive, two-phase or raw borrow is one nonnull pointer to its Place's storage): the address the place
    // writer gives, stored into the local.
    private void Ref(int local, MirPlace place, in SourceInfo source)
    {
        if (!this.Address(place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, source);
            return;
        }

        this.Store(local, address);
    }
}
