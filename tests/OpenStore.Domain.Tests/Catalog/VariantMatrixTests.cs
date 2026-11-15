using OpenStore.Domain.Catalog;
using OpenStore.Domain.Shared;
using Shouldly;

namespace OpenStore.Domain.Tests.Catalog;

public sealed class VariantMatrixTests
{
    [Fact]
    public void BuildSignature_轴顺序不同必须得到同一签名()
    {
        // 字典的插入顺序不同，枚举顺序就不同；如果不排序，同一组合会得到两个签名，
        // (ProductId, AxisSignature) 唯一索引就形同虚设（同一组合能建出两条变体）。
        var first = new Dictionary<string, string> { ["color"] = "red", ["size"] = "m" };
        var second = new Dictionary<string, string> { ["size"] = "m", ["color"] = "red" };

        var a = VariantMatrix.BuildSignature(first);
        var b = VariantMatrix.BuildSignature(second);

        a.ShouldBe("color=red|size=m");
        b.ShouldBe(a);
    }

    [Fact]
    public void BuildSignature_大小写与空白必须规范化()
    {
        var messy = new Dictionary<string, string> { [" Color "] = " Red ", ["SIZE"] = "M" };
        var clean = new Dictionary<string, string> { ["color"] = "red", ["size"] = "m" };

        VariantMatrix.BuildSignature(messy).ShouldBe(VariantMatrix.BuildSignature(clean));
    }

    [Fact]
    public void BuildSignature_三轴按key字典序拼接()
    {
        var axes = new Dictionary<string, string> { ["size"] = "m", ["material"] = "cotton", ["color"] = "red" };

        VariantMatrix.BuildSignature(axes).ShouldBe("color=red|material=cotton|size=m");
    }

    [Fact]
    public void BuildSignature_空轴集合返回空串表达单规格商品()
    {
        // 单规格商品（无颜色无尺码）必须有且只有一个变体。空签名 + (ProductId, AxisSignature) 唯一索引
        // 天然保证这一点；若这里改成抛异常，单规格商品就只能靠伪造成一个 spec=default 轴建出来，
        // 而那个假轴会出现在前台规格表里。
        VariantMatrix.BuildSignature(new Dictionary<string, string>()).ShouldBe(string.Empty);
        VariantMatrix.BuildSignature(null).ShouldBe(string.Empty);
    }

    [Fact]
    public void BuildSignature_空白轴值必须拒绝()
    {
        var axes = new Dictionary<string, string> { ["color"] = "  " };

        Should.Throw<DomainException>(() => VariantMatrix.BuildSignature(axes))
            .Code.ShouldBe("INV_VARIANT_AXIS_VALUE_REQUIRED");
    }

    [Fact]
    public void 聚合必须拒绝重复的轴签名()
    {
        var variants = new[]
        {
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=m", Prices(1999)),
            new ProductVariant(Guid.NewGuid(), "TS-RED-M-2", "color=red|size=m", Prices(2099)),
        };

        var ex = Should.Throw<InvariantViolationException>(
            () => Product.Create(Guid.NewGuid(), "t-shirt", "T-Shirt", variants));

        ex.Code.ShouldBe("INV_PRODUCT_VARIANT_SIGNATURE_DUPLICATE");
    }

    [Fact]
    public void 同一商品的不同轴组合可以共存()
    {
        var variants = new[]
        {
            new ProductVariant(Guid.NewGuid(), "TS-RED-M", "color=red|size=m", Prices(1999)),
            new ProductVariant(Guid.NewGuid(), "TS-RED-L", "color=red|size=l", Prices(1999)),
        };

        var product = Product.Create(Guid.NewGuid(), "t-shirt", "T-Shirt", variants);

        product.Variants.Count.ShouldBe(2);
    }

    internal static Dictionary<string, long> Prices(long amountMinor, string currency = "USD")
        => new(StringComparer.Ordinal) { [currency] = amountMinor };
}
