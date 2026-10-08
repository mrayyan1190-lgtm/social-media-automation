IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150320_InitialTenancy'
)
BEGIN
    CREATE TABLE [Organizations] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [DefaultLanguage] nvarchar(35) NOT NULL,
        [TimeZoneId] nvarchar(64) NOT NULL,
        [RequireDistinctReviewer] bit NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Organizations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Organizations_Status] CHECK ([Status] IN ('Active', 'Suspended'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150320_InitialTenancy'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [NormalizedEmail] nvarchar(256) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [PasswordHash] nvarchar(512) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150320_InitialTenancy'
)
BEGIN
    CREATE TABLE [OrganizationMembers] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Role] nvarchar(32) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_OrganizationMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_OrganizationMembers_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_OrganizationMembers_Role] CHECK ([Role] IN ('Admin', 'Creator', 'Reviewer')),
        CONSTRAINT [CK_OrganizationMembers_Status] CHECK ([Status] IN ('Active', 'Suspended')),
        CONSTRAINT [FK_OrganizationMembers_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrganizationMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150320_InitialTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationMembers_OrganizationId_UserId] ON [OrganizationMembers] ([OrganizationId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150320_InitialTenancy'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembers_UserId_Status] ON [OrganizationMembers] ([UserId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150320_InitialTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150320_InitialTenancy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004150320_InitialTenancy', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    DROP INDEX [IX_OrganizationMembers_OrganizationId_UserId] ON [OrganizationMembers];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    ALTER TABLE [OrganizationMembers] ADD CONSTRAINT [AK_OrganizationMembers_OrganizationId_UserId] UNIQUE ([OrganizationId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE TABLE [BrandProfiles] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [BrandName] nvarchar(200) NOT NULL,
        [Industry] nvarchar(200) NOT NULL,
        [BusinessDescription] nvarchar(2000) NOT NULL,
        [ProductsServices] nvarchar(2000) NOT NULL,
        [ValueProposition] nvarchar(2000) NOT NULL,
        [DefaultAudience] nvarchar(2000) NOT NULL,
        [VoiceTraits] nvarchar(max) NOT NULL,
        [DefaultLanguage] nvarchar(35) NOT NULL,
        [PreferredCtaStyle] nvarchar(200) NULL,
        [PreferredTerms] nvarchar(max) NOT NULL,
        [ProhibitedTerms] nvarchar(max) NOT NULL,
        [BrandHashtags] nvarchar(max) NOT NULL,
        [EmojiPreference] nvarchar(32) NOT NULL,
        [WebsiteUrl] nvarchar(2048) NULL,
        [MarketNotes] nvarchar(2000) NULL,
        [UpdatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_BrandProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_BrandProfiles_BrandHashtags_Json] CHECK (ISJSON([BrandHashtags]) = 1),
        CONSTRAINT [CK_BrandProfiles_EmojiPreference] CHECK ([EmojiPreference] IN ('None', 'Minimal', 'Moderate', 'Expressive')),
        CONSTRAINT [CK_BrandProfiles_PreferredTerms_Json] CHECK (ISJSON([PreferredTerms]) = 1),
        CONSTRAINT [CK_BrandProfiles_ProhibitedTerms_Json] CHECK (ISJSON([ProhibitedTerms]) = 1),
        CONSTRAINT [CK_BrandProfiles_VoiceTraits_Json] CHECK (ISJSON([VoiceTraits]) = 1),
        CONSTRAINT [FK_BrandProfiles_OrganizationMembers_OrganizationId_UpdatedByUserId] FOREIGN KEY ([OrganizationId], [UpdatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BrandProfiles_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE TABLE [Campaigns] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Objective] nvarchar(500) NOT NULL,
        [Topic] nvarchar(500) NOT NULL,
        [KeyMessage] nvarchar(2000) NOT NULL,
        [TargetAudience] nvarchar(2000) NULL,
        [PainPoint] nvarchar(500) NULL,
        [PrimaryBenefit] nvarchar(500) NULL,
        [CallToAction] nvarchar(200) NULL,
        [DestinationUrl] nvarchar(2048) NULL,
        [ContentCategory] nvarchar(200) NULL,
        [ToneOverride] nvarchar(200) NULL,
        [LengthPreference] nvarchar(32) NOT NULL,
        [HashtagPreference] nvarchar(32) NOT NULL,
        [EmojiPreference] nvarchar(32) NULL,
        [ContentAngle] nvarchar(500) NULL,
        [HookDirection] nvarchar(500) NULL,
        [StartDate] date NULL,
        [EndDate] date NULL,
        [EmbargoNotes] nvarchar(500) NULL,
        [Platforms] nvarchar(max) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [UpdatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Campaigns] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Campaigns_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Campaigns_EmojiPreference] CHECK ([EmojiPreference] IN ('None', 'Minimal', 'Moderate', 'Expressive')),
        CONSTRAINT [CK_Campaigns_HashtagPreference] CHECK ([HashtagPreference] IN ('Default', 'None', 'Few', 'Many')),
        CONSTRAINT [CK_Campaigns_LengthPreference] CHECK ([LengthPreference] IN ('Default', 'Short', 'Medium', 'Long')),
        CONSTRAINT [CK_Campaigns_Platforms_Json] CHECK (ISJSON([Platforms]) = 1),
        CONSTRAINT [CK_Campaigns_Status] CHECK ([Status] IN ('Active', 'Archived')),
        CONSTRAINT [FK_Campaigns_OrganizationMembers_OrganizationId_CreatedByUserId] FOREIGN KEY ([OrganizationId], [CreatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Campaigns_OrganizationMembers_OrganizationId_UpdatedByUserId] FOREIGN KEY ([OrganizationId], [UpdatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Campaigns_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE TABLE [CampaignFacts] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [CampaignId] uniqueidentifier NOT NULL,
        [Text] nvarchar(1000) NOT NULL,
        [SourceNote] nvarchar(500) NOT NULL,
        [SuppliedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RemovedAtUtc] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CampaignFacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CampaignFacts_Campaigns_OrganizationId_CampaignId] FOREIGN KEY ([OrganizationId], [CampaignId]) REFERENCES [Campaigns] ([OrganizationId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CampaignFacts_OrganizationMembers_OrganizationId_SuppliedByUserId] FOREIGN KEY ([OrganizationId], [SuppliedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BrandProfiles_OrganizationId] ON [BrandProfiles] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE INDEX [IX_BrandProfiles_OrganizationId_UpdatedByUserId] ON [BrandProfiles] ([OrganizationId], [UpdatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE INDEX [IX_CampaignFacts_OrganizationId_CampaignId] ON [CampaignFacts] ([OrganizationId], [CampaignId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE INDEX [IX_CampaignFacts_OrganizationId_SuppliedByUserId] ON [CampaignFacts] ([OrganizationId], [SuppliedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE INDEX [IX_Campaigns_OrganizationId_CreatedByUserId] ON [Campaigns] ([OrganizationId], [CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE INDEX [IX_Campaigns_OrganizationId_UpdatedAtUtc] ON [Campaigns] ([OrganizationId], [UpdatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    CREATE INDEX [IX_Campaigns_OrganizationId_UpdatedByUserId] ON [Campaigns] ([OrganizationId], [UpdatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004152309_BrandProfilesAndCampaigns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004152309_BrandProfilesAndCampaigns', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE TABLE [MediaAssets] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [OriginalFileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Width] int NOT NULL,
        [Height] int NOT NULL,
        [Sha256] char(64) NOT NULL,
        [ObjectKey] varchar(300) NOT NULL,
        [SourceType] nvarchar(32) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_MediaAssets] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_MediaAssets_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_MediaAssets_SourceType] CHECK ([SourceType] IN ('Upload')),
        CONSTRAINT [FK_MediaAssets_OrganizationMembers_OrganizationId_CreatedByUserId] FOREIGN KEY ([OrganizationId], [CreatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MediaAssets_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE TABLE [Posts] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [CampaignId] uniqueidentifier NOT NULL,
        [Platform] nvarchar(32) NOT NULL,
        [EditorialState] nvarchar(32) NOT NULL,
        [CurrentVersionId] uniqueidentifier NULL,
        [ActiveApprovalId] uniqueidentifier NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Posts] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Posts_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Posts_EditorialState] CHECK ([EditorialState] IN ('DRAFT', 'IN_REVIEW', 'CHANGES_REQUESTED', 'APPROVED', 'ARCHIVED')),
        CONSTRAINT [CK_Posts_Platform] CHECK ([Platform] IN ('LinkedIn', 'Facebook', 'Instagram')),
        CONSTRAINT [FK_Posts_Campaigns_OrganizationId_CampaignId] FOREIGN KEY ([OrganizationId], [CampaignId]) REFERENCES [Campaigns] ([OrganizationId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Posts_OrganizationMembers_OrganizationId_CreatedByUserId] FOREIGN KEY ([OrganizationId], [CreatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE TABLE [PostVersions] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [VersionNumber] int NOT NULL,
        [Hook] nvarchar(300) NULL,
        [Body] nvarchar(max) NOT NULL,
        [CallToAction] nvarchar(200) NULL,
        [DestinationUrl] nvarchar(2048) NULL,
        [Hashtags] nvarchar(max) NOT NULL,
        [SocialAccountId] uniqueidentifier NULL,
        [MediaAssetId] uniqueidentifier NULL,
        [AltText] nvarchar(1000) NULL,
        [CreationSource] nvarchar(32) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_PostVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_PostVersions_OrganizationId_PostId_Id] UNIQUE ([OrganizationId], [PostId], [Id]),
        CONSTRAINT [CK_PostVersions_CreationSource] CHECK ([CreationSource] IN ('Manual', 'AiGeneration', 'AiRewrite')),
        CONSTRAINT [CK_PostVersions_Hashtags_Json] CHECK (ISJSON([Hashtags]) = 1),
        CONSTRAINT [FK_PostVersions_MediaAssets_OrganizationId_MediaAssetId] FOREIGN KEY ([OrganizationId], [MediaAssetId]) REFERENCES [MediaAssets] ([OrganizationId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PostVersions_OrganizationMembers_OrganizationId_CreatedByUserId] FOREIGN KEY ([OrganizationId], [CreatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PostVersions_Posts_OrganizationId_PostId] FOREIGN KEY ([OrganizationId], [PostId]) REFERENCES [Posts] ([OrganizationId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MediaAssets_ObjectKey] ON [MediaAssets] ([ObjectKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE INDEX [IX_MediaAssets_OrganizationId_CreatedByUserId] ON [MediaAssets] ([OrganizationId], [CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Posts_CurrentVersionId] ON [Posts] ([CurrentVersionId]) WHERE [CurrentVersionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Posts_OrganizationId_CampaignId_Platform] ON [Posts] ([OrganizationId], [CampaignId], [Platform]) WHERE [EditorialState] <> ''ARCHIVED''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE INDEX [IX_Posts_OrganizationId_CreatedByUserId] ON [Posts] ([OrganizationId], [CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE INDEX [IX_Posts_OrganizationId_Id_CurrentVersionId] ON [Posts] ([OrganizationId], [Id], [CurrentVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE INDEX [IX_PostVersions_OrganizationId_CreatedByUserId] ON [PostVersions] ([OrganizationId], [CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE INDEX [IX_PostVersions_OrganizationId_MediaAssetId] ON [PostVersions] ([OrganizationId], [MediaAssetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PostVersions_PostId_VersionNumber] ON [PostVersions] ([PostId], [VersionNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    ALTER TABLE [Posts] ADD CONSTRAINT [FK_Posts_PostVersions_OrganizationId_Id_CurrentVersionId] FOREIGN KEY ([OrganizationId], [Id], [CurrentVersionId]) REFERENCES [PostVersions] ([OrganizationId], [PostId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    EXEC(N'CREATE TRIGGER [dbo].[TR_PostVersions_Immutable] ON [dbo].[PostVersions] AFTER UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''PostVersions are immutable. Create a new version instead.'', 1;
    END')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    EXEC(N'CREATE TRIGGER [dbo].[TR_MediaAssets_Immutable] ON [dbo].[MediaAssets] AFTER UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''MediaAssets are immutable. Upload a new asset instead.'', 1;
    END')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160646_PostsVersionsAndMedia'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004160646_PostsVersionsAndMedia', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE TABLE [AiGenerationRequests] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [CampaignId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(32) NOT NULL,
        [Platforms] nvarchar(max) NOT NULL,
        [PostId] uniqueidentifier NULL,
        [SourceVersionId] uniqueidentifier NULL,
        [RewriteAction] nvarchar(32) NULL,
        [CustomInstruction] nvarchar(500) NULL,
        [InputSnapshot] nvarchar(max) NOT NULL,
        [PromptVersion] nvarchar(50) NOT NULL,
        [SchemaVersion] nvarchar(50) NOT NULL,
        [Provider] nvarchar(32) NOT NULL,
        [Model] nvarchar(200) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [AttemptCount] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [NextAttemptAtUtc] datetime2(3) NOT NULL,
        [LeaseToken] uniqueidentifier NULL,
        [LeasedBy] nvarchar(200) NULL,
        [LeaseExpiresAtUtc] datetime2(3) NULL,
        [FailureCode] nvarchar(50) NULL,
        [FailureMessage] nvarchar(1000) NULL,
        [RequestedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [StartedAtUtc] datetime2(3) NULL,
        [CompletedAtUtc] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_AiGenerationRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_AiGenerationRequests_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_AiGenerationRequests_InputSnapshot_Json] CHECK (ISJSON([InputSnapshot]) = 1),
        CONSTRAINT [CK_AiGenerationRequests_Kind] CHECK ([Kind] IN ('Generation', 'Rewrite')),
        CONSTRAINT [CK_AiGenerationRequests_Platforms_Json] CHECK (ISJSON([Platforms]) = 1),
        CONSTRAINT [CK_AiGenerationRequests_Provider] CHECK ([Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible')),
        CONSTRAINT [CK_AiGenerationRequests_RewriteAction] CHECK ([RewriteAction] IN ('Shorten', 'Expand', 'MoreProfessional', 'ImproveHook', 'LessPromotional', 'Custom')),
        CONSTRAINT [CK_AiGenerationRequests_Status] CHECK ([Status] IN ('PENDING', 'PROCESSING', 'COMPLETED', 'FAILED')),
        CONSTRAINT [FK_AiGenerationRequests_Campaigns_OrganizationId_CampaignId] FOREIGN KEY ([OrganizationId], [CampaignId]) REFERENCES [Campaigns] ([OrganizationId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AiGenerationRequests_OrganizationMembers_OrganizationId_RequestedByUserId] FOREIGN KEY ([OrganizationId], [RequestedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AiGenerationRequests_Posts_OrganizationId_PostId] FOREIGN KEY ([OrganizationId], [PostId]) REFERENCES [Posts] ([OrganizationId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE TABLE [AiProviderSettings] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(32) NOT NULL,
        [BaseUrl] nvarchar(500) NOT NULL,
        [Model] nvarchar(200) NOT NULL,
        [ProtectedApiKey] nvarchar(4000) NOT NULL,
        [ApiKeyHint] nvarchar(8) NOT NULL,
        [UpdatedByUserId] uniqueidentifier NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_AiProviderSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AiProviderSettings_Provider] CHECK ([Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible')),
        CONSTRAINT [FK_AiProviderSettings_OrganizationMembers_OrganizationId_UpdatedByUserId] FOREIGN KEY ([OrganizationId], [UpdatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AiProviderSettings_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE TABLE [AiCallLogs] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [Purpose] nvarchar(32) NOT NULL,
        [Provider] nvarchar(32) NOT NULL,
        [Model] nvarchar(200) NOT NULL,
        [PromptVersion] nvarchar(50) NOT NULL,
        [Succeeded] bit NOT NULL,
        [ErrorCode] nvarchar(50) NULL,
        [HttpStatus] int NULL,
        [InputTokens] int NULL,
        [OutputTokens] int NULL,
        [LatencyMs] int NOT NULL,
        [EstimatedCostUsd] decimal(12,6) NULL,
        [Diagnostic] nvarchar(4000) NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_AiCallLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AiCallLogs_Provider] CHECK ([Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible')),
        CONSTRAINT [CK_AiCallLogs_Purpose] CHECK ([Purpose] IN ('Generation', 'Evaluation', 'Rewrite', 'ModelListing')),
        CONSTRAINT [FK_AiCallLogs_AiGenerationRequests_OrganizationId_RequestId] FOREIGN KEY ([OrganizationId], [RequestId]) REFERENCES [AiGenerationRequests] ([OrganizationId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE TABLE [AiRequestOutputs] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [Platform] nvarchar(32) NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [PostVersionId] uniqueidentifier NOT NULL,
        [OutputJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_AiRequestOutputs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AiRequestOutputs_OutputJson_Json] CHECK (ISJSON([OutputJson]) = 1),
        CONSTRAINT [CK_AiRequestOutputs_Platform] CHECK ([Platform] IN ('LinkedIn', 'Facebook', 'Instagram')),
        CONSTRAINT [FK_AiRequestOutputs_AiGenerationRequests_OrganizationId_RequestId] FOREIGN KEY ([OrganizationId], [RequestId]) REFERENCES [AiGenerationRequests] ([OrganizationId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AiRequestOutputs_PostVersions_OrganizationId_PostId_PostVersionId] FOREIGN KEY ([OrganizationId], [PostId], [PostVersionId]) REFERENCES [PostVersions] ([OrganizationId], [PostId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE TABLE [ContentEvaluations] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [PostVersionId] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NULL,
        [BrandAlignment] int NULL,
        [AudienceRelevance] int NULL,
        [Clarity] int NULL,
        [PlatformFit] int NULL,
        [CtaRelevance] int NULL,
        [Summary] nvarchar(1000) NULL,
        [Issues] nvarchar(max) NOT NULL,
        [HasCriticalIssue] bit NOT NULL,
        [EvaluatorModel] nvarchar(200) NULL,
        [PromptVersion] nvarchar(50) NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_ContentEvaluations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ContentEvaluations_Issues_Json] CHECK (ISJSON([Issues]) = 1),
        CONSTRAINT [FK_ContentEvaluations_AiGenerationRequests_OrganizationId_RequestId] FOREIGN KEY ([OrganizationId], [RequestId]) REFERENCES [AiGenerationRequests] ([OrganizationId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ContentEvaluations_PostVersions_OrganizationId_PostId_PostVersionId] FOREIGN KEY ([OrganizationId], [PostId], [PostVersionId]) REFERENCES [PostVersions] ([OrganizationId], [PostId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiCallLogs_OrganizationId_RequestId] ON [AiCallLogs] ([OrganizationId], [RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiGenerationRequests_OrganizationId_CampaignId_CreatedAtUtc] ON [AiGenerationRequests] ([OrganizationId], [CampaignId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiGenerationRequests_OrganizationId_PostId] ON [AiGenerationRequests] ([OrganizationId], [PostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiGenerationRequests_OrganizationId_RequestedByUserId] ON [AiGenerationRequests] ([OrganizationId], [RequestedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiGenerationRequests_Status_NextAttemptAtUtc] ON [AiGenerationRequests] ([Status], [NextAttemptAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AiProviderSettings_OrganizationId] ON [AiProviderSettings] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiProviderSettings_OrganizationId_UpdatedByUserId] ON [AiProviderSettings] ([OrganizationId], [UpdatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiRequestOutputs_OrganizationId_PostId_PostVersionId] ON [AiRequestOutputs] ([OrganizationId], [PostId], [PostVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_AiRequestOutputs_OrganizationId_RequestId] ON [AiRequestOutputs] ([OrganizationId], [RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AiRequestOutputs_RequestId_Platform] ON [AiRequestOutputs] ([RequestId], [Platform]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_ContentEvaluations_OrganizationId_PostId_PostVersionId] ON [ContentEvaluations] ([OrganizationId], [PostId], [PostVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE INDEX [IX_ContentEvaluations_OrganizationId_RequestId] ON [ContentEvaluations] ([OrganizationId], [RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ContentEvaluations_PostVersionId] ON [ContentEvaluations] ([PostVersionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004164225_AiGeneration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004164225_AiGeneration', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    DROP INDEX [IX_Posts_OrganizationId_Id_CurrentVersionId] ON [Posts];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [ActorUserId] uniqueidentifier NULL,
        [Action] nvarchar(60) NOT NULL,
        [ResourceType] nvarchar(50) NOT NULL,
        [ResourceId] uniqueidentifier NOT NULL,
        [MetadataJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AuditLogs_MetadataJson_Json] CHECK (ISJSON([MetadataJson]) = 1),
        CONSTRAINT [FK_AuditLogs_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AuditLogs_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [RecipientUserId] uniqueidentifier NOT NULL,
        [Type] nvarchar(32) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [ResourceType] nvarchar(50) NOT NULL,
        [ResourceId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [ReadAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Notifications_Type] CHECK ([Type] IN ('REVIEW_REQUESTED', 'CHANGES_REQUESTED', 'APPROVED', 'SCHEDULED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'ACTION_REQUIRED', 'REAUTHORIZATION_REQUIRED')),
        CONSTRAINT [FK_Notifications_OrganizationMembers_OrganizationId_RecipientUserId] FOREIGN KEY ([OrganizationId], [RecipientUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE TABLE [ReviewRequests] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [PostVersionId] uniqueidentifier NOT NULL,
        [VersionNumber] int NOT NULL,
        [SubmittedByUserId] uniqueidentifier NOT NULL,
        [SubmissionNote] nvarchar(2000) NULL,
        [SubmittedAtUtc] datetime2(3) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [DecidedByUserId] uniqueidentifier NULL,
        [DecidedAtUtc] datetime2(3) NULL,
        [DecisionComment] nvarchar(2000) NULL,
        [ApprovedPayloadVersion] nvarchar(50) NULL,
        [ApprovedPayloadJson] nvarchar(max) NULL,
        [ApprovedPayloadHash] char(64) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ReviewRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ReviewRequests_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [AK_ReviewRequests_OrganizationId_PostId_PostVersionId_Id] UNIQUE ([OrganizationId], [PostId], [PostVersionId], [Id]),
        CONSTRAINT [CK_ReviewRequests_ApprovedPayload] CHECK ([Status] <> 'APPROVED' OR ([ApprovedPayloadHash] IS NOT NULL AND [ApprovedPayloadJson] IS NOT NULL AND [DecidedByUserId] IS NOT NULL)),
        CONSTRAINT [CK_ReviewRequests_ApprovedPayloadJson_Json] CHECK (ISJSON([ApprovedPayloadJson]) = 1),
        CONSTRAINT [CK_ReviewRequests_ChangesExplained] CHECK ([Status] <> 'CHANGES_REQUESTED' OR LEN(LTRIM(RTRIM([DecisionComment]))) > 0),
        CONSTRAINT [CK_ReviewRequests_Status] CHECK ([Status] IN ('PENDING', 'APPROVED', 'CHANGES_REQUESTED')),
        CONSTRAINT [FK_ReviewRequests_OrganizationMembers_OrganizationId_DecidedByUserId] FOREIGN KEY ([OrganizationId], [DecidedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReviewRequests_OrganizationMembers_OrganizationId_SubmittedByUserId] FOREIGN KEY ([OrganizationId], [SubmittedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReviewRequests_PostVersions_OrganizationId_PostId_PostVersionId] FOREIGN KEY ([OrganizationId], [PostId], [PostVersionId]) REFERENCES [PostVersions] ([OrganizationId], [PostId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReviewRequests_Posts_OrganizationId_PostId] FOREIGN KEY ([OrganizationId], [PostId]) REFERENCES [Posts] ([OrganizationId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE TABLE [SocialAccounts] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Platform] nvarchar(32) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [ExternalAccountId] nvarchar(200) NULL,
        [ExecutionMode] nvarchar(32) NOT NULL,
        [ConnectionStatus] nvarchar(32) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_SocialAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_SocialAccounts_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_SocialAccounts_ConnectionStatus] CHECK ([ConnectionStatus] IN ('CONNECTED', 'REAUTHORIZATION_REQUIRED', 'DISCONNECTED')),
        CONSTRAINT [CK_SocialAccounts_ExecutionMode] CHECK ([ExecutionMode] IN ('LIVE', 'TEST_ACCOUNT', 'SIMULATED', 'NOT_CONFIGURED')),
        CONSTRAINT [CK_SocialAccounts_Platform] CHECK ([Platform] IN ('LinkedIn', 'Facebook', 'Instagram')),
        CONSTRAINT [FK_SocialAccounts_OrganizationMembers_OrganizationId_CreatedByUserId] FOREIGN KEY ([OrganizationId], [CreatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SocialAccounts_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE TABLE [ReviewComments] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [ReviewRequestId] uniqueidentifier NOT NULL,
        [AuthorUserId] uniqueidentifier NOT NULL,
        [Body] nvarchar(2000) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_ReviewComments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReviewComments_OrganizationMembers_OrganizationId_AuthorUserId] FOREIGN KEY ([OrganizationId], [AuthorUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReviewComments_ReviewRequests_OrganizationId_ReviewRequestId] FOREIGN KEY ([OrganizationId], [ReviewRequestId]) REFERENCES [ReviewRequests] ([OrganizationId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_PostVersions_OrganizationId_SocialAccountId] ON [PostVersions] ([OrganizationId], [SocialAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_Posts_OrganizationId_Id_CurrentVersionId_ActiveApprovalId] ON [Posts] ([OrganizationId], [Id], [CurrentVersionId], [ActiveApprovalId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    EXEC(N'ALTER TABLE [Posts] ADD CONSTRAINT [CK_Posts_ApprovalMatchesState] CHECK (([EditorialState] = ''APPROVED'' AND [ActiveApprovalId] IS NOT NULL) OR ([EditorialState] <> ''APPROVED'' AND [ActiveApprovalId] IS NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_ActorUserId] ON [AuditLogs] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_OrganizationId_CreatedAtUtc] ON [AuditLogs] ([OrganizationId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_OrganizationId_ResourceId] ON [AuditLogs] ([OrganizationId], [ResourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_Notifications_OrganizationId_RecipientUserId] ON [Notifications] ([OrganizationId], [RecipientUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_Notifications_RecipientUserId_OrganizationId_ReadAtUtc_CreatedAtUtc] ON [Notifications] ([RecipientUserId], [OrganizationId], [ReadAtUtc], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_ReviewComments_OrganizationId_AuthorUserId] ON [ReviewComments] ([OrganizationId], [AuthorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_ReviewComments_OrganizationId_ReviewRequestId] ON [ReviewComments] ([OrganizationId], [ReviewRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_ReviewRequests_OrganizationId_DecidedByUserId] ON [ReviewRequests] ([OrganizationId], [DecidedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_ReviewRequests_OrganizationId_Status_SubmittedAtUtc] ON [ReviewRequests] ([OrganizationId], [Status], [SubmittedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_ReviewRequests_OrganizationId_SubmittedByUserId] ON [ReviewRequests] ([OrganizationId], [SubmittedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ReviewRequests_OnePendingPerPost] ON [ReviewRequests] ([PostId]) WHERE [Status] = ''PENDING''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_SocialAccounts_OrganizationId_CreatedByUserId] ON [SocialAccounts] ([OrganizationId], [CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    CREATE INDEX [IX_SocialAccounts_OrganizationId_Platform] ON [SocialAccounts] ([OrganizationId], [Platform]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    ALTER TABLE [Posts] ADD CONSTRAINT [FK_Posts_ReviewRequests_OrganizationId_Id_CurrentVersionId_ActiveApprovalId] FOREIGN KEY ([OrganizationId], [Id], [CurrentVersionId], [ActiveApprovalId]) REFERENCES [ReviewRequests] ([OrganizationId], [PostId], [PostVersionId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    ALTER TABLE [PostVersions] ADD CONSTRAINT [FK_PostVersions_SocialAccounts_OrganizationId_SocialAccountId] FOREIGN KEY ([OrganizationId], [SocialAccountId]) REFERENCES [SocialAccounts] ([OrganizationId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    EXEC(N'CREATE TRIGGER [dbo].[TR_AuditLogs_Immutable] ON [dbo].[AuditLogs] AFTER UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''AuditLogs are append-only.'', 1;
    END')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004171321_ReviewWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004171321_ReviewWorkflow', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    ALTER TABLE [SocialAccounts] ADD [Capabilities] nvarchar(max) NOT NULL DEFAULT N'[]';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    ALTER TABLE [SocialAccounts] ADD [LastVerifiedAtUtc] datetime2(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    ALTER TABLE [SocialAccounts] ADD [OAuthConnectionId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    CREATE TABLE [OAuthConnections] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Platform] nvarchar(32) NOT NULL,
        [ExternalUserId] nvarchar(200) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [ProtectedAccessToken] nvarchar(max) NOT NULL,
        [ProtectedRefreshToken] nvarchar(max) NULL,
        [AccessTokenExpiresAtUtc] datetime2(3) NULL,
        [Scopes] nvarchar(max) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [ConnectedByUserId] uniqueidentifier NOT NULL,
        [ConnectedAtUtc] datetime2(3) NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_OAuthConnections] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_OAuthConnections_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_OAuthConnections_Platform] CHECK ([Platform] IN ('LinkedIn', 'Facebook', 'Instagram')),
        CONSTRAINT [CK_OAuthConnections_Scopes_Json] CHECK (ISJSON([Scopes]) = 1),
        CONSTRAINT [CK_OAuthConnections_Status] CHECK ([Status] IN ('ACTIVE', 'EXPIRED', 'REVOKED')),
        CONSTRAINT [FK_OAuthConnections_OrganizationMembers_OrganizationId_ConnectedByUserId] FOREIGN KEY ([OrganizationId], [ConnectedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OAuthConnections_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    CREATE TABLE [OAuthStates] (
        [Id] uniqueidentifier NOT NULL,
        [StateHash] char(64) NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Platform] nvarchar(32) NOT NULL,
        [RequestedMode] nvarchar(32) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [ExpiresAtUtc] datetime2(3) NOT NULL,
        [ConsumedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_OAuthStates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OAuthStates_Platform] CHECK ([Platform] IN ('LinkedIn', 'Facebook', 'Instagram')),
        CONSTRAINT [CK_OAuthStates_RequestedMode] CHECK ([RequestedMode] IN ('LIVE', 'TEST_ACCOUNT', 'SIMULATED', 'NOT_CONFIGURED')),
        CONSTRAINT [FK_OAuthStates_OrganizationMembers_OrganizationId_UserId] FOREIGN KEY ([OrganizationId], [UserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    CREATE INDEX [IX_SocialAccounts_OrganizationId_OAuthConnectionId] ON [SocialAccounts] ([OrganizationId], [OAuthConnectionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SocialAccounts_OrganizationId_Platform_ExternalAccountId] ON [SocialAccounts] ([OrganizationId], [Platform], [ExternalAccountId]) WHERE [ExternalAccountId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    EXEC(N'ALTER TABLE [SocialAccounts] ADD CONSTRAINT [CK_SocialAccounts_Capabilities_Json] CHECK (ISJSON([Capabilities]) = 1)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    EXEC(N'ALTER TABLE [SocialAccounts] ADD CONSTRAINT [CK_SocialAccounts_ModeMatchesGrant] CHECK (([ExecutionMode] IN (''LIVE'', ''TEST_ACCOUNT'') AND [OAuthConnectionId] IS NOT NULL AND [ExternalAccountId] IS NOT NULL) OR ([ExecutionMode] IN (''SIMULATED'', ''NOT_CONFIGURED'') AND [OAuthConnectionId] IS NULL AND [ExternalAccountId] IS NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    CREATE INDEX [IX_OAuthConnections_OrganizationId_ConnectedByUserId] ON [OAuthConnections] ([OrganizationId], [ConnectedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OAuthConnections_OrganizationId_Platform_ExternalUserId] ON [OAuthConnections] ([OrganizationId], [Platform], [ExternalUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    CREATE INDEX [IX_OAuthStates_OrganizationId_UserId] ON [OAuthStates] ([OrganizationId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OAuthStates_StateHash] ON [OAuthStates] ([StateHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    ALTER TABLE [SocialAccounts] ADD CONSTRAINT [FK_SocialAccounts_OAuthConnections_OrganizationId_OAuthConnectionId] FOREIGN KEY ([OrganizationId], [OAuthConnectionId]) REFERENCES [OAuthConnections] ([OrganizationId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004174110_SocialConnections'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004174110_SocialConnections', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE TABLE [PublicationJobs] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [PostVersionId] uniqueidentifier NOT NULL,
        [ReviewRequestId] uniqueidentifier NOT NULL,
        [SocialAccountId] uniqueidentifier NOT NULL,
        [Platform] nvarchar(32) NOT NULL,
        [ExecutionMode] nvarchar(32) NOT NULL,
        [PayloadVersion] nvarchar(50) NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [PublishAtUtc] datetime2(3) NOT NULL,
        [LocalPublishAt] datetime2(3) NULL,
        [TimeZoneId] nvarchar(100) NULL,
        [UtcOffsetMinutes] int NULL,
        [DeliveryState] nvarchar(32) NOT NULL,
        [AttemptCount] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [NextActionAtUtc] datetime2(3) NULL,
        [LockedBy] nvarchar(200) NULL,
        [LockedAtUtc] datetime2(3) NULL,
        [LeaseExpiresAtUtc] datetime2(3) NULL,
        [LeaseToken] uniqueidentifier NULL,
        [DispatchStartedAtUtc] datetime2(3) NULL,
        [ProviderContainerId] nvarchar(200) NULL,
        [ExternalPostId] nvarchar(200) NULL,
        [ExternalPostUrl] nvarchar(2048) NULL,
        [CompletedAtUtc] datetime2(3) NULL,
        [LastErrorCode] nvarchar(100) NULL,
        [LastErrorMessage] nvarchar(1000) NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL,
        [CanceledByUserId] uniqueidentifier NULL,
        [CanceledAtUtc] datetime2(3) NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_PublicationJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PublicationJobs_Attempts] CHECK ([AttemptCount] >= 0 AND [MaxAttempts] >= 1 AND [AttemptCount] <= [MaxAttempts]),
        CONSTRAINT [CK_PublicationJobs_DeliveryState] CHECK ([DeliveryState] IN ('SCHEDULED', 'PROCESSING', 'WAITING_MEDIA', 'RETRY_PENDING', 'ACTION_REQUIRED', 'RECONCILIATION_REQUIRED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'CANCELED')),
        CONSTRAINT [CK_PublicationJobs_ExecutableMode] CHECK ([ExecutionMode] <> 'NOT_CONFIGURED'),
        CONSTRAINT [CK_PublicationJobs_ExecutionMode] CHECK ([ExecutionMode] IN ('LIVE', 'TEST_ACCOUNT', 'SIMULATED', 'NOT_CONFIGURED')),
        CONSTRAINT [CK_PublicationJobs_LocalTime] CHECK (([LocalPublishAt] IS NULL AND [TimeZoneId] IS NULL AND [UtcOffsetMinutes] IS NULL) OR ([LocalPublishAt] IS NOT NULL AND [TimeZoneId] IS NOT NULL AND [UtcOffsetMinutes] IS NOT NULL)),
        CONSTRAINT [CK_PublicationJobs_PayloadJson_Json] CHECK (ISJSON([PayloadJson]) = 1),
        CONSTRAINT [CK_PublicationJobs_Platform] CHECK ([Platform] IN ('LinkedIn', 'Facebook', 'Instagram')),
        CONSTRAINT [CK_PublicationJobs_SimulationIsHonest] CHECK (NOT ([ExecutionMode] = 'SIMULATED' AND [DeliveryState] = 'PUBLISHED') AND NOT ([ExecutionMode] <> 'SIMULATED' AND [DeliveryState] = 'SIMULATED') AND ([ExecutionMode] <> 'SIMULATED' OR ([ExternalPostId] IS NULL AND [ExternalPostUrl] IS NULL))),
        CONSTRAINT [FK_PublicationJobs_OrganizationMembers_OrganizationId_CanceledByUserId] FOREIGN KEY ([OrganizationId], [CanceledByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PublicationJobs_OrganizationMembers_OrganizationId_CreatedByUserId] FOREIGN KEY ([OrganizationId], [CreatedByUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PublicationJobs_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PublicationJobs_ReviewRequests_OrganizationId_PostId_PostVersionId_ReviewRequestId] FOREIGN KEY ([OrganizationId], [PostId], [PostVersionId], [ReviewRequestId]) REFERENCES [ReviewRequests] ([OrganizationId], [PostId], [PostVersionId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PublicationJobs_SocialAccounts_OrganizationId_SocialAccountId] FOREIGN KEY ([OrganizationId], [SocialAccountId]) REFERENCES [SocialAccounts] ([OrganizationId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE INDEX [IX_PublicationJobs_DeliveryState_NextActionAtUtc] ON [PublicationJobs] ([DeliveryState], [NextActionAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE INDEX [IX_PublicationJobs_OrganizationId_CanceledByUserId] ON [PublicationJobs] ([OrganizationId], [CanceledByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE INDEX [IX_PublicationJobs_OrganizationId_CreatedByUserId] ON [PublicationJobs] ([OrganizationId], [CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE INDEX [IX_PublicationJobs_OrganizationId_PostId_PostVersionId_ReviewRequestId] ON [PublicationJobs] ([OrganizationId], [PostId], [PostVersionId], [ReviewRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE INDEX [IX_PublicationJobs_OrganizationId_PublishAtUtc] ON [PublicationJobs] ([OrganizationId], [PublishAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE INDEX [IX_PublicationJobs_OrganizationId_SocialAccountId] ON [PublicationJobs] ([OrganizationId], [SocialAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    CREATE UNIQUE INDEX [UX_PublicationJobs_LifetimeIntent] ON [PublicationJobs] ([OrganizationId], [PostVersionId], [SocialAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004180323_PublicationJobs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004180323_PublicationJobs', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    ALTER TABLE [Notifications] DROP CONSTRAINT [CK_Notifications_Type];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    ALTER TABLE [PublicationJobs] ADD [MediaContainerCreatedAtUtc] datetime2(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    ALTER TABLE [PublicationJobs] ADD CONSTRAINT [AK_PublicationJobs_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    CREATE TABLE [PublicationAttempts] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [PublicationJobId] uniqueidentifier NOT NULL,
        [AttemptNumber] int NOT NULL,
        [Step] nvarchar(32) NOT NULL,
        [WorkerId] nvarchar(200) NOT NULL,
        [StartedAtUtc] datetime2(3) NOT NULL,
        [FinishedAtUtc] datetime2(3) NULL,
        [ResultState] nvarchar(32) NULL,
        [ErrorCategory] nvarchar(32) NULL,
        [ErrorCode] nvarchar(100) NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [ProviderRequestId] nvarchar(200) NULL,
        [ExternalId] nvarchar(200) NULL,
        [Detail] nvarchar(1000) NULL,
        CONSTRAINT [PK_PublicationAttempts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PublicationAttempts_ErrorCategory] CHECK ([ErrorCategory] IN ('RETRYABLE', 'ACTION_REQUIRED', 'UNCERTAIN', 'PERMANENT')),
        CONSTRAINT [CK_PublicationAttempts_ResultState] CHECK ([ResultState] IN ('SCHEDULED', 'PROCESSING', 'WAITING_MEDIA', 'RETRY_PENDING', 'ACTION_REQUIRED', 'RECONCILIATION_REQUIRED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'CANCELED')),
        CONSTRAINT [CK_PublicationAttempts_Step] CHECK ([Step] IN ('DISPATCH', 'MEDIA_STATUS')),
        CONSTRAINT [FK_PublicationAttempts_PublicationJobs_OrganizationId_PublicationJobId] FOREIGN KEY ([OrganizationId], [PublicationJobId]) REFERENCES [PublicationJobs] ([OrganizationId], [Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    EXEC(N'ALTER TABLE [PublicationJobs] ADD CONSTRAINT [CK_PublicationJobs_PublishedWasDispatched] CHECK ([DeliveryState] <> ''PUBLISHED'' OR [DispatchStartedAtUtc] IS NOT NULL)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    EXEC(N'ALTER TABLE [Notifications] ADD CONSTRAINT [CK_Notifications_Type] CHECK ([Type] IN (''REVIEW_REQUESTED'', ''CHANGES_REQUESTED'', ''APPROVED'', ''SCHEDULED'', ''PUBLISHED'', ''SIMULATED'', ''FAILED'', ''ACTION_REQUIRED'', ''REAUTHORIZATION_REQUIRED'', ''RECONCILIATION_REQUIRED''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    CREATE INDEX [IX_PublicationAttempts_OrganizationId_PublicationJobId] ON [PublicationAttempts] ([OrganizationId], [PublicationJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    CREATE INDEX [IX_PublicationAttempts_PublicationJobId_StartedAtUtc] ON [PublicationAttempts] ([PublicationJobId], [StartedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004183516_PublishingEngine'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004183516_PublishingEngine', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005060850_Operations'
)
BEGIN
    CREATE TABLE [WorkerHeartbeats] (
        [WorkerId] nvarchar(200) NOT NULL,
        [Kind] nvarchar(32) NOT NULL,
        [StartedAtUtc] datetime2(3) NOT NULL,
        [LastSeenAtUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_WorkerHeartbeats] PRIMARY KEY ([WorkerId]),
        CONSTRAINT [CK_WorkerHeartbeats_Kind] CHECK ([Kind] IN ('AI', 'PUBLISHING'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005060850_Operations'
)
BEGIN
    CREATE INDEX [IX_WorkerHeartbeats_LastSeenAtUtc] ON [WorkerHeartbeats] ([LastSeenAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005060850_Operations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005060850_Operations', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Organizations] ADD [Industry] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Organizations] ADD [UpdatedAtUtc] datetime2(3) NOT NULL DEFAULT '0001-01-01T00:00:00.000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'UPDATE o SET o.Industry = COALESCE(NULLIF(LEFT(b.Industry, 200), N''''), N''Unspecified''), o.UpdatedAtUtc = o.CreatedAtUtc
    FROM dbo.Organizations o LEFT JOIN dbo.BrandProfiles b ON b.OrganizationId = o.Id')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC sp_rename N'[BrandProfiles].[DefaultAudience]', N'TargetAudience', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC sp_rename N'[BrandProfiles].[WebsiteUrl]', N'Website', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [BrandProfiles] ADD [BrandTone] nvarchar(32) NOT NULL DEFAULT N'Professional';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'UPDATE dbo.BrandProfiles SET BusinessDescription = LEFT(CONCAT(BusinessDescription,
        N'' Products/services: '', ProductsServices, N'' Value proposition: '', ValueProposition), 2000)')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [BrandProfiles] DROP CONSTRAINT [CK_BrandProfiles_BrandHashtags_Json];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [BrandProfiles] DROP CONSTRAINT [CK_BrandProfiles_EmojiPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [BrandProfiles] DROP CONSTRAINT [CK_BrandProfiles_PreferredTerms_Json];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [BrandProfiles] DROP CONSTRAINT [CK_BrandProfiles_VoiceTraits_Json];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'BrandName');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [BrandName];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'Industry');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [Industry];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'ProductsServices');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [ProductsServices];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'ValueProposition');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [ValueProposition];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'VoiceTraits');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [VoiceTraits];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'DefaultLanguage');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [DefaultLanguage];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'PreferredCtaStyle');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [PreferredCtaStyle];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'PreferredTerms');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var7 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [PreferredTerms];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'BrandHashtags');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var8 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [BrandHashtags];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'EmojiPreference');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var9 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [EmojiPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var10 nvarchar(max);
    SELECT @var10 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BrandProfiles]') AND [c].[name] = N'MarketNotes');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [BrandProfiles] DROP CONSTRAINT ' + @var10 + ';');
    ALTER TABLE [BrandProfiles] DROP COLUMN [MarketNotes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [BrandProfiles] ADD CONSTRAINT [CK_BrandProfiles_BrandTone] CHECK ([BrandTone] IN (''Professional'', ''Friendly'', ''Casual'', ''Confident'', ''Informative'', ''Conversational''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    CREATE TABLE [CampaignPlatforms] (
        [CampaignId] uniqueidentifier NOT NULL,
        [Platform] nvarchar(32) NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_CampaignPlatforms] PRIMARY KEY ([CampaignId], [Platform]),
        CONSTRAINT [CK_CampaignPlatforms_Platform] CHECK ([Platform] IN ('LinkedIn', 'Facebook', 'Instagram')),
        CONSTRAINT [FK_CampaignPlatforms_Campaigns_OrganizationId_CampaignId] FOREIGN KEY ([OrganizationId], [CampaignId]) REFERENCES [Campaigns] ([OrganizationId], [Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    CREATE INDEX [IX_CampaignPlatforms_OrganizationId_CampaignId] ON [CampaignPlatforms] ([OrganizationId], [CampaignId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'INSERT INTO dbo.CampaignPlatforms (CampaignId, Platform, OrganizationId)
    SELECT DISTINCT c.Id, j.[value], c.OrganizationId FROM dbo.Campaigns c CROSS APPLY OPENJSON(c.Platforms) j')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_EmojiPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_HashtagPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_LengthPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_Platforms_Json];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_Status];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC sp_rename N'[Campaigns].[KeyMessage]', N'Description', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC sp_rename N'[Campaigns].[DestinationUrl]', N'CallToActionUrl', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [ContentLength] nvarchar(32) NOT NULL DEFAULT N'MEDIUM';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [HashtagMode] nvarchar(32) NOT NULL DEFAULT N'AUTO';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [EmojiMode] nvarchar(32) NOT NULL DEFAULT N'AUTO';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'UPDATE c SET
        c.Description = LEFT(CONCAT(c.Topic, N'': '', c.Description, N'' (Objective: '', c.Objective, N'')''), 2000),
        c.Objective = N''BRAND_AWARENESS'',
        c.TargetAudience = COALESCE(c.TargetAudience, b.TargetAudience, N''''),
        c.CallToAction = CASE WHEN c.CallToActionUrl LIKE N''https://%'' THEN N''LEARN_MORE'' ELSE N''NONE'' END,
        c.CallToActionUrl = CASE WHEN c.CallToActionUrl LIKE N''https://%'' THEN c.CallToActionUrl END,
        c.ToneOverride = NULL,
        c.ContentLength = CASE c.LengthPreference WHEN N''Short'' THEN N''SHORT'' WHEN N''Long'' THEN N''LONG'' ELSE N''MEDIUM'' END,
        c.HashtagMode = CASE c.HashtagPreference WHEN N''None'' THEN N''NONE'' ELSE N''AUTO'' END,
        c.EmojiMode = CASE c.EmojiPreference WHEN N''None'' THEN N''NONE'' ELSE N''AUTO'' END
    FROM dbo.Campaigns c LEFT JOIN dbo.BrandProfiles b ON b.OrganizationId = c.OrganizationId')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var11 nvarchar(max);
    SELECT @var11 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'Topic');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var11 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [Topic];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var12 nvarchar(max);
    SELECT @var12 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'PainPoint');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var12 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [PainPoint];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var13 nvarchar(max);
    SELECT @var13 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'PrimaryBenefit');
    IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var13 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [PrimaryBenefit];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var14 nvarchar(max);
    SELECT @var14 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'ContentCategory');
    IF @var14 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var14 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [ContentCategory];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var15 nvarchar(max);
    SELECT @var15 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'ContentAngle');
    IF @var15 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var15 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [ContentAngle];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var16 nvarchar(max);
    SELECT @var16 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'HookDirection');
    IF @var16 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var16 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [HookDirection];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var17 nvarchar(max);
    SELECT @var17 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'StartDate');
    IF @var17 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var17 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [StartDate];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var18 nvarchar(max);
    SELECT @var18 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'EndDate');
    IF @var18 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var18 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [EndDate];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var19 nvarchar(max);
    SELECT @var19 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'EmbargoNotes');
    IF @var19 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var19 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [EmbargoNotes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var20 nvarchar(max);
    SELECT @var20 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'LengthPreference');
    IF @var20 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var20 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [LengthPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var21 nvarchar(max);
    SELECT @var21 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'HashtagPreference');
    IF @var21 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var21 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [HashtagPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var22 nvarchar(max);
    SELECT @var22 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'EmojiPreference');
    IF @var22 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var22 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [EmojiPreference];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var23 nvarchar(max);
    SELECT @var23 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'Platforms');
    IF @var23 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var23 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [Platforms];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var24 nvarchar(max);
    SELECT @var24 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'Objective');
    IF @var24 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var24 + ';');
    ALTER TABLE [Campaigns] ALTER COLUMN [Objective] nvarchar(32) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var25 nvarchar(max);
    SELECT @var25 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'TargetAudience');
    IF @var25 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var25 + ';');
    ALTER TABLE [Campaigns] ALTER COLUMN [TargetAudience] nvarchar(2000) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'UPDATE dbo.Campaigns SET CallToAction = N''NONE'' WHERE CallToAction IS NULL')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var26 nvarchar(max);
    SELECT @var26 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'CallToAction');
    IF @var26 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var26 + ';');
    ALTER TABLE [Campaigns] ALTER COLUMN [CallToAction] nvarchar(32) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var27 nvarchar(max);
    SELECT @var27 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'ToneOverride');
    IF @var27 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var27 + ';');
    ALTER TABLE [Campaigns] ALTER COLUMN [ToneOverride] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_Objective] CHECK ([Objective] IN (''BRAND_AWARENESS'', ''PRODUCT_LAUNCH'', ''LEAD_GENERATION'', ''ENGAGEMENT'', ''WEBSITE_TRAFFIC'', ''ANNOUNCEMENT'', ''EDUCATIONAL''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_CallToAction] CHECK ([CallToAction] IN (''NONE'', ''LEARN_MORE'', ''VISIT_WEBSITE'', ''BOOK_DEMO'', ''SIGN_UP'', ''CONTACT_US'', ''REGISTER_NOW'', ''DOWNLOAD''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_ToneOverride] CHECK ([ToneOverride] IN (''Professional'', ''Friendly'', ''Casual'', ''Confident'', ''Informative'', ''Conversational''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_ContentLength] CHECK ([ContentLength] IN (''SHORT'', ''MEDIUM'', ''LONG''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_HashtagMode] CHECK ([HashtagMode] IN (''AUTO'', ''NONE''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_EmojiMode] CHECK ([EmojiMode] IN (''AUTO'', ''NONE''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_Status] CHECK ([Status] IN (''Draft'', ''Active'', ''Archived''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    DECLARE @var28 nvarchar(max);
    SELECT @var28 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CampaignFacts]') AND [c].[name] = N'SourceNote');
    IF @var28 IS NOT NULL EXEC(N'ALTER TABLE [CampaignFacts] DROP CONSTRAINT ' + @var28 + ';');
    ALTER TABLE [CampaignFacts] ALTER COLUMN [SourceNote] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005111530_SimplifiedInputModel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005111530_SimplifiedInputModel', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005125652_SystemAdminFlag'
)
BEGIN
    ALTER TABLE [Users] ADD [IsSystemAdmin] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005125652_SystemAdminFlag'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005125652_SystemAdminFlag', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    ALTER TABLE [WorkerHeartbeats] ADD [Version] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    ALTER TABLE [PublicationJobs] ADD [CorrelationId] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] ADD [CorrelationId] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE TABLE [PlatformAiConfigurations] (
        [Id] uniqueidentifier NOT NULL,
        [Provider] nvarchar(32) NOT NULL,
        [BaseUrl] nvarchar(2048) NULL,
        [Model] nvarchar(200) NOT NULL,
        [FallbackModel] nvarchar(200) NULL,
        [MaxOutputTokens] int NULL,
        [RequestTimeoutSeconds] int NULL,
        [Enabled] bit NOT NULL,
        [ApiKeySecretRef] nvarchar(200) NULL,
        [ApiKeyUpdatedAtUtc] datetime2 NULL,
        [UpdatedByUserId] uniqueidentifier NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_PlatformAiConfigurations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlatformAiConfigurations_Provider] CHECK ([Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE TABLE [PlatformAuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [ActorUserId] uniqueidentifier NULL,
        [Action] nvarchar(100) NOT NULL,
        [TargetType] nvarchar(50) NOT NULL,
        [TargetId] nvarchar(100) NOT NULL,
        [TargetLabel] nvarchar(200) NULL,
        [OrganizationId] uniqueidentifier NULL,
        [Result] nvarchar(20) NOT NULL,
        [CorrelationId] nvarchar(64) NULL,
        [MetadataJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PlatformAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlatformAuditLogs_MetadataJson_Json] CHECK (ISJSON([MetadataJson]) = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE TABLE [PlatformSecrets] (
        [Name] nvarchar(200) NOT NULL,
        [ProtectedValue] nvarchar(max) NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PlatformSecrets] PRIMARY KEY ([Name])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE TABLE [SignInEvents] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Email] nvarchar(256) NOT NULL,
        [Area] nvarchar(20) NOT NULL,
        [EventType] nvarchar(50) NOT NULL,
        [Succeeded] bit NOT NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(300) NULL,
        [CorrelationId] nvarchar(64) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SignInEvents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE TABLE [SocialProviderConfigurations] (
        [Provider] nvarchar(32) NOT NULL,
        [ClientId] nvarchar(200) NOT NULL,
        [RedirectUri] nvarchar(2048) NOT NULL,
        [ApiVersion] nvarchar(20) NULL,
        [Enabled] bit NOT NULL,
        [ClientSecretRef] nvarchar(200) NULL,
        [SecretUpdatedAtUtc] datetime2 NULL,
        [UpdatedByUserId] uniqueidentifier NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_SocialProviderConfigurations] PRIMARY KEY ([Provider]),
        CONSTRAINT [CK_SocialProviderConfigurations_Provider] CHECK ([Provider] IN ('LINKEDIN', 'META'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE TABLE [SystemEvents] (
        [Id] uniqueidentifier NOT NULL,
        [Level] nvarchar(20) NOT NULL,
        [Service] nvarchar(50) NOT NULL,
        [EventName] nvarchar(100) NOT NULL,
        [Message] nvarchar(2000) NOT NULL,
        [CorrelationId] nvarchar(64) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemEvents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE INDEX [IX_PlatformAuditLogs_CreatedAtUtc] ON [PlatformAuditLogs] ([CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE INDEX [IX_SignInEvents_CreatedAtUtc] ON [SignInEvents] ([CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE INDEX [IX_SignInEvents_UserId_CreatedAtUtc] ON [SignInEvents] ([UserId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    CREATE INDEX [IX_SystemEvents_CreatedAtUtc] ON [SystemEvents] ([CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005154534_PlatformAdministration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005154534_PlatformAdministration', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005160000_CustomerExperience'
)
BEGIN
    ALTER TABLE [Notifications] DROP CONSTRAINT [CK_Notifications_Type];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005160000_CustomerExperience'
)
BEGIN
    EXEC(N'ALTER TABLE [Notifications] ADD CONSTRAINT [CK_Notifications_Type] CHECK ([Type] IN (''REVIEW_REQUESTED'', ''CHANGES_REQUESTED'', ''APPROVED'', ''SCHEDULED'', ''PUBLISHED'', ''SIMULATED'', ''FAILED'', ''ACTION_REQUIRED'', ''REAUTHORIZATION_REQUIRED'', ''RECONCILIATION_REQUIRED'', ''TEAM_INVITATION''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005160000_CustomerExperience'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_CallToAction];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005160000_CustomerExperience'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_Objective];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005160000_CustomerExperience'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_CallToAction] CHECK ([CallToAction] IN (''NONE'', ''LEARN_MORE'', ''VISIT_WEBSITE'', ''BOOK_DEMO'', ''SIGN_UP'', ''CONTACT_US'', ''REGISTER_NOW'', ''DOWNLOAD'', ''AUTO''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005160000_CustomerExperience'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_Objective] CHECK ([Objective] IN (''BRAND_AWARENESS'', ''PRODUCT_LAUNCH'', ''LEAD_GENERATION'', ''ENGAGEMENT'', ''WEBSITE_TRAFFIC'', ''ANNOUNCEMENT'', ''EDUCATIONAL'', ''AUTO''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005160000_CustomerExperience'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005160000_CustomerExperience', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PostVersions] DROP CONSTRAINT [CK_PostVersions_CreationSource];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [MediaAssets] DROP CONSTRAINT [CK_MediaAssets_SourceType];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT [CK_AiGenerationRequests_Kind];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [AiCallLogs] DROP CONSTRAINT [CK_AiCallLogs_Purpose];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [ImageApiKeySecretRef] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [ImageApiKeyUpdatedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [ImageBaseUrl] nvarchar(2048) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [ImageEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [ImageModel] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [ImageProvider] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [ImageSize] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [MediaAssets] ADD [GenerationModel] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [MediaAssets] ADD [GenerationProvider] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    ALTER TABLE [MediaAssets] ADD [GenerationRequestId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    EXEC(N'ALTER TABLE [PostVersions] ADD CONSTRAINT [CK_PostVersions_CreationSource] CHECK ([CreationSource] IN (''Manual'', ''AiGeneration'', ''AiRewrite'', ''AiImage''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    EXEC(N'ALTER TABLE [PlatformAiConfigurations] ADD CONSTRAINT [CK_PlatformAiConfigurations_ImageProvider] CHECK ([ImageProvider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    EXEC(N'ALTER TABLE [MediaAssets] ADD CONSTRAINT [CK_MediaAssets_SourceType] CHECK ([SourceType] IN (''Upload'', ''AiGenerated''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    EXEC(N'ALTER TABLE [AiGenerationRequests] ADD CONSTRAINT [CK_AiGenerationRequests_Kind] CHECK ([Kind] IN (''Generation'', ''Rewrite'', ''ImageGeneration''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    EXEC(N'ALTER TABLE [AiCallLogs] ADD CONSTRAINT [CK_AiCallLogs_Purpose] CHECK ([Purpose] IN (''Generation'', ''Evaluation'', ''Rewrite'', ''ModelListing'', ''ImageGeneration''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005164621_ImageGeneration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005164621_ImageGeneration', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DROP TABLE [CampaignFacts];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DROP TABLE [CampaignPlatforms];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DROP INDEX [IX_Posts_OrganizationId_CampaignId_Platform] ON [Posts];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_CallToAction];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_ContentLength];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_EmojiMode];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_HashtagMode];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_Objective];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [Campaigns] DROP CONSTRAINT [CK_Campaigns_ToneOverride];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT [CK_AiGenerationRequests_RewriteAction];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var29 nvarchar(max);
    SELECT @var29 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'CallToAction');
    IF @var29 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var29 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [CallToAction];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var30 nvarchar(max);
    SELECT @var30 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'CallToActionUrl');
    IF @var30 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var30 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [CallToActionUrl];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var31 nvarchar(max);
    SELECT @var31 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'ContentLength');
    IF @var31 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var31 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [ContentLength];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var32 nvarchar(max);
    SELECT @var32 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'EmojiMode');
    IF @var32 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var32 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [EmojiMode];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var33 nvarchar(max);
    SELECT @var33 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'HashtagMode');
    IF @var33 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var33 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [HashtagMode];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var34 nvarchar(max);
    SELECT @var34 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'Objective');
    IF @var34 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var34 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [Objective];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var35 nvarchar(max);
    SELECT @var35 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'TargetAudience');
    IF @var35 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var35 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [TargetAudience];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var36 nvarchar(max);
    SELECT @var36 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'ToneOverride');
    IF @var36 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var36 + ';');
    ALTER TABLE [Campaigns] DROP COLUMN [ToneOverride];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [ReviewRequests] ADD [AssignedReviewerUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var37 nvarchar(max);
    SELECT @var37 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Posts]') AND [c].[name] = N'CampaignId');
    IF @var37 IS NOT NULL EXEC(N'ALTER TABLE [Posts] DROP CONSTRAINT ' + @var37 + ';');
    ALTER TABLE [Posts] ALTER COLUMN [CampaignId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var38 nvarchar(max);
    SELECT @var38 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Campaigns]') AND [c].[name] = N'Description');
    IF @var38 IS NOT NULL EXEC(N'ALTER TABLE [Campaigns] DROP CONSTRAINT ' + @var38 + ';');
    ALTER TABLE [Campaigns] ALTER COLUMN [Description] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [Color] varchar(7) NOT NULL DEFAULT '#2563EB';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    DECLARE @var39 nvarchar(max);
    SELECT @var39 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AiGenerationRequests]') AND [c].[name] = N'CampaignId');
    IF @var39 IS NOT NULL EXEC(N'ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT ' + @var39 + ';');
    ALTER TABLE [AiGenerationRequests] ALTER COLUMN [CampaignId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    CREATE INDEX [IX_ReviewRequests_OrganizationId_AssignedReviewerUserId] ON [ReviewRequests] ([OrganizationId], [AssignedReviewerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    CREATE INDEX [IX_Posts_OrganizationId_CampaignId] ON [Posts] ([OrganizationId], [CampaignId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    EXEC(N'ALTER TABLE [Campaigns] ADD CONSTRAINT [CK_Campaigns_Color] CHECK ([Color] LIKE ''#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    EXEC(N'ALTER TABLE [AiGenerationRequests] ADD CONSTRAINT [CK_AiGenerationRequests_RewriteAction] CHECK ([RewriteAction] IN (''Rewrite'', ''Regenerate'', ''Shorten'', ''Expand'', ''MoreProfessional'', ''ImproveHook'', ''LessPromotional'', ''Custom''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    ALTER TABLE [ReviewRequests] ADD CONSTRAINT [FK_ReviewRequests_OrganizationMembers_OrganizationId_AssignedReviewerUserId] FOREIGN KEY ([OrganizationId], [AssignedReviewerUserId]) REFERENCES [OrganizationMembers] ([OrganizationId], [UserId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006071212_SimplifiedWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006071212_SimplifiedWorkflow', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] DROP CONSTRAINT [CK_PlatformAiConfigurations_ImageProvider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] DROP CONSTRAINT [CK_PlatformAiConfigurations_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    ALTER TABLE [AiProviderSettings] DROP CONSTRAINT [CK_AiProviderSettings_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT [CK_AiGenerationRequests_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    ALTER TABLE [AiCallLogs] DROP CONSTRAINT [CK_AiCallLogs_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [PlatformAiConfigurations] ADD CONSTRAINT [CK_PlatformAiConfigurations_ImageProvider] CHECK ([ImageProvider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [PlatformAiConfigurations] ADD CONSTRAINT [CK_PlatformAiConfigurations_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiProviderSettings] ADD CONSTRAINT [CK_AiProviderSettings_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiGenerationRequests] ADD CONSTRAINT [CK_AiGenerationRequests_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiCallLogs] ADD CONSTRAINT [CK_AiCallLogs_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007090023_HuggingFaceProvider'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007090023_HuggingFaceProvider', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007095327_AiRequestProgress'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] ADD [Stage] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007095327_AiRequestProgress'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] ADD [Warning] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007095327_AiRequestProgress'
)
BEGIN
    EXEC(N'ALTER TABLE [AiGenerationRequests] ADD CONSTRAINT [CK_AiGenerationRequests_Stage] CHECK ([Stage] IN (''ANALYZING_IMAGES'', ''WRITING'', ''GENERATING_IMAGE''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007095327_AiRequestProgress'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007095327_AiRequestProgress', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007100130_AiVisionSettings'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [VisionEnabled] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007100130_AiVisionSettings'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [VisionModel] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007100130_AiVisionSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007100130_AiVisionSettings', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007125419_VisionApiKey'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [VisionApiKeySecretRef] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007125419_VisionApiKey'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] ADD [VisionApiKeyUpdatedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007125419_VisionApiKey'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007125419_VisionApiKey', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007162849_MetaConfigurationId'
)
BEGIN
    ALTER TABLE [SocialProviderConfigurations] ADD [ConfigurationId] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007162849_MetaConfigurationId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007162849_MetaConfigurationId', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT [FK_AiGenerationRequests_Campaigns_OrganizationId_CampaignId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    ALTER TABLE [Posts] DROP CONSTRAINT [FK_Posts_Campaigns_OrganizationId_CampaignId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    DROP TABLE [Campaigns];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    DROP INDEX [IX_Posts_OrganizationId_CampaignId] ON [Posts];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    DROP INDEX [IX_AiGenerationRequests_OrganizationId_CampaignId_CreatedAtUtc] ON [AiGenerationRequests];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    DECLARE @var40 nvarchar(max);
    SELECT @var40 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Posts]') AND [c].[name] = N'CampaignId');
    IF @var40 IS NOT NULL EXEC(N'ALTER TABLE [Posts] DROP CONSTRAINT ' + @var40 + ';');
    ALTER TABLE [Posts] DROP COLUMN [CampaignId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    DECLARE @var41 nvarchar(max);
    SELECT @var41 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AiGenerationRequests]') AND [c].[name] = N'CampaignId');
    IF @var41 IS NOT NULL EXEC(N'ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT ' + @var41 + ';');
    ALTER TABLE [AiGenerationRequests] DROP COLUMN [CampaignId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    CREATE INDEX [IX_AiGenerationRequests_OrganizationId_CreatedAtUtc] ON [AiGenerationRequests] ([OrganizationId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008102833_RemoveCampaigns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008102833_RemoveCampaigns', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] DROP CONSTRAINT [CK_PlatformAiConfigurations_ImageProvider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] DROP CONSTRAINT [CK_PlatformAiConfigurations_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    ALTER TABLE [AiProviderSettings] DROP CONSTRAINT [CK_AiProviderSettings_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT [CK_AiGenerationRequests_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    ALTER TABLE [AiCallLogs] DROP CONSTRAINT [CK_AiCallLogs_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [PlatformAiConfigurations] ADD CONSTRAINT [CK_PlatformAiConfigurations_ImageProvider] CHECK ([ImageProvider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [PlatformAiConfigurations] ADD CONSTRAINT [CK_PlatformAiConfigurations_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiProviderSettings] ADD CONSTRAINT [CK_AiProviderSettings_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiGenerationRequests] ADD CONSTRAINT [CK_AiGenerationRequests_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiCallLogs] ADD CONSTRAINT [CK_AiCallLogs_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008104317_StabilityImageProvider'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008104317_StabilityImageProvider', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] DROP CONSTRAINT [CK_PlatformAiConfigurations_ImageProvider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    ALTER TABLE [PlatformAiConfigurations] DROP CONSTRAINT [CK_PlatformAiConfigurations_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    ALTER TABLE [AiProviderSettings] DROP CONSTRAINT [CK_AiProviderSettings_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    ALTER TABLE [AiGenerationRequests] DROP CONSTRAINT [CK_AiGenerationRequests_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    ALTER TABLE [AiCallLogs] DROP CONSTRAINT [CK_AiCallLogs_Provider];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [PlatformAiConfigurations] ADD CONSTRAINT [CK_PlatformAiConfigurations_ImageProvider] CHECK ([ImageProvider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI'', ''CloudflareWorkersAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [PlatformAiConfigurations] ADD CONSTRAINT [CK_PlatformAiConfigurations_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI'', ''CloudflareWorkersAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiProviderSettings] ADD CONSTRAINT [CK_AiProviderSettings_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI'', ''CloudflareWorkersAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiGenerationRequests] ADD CONSTRAINT [CK_AiGenerationRequests_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI'', ''CloudflareWorkersAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    EXEC(N'ALTER TABLE [AiCallLogs] ADD CONSTRAINT [CK_AiCallLogs_Provider] CHECK ([Provider] IN (''OpenAI'', ''Groq'', ''OpenAICompatible'', ''HuggingFace'', ''StabilityAI'', ''CloudflareWorkersAI''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008110416_CloudflareImageProvider'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008110416_CloudflareImageProvider', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    CREATE TABLE [PostVersionImages] (
        [Position] int NOT NULL,
        [PostVersionId] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [MediaAssetId] uniqueidentifier NOT NULL,
        [AltText] nvarchar(1000) NULL,
        CONSTRAINT [PK_PostVersionImages] PRIMARY KEY ([PostVersionId], [Position]),
        CONSTRAINT [CK_PostVersionImages_Position] CHECK ([Position] >= 0 AND [Position] < 4),
        CONSTRAINT [FK_PostVersionImages_MediaAssets_OrganizationId_MediaAssetId] FOREIGN KEY ([OrganizationId], [MediaAssetId]) REFERENCES [MediaAssets] ([OrganizationId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PostVersionImages_PostVersions_PostVersionId] FOREIGN KEY ([PostVersionId]) REFERENCES [PostVersions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    CREATE INDEX [IX_PostVersionImages_OrganizationId_MediaAssetId] ON [PostVersionImages] ([OrganizationId], [MediaAssetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PostVersionImages_PostVersionId_MediaAssetId] ON [PostVersionImages] ([PostVersionId], [MediaAssetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    INSERT INTO [PostVersionImages] ([PostVersionId], [Position], [OrganizationId], [MediaAssetId], [AltText])
    SELECT [Id], 0, [OrganizationId], [MediaAssetId], [AltText] FROM [PostVersions] WHERE [MediaAssetId] IS NOT NULL
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    EXEC(N'CREATE TRIGGER [dbo].[TR_PostVersionImages_Immutable] ON [dbo].[PostVersionImages] AFTER UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''PostVersionImages are immutable. Create a new version instead.'', 1;
    END')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    ALTER TABLE [PostVersions] DROP CONSTRAINT [FK_PostVersions_MediaAssets_OrganizationId_MediaAssetId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    DROP INDEX [IX_PostVersions_OrganizationId_MediaAssetId] ON [PostVersions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    DECLARE @var42 nvarchar(max);
    SELECT @var42 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PostVersions]') AND [c].[name] = N'AltText');
    IF @var42 IS NOT NULL EXEC(N'ALTER TABLE [PostVersions] DROP CONSTRAINT ' + @var42 + ';');
    ALTER TABLE [PostVersions] DROP COLUMN [AltText];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    DECLARE @var43 nvarchar(max);
    SELECT @var43 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PostVersions]') AND [c].[name] = N'MediaAssetId');
    IF @var43 IS NOT NULL EXEC(N'ALTER TABLE [PostVersions] DROP CONSTRAINT ' + @var43 + ';');
    ALTER TABLE [PostVersions] DROP COLUMN [MediaAssetId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008112426_MultiImagePosts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008112426_MultiImagePosts', N'10.0.12');
END;

COMMIT;
GO

