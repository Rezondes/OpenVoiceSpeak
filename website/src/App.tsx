import { useEffect, useRef, useState, type ReactNode } from 'react'
import { icons, type IconName } from './icons'
import { pickLanguage, storageKey, texts, type Lang } from './i18n'

const repo = 'https://github.com/Rezondes/OpenVoiceSpeak'
export const downloadUrl = `${repo}/releases/latest/download/OVS.Client.exe`
const serverCommands = `mkdir openvoicespeak && cd openvoicespeak
curl -fsSLO https://raw.githubusercontent.com/Rezondes/OpenVoiceSpeak/main/docker-compose.yml
docker compose up -d`

function Icon({ name }: { name: IconName }) {
  return (
    <svg className="icon" viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" focusable="false">
      <path d={icons[name]} fill="currentColor" />
    </svg>
  )
}

/** Storage can be blocked (private mode, strict settings): then the choice just is not remembered. */
function readStored(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

function store(lang: Lang) {
  try {
    localStorage.setItem(storageKey, lang)
  } catch {
    // not remembered, still switched
  }
}

/** Pages (settings, administration, dialogs) are rendered taller than the main window (A125). */
const tallShots = new Set(['settings-audio', 'settings-look', 'channel-dialog', 'admin-groups', 'admin-users', 'admin-bans', 'links', 'admin-logs', 'admin-server'])

/** A real app screenshot in the reader's language, dark or light like their system. */
function Shot({ file, lang, alt, eager = false }: { file: string; lang: Lang; alt: string; eager?: boolean }) {
  const path = `${import.meta.env.BASE_URL}screenshots/${file}-${lang}`
  return (
    <picture key={`${file}-${lang}`}>
      <source srcSet={`${path}-dark.webp`} media="(prefers-color-scheme: dark)" />
      <img src={`${path}-light.webp`} alt={alt} width="1100" height={tallShots.has(file) ? 760 : 700}
           loading={eager ? 'eager' : 'lazy'} decoding="async" />
    </picture>
  )
}

/** Whether a media query holds, following changes (system theme, reduced motion). */
function useMedia(query: string) {
  const read = () => typeof matchMedia === 'function' && matchMedia(query).matches
  const [on, setOn] = useState(read)
  useEffect(() => {
    if (typeof matchMedia !== 'function') return
    const list = matchMedia(query)
    const change = () => setOn(list.matches)
    list.addEventListener?.('change', change)
    return () => list.removeEventListener?.('change', change)
  }, [query])
  return on
}

/**
 * Package 118 (A127): a short clip of the animated display, muted and looped, playing only while in view. With reduced
 * motion only its first frame shows.
 */
function Clip({ id, lang, caption }: { id: string; lang: Lang; caption: string }) {
  const still = useMedia('(prefers-reduced-motion: reduce)')
  const dark = useMedia('(prefers-color-scheme: dark)')
  const base = `${import.meta.env.BASE_URL}clips/${id}-${lang}-${dark ? 'dark' : 'light'}`
  const video = useRef<HTMLVideoElement>(null)
  useEffect(() => {
    const v = video.current
    if (!v || typeof IntersectionObserver === 'undefined') return
    const io = new IntersectionObserver(([entry]) => {
      if (entry?.isIntersecting) v.play?.()?.catch(() => { /* no autoplay allowed: the poster stays */ })
      else v.pause?.()
    }, { threshold: 0.25 })
    io.observe(v)
    return () => io.disconnect()
  }, [base, still])
  if (still) return <img src={`${base}-poster.webp`} alt={caption} width="1100" height="700" loading="lazy" decoding="async" />
  return (
    <video key={base} ref={video} muted loop playsInline preload="metadata" poster={`${base}-poster.webp`} aria-label={caption} width="1100" height="700">
      <source src={`${base}.webm`} type="video/webm" />
      <source src={`${base}.mp4`} type="video/mp4" />
    </video>
  )
}

type Zoom = { file: string; alt: string }

/** A screenshot that opens larger on click or Enter. */
function ZoomShot({ file, alt, lang, label, onOpen, eager }: Zoom & { lang: Lang; label: string; onOpen: (z: Zoom) => void; eager?: boolean }) {
  return (
    <button type="button" className="zoom" aria-label={`${label}: ${alt}`} onClick={() => onOpen({ file, alt })}>
      <Shot file={file} lang={lang} alt={alt} eager={eager} />
    </button>
  )
}

/** Native dialog: focus trap, Esc and inert background come for free. A click outside the image closes it. */
function Lightbox({ zoom, lang, close, onClose }: { zoom: Zoom; lang: Lang; close: string; onClose: () => void }) {
  return (
    <dialog className="lightbox" aria-label={zoom.alt} ref={el => { if (el && !el.open) el.showModal?.() }}
            onClose={onClose} onClick={e => { if (e.target === e.currentTarget || (e.target as HTMLElement).tagName === 'IMG') onClose() }}>
      <button type="button" className="lightbox-close" aria-label={close} onClick={onClose}>&times;</button>
      <Shot file={zoom.file} lang={lang} alt={zoom.alt} eager />
    </dialog>
  )
}

/** Fades elements in once when they scroll into view. Used as a ref, so elements React remounts (language switch) are picked up again. */
const revealer = typeof IntersectionObserver === 'undefined' ? null : new IntersectionObserver(entries => {
  for (const e of entries) if (e.isIntersecting) { e.target.classList.add('in'); revealer?.unobserve(e.target) }
}, { rootMargin: '0px 0px -8% 0px', threshold: 0.1 })
if (revealer) document.documentElement.classList.add('reveal-on')
const reveal = { 'data-reveal': '', ref: (el: HTMLElement | null) => {
  if (!el || !revealer) return
  el.style.setProperty('--i', String((el.parentElement ? [...el.parentElement.children].indexOf(el) : 0) % 6))
  revealer.observe(el)
} }

/** In-page links glide to their target. Done in script because browsers drop CSS smooth scrolling when the system has animations off. */
function useGlide() {
  useEffect(() => {
    let frame = 0
    const onClick = (e: MouseEvent) => {
      const link = (e.target as HTMLElement).closest<HTMLAnchorElement>('a[href^="#"]')
      const target = link && document.getElementById(link.hash.slice(1))
      if (!link || !target || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey) return
      e.preventDefault()
      const from = scrollY
      const to = Math.max(0, from + target.getBoundingClientRect().top - parseFloat(getComputedStyle(target).scrollMarginTop || '0'))
      const start = performance.now()
      const duration = Math.min(900, 350 + Math.abs(to - from) / 4)
      cancelAnimationFrame(frame)
      const step = (now: number) => {
        const k = Math.min(1, (now - start) / duration)
        scrollTo({ top: from + (to - from) * (1 - (1 - k) ** 3), behavior: 'instant' })
        if (k < 1) frame = requestAnimationFrame(step)
      }
      frame = requestAnimationFrame(step)
      history.replaceState(null, '', link.hash)
      target.focus({ preventScroll: true })
    }
    const stop = () => cancelAnimationFrame(frame)
    addEventListener('click', onClick)
    addEventListener('wheel', stop, { passive: true })
    addEventListener('touchstart', stop, { passive: true })
    return () => { removeEventListener('click', onClick); removeEventListener('wheel', stop); removeEventListener('touchstart', stop); stop() }
  }, [])
}

/** Highlights the nav link of the section in view, and marks the header once the page is scrolled. */
function useScrollState(ids: readonly string[]) {
  const [active, setActive] = useState<string | null>(null)
  useEffect(() => {
    const root = document.documentElement
    let queued = 0
    const paint = () => {
      queued = 0
      root.classList.toggle('scrolled', scrollY > 8)
      root.style.setProperty('--sy', String(Math.round(scrollY)))
      root.style.setProperty('--p', String(Math.min(1, scrollY / Math.max(1, root.scrollHeight - innerHeight))))
    }
    const onScroll = () => { queued ||= requestAnimationFrame(paint) }
    paint()
    addEventListener('scroll', onScroll, { passive: true })
    addEventListener('resize', onScroll)
    const off = () => { removeEventListener('scroll', onScroll); removeEventListener('resize', onScroll); cancelAnimationFrame(queued) }
    if (typeof IntersectionObserver === 'undefined') return off
    const io = new IntersectionObserver(entries => {
      for (const e of entries) if (e.isIntersecting) setActive(e.target.id)
    }, { rootMargin: '-40% 0px -55% 0px' })
    ids.forEach(id => { const el = document.getElementById(id); if (el) io.observe(el) })
    return () => { io.disconnect(); off() }
  }, [ids])
  return active
}

function Section({ id, title, band = false, children }: { id: string; title: string; band?: boolean; children: ReactNode }) {
  return (
    <section id={id} aria-labelledby={`${id}-title`} className={band ? 'band' : undefined}>
      <div className="container">
        <h2 id={`${id}-title`} {...reveal}>{title}</h2>
        {children}
      </div>
    </section>
  )
}

function DownloadButton({ label, large = false }: { label: string; large?: boolean }) {
  return (
    <a className={large ? 'button button-large' : 'button'} href={downloadUrl}>
      <Icon name="arrowDown" />
      {label}
    </a>
  )
}

// "Für wen" stays a section, the navigation leaves it out: five links fit beside name and buttons in German too
const navIds = ['features', 'screenshots', 'motion', 'install', 'server'] as const

export default function App() {
  const [zoom, setZoom] = useState<Zoom | null>(null)
  const active = useScrollState(navIds)
  useGlide()
  const [lang, setLang] = useState<Lang>(() => pickLanguage(navigator.languages ?? [navigator.language], readStored()))
  const t = texts[lang]
  const version = import.meta.env.VITE_OVS_VERSION || 'dev'

  useEffect(() => {
    document.documentElement.lang = lang
    document.title = t.htmlTitle
  }, [lang, t])

  const choose = (next: Lang) => {
    setLang(next)
    store(next)
  }

  return (
    <>
      <div className="progress" aria-hidden="true" />
      <a className="skip" href="#main">{t.skip}</a>
      <header className="header">
        <div className="container header-row">
          <a className="brand" href="#main">
            <img src={`${import.meta.env.BASE_URL}logo.svg`} alt="" width="32" height="32" />
            <span>OpenVoiceSpeak</span>
          </a>
          <nav aria-label="OpenVoiceSpeak">
            <ul>
              {navIds.map(id => (
                <li key={id}><a href={`#${id}`} aria-current={active === id ? 'true' : undefined}>{t.nav[id]}</a></li>
              ))}
            </ul>
          </nav>
          <div className="header-actions">
            <div className="lang" role="group" aria-label={t.langLabel}>
              <button type="button" lang="de" aria-label="Deutsch" aria-pressed={lang === 'de'} onClick={() => choose('de')}>DE</button>
              <button type="button" lang="en" aria-label="English" aria-pressed={lang === 'en'} onClick={() => choose('en')}>EN</button>
            </div>
            <span className="header-cta"><DownloadButton label={t.download} /></span>
          </div>
        </div>
      </header>

      <main id="main" tabIndex={-1}>
        <div className="hero">
          <div className="container">
            <h1>{t.hero.title}</h1>
            <p className="lead">{t.hero.lead}</p>
            <div className="hero-actions">
              <DownloadButton label={t.hero.cta} large />
              <span className="version">{`${t.hero.version} ${version}`}</span>
              <a className="text-link" href={`${repo}/releases/latest`}>{t.hero.notes}</a>
            </div>
            <p className="hint">{t.hero.note}</p>
            <div className="frame hero-shot">
              <ZoomShot file="main-online" lang={lang} alt={t.hero.shotAlt} label={t.zoom} onOpen={setZoom} eager />
            </div>
          </div>
        </div>

        <Section id="features" title={t.features.title}>
          <ul className="cards">
            {t.features.items.map(item => (
              <li key={item.title} className="card" {...reveal}>
                <span className="card-icon"><Icon name={item.icon} /></span>
                <h3>{item.title}</h3>
                <p>{item.text}</p>
              </li>
            ))}
            {t.features.adminItems.map(item => (
              <li key={item.title} className="card card-admin" {...reveal}>
                <span className="card-icon"><Icon name={item.icon} /></span>
                <span className="tag">{t.features.adminTag}</span>
                <h3>{item.title}</h3>
                <p>{item.text}</p>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="audience" title={t.audience.title} band>
          <ul className="cards cards-three">
            {t.audience.items.map(item => (
              <li key={item.title} className="card" {...reveal}>
                <span className="card-icon"><Icon name={item.icon} /></span>
                <h3>{item.title}</h3>
                <p>{item.text}</p>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="screenshots" title={t.screenshots.title}>
          <ul className="gallery">
            {t.screenshots.items.map(item => (
              <li key={item.file} {...reveal}>
                <figure>
                  <div className="frame"><ZoomShot file={item.file} lang={lang} alt={item.alt} label={t.zoom} onOpen={setZoom} /></div>
                  <figcaption>{item.caption}</figcaption>
                </figure>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="motion" title={t.motion.title} band>
          <p className="section-lead" {...reveal}>{t.motion.lead}</p>
          <ul className="clips">
            {t.motion.items.map(item => (
              <li key={item.id} {...reveal}>
                <figure>
                  <div className="frame"><Clip id={item.id} lang={lang} caption={item.caption} /></div>
                  <figcaption>{item.caption}</figcaption>
                </figure>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="install" title={t.install.title}>
          <ol className="steps">
            {t.install.steps.map(step => (
              <li key={step.title} className="card" {...reveal}>
                <h3>{step.title}</h3>
                <p>{step.text}</p>
              </li>
            ))}
          </ol>
          <p className="callout">{t.install.smartscreen}</p>
          <div className="center"><DownloadButton label={t.hero.cta} large /></div>
        </Section>

        <Section id="server" title={t.server.title} band>
          <div className="server" {...reveal}>
            <div>
              <p>{t.server.text}</p>
              <p><a className="text-link" href={`${repo}#readme`}>{t.server.readme}</a></p>
            </div>
            <pre tabIndex={0} aria-label={t.server.code}><code>{serverCommands}</code></pre>
          </div>
        </Section>
      </main>

      <footer className="footer">
        <div className="container footer-row">
          <p>{t.footer.made}</p>
          <ul>
            <li><a href={repo}>{t.footer.source}</a></li>
            <li><a href={`${repo}/releases`}>{t.footer.releases}</a></li>
          </ul>
        </div>
      </footer>
      {zoom && <Lightbox zoom={zoom} lang={lang} close={t.close} onClose={() => setZoom(null)} />}
    </>
  )
}
