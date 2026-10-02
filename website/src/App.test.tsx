import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App, { downloadUrl } from './App'
import { storageKey, texts } from './i18n'

beforeEach(() => {
  localStorage.clear()
  vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-US'])
})

afterEach(() => {
  cleanup()
  vi.unstubAllEnvs()
  vi.restoreAllMocks()
})

describe('App', () => {
  it('Hero shows version and direct download link', () => {
    vi.stubEnv('VITE_OVS_VERSION', '280926.0jof')
    render(<App />)
    expect(downloadUrl).toBe('https://github.com/Rezondes/OpenVoiceSpeak/releases/latest/download/OVS.Client.exe')
    const ctas = [...screen.getAllByRole('link', { name: /Download/ })]
    expect(ctas.length).toBe(3) // header, hero, install
    for (const cta of ctas) expect(cta.getAttribute('href')).toBe(downloadUrl)
    expect(ctas[1].textContent).toBe('Download for Windows')
    expect(screen.getByText('Version 280926.0jof')).toBeTruthy()
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Voice chat that belongs to you.')
  })

  it('shows "dev" when built without a release version', () => {
    vi.stubEnv('VITE_OVS_VERSION', '')
    render(<App />)
    expect(screen.getByText('Version dev')).toBeTruthy()
  })

  it('language toggle switches texts and is remembered', () => {
    const { unmount } = render(<App />)
    const german = screen.getByRole('button', { name: 'Deutsch' })
    expect(german.getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(german)
    expect(german.getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Voice-Chat, der dir gehört.')
    expect(screen.getAllByRole('link', { name: /Für Windows herunterladen/ })).toHaveLength(2)
    expect(document.documentElement.lang).toBe('de')
    expect(document.title).toContain('Voice-Chat')
    expect(localStorage.getItem(storageKey)).toBe('de')

    unmount()
    render(<App />) // an English browser, but the choice stays German
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Voice-Chat, der dir gehört.')
    fireEvent.click(screen.getByRole('button', { name: 'English' }))
    expect(localStorage.getItem(storageKey)).toBe('en')
  })

  it('German browser starts in German', () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE', 'en'])
    render(<App />)
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Voice-Chat, der dir gehört.')
  })

  it('every section has a heading, every image an alt text', () => {
    const { container } = render(<App />)
    const sections = [...container.querySelectorAll('section')]
    expect(sections.map(s => s.id)).toEqual(['features', 'audience', 'screenshots', 'motion', 'install', 'server'])
    for (const section of sections) {
      const heading = section.querySelector('h2')
      expect(heading?.textContent?.trim(), section.id).toBeTruthy()
      expect(section.getAttribute('aria-labelledby')).toBe(heading?.id)
    }
    for (const link of container.querySelectorAll('nav a[href^="#"]'))
      expect(container.querySelector(link.getAttribute('href')!), link.getAttribute('href')!).toBeTruthy()
    for (const img of container.querySelectorAll('img')) {
      expect(img.hasAttribute('alt'), img.getAttribute('src')!).toBe(true) // the logo beside the name may be alt=""
      expect(img.getAttribute('width')).toBeTruthy() // no layout jump while loading
    }
    const screenshots = [...container.querySelectorAll('picture img')]
    expect(screenshots.length).toBe(13) // hero plus twelve in the gallery
    for (const img of screenshots) expect(img.getAttribute('alt')?.trim(), img.getAttribute('src')!).toBeTruthy()
    expect(container.querySelector('main')?.id).toBe('main')
    expect(screen.getByRole('link', { name: 'Skip to content' }).getAttribute('href')).toBe('#main')
  })

  it('install explains SmartScreen and the server section links the README', () => {
    render(<App />)
    expect(screen.getByText(/"More info" and then "Run anyway"/)).toBeTruthy()
    expect(screen.getByRole('link', { name: /Guide for server operators/ }).getAttribute('href'))
      .toBe('https://github.com/Rezondes/OpenVoiceSpeak#readme')
    expect(screen.getByText(/docker compose up -d/)).toBeTruthy()
  })

  it('admin features carry their tag', () => {
    render(<App />)
    const tagged = [...document.querySelectorAll('.card')].filter(card => card.querySelector('.tag')?.textContent === 'For server admins')
    expect(tagged.map(card => card.querySelector('h3')?.textContent))
      .toEqual(['Groups and rights', 'Channels your way', 'Moderation at hand', 'Logs and backups in the app'])
    expect(document.querySelectorAll('.card .tag').length).toBe(4) // the player features have none
  })

  it('gallery lists every motif and opens the lightbox', () => {
    render(<App />)
    const gallery = [...document.querySelectorAll('#screenshots picture img')].map(img => img.getAttribute('src'))
    expect(gallery).toEqual(texts.en.screenshots.items.map(item => `${import.meta.env.BASE_URL}screenshots/${item.file}-en-light.webp`))
    fireEvent.click(screen.getByRole('button', { name: /Enlarge screenshot: Log viewer with search hits/ }))
    expect(document.querySelector('dialog img')?.getAttribute('src')).toBe(`${import.meta.env.BASE_URL}screenshots/admin-logs-en-light.webp`)
  })

  it('clips play muted and looped, with reduced motion only on request, and open large', () => {
    const media = (reduced: boolean) => vi.stubGlobal('matchMedia', (query: string) => ({
      matches: reduced && query.includes('reduced-motion'), addEventListener() {}, removeEventListener() {},
    }))
    media(false)
    const { unmount } = render(<App />)
    const videos = [...document.querySelectorAll<HTMLVideoElement>('#motion video')]
    expect(videos).toHaveLength(3)
    for (const video of videos) {
      expect(video.muted).toBe(true)
      expect(video.loop).toBe(true)
      expect(video.autoplay).toBe(false) // it plays once in view
      expect(video.getAttribute('aria-label')).toBeTruthy()
      expect([...video.querySelectorAll('source')].map(s => s.getAttribute('type'))).toEqual(['video/webm', 'video/mp4'])
    }
    expect(videos[0].querySelector('source')?.getAttribute('src')).toBe(`${import.meta.env.BASE_URL}clips/clip-switch-en-light.webm`)
    expect(document.querySelectorAll('#motion .play')).toHaveLength(0) // they move by themselves
    unmount()

    media(true)
    render(<App />)
    const stills = [...document.querySelectorAll<HTMLVideoElement>('#motion video')]
    expect(stills.map(v => v.getAttribute('poster'))).toEqual(texts.en.motion.items.map(c => `${import.meta.env.BASE_URL}clips/${c.id}-en-light-poster.webp`))
    expect(stills.every(v => v.preload === 'none' && !v.autoplay)).toBe(true) // nothing moves or loads by itself
    expect(document.querySelectorAll('#motion .play')).toHaveLength(3) // but each shows it can be played

    fireEvent.click(screen.getByRole('button', { name: /Play video enlarged: Everything glides into place/ }))
    const large = document.querySelector<HTMLVideoElement>('dialog video')!
    expect(large.autoplay && large.controls && large.loop && large.muted).toBe(true) // asked for, so it plays
    expect(large.querySelector('source')?.getAttribute('src')).toBe(`${import.meta.env.BASE_URL}clips/clip-switch-en-light.webm`)
    vi.unstubAllGlobals()
  })

  it('language switch changes image and clip sources', () => {
    render(<App />)
    fireEvent.click(screen.getByRole('button', { name: 'Deutsch' }))
    expect(document.querySelector('#screenshots picture img')?.getAttribute('src')).toBe(`${import.meta.env.BASE_URL}screenshots/main-private-de-light.webp`)
    expect(document.querySelector('#motion source')?.getAttribute('src')).toBe(`${import.meta.env.BASE_URL}clips/clip-switch-de-light.webm`)
    expect(screen.getAllByText('Für Serverbetreiber')).toHaveLength(4)
  })

  it('screenshots open enlarged and close again', () => {
    render(<App />)
    expect(document.querySelector('dialog')).toBeNull()
    fireEvent.click(screen.getAllByRole('button', { name: /Enlarge screenshot/ })[1])
    const dialog = document.querySelector('dialog')!
    expect(dialog.querySelector('img')?.getAttribute('alt')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Close', hidden: true }))
    expect(document.querySelector('dialog')).toBeNull()
  })
})
