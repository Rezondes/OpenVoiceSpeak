/**
 * Package 170: the server of "?join=host:port", with the client's rules (JoinLink): a host name of letters, digits and
 * hyphens, an IPv4 or a bracketed IPv6 address, and a port from 1 to 65535; null for anything else.
 */
export function readJoin(search: string): string | null {
  const match = /^(\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9.-]{1,253}):(\d{1,5})$/.exec(new URLSearchParams(search).get('join') ?? '')
  if (!match) return null
  const [, host, portText] = match
  const port = Number(portText)
  if (port < 1 || port > 65535) return null
  if (host.startsWith('[') ? !host.includes(':') : host.split('.').some(label => !/^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/.test(label))) return null
  return `${host}:${port}`
}

/** Package 170: hands the link to the browser, which starts OVS when it is installed; a seam for the tests. */
export const protocol = { open: (url: string) => { location.href = url } }
