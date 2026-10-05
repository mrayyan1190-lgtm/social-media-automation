namespace SocialMediaAutomation.Domain.Enums;

/// <summary>Campaign lifecycle only; approval and publishing states live on Post and PublicationJob.</summary>
public enum CampaignStatus
{
    /// <summary>Input is being captured; no content has been requested yet.</summary>
    Draft,

    /// <summary>AI generation has been requested at least once.</summary>
    Active,

    Archived,
}
