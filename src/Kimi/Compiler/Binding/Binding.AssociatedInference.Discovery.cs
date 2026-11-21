// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The associated-inference owner's discovery (R7-C4, PLAN G172; design Revision 2 §3.2-§3.3; the owner's O-484 (1 a)): once per pass, before
// any inferred binding is read, what each inferring definition can read and the components the inferring definitions form under it.
// - Inputs: the pass's inferring definitions (the batches with paths, PrepareAssociatedInference) and their members' Types as written: each
//   function's result and parameters, in the definition's container and in each conditional block (SPEC 8.5.5: the declared results and
//   the identification keys are a source's reads).
// - Outputs: per batch its Heads (the definitions its written Types name, at any depth) and Reads (the inferring definitions it can read:
//   a projection's subject definition; for a value-dependent projection, one whose subject is itself a projection, every inferring
//   definition among the Heads of the definition that subject reads and of what that definition reads, closed: SPEC 8.5.5 Extraction
//   reads the subject's value first); the components of the inferring definitions under Reads in Tarjan's emission order (each after
//   every component it reads), and each batch's component (AssociatedInference.Component).
// - Invariants: a function of the declarations, whichever order Tarjan visits; no inferred binding, normalization, identification or
//   proof is read; Reads only grows to its fixpoint. A generic definition's parameter names no definition: its argument's definitions are
//   among the subject's Heads (D2-B1). The walks keep their work on the batches' lists and this owner's stack (no host recursion per
//   definition: a chain of 10,000 definitions is 10,000 steps).
// - Prohibitions: no read of an inferred value; no state but the batches' discovery fields and the lists below, reset here each pass.
// Until R7-C4's cutover the components only count: a read the batches make outside the reader's Reach (ResolveAssociated) is counted in
// InferenceReachMisses, which the rows assert is zero (the validator, enforced by the cutover).
public sealed partial class Binding
{
    // The inferring definitions of the pass in Tarjan's emission order (each component after every component it reads).
    private readonly List<AssociatedInference> inferenceComponents = new();

    // Discovery's work stack (Tarjan's stack, then the Reach walk's), kept with its capacity across passes.
    private readonly List<AssociatedInference> inferenceWork = new();

    private readonly Stack<AssociatedInference> inferenceTarjan = new();

    private int inferenceVisit;

    // Reads the batches made outside the reader's Reach in this compilation (the validator's count until the cutover enforces it).
    internal int InferenceReachMisses { get; private set; }

    private void DiscoverInferenceComponents()
    {
        this.inferenceComponents.Clear();
        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            if (batches.Current is { } batch && batch.Paths.Count > 0)
            {
                batch.Heads.Clear();
                batch.Reads.Clear();
                batch.Dependent.Clear();
                batch.Index = -1;
                batch.Component = -1;
                var container = (DeclarationContainerKoto)batch.Type.Declaration;
                for (var i = 0; i < container.Members.Count; i++)
                {
                    this.DiscoverMember(batch, container.Members[i]);
                }
            }
        }

        // A value-dependent projection reads every inferring definition among the Heads its subject's definition can name, and what that
        // definition reads, to the fixpoint (Reads only grows; the inferring definitions are finite).
        for (var changed = true; changed;)
        {
            changed = false;
            for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
            {
                if (batches.Current is { } batch && batch.Paths.Count > 0)
                {
                    for (var i = 0; i < batch.Dependent.Count; i++)
                    {
                        changed |= this.AddHeadReads(batch, batch.Dependent[i]);
                    }
                }
            }
        }

