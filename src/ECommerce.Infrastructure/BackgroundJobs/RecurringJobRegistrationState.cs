namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class RecurringJobRegistrationState : IRecurringJobRegistrationState
{
    private readonly object _gate = new();
    private bool _isRegistered;
    private DateTimeOffset? _lastAttemptUtc;
    private DateTimeOffset? _lastSuccessUtc;
    private string? _lastError;

    public bool IsRegistered
    {
        get { lock (_gate) return _isRegistered; }
    }

    public DateTimeOffset? LastAttemptUtc
    {
        get { lock (_gate) return _lastAttemptUtc; }
    }

    public DateTimeOffset? LastSuccessUtc
    {
        get { lock (_gate) return _lastSuccessUtc; }
    }

    public string? LastError
    {
        get { lock (_gate) return _lastError; }
    }

    public void ReportAttempt(DateTimeOffset attemptedAtUtc)
    {
        lock (_gate)
        {
            _lastAttemptUtc = attemptedAtUtc;
        }
    }

    public void ReportSuccess(DateTimeOffset succeededAtUtc)
    {
        lock (_gate)
        {
            _isRegistered = true;
            _lastSuccessUtc = succeededAtUtc;
            _lastError = null;
        }
    }

    public void ReportFailure(string error, DateTimeOffset failedAtUtc)
    {
        lock (_gate)
        {
            _isRegistered = false;
            _lastAttemptUtc = failedAtUtc;
            _lastError = error;
        }
    }
}
