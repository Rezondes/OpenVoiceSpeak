import { useEffect, useState, type ReactNode } from 'react'
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

/** A real app screenshot in the reader's language, dark or light like their system. */
function Shot({ file, lang, alt, eager = false }: { file: string; lang: Lang; alt: string; eager?: boolean }) {
  const path = `${import.meta.env.BASE_URL}screenshots/${file}-${lang}`
  return (
    <picture>
      <source srcSet={`${path}-dark.webp`} media="(prefers-color-scheme: dark)" />
      <img src={`${path}-light.webp`} alt={alt} width="1100" height={file.startsWith('main') ? 700 : 760}
           loading={eager ? 'eager' : 'lazy'} decoding="async" />
    </picture>
  )
}

function Section({ id, title, band = false, children }: { id: string; title: string; band?: boolean; children: ReactNode }) {
  return (
    <section id={id} aria-labelledby={`${id}-title`} className={band ? 'band' : undefined}>
      <div className="container">
        <h2 id={`${id}-title`}>{title}</h2>
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

export default function App() {
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
      <a className="skip" href="#main">{t.skip}</a>
      <header className="header">
        <div className="container header-row">
          <a className="brand" href="#main">
            <img src={`${import.meta.env.BASE_URL}logo.svg`} alt="" width="32" height="32" />
            <span>OpenVoiceSpeak</span>
          </a>
          <nav aria-label="OpenVoiceSpeak">
            <ul>
              {(['features', 'audience', 'screenshots', 'install', 'server'] as const).map(id => (
                <li key={id}><a href={`#${id}`}>{t.nav[id]}</a></li>
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
              <Shot file="main-online" lang={lang} alt={t.hero.shotAlt} eager />
            </div>
          </div>
        </div>

        <Section id="features" title={t.features.title}>
          <ul className="cards">
            {t.features.items.map(item => (
              <li key={item.title} className="card">
                <span className="card-icon"><Icon name={item.icon} /></span>
                <h3>{item.title}</h3>
                <p>{item.text}</p>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="audience" title={t.audience.title} band>
          <ul className="cards cards-three">
            {t.audience.items.map(item => (
              <li key={item.title} className="card">
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
              <li key={item.file}>
                <figure>
                  <div className="frame"><Shot file={item.file} lang={lang} alt={item.alt} /></div>
                  <figcaption>{item.caption}</figcaption>
                </figure>
              </li>
            ))}
          </ul>
        </Section>

        <Section id="install" title={t.install.title} band>
          <ol className="steps">
            {t.install.steps.map(step => (
              <li key={step.title} className="card">
                <h3>{step.title}</h3>
                <p>{step.text}</p>
              </li>
            ))}
          </ol>
          <p className="callout">{t.install.smartscreen}</p>
          <div className="center"><DownloadButton label={t.hero.cta} large /></div>
        </Section>

        <Section id="server" title={t.server.title}>
          <div className="server">
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
    </>
  )
}
