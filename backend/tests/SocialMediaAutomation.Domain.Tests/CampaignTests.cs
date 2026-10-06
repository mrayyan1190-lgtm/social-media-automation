using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

/// <summary>A campaign is a folder: name, colour and an optional description.</summary>
public class CampaignTests
{
    internal static Campaign New(Guid? organizationId = null) =>
        Campaign.Create(Guid.NewGuid(), organizationId ?? Guid.NewGuid(), new CampaignDetails("Spring launch", "#2563eb", null), Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public void New_campaign_is_active_with_a_normalized_color()
    {
        var campaign = New();

        Assert.Equal(CampaignStatus.Active, campaign.Status);
        Assert.Equal("#2563EB", campaign.Color);
        Assert.Null(campaign.Description);
    }

    [Fact]
    public void Missing_color_defaults_to_blue() =>
        Assert.Equal(Campaign.DefaultColor,
            Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), new CampaignDetails("Launch", null, null), Guid.NewGuid(), DateTime.UtcNow).Color);

    [Theory]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Invalid_color_is_rejected(string color) =>
        AssertInvalid(() => Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), new CampaignDetails("Launch", color, null), Guid.NewGuid(), DateTime.UtcNow));

    [Fact]
    public void Name_is_required() =>
        AssertInvalid(() => Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), new CampaignDetails(" ", "#2563EB", null), Guid.NewGuid(), DateTime.UtcNow));

    [Fact]
    public void Archived_campaign_is_read_only()
    {
        var campaign = New();
        campaign.Archive(Guid.NewGuid(), DateTime.UtcNow);

        var error = Assert.Throws<BusinessException>(() => campaign.Update(new CampaignDetails("Renamed", "#16A34A", null), Guid.NewGuid(), DateTime.UtcNow));

        Assert.Equal(ErrorCodes.InvalidStateTransition, error.Code);
    }

    private static void AssertInvalid(Action action) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(action).Code);
}
