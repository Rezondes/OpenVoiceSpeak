import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App, { downloadUrl } from './App'
import { storageKey } from './i18n'

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
    expect(sections.map(s => s.id)).toEqual(['features', 'audience', 'screenshots', 'install', 'server'])
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
    expect(screenshots.length).toBe(4) // hero plus three in the gallery
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
})
