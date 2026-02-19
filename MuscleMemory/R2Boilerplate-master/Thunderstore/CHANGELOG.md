## 2.0.0

- **Multi-tier milestones**: 3 milestone tiers per skill slot (12 total perks).
  - Primary: crit chance → bleed → attack speed.
  - Secondary: CDR → kill stock refund → further CDR.
  - Utility: flow speed → armor during flow → extended flow duration.
  - Special: barrier → cooldown refund on other skills → greater barrier.
- **Color-coded HUD**: level numbers tinted Gray/White/Green/Blue/Purple by tier.
- **Progress bars**: visual proficiency-toward-next-level indicator per slot.
- **Proficiency decay**: optional slow decay for idle slots (off by default).
- **Survivor scaling**: global proficiency multiplier for per-profile tuning.
- **Chat throttle**: option to broadcast only milestone unlocks instead of every level-up.
- **Late-join support**: full snapshot sent to clients on connect.
- **Performance**: cached reflection, cached GUIStyle, direct log2 level calculation.
- **Architecture**: split monolithic plugin into Config, ProgressionManager, StatHooks, NetworkSync, SkillHud, MilestoneSystem.
- **Namespace renamed** from ExamplePlugin to MuscleMemory.
- **Fixed** player name in chat (now shows actual player name instead of master object name).
- **Networking**: changed message type to reduce collision risk with other mods.

## 1.0.0

- Initial Muscle Memory release.
- Run-scoped logarithmic mastery tracking for Primary/Secondary/Utility/Special slots.
- Tiny slot-based bonuses with cold-start penalties enabled by default.
- Host-authoritative progression with replicated slot levels for multiplayer consistency.