using System;

namespace JETHelper.Diagnostics.Models;

/// <summary>
/// One structured diagnostic event kept in memory and, when enabled, written
/// to JETHelper.log.
/// </summary>
public sealed record DiagnosticEntry(DateTimeOffset Timestamp,
                                     DiagnosticLevel Level,
                                     string Category,
                                     string Message);
