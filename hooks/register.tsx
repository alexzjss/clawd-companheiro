import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register, ThemeSetArgs } from 'claude-code'

import type { Account, Activity, ActivityKind, Memory, Mood, SessionInfo } from '../types'
import { PALETTES, animForTool, animSvg, clawdSvg } from './sprites'
import type { AnimName } from './sprites'
import { customTheme } from './themes'
import { PASSING, RELAY_BACKOFF_MS, gistHeaders, relayDue, relayFile, relayFrom, relayKey, relayPayload, staleFiles, withHold } from './relay'
import type { Relay } from './relay'
import type { ContaTema } from './themes'

const PLUGIN = 'clawdesk'
const PANE = 'clawd-sessoes'
const SCENE = 'clawd'

const activity = atom({ plugin: 'clawdesk', key: 'activity' } as const, { kind: 'ocioso', since: 0 } as Activity)
const moodAtom = atom({ plugin: 'clawdesk', key: 'mood' } as const, 'tranquilo' as Mood)
const sessions = atom({ plugin: 'clawdesk', key: 'sessions' } as const, [] as SessionInfo[])

// Quanto tempo cada estado passageiro fica na tela, e quando o Clawd pega no sono.
const SHORT_MS = 9_000
const SLEEP_AFTER_MS = 10 * 60_000
const ACTIVE_WINDOW_MS = 3 * 60_000
const HEARTBEAT_MS = 20_000

// Uma conta como a Central dos Clawds grava em ~/.claude/clawd/config.json: de quais
// e-mails ela é e as cores do tema dela.
export type Conta = { chave: string; nome: string; emails: string; tema?: ContaTema }

// Sem config (a Central ainda não foi aberta), toda sessão é de uma conta só, sem tema.
const CONTAS_PADRAO: readonly Conta[] = [{ chave: 'claude', nome: 'Claude', emails: '*', tema: { ativo: false } }]
const CONFIG_EVERY_TICKS = 2

// O idioma do que o mod escreve: o mesmo do app (o Clawdesk grava "idiomaAtual" na config);
// sem config, o do sistema (LANG); sem nada, português.
export type Lang = 'pt' | 'en'

export function langFrom(config: unknown, env: string | undefined): Lang {
  if (config === 'pt' || config === 'en') return config
  if (env === undefined || env.trim() === '') return 'pt'

  return /^pt/i.test(env.trim()) ? 'pt' : 'en'
}

const STATE_TEXT: Record<Lang, Record<ActivityKind, string>> = {
  pt: {
    oi: 'chegou agora',
    ocioso: 'de bobeira',
    pensando: 'pensando',
    trabalhando: 'trabalhando',
    feito: 'terminou!',
    erro: 'algo deu errado',
    dormindo: 'dormindo',
    aguardando: 'esperando você',
    compactando: 'resumindo a conversa',
    interrompido: 'foi interrompido',
    recusa: 'recusou o pedido',
  },
  en: {
    oi: 'just arrived',
    ocioso: 'hanging out',
    pensando: 'thinking',
    trabalhando: 'working',
    feito: 'done!',
    erro: 'something went wrong',
    dormindo: 'asleep',
    aguardando: 'waiting for you',
    compactando: 'compacting the conversation',
    interrompido: 'was interrupted',
    recusa: 'refused the request',
  },
}

const MOOD_TEXT: Record<Lang, Record<Mood, string>> = {
  pt: { feliz: 'feliz', tranquilo: 'tranquilo', triste: 'triste', sonolento: 'sonolento' },
  en: { feliz: 'happy', tranquilo: 'calm', triste: 'sad', sonolento: 'sleepy' },
}

// Ferramentas que param e esperam a pessoa responder.
const ASKS_PERSON = /^(AskUserQuestion|ExitPlanMode)$/

const CODE = /^(ts|tsx|js|jsx|mjs|cjs|cs|py|java|kt|go|rs|c|h|cpp|hpp|rb|php|swift|lua|gd|r|sql|sh|ps1|psm1|vue|svelte|dart|scala)$/
const DOCS = /^(md|mdx|txt|rst|tex|docx|odt)$/
const DATA = /^(json|jsonl|yaml|yml|toml|csv|tsv|xml|ini|env|lock|xlsx)$/
const IMAGE = /^(png|jpg|jpeg|gif|svg|webp|ico|bmp)$/
const WEB = /^(html|htm|css|scss|less)$/

