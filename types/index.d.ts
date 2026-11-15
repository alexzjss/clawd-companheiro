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
