using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class PostTests
{
    private static readonly Guid User = Guid.NewGuid();
    private readonly Campaign campaign = CampaignTests.New();

    private static PostContent Content(string body = "Close the books faster.", Guid? mediaId = null, string? alt = null, string[]? hashtags = null) =>
        new("Month-end, minus the pain", body, "Book a demo", "https://brightline.example/demo", hashtags ?? ["FinanceOps"], mediaId, alt);

    private MediaAsset Image(string type = "image/jpeg", int width = 1080, int height = 1080, Guid? organizationId = null) =>
        MediaAsset.CreateUpload(Guid.NewGuid(), organizationId ?? campaign.OrganizationId, "a.jpg", type, 1000, width, height,
            new string('a', 64), $"k/{Guid.NewGuid():N}", User, DateTime.UtcNow);

    private (Post Post, PostVersion V1) CreatePost(SocialPlatform platform = SocialPlatform.LinkedIn, MediaAsset? media = null) =>
        Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.OrganizationId, campaign, platform, Content(mediaId: media?.Id), media, null, PostVersionSource.Manual, User, DateTime.UtcNow);

    [Fact]
    public void Create_makes_version_1_and_points_current_version_at_it()
    {
        var (post, v1) = CreatePost();

        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal(v1.Id, post.CurrentVersionId);
        Assert.Equal((post.OrganizationId, post.Id), (v1.OrganizationId, v1.PostId));
        Assert.Equal(EditorialState.DRAFT, post.EditorialState);
        Assert.Equal(["#FinanceOps"], v1.Hashtags);
    }

    [Fact]
    public void Edit_creates_version_2_and_leaves_version_1_unchanged()
    {
        var (post, v1) = CreatePost();
        var before = (v1.Id, v1.Body, v1.VersionNumber);

        var v2 = post.Revise(Guid.NewGuid(), v1, Content("Close the books in days."), null, null, PostVersionSource.Manual, User, DateTime.UtcNow)!;

        Assert.Equal(2, v2.VersionNumber);
        Assert.Equal(v2.Id, post.CurrentVersionId);
        Assert.Equal("Close the books in days.", v2.Body);
        Assert.Equal(before, (v1.Id, v1.Body, v1.VersionNumber));
    }

    [Fact]
    public void Edit_without_public_change_creates_no_version()
    {
        var (post, v1) = CreatePost();

        Assert.Null(post.Revise(Guid.NewGuid(), v1, Content(), null, null, PostVersionSource.Manual, User, DateTime.UtcNow));
        Assert.Equal(v1.Id, post.CurrentVersionId);
    }

    [Fact]
    public void Revise_must_start_from_the_current_version_of_the_same_post()
    {
        var (post, v1) = CreatePost();
        var (_, otherPostV1) = CreatePost(SocialPlatform.Instagram, Image());
        post.Revise(Guid.NewGuid(), v1, Content("v2"), null, null, PostVersionSource.Manual, User, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => post.Revise(Guid.NewGuid(), v1, Content("stale"), null, null, PostVersionSource.Manual, User, DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => post.Revise(Guid.NewGuid(), otherPostV1, Content("x"), null, null, PostVersionSource.Manual, User, DateTime.UtcNow));
    }

    [Fact]
    public void Replacing_the_image_creates_a_new_version_and_keeps_the_old_reference()
    {
        var first = Image();
        var second = Image();
        var (post, v1) = CreatePost(SocialPlatform.Instagram, first);

        var v2 = post.Revise(Guid.NewGuid(), v1, Content(mediaId: second.Id, alt: "Dashboard screenshot"), second, null, PostVersionSource.Manual, User, DateTime.UtcNow)!;

        Assert.Equal(first.Id, v1.MediaAssetId);
        Assert.Equal(second.Id, v2.MediaAssetId);
        Assert.Equal(2, v2.VersionNumber);
    }

    [Fact]
    public void Image_from_another_workspace_is_rejected()
    {
        var foreign = Image(organizationId: Guid.NewGuid());

        var error = Assert.Throws<BusinessException>(() => CreatePost(SocialPlatform.LinkedIn, foreign));

        Assert.Equal(ErrorCodes.TenantMismatch, error.Code);
    }

    [Theory]
    [InlineData("image/png", 1080, 1080)] // Instagram publishes JPEG only
    [InlineData("image/jpeg", 1080, 1920)] // 9:16 is taller than the 4:5 feed limit
    [InlineData("image/jpeg", 2000, 1000)] // 2:1 is wider than 1.91:1
    public void Instagram_image_requirements_are_enforced(string type, int width, int height) =>
        AssertInvalid(() => CreatePost(SocialPlatform.Instagram, Image(type, width, height)));

    [Fact]
    public void Instagram_accepts_square_and_portrait_jpeg()
    {
        CreatePost(SocialPlatform.Instagram, Image(width: 1080, height: 1080));
        CreatePost(SocialPlatform.Instagram, Image(width: 1080, height: 1350));
    }

    [Fact]
    public void Post_needs_no_campaign_and_can_be_filed_without_a_new_version()
    {
        var (post, v1) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.OrganizationId, null, SocialPlatform.Facebook, Content(), null, null,
            PostVersionSource.Manual, User, DateTime.UtcNow);
        Assert.Null(post.CampaignId);

        post.MoveToCampaign(campaign, DateTime.UtcNow);

        Assert.Equal(campaign.Id, post.CampaignId);
        Assert.Equal(v1.Id, post.CurrentVersionId);
    }

    [Fact]
    public void Campaign_from_another_workspace_is_rejected()
    {
        var foreign = CampaignTests.New();

        var error = Assert.Throws<BusinessException>(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.OrganizationId, foreign,
            SocialPlatform.LinkedIn, Content(), null, null, PostVersionSource.Manual, User, DateTime.UtcNow));

        Assert.Equal(ErrorCodes.TenantMismatch, error.Code);
    }

    [Fact]
    public void Alt_text_without_image_is_rejected() =>
        AssertInvalid(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.OrganizationId, campaign, SocialPlatform.LinkedIn, Content(alt: "orphan"), null,
            null, PostVersionSource.Manual, User, DateTime.UtcNow));

    [Fact]
    public void Platform_text_and_hashtag_limits_are_enforced()
    {
        AssertInvalid(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.OrganizationId, campaign, SocialPlatform.LinkedIn, Content(new string('x', 2990)), null,
            null, PostVersionSource.Manual, User, DateTime.UtcNow));
        AssertInvalid(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.OrganizationId, campaign, SocialPlatform.LinkedIn,
            Content(hashtags: Enumerable.Range(0, 31).Select(i => $"tag{i}").ToArray()), null, null, PostVersionSource.Manual, User, DateTime.UtcNow));
    }

    [Fact]
    public void Oversized_or_tiny_upload_is_rejected()
    {
        AssertInvalid(() => MediaAsset.CreateUpload(Guid.NewGuid(), Guid.NewGuid(), "a.png", "image/png", 8 * 1024 * 1024 + 1, 1000, 1000, new string('a', 64), "k", User, DateTime.UtcNow));
        AssertInvalid(() => MediaAsset.CreateUpload(Guid.NewGuid(), Guid.NewGuid(), "a.png", "image/png", 100, 50, 50, new string('a', 64), "k", User, DateTime.UtcNow));
        AssertInvalid(() => MediaAsset.CreateUpload(Guid.NewGuid(), Guid.NewGuid(), "a.gif", "image/gif", 100, 500, 500, new string('a', 64), "k", User, DateTime.UtcNow));
    }

    private static void AssertInvalid(Action action) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(action).Code);
}
