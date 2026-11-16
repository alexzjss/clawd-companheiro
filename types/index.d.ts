/** A chave da conta na config da Central (usp, pessoal...); 'outra' quando nenhuma serve. */
export type Account = string

export type ActivityKind =
  | 'oi'
  | 'ocioso'
  | 'pensando'
  | 'trabalhando'
  | 'feito'
  | 'erro'
  | 'dormindo'
  | 'aguardando'
  | 'compactando'
  | 'interrompido'
  | 'recusa'

export type Activity = { kind: ActivityKind; tool?: string; detail?: string; since: number; line?: string }

/** De onde vem uma sessão que não está no PC dos Clawds: a nuvem do claude.ai ou outro computador. */
export type SessionOrigin = 'nuvem' | 'outro'

export type Mood = 'feliz' | 'tranquilo' | 'triste' | 'sonolento'

export type Memory = {
  firstMet: number
  lastSeen: number
  sessions: number
  tasksDone: number
  failures: number
  streak: number
  today: string
  tasksToday: number
  lastFailAt: number
  pets: number
}

export type SessionInfo = {
  id: string
  account: Account
  label: string
  cwd: string
  state: ActivityKind
  tool?: string
  startedAt: number
  updatedAt: number
  endedAt?: number
  turns?: number
  turnStartedAt?: number
  costUsd?: number
  contextPercent?: number
  /** O tipo de comando (git, teste, instalar...) ou de arquivo (codigo, doc, dados...). */
  detail?: string
  /** Subagentes trabalhando agora. */
  agents?: number
  /** O modelo do último passo do turno. */
  model?: string
  /** Chamadas de ferramenta na sessão, por "Ferramenta" ou "Ferramenta:categoria" (nunca o comando ou o caminho). */
  tools?: Record<string, number>
  /** Tempo somado dos turnos já terminados, em ms. */
  busyMs?: number
  /** Turnos que terminaram com resposta e com erro. */
  done?: number
  failed?: number
  /** Só nas sessões que chegam pelo Gist: de onde vêm. */
  origin?: SessionOrigin
  /** Só nas sessões que chegam pelo Gist: o e-mail da conta, para o Desktop achar a conta. */
  email?: string
}

declare module 'claude-code' {
  interface PluginState {
    'clawd-companheiro': {
      activity: Activity
      mood: Mood
      sessions: SessionInfo[]
    }
  }
}
