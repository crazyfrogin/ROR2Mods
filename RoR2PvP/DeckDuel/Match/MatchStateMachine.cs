using System;
using System.Collections.Generic;
using DeckDuel.Models;
using DeckDuel.Networking;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace DeckDuel.Match
{
    /// <summary>
    /// FFA Stock Ring Duel — Last player standing wins a game, Best of N games.
    /// Tiebreak on equal stocks at time-out: 1-stock, 1:00 game.
    /// </summary>
    public class MatchStateMachine : IDisposable
    {
        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
        public int GameNumber { get; private set; }
        public bool IsSoloMode { get; private set; }
        public bool IsTiebreak { get; private set; }

        // N-player state
        private readonly List<CharacterMaster> _players = new List<CharacterMaster>();
        private readonly List<Deck> _decks = new List<Deck>();
        private int[] _scores = System.Array.Empty<int>();
        private int[] _stocks = System.Array.Empty<int>();
        private readonly HashSet<int> _eliminated = new HashSet<int>();
        private int _expectedPlayerCount;

        private RoundTimer _timer;
        private int _decksReceived;
        private bool _gameEndHandled;
        private float _gameEndDelay;
        private const float GameEndDelayDuration = 3f;

        // Match-end: show winner announcement then return to lobby
        private bool _matchEndHandled;
        private float _matchEndDelay;
        private const float MatchEndDelayDuration = 7f;
        private bool _stageHooked;
        private readonly List<Deck> _pendingDecks = new List<Deck>();
        private bool _pendingMatchStart;

        // Respawn queue: (player, delay remaining)
        private readonly List<(CharacterMaster master, int playerIndex, float delay)> _respawnQueue
            = new List<(CharacterMaster, int, float)>();
        private const float RespawnDelay = 1.5f;

        // Enemy position indicators (boss-style arrows that track through walls)
        private static GameObject _positionIndicatorPrefab;
        private static bool _prefabLoadAttempted;
        private readonly List<GameObject> _enemyIndicators = new List<GameObject>();

        public RoundTimer Timer => _timer;
        public int[] Scores => _scores;
        public int[] Stocks => _stocks;
        public int PlayerCount => _players.Count;

        public MatchStateMachine()
        {
            _timer = new RoundTimer();
            _timer.OnWarmupEnd += HandleWarmupEnd;
            _timer.OnRingPhaseChanged += HandleRingPhaseChanged;
            _timer.OnGameTimeExpired += HandleGameTimeExpired;
            _timer.OnDealCard += HandleDealCard;

            On.RoR2.GlobalEventManager.OnPlayerCharacterDeath += OnPlayerDeath;
            On.RoR2.CharacterBody.OnDeathStart += OnAnyBodyDeath;
            Run.onRunStartGlobal += OnRunStart;
            Stage.onStageStartGlobal += OnStageStart;
            _stageHooked = true;
        }

        // === Run / Stage Hooks ===

        private void OnRunStart(Run run)
        {
            Log.Info($">>> OnRunStart fired. NetworkServer.active={NetworkServer.active}");
            if (!NetworkServer.active) return;

            // Detect player count and solo mode
            int humanCount = PlayerCharacterMasterController.instances.Count;
            IsSoloMode = humanCount <= 1 && DeckDuelPlugin.Cfg.EnableAIOpponent.Value;
            _expectedPlayerCount = IsSoloMode ? 2 : humanCount;

            Log.Info($">>> OnRunStart: humanCount={humanCount}, EnableAI={DeckDuelPlugin.Cfg.EnableAIOpponent.Value}, IsSoloMode={IsSoloMode}, expectedPlayers={_expectedPlayerCount}");

            // Enable duel mode to block spawns from the very start
            DeckDuelPlugin.Instance.PvPSetup.EnableDuelMode();
            StartDeckBuilding();

            // Process any decks submitted during character select (Lobby phase)
            if (_pendingDecks.Count > 0)
            {
                Log.Info($"Processing {_pendingDecks.Count} pending deck(s) from character select...");
                var pending = new List<Deck>(_pendingDecks);
                _pendingDecks.Clear();
                foreach (var deck in pending)
                    OnDeckReceived(deck);
            }
        }

        private void OnStageStart(Stage stage)
        {
            Log.Info($">>> OnStageStart fired. Phase={Phase}, NetworkServer.active={NetworkServer.active}");
            if (!NetworkServer.active) return;

            // Re-enable duel mode each stage (in case it was cleared)
            if (Phase == MatchPhase.DeckBuilding || Phase == MatchPhase.Lobby)
            {
                DeckDuelPlugin.Instance.PvPSetup.EnableDuelMode();
            }
        }

        // === Public API ===

        public void StartDeckBuilding()
        {
            if (!NetworkServer.active) return;

            Phase = MatchPhase.DeckBuilding;
            _decks.Clear();
            _decksReceived = 0;
            _scores = new int[_expectedPlayerCount];
            GameNumber = 0;
            IsTiebreak = false;

            BroadcastState();
            Log.Info($"Deck building phase started. Waiting for {_expectedPlayerCount} deck(s)...");
        }

        public void OnDeckReceived(Deck deck)
        {
            Log.Info($">>> OnDeckReceived: Phase={Phase}, NetworkServer.active={NetworkServer.active}, IsSoloMode={IsSoloMode}, decksReceived={_decksReceived}, deckCards={deck?.Cards?.Count}");

            if (!NetworkServer.active) { Log.Warning("OnDeckReceived: Not server, ignoring."); return; }

            // Accept decks during Lobby (char select) — store as pending until run starts
            if (Phase == MatchPhase.Lobby)
            {
                _pendingDecks.Add(deck);
                Log.Info($"Deck received during Lobby — stored as pending ({_pendingDecks.Count} total). Cards={deck.Cards.Count}, Cost={deck.TotalCost}");
                new DeckResultMessage(true).Send(NetworkDestination.Clients);
                return;
            }

            if (Phase != MatchPhase.DeckBuilding) { Log.Warning($"OnDeckReceived: Phase is {Phase}, not DeckBuilding. Ignoring deck."); return; }

            _decksReceived++;
            _decks.Add(deck);
            Log.Info($"Player {_decksReceived} deck received. Cards={deck.Cards.Count}, Cost={deck.TotalCost}");

            // Send approval
            new DeckResultMessage(true).Send(NetworkDestination.Clients);

            int decksNeeded = IsSoloMode ? 1 : _expectedPlayerCount;

            if (IsSoloMode && _decksReceived >= 1)
            {
                Log.Info("Solo mode: generating AI deck and waiting for player spawn...");
                var aiOpponent = DeckDuelPlugin.Instance.AIOpponent;
                _decks.Add(aiOpponent.GenerateRandomDeck());
                _pendingMatchStart = true;
            }
            else if (_decksReceived >= decksNeeded)
            {
                Log.Info($"All {decksNeeded} decks received, waiting for player spawn...");
                _pendingMatchStart = true;
            }
            else
            {
                Log.Info($"Waiting for more decks. Have {_decksReceived}, need {decksNeeded}.");
            }
        }

        public void OnMatchStateReceived(MatchPhase phase, int gameNumber, float timer)
        {
            // Client-side state sync
            if (NetworkServer.active) return;
            Phase = phase;
            GameNumber = gameNumber;
        }

        public void Tick(float deltaTime)
        {
            if (!NetworkServer.active) return;

            // Poll for player bodies before starting the game
            if (_pendingMatchStart)
            {
                if (TryBeginGame())
                {
                    _pendingMatchStart = false;
                }
            }

            _timer.Tick(deltaTime);

            // Process respawn queue
            ProcessRespawnQueue(deltaTime);

            // Handle game end delay (show results briefly before next game)
            if (_gameEndHandled)
            {
                _gameEndDelay -= deltaTime;
                if (_gameEndDelay <= 0f)
                {
                    _gameEndHandled = false;
                    CheckMatchEnd();
                }
            }

            // Handle match end delay (return to character select after announcement)
            if (_matchEndHandled)
            {
                _matchEndDelay -= deltaTime;
                if (_matchEndDelay <= 0f)
                {
                    _matchEndHandled = false;
                    ReturnToCharacterSelect();
                }
            }
        }

        public void Dispose()
        {
            On.RoR2.GlobalEventManager.OnPlayerCharacterDeath -= OnPlayerDeath;
            On.RoR2.CharacterBody.OnDeathStart -= OnAnyBodyDeath;
            if (_stageHooked)
            {
                Run.onRunStartGlobal -= OnRunStart;
                Stage.onStageStartGlobal -= OnStageStart;
                _stageHooked = false;
            }
            _timer.OnWarmupEnd -= HandleWarmupEnd;
            _timer.OnRingPhaseChanged -= HandleRingPhaseChanged;
            _timer.OnGameTimeExpired -= HandleGameTimeExpired;
            _timer.OnDealCard -= HandleDealCard;
        }

        // === Game Flow (Stock Ring Duel v1) ===

        private bool TryBeginGame()
        {
            // Check that human player(s) have spawned with bodies
            var duelists = DeckDuelPlugin.Instance.PvPSetup.GetDuelists();
            if (duelists.Count < 1) return false;

            // All human players must have alive bodies
            foreach (var master in duelists)
            {
                var body = master?.GetBody();
                if (body == null || !body.healthComponent.alive) return false;
            }

            if (!IsSoloMode && duelists.Count < _expectedPlayerCount) return false;

            Log.Info("TryBeginGame: All players have bodies, starting game.");
            AssignPlayers();
            StartGame();
            return true;
        }

        private void AssignPlayers()
        {
            _players.Clear();
            var duelists = DeckDuelPlugin.Instance.PvPSetup.GetDuelists();
            Log.Info($">>> AssignPlayers: duelists.Count={duelists.Count}, IsSoloMode={IsSoloMode}");

            if (duelists.Count < 1)
            {
                Log.Error("AssignPlayers: No human players found!");
                return;
            }

            // Add all human players
            for (int i = 0; i < duelists.Count; i++)
            {
                _players.Add(duelists[i]);
                Log.Info($"AssignPlayers: Player {i} = {duelists[i].name}, body={duelists[i].GetBody()?.name ?? "NULL"}");
            }

            // In solo mode, add AI as the second player
            if (IsSoloMode)
            {
                var aiOpponent = DeckDuelPlugin.Instance.AIOpponent;
                Log.Info($"AssignPlayers: AI spawned already? {aiOpponent.IsSpawned}");
                if (!aiOpponent.IsSpawned)
                {
                    aiOpponent.SpawnAI(_players[0]);
                }
                var aiMaster = aiOpponent.AIMaster;
                if (aiMaster == null)
                {
                    Log.Error("AssignPlayers: AIOpponent.AIMaster is NULL after SpawnAI!");
                    return;
                }
                _players.Add(aiMaster);
                Log.Info($"AssignPlayers: Player {_players.Count - 1} (AI) = {aiMaster.name}");
            }

            _expectedPlayerCount = _players.Count;
            if (_scores.Length != _expectedPlayerCount)
                _scores = new int[_expectedPlayerCount];

            Log.Info($"AssignPlayers: Total {_players.Count} players assigned.");
        }

        private void StartGame(bool tiebreak = false)
        {
            // Validate all players
            for (int i = 0; i < _players.Count; i++)
            {
                bool valid = _players[i] != null && _players[i];
                if (!valid)
                {
                    Log.Error($"StartGame: ABORTING — player {i} is null/destroyed.");
                    return;
                }
            }
            Log.Info($">>> StartGame: {_players.Count} players, {_decks.Count} decks, tiebreak={tiebreak}");

            GameNumber++;
            IsTiebreak = tiebreak;
            Phase = tiebreak ? MatchPhase.Tiebreak : MatchPhase.Active;
            _gameEndHandled = false;
            _respawnQueue.Clear();
            _eliminated.Clear();

            // Initialize stocks for all players
            int stockCount = tiebreak
                ? DeckDuelPlugin.Cfg.TiebreakStocks.Value
                : DeckDuelPlugin.Cfg.StocksPerGame.Value;
            _stocks = new int[_players.Count];
            for (int i = 0; i < _stocks.Length; i++)
                _stocks[i] = stockCount;

            var pvp = DeckDuelPlugin.Instance.PvPSetup;
            var arena = DeckDuelPlugin.Instance.ArenaController;
            var dealer = DeckDuelPlugin.Instance.CardDealer;

            // Activate PvP hooks
            pvp.Activate();

            // Setup arena
            Vector3 center = arena.FindArenaCenter();
            arena.StartArena(center);

            // Respawn all players if dead, then teleport to arena
            for (int i = 0; i < _players.Count; i++)
            {
                RespawnIfDead(_players[i]);
                var body = _players[i].GetBody();
                if (body != null) arena.TeleportPlayerToArena(body, i, _players.Count);
            }

            // Assign FFA teams AFTER respawn+teleport so we set teams on actual current bodies
            pvp.AssignFFATeams(_players);

            // Update AI target after teleporting
            if (IsSoloMode && _players.Count >= 2)
            {
                DeckDuelPlugin.Instance.AIOpponent?.UpdateAITarget(_players[0]);
            }

            // Setup decks in card dealer (skip for tiebreak — items persist)
            if (!tiebreak)
            {
                dealer.CleanupRound();
                for (int i = 0; i < _players.Count && i < _decks.Count; i++)
                {
                    if (_decks[i] != null) dealer.SetupDeck(_players[i], _decks[i]);
                }

                // Deal starting cards to all players
                int startCards = DeckDuelPlugin.Cfg.StartingCards.Value;
                for (int i = 0; i < _players.Count; i++)
                    dealer.DealCards(_players[i], startCards);
            }

            // Start timer
            _timer.StartRound(tiebreak);

            // Heal all players to full
            for (int i = 0; i < _players.Count; i++)
                HealToFull(_players[i]);

            // Create enemy position indicators so players can see each other
            CreateEnemyIndicators();

            BroadcastState();
            BroadcastStocks();
            new MatchScoreMessage(_scores).Send(NetworkDestination.Clients);

            string gameType = tiebreak ? "TIEBREAK" : $"Game {GameNumber}";
            Log.Info($"{gameType} started. {_players.Count} players, {stockCount} stocks each.");
        }

        private void HandleWarmupEnd()
        {
            Phase = IsTiebreak ? MatchPhase.Tiebreak : MatchPhase.Active;

            // Remove spawn protection from all players
            for (int i = 0; i < _players.Count; i++)
                ApplySpawnProtection(_players[i], false);

            // Show enemy position indicators
            CreateEnemyIndicators();

            BroadcastState();
            Log.Info("Warmup ended — FIGHT!");
        }

        private void HandleRingPhaseChanged(RingPhase newPhase)
        {
            DeckDuelPlugin.Instance.ArenaController.SetRingPhase(newPhase);
            BroadcastState();
            Log.Info($"Ring phase changed to {newPhase}");
        }

        private void HandleGameTimeExpired()
        {
            // Timer ran out — determine winner by most stocks remaining
            int winner = DetermineWinnerByStocks();

            if (winner == -1)
            {
                // Stocks tied — start tiebreak game
                Log.Info("Time expired with tied stocks — starting tiebreak!");
                _timer.StopRound();
                DeckDuelPlugin.Instance.ArenaController.StopArena();

                // Re-assign and start tiebreak
                AssignPlayers();
                StartGame(tiebreak: true);
            }
            else
            {
                EndGame(winner);
            }
        }

        private void HandleDealCard()
        {
            var dealer = DeckDuelPlugin.Instance.CardDealer;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_eliminated.Contains(i)) continue;
                var p = _players[i];
                if (p != null && p) dealer.DealCards(p, 1);
            }
        }

        // === Death Handling (Stock System) ===

        private void OnPlayerDeath(
            On.RoR2.GlobalEventManager.orig_OnPlayerCharacterDeath orig,
            GlobalEventManager self,
            DamageReport damageReport,
            NetworkUser victimNetworkUser)
        {
            orig(self, damageReport, victimNetworkUser);

            if (!NetworkServer.active) return;
            if (!IsActiveGamePhase()) return;

            // Use victimNetworkUser.master — after orig() runs RoR2's death handling,
            // damageReport.victimBody may be a destroyed Unity Object whose .master returns null.
            var victimMaster = victimNetworkUser?.master;
            if (victimMaster == null)
            {
                Log.Warning("OnPlayerDeath: victimMaster is null (NetworkUser.master failed). Trying damageReport fallback.");
                victimMaster = damageReport?.victimBody?.master;
            }
            if (victimMaster == null)
            {
                Log.Warning("OnPlayerDeath: Could not resolve victimMaster — stock loss skipped!");
                return;
            }

            HandleStockLoss(victimMaster);
        }

        private void OnAnyBodyDeath(On.RoR2.CharacterBody.orig_OnDeathStart orig, CharacterBody self)
        {
            orig(self);

            if (!NetworkServer.active) return;
            if (!IsActiveGamePhase()) return;
            if (!IsSoloMode) return;

            var master = self.master;
            if (master == null) return;

            // Only handle AI deaths here — human player deaths are already caught by OnPlayerDeath
            int idx = _players.IndexOf(master);
            if (idx < 0) return;
            // Skip if this player has a PlayerCharacterMasterController (handled by OnPlayerDeath)
            if (master.GetComponent<PlayerCharacterMasterController>() != null) return;

            HandleStockLoss(master);
        }

        private void HandleStockLoss(CharacterMaster victimMaster)
        {
            int idx = _players.IndexOf(victimMaster);
            if (idx < 0)
            {
                Log.Warning("HandleStockLoss: victim not found in player list.");
                return;
            }
            if (_eliminated.Contains(idx)) return; // already out

            _stocks[idx]--;
            Log.Info($"Player {idx} lost a stock. Remaining: {_stocks[idx]}");
            BroadcastStocks();

            if (_stocks[idx] <= 0)
            {
                _eliminated.Add(idx);
                Log.Info($"Player {idx} ELIMINATED! ({_eliminated.Count}/{_players.Count} eliminated)");

                // Check if only one player remains
                int alive = 0;
                int lastAlive = -1;
                for (int i = 0; i < _players.Count; i++)
                {
                    if (!_eliminated.Contains(i)) { alive++; lastAlive = i; }
                }

                if (alive <= 1)
                {
                    EndGame(lastAlive); // last player standing wins
                }
            }
            else
            {
                QueueRespawn(victimMaster, idx);
            }
        }

        private void QueueRespawn(CharacterMaster master, int playerIndex)
        {
            _respawnQueue.Add((master, playerIndex, RespawnDelay));
            Log.Info($"Queued respawn for player {playerIndex} in {RespawnDelay}s");
        }

        private void ProcessRespawnQueue(float deltaTime)
        {
            if (_respawnQueue.Count == 0) return;

            for (int i = _respawnQueue.Count - 1; i >= 0; i--)
            {
                var entry = _respawnQueue[i];
                float newDelay = entry.delay - deltaTime;

                if (newDelay <= 0f)
                {
                    _respawnQueue.RemoveAt(i);
                    PerformRespawn(entry.master, entry.playerIndex);
                }
                else
                {
                    _respawnQueue[i] = (entry.master, entry.playerIndex, newDelay);
                }
            }
        }

        private void PerformRespawn(CharacterMaster master, int playerIndex)
        {
            if (master == null || !master)
            {
                Log.Warning($"PerformRespawn: master is null/destroyed for player {playerIndex} — respawn aborted!");
                return;
            }
            if (!IsActiveGamePhase())
            {
                Log.Warning($"PerformRespawn: Phase={Phase} is not active — respawn aborted for player {playerIndex}!");
                return;
            }

            // Respawn the player
            var arena = DeckDuelPlugin.Instance.ArenaController;
            Vector3 spawnPos = arena.GetSpawnPosition(playerIndex, _players.Count);
            master.Respawn(spawnPos, Quaternion.identity);

            var body = master.GetBody();
            if (body != null)
            {
                // Heal to full
                body.healthComponent.Heal(body.healthComponent.fullHealth, default);
                if (body.healthComponent.shield < body.healthComponent.fullShield)
                    body.healthComponent.shield = body.healthComponent.fullShield;

                // Brief respawn invulnerability
                float invulnDuration = DeckDuelPlugin.Cfg.RespawnInvulnDuration.Value;
                body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, invulnDuration);

                // Refresh enemy position indicators after respawn (new body = new target)
                CreateEnemyIndicators();
            }

            // Re-assign FFA teams after respawn
            var pvp = DeckDuelPlugin.Instance.PvPSetup;
            pvp.AssignFFATeams(_players);

            // Update AI target if needed
            if (IsSoloMode && _players.Count >= 2)
                DeckDuelPlugin.Instance.AIOpponent?.UpdateAITarget(_players[0]);

            Log.Info($"Player {playerIndex} respawned at {spawnPos} with invuln for {DeckDuelPlugin.Cfg.RespawnInvulnDuration.Value}s");
        }

        // === Game End ===

        private void EndGame(int winner)
        {
            if (_gameEndHandled) return;
            _gameEndHandled = true;

            _timer.StopRound();
            DeckDuelPlugin.Instance.ArenaController.StopArena();
            _respawnQueue.Clear();
            DestroyEnemyIndicators();

            Phase = MatchPhase.RoundEnd;

            if (winner >= 0 && winner < _scores.Length)
                _scores[winner]++;

            string winnerName = winner >= 0 ? $"Player {winner + 1}" : "Draw";
            string gameType = IsTiebreak ? "Tiebreak" : $"Game {GameNumber}";
            Log.Info($"{gameType} ended. Winner: {winnerName}. Scores: {string.Join("-", _scores)}");

            new MatchScoreMessage(_scores).Send(NetworkDestination.Clients);
            BroadcastState();
            BroadcastStocks();

            _gameEndDelay = GameEndDelayDuration;
        }

        private void CheckMatchEnd()
        {
            int winsNeeded = (DeckDuelPlugin.Cfg.BestOf.Value / 2) + 1;

            // Check if any player has reached the wins threshold
            int matchWinner = -1;
            for (int i = 0; i < _scores.Length; i++)
            {
                if (_scores[i] >= winsNeeded)
                {
                    matchWinner = i;
                    break;
                }
            }

            if (matchWinner >= 0)
            {
                // Match over — keep duel mode active until ReturnToCharacterSelect
                Phase = MatchPhase.MatchEnd;
                DeckDuelPlugin.Instance.ArenaController.StopArena();
                DeckDuelPlugin.Instance.CardDealer.CleanupRound();

                if (IsSoloMode)
                {
                    DeckDuelPlugin.Instance.AIOpponent?.DespawnAI();
                }

                string winnerName = $"Player {matchWinner + 1}";
                Log.Info($"MATCH OVER! {winnerName} wins! Scores: {string.Join("-", _scores)}");

                // Announce winner to all players via chat
                AnnounceWinner(winnerName);

                BroadcastState();

                // Start countdown to return to character select
                _matchEndHandled = true;
                _matchEndDelay = MatchEndDelayDuration;
            }
            else
            {
                // Next game — re-assign players (references may be stale/destroyed)
                IsTiebreak = false;
                AssignPlayers();
                StartGame();
            }
        }

        // === Helpers ===

        private bool IsActiveGamePhase()
        {
            return Phase == MatchPhase.Warmup || Phase == MatchPhase.Active || Phase == MatchPhase.Tiebreak;
        }

        private int DetermineWinnerByStocks()
        {
            // Find the player with the most stocks remaining
            int bestIdx = -1;
            int bestStocks = -1;
            bool tied = false;

            for (int i = 0; i < _stocks.Length; i++)
            {
                if (_eliminated.Contains(i)) continue;
                if (_stocks[i] > bestStocks)
                {
                    bestStocks = _stocks[i];
                    bestIdx = i;
                    tied = false;
                }
                else if (_stocks[i] == bestStocks)
                {
                    tied = true;
                }
            }

            // If tied, return -1 to trigger tiebreak
            return tied ? -1 : bestIdx;
        }

        private void ApplySpawnProtection(CharacterMaster player, bool apply)
        {
            var body = player?.GetBody();
            if (body == null) return;

            if (apply)
            {
                body.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, DeckDuelPlugin.Cfg.WarmupDuration.Value);
            }
            else
            {
                body.ClearTimedBuffs(RoR2Content.Buffs.HiddenInvincibility);
            }
        }

        // === Enemy Position Indicators ===

        private void EnsurePositionIndicatorPrefab()
        {
            if (_prefabLoadAttempted) return;
            _prefabLoadAttempted = true;

            // Try multiple known paths for the boss position indicator prefab
            try
            {
                _positionIndicatorPrefab = LegacyResourcesAPI.Load<GameObject>(
                    "Prefabs/PositionIndicators/BossPositionIndicator");
            }
            catch { }

            if (_positionIndicatorPrefab == null)
            {
                try
                {
                    _positionIndicatorPrefab = UnityEngine.AddressableAssets.Addressables
                        .LoadAssetAsync<GameObject>("RoR2/Base/Common/BossPositionIndicator.prefab")
                        .WaitForCompletion();
                }
                catch { }
            }

            if (_positionIndicatorPrefab == null)
            {
                try
                {
                    _positionIndicatorPrefab = UnityEngine.AddressableAssets.Addressables
                        .LoadAssetAsync<GameObject>("RoR2/Base/Common/PositionIndicators/BossPositionIndicator.prefab")
                        .WaitForCompletion();
                }
                catch { }
            }

            Log.Info(_positionIndicatorPrefab != null
                ? $"Loaded PositionIndicator prefab: {_positionIndicatorPrefab.name}"
                : "WARNING: Could not load any PositionIndicator prefab — enemy markers disabled.");
        }

        private void CreateEnemyIndicators()
        {
            DestroyEnemyIndicators();
            EnsurePositionIndicatorPrefab();

            if (_positionIndicatorPrefab == null) return;

            // Create an indicator for every player so all players are visible through walls
            for (int i = 0; i < _players.Count; i++)
            {
                if (_eliminated.Contains(i)) continue;
                var body = _players[i]?.GetBody();
                if (body == null) continue;

                var indicator = UnityEngine.Object.Instantiate(_positionIndicatorPrefab);
                var pi = indicator.GetComponent<PositionIndicator>();
                if (pi != null)
                {
                    pi.targetTransform = body.coreTransform ?? body.transform;
                    pi.insideViewObject?.SetActive(true);
                    pi.outsideViewObject?.SetActive(true);
                }
                _enemyIndicators.Add(indicator);
                Log.Info($"Created position indicator tracking player {i} ({body.GetUserName()}).");
            }
        }

        private void DestroyEnemyIndicators()
        {
            foreach (var indicator in _enemyIndicators)
            {
                if (indicator != null)
                    UnityEngine.Object.Destroy(indicator);
            }
            _enemyIndicators.Clear();
        }

        private void HealToFull(CharacterMaster player)
        {
            var body = player?.GetBody();
            if (body == null) return;
            body.healthComponent.Heal(body.healthComponent.fullHealth, default);
            if (body.healthComponent.shield < body.healthComponent.fullShield)
                body.healthComponent.shield = body.healthComponent.fullShield;
        }

        private void RespawnIfDead(CharacterMaster player)
        {
            if (player == null) return;
            var body = player.GetBody();
            if (body == null || !body.healthComponent.alive)
            {
                player.Respawn(player.transform.position, player.transform.rotation);
            }
        }

        private void BroadcastState()
        {
            if (!NetworkServer.active) return;
            float displayTimer = _timer.GetDisplayTimer();
            new MatchStateMessage(Phase, GameNumber, displayTimer).Send(NetworkDestination.Clients);
        }

        private void BroadcastStocks()
        {
            if (!NetworkServer.active) return;
            new StockSyncMessage(_stocks).Send(NetworkDestination.Clients);
        }

        private void AnnounceWinner(string winnerName)
        {
            string scoreStr = string.Join("–", _scores);
            string msg = $"<style=cWorldEvent><sprite name=\"Skull\" tint=1> MATCH OVER — {winnerName} wins! ({scoreStr}) <sprite name=\"Skull\" tint=1></style>";
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = msg });

            // Also send a secondary line after a beat so it stands out
            string returnMsg = "<style=cSub>Returning to character select...</style>";
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = returnMsg });
        }

        private void ReturnToCharacterSelect()
        {
            if (!NetworkServer.active) return;

            Log.Info("Match ended — returning to character select.");

            // Reset match state so a fresh match can start on the next run
            Phase = MatchPhase.Lobby;
            GameNumber = 0;
            _scores = System.Array.Empty<int>();
            _stocks = System.Array.Empty<int>();
            _decksReceived = 0;
            _decks.Clear();
            _players.Clear();
            _eliminated.Clear();
            _pendingMatchStart = false;
            _pendingDecks.Clear();
            IsTiebreak = false;
            _respawnQueue.Clear();
            DestroyEnemyIndicators();

            // Deactivate PvP and disable duel mode BEFORE calling BeginGameOver,
            // otherwise our Run_BeginGameOver hook will block it
            DeckDuelPlugin.Instance.PvPSetup.Deactivate();
            DeckDuelPlugin.Instance.PvPSetup.DisableDuelMode();

            // Hide the match HUD
            DeckDuelPlugin.Instance.MatchHUD?.Hide();

            // End the run — BeginGameOver will now pass through since duel mode is off
            if (Run.instance != null)
            {
                Run.instance.BeginGameOver(RoR2Content.GameEndings.StandardLoss);
            }
        }
    }
}
