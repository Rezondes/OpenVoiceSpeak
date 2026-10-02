import type { IconName } from './icons'

export type Lang = 'de' | 'en'
export const storageKey = 'ovs-lang'

type Card = { icon: IconName; title: string; text: string }

const de = {
  htmlTitle: 'OpenVoiceSpeak: Voice-Chat für deine Leute',
  skip: 'Zum Inhalt springen',
  nav: { features: 'Vorteile', audience: 'Für wen', screenshots: 'Screenshots', motion: 'In Bewegung (Beta)', install: 'Installation', server: 'Eigener Server' },
  langLabel: 'Sprache',
  zoom: 'Screenshot vergrößern',
  close: 'Schließen',
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
    adminTag: 'Für Serverbetreiber',
    items: [
      { icon: 'headphones', title: 'Klarer Sound', text: 'Opus-Codec, Push-to-Talk oder Sprachaktivierung und Tasten, die auch mitten im Spiel wirken.' },
      { icon: 'home', title: 'Dein eigener Server', text: 'Der Server läuft per Docker bei dir, eure Gespräche laufen über keinen fremden Dienst.' },
      { icon: 'key', title: 'Keine Accounts, verschlüsselt', text: 'Deine Identität ist ein Schlüssel auf deinem PC, die Sprache ist per AES-GCM verschlüsselt und die Steuerung läuft über TLS.' },
      { icon: 'chat', title: 'Chat im Channel und privat', text: 'Schreib im Channel, serverweit oder privat, mit Hinweistönen, die du anpassen kannst.' },
      { icon: 'link', title: 'Channels verbinden', text: 'Verlinkte Channels hören sich per Link-Taste gegenseitig, ideal für Raids und große Events.' },
      { icon: 'speaker', title: 'Lautstärke je Person', text: 'Stell jede Stimme einzeln lauter oder leiser, bis 200 Prozent.' },
      { icon: 'plugConnected', title: 'Schnell verbunden', text: 'Lesezeichen in der Seitenleiste, gespeicherte Passwörter und Updates, die sich selbst melden.' },
      { icon: 'eye', title: 'Sieht aus, wie du willst', text: 'Hell oder dunkel, durchscheinend, animiert (Beta) oder schlicht, auf Deutsch oder Englisch.' },
    ] as Card[],
    adminItems: [
      { icon: 'shieldPerson', title: 'Gruppen und Rechte', text: 'Gruppen wie Gast, Moderator und Admin mit fein einstellbaren Rechten für jede Aufgabe.' },
      { icon: 'settings', title: 'Channels nach Maß', text: 'Nutzerlimits, Sperren per Gruppe oder Passwort, Trenner und stumme Channels.' },
      { icon: 'prohibited', title: 'Moderation im Griff', text: 'Kicken, bannen und stummschalten, dazu alle Nutzer mit Statistiken und alle Bans mit ihrem Verlauf.' },
      { icon: 'document', title: 'Logs und Backups in der App', text: 'Server- und Channel-Logs lesen und durchsuchen, Backups anlegen und wiederherstellen.' },
    ] as Card[],
  },
  audience: {
    title: 'Für wen ist OpenVoiceSpeak?',
    items: [
      { icon: 'headphones', title: 'Gaming-Gruppen und Gilden', text: 'Channels für Raid, Strategie und AFK auf einem Server, der nur euch gehört.' },
      { icon: 'people', title: 'Freunde und Communities', text: 'Ein Server für eure Runde, auf dem ihr selbst bestimmt, wer rein darf.' },
      { icon: 'person', title: 'Kleine Teams und Vereine', text: 'Schnell reden, ohne Termin und ohne Konto bei einem großen Anbieter.' },
    ] as Card[],
  },
  screenshots: {
    title: 'So sieht es aus',
    items: [
      { file: 'main-private', caption: 'Privat schreiben, direkt neben dem Channel-Chat', alt: 'Privater Chat mit Mara im Hauptfenster' },
      { file: 'start', caption: 'Lesezeichen und Verbinden mit einem Klick', alt: 'Startseite mit Lesezeichen und dem Dialog zum Verbinden' },
      { file: 'settings-audio', caption: 'Sprachaktivierung mit Pegelanzeige und Selbsttest', alt: 'Einstellungen mit Lautstärke, Sprachaktivierung und Pegel' },
      { file: 'settings-look', caption: 'Design, Sprache und Darstellung nach deinem Geschmack', alt: 'Einstellungen zur Darstellung mit Design und Sprache' },
      { file: 'user-volume', caption: 'Jede Stimme einzeln lauter oder leiser', alt: 'Menü einer Person mit Lautstärke 140 Prozent' },
      { file: 'channel-dialog', caption: 'Channels mit Limit, Gruppensperre und Passwort', alt: 'Dialog eines Channels mit Nutzerlimit, Gruppen und Passwort' },
      { file: 'admin-groups', caption: 'Gruppen mit fein einstellbaren Rechten', alt: 'Verwaltung der Gruppen mit ihren Rechten' },
      { file: 'admin-users', caption: 'Alle Nutzer mit Statistiken und Gruppen', alt: 'Nutzerübersicht mit Statistiken und Gruppen' },
      { file: 'admin-bans', caption: 'Bans mit Grund, Dauer und Verlauf', alt: 'Übersicht der Bans mit Details' },
      { file: 'links', caption: 'Mehrere Channels mit wenigen Klicks verbinden', alt: 'Verwaltung mit der Übersicht der Channel-Links' },
      { file: 'admin-logs', caption: 'Logs lesen und durchsuchen, direkt in der App', alt: 'Log-Ansicht mit Suchtreffern' },
      { file: 'admin-server', caption: 'Neustart, Limits und Backups für den Server', alt: 'Servereinstellungen mit Neustart und Backups' },
    ],
  },
  motion: {
    title: 'In Bewegung (Beta)',
    lead: 'In der animierten Darstellung (Beta, noch in Arbeit) gleitet alles an seinen Platz. Wer es ruhiger mag, stellt in den Einstellungen die vereinfachte Darstellung ein.',
    items: [
      { id: 'clip-switch', caption: 'Beim Channelwechsel gleitet alles an seinen Platz' },
      { id: 'clip-connect', caption: 'Beim Verbinden baut sich der Server Stück für Stück auf' },
      { id: 'clip-pages', caption: 'Seiten und Tabs mit weichen Übergängen' },
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
    text: 'Du brauchst einen Server, auf dem Docker läuft. Das Betriebssystem ist egal. Drei Befehle, dann läuft dein Server. Gib Port 7000 für TCP und UDP frei.',
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
  nav: { features: 'Features', audience: 'Who it is for', screenshots: 'Screenshots', motion: 'In motion (Beta)', install: 'Install', server: 'Own server' },
  langLabel: 'Language',
  zoom: 'Enlarge screenshot',
  close: 'Close',
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
    adminTag: 'For server admins',
    items: [
      { icon: 'headphones', title: 'Clear sound', text: 'Opus codec, push-to-talk or voice activation and keys that work right in the middle of a game.' },
      { icon: 'home', title: 'Your own server', text: 'The server runs in Docker on your side, so your conversations never pass through someone else\'s service.' },
      { icon: 'key', title: 'No accounts, encrypted', text: 'Your identity is a key on your PC, voice is encrypted with AES-GCM and control runs over TLS.' },
      { icon: 'chat', title: 'Chat in the channel and private', text: 'Write in the channel, server-wide or privately, with notification sounds you can tune.' },
      { icon: 'link', title: 'Link channels', text: 'Linked channels hear each other with the link key, made for raids and big events.' },
      { icon: 'speaker', title: 'Volume per person', text: 'Make every voice louder or quieter on its own, up to 200 percent.' },
      { icon: 'plugConnected', title: 'Connected in a click', text: 'Bookmarks in the sidebar, saved passwords and updates that announce themselves.' },
      { icon: 'eye', title: 'Looks your way', text: 'Light or dark, see-through, animated (beta) or simple, in German or English.' },
    ],
    adminItems: [
      { icon: 'shieldPerson', title: 'Groups and rights', text: 'Groups like guest, moderator and admin with fine-grained rights for every task.' },
      { icon: 'settings', title: 'Channels your way', text: 'User limits, locks by group or password, separators and muted channels.' },
      { icon: 'prohibited', title: 'Moderation at hand', text: 'Kick, ban and mute, plus every user with statistics and every ban with its history.' },
      { icon: 'document', title: 'Logs and backups in the app', text: 'Read and search server and channel logs, create and restore backups.' },
    ],
  },
  audience: {
    title: 'Who is OpenVoiceSpeak for?',
    items: [
      { icon: 'headphones', title: 'Gaming groups and guilds', text: 'Channels for raid, strategy and AFK on a server that is yours alone.' },
      { icon: 'people', title: 'Friends and communities', text: 'One server for your crew where you decide who gets in.' },
      { icon: 'person', title: 'Small teams and clubs', text: 'Talk right away, without a meeting invite and without an account at a big provider.' },
    ],
  },
  screenshots: {
    title: 'How it looks',
    items: [
      { file: 'main-private', caption: 'Chat privately, right next to the channel chat', alt: 'Private chat with Mara in the main window' },
      { file: 'start', caption: 'Bookmarks and connecting in one click', alt: 'Start screen with bookmarks and the connect dialog' },
      { file: 'settings-audio', caption: 'Voice activation with level meter and self test', alt: 'Settings with volume, voice activation and level' },
      { file: 'settings-look', caption: 'Theme, language and display to your taste', alt: 'Appearance settings with theme and language' },
      { file: 'user-volume', caption: 'Every voice louder or quieter on its own', alt: 'A person\'s menu with the volume at 140 percent' },
      { file: 'channel-dialog', caption: 'Channels with limit, group lock and password', alt: 'Channel dialog with user limit, groups and password' },
      { file: 'admin-groups', caption: 'Groups with fine-grained rights', alt: 'Administration of the groups and their rights' },
      { file: 'admin-users', caption: 'Every user with statistics and groups', alt: 'User overview with statistics and groups' },
      { file: 'admin-bans', caption: 'Bans with reason, duration and history', alt: 'Ban overview with details' },
      { file: 'links', caption: 'Link several channels in a few clicks', alt: 'Administration with the channel link overview' },
      { file: 'admin-logs', caption: 'Read and search logs right in the app', alt: 'Log viewer with search hits' },
      { file: 'admin-server', caption: 'Restart, limits and backups for the server', alt: 'Server settings with restart and backups' },
    ],
  },
  motion: {
    title: 'In motion (Beta)',
    lead: 'In the animated display (beta, still in progress) everything glides into place. If you like it calmer, switch to the simplified display in the settings.',
    items: [
      { id: 'clip-switch', caption: 'Everything glides into place when you switch channels' },
      { id: 'clip-connect', caption: 'The server view builds up piece by piece as you connect' },
      { id: 'clip-pages', caption: 'Pages and tabs with smooth transitions' },
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
    text: 'You need a server that runs Docker. The operating system does not matter. Three commands and your server is running. Open port 7000 for TCP and UDP.',
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
