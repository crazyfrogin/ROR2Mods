# Muscle Memory

Muscle Memory is a code-only Risk of Rain 2 mod that adds run-scoped, logarithmic skill-slot mastery.

## Core behavior
- Tracks hidden proficiency for Primary/Secondary/Utility/Special slots.
- Converts proficiency to uncapped level with `floor(log2(1 + P / K))`.
- Applies tiny slot-based bonuses with optional Cold Start penalties (enabled by default).
- Uses host-authoritative tracking with replicated slot levels for multiplayer consistency.