// O que o Clawd do Windows precisa saber de uma chamada para escolher a animação:
// o tipo de comando de um terminal ou o tipo de arquivo de uma ferramenta de arquivo.
// Só a categoria sai daqui, nunca o comando ou o caminho.
export function detailFor(tool: string, input: Record<string, unknown>): string | undefined {
  const command = typeof input.command === 'string' ? input.command : undefined
  if (command !== undefined && /^(Bash|PowerShell)$/.test(tool)) {
    const c = command.trim()
    if (/^(git|gh)\b/.test(c)) return 'git'
    if (/\b(jest|vitest|pytest|mocha|playwright|(npm|pnpm|yarn|bun)( run)? test|go test|cargo test|dotnet test|plugin test)\b/i.test(c)) return 'teste'
    if (/\b((npm|pnpm|yarn|bun) (i|install|add|ci)|pip3? install|cargo add|winget|choco|scoop|apt(-get)?|brew install|Install-Module)\b/i.test(c)) return 'instalar'
    if (/\b(build|tsc|make|cmake|msbuild|csc|gradle|mvn|webpack|esbuild|rollup)\b/i.test(c)) return 'compilar'
    if (/^(rm|del|rmdir|rd|Remove-Item)\b/i.test(c)) return 'apagar'
    if (/\b(docker|podman|kubectl|compose)\b/i.test(c)) return 'docker'
    if (/\b(curl|wget|Invoke-WebRequest|Invoke-RestMethod|iwr|irm|ssh|scp|ping|nslookup)\b/i.test(c)) return 'rede'
    if (/^(grep|rg|find|ls|dir|tree|cat|type|head|tail|Get-ChildItem|Get-Content|Select-String|gci|gc|wc)\b/i.test(c)) return 'olhar'
    if (/\b(node|python3?|py|deno|bun|ruby|java|dotnet run|cargo run|go run|Start-Process|(npm|pnpm|yarn) (run )?(dev|start|serve))\b/i.test(c)) return 'rodar'

    return 'comando'
  }
  const path = typeof input.file_path === 'string' ? input.file_path : typeof input.notebook_path === 'string' ? input.notebook_path : undefined
  if (path !== undefined) {
    const ext = (path.split(/[\\/]/).pop() ?? '').split('.').pop()?.toLowerCase() ?? ''
    if (ext === 'ipynb') return 'notebook'
    if (CODE.test(ext)) return 'codigo'
    if (DOCS.test(ext)) return 'doc'
    if (DATA.test(ext)) return 'dados'
    if (IMAGE.test(ext)) return 'imagem'
    if (WEB.test(ext)) return 'web'
    if (ext === 'pdf') return 'pdf'

    return 'arquivo'
  }

  return undefined
}

const PETS: Record<Lang, readonly string[]> = {
  pt: ['Hehe!', 'Obrigado!', 'Que carinho bom', 'De novo!', '♥', 'Tô feliz!'],
  en: ['Hehe!', 'Thank you!', 'That feels nice', 'Again!', '♥', "I'm happy!"],
}

// A conta de um e-mail. Cada conta lista domínios (empresa.com vale para ti.empresa.com também) ou
// e-mails inteiros; a que tiver "*" fica com todos os outros.
export function accountOf(email: string | undefined, contas: readonly Conta[] = CONTAS_PADRAO): Account {
  if (email === undefined || email.trim() === '') return 'outra'
  const mail = email.trim().toLowerCase()
  let rest: string | undefined
  for (const c of contas) {
    for (const rule of c.emails.toLowerCase().split(/[\s,;]+/).filter(Boolean)) {
      if (rule === '*') {
        rest ??= c.chave
        continue
      }
      const domain = rule.replace(/^@/, '')
      const isWholeEmail = rule.includes('@') && !rule.startsWith('@')
      if (isWholeEmail ? mail === rule : mail.endsWith(`@${domain}`) || mail.endsWith(`.${domain}`)) return c.chave
    }
  }

  return rest ?? 'outra'
}

function isConta(v: unknown): v is Conta {
  const c = v as Partial<Conta> | null

  return typeof c === 'object' && c !== null && typeof c.chave === 'string' && typeof c.nome === 'string' && typeof c.emails === 'string'
}

// O texto no idioma da sessão.
function tx(pt: string, en: string): string {
  return st.lang === 'en' ? en : pt
}

function accentOf(account: Account): string {
  const main = st.contas.find(c => c.chave === account)?.tema?.principal

  return main !== undefined && /^#[0-9a-f]{6}$/i.test(main) ? main : '#d97757'
}

function nameOf(account: Account): string {
  return st.contas.find(c => c.chave === account)?.nome ?? 'Conta'
}

export function moodOf(m: Memory, now: number, hour: number): Mood {
  if (m.lastFailAt > 0 && now - m.lastFailAt < 10 * 60_000) return 'triste'
  if (hour < 6) return 'sonolento'
  if (m.streak >= 3 || m.tasksToday >= 5) return 'feliz'

  return 'tranquilo'
}

