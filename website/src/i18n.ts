import type { IconName } from './icons'

export type Lang = 'de' | 'en'
export const storageKey = 'ovs-lang'

type Card = { icon: IconName; title: string; text: string }

const de = {
  htmlTitle: 'OpenVoiceSpeak: Voice-Chat für deine Leute',
  skip: 'Zum Inhalt springen',
  nav: { features: 'Vorteile', audience: 'Für wen', screenshots: 'Screenshots', install: 'Installation', server: 'Eigener Server' },
  langLabel: 'Sprache',
  download: 'Herunterladen',
  hero: {
    title: 'Voice-Chat, der dir gehört.',
    lead: 'OpenVoiceSpeak ist ein schlanker Voice-Chat für deine Gruppe: klarer Sound, eigener Server, keine Accounts. Starten, verbinden, reden.',
    cta: 'Für Windows herunterladen',
    version: 'Version',
    note: 'Windows 10 und 11, eine einzige Datei, keine Installation.',
    notes: 'Was ist neu?',
    shotAlt: 'OpenVoiceSpeak mit Channels, Teilnehmern und Chat',
  },
  features: {
    title: 'Was OpenVoiceSpeak ausmacht',
    items: [
      { icon: 'headphones', title: 'Klarer Sound', text: 'Sprache mit dem Opus-Codec, Push-to-Talk oder Sprachaktivierung und Tasten, die auch im Spiel wirken.' },
      { icon: 'home', title: 'Dein eigener Server', text: 'Der Server läuft per Docker auf deinem Linux-Server. Eure Gespräche laufen über keinen fremden Dienst.' },
      { icon: 'key', title: 'Keine Accounts', text: 'Deine Identität ist ein Schlüssel auf deinem PC. Keine Registrierung, keine E-Mail, kein Passwort zum Vergessen.' },
      { icon: 'link', title: 'Channels verbinden', text: 'Verlinkte Channels hören sich per Link-Taste gegenseitig. Ideal für Raids, Turniere und große Events.' },
      { icon: 'lockClosed', title: 'Verschlüsselt', text: 'Sprache per AES-GCM verschlüsselt, Steuerung über TLS. Der Server speichert keinen Chatverlauf.' },
      { icon: 'chat', title: 'Chat und Sounds', text: 'Text-Chat im Channel, serverweit und privat. Hinweistöne kannst du anpassen oder ausschalten.' },
    ] as Card[],
  },
  audience: {
    title: 'Für wen ist OpenVoiceSpeak?',
    items: [
      { icon: 'headphones', title: 'Gaming-Gruppen und Gilden', text: 'Channels für Raid, Strategie und AFK, Rechte über Gruppen wie Gast, Moderator und Admin.' },
      { icon: 'people', title: 'Freunde und Communities', text: 'Ein Server für eure Runde, auf dem ihr selbst bestimmt, wer rein darf.' },
      { icon: 'person', title: 'Kleine Teams und Vereine', text: 'Schnell reden, ohne Termin und ohne Konto bei einem großen Anbieter.' },
    ] as Card[],
  },
  screenshots: {
    title: 'So sieht es aus',
    items: [
      { file: 'main-private', caption: 'Privat schreiben, direkt neben dem Channel-Chat', alt: 'Privater Chat mit einer anderen Person im Hauptfenster' },
      { file: 'links', caption: 'Mehrere Channels mit wenigen Klicks verbinden', alt: 'Verwaltung mit der Übersicht der Channel-Links' },
      { file: 'settings', caption: 'Geräte, Tasten, Sprache und Sounds einstellen', alt: 'Einstellungen für Audio und Tasten' },
    ],
  },
  install: {
    title: 'In drei Schritten startklar',
    steps: [
      { title: 'Herunterladen', text: 'Lade OVS.Client.exe herunter und lege sie in einen Ordner deiner Wahl.' },
      { title: 'Starten', text: 'Beim ersten Start zeigt Windows eventuell "Der Computer wurde durch Windows geschützt". Klicke auf "Weitere Informationen" und dann auf "Trotzdem ausführen".' },
      { title: 'Verbinden', text: 'Klicke auf "Verbinden ...", gib die Adresse deines Servers und deinen Namen ein. Fertig. Neue Versionen meldet die App selbst.' },
    ],
    smartscreen: 'Warum die Warnung? Die App hat kein kostenpflichtiges Signaturzertifikat, deshalb kennt Windows sie noch nicht. Zu jedem Release gehört eine SHA-256-Prüfsumme.',
  },
  server: {
    title: 'Eigenen Server betreiben',
    text: 'Du brauchst einen Linux-Server mit Docker. Drei Befehle, dann läuft dein Server. Gib Port 7000 für TCP und UDP frei.',
    readme: 'Anleitung für Serverbetreiber',
    code: 'Befehle für den Server',
  },
  footer: {
    source: 'Quellcode auf GitHub',
    releases: 'Alle Versionen',
    made: 'OpenVoiceSpeak, Voice-Chat für deine Leute.',
  },
}

