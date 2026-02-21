using System;

namespace DeckDuel.Match
{
    public class RoundTimer
    {
        public float ElapsedTime { get; private set; }
        public float GameDuration { get; private set; }
        public float WarmupDuration { get; private set; }
        public float CardInterval { get; private set; }
        public int StartingCards { get; private set; }

        // Ring phase boundaries (game-clock seconds, excluding warmup)
        public float PhaseAEnd { get; private set; }
        public float PhaseBEnd { get; private set; }

        public bool IsRunning { get; private set; }
        public bool IsTiebreak { get; private set; }
        public MatchPhase CurrentPhase { get; private set; }
        public RingPhase CurrentRingPhase { get; private set; }

        // Game clock = time elapsed since warmup ended (the actual fight timer)
        public float GameClock { get; private set; }

        private float _lastCardDealTime;
        private int _cardsDealedThisRound;

        public event Action OnWarmupEnd;
        public event Action<RingPhase> OnRingPhaseChanged;
        public event Action OnGameTimeExpired;
        public event Action OnDealCard;

        public void StartRound(bool tiebreak = false)
        {
            var cfg = DeckDuelPlugin.Cfg;
            IsTiebreak = tiebreak;
            GameDuration = tiebreak ? cfg.TiebreakDuration.Value : cfg.GameDuration.Value;
            WarmupDuration = cfg.WarmupDuration.Value;
            CardInterval = cfg.CardInterval.Value;
            StartingCards = cfg.StartingCards.Value;

            // Scale ring phase boundaries proportionally for tiebreaks
            float scale = GameDuration / cfg.GameDuration.Value;
            PhaseAEnd = cfg.PhaseAEnd.Value * scale;
            PhaseBEnd = cfg.PhaseBEnd.Value * scale;

            ElapsedTime = 0f;
            GameClock = 0f;
            WarmupDuration = 0f;
            _lastCardDealTime = 0f;
            _cardsDealedThisRound = 0;
            IsRunning = true;
            CurrentPhase = tiebreak ? MatchPhase.Tiebreak : MatchPhase.Active;
            CurrentRingPhase = RingPhase.A;
        }

        public void StopRound()
        {
            IsRunning = false;
        }

        public void Tick(float deltaTime)
        {
            if (!IsRunning) return;

            ElapsedTime += deltaTime;

            // Warmup → Active transition
            if (CurrentPhase == MatchPhase.Warmup && ElapsedTime >= WarmupDuration)
            {
                CurrentPhase = IsTiebreak ? MatchPhase.Tiebreak : MatchPhase.Active;
                _lastCardDealTime = 0f;
                OnWarmupEnd?.Invoke();
            }

            // Advance game clock only after warmup
            if (CurrentPhase == MatchPhase.Active || CurrentPhase == MatchPhase.Tiebreak)
            {
                GameClock += deltaTime;

                // Ring phase transitions
                UpdateRingPhase();

                // Game time expired
                if (GameClock >= GameDuration)
                {
                    IsRunning = false;
                    OnGameTimeExpired?.Invoke();
                    return;
                }

                // Card dealing timer
                if (GameClock - _lastCardDealTime >= CardInterval)
                {
                    _lastCardDealTime = GameClock;
                    _cardsDealedThisRound++;
                    OnDealCard?.Invoke();
                }
            }
        }

        private void UpdateRingPhase()
        {
            RingPhase newPhase;
            if (GameClock < PhaseAEnd)
                newPhase = RingPhase.A;
            else if (GameClock < PhaseBEnd)
                newPhase = RingPhase.B;
            else
                newPhase = RingPhase.C;

            if (newPhase != CurrentRingPhase)
            {
                CurrentRingPhase = newPhase;
                OnRingPhaseChanged?.Invoke(newPhase);
            }
        }

        public float GetDisplayTimer()
        {
            return Math.Max(0f, GameDuration - GameClock);
        }

        public float GetGameClock()
        {
            return GameClock;
        }

        public float GetPhaseProgress()
        {
            switch (CurrentRingPhase)
            {
                case RingPhase.A:
                    return PhaseAEnd > 0f ? GameClock / PhaseAEnd : 0f;
                case RingPhase.B:
                    float bLen = PhaseBEnd - PhaseAEnd;
                    return bLen > 0f ? (GameClock - PhaseAEnd) / bLen : 0f;
                case RingPhase.C:
                    float cLen = GameDuration - PhaseBEnd;
                    return cLen > 0f ? (GameClock - PhaseBEnd) / cLen : 0f;
                default:
                    return 0f;
            }
        }
    }
}
