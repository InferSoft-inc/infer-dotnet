using System.Globalization;

namespace Infersoft;

/// <summary>
/// Raised by <c>Jobs.Run(maxCredits: …)</c> when the estimate exceeds the budget. The job is
/// <b>not</b> started; the offending <see cref="Estimate"/> is attached (inspect its credits or
/// start it explicitly via <c>Jobs.Start(estimate.Id)</c>).
/// </summary>
public sealed class CreditsLimitExceededException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="CreditsLimitExceededException"/> class.</summary>
    /// <param name="estimate">The estimate that exceeded the budget.</param>
    /// <param name="maxCredits">The budget that was exceeded.</param>
    public CreditsLimitExceededException(CreditsEstimate estimate, int maxCredits)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "estimated cost of {0} credits exceeds maxCredits={1}; job not started",
            estimate?.TotalCredits ?? 0,
            maxCredits))
    {
        Estimate = estimate!;
        MaxCredits = maxCredits;
    }

    /// <summary>The estimate that exceeded the budget.</summary>
    public CreditsEstimate Estimate { get; }

    /// <summary>The budget that was exceeded.</summary>
    public int MaxCredits { get; }
}
