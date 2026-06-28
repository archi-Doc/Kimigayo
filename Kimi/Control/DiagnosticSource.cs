// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Diagnostics;

public sealed record class DiagnosticSource(DiagnosticCollection DiagnosticCollection, SourceRange Range, Kotonoha kotonoha, uint SourceId);