export function greetingFor(m: Memory, now: number, hour: number, lang: Lang = 'pt'): string {
  const en = lang === 'en'
  if (m.sessions === 0) return en ? "Hi! I'm Clawd. I'll stick around with you." : 'Oi! Eu sou o Clawd. Vou ficar por aqui com você.'
  const days = Math.floor((now - m.lastSeen) / 86_400_000)
  if (days >= 2) return en ? `Long time no see! ${days} days.` : `Quanto tempo! ${days} dias sem te ver.`
  if (m.lastFailAt > 0 && now - m.lastFailAt < 60 * 60_000) return en ? "Try again? This time it'll work!" : 'Bora de novo? Dessa vez vai!'
  const morning = hour < 12 && hour >= 5, afternoon = hour < 18 && hour >= 12
  const hi = en ? (morning ? 'Good morning!' : afternoon ? 'Good afternoon!' : 'Good evening!') : morning ? 'Bom dia!' : afternoon ? 'Boa tarde!' : 'Boa noite!'
  if (m.tasksDone <= 0) return hi

  return en ? `${hi} ${m.tasksDone} tasks together so far.` : `${hi} Já são ${m.tasksDone} tarefas juntos.`
}

// A animação de cada estado; ocioso depende do humor.
export function animFor(a: Activity, mood: Mood): AnimName {
  switch (a.kind) {
    case 'oi':
      return 'oi'
    case 'pensando':
      return 'pensar'
    case 'trabalhando':
      return animForTool(a.tool)
    case 'feito':
      return 'feito'
    case 'erro':
      return 'erro'
    case 'dormindo':
      return 'dormir'
    case 'aguardando':
      return 'oi'
    case 'compactando':
      return 'pensar'
    case 'interrompido':
    case 'recusa':
      return 'erro'
    default:
      return mood === 'feliz' ? 'andarFeliz' : mood === 'triste' ? 'andarTriste' : mood === 'sonolento' ? 'sonolento' : 'andar'
  }
}

export function spinnerAnim(mode: string, tool: string | undefined): AnimName {
  if (mode === 'tool-use' || mode === 'tool-input') return animForTool(tool)
  if (mode === 'responding') return 'responder'

  return 'pensar'
}

function emptyMemory(now: number): Memory {
  return { firstMet: now, lastSeen: 0, sessions: 0, tasksDone: 0, failures: 0, streak: 0, today: '', tasksToday: 0, lastFailAt: 0, pets: 0 }
}

function dayKey(now: number): string {
  return new Date(now).toISOString().slice(0, 10)
}

function localHour(now: number): number {
  return new Date(now).getHours()
}

