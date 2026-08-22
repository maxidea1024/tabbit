using System;

namespace Tabbit.Caching;

/// <summary>
/// What one command line option means to the build cache.
/// </summary>
/// <remarks>
/// Every option is classified, and the classification lives on the option itself. A list
/// kept somewhere else is a list that drifts when an option is added, and the drift is
/// silent in the direction that matters: an option that changes the output but was never
/// added to the key produces a run that reuses somebody else's answer.
///
/// <see cref="CacheAttribute"/> is required rather than defaulted, and a test fails when a
/// property is missing one - so the person adding an option decides this, at the moment they
/// have the context to decide it.
/// </remarks>
public enum CacheRelevance
{
    /// <summary>
    /// Says which cache this run is about, rather than what is in it.
    /// </summary>
    Identity,

    /// <summary>
    /// Changes what the conversion produces. Part of the key.
    /// </summary>
    Output,

    /// <summary>
    /// Changes what validation checks, and nothing about the output.
    /// </summary>
    Validation,

    /// <summary>
    /// Names the commit a snapshot is filed under.
    /// </summary>
    /// <remarks>
    /// In the key only when the recipe has a target that records one. A conversion with no
    /// such target does not care which commit it is of, and putting the commit in the key
    /// unconditionally would mean every commit is a full run for every project - which is
    /// most of them, and none of them would benefit.
    /// </remarks>
    Commit,

    /// <summary>
    /// Does not touch the output at all.
    /// </summary>
    /// <remarks>
    /// The logging switches. Putting these in the key would mean that turning on `--verbose`
    /// to look into a slow run is what makes that run slow - the flag would invalidate the
    /// cache it was used to inspect.
    /// </remarks>
    Irrelevant,

    /// <summary>
    /// Decides how the cache itself is used. Never part of a key.
    /// </summary>
    Control,

    /// <summary>
    /// Belongs to something other than a conversion - a query, a scaffold, a server.
    /// </summary>
    /// <remarks>
    /// Those paths return before any cache is consulted, so these options are reached only
    /// when the run is not a conversion at all.
    /// </remarks>
    NotAConversion,
}

/// <summary>
/// Declares what an option means to the build cache. See <see cref="CacheRelevance"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CacheAttribute : Attribute
{
    public CacheAttribute(CacheRelevance relevance)
    {
        Relevance = relevance;
    }

    /// <summary>How this option is treated.</summary>
    public CacheRelevance Relevance { get; }
}