        var counter = 0;
        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            if (batches.Current is { } batch && batch.Paths.Count > 0 && batch.Index < 0)
            {
                this.ConnectInference(batch, ref counter);
            }
        }
    }

    private void DiscoverMember(AssociatedInference batch, Koto member)
    {
        switch (member)
        {
            case FunctionKoto function:
                if (function.ReturnType is { BoundType: { } result })
                {
                    this.DiscoverWritten(batch, result);
                }

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].Type is { BoundType: { } parameter })
                    {
                        this.DiscoverWritten(batch, parameter);
                    }
                }

                break;
            case SyntaxFormKoto syntax when TryConditionalBlock(syntax, out var block):
                for (var i = 0; i < block.Items.Count; i++)
                {
                    this.DiscoverMember(batch, block.Items[i]);
                }

                break;
        }
    }

    private void DiscoverWritten(AssociatedInference batch, BoundType type)
    {
        if (type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && type.Symbol is { Declaration: StructKoto or EnumKoto } head &&
            !batch.Heads.Contains(head))
        {
            batch.Heads.Add(head);
        }

        if (type.Kind == BoundTypeKind.AssociatedProjection && type.Components.Count > 0)
        {
            var subject = type.Components[0];
            if (this.InferringDefinition(subject) is { } read && !ReferenceEquals(read, batch) && !batch.Reads.Contains(read))
            {
                batch.Reads.Add(read);
            }

            if (subject.Kind == BoundTypeKind.AssociatedProjection && subject.Components.Count > 0 &&
                this.InferringDefinition(subject.Components[0]) is { } dependent && !batch.Dependent.Contains(dependent))
            {
                batch.Dependent.Add(dependent);
            }
        }

        foreach (var component in (BoundType[])type.Components)
        {
            this.DiscoverWritten(batch, component);
        }
    }

    // The inferring definition a Type names as its head, or none.
    private AssociatedInference? InferringDefinition(BoundType type)
        => type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed && type.Symbol is { Declaration: StructKoto or EnumKoto } symbol &&
            this.associatedInference.TryGetValue(symbol, out var batch) && batch.Paths.Count > 0 ? batch : null;

    // Adds to `batch`'s Reads the inferring definitions among `source`'s Heads and Reads; whether any was new.
    private bool AddHeadReads(AssociatedInference batch, AssociatedInference source)
    {
        var added = false;
        for (var i = 0; i < source.Heads.Count; i++)
        {
            if (this.associatedInference.TryGetValue(source.Heads[i], out var head) && head.Paths.Count > 0 && !ReferenceEquals(head, batch) &&
                !batch.Reads.Contains(head))
            {
                batch.Reads.Add(head);
                added = true;
            }
        }

        for (var i = 0; i < source.Reads.Count; i++)
        {
            if (!ReferenceEquals(source.Reads[i], batch) && !batch.Reads.Contains(source.Reads[i]))
            {
                batch.Reads.Add(source.Reads[i]);
                added = true;
            }
        }

        if (!ReferenceEquals(source, batch) && !batch.Reads.Contains(source))
        {
            batch.Reads.Add(source);
            added = true;
        }

        return added;
    }

    // Tarjan's algorithm over the inferring definitions under Reads, iterative on this owner's work list (a batch's Cursor is its next
    // read); a component is emitted after every component it reads.
    private void ConnectInference(AssociatedInference root, ref int counter)
    {
        var stack = this.inferenceWork;
        stack.Clear();
        var tarjan = this.inferenceTarjan;
        Enter(root, ref counter);
        while (stack.Count > 0)
        {
            var batch = stack[^1];
            if (batch.Cursor < batch.Reads.Count)
            {
                var read = batch.Reads[batch.Cursor++];
                if (read.Index < 0)
                {
                    Enter(read, ref counter);
                }
                else if (read.OnStack)
                {
                    batch.Low = Math.Min(batch.Low, read.Index);
                }

                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            if (stack.Count > 0)
            {
                stack[^1].Low = Math.Min(stack[^1].Low, batch.Low);
            }

            if (batch.Low == batch.Index)
            {
                var component = this.inferenceComponents.Count == 0 ? 0 : this.inferenceComponents[^1].Component + 1;
                AssociatedInference member;
                do
                {
                    member = tarjan.Pop();
                    member.OnStack = false;
                    member.Component = component;
                    this.inferenceComponents.Add(member);
                }
                while (!ReferenceEquals(member, batch));
            }
        }

        void Enter(AssociatedInference entered, ref int next)
        {
            entered.Index = entered.Low = next++;
            entered.Cursor = 0;
            entered.OnStack = true;
            tarjan.Push(entered);
            stack.Add(entered);
        }
    }

    // The validator (counted until the cutover): a batch collecting its evidence reads another inferring definition's binding only within its
    // Reach (its Reads, closed transitively).
    private void CountInferenceRead(BindingSymbol readType)
    {
        if (this.collectingAssociated is not { Component: >= 0 } reader || ReferenceEquals(reader.Type, readType) ||
            !this.associatedInference.TryGetValue(readType, out var read) || read.Paths.Count == 0 || this.Reaches(reader, read))
        {
            return;
        }

        this.InferenceReachMisses++;
    }

    // Whether `to` is in `from`'s Reach: Reads closed transitively, walked on the work list with a visit stamp (no allocation).
    private bool Reaches(AssociatedInference from, AssociatedInference to)
    {
        var visit = ++this.inferenceVisit;
        var work = this.inferenceWork;
        work.Clear();
        work.Add(from);
        from.Visit = visit;
        while (work.Count > 0)
        {
            var batch = work[^1];
            work.RemoveAt(work.Count - 1);
            for (var i = 0; i < batch.Reads.Count; i++)
            {
                var read = batch.Reads[i];
                if (ReferenceEquals(read, to))
                {
                    return true;
                }

                if (read.Visit != visit)
                {
                    read.Visit = visit;
                    work.Add(read);
                }
            }
        }

        return false;
    }
}