function esc(text: string): string {
  return text.replace(/[&<>"]/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[ch] ?? ch)
}

// A cena do fundo: um brilho na cor da conta e o Clawd (laranja, um por sessão) no canto
// inferior direito, longe do texto e da caixa de mensagem. Quando está à toa, passeia.
export function sceneSvg(a: Activity, mood: Mood, account: Account): string {
  const name = animFor(a, mood)
  const walking = a.kind === 'ocioso' && mood !== 'sonolento'
  const accent = accentOf(account)
  const line = a.line ? esc(a.line.length > 46 ? a.line.slice(0, 45) + '…' : a.line) : ''
  const bubbleW = Math.max(90, line.length * 7.2 + 24)
  const bubble = line
    ? `<g transform="translate(${Math.max(8, 300 - bubbleW)},120)">` +
      `<rect width="${bubbleW}" height="30" rx="10" fill="#ffffff" fill-opacity="0.92"/>` +
      `<path d="M${bubbleW - 40} 30 l10 10 l4 -10z" fill="#ffffff" fill-opacity="0.92"/>` +
      `<text x="12" y="20" font-family="Segoe UI, sans-serif" font-size="13" fill="#2a2a33">${line}</text></g>`
    : ''

  return (
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 900" preserveAspectRatio="xMidYMid slice">` +
    `<defs><radialGradient id="glow" cx="85%" cy="90%" r="60%">` +
    `<stop offset="0" stop-color="${accent}" stop-opacity="0.16"/><stop offset="1" stop-color="${accent}" stop-opacity="0"/>` +
    `</radialGradient></defs><rect width="1600" height="900" fill="url(#glow)"/>` +
    `<svg width="100%" height="100%" viewBox="0 0 420 300" preserveAspectRatio="xMaxYMax meet">` +
    `<style>.walk{animation:walk 18s ease-in-out infinite alternate}` +
    `@keyframes walk{0%{transform:translateX(0)}100%{transform:translateX(-200px)}}</style>` +
    bubble +
    `<g class="${walking ? 'walk' : ''}"><ellipse cx="342" cy="272" rx="44" ry="5" fill="#000" fill-opacity="0.12"/>` +
    `<g transform="translate(282,176) scale(5)">${animSvg(name, PALETTES.laranja, { id: 'bg' })}</g></g>` +
    `</svg></svg>`
  )
}

// Estado desta sessão. O módulo recomeça a cada recarga; register() o zera.
const st = {
  account: 'outra' as Account,
  sessionId: '',
  label: '',
  cwd: '',
  startedAt: 0,
  lastActive: 0,
  turnActive: false,
  lastWrite: 0,
  dir: '',
  memory: emptyMemory(0),
  ended: false,
  turns: 0,
  turnStartedAt: 0,
  costUsd: undefined as number | undefined,
  contextPercent: undefined as number | undefined,
  agents: {} as Record<string, number>,
  lastAgents: 0,
  model: '',
  contas: CONTAS_PADRAO,
  themeKey: '',
  ticks: 0,
  lang: 'pt' as Lang,
  tools: {} as Record<string, number>,
  busyMs: 0,
  done: 0,
  failed: 0,
  // o envio para o Gist (sessões fora do PC dos Clawds)
  email: '',
  relay: undefined as Relay | undefined,
  relayInfo: undefined as SessionInfo | undefined,
  relayHold: undefined as { kind: ActivityKind; since: number } | undefined,
  relayLast: 0,
  relayKey: '',
  relayBusy: false,
  relayPruned: false,
}

// Quantas chaves de ferramenta a sessão guarda, no máximo (cada MCP tem as suas).
const MAX_TOOL_KEYS = 80

// Conta uma chamada de ferramenta para as estatísticas da Central: só o nome da ferramenta
// e a categoria (git, código...), como no resto do arquivo da sessão.
export function countTool(tools: Record<string, number>, tool: string, detail: string | undefined): Record<string, number> {
  const key = detail !== undefined ? `${tool}:${detail}` : tool
  if (tools[key] === undefined && Object.keys(tools).length >= MAX_TOOL_KEYS) return tools

  return { ...tools, [key]: (tools[key] ?? 0) + 1 }
}

const AGENT_FRESH_MS = 20_000

function agentCount(now: number): number {
  let n = 0
  for (const [id, seen] of Object.entries(st.agents)) {
    if (now - seen < AGENT_FRESH_MS) n += 1
    else delete st.agents[id]
  }

  return n
}

type E = EngineInterface

type Limits = readonly { kind: string; percentUsed: number; resetsAt?: string }[]

// Os limites da conta (5 horas, semanal, os de cada modelo) para o Clawd do Windows mostrar.
async function writeUsage($: E, limits: Limits, cost: { usd: number } | undefined, context: { percent?: number }) {
  if (cost) st.costUsd = cost.usd
  if (context.percent !== undefined) st.contextPercent = context.percent
  if (st.dir === '' || limits.length === 0) return
  const now = await $.clock.now()
  try {
    await $.fs.write(`${st.dir}/uso-${st.account}.json`, JSON.stringify({ updatedAt: now, rateLimits: limits }))
  } catch {
    // idem
  }
}

async function measureNow($: E) {
  const u = await $.session.usage()
  await writeUsage($, u.rateLimits, u.cost, u.context)
}

async function writeSession($: E, force: boolean) {
  if ((st.dir === '' && st.relay === undefined) || st.sessionId === '') return
  const now = await $.clock.now()
  if (!force && now - st.lastWrite < HEARTBEAT_MS) return
  st.lastWrite = now
  const a = await read($, activity)
  st.lastAgents = agentCount(now)
  const info: SessionInfo = {
    id: st.sessionId, account: st.account, label: st.label, cwd: st.cwd, state: a.kind, tool: a.tool,
    ...(a.detail !== undefined ? { detail: a.detail } : {}),
    ...(st.lastAgents > 0 ? { agents: st.lastAgents } : {}),
    ...(st.model !== '' ? { model: st.model } : {}),
    startedAt: st.startedAt, updatedAt: now, ...(st.ended ? { endedAt: now } : {}),
    turns: st.turns, turnStartedAt: st.turnStartedAt,
    ...(st.costUsd !== undefined ? { costUsd: st.costUsd } : {}),
    ...(st.contextPercent !== undefined ? { contextPercent: st.contextPercent } : {}),
    ...(Object.keys(st.tools).length > 0 ? { tools: st.tools } : {}),
    busyMs: st.busyMs, done: st.done, failed: st.failed,
  }
  st.relayInfo = info
  if (st.dir === '') return
  try {
    await $.fs.write(`${st.dir}/sessions/${st.sessionId}.json`, JSON.stringify(info))
  } catch {
    // Sem a pasta compartilhada o Clawd do app segue funcionando; só o do Windows não vê.
  }
}

type FetchReply = { status?: number; text?: string }

async function gist($: E, r: Relay, method: 'GET' | 'PATCH', body?: unknown): Promise<FetchReply> {
  const reply = (await $.http.fetch(`https://api.github.com/gists/${r.gist}`, {
    method,
    headers: gistHeaders(r),
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  })) as unknown as FetchReply & { value?: FetchReply }

  return reply.value ?? reply
}

// Tira do Gist as sessões de ontem para trás (uma vez por sessão).
async function pruneRelay($: E, r: Relay, now: number, keep: string) {
  const got = await gist($, r, 'GET')
  if (got.status !== 200 || got.text === undefined) return
  const files = (JSON.parse(got.text) as { files?: Record<string, { content?: string } | null> }).files ?? {}
  const old = staleFiles(files, now, keep).slice(0, 40)
  if (old.length > 0) await gist($, r, 'PATCH', { files: Object.fromEntries(old.map(n => [n, null])) })
}

// Manda o estado desta sessão para o Gist, sem passar do ritmo que o GitHub aceita.
async function flushRelay($: E, ended = false) {
  const r = st.relay
  const info = st.relayInfo
  if (r === undefined || info === undefined || st.relayBusy) return
  const now = await $.clock.now()
  const payload = relayPayload(withHold(info, st.relayHold), r.origin, st.email)
  const key = relayKey(payload)
  if (!relayDue(now, st.relayLast, st.relayKey, key, ended)) return
  st.relayBusy = true
  st.relayLast = now
  try {
    const file = relayFile(info.id)
    if (!st.relayPruned) {
      st.relayPruned = true
      await pruneRelay($, r, now, file).catch(() => undefined)
    }
    const sent = await gist($, r, 'PATCH', { files: { [file]: { content: JSON.stringify(payload) } } })
    if (sent.status === 200) {
      st.relayKey = key
      if (st.relayHold !== undefined && payload.state === st.relayHold.kind) st.relayHold = undefined
    } else if (sent.status === 401 || sent.status === 404) {
      // Gist ou token errados: para de tentar nesta sessão.
      st.relay = undefined
    } else if (sent.status === 403 || sent.status === 429) {
      st.relayLast = now + RELAY_BACKOFF_MS
    }
  } catch {
    // sem rede ou sem permissão para api.github.com: tenta de novo na próxima volta
  } finally {
    st.relayBusy = false
  }
}

async function setActivity($: E, next: Activity) {
  if (PASSING.includes(next.kind)) st.relayHold = { kind: next.kind, since: next.since }
  await update($, activity, () => next)
  await writeSession($, true)
}

async function saveMemory($: E) {
  if (st.dir === '') return
  try {
    await $.fs.write(`${st.dir}/memoria-${st.account}.json`, JSON.stringify(st.memory))
  } catch {
    // idem
  }
}

async function refreshMood($: E) {
  const now = await $.clock.now()
  const mood = moodOf(st.memory, now, localHour(now))
  if ((await read($, moodAtom)) !== mood) await update($, moodAtom, () => mood)
}

async function refreshSessions($: E) {
  if (st.dir === '') return
  const now = await $.clock.now()
  let entries: readonly { name: string; kind: string; mtimeMs: number }[] = []
  try {
    entries = await $.fs.list(`${st.dir}/sessions`)
  } catch {
    return
  }
  const fresh = entries.filter(f => f.kind === 'file' && f.name.endsWith('.json') && now - f.mtimeMs < 24 * 3_600_000)
  const list: SessionInfo[] = []
  for (const f of fresh) {
    try {
      const text = await $.fs.read(`${st.dir}/sessions/${f.name}`)
      list.push(JSON.parse(String(text)) as SessionInfo)
    } catch {
      // arquivo sendo escrito por outra sessão: fica para a próxima volta
    }
  }
  const isActive = (s: SessionInfo) => s.endedAt === undefined && now - s.updatedAt < ACTIVE_WINDOW_MS
  list.sort((x, y) => Number(isActive(y)) - Number(isActive(x)) || y.updatedAt - x.updatedAt)
  const top = list.slice(0, 12)
  const old = await read($, sessions)
  if (JSON.stringify(old) !== JSON.stringify(top)) await update($, sessions, () => top)
}

// O ClawdDesktop instalado nesta máquina: aí quem mostra é ele, lendo o disco.
async function hasDesktop($: E): Promise<boolean> {
  try {
    return (await $.fs.list(st.dir)).some(f => f.name === 'app' && f.kind === 'dir')
  } catch {
    return false
  }
}

// As contas e os temas que a Central gravou. Sem o arquivo, ficam as contas de sempre.
async function loadConfig($: E) {
  if (st.dir === '') return
  try {
    const raw = JSON.parse(String(await $.fs.read(`${st.dir}/config.json`))) as { contas?: unknown; idiomaAtual?: unknown }
    const list = Array.isArray(raw.contas) ? raw.contas.filter(isConta) : []
    if (list.length > 0) st.contas = list
    if (raw.idiomaAtual === 'pt' || raw.idiomaAtual === 'en') st.lang = raw.idiomaAtual
  } catch {
    // sem config, ou sendo gravada agora: fica como está
  }
}

// O tema da conta, com o Clawd desenhado no fundo. Só chama o app quando algo mudou.
async function applyTheme($: E) {
  const conta = st.contas.find(c => c.chave === st.account)
  if (conta === undefined) return
  const tema = conta.tema ?? {}
  const key = JSON.stringify(tema)
  if (key === st.themeKey) return
  st.themeKey = key
  const colors = tema.ativo === true ? customTheme(conta.nome, tema) : undefined
  const look: ThemeSetArgs = { ...(colors !== undefined ? { colors } : {}), backdrop: { scene: SCENE } }
  await $.theme.set(look)
}

async function tick($: E) {
  const now = await $.clock.now()
  st.ticks += 1
  if (st.ticks % CONFIG_EVERY_TICKS === 0) {
    // A Central mudou as cores com a sessão aberta: o tema acompanha.
    await loadConfig($)
    await applyTheme($).catch(() => undefined)
  }
  const a = await read($, activity)
  const passing = a.kind === 'feito' || a.kind === 'erro' || a.kind === 'oi' || a.kind === 'interrompido' || a.kind === 'recusa'
  if (passing && now - a.since > SHORT_MS && !st.turnActive) {
    await setActivity($, { kind: 'ocioso', since: now })
  } else if (a.kind === 'ocioso' && now - st.lastActive > SLEEP_AFTER_MS) {
    await setActivity($, { kind: 'dormindo', since: now })
  }
  await refreshMood($)
  await writeSession($, agentCount(now) !== st.lastAgents)
  await flushRelay($)
  await refreshSessions($)
}

async function pet($: E) {
  st.memory = { ...st.memory, pets: st.memory.pets + 1 }
  await saveMemory($)
  const pets = PETS[st.lang]
  const line = pets[st.memory.pets % pets.length]
  await setActivity($, { kind: 'oi', since: await $.clock.now(), ...(line ? { line } : {}) })
}

async function notePrompt($: E, text: string) {
  if (st.label === '' || st.label === st.cwd.split(/[\\/]/).filter(Boolean).pop()) {
    // Blocos que o app cola no prompt (<system-reminder>…</system-reminder> etc.) não são o assunto.
    const first = text.replace(/<([a-z][\w-]*)[^>]*>[\s\S]*?<\/\1>/gi, ' ').replace(/\s+/g, ' ').trim()
    if (first !== '' && !first.startsWith('/') && !first.startsWith('<')) st.label = first.length > 48 ? first.slice(0, 47) + '…' : first
  }
  st.lastActive = await $.clock.now()
}

async function noteToolStart($: E, tool: string, input: Record<string, unknown>, agentId: string | undefined) {
  const now = await $.clock.now()
  st.lastActive = now
  if (st.turnActive || agentId !== undefined) st.tools = countTool(st.tools, tool, detailFor(tool, input))
  if (agentId !== undefined) {
    // Um subagente trabalhando: o Clawd ganha ajudantes, mas segue na tarefa dele.
    const isNew = st.agents[agentId] === undefined
    st.agents[agentId] = now
    if (isNew) await writeSession($, true)

    return
  }
  if (!st.turnActive) return
  if (ASKS_PERSON.test(tool)) {
    await setActivity($, { kind: 'aguardando', tool, since: now })

    return
  }
  const detail = detailFor(tool, input)
  await setActivity($, { kind: 'trabalhando', tool, ...(detail !== undefined ? { detail } : {}), since: now })
}

async function noteToolEnd($: E, tool: string, agentId: string | undefined) {
  if (!st.turnActive || agentId !== undefined) return
  const after = await read($, activity)
  if ((after.kind === 'trabalhando' || after.kind === 'aguardando') && after.tool === tool) {
    await setActivity($, { kind: 'pensando', since: await $.clock.now() })
  }
}

// O pedido de permissão vai para a pessoa: o Clawd fica esperando por ela.
async function noteAsk($: E, tool: string) {
  if (!st.turnActive) return
  await setActivity($, { kind: 'aguardando', tool, since: await $.clock.now() })
}

async function noteCompact($: E, done: boolean) {
  const now = await $.clock.now()
  if (!done) await setActivity($, { kind: 'compactando', since: now })
  else if ((await read($, activity)).kind === 'compactando') await setActivity($, { kind: st.turnActive ? 'pensando' : 'ocioso', since: now })
}

export const register: Register = on => {
  Object.assign(st, {
    account: 'outra', sessionId: '', label: '', cwd: '', startedAt: 0, lastActive: 0,
    turnActive: false, lastWrite: 0, dir: '', memory: emptyMemory(0), ended: false,
    turns: 0, turnStartedAt: 0, costUsd: undefined, contextPercent: undefined,
    agents: {}, lastAgents: 0, model: '', contas: CONTAS_PADRAO, themeKey: '', ticks: 0,
    lang: 'pt', tools: {}, busyMs: 0, done: 0, failed: 0,
    email: '', relay: undefined, relayInfo: undefined, relayHold: undefined, relayLast: 0, relayKey: '',
    relayBusy: false, relayPruned: false,
  })

  on('session.measure', async ($, e, next) => {
    await writeUsage($, e.rateLimits, e.cost, e.context).catch(() => undefined)
    await writeSession($, true).catch(() => undefined)

    return next(e)
  })

  on('session.start', async ($, e, next) => {
    const now = await $.clock.now()
    st.sessionId = await $.session.id()
    st.cwd = e.cwd
    st.lang = langFrom(undefined, (await $.env.get('LC_ALL')) ?? (await $.env.get('LANG')))
    st.label = st.cwd.split(/[\\/]/).filter(Boolean).pop() ?? tx('sessão', 'session')
    st.startedAt = now
    st.lastActive = now
    // No Windows a pasta é a que o ClawdDesktop lê; fora dele (nuvem, Mac, Linux) ela serve
    // só para o painel /clawd desta máquina.
    const profile = await $.env.get('USERPROFILE')
    const home = profile ?? (await $.env.get('HOME'))
    st.dir = home ? `${home}/.claude/clawd` : ''
    await loadConfig($)
    st.email = (await $.env.get('CLAUDE_CODE_USER_EMAIL')) ?? ''
    st.account = accountOf(st.email, st.contas)
    st.relay = relayFrom(
      {
        gist: await $.env.get('CLAWD_GIST_ID'),
        token: await $.env.get('CLAWD_GITHUB_TOKEN'),
        remote: await $.env.get('CLAUDE_CODE_REMOTE'),
        force: await $.env.get('CLAWD_RELAY'),
      },
      profile !== undefined && (await hasDesktop($)),
    )

    // Memória da conta: cumprimenta conforme o que lembra, depois atualiza.
    if (st.dir !== '') {
      try {
        st.memory = { ...emptyMemory(now), ...(JSON.parse(await $.fs.read(`${st.dir}/memoria-${st.account}.json`)) as Partial<Memory>) }
      } catch {
        st.memory = emptyMemory(now)
      }
    }
    const hello = greetingFor(st.memory, now, localHour(now), st.lang)
    st.memory = { ...st.memory, sessions: st.memory.sessions + 1, lastSeen: now }
    await saveMemory($)

    await applyTheme($)

    await update($, activity, () => ({ kind: 'oi', since: now, line: hello }) as Activity)
    await refreshMood($)
    await measureNow($).catch(() => undefined)
    await writeSession($, true)
    await refreshSessions($)

    await $.command.register({ name: 'clawd', description: tx('Abre o painel dos Clawds de cada sessão', 'Opens the panel with the Clawd of each session') })
    void $.ui.open({ id: PANE, title: 'Clawds' })
    $.clock.every(4_000, () => tick($))

    return next(e)
  })

  on('command.run', { command: 'clawd' }, async $ => {
    await $.ui.open({ id: PANE, title: 'Clawds' })

    return { text: tx('Painel dos Clawds aberto.', 'Clawds panel opened.') }
  })

  // O Clawd nunca segura a mensagem nem a ferramenta: falhas dele só são ignoradas.
  on('prompt.submit', async ($, e, next) => {
    await notePrompt($, e.text).catch(() => undefined)

    return next(e)
  })

  on('turn.start', async ($, e, next) => {
    if (e.agentId === undefined) {
      st.turnActive = true
      const now = await $.clock.now()
      st.lastActive = now
      st.turnStartedAt = now
      await setActivity($, { kind: 'pensando', since: now })
    }

    return next(e)
  })

  on('tool.call', async ($, e, next) => {
    await noteToolStart($, e.tool, e as unknown as Record<string, unknown>, e.agentId).catch(() => undefined)
    const ran = await next(e)
    await noteToolEnd($, e.tool, e.agentId).catch(() => undefined)

    return ran
  })

  on('tool.check', async ($, e, next) => {
    const r = await next(e)
    if (r.decision === 'ask' && e.tool_use_id !== undefined && e.agentId === undefined) await noteAsk($, e.tool).catch(() => undefined)

    return r
  })

  on('session.compact', async ($, e, next) => {
    if (e.agentId !== undefined) return next(e)
    await noteCompact($, false).catch(() => undefined)
    try {
      return await next(e)
    } finally {
      await noteCompact($, true).catch(() => undefined)
    }
  })

  // O passo transmite em partes: só anota o modelo e repassa tudo.
  on('turn.step', async function* ($, e, next) {
    if (!('agentId' in e) || (e as { agentId?: string }).agentId === undefined) st.model = e.model

    return yield* next(e)
  })

  on('turn.complete', async ($, e, next) => {
    const isMain = !('agentId' in e) || (e as { agentId?: string }).agentId === undefined
    if (isMain && st.turnActive) {
      st.turnActive = false
      st.turns += 1
      const now = await $.clock.now()
      st.lastActive = now
      if (st.turnStartedAt > 0) st.busyMs += Math.max(0, now - st.turnStartedAt)
      if (e.reason === 'answer') st.done += 1
      else if (e.reason !== 'aborted' && e.reason !== 'refusal') st.failed += 1
      const today = dayKey(now)
      const base = st.memory.today === today ? st.memory : { ...st.memory, today, tasksToday: 0 }
      if (e.reason === 'answer') {
        st.memory = { ...base, tasksDone: base.tasksDone + 1, tasksToday: base.tasksToday + 1, streak: base.streak + 1, lastSeen: now }
        const line = st.memory.streak > 0 && st.memory.streak % 5 === 0 ? tx(`${st.memory.streak} seguidas! Mandou bem.`, `${st.memory.streak} in a row! Nice work.`) : undefined
        await setActivity($, { kind: 'feito', since: now, ...(line ? { line } : {}) })
      } else if (e.reason === 'aborted') {
        st.memory = { ...base, lastSeen: now }
        await setActivity($, { kind: 'interrompido', since: now, line: tx('Opa, parei!', 'Whoa, stopped!') })
      } else if (e.reason === 'refusal') {
        st.memory = { ...base, lastSeen: now }
        await setActivity($, { kind: 'recusa', since: now, line: tx('Esse eu não faço.', "I won't do that one.") })
      } else {
        st.memory = { ...base, failures: base.failures + 1, streak: 0, lastFailAt: now, lastSeen: now }
        await setActivity($, { kind: 'erro', since: now, line: tx('Ops… vamos ver o que houve.', "Oops… let's see what happened.") })
      }
      await saveMemory($)
      await refreshMood($)
    }

    return next(e)
  })

  on('session.end', async ($, e, next) => {
    st.ended = true
    await update($, activity, () => ({ kind: 'dormindo', since: 0 }) as Activity)
    await writeSession($, true)
    await flushRelay($, true).catch(() => undefined)

    return next(e)
  })

  // O indicador de trabalho do Desktop: o Clawd faz a animação da tarefa ao lado do texto.
  on('ui.render', { component: 'Spinner' }, async ($, e, next) => {
    if (e.surface !== 'desktop') return next(e)
    const { Box, Svg } = $.ui.resolve(e)
    const a = await read($, activity)
    const name = spinnerAnim(e.props.mode, a.kind === 'trabalhando' ? a.tool : undefined)
    const row = await next(e)

    return (
      <Box flexDirection="row" alignItems="center">
        <Svg source={clawdSvg(name, PALETTES.laranja, { id: 'sp' })} alt={`Clawd: ${name}`} width={36} height={30} />
        {row}
      </Box>
    )
  })

  // O fundo da conversa.
  on('ui.render', { component: 'Backdrop' }, async ($, e, next) => {
    if (e.props.plugin !== PLUGIN || e.props.scene !== SCENE) return next(e)
    if (e.surface !== 'desktop' && e.surface !== 'mobile') return next(e)
    const { Svg } = $.ui.resolve(e)
    const a = await read($, activity)
    const mood = await read($, moodAtom)

    return <Svg source={sceneSvg(a, mood, st.account)} alt={`Clawd ${STATE_TEXT[st.lang][a.kind]}`} isInteractive />
  })

  // O painel: um mini Clawd por sessão (ativas primeiro, as paradas dormindo).
  on('ui.render', { component: 'Pane', requestId: PANE }, async ($, e) => {
    const { Box, Text, Button } = $.ui.resolve(e)
    const list = await read($, sessions)
    const mood = await read($, moodAtom)
    const now = await $.clock.now()
    const svgOk = e.surface !== 'terminal'
    const table = $.ui.resolve(e) as unknown as { Svg?: (p: { source: string; alt: string; width?: number; height?: number }) => unknown }
    const Svg = table.Svg as any

    const header = (
      <Box flexDirection="column" marginBottom={1}>
        <Text bold>{tx(`Clawd está ${MOOD_TEXT.pt[mood]}`, `Clawd is ${MOOD_TEXT.en[mood]}`)}</Text>
        <Text dimColor>
          {tx(
            `${st.memory.tasksDone} tarefas juntos · ${st.memory.tasksToday} hoje · ${st.memory.pets} carinhos`,
            `${st.memory.tasksDone} tasks together · ${st.memory.tasksToday} today · ${st.memory.pets} pets`,
          )}
        </Text>
        <Button
          key="pet"
          label={tx('Fazer carinho', 'Pet')}
          onPress={() => pet($)}
        />
      </Box>
    )

    const rows = list.map((s, i) => {
      const active = s.endedAt === undefined && now - s.updatedAt < ACTIVE_WINDOW_MS
      const shown: Activity = active ? { kind: s.state, tool: s.tool, since: 0 } : { kind: 'dormindo', since: 0 }
      const name = animFor(shown, 'tranquilo')
      const mine = s.id === st.sessionId
      const minutes = Math.max(0, Math.round((now - s.updatedAt) / 60_000))

      return (
        <Box key={s.id} flexDirection="row" alignItems="center" marginBottom={1}>
          {svgOk && Svg ? (
            <Svg source={clawdSvg(name, PALETTES.laranja, { id: `p${i}` })} alt={`Clawd ${STATE_TEXT[st.lang][shown.kind]}`} width={44} height={36} />
          ) : (
            <Text>{active ? '●' : '○'} </Text>
          )}
          <Box flexDirection="column" marginLeft={1}>
            <Text bold={mine}>
              {mine ? '▸ ' : ''}
              {s.label}
            </Text>
            <Text dimColor>
              {nameOf(s.account)} · {active ? STATE_TEXT[st.lang][s.state] : minutes < 60 ? tx(`parado há ${minutes} min`, `idle for ${minutes} min`) : tx('dormindo', 'asleep')}
            </Text>
          </Box>
        </Box>
      )
    })

    return (
      <Box flexDirection="column">
        {header}
        {rows.length === 0 ? <Text dimColor>{tx('Nenhuma sessão ainda.', 'No sessions yet.')}</Text> : rows}
      </Box>
    )
  })
}
