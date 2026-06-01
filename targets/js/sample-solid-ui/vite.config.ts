import { defineConfig } from 'vite'
import solid from 'vite-plugin-solid'
import viteTsConfigPath from 'vite-tsconfig-paths'

export default defineConfig({
  plugins: [viteTsConfigPath(), solid()],
})
