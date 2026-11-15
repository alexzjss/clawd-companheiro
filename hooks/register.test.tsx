import { expect, mock, test } from 'claude-code/testing'

import { accountOf, animFor, detailFor, greetingFor, moodOf, sceneSvg, spinnerAnim } from './register'
import { customTheme, surfaces } from './themes'
import { ANIMS, PALETTES, animForTool, clawdSvg } from './sprites'

type T = Parameters<Extract<Parameters<typeof test>[1], (...args: any[]) => any>>
const SPINNER = { plugin: 'clawd-companheiro', component: 'Spinner', requestId: 'spin' } as const

// O motor embaixo do mod, sem disco (USERPROFILE ausente: nada é gravado).
function engine(on: T[1], email: string, themes: unknown[]) {
  mock.env(on, { CLAUDE_CODE_USER_EMAIL: email })
  mock.clock(on, { now: Date.UTC(2026, 10, 14, 15, 0, 0) })
  on('theme.set', (_, e) => {
    themes.push(e)

    return { value: { isDrawn: true, requestId: 'theme' } }
  })
  on('session.start', (_, e) => ({ cwd: e.cwd }))
  on('session.id', () => ({ value: 'sessao-teste' }))
  on('command.register', () => ({ value: { command: 'clawd' } }))
  on('ui.open', () => ({ value: { isPlaced: true } }))
}

test('sem config, o Clawd vai para o fundo e as cores ficam as do app', async ($, on) => {
  const themes: Array<{ colors?: unknown; backdrop?: { scene?: string } }> = []
  engine(on, 'alguem@usp.br', themes)
  await $.session.start({ cwd: 'C:/proj/x', surface: 'desktop', isInteractive: true })

  // Sem config não há tema: só o Clawd no fundo.
  expect(themes.length).toBe(1)
  expect(themes[0]?.backdrop?.scene).toBe('clawd')
  expect(themes[0]?.colors).toBeUndefined()
})

test('o indicador mostra a animação da ferramenta em uso', async ($, on) => {
  engine(on, 'alguem@gmail.com', [])
  on('turn.start', (_, e) => ({ turnId: e.turnId }))
  on('tool.call', () => ({ result: {} }))
  await $.session.start({ cwd: '/', surface: 'desktop', isInteractive: true })

  const ui = await $.ui.mount({
    ...SPINNER,
    surface: 'desktop',
    props: { word: 'Working', message: null, suffix: '…', mode: 'thinking' },
  })
  const drawn = await ui.find({ type: 'Svg' })
  expect(drawn?.props?.alt).toBe('Clawd: pensar')
  await ui.unmount()

  const tool = await $.ui.mount({
    ...SPINNER,
    surface: 'desktop',
    props: { word: 'Working', message: null, suffix: '…', mode: 'responding' },
  })
  expect((await tool.find({ type: 'Svg' }))?.props?.alt).toBe('Clawd: responder')
  await tool.unmount()
})

test('ferramentas viram as animações certas', () => {
  expect(animForTool('Bash')).toBe('bash')
  expect(animForTool('PowerShell')).toBe('bash')
  expect(animForTool('Read')).toBe('ler')
  expect(animForTool('Grep')).toBe('ler')
  expect(animForTool('Edit')).toBe('editar')
  expect(animForTool('Write')).toBe('editar')
  expect(animForTool('WebSearch')).toBe('web')
  expect(animForTool('mcp__Claude_Browser__navigate')).toBe('web')
  expect(animForTool('Agent')).toBe('agente')
  expect(animForTool('mcp__ccd_turn__update_plan')).toBe('planejar')
  expect(animForTool('mcp__alguma__coisa')).toBe('ferramenta')
  expect(spinnerAnim('tool-use', 'Edit')).toBe('editar')
  expect(spinnerAnim('thinking', 'Edit')).toBe('pensar')
})

