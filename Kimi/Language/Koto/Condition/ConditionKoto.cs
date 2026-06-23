// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;

namespace Kimigayo.Language;

[TinyhandUnion((int)KotoKind.ConditionNot, typeof(ConditionNegateKoto))]
public abstract partial class ConditionKoto : Koto
{
}
