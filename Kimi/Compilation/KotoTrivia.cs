// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

namespace Kimigayo.Diagnostics;

public sealed record class KotoTrivia(DiagnosticCollection DiagnosticCollection, Kotonoha kotonoha, int SourceId, SourceRange Range);
