// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public enum ReferenceKind : byte
{
    None,
    Ref, // "ref" In-stack reference
    Amp, // "&" In-scope reference
    Own, // "&own" Owner reference
    Unsafe, // "&unsafe" Unsafe reference
    Rc, // "&rc" Out-scope reference with counter
    Arc, // "&arc" Out-thread reference with atomic counter
}
