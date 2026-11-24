namespace ECommerce.Infrastructure.BackgroundJobs;

public interface IRecurringJobRegistrationState
{
    bool IsRegistered { get; }
    DateTimeOffset? LastAttemptUtc { get; }
    DateTimeOffset? LastSuccessUtc { get; }
    string? LastError { get; }
    void ReportAttempt(DateTimeOffset attemptedAtUtc);
    void ReportSuccess(DateTimeOffset succeededAtUtc);
    void ReportFailure(string error, DateTimeOffset failedAtUtc);
}
