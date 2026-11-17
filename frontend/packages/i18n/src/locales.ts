import en from '../locales/en.json'
import zh from '../locales/zh.json'
import { flatten } from './t.js'
import type { Dict } from './t.js'

export type { MessageKey } from './keys.generated.js'

/** 源语言字典（en 为唯一真源，见前端设计 §7.2）。 */
export const enDict: Dict = flatten(en)

/** 简体中文字典（本期 admin 主用）。 */
export const zhDict: Dict = flatten(zh)
