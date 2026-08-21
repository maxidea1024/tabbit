namespace Tabbit;

/// <summary>
/// What this program's exit code means.
/// </summary>
/// <remarks>
/// Two of these have always existed and were written as literals. The third is the one worth
/// a type: a build pipeline wants to know whether there is anything new to publish, and
/// "the conversion succeeded" does not answer that once a run can decide it has nothing to
/// do.
///
/// It is behind <see cref="Options.DetailedExitCode"/> rather than always returned, because
/// almost everything that invokes a command line tool treats any non-zero code as a failure.
/// A skipped run is not a failure, so making it non-zero by default would break every script
/// that chains a step after this one - and it would break them the day the cache first
/// worked, which is the worst possible day to look like a new bug.
/// </remarks>
public static class ExitCode
{
    /// <summary>The run did what it was asked to.</summary>
    public const int Success = 0;

    /// <summary>The run failed, and said why.</summary>
    public const int Failed = 1;

    /// <summary>
    /// Nothing had changed, so nothing was produced. Only with
    /// <see cref="Options.DetailedExitCode"/>.
    /// </summary>
    public const int NothingToDo = 2;
}
