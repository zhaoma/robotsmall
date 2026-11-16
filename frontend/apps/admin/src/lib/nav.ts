import { base } from '#lib/paths'
import type { IconName } from '#lib/components/icons'

export interface NavItem {
  href: string
  labelKey: string
  icon: IconName
}

/**
 * 一级 = 业务域（图标轨），二级 = 域内页面（上下文列）。
 * 分组不再靠「空行 + 小标题」制造层次，而是真正折叠：同一时刻只显示当前域的 4 项。
 */
export interface NavDomain {
  key: string
  labelKey: string
  icon: IconName
  /** 无子项时点击图标直接跳这里（如仪表盘） */
  href?: string
  items: NavItem[]
}

export const domains: NavDomain[] = [
  {
    key: 'home',
    labelKey: 'admin.nav.dashboard',
    icon: 'dashboard',
    href: `${base}/`,
    items: [],
  },
  {
    key: 'catalog',
    labelKey: 'admin.nav.catalog',
    icon: 'box',
    items: [
      { href: `${base}/products`, labelKey: 'admin.nav.productList', icon: 'box' },
      { href: `${base}/categories`, labelKey: 'admin.nav.categories', icon: 'table' },
      { href: `${base}/attributes`, labelKey: 'admin.nav.attributes', icon: 'layers' },
      { href: `${base}/inventory`, labelKey: 'admin.nav.inventory', icon: 'panel' },
    ],
  },
  {
    key: 'trade',
    labelKey: 'admin.nav.trade',
    icon: 'bag',
    items: [
      { href: `${base}/orders`, labelKey: 'admin.nav.orders', icon: 'bag' },
      { href: `${base}/returns`, labelKey: 'admin.nav.returns', icon: 'refresh' },
      { href: `${base}/customers`, labelKey: 'admin.nav.customers', icon: 'users' },
    ],
  },
  {
    key: 'content',
    labelKey: 'admin.nav.content',
    icon: 'file',
    items: [
      { href: `${base}/content/themes`, labelKey: 'admin.nav.themes', icon: 'file' },
      { href: `${base}/content/pages`, labelKey: 'admin.nav.pages', icon: 'file' },
    ],
  },
  {
    key: 'i18n',
    labelKey: 'admin.nav.i18n',
    icon: 'globe',
    items: [
      { href: `${base}/i18n/ui-texts`, labelKey: 'admin.nav.uiTexts', icon: 'globe' },
      { href: `${base}/i18n/content`, labelKey: 'admin.nav.contentTranslations', icon: 'globe' },
      { href: `${base}/i18n/coverage`, labelKey: 'admin.nav.coverage', icon: 'trend' },
    ],
  },
  {
    key: 'system',
    labelKey: 'admin.nav.system',
    icon: 'sliders',
    items: [
      { href: `${base}/settings`, labelKey: 'admin.nav.settings', icon: 'sliders' },
      { href: `${base}/settings/users`, labelKey: 'admin.nav.users', icon: 'users' },
      { href: `${base}/settings/audit`, labelKey: 'admin.nav.audit', icon: 'file' },
    ],
  },
]

/** 精确匹配 + 子路径匹配（/admin/products/new 仍高亮「商品列表」），且不把 /products 当成 /products-xxx 的前缀。 */
export function isItemActive(pathname: string, href: string): boolean {
  if (pathname === href) return true
  return pathname.startsWith(href.endsWith('#lib/') ? href : `${href}/`)
}

export function activeDomain(pathname: string): NavDomain | undefined {
  return domains.find((d) => {
    if (d.items.length === 0) return d.href ? isItemActive(pathname, d.href) : false
    return d.items.some((i) => isItemActive(pathname, i.href))
  })
}

/** 面包屑用：当前域 + 当前页。子页面（如 /products/new）取最长的匹配项。 */
export function activeItem(pathname: string): NavItem | undefined {
  const all = domains.flatMap((d) => d.items)
  return all
    .filter((i) => isItemActive(pathname, i.href))
    .sort((a, b) => b.href.length - a.href.length)[0]
}
