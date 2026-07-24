---
name: skycofl-chat-eval
description: Run a deterministic three-question benchmark against the SkyCofl AI chat endpoint and check answers for required commands, links, distinctions, and supporting details. Use after changing SkyCofl guides, knowledge indexing, retrieval, prompts, or chat models; when investigating a factual chat regression; or before promoting a knowledge refresh.
---

# SkyCofl Chat Evaluation

Resolve `scripts/run-eval.mjs` relative to this `SKILL.md`, then run the API integration benchmark from the skill directory:

```bash
node scripts/run-eval.mjs
```

The runner sends three independent questions about Bazaar commands, Forge flips, and filter-group logic. It prints every answer, checks required facts, and exits nonzero when an answer is incomplete or repeats the known Bazaar tracking regression.

The production endpoint is the default. Override it for a local, port-forwarded, or staged API:

```bash
SKYCOFL_AI_ENDPOINT=http://localhost:5000/api/data/ai node scripts/run-eval.mjs
```

Set `SKYCOFL_AI_TOKEN` when authenticated evaluation capacity is available. Never print the token. Anonymous production runs consume all three daily anonymous messages, so avoid rerunning them without a token or waiting for the quota reset.

Use `--json` for machine-readable output:

```bash
node scripts/run-eval.mjs --json
```

For a failure:

1. Read the failed check, answer, trace ID, and HTTP status.
2. Compare the claim with the authoritative feature wiki and mod-command implementation.
3. Remove conflicting indexed wording instead of only adding another duplicate fact.
4. Deploy the source change and complete the knowledge refresh.
5. Rerun the benchmark against the refreshed environment.

Do not weaken an assertion merely to accept a plausible answer. Update a check only when the documented product behavior intentionally changes.
