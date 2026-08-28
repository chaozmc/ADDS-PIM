namespace ADDS.PIM.Contracts.Notifications;

/// <summary>Well-known <c>NotificationTemplateEntity.TemplateKey</c> values, shared by the enqueue code, the
/// admin API, and the admin UI so all three always agree on the same key string.</summary>
public static class NotificationTemplateKeys
{
    public const string MembershipRequestOutcome = "membership-request-outcome";
    public const string RequesterOutcomeNotification = "requester-outcome-notification";
    public const string ApprovalPendingNotification = "approval-pending-notification";
    public const string ApprovalDecisionNotification = "approval-decision-notification";
}
