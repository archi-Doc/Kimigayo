// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial record class KimiFile([property: Key(0)] string File, [property: Key(1)] string[] AliasArray);
