namespace DeckDuel.Match
{
    public enum MatchPhase : byte
    {
        Lobby = 0,
        DeckBuilding = 1,
        Warmup = 2,
        Active = 3,
        SuddenDeath = 4,
        RoundEnd = 5,
        MatchEnd = 6,
        Tiebreak = 7
    }

    public enum RingPhase : byte
    {
        A = 0, // Large ring, no outside damage
        B = 1, // Shrinking, light outside damage
        C = 2  // Small ring, heavy outside damage
    }
}
