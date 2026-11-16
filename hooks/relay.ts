// As sessões que não rodam no PC dos Clawds (na nuvem do claude.ai, em outro computador)
// mandam o estado delas para um Gist privado do GitHub; o ClawdDesktop lê de lá.
// Este arquivo tem só as regras: quem manda, o que vai e quando. O envio fica em register.tsx.

import type { ActivityKind, SessionInfo, SessionOrigin } from '../types'

export type Relay = { gist: string; token?: string; origin: SessionOrigin }

// O GitHub limita as escritas (umas 500 por hora): um envio a cada 20 s no máximo por sessão,
// e sem mudança só um sinal de vida por minuto.
export const RELAY_MIN_MS = 20_000
export const RELAY_HEARTBEAT_MS = 60_000
// Recusas do GitHub que valem esperar antes de tentar de novo (limite, proxy).
export const RELAY_BACKOFF_MS = 5 * 60_000
// Arquivos de sessão mais velhos que isso saem do Gist.
export const RELAY_KEEP_MS = 24 * 3_600_000

const GIST_ID = /^[0-9a-f]{20,40}$/i

// Estados que passam rápido e o Desktop não pode perder (ele lê o Gist de tempos em tempos).
export const PASSING: readonly ActivityKind[] = ['feito', 'erro', 'interrompido', 'recusa']

export type RelayEnv = {
  gist?: string
  token?: string
  /** CLAUDE_CODE_REMOTE: a sessão roda num container na nuvem. */
  remote?: string
  /** CLAWD_RELAY: '1' sempre manda, '0' nunca. */
  force?: string
}

// Quem manda: só quem tem o Gist configurado e não está no PC que mostra os Clawds.
// hasDesktop é ter o ClawdDesktop instalado nesta máquina (~/.claude/clawd/app).
export function relayFrom(env: RelayEnv, hasDesktop: boolean): Relay | undefined {
  const gist = env.gist?.trim() ?? ''
  if (!GIST_ID.test(gist) || env.force === '0') return undefined
  const remote = env.remote === 'true' || env.remote === '1'
  if (!remote && hasDesktop && env.force !== '1') return undefined
  const token = env.token?.trim()

  return { gist, ...(token ? { token } : {}), origin: remote ? 'nuvem' : 'outro' }
}

export function relayFile(id: string): string {
  return `sessao-${id.replace(/[^\w.-]/g, '_')}.json`
}

// O que vai para o Gist: o mesmo estado do arquivo local, mais a origem e o e-mail (para o
// Desktop achar a conta pelas regras da Central). Do caminho, só o nome da pasta.
export function relayPayload(info: SessionInfo, origin: SessionOrigin, email: string): SessionInfo {
  const folder = info.cwd.split(/[\\/]/).filter(Boolean).pop() ?? ''

  return { ...info, cwd: folder, origin, ...(email !== '' ? { email } : {}) }
}

// Um estado que passa rápido (terminou, deu erro...) segura o lugar até ser enviado, para o
// Desktop comemorar mesmo que a sessão já tenha voltado a ficar à toa. Uma tarefa nova manda.
export function withHold(info: SessionInfo, hold: { kind: ActivityKind; since: number } | undefined): SessionInfo {
  if (hold === undefined) return info
  if (info.state !== 'ocioso' && info.state !== 'dormindo' && info.state !== 'oi') return info

  return { ...info, state: hold.kind, tool: undefined }
}

// O que conta como mudança: tudo menos a hora da escrita.
export function relayKey(info: SessionInfo): string {
  const { updatedAt: _ignored, ...rest } = info

  return JSON.stringify(rest)
}

export function relayDue(now: number, lastSent: number, lastKey: string, key: string, ended: boolean): boolean {
  if (ended) return true
  if (now - lastSent < RELAY_MIN_MS) return false

  return key !== lastKey || now - lastSent >= RELAY_HEARTBEAT_MS
}

// Os arquivos de sessão do Gist que já podem sair: velhos ou ilegíveis.
export function staleFiles(files: Record<string, { content?: string } | null | undefined>, now: number, keep: string): string[] {
  const out: string[] = []
  for (const [name, f] of Object.entries(files)) {
    if (!name.startsWith('sessao-') || !name.endsWith('.json') || name === keep) continue
    let updated = 0
    try {
      updated = Number((JSON.parse(f?.content ?? '') as { updatedAt?: unknown }).updatedAt) || 0
    } catch {
      updated = 0
    }
    if (now - updated > RELAY_KEEP_MS) out.push(name)
  }

  return out
}

export function gistHeaders(r: Relay): Record<string, string> {
  return {
    Accept: 'application/vnd.github+json',
    'Content-Type': 'application/json',
    'User-Agent': 'clawd-companheiro',
    'X-GitHub-Api-Version': '2022-11-28',
    // Na nuvem o token pode vir do proxy do ambiente; aí não vai nada aqui.
    ...(r.token !== undefined ? { Authorization: `Bearer ${r.token}` } : {}),
  }
}
