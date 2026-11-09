import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// 开发时把 /api 代理到后端，前端代码里只写 /api/...，不出现跨域和硬编码端口
export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5052',
        changeOrigin: true
      }
    }
  }
})
