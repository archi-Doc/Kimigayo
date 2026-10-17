// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the LLVM text of scalar arithmetic and its checks (SPEC 13.3.1, 22.5.2; IMPL 21.3.3; DECISIONS R5-M17): an operation's
// instruction, the overflow intrinsic of a checked one, the zero, minimum / -1 and count checks of division, remainder and
// shifts with the divisor substitutions they need, and each check's own failure block, which calls the runtime Abort with the
// check's reason and location. Both generators write scalar arithmetic through it (PD2: promoted, never copied).
// Input: a writer; a ScalarOperation (operator and Type text, operand spellings, result name), where an operand is its LLVM
// spelling, so a literal -1 is the text -1 and a dynamic operand a local name; for a check, a ScalarCheck (the number naming its
// temporaries and failure label, its success label, its location); for a check another writer computes (a conversion or bounds
// check), its condition and abort reason.
// Output: LLVM text written to the writer.
// Invariants: a check names its temporaries role + Id and its failure label abort + Id; `select i1` appears only on a failure
// block's zero test (its reason) and on a signed divisor's -1 test (its substitution by 1, the wrapping quotient's negation), the
// forms PD8 admits; no nsw or nuw; the legacy writer's .ll stays byte-identical while it shares this text; writing allocates
// nothing.
// Must not take an opcode or check kind of either IR, syntax, HIR, Binding internals, EmissionModule or MIR types, or use LINQ.

#pragma warning disable SA1402 // The operation and check records are this writer's inputs.

using System.Globalization;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>
/// One scalar instruction: its LLVM operator (an instruction such as add, sdiv or fneg, a comparison predicate, or an overflow
/// intrinsic's stem such as sadd) and operand Type, its operands' spellings (the right one empty for fneg) and its result's name.
/// A comparison's predicate belongs to icmp, or to fcmp on a floating Type.
/// </summary>
internal readonly ref struct ScalarOperation(
    string op, string type, ReadOnlySpan<char> left, ReadOnlySpan<char> right, ReadOnlySpan<char> result, bool comparison)
{
    internal string Operator { get; } = op;

    internal string Type { get; } = type;

    internal ReadOnlySpan<char> Left { get; } = left;

    internal ReadOnlySpan<char> Right { get; } = right;

    internal ReadOnlySpan<char> Result { get; } = result;

    internal bool IsComparison { get; } = comparison;
}

/// <summary>
/// One check's failure block: the number that makes its temporaries and failure label unique in the function, the local name of
/// the label where execution continues when the check passes (%b3), and the pooled location constant its Abort reports, or null
/// for the function's forwarded caller location (SPEC 22.5.1).
/// </summary>
internal readonly ref struct ScalarCheck(int id, ReadOnlySpan<char> success, LlvmConstantPool.Entry? location)
{
    internal int Id { get; } = id;

    internal ReadOnlySpan<char> Success { get; } = success;

    internal LlvmConstantPool.Entry? Location { get; } = location;
}

/// <summary>Writes scalar arithmetic and its checks as LLVM text.</summary>
internal static class LlvmScalarText
{
    // The condition of a check that the compiler proved: the failure block stays unreachable.
    internal const string ProvenValid = "false";

    // A check failure carries no operating-system error (the runtime reports one only when it is nonnegative).
    private const string NoOsError = "-2";

    // A role followed by an int.
    private const int NameLength = 24;

    // An operation without a check. SPEC 13.3.1, IMPL 21.3.3: a signed quotient by a literal -1 is the negation and a signed
    // remainder by it divides by 1, so sdiv and srem never see the minimum / -1 pair.
    internal static void WriteOperation(TextWriter output, in ScalarOperation operation)
        => WriteInstruction(output, operation, operation.Right);

