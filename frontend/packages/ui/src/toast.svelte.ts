export type ToastKind = 'success' | 'error' | 'info'

export interface ToastItem {
  id: number
  kind: ToastKind
  message: string
}

let items = $state<ToastItem[]>([])
let nextId = 0

/** 轻提示队列。仅 admin（ssr=false 纯 SPA）使用；storefront 走自己的 SSR-safe 通道。 */
export function toast(kind: ToastKind, message: string) {
  const id = ++nextId
  items = [...items, { id, kind, message }]
  setTimeout(() => {
    items = items.filter((t) => t.id !== id)
  }, 4000)
}

export function dismissToast(id: number) {
  items = items.filter((t) => t.id !== id)
}

export const toastItems = {
  get value() {
    return items
  },
}
