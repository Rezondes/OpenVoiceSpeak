import { describe, expect, it } from 'vitest'
import { pickLanguage, texts } from './i18n'

/** Every key path, with array positions, so both languages must have the same texts in the same places. */
function shape(value: unknown, path = ''): string[] {
  if (Array.isArray(value)) return [`${path}[${value.length}]`, ...value.flatMap((v, i) => shape(v, `${path}[${i}]`))]
  if (value && typeof value === 'object') return Object.entries(value).flatMap(([k, v]) => shape(v, `${path}.${k}`))
  return [path]
}

function leaves(value: unknown): unknown[] {
  if (value && typeof value === 'object') return Object.values(value).flatMap(leaves)
  return [value]
}

describe('i18n', () => {
  it('same keys in German and English', () => {
    expect(shape(texts.en)).toEqual(shape(texts.de))
    for (const lang of ['de', 'en'] as const)
      for (const text of leaves(texts[lang])) {
        expect(typeof text).toBe('string')
        expect((text as string).trim()).not.toBe('')
        expect(text).not.toContain('\u2014') // no em dashes
      }
    expect(texts.en.hero.title).not.toBe(texts.de.hero.title)
  })

  it('browser language picks German or English', () => {
    expect(pickLanguage(['de-DE', 'en'], null)).toBe('de')
    expect(pickLanguage(['de'], null)).toBe('de')
    expect(pickLanguage(['en-US', 'de'], null)).toBe('en')
    expect(pickLanguage(['fr-FR'], null)).toBe('en')
    expect(pickLanguage([], null)).toBe('en')
    expect(pickLanguage(['de-DE'], 'en')).toBe('en') // the stored choice wins
    expect(pickLanguage(['en-US'], 'de')).toBe('de')
    expect(pickLanguage(['de-AT'], 'xx')).toBe('de') // junk in storage is ignored
  })
})