    // An integer operation checked through its overflow intrinsic, whose stem is the operator.
    internal static void WriteOverflowChecked(TextWriter output, in ScalarOperation operation, in ScalarCheck check)
    {
        var type = operation.Type;
        WriteName(output, "  %checked", check.Id);
        output.Write(" = call { ");
        output.Write(type);
        output.Write(", i1 } @llvm.");
        output.Write(operation.Operator);
        output.Write(".with.overflow.");
        output.Write(type);
        output.Write('(');
        output.Write(type);
        output.Write(' ');
        output.Write(operation.Left);
        output.Write(", ");
        output.Write(type);
        output.Write(' ');
        output.Write(operation.Right);
        output.Write(")\n  ");
        output.Write(operation.Result);
        WriteCheckedValue(output, type, check.Id);
        output.Write(", 0\n");
        WriteName(output, "  %overflow", check.Id);
        WriteCheckedValue(output, type, check.Id);
        output.Write(", 1\n");
        WriteFailure(output, "%overflow", AbortReasons.IntegerOverflow, check);
    }

    // The signed quotient of an integer Type (SPEC 13.3.1): a zero divisor and the minimum / -1 pair Abort, the reason selected
    // by the zero test into the name reason; a literal -1 keeps only the minimum check and is the negation (IMPL 21.3.3).
    internal static void WriteSignedQuotient(TextWriter output, in ScalarOperation operation, in ScalarCheck check, ReadOnlySpan<char> reason)
    {
        var id = check.Id;
        var minimum = long.MinValue >> (64 - Width(operation.Type));
        if (operation.Right is "-1")
        {
            WriteEquality(output, "%minimum", id, operation.Type, operation.Left, minimum);
            WriteFailure(output, "%minimum", AbortReasons.IntegerOverflow, check);
            WriteInstruction(output, operation, operation.Right);
            return;
        }

        WriteEquality(output, "%zero", id, operation.Type, operation.Right, 0);
        WriteEquality(output, "%minimum", id, operation.Type, operation.Left, minimum);
        WriteEquality(output, "%minusone", id, operation.Type, operation.Right, -1);
        WriteName(output, "  %overflow", id);
        WriteName(output, " = and i1 %minimum", id);
        WriteName(output, ", %minusone", id);
        WriteName(output, "\n  %invalid", id);
        WriteName(output, " = or i1 %zero", id);
        WriteName(output, ", %overflow", id);
        output.Write('\n');
        WriteBranch(output, "%invalid", check);
        output.Write("  ");
        output.Write(reason);
        WriteName(output, " = select i1 %zero", id);
        output.Write(", i32 ");
        WriteNumber(output, AbortReasons.IntegerDivisionZero);
        output.Write(", i32 ");
        WriteNumber(output, AbortReasons.IntegerOverflow);
        output.Write('\n');
        WriteAbort(output, reason, check);
        WriteInstruction(output, operation, operation.Right);
    }

    // An unsigned quotient or any remainder (SPEC 13.3.1): only a zero divisor Aborts. A signed remainder by -1 is 0 for every
    // dividend, so a dynamic divisor of -1 becomes 1 and a literal -1 is written as 1.
    internal static void WriteZeroChecked(TextWriter output, in ScalarOperation operation, in ScalarCheck check)
    {
        WriteEquality(output, "%zero", check.Id, operation.Type, operation.Right, 0);
        WriteFailure(output, "%zero", AbortReasons.IntegerDivisionZero, check);
        if (operation.Operator != "srem" || !IsLocal(operation.Right))
        {
            WriteInstruction(output, operation, operation.Right);
            return;
        }

        Span<char> name = stackalloc char[NameLength];
        WriteInstruction(output, operation, WriteDivisorSubstitution(output, operation, check.Id, name));
    }

