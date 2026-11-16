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
      // $lib 已迁到 package.json 的 #lib subpath imports（SvelteKit 3 推荐方式）。
      paths: {
        base: '/admin',
      },
      prerender: {
        entries: [],
      },
    }),
  ],
  // 开发期把 /api 转给后端：没有这条，dev server 会把 /api/** 当成自己的路由并回 404，
  // 本地登录与所有列表都拿不到数据（只能靠 mock）。生产同源部署不需要它。
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: false,
      },
    },
  },
  // 前端设计 §1.3：两个必须项，否则 .svelte 会被 esbuild 预打包而报错。
  optimizeDeps: {
    exclude: ['@openstore/ui', '@openstore/i18n', '@openstore/sdk'],
  },
  ssr: {
    noExternal: ['@openstore/ui', '@openstore/i18n', '@openstore/sdk'],
  },
})
