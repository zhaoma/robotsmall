/**
 * 金额纯函数（对齐 Domain/Shared/CurrencyMinorUnits.cs）：
 * 本期只区分"零小数位"（JPY/KRW）与"两位小数"两类，其余一律按 2 位。
 */

export function currencyMinorUnits(currency: string): number {
  return currency === 'JPY' || currency === 'KRW' ? 0 : 2
}

/** minor units → 主单位数值（仅展示用，禁止用于再计算）。 */
export function minorToMajor(minor: number, currency: string): number {
  return minor / 10 ** currencyMinorUnits(currency)
}

/** 主单位数值 → minor units（AwayFromZero，对齐 Money.FromMajorUnits）。 */
export function majorToMinor(major: number, currency: string): number {
  const factor = 10 ** currencyMinorUnits(currency)
  const sign = major < 0 ? -1 : 1
  const abs = Math.abs(major) * factor
  return sign * Math.round(abs + Number.EPSILON * factor)
}

export interface FormatMoneyOptions {
  locale?: string
}

/** minor units → 带货币符号的本地化字符串。locale 必须显式传入（不得用 navigator.language，防水合漂移）。 */
export function formatMoney(minor: number, currency: string, opts: FormatMoneyOptions = {}): string {
  const locale = opts.locale ?? 'zh-Hans'
  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      minimumFractionDigits: currencyMinorUnits(currency),
      maximumFractionDigits: currencyMinorUnits(currency),
    }).format(minorToMajor(minor, currency))
  } catch {
    // 币种未在 Intl 支持内（罕见）：退化为 `主单位 + 币种代码`
    return `${minorToMajor(minor, currency).toFixed(currencyMinorUnits(currency))} ${currency}`
  }
}
