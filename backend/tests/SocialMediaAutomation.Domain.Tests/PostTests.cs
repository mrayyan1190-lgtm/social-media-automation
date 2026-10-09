using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class PostTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Org = Guid.NewGuid();

    private static PostContent Content(string body = "Close the books faster.", Guid? mediaId = null, string? alt = null, string[]? hashtags = null) =>
        new("Month-end, minus the pain", body, "Book a demo", "https://brightline.example/demo", hashtags ?? ["FinanceOps"],
            mediaId is { } id ? [new PostImage(id, alt)] : null);

    private MediaAsset Image(string type = "image/jpeg", int width = 1080, int height = 1080, Guid? organizationId = null) =>
        MediaAsset.CreateUpload(Guid.NewGuid(), organizationId ?? Org, "a.jpg", type, 1000, width, height,
            new string('a', 64), $"k/{Guid.NewGuid():N}", User, DateTime.UtcNow);

    private (Post Post, PostVersion V1) CreatePost(SocialPlatform platform = SocialPlatform.LinkedIn, MediaAsset? media = null) =>
        Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, platform, Content(mediaId: media?.Id), media is null ? [] : [media], null, PostVersionSource.Manual, User, DateTime.UtcNow);

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

        var v2 = post.Revise(Guid.NewGuid(), v1, Content("Close the books in days."), [], null, PostVersionSource.Manual, User, DateTime.UtcNow)!;

        Assert.Equal(2, v2.VersionNumber);
        Assert.Equal(v2.Id, post.CurrentVersionId);
        Assert.Equal("Close the books in days.", v2.Body);
        Assert.Equal(before, (v1.Id, v1.Body, v1.VersionNumber));
    }

    [Fact]
    public void Edit_without_public_change_creates_no_version()
    {
        var (post, v1) = CreatePost();

        Assert.Null(post.Revise(Guid.NewGuid(), v1, Content(), [], null, PostVersionSource.Manual, User, DateTime.UtcNow));
        Assert.Equal(v1.Id, post.CurrentVersionId);
    }

    [Fact]
    public void Revise_must_start_from_the_current_version_of_the_same_post()
    {
        var (post, v1) = CreatePost();
        var (_, otherPostV1) = CreatePost(SocialPlatform.Instagram, Image());
        post.Revise(Guid.NewGuid(), v1, Content("v2"), [], null, PostVersionSource.Manual, User, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => post.Revise(Guid.NewGuid(), v1, Content("stale"), [], null, PostVersionSource.Manual, User, DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => post.Revise(Guid.NewGuid(), otherPostV1, Content("x"), [], null, PostVersionSource.Manual, User, DateTime.UtcNow));
    }

    [Fact]
    public void Replacing_the_image_creates_a_new_version_and_keeps_the_old_reference()
    {
        var first = Image();
        var second = Image();
        var (post, v1) = CreatePost(SocialPlatform.Instagram, first);

        var v2 = post.Revise(Guid.NewGuid(), v1, Content(mediaId: second.Id, alt: "Dashboard screenshot"), [second], null, PostVersionSource.Manual, User, DateTime.UtcNow)!;

        Assert.Equal(first.Id, v1.Images.Single().MediaAssetId);
        Assert.Equal((second.Id, "Dashboard screenshot"), (v2.Images.Single().MediaAssetId, v2.Images.Single().AltText));
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
    public void Up_to_four_images_keep_their_order_and_alt_text()
    {
        var images = Enumerable.Range(0, 4).Select(_ => Image()).ToList();
        var content = Content() with { Images = images.Select((m, i) => new PostImage(m.Id, $"alt {i}")).ToList() };

        var (_, v1) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.Instagram, content, images, null, PostVersionSource.Manual, User, DateTime.UtcNow);

        Assert.Equal(images.Select(m => m.Id), v1.Images.OrderBy(i => i.Position).Select(i => i.MediaAssetId));
        Assert.Equal(["alt 0", "alt 1", "alt 2", "alt 3"], v1.Images.OrderBy(i => i.Position).Select(i => i.AltText));
        Assert.Equal(images[0].Id, v1.Cover!.MediaAssetId);
    }

    [Fact]
    public void More_than_four_or_duplicate_images_are_rejected()
    {
        var five = Enumerable.Range(0, 5).Select(_ => Image()).ToList();
        AssertInvalid(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn,
            Content() with { Images = five.Select(m => new PostImage(m.Id)).ToList() }, five, null, PostVersionSource.Manual, User, DateTime.UtcNow));

        var image = Image();
        AssertInvalid(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn,
            Content() with { Images = [new PostImage(image.Id), new PostImage(image.Id)] }, [image, image], null, PostVersionSource.Manual, User, DateTime.UtcNow));
    }

    [Fact]
    public void Reordering_images_is_a_public_change()
    {
        var (a, b) = (Image(), Image());
        var content = Content() with { Images = [new PostImage(a.Id), new PostImage(b.Id)] };
        var (post, v1) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn, content, [a, b], null, PostVersionSource.Manual, User, DateTime.UtcNow);

        Assert.Null(post.Revise(Guid.NewGuid(), v1, content, [a, b], null, PostVersionSource.Manual, User, DateTime.UtcNow));
        var v2 = post.Revise(Guid.NewGuid(), v1, content with { Images = [new PostImage(b.Id), new PostImage(a.Id)] }, [b, a], null,
            PostVersionSource.Manual, User, DateTime.UtcNow)!;
        Assert.Equal(b.Id, v2.Cover!.MediaAssetId);
    }

    [Fact]
    public void Platform_text_and_hashtag_limits_are_enforced()
    {
        AssertInvalid(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn, Content(new string('x', 2990)), [],
            null, PostVersionSource.Manual, User, DateTime.UtcNow));
        AssertInvalid(() => Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn,
            Content(hashtags: Enumerable.Range(0, 31).Select(i => $"tag{i}").ToArray()), [], null, PostVersionSource.Manual, User, DateTime.UtcNow));
    }

    [Fact]
    public void Oversized_or_tiny_upload_is_rejected()
    {
        AssertInvalid(() => MediaAsset.CreateUpload(Guid.NewGuid(), Guid.NewGuid(), "a.png", "image/png", 8 * 1024 * 1024 + 1, 1000, 1000, new string('a', 64), "k", User, DateTime.UtcNow));
        AssertInvalid(() => MediaAsset.CreateUpload(Guid.NewGuid(), Guid.NewGuid(), "a.png", "image/png", 100, 50, 50, new string('a', 64), "k", User, DateTime.UtcNow));
        AssertInvalid(() => MediaAsset.CreateUpload(Guid.NewGuid(), Guid.NewGuid(), "a.gif", "image/gif", 100, 500, 500, new string('a', 64), "k", User, DateTime.UtcNow));
    }

    [Fact]
    public void Delete_draft_archives_it_and_cannot_be_repeated()
    {
        var (post, _) = CreatePost();

        post.Archive(DateTime.UtcNow);

        Assert.Equal(EditorialState.ARCHIVED, post.EditorialState);
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<BusinessException>(() => post.Archive(DateTime.UtcNow)).Code);
    }

    private static void AssertInvalid(Action action) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(action).Code);
}
