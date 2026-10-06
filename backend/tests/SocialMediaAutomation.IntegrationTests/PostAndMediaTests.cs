using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Media.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.Tests;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PostAndMediaTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string OrgB = $"/api/v1/organizations/{DemoData.OrganizationBId}";

    private static object Content(string body = "Close the books in days, not weeks.", Guid? mediaAssetId = null, string? altText = null) => new
    {
        Hook = "Month-end, minus the pain",
        Body = body,
        CallToAction = "Book a demo",
        DestinationUrl = "https://brightline.example/demo",
        Hashtags = new[] { "FinanceOps" },
        MediaAssetId = mediaAssetId,
        AltText = altText,
    };

    private static object NewPost(string platform, Guid? mediaAssetId = null) => new
    {
        Platform = platform,
        Body = "Version one body",
        Hashtags = new[] { "Brightline" },
        MediaAssetId = mediaAssetId,
        AltText = mediaAssetId is null ? null : "Product screenshot",
    };

    private async Task<(HttpClient Client, string CampaignUrl)> CampaignAsync(DemoData.DemoUser user, string org)
    {
        var client = await factory.SignInAsync(user);
        var campaign = await client.CreateCampaignAsync(org);
        return (client, $"{org}/campaigns/{campaign.Id}");
    }

    private static async Task<MediaAssetDto> UploadAsync(HttpClient client, string org, byte[] bytes, string fileName = "image.png", string declaredType = "image/png")
    {
        var response = await UploadRawAsync(client, org, bytes, fileName, declaredType);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<MediaAssetDto>();
    }

    private static Task<HttpResponseMessage> UploadRawAsync(HttpClient client, string org, byte[] bytes, string fileName, string declaredType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(declaredType);
        return client.PostAsync($"{org}/media", new MultipartFormDataContent { { file, "file", fileName } });
    }

    private static Task<HttpResponseMessage> ReviseAsync(HttpClient client, string org, PostDto post, object content) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{org}/posts/{post.Id}/versions")
        {
            Content = JsonContent.Create(content, options: ApiClient.Json),
            Headers = { { "If-Match", ApiClient.ETag(post.RowVersion) } },
        });

    // ---- Versioning --------------------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_post_creates_version_1_as_the_current_version()
    {
        var (creator, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);

        var response = await creator.CreatePostAsync(campaignUrl, NewPost("LinkedIn"));
        var post = await response.ReadAsync<PostDto>();
        var summaries = await (await creator.GetAsync(ApiClient.PostsOf(campaignUrl))).ReadAsync<List<PostListItemDto>>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(EditorialState.DRAFT, post.EditorialState);
        Assert.Equal(1, post.CurrentVersion.VersionNumber);
        Assert.Equal(post.CurrentVersionId, post.CurrentVersion.Id);
        Assert.Equal(PostVersionSource.Manual, post.CurrentVersion.CreationSource);
        Assert.Equal(DemoData.CreatorA.Id, post.CurrentVersion.CreatedByUserId);
        Assert.Equal((SocialPlatform.LinkedIn, 1), (Assert.Single(summaries).Platform, summaries[0].CurrentVersionNumber));
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "05 stale edit rejected")]
    public async Task Edit_creates_version_2_and_version_1_stays_unchanged()
    {
        var (creator, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var v1Post = await (await creator.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();

        var v2Post = await (await ReviseAsync(creator, OrgA, v1Post, Content("Edited body"))).ReadAsync<PostDto>();
        var stale = await ReviseAsync(creator, OrgA, v1Post, Content("Stale edit"));
        var noChange = await (await ReviseAsync(creator, OrgA, v2Post, Content("Edited body"))).ReadAsync<PostDto>();
        var versions = await (await creator.GetAsync($"{OrgA}/posts/{v1Post.Id}/versions")).ReadAsync<List<PostVersionDto>>();

        Assert.Equal(2, v2Post.CurrentVersion.VersionNumber);
        Assert.Equal("Edited body", v2Post.CurrentVersion.Body);
        await stale.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        Assert.Equal(v2Post.CurrentVersionId, noChange.CurrentVersionId);
        Assert.Equal([2, 1], versions.Select(v => v.VersionNumber));
        var v1 = versions[1];
        Assert.Equal((v1Post.CurrentVersion.Id, "Version one body"), (v1.Id, v1.Body));
        Assert.Equal(["#Brightline"], v1.Hashtags);
    }

    [Fact]
    public async Task Database_rejects_in_place_changes_and_cross_post_current_version()
    {
        var (creator, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var linkedIn = await (await creator.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();
        var media = await UploadAsync(creator, OrgA, ImageProbeTests.JpegHeader(1080, 1080), "a.jpg", "image/jpeg");
        var instagram = await (await creator.CreatePostAsync(campaignUrl, NewPost("Instagram", media.Id))).ReadAsync<PostDto>();
        var unused = await UploadAsync(creator, OrgA, ImageProbeTests.PngHeader(400, 400));
        // LinkedIn v1 becomes a non-current version, so only the same-post FK (not the unique pointer index) can reject reuse.
        await (await ReviseAsync(creator, OrgA, linkedIn, Content("LinkedIn v2"))).ReadAsync<PostDto>();
        await using var db = factory.CreateDbContext();

        var update = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PostVersions SET Body = 'tampered' WHERE Id = {linkedIn.CurrentVersionId}"));
        var delete = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM MediaAssets WHERE Id = {unused.Id}"));
        var crossPost = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Posts SET CurrentVersionId = {linkedIn.CurrentVersionId} WHERE Id = {instagram.Id}"));

        Assert.Contains("immutable", update.Message);
        Assert.Contains("immutable", delete.Message);
        Assert.Equal(547, crossPost.Number); // foreign key violation: the version belongs to another post
    }

    [Fact]
    public async Task Campaign_holds_any_posts_and_moving_a_post_creates_no_version()
    {
        var (creator, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        await (await creator.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();
        await (await creator.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();
        var unfiled = await (await creator.PostAsJsonAsync($"{OrgA}/posts", NewPost("Facebook"))).ReadAsync<PostDto>();
        Assert.Null(unfiled.CampaignId);

        var moved = await (await creator.PutJsonAsync($"{OrgA}/posts/{unfiled.Id}/campaign", new { CampaignId = campaignUrl.Split('/')[^1] },
            ApiClient.ETag(unfiled.RowVersion))).ReadAsync<PostDto>();
        var inCampaign = await (await creator.GetAsync(ApiClient.PostsOf(campaignUrl))).ReadAsync<List<PostListItemDto>>();

        Assert.Equal((unfiled.CurrentVersionId, 1), (moved.CurrentVersionId, moved.CurrentVersion.VersionNumber));
        Assert.Equal(3, inCampaign.Count);
        await (await creator.PutJsonAsync($"{OrgA}/posts/{unfiled.Id}/campaign", new { CampaignId = (Guid?)null }, ApiClient.ETag(unfiled.RowVersion)))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Reviewer_cannot_create_or_edit_posts_or_upload()
    {
        var (creator, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var post = await (await creator.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        await (await reviewer.GetAsync($"{OrgA}/posts/{post.Id}")).ReadAsync<PostDto>();
        await (await ReviseAsync(reviewer, OrgA, post, Content("Reviewer edit"))).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await (await UploadRawAsync(reviewer, OrgA, ImageProbeTests.PngHeader(500, 500), "a.png", "image/png"))
            .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    // ---- Media -------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_detects_type_from_bytes_and_stores_immutable_metadata()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var bytes = ImageProbeTests.PngHeader(1200, 628, padding: 1000);

        var asset = await UploadAsync(creator, OrgA, bytes, "../../evil/banner.png", declaredType: "application/octet-stream");
        var content = await creator.GetAsync($"{OrgA}/media/{asset.Id}/content");

        Assert.Equal(("image/png", 1200, 628, bytes.LongLength), (asset.ContentType, asset.Width, asset.Height, asset.SizeBytes));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), asset.Sha256);
        Assert.Equal("banner.png", asset.OriginalFileName);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", content.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("text")]
    [InlineData("svg")]
    public async Task Non_image_disguised_as_png_is_rejected(string kind)
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var bytes = kind == "svg" ? "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray() : "not really an image"u8.ToArray();

        await (await UploadRawAsync(creator, OrgA, bytes, "photo.png", "image/png")).AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Oversized_upload_is_rejected()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var bytes = ImageProbeTests.PngHeader(2000, 2000, padding: 8 * 1024 * 1024);

        await (await UploadRawAsync(creator, OrgA, bytes, "huge.png", "image/png")).AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Replacing_the_image_creates_a_new_version_and_keeps_the_previous_one()
    {
        var (creator, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var first = await UploadAsync(creator, OrgA, ImageProbeTests.JpegHeader(1080, 1080), "first.jpg", "image/jpeg");
        var second = await UploadAsync(creator, OrgA, ImageProbeTests.JpegHeader(1080, 1350), "second.jpg", "image/jpeg");
        var v1 = await (await creator.CreatePostAsync(campaignUrl, NewPost("Instagram", first.Id))).ReadAsync<PostDto>();

        var v2 = await (await ReviseAsync(creator, OrgA, v1, NewPost("Instagram", second.Id))).ReadAsync<PostDto>();
        var altOnly = await (await ReviseAsync(creator, OrgA, v2, new
        {
            Platform = "Instagram", Body = "Version one body", Hashtags = new[] { "Brightline" }, MediaAssetId = second.Id, AltText = "Portrait screenshot",
        })).ReadAsync<PostDto>();
        var versions = await (await creator.GetAsync($"{OrgA}/posts/{v1.Id}/versions")).ReadAsync<List<PostVersionDto>>();

        Assert.Equal(2, v2.CurrentVersion.VersionNumber);
        Assert.Equal(second.Id, v2.CurrentVersion.MediaAssetId);
        Assert.Equal(3, altOnly.CurrentVersion.VersionNumber);
        Assert.Equal([second.Id, second.Id, first.Id], versions.Select(v => v.MediaAssetId!.Value));
        Assert.Equal(1080, versions[^1].Media!.Height);
    }

    [Fact]
    public async Task Instagram_rejects_png_but_accepts_jpeg()
    {
        var (creator, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var png = await UploadAsync(creator, OrgA, ImageProbeTests.PngHeader(1080, 1080));

        await (await creator.CreatePostAsync(campaignUrl, NewPost("Instagram", png.Id)))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    // ---- Tenant isolation --------------------------------------------------------------------------

    [Fact]
    public async Task Image_from_another_workspace_cannot_be_attached_or_read()
    {
        var (creatorA, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var post = await (await creatorA.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var mediaB = await UploadAsync(adminB, OrgB, ImageProbeTests.PngHeader(800, 800));

        var attach = await ReviseAsync(creatorA, OrgA, post, Content(mediaAssetId: mediaB.Id, altText: "foreign"));
        var create = await creatorA.CreatePostAsync(campaignUrl, NewPost("Instagram", mediaB.Id));
        var readViaOwnOrg = await creatorA.GetAsync($"{OrgA}/media/{mediaB.Id}/content");
        var versions = await (await creatorA.GetAsync($"{OrgA}/posts/{post.Id}/versions")).ReadAsync<List<PostVersionDto>>();

        await attach.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await create.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await readViaOwnOrg.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Single(versions);
    }

    [Fact]
    public async Task Database_rejects_a_version_referencing_another_workspace_image()
    {
        var (creatorA, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var post = await (await creatorA.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();
        var mediaB = await UploadAsync(await factory.SignInAsync(DemoData.AdminB), OrgB, ImageProbeTests.PngHeader(800, 800));
        await using var db = factory.CreateDbContext();

        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PostVersions (Id, OrganizationId, PostId, VersionNumber, Body, Hashtags, MediaAssetId, CreationSource, CreatedByUserId, CreatedAtUtc)
            VALUES ({Guid.NewGuid()}, {DemoData.OrganizationAId}, {post.Id}, 99, 'smuggled', '[]', {mediaB.Id}, 'Manual', {DemoData.CreatorA.Id}, SYSUTCDATETIME())
            """));

        Assert.Equal(547, error.Number);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "01 cross-tenant read denied")]
    public async Task Other_workspace_cannot_read_or_edit_posts()
    {
        var (creatorA, campaignUrl) = await CampaignAsync(DemoData.CreatorA, OrgA);
        var post = await (await creatorA.CreatePostAsync(campaignUrl, NewPost("LinkedIn"))).ReadAsync<PostDto>();
        var adminB = await factory.SignInAsync(DemoData.AdminB);

        await (await adminB.GetAsync($"{OrgA}/posts/{post.Id}")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.GetAsync($"{OrgB}/posts/{post.Id}")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.GetAsync($"{OrgB}/posts/{post.Id}/versions")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await ReviseAsync(adminB, OrgB, post, Content("Hijack"))).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.CreatePostAsync($"{OrgB}/campaigns/{campaignUrl.Split('/')[^1]}", NewPost("Facebook")))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
