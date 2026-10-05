namespace SocialMediaAutomation.Domain.Enums;

/// <summary>Destination execution mode (PRD §23). Snapshotted onto each publication job; never silently changed.</summary>
public enum ExecutionMode
{
    LIVE,
    TEST_ACCOUNT,
    SIMULATED,
    NOT_CONFIGURED,
}

public enum ConnectionStatus
{
    CONNECTED,
    REAUTHORIZATION_REQUIRED,
    DISCONNECTED,
}

/// <summary>Outcome of one review of one exact PostVersion.</summary>
public enum ReviewStatus
{
    PENDING,
    APPROVED,
    CHANGES_REQUESTED,
}

/// <summary>In-app notification types (PRD §31). Notifications are workflow feedback, not the audit log.</summary>
public enum NotificationType
{
    REVIEW_REQUESTED,
    CHANGES_REQUESTED,
    APPROVED,
    SCHEDULED,
    PUBLISHED,
    SIMULATED,
    FAILED,
    ACTION_REQUIRED,
    REAUTHORIZATION_REQUIRED,
    RECONCILIATION_REQUIRED,
    TEAM_INVITATION,
}
