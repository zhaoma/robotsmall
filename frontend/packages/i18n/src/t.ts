export type Dict = Record<string, string>
export type Vars = Record<string, string | number>

/**
 * 多语种 t() 构造器。
 * 解析顺序严格为：DB 覆盖 → 当前语种 → 默认语种 → key 本身（架构 §5.2.1）。
 * 返回 key 而不是空串，是刻意的：空串会让"漏译"静默通过，key 在页面上极其刺眼。
 */
export function createT(dict?: Dict, fallback?: Dict, override?: Dict, dev = false) {
  const miss = new Set<string>()
  const t = (key: string, vars?: Vars): string => {
    const raw = override?.[key] ?? dict?.[key] ?? fallback?.[key]
    if (raw === undefined) {
      if (dev) {
        miss.add(key)
        console.warn('[i18n] missing', key)
      }
      return key
    }
    if (!vars) return raw
    return raw.replace(/\{(\w+)\}/g, (m, k) => (k in vars ? String(vars[k]) : m))
  }
  return { t, misses: () => [...miss] }
}

/** 把嵌套 JSON 展平为点分键（`{a:{b:'x'}}` → `{'a.b':'x'}`），供 createT 使用。 */
export function flatten(obj: Record<string, unknown>, prefix = ''): Dict {
  const out: Dict = {}
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k
    if (typeof v === 'object' && v !== null && !Array.isArray(v)) {
      Object.assign(out, flatten(v as Record<string, unknown>, key))
    } else {
      out[key] = String(v)
    }
  }
  return out
}