    // The signed quotient of a wrapping integer Type (SPEC 13.3.1): only a zero divisor Aborts. The minimum / -1 is the minimum,
    // which sdiv cannot compute, so a divisor of -1 divides as 1 and the quotient is negated; a literal -1 carries no check and is
    // the negation alone (WriteOperation, IMPL 21.3.3).
    internal static void WriteWrappingQuotient(TextWriter output, in ScalarOperation operation, in ScalarCheck check)
    {
        var id = check.Id;
        var type = operation.Type;
        WriteEquality(output, "%zero", id, type, operation.Right, 0);
        WriteFailure(output, "%zero", AbortReasons.IntegerDivisionZero, check);
        Span<char> name = stackalloc char[NameLength];
        var divisor = WriteDivisorSubstitution(output, operation, id, name);
        WriteName(output, "  %q", id);
        output.Write(" = sdiv ");
        output.Write(type);
        output.Write(' ');
        output.Write(operation.Left);
        output.Write(", ");
        output.Write(divisor);
        WriteName(output, "\n  %neg", id);
        output.Write(" = sub ");
        output.Write(type);
        WriteName(output, " 0, %q", id);
        output.Write("\n  ");
        output.Write(operation.Result);
        WriteName(output, " = select i1 %minusone", id);
        output.Write(", ");
        output.Write(type);
        WriteName(output, " %neg", id);
        output.Write(", ");
        output.Write(type);
        WriteName(output, " %q", id);
        output.Write('\n');
    }

    // A shift (SPEC 13.3.1): a count that is negative or not less than the left operand's width Aborts; only a successful count
    // is converted to the left operand's Type. countType is the count's LLVM Type.
    internal static void WriteShift(TextWriter output, in ScalarOperation operation, in ScalarCheck check, string countType)
    {
        var width = Width(operation.Type);
        WriteName(output, "  %invalid", check.Id);
        output.Write(" = icmp uge ");
        output.Write(countType);
        output.Write(' ');
        output.Write(operation.Right);
        output.Write(", ");
        WriteNumber(output, width);
        output.Write('\n');
        WriteFailure(output, "%invalid", AbortReasons.IntegerShiftCount, check);
        var countWidth = Width(countType);
        if (countWidth == width)
        {
            WriteInstruction(output, operation, operation.Right);
            return;
        }

        Span<char> name = stackalloc char[NameLength];
        var count = Format("%shift", check.Id, name);
        output.Write("  ");
        output.Write(count);
        output.Write(countWidth < width ? " = zext " : " = trunc ");
        output.Write(countType);
        output.Write(' ');
        output.Write(operation.Right);
        output.Write(" to ");
        output.Write(operation.Type);
        output.Write('\n');
        WriteInstruction(output, operation, count);
    }

    // The failure block of a check whose condition, the temporary condition + Id, another writer computed, or ProvenValid.
    internal static void WriteFailure(TextWriter output, string condition, int reason, in ScalarCheck check)
    {
        WriteBranch(output, condition, check);
        Span<char> digits = stackalloc char[11];
        reason.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        WriteAbort(output, digits[..length], check);
    }

    // `name + id = icmp eq type operand, constant`.
    internal static void WriteEquality(TextWriter output, string name, int id, string type, ReadOnlySpan<char> operand, long constant)
    {
        output.Write("  ");
        WriteName(output, name, id);
        output.Write(" = icmp eq ");
        output.Write(type);
        output.Write(' ');
        output.Write(operand);
        output.Write(", ");
        WriteNumber(output, constant);
        output.Write('\n');
    }

    // The instruction after its checks; right is the right operand or the divisor or count they prepared. SPEC 13.3.1,
    // IMPL 21.3.3: a signed quotient by a literal -1 is the negation, and a signed remainder by it divides by 1.
    private static void WriteInstruction(TextWriter output, in ScalarOperation operation, ReadOnlySpan<char> right)
    {
        var negated = operation.Operator == "sdiv" && operation.Right is "-1";
        output.Write("  ");
        output.Write(operation.Result);
        output.Write(" = ");
        if (operation.IsComparison)
        {
            output.Write(operation.Type is "float" or "double" ? "fcmp " : "icmp ");
        }

        output.Write(negated ? "sub" : operation.Operator);
        output.Write(' ');
        output.Write(operation.Type);
        output.Write(' ');
        output.Write(negated ? "0" : operation.Left);
        if (operation.Operator == "fneg")
        {
            output.Write('\n');
            return;
        }

        output.Write(", ");
        if (negated)
        {
            output.Write(operation.Left);
        }
        else
        {
            output.Write(operation.Operator == "srem" && right is "-1" ? "1" : right);
        }

        output.Write('\n');
    }

