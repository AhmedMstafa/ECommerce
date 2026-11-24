namespace ECommerce.UseCases.Common.Auth;

public enum GoogleLoginDecision
{
    CreateNewUser,
    UseLinkedUser,
    RejectUnlinkedEmailOwner
}

/// <summary>
/// Pure rules for Google account linking. Keeping these decisions independent
/// of Identity makes the security-critical "no email-only linking" rule easy
/// to unit test.
/// </summary>
public static class GoogleLinkingRules
{
    public static GoogleLoginDecision DecideLogin(bool hasLinkedUser, bool hasEmailOwner)
    {
        if (hasLinkedUser)
        {
            return GoogleLoginDecision.UseLinkedUser;
        }

        // An existing account is never linked automatically based on email.
        return hasEmailOwner
            ? GoogleLoginDecision.RejectUnlinkedEmailOwner
            : GoogleLoginDecision.CreateNewUser;
    }

    public static bool CanExplicitlyLink(string? authenticatedUserEmail, string verifiedGoogleEmail)
    {
        return !string.IsNullOrWhiteSpace(authenticatedUserEmail) &&
               string.Equals(authenticatedUserEmail, verifiedGoogleEmail, StringComparison.OrdinalIgnoreCase);
    }
}
