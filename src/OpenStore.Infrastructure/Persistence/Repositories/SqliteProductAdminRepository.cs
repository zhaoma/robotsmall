using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Catalog.Commands;
using OpenStore.Domain.Catalog;
using OpenStore.Domain.Shared;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IProductAdminRepository"/> 的 SQLite 实现。
/// </summary>
/// <remarks>
/// <b>唯一允许调用 <c>SaveChangesAsync</c> 的位置之一</b>（另一个是 UnitOfWork，M1）。
/// Api 层被架构测试禁止出现这个字面量，正是为了逼出"写操作必须经过用例 + 仓储"的结构。
/// </remarks>
public sealed class SqliteProductAdminRepository(StoreDbContext db) : IProductAdminRepository
{
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var record = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        return record is null ? null : ProductMapping.ToDomain(record);
    }

    public async Task AddAsync(Product product, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(product);

        var record = new ProductRecord { Id = product.Id };
        ProductMapping.ApplyScalars(product, record);
        record.Variants = product.Variants.Select(v => ProductMapping.ToRecord(v, product.Id)).ToList();

        db.Products.Add(record);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // 唯一索引（Slug / SKU）冲突：竞态下预检没拦住，这里给可读错误码而不是 500
            throw new DomainException(
                "PRODUCT_PERSISTENCE_CONFLICT",
                "A product with this slug or variant SKU already exists.",
                ex);
        }
    }

    public async Task SaveAsync(Product product, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(product);

        var record = await db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == product.Id, ct)
            .ConfigureAwait(false)
            ?? throw new DomainException(
                "PRODUCT_NOT_FOUND",
                $"Product '{product.Id}' disappeared between read and write (concurrency or deletion).");

        ProductMapping.ApplyScalars(product, record);

        // M0 编辑只改既有变体（SKU/价格），不新增/删除变体；出现陌生变体按新增处理（防御性）。
        foreach (var variant in product.Variants)
        {
            var variantRecord = record.Variants.FirstOrDefault(v => v.Id == variant.Id);
            if (variantRecord is null)
            {
                db.ProductVariants.Add(ProductMapping.ToRecord(variant, product.Id));
            }
            else
            {
                ProductMapping.ApplyVariant(variant, variantRecord);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        // 这只是"给可读错误码"的预检，**不是唯一性保证**——保证由唯一索引 IX_Product_Slug 负责。
        // 预检与写入之间永远有窗口，并发下两个请求都会通过预检；唯一索引才是最终裁决者。
        // 不过滤软删：软删商品继续占用 slug（数据模型 D4），与索引语义保持一致。
        return await db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Slug == slug, ct)
            .ConfigureAwait(false);
    }
}
