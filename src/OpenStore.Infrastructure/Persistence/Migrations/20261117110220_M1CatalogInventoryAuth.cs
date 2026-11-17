using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1CatalogInventoryAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminUser",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    EmailNormalized = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockoutUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PasswordChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUser", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttributeGroup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttributeSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ObjectType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ObjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObjectLabel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaAsset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ByteSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAsset", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Session",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IdleExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IpHash = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgentHash = table.Column<string>(type: "TEXT", nullable: true),
                    CsrfHash = table.Column<string>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokeReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Session", x => x.TokenHash);
                });

            migrationBuilder.CreateTable(
                name: "AttributeDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DataType = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFilterable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeDefinition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeDefinition_AttributeGroup_GroupId",
                        column: x => x.GroupId,
                        principalTable: "AttributeGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    AttributeSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.CheckConstraint("CK_Category_Depth", "\"Depth\" >= 1 AND \"Depth\" <= 5");
                    table.ForeignKey(
                        name: "FK_Category_AttributeSet_AttributeSetId",
                        column: x => x.AttributeSetId,
                        principalTable: "AttributeSet",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Category_Category_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttributeDefinitionTranslation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HelpText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeDefinitionTranslation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeDefinitionTranslation_AttributeDefinition_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttributeOption",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 9, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeOption", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeOption_AttributeDefinition_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttributeSetMember",
                columns: table => new
                {
                    AttributeSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeSetMember", x => new { x.AttributeSetId, x.AttributeDefinitionId });
                    table.ForeignKey(
                        name: "FK_AttributeSetMember_AttributeDefinition_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttributeSetMember_AttributeSet_AttributeSetId",
                        column: x => x.AttributeSetId,
                        principalTable: "AttributeSet",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CategoryAttributeOverride",
                columns: table => new
                {
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryAttributeOverride", x => new { x.CategoryId, x.AttributeDefinitionId });
                    table.ForeignKey(
                        name: "FK_CategoryAttributeOverride_AttributeDefinition_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CategoryAttributeOverride_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CategoryTranslation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryTranslation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryTranslation_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttributeOptionTranslation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeOptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeOptionTranslation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeOptionTranslation_AttributeOption_AttributeOptionId",
                        column: x => x.AttributeOptionId,
                        principalTable: "AttributeOption",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    InternalCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MinPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PrimaryImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WeightGram = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NumberValue = table.Column<double>(type: "REAL", nullable: true),
                    TextValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    BoolValue = table.Column<bool>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeValue", x => x.Id);
                    table.CheckConstraint("CK_PAV_BoolRange", "\"BoolValue\" IS NULL OR \"BoolValue\" IN (0, 1)");
                    table.CheckConstraint("CK_PAV_ExactlyOne", "(CASE WHEN \"OptionId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"NumberValue\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"TextValue\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"BoolValue\" IS NOT NULL THEN 1 ELSE 0 END) = 1");
                    table.ForeignKey(
                        name: "FK_ProductAttributeValue_AttributeDefinition_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValue_AttributeOption_OptionId",
                        column: x => x.OptionId,
                        principalTable: "AttributeOption",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValue_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductCategory",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategory", x => new { x.ProductId, x.CategoryId });
                    table.CheckConstraint("CK_ProductCategory_IsPrimary_Bit", "\"IsPrimary\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_ProductCategory_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCategory_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductTranslation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ShortDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTranslation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductTranslation_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantAxis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantAxis", x => x.Id);
                    table.CheckConstraint("CK_ProductVariantAxis_SortOrder", "\"SortOrder\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_ProductVariantAxis_AttributeDefinition_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantAxis_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MediaAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    AltText = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImage_MediaAsset_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAsset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductImage_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AxisSignature = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    MainImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    WeightGram = table.Column<int>(type: "INTEGER", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CompareAtMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariant", x => x.Id);
                    table.CheckConstraint("CK_ProductVariant_AmountMinor_Positive", "\"AmountMinor\" > 0");
                    table.CheckConstraint("CK_ProductVariant_CompareAt", "\"CompareAtMinor\" IS NULL OR \"CompareAtMinor\" >= \"AmountMinor\"");
                    table.ForeignKey(
                        name: "FK_ProductVariant_ProductImage_MainImageId",
                        column: x => x.MainImageId,
                        principalTable: "ProductImage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariant_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockItem",
                columns: table => new
                {
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OnHand = table.Column<int>(type: "INTEGER", nullable: false),
                    Reserved = table.Column<int>(type: "INTEGER", nullable: false),
                    Sold = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItem", x => x.VariantId);
                    table.CheckConstraint("CK_StockItem_NoOversell", "\"OnHand\" >= \"Reserved\" + \"Sold\"");
                    table.CheckConstraint("CK_StockItem_NonNegative", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Sold\" >= 0");
                    table.ForeignKey(
                        name: "FK_StockItem_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockLedger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<int>(type: "INTEGER", nullable: false),
                    OnHandDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    ReservedDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    SoldDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    OnHandAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    ReservedAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    SoldAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrderLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperatorType = table.Column<int>(type: "INTEGER", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperatorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockLedger_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VariantAttributeValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantAttributeValue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VariantAttributeValue_AttributeDefinition_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VariantAttributeValue_AttributeOption_OptionId",
                        column: x => x.OptionId,
                        principalTable: "AttributeOption",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VariantAttributeValue_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AdminUser_EmailNormalized",
                table: "AdminUser",
                column: "EmailNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttrDef_Filterable_Scope",
                table: "AttributeDefinition",
                columns: new[] { "IsFilterable", "Scope" });

            migrationBuilder.CreateIndex(
                name: "IX_AttrDef_Scope",
                table: "AttributeDefinition",
                column: "Scope");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeDefinition_GroupId",
                table: "AttributeDefinition",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "UX_AttributeDefinition_Code",
                table: "AttributeDefinition",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AttrDefTranslation_Def_Locale",
                table: "AttributeDefinitionTranslation",
                columns: new[] { "AttributeDefinitionId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AttributeGroup_Code",
                table: "AttributeGroup",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AttributeOption_Def_Code",
                table: "AttributeOption",
                columns: new[] { "AttributeDefinitionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AttrOptionTranslation_Option_Locale",
                table: "AttributeOptionTranslation",
                columns: new[] { "AttributeOptionId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AttributeSet_Code",
                table: "AttributeSet",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttributeSetMember_Definition",
                table: "AttributeSetMember",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Action",
                table: "AuditLog",
                columns: new[] { "Action", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Actor",
                table: "AuditLog",
                columns: new[] { "ActorType", "ActorId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Object",
                table: "AuditLog",
                columns: new[] { "ObjectType", "ObjectId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_OccurredAt",
                table: "AuditLog",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Category_AttributeSetId",
                table: "Category",
                column: "AttributeSetId");

            migrationBuilder.CreateIndex(
                name: "IX_Category_Parent_Sort",
                table: "Category",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Category_Path",
                table: "Category",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryAttrOverride_Definition",
                table: "CategoryAttributeOverride",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_CategoryTranslation_Category_Locale",
                table: "CategoryTranslation",
                columns: new[] { "CategoryId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CategoryTranslation_Locale_Slug",
                table: "CategoryTranslation",
                columns: new[] { "Locale", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAsset_ContentHash",
                table: "MediaAsset",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "UX_MediaAsset_StorageKey",
                table: "MediaAsset",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_Slug",
                table: "Product",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_Status_MinPrice",
                table: "Product",
                columns: new[] { "Status", "MinPriceMinor", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Status_PublishedAt",
                table: "Product",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Product_InternalCode",
                table: "Product",
                column: "InternalCode",
                unique: true,
                filter: "\"InternalCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Product_PrimaryImageId",
                table: "Product",
                column: "PrimaryImageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAV_Facet",
                table: "ProductAttributeValue",
                columns: new[] { "AttributeDefinitionId", "OptionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PAV_Number",
                table: "ProductAttributeValue",
                columns: new[] { "AttributeDefinitionId", "NumberValue" });

            migrationBuilder.CreateIndex(
                name: "IX_PAV_Product",
                table: "ProductAttributeValue",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValue_OptionId",
                table: "ProductAttributeValue",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "UX_PAV_Product_Def_Option",
                table: "ProductAttributeValue",
                columns: new[] { "ProductId", "AttributeDefinitionId", "OptionId" },
                unique: true,
                filter: "\"OptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategory_Category_Product",
                table: "ProductCategory",
                columns: new[] { "CategoryId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductCategory_PrimaryPerProduct",
                table: "ProductCategory",
                column: "ProductId",
                unique: true,
                filter: "\"IsPrimary\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImage_MediaAssetId",
                table: "ProductImage",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImage_Product_Sort",
                table: "ProductImage",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImage_Variant",
                table: "ProductImage",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductTranslation_Locale_Slug",
                table: "ProductTranslation",
                columns: new[] { "Locale", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductTranslation_Product_Locale",
                table: "ProductTranslation",
                columns: new[] { "ProductId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Variant_Barcode",
                table: "ProductVariant",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_Variant_Product_Status",
                table: "ProductVariant",
                columns: new[] { "ProductId", "Status", "Position" });

            migrationBuilder.CreateIndex(
                name: "UX_Variant_MainImageId",
                table: "ProductVariant",
                column: "MainImageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Variant_Product_AxisSignature",
                table: "ProductVariant",
                columns: new[] { "ProductId", "AxisSignature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Variant_Sku",
                table: "ProductVariant",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAxis_AttributeDefinitionId",
                table: "ProductVariantAxis",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_VariantAxis_Product_Def",
                table: "ProductVariantAxis",
                columns: new[] { "ProductId", "AttributeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Session_AbsoluteExpiresAt",
                table: "Session",
                column: "AbsoluteExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Session_Actor",
                table: "Session",
                columns: new[] { "ActorType", "ActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_ChangeType_Created",
                table: "StockLedger",
                columns: new[] { "ChangeType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_CreatedAt",
                table: "StockLedger",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_Order",
                table: "StockLedger",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_Variant_Id",
                table: "StockLedger",
                columns: new[] { "VariantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_VAV_Attr_Option",
                table: "VariantAttributeValue",
                columns: new[] { "AttributeDefinitionId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_VariantAttributeValue_OptionId",
                table: "VariantAttributeValue",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "UX_VAV_Variant_Attr",
                table: "VariantAttributeValue",
                columns: new[] { "VariantId", "AttributeDefinitionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_ProductImage_PrimaryImageId",
                table: "Product",
                column: "PrimaryImageId",
                principalTable: "ProductImage",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductImage_ProductVariant_VariantId",
                table: "ProductImage",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Product_ProductImage_PrimaryImageId",
                table: "Product");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariant_ProductImage_MainImageId",
                table: "ProductVariant");

            migrationBuilder.DropTable(
                name: "AdminUser");

            migrationBuilder.DropTable(
                name: "AttributeDefinitionTranslation");

            migrationBuilder.DropTable(
                name: "AttributeOptionTranslation");

            migrationBuilder.DropTable(
                name: "AttributeSetMember");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "CategoryAttributeOverride");

            migrationBuilder.DropTable(
                name: "CategoryTranslation");

            migrationBuilder.DropTable(
                name: "ProductAttributeValue");

            migrationBuilder.DropTable(
                name: "ProductCategory");

            migrationBuilder.DropTable(
                name: "ProductTranslation");

            migrationBuilder.DropTable(
                name: "ProductVariantAxis");

            migrationBuilder.DropTable(
                name: "Session");

            migrationBuilder.DropTable(
                name: "StockItem");

            migrationBuilder.DropTable(
                name: "StockLedger");

            migrationBuilder.DropTable(
                name: "VariantAttributeValue");

            migrationBuilder.DropTable(
                name: "Category");

            migrationBuilder.DropTable(
                name: "AttributeOption");

            migrationBuilder.DropTable(
                name: "AttributeSet");

            migrationBuilder.DropTable(
                name: "AttributeDefinition");

            migrationBuilder.DropTable(
                name: "AttributeGroup");

            migrationBuilder.DropTable(
                name: "ProductImage");

            migrationBuilder.DropTable(
                name: "MediaAsset");

            migrationBuilder.DropTable(
                name: "ProductVariant");

            migrationBuilder.DropTable(
                name: "Product");
        }
    }
}
