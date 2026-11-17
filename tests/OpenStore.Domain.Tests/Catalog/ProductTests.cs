using OpenStore.Domain.Catalog;
using OpenStore.Domain.Shared;
using Shouldly;

namespace OpenStore.Domain.Tests.Catalog;

public sealed class ProductTests
{
    private static Dictionary<string, long> Prices(long amountMinor, string currency = "USD")
        => new(StringComparer.Ordinal) { [currency] = amountMinor };

    [Theory]
    [InlineData("T-Shirt")]
    [InlineData("t_shirt")]
    [InlineData("-t-shirt")]
    [InlineData("t-shirt-")]
    [InlineData("t--shirt")]
    [InlineData("t shirt")]
    public void Slug_不符合规范必须拒绝(string slug)
    {
        Should.Throw<DomainException>(() => Product.Create(Guid.NewGuid(), slug, "T", Array.Empty<ProductVariant>()))
            .Code.ShouldBe("INV_PRODUCT_SLUG_INVALID");
    }

    [Fact]
    public void Slug_为空必须拒绝()
    {
        Should.Throw<DomainException>(() => Product.Create(Guid.NewGuid(), "  ", "T", Array.Empty<ProductVariant>()))
            .Code.ShouldBe("INV_PRODUCT_SLUG_REQUIRED");
    }

    [Theory]
    [InlineData("t-shirt")]
    [InlineData("t-shirt-2")]
    [InlineData("a")]
    public void Slug_符合规范可以创建(string slug)
    {
        var product = Product.Create(Guid.NewGuid(), slug, "T", Array.Empty<ProductVariant>());
        product.Slug.ShouldBe(slug);
        product.Status.ShouldBe(ProductStatus.Draft);
    }

    [Fact]
    public void 没有变体的商品不能激活()
    {
        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T", Array.Empty<ProductVariant>());

        var ex = Should.Throw<InvariantViolationException>(() => product.ChangeStatus(ProductStatus.Active));
        ex.Code.ShouldBe("INV_PRODUCT_CANNOT_ACTIVATE_NO_VARIANT");
        product.Status.ShouldBe(ProductStatus.Draft);
    }

    [Fact]
    public void 有变体但某变体没有价格的商品不能激活()
    {
        var variants = new[]
        {
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=m", Prices(1999)),
            new ProductVariant(Guid.NewGuid(), "TS-RED-L", "color=red|size=l", new Dictionary<string, long>()),
        };
        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T", variants);

        var ex = Should.Throw<InvariantViolationException>(() => product.ChangeStatus(ProductStatus.Active));
        ex.Code.ShouldBe("INV_PRODUCT_CANNOT_ACTIVATE_VARIANT_WITHOUT_PRICE");
    }

    [Fact]
    public void 全部变体都有价格时可以激活_并且状态变更是幂等的()
    {
        var variants = new[]
        {
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=m", Prices(1999)),
            new ProductVariant(Guid.NewGuid(), "TS-RED-L", "color=red|size=l", Prices(2099)),
        };
        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T", variants);

        product.ChangeStatus(ProductStatus.Active);
        product.Status.ShouldBe(ProductStatus.Active);

        // 重复 PATCH 同一状态不能报错（admin 端点的幂等要求）
        Should.NotThrow(() => product.ChangeStatus(ProductStatus.Active));
        product.Status.ShouldBe(ProductStatus.Active);

        product.ChangeStatus(ProductStatus.Archived);
        product.Status.ShouldBe(ProductStatus.Archived);
    }

    [Fact]
    public void 未定义的枚举值必须拒绝_不能以脏数据落库()
    {
        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T", Array.Empty<ProductVariant>());

        Should.Throw<InvariantViolationException>(() => product.ChangeStatus((ProductStatus)99))
            .Code.ShouldBe("INV_PRODUCT_STATUS_INVALID");
    }

    [Fact]
    public void Rehydrate_重建时也必须校验不变量_脏数据不能静默通过()
    {
        // 模拟数据库里出现"Active 但变体无价"的历史脏数据：读取侧要炸，不要让它流到下单流程
        var ex = Should.Throw<InvariantViolationException>(() => Product.Rehydrate(
            Guid.NewGuid(),
            "t-shirt",
            "T",
            ProductStatus.Active,
            new[]
            {
                new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=m", new Dictionary<string, long>()),
            }));

        ex.Code.ShouldBe("INV_PRODUCT_CANNOT_ACTIVATE_VARIANT_WITHOUT_PRICE");
    }

    [Fact]
    public void 变体SKU_必须符合规范且不区分小写字母()
    {
        Should.Throw<DomainException>(() => new ProductVariant(Guid.NewGuid(), "ts-red-m", "color=red", Prices(1)))
            .Code.ShouldBe("INV_PRODUCT_VARIANT_SKU_INVALID");

        Should.Throw<DomainException>(() => new ProductVariant(Guid.NewGuid(), "TS RED M", "color=red", Prices(1)))
            .Code.ShouldBe("INV_PRODUCT_VARIANT_SKU_INVALID");
    }

