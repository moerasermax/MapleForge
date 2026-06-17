# MapleForge Agent Entry

This repository is the active MapleForge server rewrite. Every new agent session must load the stable project rules before changing code or docs.

## Required First Reads

Read these in order before substantial work:

1. `docs/specs/session-invariants.md` — immutable north star, workflow laws, evidence rules.
2. `docs/devlog/任務歷程/README.md` — task journal discipline.
3. `docs/specs/conventions.md` — namespace and dependency boundaries.
4. `docs/design/重構架構設計書.md` — architecture intent.
5. `docs/devlog/任務追蹤.md` and `docs/devlog/進度日誌.md` — current state and narrative history.

For protocol, client, WZ, live-client, or packet tasks also read:

- `docs/specs/v113-protocol-spec.md`
- `docs/specs/test-strategy.md`
- `docs/design/封包擷取模式-設計.md`
- `docs/design/MapleForge方法論融合綱領.md`

## Non-Negotiable Rules

- Create or update a task journal file in `docs/devlog/任務歷程/` before any substantial task.
- Keep `Maple.Core` and `Maple.Application` free of v113 packet/opcode/byte-layout knowledge.
- Put v113 protocol details in `Maple.Adapters.V113`.
- Treat the old Java server as behavior oracle, not as architecture to copy.
- Treat the true v113 client and decrypted captures as the final protocol verifier.
- Do not promote server-to-client fixtures to golden truth unless they have verified ground truth; mark uncertain fixtures as unverified.
- Do not modify the old Java server, client binaries, WZ references, or sibling projects unless the user explicitly asks.
- Avoid OdinMS-style static global state; multi-instance safety is a core design pillar.
- Use targeted builds/tests. Avoid full-solution churn when a project-level test is enough.
- Update living docs when changing protocol, persistence, world/runtime behavior, tools, or workflow.

## Commit And Checkpoint Policy

The remote is a private backup. Commit and push per CLAUDE.md checkpoint discipline: proactively commit+push after each tested unit during long/auto work sessions (anti-crash). Also commit when the user explicitly asks. Never force-push or perform destructive git operations without explicit approval.

## Task Start Checklist

1. Check `git status --short`.
2. Read the relevant invariant/design/spec documents.
3. Create a task journal from `_範本.md`, fill the goal and DoD, and set status to `🚧 執行中`.
4. Make the smallest coherent change.
5. Run targeted verification.
6. Update task journal, progress log, and any affected living docs.
