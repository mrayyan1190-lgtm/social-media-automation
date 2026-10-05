using SocialMediaAutomation.Application.AiGeneration.Contracts;
using SocialMediaAutomation.Application.AiGeneration.Validation;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Tests;

public class AiValidationTests
{
    private static readonly Guid FactId = Guid.NewGuid();
    private static readonly SocialPlatform[] All = [SocialPlatform.LinkedIn, SocialPlatform.Facebook, SocialPlatform.Instagram];

    private static string Variant(string platform, string body = "Body", string language = "en") =>
        $$"""{"platform":"{{platform}}","language":"{{language}}","hook":"Hook","body":"{{body}}","cta":"Book a demo","hashtags":["Ops"],"factIdsUsed":[]}""";

    private static string Content(params string[] variants) => $$"""{"schemaVersion":"content-v1","variants":[{{string.Join(",", variants)}}]}""";

    // ---- Structural validation ----------------------------------------------------------------------

    [Fact]
    public void Valid_output_yields_one_variant_per_requested_platform()
    {
        var result = OutputParser.ParseContent(Content(Variant("LinkedIn"), Variant("facebook"), Variant("Instagram")), All);

        Assert.Equal(All.Order(), result.Keys.Order());
    }

    [Fact]
    public void Markdown_fenced_json_is_accepted() =>
        Assert.Single(OutputParser.ParseContent("```json\n" + Content(Variant("LinkedIn")) + "\n```", [SocialPlatform.LinkedIn]));

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"variants\": []}")]
    [InlineData("{\"schemaVersion\":\"content-v9\",\"variants\":[]}")]
    public void Non_conforming_output_is_rejected(string raw) =>
        Assert.Throws<AiOutputInvalidException>(() => OutputParser.ParseContent(raw, [SocialPlatform.LinkedIn]));

    [Fact]
    public void Missing_requested_platform_is_rejected()
    {
        var error = Assert.Throws<AiOutputInvalidException>(() => OutputParser.ParseContent(Content(Variant("LinkedIn"), Variant("Facebook")), All));
        Assert.Contains("Instagram", error.Message);
    }

    [Theory]
    [InlineData("TikTok")]
    [InlineData("Facebook")] // valid platform, but not requested
    public void Unknown_or_unrequested_platform_is_rejected(string platform) =>
        Assert.Throws<AiOutputInvalidException>(() => OutputParser.ParseContent(Content(Variant("LinkedIn"), Variant(platform)), [SocialPlatform.LinkedIn]));

    [Fact]
    public void Duplicate_platform_or_empty_body_is_rejected()
    {
        Assert.Throws<AiOutputInvalidException>(() => OutputParser.ParseContent(Content(Variant("LinkedIn"), Variant("LinkedIn")), [SocialPlatform.LinkedIn]));
        Assert.Throws<AiOutputInvalidException>(() => OutputParser.ParseContent(Content(Variant("LinkedIn", body: " ")), [SocialPlatform.LinkedIn]));
    }

    // ---- Deterministic content checks ----------------------------------------------------------------

    private static AiInputSnapshot Snapshot() => new(
        AiVersions.Snapshot, Guid.NewGuid(), "en",
        new BrandSnapshot("Brand", "Software", "We build reporting software for a faster month-end.", "Finance leaders", BrandTone.Professional,
            ["guaranteed", "#1"], DateTime.UtcNow),
        new CampaignSnapshot(Guid.NewGuid(), "Launch", CampaignObjective.LEAD_GENERATION, "Close the books in 3 days", "Finance leaders",
            CallToActionType.BOOK_DEMO, "Book a demo", "https://brand.example/demo", BrandTone.Professional, ContentLength.MEDIUM, HashtagMode.AUTO, EmojiMode.AUTO),
        [new FactSnapshot(FactId, "Used by 40 customers, who cut close time by 30%", "CRM export")],
        All, null);

    private static PostVersion Version(SocialPlatform platform, string body, string[]? hashtags = null)
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), new CampaignDetails("C", CampaignObjective.LEAD_GENERATION, "K", "A"), null, Guid.NewGuid(), DateTime.UtcNow);
        campaign.SetPlatforms(All, Guid.NewGuid(), DateTime.UtcNow);
        return Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign, platform, new PostContent("Hook", body, "Book a demo", null, hashtags ?? [], null, null),
            null, null, PostVersionSource.AiGeneration, Guid.NewGuid(), DateTime.UtcNow).Version;
    }

    private static IReadOnlyList<EvaluationIssue> Check(string body, string language = "en", List<string>? factIds = null, SocialPlatform platform = SocialPlatform.LinkedIn, string[]? hashtags = null) =>
        DeterministicValidator.Validate(Snapshot(), platform, Version(platform, body, hashtags), new GeneratedVariant { Language = language, FactIdsUsed = factIds });

    [Fact]
    public void Clean_supported_text_has_no_critical_issues() =>
        Assert.DoesNotContain(Check("Teams cut close time by 30% with our reports.", factIds: [FactId.ToString()]), i => i.Severity == IssueSeverity.CRITICAL);

    [Theory]
    [InlineData("Results guaranteed for every team.", "guaranteed")]
    [InlineData("The #1 tool for finance.", "#1")]
    public void Prohibited_terms_are_critical(string body, string term) =>
        Assert.Contains(Check(body), i => i is { Severity: IssueSeverity.CRITICAL, Category: "PROHIBITED_TERM" } && i.Excerpt == term);

    [Fact]
    public void Prohibited_term_inside_another_word_is_not_flagged() =>
        Assert.DoesNotContain(Check("Our guaranteedly clear reports."), i => i.Category == "PROHIBITED_TERM");

    [Fact]
    public void Embedded_url_is_critical() =>
        Assert.Contains(Check("Read more at https://evil.example/x today."), i => i is { Severity: IssueSeverity.CRITICAL, Category: "URL" });

    [Fact]
    public void Unknown_fact_reference_is_critical() =>
        Assert.Contains(Check("Plain text.", factIds: [Guid.NewGuid().ToString()]), i => i is { Severity: IssueSeverity.CRITICAL, Category: "FACT_REFERENCE" });

    [Fact]
    public void Wrong_language_is_critical_but_regional_variant_is_fine()
    {
        Assert.Contains(Check("Texte en français.", language: "fr"), i => i is { Severity: IssueSeverity.CRITICAL, Category: "LANGUAGE" });
        Assert.DoesNotContain(Check("Plain text.", language: "en-GB"), i => i.Category == "LANGUAGE");
    }

    [Fact]
    public void Figures_absent_from_facts_are_flagged_as_possible_unsupported_claims()
    {
        var issues = Check("Customers save 50% and pay only $99, with 30% faster close.");

        Assert.Contains(issues, i => i is { Category: "UNSUPPORTED_CLAIM", Excerpt: "50%" });
        Assert.Contains(issues, i => i is { Category: "UNSUPPORTED_CLAIM", Excerpt: "$99" });
        Assert.DoesNotContain(issues, i => i.Excerpt == "30%"); // stated in a supplied fact
    }

    [Fact]
    public void Instagram_without_image_gets_media_warning() =>
        Assert.Contains(Check("Visual copy.", platform: SocialPlatform.Instagram), i => i is { Severity: IssueSeverity.WARNING, Category: "MEDIA" });
}
