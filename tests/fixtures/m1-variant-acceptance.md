# M1 完成判据：六变体固定样本

> **本文件与同目录的 `m1-variant-acceptance.json` 是 M1 完成判据的唯一机器样本出处。**
> **改 JSON = 改判据**，必须同步 `docs/06-测试验收.md` 的 **TC-E2-05 / TC-E2-06** 和调用它的 API 测试。
> 不要在测试里另写一份"等价"的商品/变体数组——两份样本一定会漂移，而且漂移时两边都绿。

## 1. 字段形状（给测试直接读）

JSON 顶层就是 `CreateProductRequest`，变体就是 `CreateProductVariantRequest`，金额就是 `AdminMoneyDto`：

| JSON | C# 类型 | 说明 |
|---|---|---|
| `slug` / `title` | `CreateProductRequest.Slug` / `.Title` | `slug` 合法：`^[a-z0-9]+(-[a-z0-9]+)*$`、小写 |
| `variants[]` | `.Variants` | 必须 6 条，颜色 × 尺码 = 2 × 3 |
| `variants[].sku` | `CreateProductVariantRequest.Sku` | 可读且唯一：`ACC-{BLK\|WHT}-{S\|M\|L}` |
| `variants[].axes` | `.Axes` | `{"color": …, "size": …}`，键/值都遵循 `VariantMatrix.Normalize` |
| `variants[].price` | `.Price` | `{ "currency": "CNY", "amountMinor": <正整数> }`，禁止 `amount` 或十进制金额 |

测试侧直接 `JsonSerializer.Deserialize<CreateProductRequest>(File.ReadAllText(path))`；
若不是 ASP.NET 的 web 默认 options，请显式给 `PropertyNameCaseInsensitive = true`（这样 PascalCase 也能读）。
**不要把字段重新拼一遍到测试里**——那是第二份样本。

### 轴签名口径（不改 JSON，只说明它为什么合法）

`VariantMatrix.BuildSignature` 对轴 key 用 `StringComparer.Ordinal` 排序，key 与 value 各自
`trim().ToLowerInvariant()`，再校验 `^[a-z0-9][a-z0-9_-]*$`，最后拼成 `key=value|key=value`。
本样本 6 条签名互不重复，例如 `ACC-BLK-S` → `color=black|size=s`。
**签名不要在 JSON 里另存一列**：它是 `Axes` 的派生量，存了就出现两个真源。

### 判据读数

- 变体数：**6**（`jq '.variants | length'`）
- SKU 唯一：**6 个不同值**
- 币种：全部 **CNY**；价格：**6 个互不相同的正整数 minor**
- 该币种下的最低正价：**1999**（`ACC-BLK-S`）— **断言时从文件算 `min(.variants[].price.amountMinor)`，别把 1999 硬编码成第二份样本**

对应 `06` 的解除条件（`GET /api/v1/{locale}/products/{slug}` 的 `AmountMinor` 等于最低正价）：
同一条断言块里必须有「从管理端建这 6 变体 → 调前台接口 → 断言等于上面的 min」三件事。

## 2. 确定性建库命令（任意 cwd，指定临时路径）

下面整条可粘贴执行；`DB` 换成任意临时路径即可。`cd "$REPO"` 是为了让 `global.json` 锁到
SDK 8.0.418，`dotnet ef` 的 `-p/-s` 都用绝对路径，不依赖调用者的当前目录。
**它不碰 `src/OpenStore.Api/openstore.dev.db`**：schema 来自模型生成的 SQL，不是从开发库拷贝。

```bash
DB=/tmp/m1-acceptance.db                    # ← 换成你自己的临时路径
REPO=/Users/zhaoma/vs/robotsmall
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
cd "$REPO" \
  && dotnet ef migrations script -p "$REPO/src/OpenStore.Infrastructure" \
       -s "$REPO/src/OpenStore.Infrastructure" -o "$DB.sql" \
  && rm -f "$DB" && sqlite3 "$DB" < "$DB.sql"
```

`rm -f "$DB"` 放在应用之前：同一路径重跑得到同一份 schema；指定新路径得到同样的 schema。
**不要改成 `dotnet ef database update` 去连开发库** —— 那会把临时验收和开发状态绑在一起。

实测输出（2026-11-17，`DB=/tmp/m1-acceptance.db`）：

```
Build started...
Build succeeded.
rc=0
__EFMigrationsHistory = 20261117110220_M1CatalogInventoryAuth|8.0.11
```

### schema 核对读数

```bash
sqlite3 "$DB" "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT IN ('__EFMigrationsHistory','sqlite_sequence');"          # 24
sqlite3 "$DB" "SELECT sql FROM sqlite_master WHERE type='table';" | grep -o 'CONSTRAINT "CK_[A-Za-z_]*"' | sort -u | wc -l               # 9
sqlite3 "$DB" "SELECT count(*) FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_autoindex_%' ESCAPE '\';"                 # 56
```

| 项 | 实测 | 口径 |
|---|---|---|
| 业务表 | **24** | 单一基线从空库建出的**全部业务表**；另有 `__EFMigrationsHistory` 与 `sqlite_sequence` 两个 SQLite 表项 |
| CHECK 唯一名 | **9** | 每条在最终库里恰好出现 1 次；与迁移里 9 条逐名一致 |
| 非自增索引 | **56** | 排除 `sqlite_autoindex_*` |

**"14 表"是增量不是总数**：`dfe35fc` 那一笔新增 14 张（属性/媒体/审计等），原有 10 张 +
14 = 24。若某处写"表数 14"而读者照上面的 `SELECT count(*)` 得到 24，不是库建错，是口径没写。
squash 之后重建表消失，所以脚本文本里的 `CONSTRAINT "CK_` 计数与库里唯一名数相等（差额 0）；
`02 §6.1` 的"文本计数恒 ≥ 唯一名数、差额 = 重建表次数"仍然成立，只是当前差额为 0。

## 3. 更新纪律

1. 改 `m1-variant-acceptance.json` → 同步 `docs/06-测试验收.md` 的 TC-E2-05 / TC-E2-06 与 API 测试，并在同一笔提交里改完。
2. 本 `.md` 只写**口径与复现命令**，不复述样本数据；判据里的价格由测试从 JSON 计算，不在这里硬编码第二份。
3. **不要改迁移**（`src/OpenStore.Infrastructure/Persistence/Migrations/*`）。迁移是共享文件，改它走 backend-engineer；建库命令永远从当前基线的模型生成 SQL，因此迁移变了命令不用改。
