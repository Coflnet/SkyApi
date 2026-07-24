#!/usr/bin/env node

const endpoint = process.env.SKYCOFL_AI_ENDPOINT || 'https://sky.coflnet.com/api/data/ai'
const token = process.env.SKYCOFL_AI_TOKEN
const jsonOutput = process.argv.includes('--json')

const cases = [
    {
        id: 'bazaar-commands',
        question:
            'How do I find current Bazaar flips in the SkyCofl mod, and how do I view tracked profit from completed Bazaar flips? Include the exact commands and the full guide link.',
        checks: [
            check('names /cofl bazaar for current flips', answer => near(answer, /\/cofl\s+bazaar\b/i, /current|live|find|top|opportunit/i)),
            check('names the history subcommand for tracked profit', answer =>
                near(answer, /\/cofl\s+(?:bazaar\s+history|bz\s+(?:h|history))\b/i, /track|profit|history|completed/i)
            ),
            check('links the Bazaar flipping guide', answer => /(?:https:\/\/sky\.coflnet\.com)?\/wiki\/bazaar-flips\b/i.test(answer)),
            check('does not claim the bare Bazaar command opens tracking', answer => !claimsBareBazaarTracks(answer))
        ]
    },
    {
        id: 'forge-discovery',
        question:
            'Where can I find SkyCofl Forge flips on the website and in the mod? Include the exact in-game command, full guide link, and what makes the in-game results profile-aware.',
        checks: [
            check('links the Forge page', answer => /(?:https:\/\/sky\.coflnet\.com)?\/forge\b/i.test(answer)),
            check('names /cofl forge', answer => /\/cofl\s+forge\b/i.test(answer)),
            check('links the Forge guide', answer => /(?:https:\/\/sky\.coflnet\.com)?\/wiki\/forge-flips\b/i.test(answer)),
            check('mentions profile-aware inputs', answer => countMatches(answer, [/\bpurse\b/i, /heart of the mountain|\bhotm\b/i, /quick forge/i, /unlock/i]) >= 2)
        ]
    },
    {
        id: 'filter-group-logic',
        question:
            'How do SkyCofl flip filter groups combine filters with AND and OR logic, and where is the complete filter guide? Include a concrete filter example.',
        checks: [
            check('states AND logic within a group', answer => /\band\b.{0,80}\b(?:same|one|within|inside)\b.{0,40}\bgroup|\bgroup.{0,80}\band\b/is.test(answer)),
            check('states OR logic between groups', answer => /\bor\b.{0,80}\b(?:multiple|different|between|across)\b.{0,40}\bgroups?|\bgroups?.{0,80}\bor\b/is.test(answer)),
            check('links the filter guide', answer => /(?:https:\/\/sky\.coflnet\.com)?\/wiki\/filters\b/i.test(answer)),
            check('includes a concrete filter example', answer => /\bprofit\b|\bvolume\b|\bmaxcost\b|\bminprofit\b/i.test(answer))
        ]
    }
]

function check(label, test) {
    return { label, test }
}

function near(answer, command, purpose) {
    const text = answer.replace(/[`*_]/g, '')
    for (const match of text.matchAll(new RegExp(command.source, `${command.flags.replace('g', '')}g`))) {
        const start = Math.max(0, match.index - 120)
        const end = Math.min(text.length, match.index + match[0].length + 180)
        if (purpose.test(text.slice(start, end))) return true
    }
    return false
}

function claimsBareBazaarTracks(answer) {
    const clauses = answer
        .replace(/[`*_]/g, '')
        .split(/[.;\n]+/)
        .map(value => value.trim())
        .filter(Boolean)

    return clauses.some(clause => {
        const mentionsTracking = /\btrack(?:ed|ing)?\b|\bhistory\b|\bcompleted\b|\bprofit\b/i.test(clause)
        const hasBareCommand = /\/cofl\s+(?:bazaar|bz)(?!\s+(?:history|h|list|l)\b)/i.test(clause)
        const hasTrackingSubcommand = /\/cofl\s+(?:bazaar\s+(?:history|list)|bz\s+(?:h|history|l|list))\b/i.test(clause)
        const explicitlyNegates = /\b(?:does\s+not|doesn't|is\s+not|isn't|not\s+used\s+to)\b/i.test(clause)
        return mentionsTracking && hasBareCommand && !hasTrackingSubcommand && !explicitlyNegates
    })
}

function countMatches(answer, patterns) {
    return patterns.filter(pattern => pattern.test(answer)).length
}

async function run(testCase) {
    const headers = { 'Content-Type': 'application/json', 'User-Agent': 'SkyCofl-Chat-Eval/1.0' }
    if (token) headers.Authorization = `Bearer ${token}`

    try {
        const response = await fetch(endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({ message: testCase.question, page: '/chat' }),
            signal: AbortSignal.timeout(120_000)
        })
        const body = await response.json().catch(() => ({}))
        const answer = body.answer ?? body.Answer ?? body.message ?? body.Message ?? ''
        const traceId = body.traceId ?? body.TraceId ?? response.headers.get('x-trace-id') ?? null
        const requiresBugReport = Boolean(body.requiresBugReport ?? body.RequiresBugReport)
        const checks = testCase.checks.map(expectation => ({
            label: expectation.label,
            passed: response.ok && !requiresBugReport && typeof answer === 'string' && expectation.test(answer)
        }))
        return {
            id: testCase.id,
            question: testCase.question,
            passed: response.ok && !requiresBugReport && checks.every(expectation => expectation.passed),
            status: response.status,
            traceId,
            requiresBugReport,
            checks,
            answer: typeof answer === 'string' ? answer : JSON.stringify(body)
        }
    } catch (error) {
        return {
            id: testCase.id,
            question: testCase.question,
            passed: false,
            status: null,
            traceId: null,
            requiresBugReport: false,
            checks: testCase.checks.map(expectation => ({ label: expectation.label, passed: false })),
            answer: error instanceof Error ? error.message : String(error)
        }
    }
}

const results = []
for (const testCase of cases) results.push(await run(testCase))

if (jsonOutput) {
    process.stdout.write(`${JSON.stringify({ endpoint, passed: results.every(result => result.passed), results }, null, 2)}\n`)
} else {
    for (const result of results) {
        console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.id} (HTTP ${result.status ?? 'error'}${result.traceId ? `, trace ${result.traceId}` : ''})`)
        for (const expectation of result.checks) console.log(`  ${expectation.passed ? '✓' : '✗'} ${expectation.label}`)
        if (result.requiresBugReport) console.log('  ✗ endpoint marked the answer as requiring a bug report')
        console.log(`\n${result.answer}\n`)
    }
    console.log(`${results.filter(result => result.passed).length}/${results.length} benchmark answers passed`)
}

if (results.some(result => !result.passed)) process.exitCode = 1
