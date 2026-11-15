import { sveltekit } from '@sveltejs/kit/vite'
import adapter from '@sveltejs/adapter-static'
import { defineConfig } from 'vite'

// SvelteKit 3：配置直接传给 sveltekit() 插件（svelte.config.js 已废弃）。
// 纯 SPA：adapter-static + fallback + 全局 ssr=false（前端设计 §1.2）。
export default defineConfig({
  plugins: [
    sveltekit({
      adapter: adapter({
        pages: 'build',
        assets: 'build',
        fallback: 'index.html',
      }),
      // SvelteKit 3 移除了内置 $lib 别名；按官方迁移提示显式保留 $lib（设计文档沿用 $lib）。
      alias: {
        '$lib': 'src/lib',
      },
      paths: {
        base: '/admin',
      },
      prerender: {
        entries: [],
      },
    }),
  ],
  // 前端设计 §1.3：两个必须项，否则 .svelte 会被 esbuild 预打包而报错。
  optimizeDeps: {
    exclude: ['@openstore/ui', '@openstore/i18n', '@openstore/sdk'],
  },
  ssr: {
    noExternal: ['@openstore/ui', '@openstore/i18n', '@openstore/sdk'],
  },
})
