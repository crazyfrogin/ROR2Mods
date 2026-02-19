# Muscle Memory

Muscle Memory is a code-only Risk of Rain 2 mod that adds run-scoped, logarithmic skill-slot mastery.

## Core Behavior
- Tracks hidden proficiency for Primary / Secondary / Utility / Special slots.
- Converts proficiency to uncapped level via `floor(log2(1 + P / K))`.
- Applies slot-based bonuses with optional Cold Start penalties (enabled by default).
- Host-authoritative tracking with replicated slot levels for multiplayer consistency.
- Late-joining clients receive a full snapshot immediately on connect.

## Multi-Tier Milestones
Each skill slot has three milestone tiers that unlock at configurable levels:

| Slot | Tier 1 (Lv 5) | Tier 2 (Lv 10) | Tier 3 (Lv 15) |
|------|---------------|-----------------|-----------------|
| **Primary** | Bonus crit chance | Bleed on hit | Attack speed bonus |
| **Secondary** | Bonus CDR | Kills refund a stock | Further CDR |
| **Utility** | Enhanced flow speed | Armor during flow | Extended flow duration |
| **Special** | Bonus barrier | Special refunds other cooldowns | Greater barrier |

## HUD
- Color-coded level numbers above each skill icon (Gray → White → Green → Blue → Purple).
- Progress bar showing proficiency toward the next level.

## Optional Systems
- **Cold Start**: Penalty for slots still at level 0.
- **Proficiency Decay**: Idle slots slowly lose proficiency (off by default).
- **Survivor Scaling**: Global multiplier to tune proficiency gain rates.
- **Chat Throttle**: Option to only broadcast milestone unlocks instead of every level-up.