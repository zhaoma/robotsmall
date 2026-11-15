import { createT, enDict, zhDict } from '@openstore/i18n'
import type { MessageKey, Vars } from '@openstore/i18n'

// 本期 admin 主用 zh-Hans；en 作为 fallback（缺键回退英文，而非显示 key 本身）。
const { t: baseT } = createT(zhDict, enDict, undefined, import.meta.env.DEV)

/** 收窄签名：内置键有补全，自定义键仍可传任意字符串（前端设计 §7.3）。 */
export const t = baseT as (key: MessageKey | (string & {}), vars?: Vars) => string

export type T = typeof t
