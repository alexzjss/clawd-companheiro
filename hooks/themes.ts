import type { ThemeColors } from 'claude-code'

import { makeColors } from './make-colors.js'

// As cores de um tema como a Central dos Clawds grava em ~/.claude/clawd/config.json.
export type ContaTema = {
  ativo?: boolean
  principal?: string
  fundo?: string
  aviso?: string
  sucesso?: string
  erro?: string
  pro?: string
}

const HEX = /^#[0-9a-f]{6}$/i

function hueSat(hex: string): [number, number] {
  const r = parseInt(hex.slice(1, 3), 16) / 255
  const g = parseInt(hex.slice(3, 5), 16) / 255
  const b = parseInt(hex.slice(5, 7), 16) / 255
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const d = max - min
  const l = (max + min) / 2
  if (d < 1e-9) return [0, 0]
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
  const h = max === r ? ((g - b) / d + (g < b ? 6 : 0)) * 60 : max === g ? ((b - r) / d + 2) * 60 : ((r - g) / d + 4) * 60

  return [h, s]
}

function hsl(h: number, s: number, l: number): string {
  const sat = Math.max(0, Math.min(1, s))
  const c = (1 - Math.abs(2 * l - 1)) * sat
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const m = l - c / 2
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x]
  const to = (v: number) => Math.round((v + m) * 255).toString(16).padStart(2, '0')

  return `#${to(r)}${to(g)}${to(b)}`
}

// Fundos e textos tingidos com o matiz de uma cor. A Central faz a mesma conta
// (desktop/ClawdConfig.cs, Tint.Surfaces) para a prévia dela bater com o app.
export function surfaces(tint: string, dark: boolean) {
  const [h, s] = hueSat(tint)
  const k = Math.min(1, s / 0.6)
  if (dark) {
    return {
      'surface-0': hsl(h, 0.56 * k, 0.09),
      'surface-1': hsl(h, 0.5 * k, 0.12),
      'surface-2': hsl(h, 0.46 * k, 0.15),
      'surface-3': hsl(h, 0.44 * k, 0.17),
      'text-primary': hsl(h, 0.45 * k, 0.93),
      'text-secondary': hsl(h, 0.3 * k, 0.78),
      'text-muted': hsl(h, 0.22 * k, 0.64),
    }
  }

  return {
    'surface-0': hsl(h, 0.4 * k, 0.93),
    'surface-1': hsl(h, 0.42 * k, 0.955),
    'surface-2': hsl(h, 0.45 * k, 0.98),
    'text-primary': hsl(h, 0.6 * k, 0.12),
    'text-secondary': hsl(h, 0.33 * k, 0.26),
    'text-muted': hsl(h, 0.25 * k, 0.36),
  }
}

// O tema inteiro a partir das cores escolhidas na Central. Sem cor que sirva, nada.
export function customTheme(label: string, t: ContaTema): ThemeColors | undefined {
  const pick = (v: string | undefined, fallback: string) => (v !== undefined && HEX.test(v) ? v : fallback)
  const accent = pick(t.principal, '#d97757')
  const tint = pick(t.fundo, accent)
  try {
    return makeColors({
      label,
      roles: {
        accent,
        danger: pick(t.erro, '#c23b32'),
        success: pick(t.sucesso, '#2f8a5b'),
        warning: pick(t.aviso, '#c9801a'),
        pro: pick(t.pro, '#3f6db3'),
      },
      light: { gray: { anchors: surfaces(tint, false) } },
      dark: { gray: { anchors: surfaces(tint, true) } },
    }).theme
  } catch {
    return undefined
  }
}
