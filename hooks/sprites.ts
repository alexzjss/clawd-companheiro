// Motor de sprites do Clawd: pixel art montada em camadas (corpo, olhos, braços, pernas,
// acessórios). As mesmas animações viram SVG no app e quadros PNG no Clawd do Windows
// (exportadas em JSON por export-sprites.mts).

export type Letter = 'B' | 'D' | 'L' | 'E' | 'W' | 'Y' | 'P' | 'C' | 'G' | 'S' | 'K' | 'N' | 'R'
export type Rect = readonly [x: number, y: number, w: number, c: Letter]
export type Frame = readonly Rect[]
export type Anim = { readonly fps: number; readonly frames: readonly Frame[] }

export const WIDTH = 24
export const HEIGHT = 20
const OX = 4
const OY = 6

type Eyes = 'open' | 'closed' | 'happy' | 'sad' | 'wide' | 'up'
type Arms = 'down' | 'up' | 'wave1' | 'wave2' | 'hold' | 'holdLeft'
type Legs = 'stand' | 'walk1' | 'walk2' | 'tuck' | 'none'
type Pose = { eyes?: Eyes; arms?: Arms; legs?: Legs; dx?: number; dy?: number }

class Canvas {
  readonly px = new Map<number, Letter>()

  set(x: number, y: number, c: Letter) {
    if (x >= 0 && x < WIDTH && y >= 0 && y < HEIGHT) {
      this.px.set(y * WIDTH + x, c)
    }
  }

  grid(x0: number, y0: number, rows: readonly string[]) {
    rows.forEach((row, dy) => {
      ;[...row].forEach((ch, dx) => {
        if (ch !== '.') {
          this.set(x0 + dx, y0 + dy, ch as Letter)
        }
      })
    })
  }

  // Linhas de mesma cor viram um retângulo só: SVG menor e menos quadrados no Windows.
  rects(): Frame {
    const out: Rect[] = []
    for (let y = 0; y < HEIGHT; y++) {
      let x = 0
      while (x < WIDTH) {
        const c = this.px.get(y * WIDTH + x)
        if (c === undefined) {
          x++
          continue
        }
        let w = 1
        while (x + w < WIDTH && this.px.get(y * WIDTH + x + w) === c) {
          w++
        }
        out.push([x, y, w, c])
        x += w
      }
    }

    return out
  }
}

const ARMS: Record<Arms, readonly (readonly [number, number])[]> = {
  down: [[0, 4], [1, 4], [0, 5], [1, 5], [13, 4], [14, 4], [13, 5], [14, 5]],
  up: [[0, 1], [1, 1], [0, 2], [1, 2], [13, 1], [14, 1], [13, 2], [14, 2]],
  wave1: [[0, 4], [1, 4], [0, 5], [1, 5], [13, 1], [14, 1], [13, 2], [14, 2]],
  wave2: [[0, 4], [1, 4], [0, 5], [1, 5], [13, 3], [14, 3], [14, 2], [15, 2]],
  hold: [[0, 4], [1, 4], [0, 5], [1, 5], [13, 4], [14, 4], [15, 4], [13, 5], [14, 5]],
  holdLeft: [[-1, 4], [0, 4], [1, 4], [0, 5], [1, 5], [13, 4], [14, 4], [13, 5], [14, 5]],
}

const LEGS: Record<Exclude<Legs, 'none'>, { xs: readonly number[]; rows: readonly number[] }> = {
  stand: { xs: [3, 5, 9, 11], rows: [7, 8] },
  walk1: { xs: [2, 5, 8, 11], rows: [7, 8] },
  walk2: { xs: [3, 6, 9, 12], rows: [7, 8] },
  tuck: { xs: [3, 5, 9, 11], rows: [7] },
}

const EYES: Record<Eyes, readonly (readonly [number, number, Letter?])[]> = {
  open: [[4, 2], [4, 3], [10, 2], [10, 3]],
  closed: [[3, 3], [4, 3], [10, 3], [11, 3]],
  happy: [[3, 3], [4, 2], [5, 3], [9, 3], [10, 2], [11, 3]],
  sad: [[4, 3], [10, 3], [3, 2], [11, 2], [4, 4, 'C']],
  wide: [[4, 2], [5, 2], [4, 3], [5, 3], [9, 2], [10, 2], [9, 3], [10, 3]],
  up: [[4, 2], [10, 2]],
}

