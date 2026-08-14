using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// A bounded persistence failure without an unsafe path or inner exception.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Arbitrary messages and inner exceptions could expose filesystem paths or secrets.")]
public sealed class ActivationJournalException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActivationJournalException"/> class.
    /// </summary>
    internal ActivationJournalException()
        : base("Activation journal persistence failed.")
    {
    }
}
