import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Served from https://rezondes.github.io/OpenVoiceSpeak/
export default defineConfig({
  base: '/OpenVoiceSpeak/',
  plugins: [react()],
  test: { environment: 'jsdom' },
})
