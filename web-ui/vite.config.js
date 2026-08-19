import { readFileSync, writeFileSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const root = dirname(fileURLToPath(import.meta.url))
const versionFile = resolve(root, 'src/version.json')

function readVersion() {
  try {
    return JSON.parse(readFileSync(versionFile, 'utf8'))
  } catch {
    return { version: '1.0.0' }
  }
}

function bumpPatch(version) {
  const parts = String(version || '1.0.0').split('.').map((n) => Number(n) || 0)
  while (parts.length < 3) parts.push(0)
  parts[2] += 1
  return `${parts[0]}.${parts[1]}.${parts[2]}`
}

export default defineConfig(({ command }) => {
  const data = readVersion()
  if (command === 'build') {
    data.version = bumpPatch(data.version)
    data.builtAt = new Date().toISOString()
    writeFileSync(versionFile, `${JSON.stringify(data, null, 2)}\n`)
  }

  return {
    plugins: [react()],
    define: {
      __APP_VERSION__: JSON.stringify(data.version || '1.0.0'),
    },
    server: {
      port: 3000,
      proxy: {
        '/api': {
          target: 'http://localhost:5149',
          changeOrigin: true,
        },
      },
    },
  }
})
