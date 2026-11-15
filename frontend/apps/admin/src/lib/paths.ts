// SvelteKit 3 已从 $app/paths 的公共 API 移除 `base` 导出（仅内部 client 模块保留）。
// 由于 vite.config.ts 中 `paths.base` 固定为 '/admin'，这里用常量统一承载前缀，
// 避免在多个页面里硬编码；如需调整部署路径，改 config 与本常量即可。
export const base = '/admin'
