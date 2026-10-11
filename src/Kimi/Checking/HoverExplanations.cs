// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Checking;

/// <summary>General explanations of specified semantics, never use-site legality or proof.</summary>
internal static class HoverExplanations
{
    internal static string Semantics(SemanticsKind kind) => kind switch
    {
        SemanticsKind.Owner => "owner — Direct ownership of a value.",
        SemanticsKind.Ref => "ref — Shared, non-owning access to a value.",
        SemanticsKind.Uniq => "uniq — Exclusive, mutable, non-owning access to a value.",
        SemanticsKind.Obj => "obj — Exclusive ownership of an object.",
        SemanticsKind.Rc => "rc — Shared object ownership with non-atomic reference counting.",
        SemanticsKind.Arc => "arc — Shared object ownership with atomic reference counting. Atomicity concerns the count, not concurrent mutation of the contents.",
        SemanticsKind.ObjRef => "objref — Shared, non-owning access to an object.",
        SemanticsKind.ObjUniq => "objuniq — Exclusive, mutable, non-owning access to an object.",
        SemanticsKind.Raw => "raw — A raw pointer without safe-borrow guarantees.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string Operation(string name, bool fullTarget) => name switch
    {
        "ref" => "Creates a shared borrow of the operand's storage. If the operand stores a reference, this borrows the reference slot." + (fullTarget ? " The written target must match the complete stored Type." : string.Empty),
        "uniq" => "Creates an exclusive, mutable borrow of the operand's storage. If the operand stores a reference, this borrows the reference slot." + (fullTarget ? " The written target must match the complete stored Type." : string.Empty),
        "obj" or "rc" or "arc" => (fullTarget
            ? "Uses the written complete object target for acquisition, eligible object creation or a permitted object upcast."
            : "Infers the object target from the operand. Creates an object from an eligible owned value, or acquires an existing object with the same ownership semantics.") +
            " Does not implicitly move a non-Copy value out of a place or duplicate a strong owner.",
        "objref" => "Obtains shared, non-owning access to an object: borrows an owning handle's object, copies a shared object reference, or reborrows an exclusive object reference." + (fullTarget ? " Uses the written View target, including permitted object upcasts." : " Infers the View target from the operand."),
        "objuniq" => "Obtains exclusive, mutable, non-owning access to an exclusively owned object or reborrows an exclusive object reference." + (fullTarget ? " Uses the written View target, subject to the object upcast rules." : " Infers the View target from the operand."),
        "move" => "Transfers a movable place's value and destruction responsibility, marking it moved even when Copy. A reference is moved, not its referent. A temporary is used as is.",
        "copy" => "Copies the operand's value when its complete Type is proven Copy, keeping a place's value and state. A reference is copied, not its referent; a temporary is used as is.",
        "follow" => "Selects the referent place of a safe value reference without copying or moving it. For an object handle or borrow, exposes the complete payload only with a proven Sealed View target and valid access. An admitted owner/ref/uniq generic pair selects its target place under the pair's capabilities; following does not grant Take.",
        "raw" => fullTarget ? "Uses the explicit raw-pointer target for a permitted pointer conversion or typed null formation. This does not establish safe-borrow guarantees." : "Obtains a raw pointer to the operand's storage. Taking its address does not require an unsafe context or establish safe-borrow guarantees.",
        "wrap" => "Converts an integer or wrapping integer to the written integer target by wrapping modulo the target's bit width. Safe references may supply their numeric value read.",
        "bits" => "Converts between a floating-point Type and a same-width integer or wrapping integer Type while preserving the bit pattern. Safe references may supply their numeric value read.",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
