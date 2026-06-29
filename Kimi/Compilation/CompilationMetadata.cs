// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

namespace Kimigayo.Diagnostics;

public sealed record class CompilationMetadata(DiagnosticCollection DiagnosticCollection, SourceRange Range, Kotonoha kotonoha, uint SourceId);
