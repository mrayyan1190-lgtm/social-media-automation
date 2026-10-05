namespace SocialMediaAutomation.Domain.Enums;

public enum CampaignObjective
{
    BRAND_AWARENESS,
    PRODUCT_LAUNCH,
    LEAD_GENERATION,
    ENGAGEMENT,
    WEBSITE_TRAFFIC,
    ANNOUNCEMENT,
    EDUCATIONAL,

    /// <summary>"Auto Detect": the AI infers the objective from the campaign prompt.</summary>
    AUTO,
}

public enum CallToActionType
{
    NONE,
    LEARN_MORE,
    VISIT_WEBSITE,
    BOOK_DEMO,
    SIGN_UP,
    CONTACT_US,
    REGISTER_NOW,
    DOWNLOAD,

    /// <summary>"Auto Detect": the AI writes the CTA that fits the prompt. No fixed wording and no required link.</summary>
    AUTO,
}

public enum ContentLength
{
    SHORT,
    MEDIUM,
    LONG,
}

public enum HashtagMode
{
    AUTO,
    NONE,
}

public enum EmojiMode
{
    AUTO,
    NONE,
}

public static class CallToActions
{
    /// <summary>CTAs that send the reader somewhere, so they need a destination link. CONTACT_US may point at a phone or inbox.</summary>
    public static bool RequiresUrl(CallToActionType type) => type is not (CallToActionType.NONE or CallToActionType.CONTACT_US or CallToActionType.AUTO);

    /// <summary>Default wording for the post's CTA line; null for NONE and AUTO.</summary>
    public static string? Text(CallToActionType type) => type switch
    {
        CallToActionType.LEARN_MORE => "Learn more",
        CallToActionType.VISIT_WEBSITE => "Visit our website",
        CallToActionType.BOOK_DEMO => "Book a demo",
        CallToActionType.SIGN_UP => "Sign up",
        CallToActionType.CONTACT_US => "Contact us",
        CallToActionType.REGISTER_NOW => "Register now",
        CallToActionType.DOWNLOAD => "Download",
        _ => null,
    };
}
