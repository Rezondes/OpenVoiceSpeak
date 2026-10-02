// Package 116 (A125): turns the clip frames rendered by tests/OVS.Tests/Website (frame0000.png ... and poster.webp in
// <folder>/clips/<clip>-<lang>-<theme>/) into public/clips/<clip>-<lang>-<theme>.webm, .mp4 and -poster.webp.
//   npm run clips -- <folder>
import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, readdirSync, statSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import ffmpeg from 'ffmpeg-static'

const fps = 30
const folder = process.argv[2]
if (!folder || !existsSync(join(folder, 'clips'))) {
  console.error('usage: npm run clips -- <folder with clips/ from OVS_SHOTS>')
  process.exit(1)
}
const out = join(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'clips')
mkdirSync(out, { recursive: true })

function run(args) {
  const result = spawnSync(ffmpeg, ['-hide_banner', '-loglevel', 'error', '-y', ...args], { stdio: 'inherit' })
  if (result.status !== 0) throw new Error(`ffmpeg failed: ${args.join(' ')}`)
}

for (const clip of readdirSync(join(folder, 'clips'))) {
  const frames = join(folder, 'clips', clip)
  if (!statSync(frames).isDirectory()) continue
  const input = ['-framerate', String(fps), '-i', join(frames, 'frame%04d.png'), '-t', '6'] // A125: at most 6 s
  // muted, small: the website plays them looped and only while in view
  run([...input, '-an', '-c:v', 'libvpx-vp9', '-b:v', '0', '-crf', '42', '-row-mt', '1', '-pix_fmt', 'yuv420p', join(out, `${clip}.webm`)])
  run([...input, '-an', '-c:v', 'libx264', '-preset', 'slow', '-crf', '30', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', join(out, `${clip}.mp4`)])
  copyFileSync(join(frames, 'poster.webp'), join(out, `${clip}-poster.webp`))
  const kb = name => Math.round(statSync(join(out, name)).size / 1024)
  console.log(`${clip}: webm ${kb(`${clip}.webm`)} KB, mp4 ${kb(`${clip}.mp4`)} KB`)
}