export type Texts = typeof de

const en: Texts = {
  htmlTitle: 'OpenVoiceSpeak: voice chat for your people',
  skip: 'Skip to content',
  nav: { features: 'Features', audience: 'Who it is for', screenshots: 'Screenshots', install: 'Install', server: 'Own server' },
  langLabel: 'Language',
  download: 'Download',
  hero: {
    title: 'Voice chat that belongs to you.',
    lead: 'OpenVoiceSpeak is a lean voice chat for your group: clear sound, your own server, no accounts. Start, connect, talk.',
    cta: 'Download for Windows',
    version: 'Version',
    note: 'Windows 10 and 11, a single file, no installation.',
    notes: 'What is new?',
    shotAlt: 'OpenVoiceSpeak with channels, people and chat',
  },
  features: {
    title: 'What makes OpenVoiceSpeak',
    items: [
      { icon: 'headphones', title: 'Clear sound', text: 'Voice with the Opus codec, push-to-talk or voice activation and keys that work while you play.' },
      { icon: 'home', title: 'Your own server', text: 'The server runs in Docker on your Linux server. Your conversations never pass through someone else\'s service.' },
      { icon: 'key', title: 'No accounts', text: 'Your identity is a key on your PC. No sign-up, no email, no password to forget.' },
      { icon: 'link', title: 'Link channels', text: 'Linked channels hear each other with the link key. Made for raids, tournaments and big events.' },
      { icon: 'lockClosed', title: 'Encrypted', text: 'Voice encrypted with AES-GCM, control over TLS. The server keeps no chat history.' },
      { icon: 'chat', title: 'Chat and sounds', text: 'Text chat in the channel, server-wide and private. Notification sounds can be changed or turned off.' },
    ],
  },
  audience: {
    title: 'Who is OpenVoiceSpeak for?',
    items: [
      { icon: 'headphones', title: 'Gaming groups and guilds', text: 'Channels for raid, strategy and AFK, rights through groups like guest, moderator and admin.' },
      { icon: 'people', title: 'Friends and communities', text: 'One server for your crew where you decide who gets in.' },
      { icon: 'person', title: 'Small teams and clubs', text: 'Talk right away, without a meeting invite and without an account at a big provider.' },
    ],
  },
  screenshots: {
    title: 'How it looks',
    items: [
      { file: 'main-private', caption: 'Chat privately, right next to the channel chat', alt: 'Private chat with another person in the main window' },
      { file: 'links', caption: 'Link several channels in a few clicks', alt: 'Administration with the channel link overview' },
      { file: 'settings', caption: 'Set up devices, keys, language and sounds', alt: 'Settings for audio and keys' },
    ],
  },
  install: {
    title: 'Ready in three steps',
    steps: [
      { title: 'Download', text: 'Download OVS.Client.exe and put it in a folder of your choice.' },
      { title: 'Start', text: 'On the first start Windows may show "Windows protected your PC". Click "More info" and then "Run anyway".' },
      { title: 'Connect', text: 'Click "Connect ...", enter your server\'s address and your name. Done. The app tells you about new versions itself.' },
    ],
    smartscreen: 'Why the warning? The app has no paid code signing certificate, so Windows does not know it yet. Every release comes with a SHA-256 checksum.',
  },
  server: {
    title: 'Run your own server',
    text: 'You need a Linux server with Docker. Three commands and your server is running. Open port 7000 for TCP and UDP.',
    readme: 'Guide for server operators',
    code: 'Commands for the server',
  },
  footer: {
    source: 'Source code on GitHub',
    releases: 'All versions',
    made: 'OpenVoiceSpeak, voice chat for your people.',
  },
}

export const texts: Record<Lang, Texts> = { de, en }

/** A stored choice wins, otherwise the browser's first language: German for "de", English for anything else. */
export function pickLanguage(browser: readonly string[], stored: string | null): Lang {
  if (stored === 'de' || stored === 'en') return stored
  return browser[0]?.toLowerCase().startsWith('de') ? 'de' : 'en'
}