test('humor e cumprimento vêm da memória', () => {
  const now = Date.UTC(2026, 10, 14, 15)
  const base = { firstMet: 0, lastSeen: now - 3_600_000, sessions: 4, tasksDone: 12, failures: 0, streak: 0, today: '', tasksToday: 0, lastFailAt: 0, pets: 0 }

  expect(greetingFor({ ...base, sessions: 0 }, now, 10)).toBe('Oi! Eu sou o Clawd. Vou ficar por aqui com você.')
  expect(greetingFor({ ...base, lastSeen: now - 3 * 86_400_000 }, now, 10)).toBe('Quanto tempo! 3 dias sem te ver.')
  expect(greetingFor(base, now, 9)).toBe('Bom dia! Já são 12 tarefas juntos.')
  expect(moodOf({ ...base, lastFailAt: now - 60_000 }, now, 15)).toBe('triste')
  expect(moodOf({ ...base, streak: 3 }, now, 15)).toBe('feliz')
  expect(moodOf(base, now, 3)).toBe('sonolento')
  expect(moodOf(base, now, 15)).toBe('tranquilo')
  expect(animFor({ kind: 'ocioso', since: 0 }, 'feliz')).toBe('andarFeliz')
  expect(animFor({ kind: 'trabalhando', tool: 'Bash', since: 0 }, 'tranquilo')).toBe('bash')
})

test('limites e sessão vão para a pasta que o Clawd do Windows lê', async ($, on) => {
  const writes: Record<string, string> = {}
  mock.env(on, { CLAUDE_CODE_USER_EMAIL: 'alguem@usp.br', USERPROFILE: 'C:/Users/x' })
  mock.clock(on, { now: Date.UTC(2026, 10, 14, 15, 0, 0) })
  on('theme.set', () => ({ value: { isDrawn: true, requestId: 'theme' } }))
  on('session.start', (_, e) => ({ cwd: e.cwd }))
  on('session.id', () => ({ value: 'sessao-teste' }))
  on('command.register', () => ({ value: { command: 'clawd' } }))
  on('ui.open', () => ({ value: { isPlaced: true } }))
  on('fs.read', () => ({ value: '{}' }))
  on('fs.list', () => ({ value: [] }))
  on('fs.write', (_, e) => {
    writes[e.path] = e.text

    return { value: undefined }
  })
  on('session.usage', () => ({
    value: {
      startedAt: 0,
      context: { window: 200_000, percent: 12 },
      rateLimits: [{ kind: 'five_hour', percentUsed: 40, resetsAt: '2026-11-14T18:00:00Z' }, { kind: 'seven_day', percentUsed: 9 }],
      cost: { usd: 1.25 },
    },
  }))
  await $.session.start({ cwd: 'C:/proj/x', surface: 'desktop', isInteractive: true })

  // O motor entrega o caminho no formato do Windows; basta achar pelo fim.
  const written = (end: string) => Object.entries(writes).find(([k]) => k.split(String.fromCharCode(92)).join('/').endsWith(end))?.[1] ?? '{}'
  const uso = JSON.parse(written('/.claude/clawd/uso-claude.json'))
  expect(uso.rateLimits?.[0]?.kind).toBe('five_hour')
  expect(uso.rateLimits?.[1]?.percentUsed).toBe(9)
  const sessao = JSON.parse(written('/.claude/clawd/sessions/sessao-teste.json'))
  expect(sessao.account).toBe('claude')
  expect(sessao.costUsd).toBe(1.25)
  expect(sessao.contextPercent).toBe(12)
})

test('o tipo do comando ou do arquivo vira detalhe, sem o texto dele', () => {
  expect(detailFor('Bash', { command: 'git status' })).toBe('git')
  expect(detailFor('Bash', { command: 'npm test' })).toBe('teste')
  expect(detailFor('PowerShell', { command: 'npm install react' })).toBe('instalar')
  expect(detailFor('Bash', { command: 'tsc -p .' })).toBe('compilar')
  expect(detailFor('Bash', { command: 'rm -rf dist' })).toBe('apagar')
  expect(detailFor('Bash', { command: 'curl https://x' })).toBe('rede')
  expect(detailFor('Bash', { command: 'ls -la' })).toBe('olhar')
  expect(detailFor('Bash', { command: 'python app.py' })).toBe('rodar')
  expect(detailFor('Bash', { command: 'echo oi' })).toBe('comando')
  expect(detailFor('Read', { file_path: 'C:/x/a.tsx' })).toBe('codigo')
  expect(detailFor('Edit', { file_path: '/x/README.md' })).toBe('doc')
  expect(detailFor('Write', { file_path: '/x/data.json' })).toBe('dados')
  expect(detailFor('Read', { file_path: '/x/foto.PNG' })).toBe('imagem')
  expect(detailFor('WebSearch', { query: 'x' })).toBeUndefined()
})