function body(c: Canvas, pose: Pose) {
  const ox = OX + (pose.dx ?? 0)
  const oy = OY + (pose.dy ?? 0)
  for (let y = 1; y <= 6; y++) {
    for (let x = 2; x <= 12; x++) {
      c.set(ox + x, oy + y, y === 6 ? 'D' : 'B')
    }
  }
  c.set(ox + 3, oy + 1, 'L')
  c.set(ox + 4, oy + 1, 'L')
  for (const [x, y] of ARMS[pose.arms ?? 'down']) {
    c.set(ox + x, oy + y, 'B')
  }
  const legs = pose.legs ?? 'stand'
  if (legs !== 'none') {
    const { xs, rows } = LEGS[legs]
    for (const x of xs) {
      rows.forEach((y, i) => c.set(ox + x, oy + y, i === rows.length - 1 ? 'D' : 'B'))
    }
  }
  for (const [x, y, color] of EYES[pose.eyes ?? 'open']) {
    c.set(ox + x, oy + y, color ?? 'E')
  }
}

function frame(pose: Pose, ...props: ((c: Canvas) => void)[]): Frame {
  const c = new Canvas()
  body(c, pose)
  props.forEach(draw => draw(c))

  return c.rects()
}

// ---------------------------------------------------------------- acessórios --

const terminal = (spark: boolean) => (c: Canvas) => {
  c.grid(18, 12, ['NNNNNN', 'NCNNNN', 'NNNNNN'])
  if (spark) {
    c.grid(20, 10, ['Y.Y', '.Y.'])
  }
}
const hammerUp = (c: Canvas) => c.grid(18, 3, ['SSSSS', 'SSSSS', '..K..', '..K..', '..K..', '..K..', '..K..'])
const hammerDown = (c: Canvas) => c.grid(19, 8, ['...SS', '...SS', 'KKKSS', '...SS'])
const lens = (x: number) => (c: Canvas) => c.grid(x, 7, ['SSS..', 'SCS..', 'SSS..', '...K.', '....K'])
const paper = (written: number) => (c: Canvas) => {
  c.grid(17, 12, ['WWWWWWW', 'WWWWWWW', 'WWWWWWW'])
  for (let i = 0; i < written; i++) {
    c.set(18 + i, 13, 'N')
  }
}
const pencil = (dx: number) => (c: Canvas) => c.grid(18 + dx, 7, ['R...', '.Y..', '..Y.', '...N'])
const GLOBE = [
  ['.CCC.', 'CCGCC', 'CGGCC', 'CCCGC', '.CCC.'],
  ['.CCC.', 'CCCGC', 'CCGGC', 'CGCCC', '.CCC.'],
]
const globe = (i: number) => (c: Canvas) => c.grid(10, 0, GLOBE[i] ?? [])
const sparkle = (x: number, y: number) => (c: Canvas) => c.grid(x - 1, y - 1, ['.Y.', 'YYY', '.Y.'])
const clipboard = (checked: boolean) => (c: Canvas) => {
  c.grid(0, 8, ['.KK.', 'KWWK', 'KNWK', 'KWWK', 'KNWK', 'KKKK'])
  if (checked) {
    c.grid(1, 9, ['.Y', 'Y.'])
  }
}
const wrench = (up: boolean) => (c: Canvas) => {
  if (up) c.grid(19, 5, ['S.S', 'SSS', '.S.', '.S.', '.S.'])
  else c.grid(19, 9, ['..S.S', 'SSSSS', '..S.S'])
}
const bubble = (n: number) => (c: Canvas) => {
  if (n >= 1) c.set(17, 6, 'W')
  if (n >= 2) c.grid(18, 3, ['WW', 'WW'])
  if (n >= 3) c.grid(19, 0, ['.WWWW.', 'WWNWWW', '.WWWW.'])
}
const speech = (dots: number) => (c: Canvas) => {
  c.grid(17, 2, ['WWWWWWW', 'WWWWWWW', 'WWWWWWW', 'WW.....'])
  for (let i = 0; i < dots; i++) {
    c.set(18 + i * 2, 3, 'N')
  }
}
const CONFETTI: readonly (readonly [number, number, Letter])[][] = [
  [[1, 1, 'Y'], [6, 0, 'P'], [17, 1, 'C'], [22, 3, 'R'], [20, 7, 'Y'], [2, 6, 'C']],
  [[3, 0, 'C'], [8, 2, 'Y'], [15, 0, 'R'], [21, 1, 'P'], [23, 6, 'C'], [0, 4, 'P']],
]
const confetti = (i: number) => (c: Canvas) => (CONFETTI[i] ?? []).forEach(([x, y, l]) => c.set(x, y, l))
const rain = (i: number) => (c: Canvas) => {
  c.grid(7, 0, ['.GGGG.GG.', 'GGGGGGGGG', '.GGGGGGG.'])
  const drops = i === 0 ? [8, 11, 14] : [9, 12, 15]
  drops.forEach(x => c.set(x, i === 0 ? 4 : 5, 'C'))
}
const zzz = (i: number) => (c: Canvas) => {
  c.grid(17, 7, ['WW', '.W', 'WW'])
  if (i === 1) c.grid(20, 2, ['WWW', '.W.', 'WWW'])
}
const heart = (c: Canvas) => c.grid(20, 1, ['P.P', 'PPP', '.P.'])

