using System;
using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace DeckDuel.Match
{
    public class PvPSetup : IDisposable
    {
        // FFA: each player gets a unique team so everyone can damage everyone
        private static readonly TeamIndex[] FFATeams = {
            TeamIndex.Player,   // player 0
            TeamIndex.Monster,  // player 1
            TeamIndex.Neutral,  // player 2
            TeamIndex.Lunar,    // player 3
        };

        private readonly HashSet<TeamIndex> _activeTeams = new HashSet<TeamIndex>();
        private bool _isActive;
        private bool _duelModeActive;

        public bool IsDuelModeActive => _duelModeActive;

        public TeamIndex GetTeamForPlayer(int playerIndex)
        {
            if (playerIndex >= 0 && playerIndex < FFATeams.Length)
                return FFATeams[playerIndex];
            // Wrap around for >4 players (unlikely in RoR2 but safe)
            return FFATeams[playerIndex % FFATeams.Length];
        }

        public void Initialize()
        {
            // Hook spawn/item blocking early so they fire before SceneDirector runs
            On.RoR2.CombatDirector.AttemptSpawnOnTarget += CombatDirector_AttemptSpawnOnTarget;
            On.RoR2.SceneDirector.PopulateScene += SceneDirector_PopulateScene;
            On.RoR2.CharacterMaster.GiveMoney += CharacterMaster_GiveMoney;

            // Block game-over / defeat screen during duel
            On.RoR2.Run.BeginGameOver += Run_BeginGameOver;
            On.RoR2.UI.GameEndReportPanelController.Awake += GameEndReportPanel_Awake;

            Log.Info($"PvPSetup initialized (FFA mode, up to {FFATeams.Length} players).");
        }

        public void EnableDuelMode()
        {
            _duelModeActive = true;
            Log.Info("Duel mode enabled — spawns and interactables will be blocked.");
        }

        public void DisableDuelMode()
        {
            _duelModeActive = false;
            Log.Info("Duel mode disabled.");
        }

        public void Activate()
        {
            if (_isActive) return;
            _isActive = true;
            _duelModeActive = true;

            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;

            Log.Info("PvP hooks activated.");
        }

        public void Deactivate()
        {
            if (!_isActive) return;
            _isActive = false;

            On.RoR2.HealthComponent.TakeDamage -= HealthComponent_TakeDamage;

            Log.Info("PvP hooks deactivated.");
        }

        public void AssignFFATeams(List<CharacterMaster> players)
        {
            _activeTeams.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null) continue;
                var team = GetTeamForPlayer(i);
                player.teamIndex = team;
                var body = player.GetBody();
                if (body != null) body.teamComponent.teamIndex = team;
                _activeTeams.Add(team);
                Log.Info($"FFA team assigned: player {i} ({player.name}) → {team}");
            }
        }

        public List<CharacterMaster> GetDuelists()
        {
            var duelists = new List<CharacterMaster>();
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.master != null)
                    duelists.Add(pc.master);
            }
            return duelists;
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            // FFA: allow PvP damage between any two active FFA teams
            if (_isActive && damageInfo.attacker != null)
            {
                var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                if (attackerBody != null && self.body != null)
                {
                    var attackerTeam = attackerBody.teamComponent.teamIndex;
                    var victimTeam = self.body.teamComponent.teamIndex;

                    // If both are active FFA teams and different from each other, allow damage
                    bool isFFAAttack = attackerTeam != victimTeam
                                       && _activeTeams.Contains(attackerTeam)
                                       && _activeTeams.Contains(victimTeam);

                    if (isFFAAttack)
                    {
                        // Different teams = damage goes through naturally in RoR2
                    }
                }
            }

            orig(self, damageInfo);
        }

        private bool CombatDirector_AttemptSpawnOnTarget(
            On.RoR2.CombatDirector.orig_AttemptSpawnOnTarget orig,
            CombatDirector self,
            UnityEngine.Transform spawnTarget,
            DirectorPlacementRule.PlacementMode placementMode)
        {
            // Block all monster spawns when duel mode is active
            if (_duelModeActive) return false;
            return orig(self, spawnTarget, placementMode);
        }

        private void SceneDirector_PopulateScene(On.RoR2.SceneDirector.orig_PopulateScene orig, SceneDirector self)
        {
            // Block all interactable/chest spawns when duel mode is active
            if (_duelModeActive)
            {
                Log.Info("SceneDirector.PopulateScene blocked during duel.");
                return;
            }
            orig(self);
        }

        private void CharacterMaster_GiveMoney(On.RoR2.CharacterMaster.orig_GiveMoney orig, CharacterMaster self, uint amount)
        {
            // Block gold gain when duel mode is active
            if (_duelModeActive) return;
            orig(self, amount);
        }

        private void Run_BeginGameOver(On.RoR2.Run.orig_BeginGameOver orig, Run self, GameEndingDef gameEndingDef)
        {
            if (_duelModeActive)
            {
                Log.Info($"Run.BeginGameOver blocked during duel (ending={gameEndingDef?.cachedName}).");
                return;
            }
            orig(self, gameEndingDef);
        }

        private void GameEndReportPanel_Awake(On.RoR2.UI.GameEndReportPanelController.orig_Awake orig, GameEndReportPanelController self)
        {
            if (_duelModeActive)
            {
                Log.Info("GameEndReportPanel destroyed during duel.");
                UnityEngine.Object.Destroy(self.gameObject);
                return;
            }
            orig(self);
        }

        public void Dispose()
        {
            Deactivate();
            _duelModeActive = false;

            On.RoR2.CombatDirector.AttemptSpawnOnTarget -= CombatDirector_AttemptSpawnOnTarget;
            On.RoR2.SceneDirector.PopulateScene -= SceneDirector_PopulateScene;
            On.RoR2.CharacterMaster.GiveMoney -= CharacterMaster_GiveMoney;
            On.RoR2.Run.BeginGameOver -= Run_BeginGameOver;
            On.RoR2.UI.GameEndReportPanelController.Awake -= GameEndReportPanel_Awake;
        }
    }
}
