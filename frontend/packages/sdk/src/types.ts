/**
 * admin 契约 DTO 类型（本期手工定义，待后端 openapi-typescript 生成后替换）。
 *
 * 字段命名以后端权威契约为准（已与 M0 领域实体逐字段对齐）：
 * - 列表项：{ id, slug, title, status, variantCount }
 * - 详情变体：{ id, sku, axisSignature, price(单对象可 null) }
 * - 新建：{ slug, title, variants:[{ sku, axes?, price(必填) }] }
 * - 编辑：PATCH { title?, variants? }
 */

export type ProductStatus = 'Draft' | 'Active' | 'Archived'

export type AdminRole = 'Admin' | 'Staff'

/** 商品列表行（后端权威契约：{id, slug, title, status, variantCount}）。 */
export interface AdminProductListDto {
  id: string
  slug: string
  title: string
  status: ProductStatus
  variantCount: number
}

/** 单个价格（金额用 minor units + ISO 4217 币种）。 */
export interface AdminPriceDto {
  amountMinor: number
  currency: string
}

export interface AdminProductVariantDto {
  id: string
  sku: string
  axisSignature: string
  /** 单价格对象（后端契约可 null），非数组。 */
  price: AdminPriceDto | null
}

export interface AdminProductDto {
  id: string
  slug: string
  title: string
  status: ProductStatus
  variants: AdminProductVariantDto[]
  // images / categoryIds / attributes / seo —— 后续里程碑补齐，本期编辑页留占位 Tab
}

/** /api/admin/auth/me 返回（后端契约 AdminAuthResponse）。 */
export interface AdminAuthResponse {
  id: string
  email: string
  displayName: string
  role: AdminRole
  permissions: string[]
}

/** 分类树节点（后端契约 AdminCategoryNodeDto；M0 返回空数组）。 */
export interface AdminCategoryNodeDto {
  id: string
  slug: string
  name: string
  parentId?: string
  sortOrder: number
  children: AdminCategoryNodeDto[]
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  pageCount: number
}

/** 新建商品变体（后端契约：price 必填，axes 可选）。 */
export interface CreateProductVariantInput {
  sku: string
  axes?: Record<string, string>
  price: AdminPriceDto
}

export interface CreateProductRequest {
  slug: string
  title: string
  variants: CreateProductVariantInput[]
}

/** 商品状态变更请求（对齐既有 PATCH /api/admin/products/{id}/status）。 */
export interface ChangeProductStatusRequest {
  status: ProductStatus
}

/** 仪表盘聚合（docs/03 §5.4 dashboard/summary；本期后端未就绪，前端用占位 0）。 */
export interface DashboardSummaryDto {
  ordersToday: number
  pendingShipmentCount: number
  pendingRefundCount: number
  awaitingManualRefundCount: number
  lowStockCount: number
  canSell: boolean
  canSellReason?: string
}