// ---------------------------------------------------------------- animações --

export const ANIMS = {
  andar: { fps: 6, frames: [frame({ legs: 'walk1' }), frame({ legs: 'walk2' })] },
  andarFeliz: { fps: 7, frames: [frame({ legs: 'walk1', eyes: 'happy' }), frame({ legs: 'walk2', eyes: 'happy', dy: -1 })] },
  andarTriste: { fps: 3, frames: [frame({ legs: 'walk1', eyes: 'sad' }), frame({ legs: 'walk2', eyes: 'sad' })] },
  parado: {
    fps: 3,
    frames: [frame({}), frame({}), frame({}), frame({}), frame({ eyes: 'closed' }), frame({})],
  },
  feliz: { fps: 3, frames: [frame({ eyes: 'happy' }), frame({ eyes: 'happy', legs: 'tuck', dy: -2 }), frame({ eyes: 'happy' })] },
  triste: { fps: 2, frames: [frame({ eyes: 'sad' }), frame({ eyes: 'sad', dy: 1 })] },
  sonolento: { fps: 1.5, frames: [frame({ eyes: 'closed' }), frame({ eyes: 'closed', dy: 1 }), frame({ eyes: 'open' })] },
  pensar: {
    fps: 3,
    frames: [frame({ eyes: 'up' }, bubble(1)), frame({ eyes: 'up' }, bubble(2)), frame({ eyes: 'up' }, bubble(3))],
  },
  responder: {
    fps: 4,
    frames: [frame({ arms: 'wave2' }, speech(1)), frame({}, speech(2)), frame({ arms: 'wave2' }, speech(3))],
  },
  bash: {
    fps: 5,
    frames: [frame({ arms: 'hold' }, terminal(false), hammerUp), frame({ arms: 'hold', dy: 1 }, terminal(true), hammerDown)],
  },
  ler: { fps: 3, frames: [frame({ eyes: 'wide' }, lens(7)), frame({ eyes: 'wide' }, lens(12))] },
  editar: {
    fps: 4,
    frames: [
      frame({ arms: 'hold' }, paper(1), pencil(0)),
      frame({ arms: 'hold' }, paper(3), pencil(1)),
      frame({ arms: 'hold' }, paper(5), pencil(2)),
    ],
  },
  web: { fps: 3, frames: [frame({ arms: 'up', eyes: 'up' }, globe(0)), frame({ arms: 'up', eyes: 'up' }, globe(1))] },
  agente: {
    fps: 4,
    frames: [
      frame({ eyes: 'happy' }, sparkle(2, 3), sparkle(21, 6)),
      frame({ eyes: 'happy', dy: -1 }, sparkle(5, 1), sparkle(19, 2), sparkle(22, 12)),
    ],
  },
  planejar: { fps: 2, frames: [frame({ arms: 'holdLeft' }, clipboard(false)), frame({ arms: 'holdLeft', eyes: 'happy' }, clipboard(true))] },
  ferramenta: { fps: 4, frames: [frame({ arms: 'hold' }, wrench(true)), frame({ arms: 'hold', dy: 1 }, wrench(false))] },
  feito: {
    fps: 4,
    frames: [frame({ arms: 'up', eyes: 'happy', legs: 'tuck', dy: -2 }, confetti(0)), frame({ arms: 'up', eyes: 'happy' }, confetti(1))],
  },
  erro: { fps: 3, frames: [frame({ eyes: 'sad' }, rain(0)), frame({ eyes: 'sad' }, rain(1))] },
  dormir: { fps: 1.5, frames: [frame({ eyes: 'closed', legs: 'none', dy: 2 }, zzz(0)), frame({ eyes: 'closed', legs: 'none', dy: 2 }, zzz(1))] },
  oi: { fps: 4, frames: [frame({ arms: 'wave1', eyes: 'happy' }, heart), frame({ arms: 'wave2', eyes: 'happy' })] },
  arrastado: { fps: 6, frames: [frame({ arms: 'up', eyes: 'wide', legs: 'walk1' }), frame({ arms: 'up', eyes: 'wide', legs: 'walk2' })] },
} satisfies Record<string, Anim>

