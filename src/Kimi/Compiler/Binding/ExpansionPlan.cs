// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // The kind belongs to its plan.

namespace Kimi.Compiler;

/// <summary>What the MIR builder desugars at a node whose Op is an expansion (plan 5.3): the role of each call that
/// <see cref="CallPlan.Of"/>'s order does not fix, and the argument that stands for an operand the expansion already evaluated, which the
/// builder never evaluates again. The node's other calls are <see cref="CallPlan.Of"/> in their order, its Pattern plan
/// <see cref="BoundMatch.Of"/>, and each consumed child's acquisition its Adjust.</summary>
/// <param name="Kind">The expansion.</param>
/// <param name="First">An index's key resolution; a compound update's getter.</param>
/// <param name="Second">An index's selected element or view call; a compound update's setter.</param>
/// <param name="Evaluated">The argument of the key resolution that reads the length of the receiver the selection evaluated, or of the
/// setter that takes the value the update computed; -1 when none.</param>
/// <param name="Direct">An interpolation passed directly as the one argument of <c>Console.writeLine</c>, whose writes the instance places
/// in a stack region when they are bounded within the profile's limit (utf8-formatting 6.1).</param>
internal readonly record struct ExpansionPlan(ExpansionKind Kind, CallPlan? First = null, CallPlan? Second = null, int Evaluated = -1, bool Direct = false)
{
    /// <summary>Gets the expansion of a published node, or null when its Op is none.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The plan.</returns>
    internal static ExpansionPlan? Of(Koto node)
    {
        var binding = node.CodeContext.Compilation.Binding;
        switch (HirTables.Op(node))
        {
            case HirOp.For:
                return new(ExpansionKind.Iteration);
            case HirOp.Try:
                return new(ExpansionKind.Try);
            case HirOp.Interpolation:
                var output = node.Parent as InvocationKoto;
                return new(ExpansionKind.Format, Direct: node is InterpolatedStringKoto && output is { ArgumentNodes: [var text], BoundCall.Target.CompilerFunction: CompilerFunctionKind.WriteLine } && ReferenceEquals(text, node));
            case HirOp.DictionaryLiteral:
                return new(ExpansionKind.DictionaryLiteral);
            case HirOp.Range:
                return new(ExpansionKind.Range);
            case HirOp.CompoundAssign:
                var target = node is BinaryKoto update ? update.Left : ((UnaryKoto)node).Operand;
                var setter = binding.PropertyCall(target, PropertyAccessorKind.Set)?.CallStorage;
                return new(ExpansionKind.CompoundAssign, binding.PropertyCall(target, PropertyAccessorKind.Get)?.CallStorage, setter, EvaluatedArgument(setter));
            case HirOp.Index or HirOp.Call when node is IndexKoto index:
                var key = binding.ResolvedKeyCall(index)?.CallStorage;
                var selected = binding.IndexerCall(index, HirTables.PlanOf(index).Mode != 0) ?? binding.ViewRangeCall(index);
                return new(ExpansionKind.Index, key, selected?.CallStorage, EvaluatedArgument(key));
            default:
                return null;
        }
    }

    private static int EvaluatedArgument(CallPlan? call)
    {
        for (var i = 0; call is not null && i < call.ArgumentOperations.Length; i++)
        {
            if (call.ArgumentOperations[i].Source is EvaluatedKoto or MemberAccessKoto { Left: EvaluatedKoto })
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>The expansions the MIR builder desugars (plan 5.3); each names its calls in <see cref="CallPlan.Of"/>'s order. A step without
/// a call is served as the rows of <see cref="IntrinsicDisposition"/> are (plan 5.8): an interpolation's buffer, hints and end are the
/// temporary intrinsics FormatCapacity, FormatHint and FormatFinish over the estimates its instance computes (utf8-formatting 5.1, 6.1), and
/// a failed write a Fail of KIMI_E_FORMAT; a dictionary literal's duplicate key (found by the requireAbsent body) a Fail of
/// KIMI_E_DUPLICATE_KEY and its insertion the placeEntry body; a `^x` key the receiver's MIR length minus x.</summary>
internal enum ExpansionKind : byte
{
    Iteration, // A for loop: a user loop's entry and step calls with its Some/None Pattern plan; a ResolvedRange loop has none and binds each isize position.
    Try, // Its Pattern plan's arms: the payload, or the failure the generated return transfers (SPEC 17.2.4).
    Format, // An interpolation's buffer and adapter calls, then its writes; a $tryWrite's writes, then its status (utf8-formatting 5).
    DictionaryLiteral, // Its key lookup (SPEC 12.3.4), for each entry before its insertion.
    Range, // A key applied directly: each written boundary an isize position, a `^x` one the length of the selection's receiver minus x.
    Index, // A built-in element or slice, by its key or its key's resolution, or a user or view selection's selected call (SPEC 4.6.9).
    CompoundAssign, // Its Place, or its Property's getter and setter, read and written once; its arithmetic call, or a primitive operation.
}