    // SPEC 13.3.1: a dynamic signed divisor of -1 becomes 1, so sdiv and srem never see the minimum / -1 pair. Returns the
    // substituted divisor's name, formatted into name.
    private static ReadOnlySpan<char> WriteDivisorSubstitution(TextWriter output, in ScalarOperation operation, int id, Span<char> name)
    {
        WriteEquality(output, "%minusone", id, operation.Type, operation.Right, -1);
        var divisor = Format("%safe", id, name);
        output.Write("  ");
        output.Write(divisor);
        WriteName(output, " = select i1 %minusone", id);
        output.Write(", ");
        output.Write(operation.Type);
        output.Write(" 1, ");
        output.Write(operation.Type);
        output.Write(' ');
        output.Write(operation.Right);
        output.Write('\n');
        return divisor;
    }

    // Branches to the failure label when the condition holds, else to the success label, and opens the failure label.
    private static void WriteBranch(TextWriter output, string condition, in ScalarCheck check)
    {
        output.Write("  br i1 ");
        if (condition == ProvenValid)
        {
            output.Write(ProvenValid);
        }
        else
        {
            WriteName(output, condition, check.Id);
        }

        WriteName(output, ", label %abort", check.Id);
        output.Write(", label ");
        output.Write(check.Success);
        WriteName(output, "\nabort", check.Id);
        output.Write(":\n");
    }

    // The runtime Abort with the reason and the check's location (SPEC 22.5.1-22.5.2), then the success label.
    private static void WriteAbort(TextWriter output, ReadOnlySpan<char> reason, in ScalarCheck check)
    {
        var abort = RuntimeAbi.Abort;
        output.Write("  call ");
        output.Write(abort.Result);
        output.Write(" @");
        output.Write(abort.Name);
        WriteArgument(output, "(", abort.Parameters[0], reason);
        if (check.Location is { } location)
        {
            WriteArgument(output, ", ", abort.Parameters[1], "@");
            output.Write(location.Name);
            WriteArgument(output, ", ", abort.Parameters[2], default);
            WriteNumber(output, location.ByteLength);
        }
        else
        {
            WriteArgument(output, ", ", abort.Parameters[1], "%location");
            WriteArgument(output, ", ", abort.Parameters[2], "%location_length");
        }

        WriteArgument(output, ", ", abort.Parameters[3], NoOsError);
        output.Write(")\n  unreachable\n");
        output.Write(check.Success[1..]);
        output.Write(":\n");
    }

    private static void WriteArgument(TextWriter output, string separator, in AbiParameter parameter, ReadOnlySpan<char> value)
    {
        output.Write(separator);
        output.Write(parameter.Type);
        output.Write(parameter.Attributes);
        output.Write(' ');
        output.Write(value);
    }

    private static void WriteCheckedValue(TextWriter output, string type, int id)
    {
        output.Write(" = extractvalue { ");
        output.Write(type);
        WriteName(output, ", i1 } %checked", id);
    }

    private static void WriteName(TextWriter output, string role, int id)
    {
        output.Write(role);
        WriteNumber(output, id);
    }

    private static ReadOnlySpan<char> Format(string role, int id, Span<char> name)
    {
        role.CopyTo(name);
        id.TryFormat(name[role.Length..], out var length, default, CultureInfo.InvariantCulture);
        return name[..(role.Length + length)];
    }

    // The bit width of an LLVM integer Type (iN).
    private static int Width(string type) => int.Parse(type.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);

    // LLVM local names begin with %; constants do not.
    private static bool IsLocal(ReadOnlySpan<char> operand) => operand.Length != 0 && operand[0] == '%';
}
