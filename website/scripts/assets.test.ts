import { readdirSync, statSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// Package 117 (A125): the images and clips stay small enough for a quick page
const dir = (name: string) => join(dirname(fileURLToPath(import.meta.url)), '..', 'public', name)
type Asset = { name: string; size: number }
const files = (name: string): Asset[] => readdirSync(dir(name)).map((f: string) => ({ name: f, size: statSync(join(dir(name), f)).size }))

describe('website assets', () => {
  it('every image is at most about 150 KB, together under 6 MB', () => {
    const images = files('screenshots')
    for (const image of images) expect(image.size, image.name).toBeLessThanOrEqual(150 * 1024)
    expect(images.reduce((sum, f) => sum + f.size, 0)).toBeLessThan(6 * 1024 * 1024)
  })

  it('every clip is at most about 600 KB and comes as WebM, MP4 and poster', () => {
    const clips = files('clips')
    for (const clip of clips) expect(clip.size, clip.name).toBeLessThanOrEqual(600 * 1024)
    const names = new Set(clips.map(c => c.name))
    for (const webm of clips.filter(c => c.name.endsWith('.webm'))) {
      const base = webm.name.slice(0, -'.webm'.length)
      expect(names.has(`${base}.mp4`), base).toBe(true)
      expect(names.has(`${base}-poster.webp`), base).toBe(true)
    }
  })
})
