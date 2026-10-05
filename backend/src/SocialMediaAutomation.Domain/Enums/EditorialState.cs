namespace SocialMediaAutomation.Domain.Enums;

/// <summary>
/// Post editorial state (PRD §18). Members are spelled exactly as the PRD names them so the database, API and UI
/// share one canonical representation. Delivery states never belong here; they live on PublicationJob.
/// </summary>
public enum EditorialState
{
    DRAFT,
    IN_REVIEW,
    CHANGES_REQUESTED,
    APPROVED,
    ARCHIVED,
}