    [Fact]
    public void 变体价格不允许负数()
    {
        Should.Throw<DomainException>(() => new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red", Prices(-1)))
            .Code.ShouldBe("INV_PRODUCT_VARIANT_PRICE_NEGATIVE");
    }

    [Fact]
    public void 变体价格字典必须不可被外部篡改()
    {
        var prices = Prices(1999);
        var variant = new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red", prices);

        prices["USD"] = 1; // 改原始字典

        variant.PriceIn("USD").MinorUnits.ShouldBe(1999);
    }

    [Fact]
    public void UpdateTitle_修改标题并沿用同一守卫()
    {
        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T", Array.Empty<ProductVariant>());

        product.UpdateTitle("New Title");

        product.Title.ShouldBe("New Title");
    }

    [Fact]
    public void UpdateTitle_空白标题必须拒绝()
    {
        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T", Array.Empty<ProductVariant>());

        Should.Throw<DomainException>(() => product.UpdateTitle("   "))
            .Code.ShouldBe("INV_PRODUCT_TITLE_REQUIRED");
    }

    [Fact]
    public void UpdateVariantPrice_覆盖既有币种价格()
    {
        var variantId = Guid.NewGuid();
        var product = Product.Create(
            Guid.NewGuid(),
            "t-shirt",
            "T",
            new[] { new ProductVariant(variantId, "TS-RED-M", "color=red|size=m", Prices(1999)) });

        product.UpdateVariantPrice(variantId, "USD", 2599);

        product.Variants.Single().PriceIn("USD").MinorUnits.ShouldBe(2599);
    }

    [Fact]
    public void UpdateVariantPrice_负价格必须拒绝()
    {
        var variantId = Guid.NewGuid();
        var product = Product.Create(
            Guid.NewGuid(),
            "t-shirt",
            "T",
            new[] { new ProductVariant(variantId, "TS-RED-M", "color=red|size=m", Prices(1999)) });

        Should.Throw<DomainException>(() => product.UpdateVariantPrice(variantId, "USD", -1))
            .Code.ShouldBe("INV_PRODUCT_VARIANT_PRICE_NEGATIVE");
    }

    [Fact]
    public void UpdateVariantPrice_不存在的变体必须拒绝()
    {
        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T", Array.Empty<ProductVariant>());

        Should.Throw<DomainException>(() => product.UpdateVariantPrice(Guid.NewGuid(), "USD", 1999))
            .Code.ShouldBe("PRODUCT_VARIANT_NOT_FOUND");
    }

    [Fact]
    public void UpdateVariantSku_修改SKU并沿用同一守卫()
    {
        var variantId = Guid.NewGuid();
        var product = Product.Create(
            Guid.NewGuid(),
            "t-shirt",
            "T",
            new[] { new ProductVariant(variantId, "TS-RED-M", "color=red|size=m", Prices(1999)) });

        product.UpdateVariantSku(variantId, "TS-RED-M-2");

        product.Variants.Single().Sku.ShouldBe("TS-RED-M-2");
    }

    [Fact]
    public void UpdateVariantSku_非法SKU必须拒绝()
    {
        var variantId = Guid.NewGuid();
        var product = Product.Create(
            Guid.NewGuid(),
            "t-shirt",
            "T",
            new[] { new ProductVariant(variantId, "TS-RED-M", "color=red|size=m", Prices(1999)) });

        Should.Throw<DomainException>(() => product.UpdateVariantSku(variantId, "ts-red-m"))
            .Code.ShouldBe("INV_PRODUCT_VARIANT_SKU_INVALID");
    }

    [Fact]
    public void 重复SKU_同一商品内必须拒绝_Create路径()
    {
        // 两条轴签名不同（不会撞签名守卫），仅 SKU 相同 —— 用来证明 SKU 唯一是独立不变量。
        var variants = new[]
        {
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=m", Prices(1999)),
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=l", Prices(2099)),
        };

        Should.Throw<InvariantViolationException>(
                () => Product.Create(Guid.NewGuid(), "t-shirt", "T", variants))
            .Code.ShouldBe("INV_PRODUCT_SKU_DUPLICATE");
    }

    [Fact]
    public void 重复SKU_同一商品内必须拒绝_Rehydrate路径()
    {
        // 读取侧也守：库里若出现重复 SKU 的历史脏数据，宁可炸在读取侧也不放进下单流程。
        var variants = new[]
        {
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=m", Prices(1999)),
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=l", Prices(2099)),
        };

        Should.Throw<InvariantViolationException>(
                () => Product.Rehydrate(Guid.NewGuid(), "t-shirt", "T", ProductStatus.Draft, variants))
            .Code.ShouldBe("INV_PRODUCT_SKU_DUPLICATE");
    }
}
