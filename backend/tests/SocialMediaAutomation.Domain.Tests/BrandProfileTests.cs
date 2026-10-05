using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class BrandProfileTests
{
    private static BrandProfileDetails Details(
        string description = "We build software.", string audience = "Finance leaders", BrandTone tone = BrandTone.Professional,
        string? website = "https://brand.example", string[]? prohibited = null) =>
        new(description, audience, tone, website, prohibited ?? ["guaranteed", "Guaranteed", " "]);

    private static BrandProfile Create(BrandProfileDetails details) =>
        BrandProfile.Create(Guid.NewGuid(), Guid.NewGuid(), details, Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public void Saves_the_four_inputs_and_deduplicates_prohibited_terms()
    {
        var profile = Create(Details(tone: BrandTone.Friendly));

        Assert.Equal(("We build software.", "Finance leaders", BrandTone.Friendly, "https://brand.example"),
            (profile.BusinessDescription, profile.TargetAudience, profile.BrandTone, profile.Website));
        Assert.Equal(["guaranteed"], profile.ProhibitedTerms);
    }

    [Theory]
    [InlineData("", "Audience")]
    [InlineData("Description", " ")]
    public void Description_and_audience_are_required(string description, string audience) =>
        AssertInvalid(() => Create(Details(description, audience)));

    [Fact]
    public void Undefined_tone_is_rejected() => AssertInvalid(() => Create(Details(tone: (BrandTone)42)));

    [Theory]
    [InlineData("brand.example")]
    [InlineData("mailto:hello@brand.example")]
    public void Website_must_be_an_absolute_http_url(string website) => AssertInvalid(() => Create(Details(website: website)));

    [Fact]
    public void Website_and_prohibited_terms_are_optional()
    {
        var profile = Create(new BrandProfileDetails("We build software.", "Finance leaders", BrandTone.Casual, ""));
        Assert.Null(profile.Website);
        Assert.Empty(profile.ProhibitedTerms);
    }

    [Fact]
    public void Too_many_prohibited_terms_are_rejected() =>
        AssertInvalid(() => Create(Details(prohibited: Enumerable.Range(0, 101).Select(i => $"term{i}").ToArray())));

    private static void AssertInvalid(Action action) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(action).Code);
}
