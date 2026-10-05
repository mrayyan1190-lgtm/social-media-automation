using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class OrganizationTests
{
    private static Organization Create(string name = "Acme", string? language = "en", string timeZone = "Europe/London", string industry = "Retail") =>
        Organization.Create(Guid.NewGuid(), name, industry, language, timeZone, true, DateTime.UtcNow);

    [Fact]
    public void Create_sets_active_status_and_settings()
    {
        var organization = Create(language: "en-US", timeZone: "America/New_York");

        Assert.Equal(OrganizationStatus.Active, organization.Status);
        Assert.Equal("en-US", organization.DefaultLanguage);
        Assert.Equal("America/New_York", organization.TimeZoneId);
        Assert.True(organization.RequireDistinctReviewer);
    }

    [Theory]
    [InlineData("Pacific Standard Time")] // Windows id, not IANA
    [InlineData("UTC+5")] // fixed offset: cannot follow daylight saving
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    public void Non_IANA_time_zone_is_rejected(string timeZone) =>
        Assert.Equal(ErrorCodes.InvalidTimeZone, Assert.Throws<BusinessException>(() => Create(timeZone: timeZone)).Code);

    [Fact]
    public void Unknown_language_is_rejected() => AssertValidationFailed(() => Create(language: "not-a-language"));

    [Fact]
    public void Language_defaults_to_english_and_industry_is_required()
    {
        Assert.Equal("en", Create(language: null).DefaultLanguage);
        AssertValidationFailed(() => Create(industry: " "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_rejected(string name) =>
        AssertValidationFailed(() => Create(name: name));

    [Fact]
    public void Name_longer_than_limit_is_rejected() =>
        AssertValidationFailed(() => Create(name: new string('x', Organization.NameMaxLength + 1)));

    [Fact]
    public void Member_with_undefined_role_is_rejected() =>
        AssertValidationFailed(() => OrganizationMember.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (OrganizationRole)99, DateTime.UtcNow));

    private static void AssertValidationFailed(Action action) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(action).Code);
}