test('conta pelo domínio do e-mail', () => {
  const contas = [
    { chave: 'usp', nome: 'USP', emails: 'usp.br' },
    { chave: 'pessoal', nome: 'Pessoal', emails: '*' },
  ]
  expect(accountOf('a@usp.br', contas)).toBe('usp')
  expect(accountOf('a@ime.usp.br', contas)).toBe('usp')
  expect(accountOf('a@fakeusp.br', contas)).toBe('pessoal')
  expect(accountOf(undefined, contas)).toBe('outra')
  // sem config, tudo é de uma conta só
  expect(accountOf('a@qualquer.com')).toBe('claude')
})

test('a config da Central decide a conta e o tema', async ($, on) => {
  const contas = [
    { chave: 'trabalho', nome: 'Trabalho', emails: 'empresa.com, chefe@gmail.com' },
    { chave: 'casa', nome: 'Casa', emails: '*' },
  ]
  expect(accountOf('eu@empresa.com', contas)).toBe('trabalho')
  expect(accountOf('eu@ti.empresa.com', contas)).toBe('trabalho')
  expect(accountOf('chefe@gmail.com', contas)).toBe('trabalho')
  expect(accountOf('outro@gmail.com', contas)).toBe('casa')
  expect(accountOf('eu@empresa.com', [{ chave: 'x', nome: 'X', emails: '' }])).toBe('outra')

  expect(surfaces('#1094ab', true)['surface-0']).toMatch(/^#[0-9a-f]{6}$/)
  expect(customTheme('Verde', { principal: '#2f9e6b', fundo: '#2f9e6b' })).toBeDefined()
  expect(customTheme('Sem cor', { principal: 'azul' })).toBeDefined()

  // Com a config na pasta, a sessão pega a conta e o tema dela.
  const themes: Array<{ colors?: unknown }> = []
  mock.env(on, { CLAUDE_CODE_USER_EMAIL: 'eu@empresa.com', USERPROFILE: 'C:/Users/x' })
  mock.clock(on, { now: Date.UTC(2026, 10, 14, 15, 0, 0) })
  on('theme.set', (_, e) => {
    themes.push(e)

    return { value: { isDrawn: true, requestId: 'theme' } }
  })
  on('session.start', (_, e) => ({ cwd: e.cwd }))
  on('session.id', () => ({ value: 'sessao-teste' }))
  on('command.register', () => ({ value: { command: 'clawd' } }))
  on('ui.open', () => ({ value: { isPlaced: true } }))
  on('fs.list', () => ({ value: [] }))
  on('fs.write', () => ({ value: undefined }))
  on('session.usage', () => ({ value: { startedAt: 0, context: { window: 200_000 }, rateLimits: [] } }))
  const config = { contas: [{ ...contas[0], tema: { ativo: true, principal: '#2f9e6b', fundo: '#2f9e6b' } }, contas[1]] }
  on('fs.read', (_, e) => ({ value: e.path.endsWith('config.json') ? JSON.stringify(config) : '{}' }))
  await $.session.start({ cwd: 'C:/proj/x', surface: 'desktop', isInteractive: true })

  expect(themes.length).toBe(1)
  expect(themes[0]?.colors).toBeDefined()
})

test('todo SVG cabe no limite de 131072 caracteres', () => {
  for (const name of Object.keys(ANIMS) as (keyof typeof ANIMS)[]) {
    expect(clawdSvg(name, PALETTES.laranja).length).toBeLessThan(131_072)
    expect(sceneSvg({ kind: 'oi', since: 0, line: 'Oi! Eu sou o Clawd. Vou ficar por aqui com você.' }, 'feliz', 'usp').length).toBeLessThan(131_072)
  }
})
