# WarfrontDirector

WarfrontDirector is a server-authoritative game mode layer for Risk of Rain 2 that turns stages into enemy-led operations.

## V1 + V2 features
- Contested teleporter charge with rollback caps, immunity windows, and breach escalation.
- Role-driven assault identity: contesters, peelers, flankers, artillery, hunters, and anchors.
- Spawned enemies now receive role-aware runtime steering and doctrine/role/event buff packages.
- Expanded stage operations roll from a 12-warning / 8-anomaly roster.
- Enemy command archetypes: Relay Commander, Forge Commander, Siren Commander, Cache Commander.
- Command elites use curated elite affix visuals for readability and are kill-only (no interactable shrine sabotage flow).
- Commander rewards are base-by-type and scale by stage, difficulty, and alive player count.
- Commander death triggers one of: retaliation micro-wave, doctrine pivot, breach deployment, or immediate credit dump.
- Multiplayer fairness systems:
  - anti-lone-wolf objective pressure
  - short revive mercy windows
- Phase 3 adaptive campaign hooks:
  - stage-to-stage adaptation signals track what pressure patterns hurt the team most
  - doctrine profile generator selects each stage front (Balanced, Swarm, Artillery, Hunter, Siege, Disruption)
  - doctrine selection is bounded/decayed to avoid hard-locking one pattern every stage
- Compact HUD overlay with phase/intensity/contest plus role/fairness readouts, resized and positioned below money/lunar UI.

## Notes
- Warfront is always-on when enabled in config.
- Uses only existing in-game enemies, elite visuals, and buff icons.