export type AnimName = keyof typeof ANIMS

// ---------------------------------------------------------------- paletas --

export type Palette = Record<Letter, string>

const COMMON = {
  E: '#1b1820', W: '#ffffff', Y: '#fcb421', P: '#ff6fae', C: '#5cc8ff',
  G: '#9aa3ad', S: '#c9ced6', K: '#8a5a2b', N: '#2a2a33', R: '#e5484d',
} as const

export const PALETTES = {
  laranja: { ...COMMON, B: '#d97757', D: '#b85c3e', L: '#eea183' },
  usp: { ...COMMON, B: '#1094ab', D: '#0b6f80', L: '#64c4d2', Y: '#fcb421' },
  pessoal: { ...COMMON, B: '#8b30e6', D: '#6a1fb8', L: '#b77cf2', P: '#e04cf0' },
} satisfies Record<string, Palette>

// ---------------------------------------------------------------- ferramenta -> animação --

export function animForTool(tool: string | undefined): AnimName {
  if (tool === undefined) return 'pensar'
  const t = tool.toLowerCase()
  if (/^(bash|powershell|bashoutput|killshell|monitor)$/.test(t)) return 'bash'
  if (/^(read|grep|glob|ls|notebookread|skill|toolsearch)$/.test(t)) return 'ler'
  if (/^(edit|write|multiedit|notebookedit)$/.test(t)) return 'editar'
  if (/^(websearch|webfetch)$/.test(t) || /browser|navigate|chrome|fetch/.test(t)) return 'web'
  if (/^(agent|task|workflow)$/.test(t)) return 'agente'
  if (/todowrite|update_plan|wrap_up|^task(create|update|list|get)$/.test(t)) return 'planejar'

  return 'ferramenta'
}

// ---------------------------------------------------------------- SVG --

// Um <g> por quadro, cada um visível só na sua fatia do ciclo (CSS step-end).
export function animSvg(
  name: AnimName,
  palette: Palette,
  opts: { id?: string; flip?: boolean; speed?: number } = {},
): string {
  const anim: Anim = ANIMS[name]
  const id = opts.id ?? 'c'
  const n = anim.frames.length
  const dur = n / (anim.fps * (opts.speed ?? 1))
  const slice = 100 / n
  const groups = anim.frames
    .map((f, i) => {
      const rects = f.map(([x, y, w, c]) => `<rect x="${x}" y="${y}" width="${w}" height="1" fill="${palette[c]}"/>`).join('')

      return `<g class="${id}f" style="animation-delay:${((i * dur) / n).toFixed(3)}s">${rects}</g>`
    })
    .join('')
  const css =
    n === 1
      ? ''
      : `.${id}f{visibility:hidden;animation:${id}k ${dur.toFixed(3)}s step-end infinite}` +
        `@keyframes ${id}k{0%{visibility:visible}${slice.toFixed(3)}%{visibility:hidden}100%{visibility:hidden}}`
  const flip = opts.flip ? ` transform="translate(${WIDTH},0) scale(-1,1)"` : ''

  return `<style>${css}</style><g${flip}>${groups}</g>`
}

export function clawdSvg(name: AnimName, palette: Palette, opts: { id?: string } = {}): string {
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${WIDTH} ${HEIGHT}" ` +
    `shape-rendering="crispEdges">${animSvg(name, palette, opts)}</svg>`
  )
}
