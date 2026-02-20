using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace MuscleMemory
{
    internal sealed class NetworkSync
    {
        private const short SkillSyncMessageType = 14097;
        private const short SkillActivationNotifyType = 14098;

        private readonly MuscleMemoryConfig _config;
        private readonly ProgressionManager _progression;

        private NetworkClient _registeredClient;
        private bool _clientHandlerRegistered;
        private bool _serverHandlerRegistered;
        private float _nextReplicationAt;

        internal NetworkSync(MuscleMemoryConfig config, ProgressionManager progression)
        {
            _config = config;
            _progression = progression;
            _progression.OnAnyLevelChanged += OnLevelChanged;
        }

        private void OnLevelChanged()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            BroadcastProgressSnapshotsToClients(Time.fixedTime);
        }

        internal void ResetReplicationTimer()
        {
            _nextReplicationAt = 0f;
        }

        internal void TryRegisterServerMessageHandler()
        {
            if (!NetworkServer.active)
            {
                if (_serverHandlerRegistered)
                {
                    UnregisterServerMessageHandler();
                }

                return;
            }

            if (_serverHandlerRegistered)
            {
                return;
            }

            NetworkServer.RegisterHandler(SkillActivationNotifyType, OnServerSkillActivationReceived);
            _serverHandlerRegistered = true;
        }

        internal void UnregisterServerMessageHandler()
        {
            if (!_serverHandlerRegistered)
            {
                return;
            }

            if (NetworkServer.active)
            {
                NetworkServer.UnregisterHandler(SkillActivationNotifyType);
            }

            _serverHandlerRegistered = false;
        }

        internal void SendSkillActivationToServer(NetworkInstanceId masterNetId, SkillSlotKind slot)
        {
            if (NetworkServer.active)
            {
                return;
            }

            if (_registeredClient == null || !_registeredClient.isConnected)
            {
                return;
            }

            var message = new SkillActivationNotifyMessage
            {
                MasterNetId = masterNetId,
                SlotIndex = (int)slot
            };

            _registeredClient.Send(SkillActivationNotifyType, message);
        }

        private void OnServerSkillActivationReceived(NetworkMessage networkMessage)
        {
            SkillActivationNotifyMessage message = networkMessage.ReadMessage<SkillActivationNotifyMessage>();

            int slotIndex = message.SlotIndex;
            if (slotIndex < 0 || slotIndex >= Constants.SlotCount)
            {
                return;
            }

            GameObject masterObject = NetworkServer.FindLocalObject(message.MasterNetId);
            if (masterObject == null)
            {
                return;
            }

            CharacterMaster master = masterObject.GetComponent<CharacterMaster>();
            if (master == null || !master.playerCharacterMasterController)
            {
                return;
            }

            PlayerProgressState state = _progression.GetOrCreateProgressState(master);
            SkillSlotKind slot = (SkillSlotKind)slotIndex;
            float now = Time.fixedTime;

            state.LastActivatedSlot = slot;
            state.LastActivatedTime = now;
            state.Slots[slotIndex].LastActivatedTime = now;
        }

        internal void TryRegisterClientMessageHandler()
        {
            NetworkClient currentClient = null;
            if (NetworkManager.singleton != null)
            {
                currentClient = NetworkManager.singleton.client;
            }

            if (currentClient == null)
            {
                List<NetworkClient> allClients = NetworkClient.allClients;
                if (allClients != null)
                {
                    for (int i = 0; i < allClients.Count; i++)
                    {
                        if (allClients[i] != null)
                        {
                            currentClient = allClients[i];
                            break;
                        }
                    }
                }
            }

            if (currentClient == null)
            {
                if (_clientHandlerRegistered)
                {
                    UnregisterClientMessageHandler();
                }

                return;
            }

            if (_clientHandlerRegistered && _registeredClient == currentClient)
            {
                return;
            }

            if (_clientHandlerRegistered)
            {
                UnregisterClientMessageHandler();
            }

            currentClient.RegisterHandler(SkillSyncMessageType, OnClientSkillStateSyncMessageReceived);
            _registeredClient = currentClient;
            _clientHandlerRegistered = true;
        }

        internal void UnregisterClientMessageHandler()
        {
            if (!_clientHandlerRegistered)
            {
                return;
            }

            if (_registeredClient != null)
            {
                _registeredClient.UnregisterHandler(SkillSyncMessageType);
            }

            _registeredClient = null;
            _clientHandlerRegistered = false;
        }

        internal void TryBroadcast(float now)
        {
            if (now < _nextReplicationAt)
            {
                return;
            }

            _nextReplicationAt = now + Mathf.Max(0.05f, _config.ReplicationInterval.Value);
            BroadcastProgressSnapshotsToClients(now);
        }

        internal void SendFullSnapshotToClient(NetworkConnection conn)
        {
            if (!NetworkServer.active || conn == null)
            {
                return;
            }

            foreach (var entry in _progression.PlayerStates)
            {
                CharacterMaster master = entry.Key;
                PlayerProgressState state = entry.Value;
                if (!master || master.netId == NetworkInstanceId.Invalid)
                {
                    continue;
                }

                var message = BuildSyncMessage(master, state, Time.fixedTime);
                NetworkServer.SendToClient(conn.connectionId, SkillSyncMessageType, message);
            }
        }

        private void BroadcastProgressSnapshotsToClients(float now)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            foreach (var entry in _progression.PlayerStates)
            {
                CharacterMaster master = entry.Key;
                PlayerProgressState state = entry.Value;
                if (!master || master.netId == NetworkInstanceId.Invalid)
                {
                    continue;
                }

                var message = BuildSyncMessage(master, state, now);

                UpsertReplicatedLevelState(master.netId, message);

                NetworkServer.SendToAll(SkillSyncMessageType, message);
            }
        }

        private SkillStateSyncMessage BuildSyncMessage(CharacterMaster master, PlayerProgressState state, float now)
        {
            return new SkillStateSyncMessage
            {
                MasterNetId = master.netId,
                PrimaryLevel = state.Slots[(int)SkillSlotKind.Primary].Level,
                SecondaryLevel = state.Slots[(int)SkillSlotKind.Secondary].Level,
                UtilityLevel = state.Slots[(int)SkillSlotKind.Utility].Level,
                SpecialLevel = state.Slots[(int)SkillSlotKind.Special].Level,
                PrimaryProgress = _progression.CalculateProgressFraction(
                    state.Slots[(int)SkillSlotKind.Primary].Proficiency,
                    state.Slots[(int)SkillSlotKind.Primary].Level),
                SecondaryProgress = _progression.CalculateProgressFraction(
                    state.Slots[(int)SkillSlotKind.Secondary].Proficiency,
                    state.Slots[(int)SkillSlotKind.Secondary].Level),
                UtilityProgress = _progression.CalculateProgressFraction(
                    state.Slots[(int)SkillSlotKind.Utility].Proficiency,
                    state.Slots[(int)SkillSlotKind.Utility].Level),
                SpecialProgress = _progression.CalculateProgressFraction(
                    state.Slots[(int)SkillSlotKind.Special].Proficiency,
                    state.Slots[(int)SkillSlotKind.Special].Level),
                FlowActive = now <= state.FlowWindowEnd
            };
        }

        private void OnClientSkillStateSyncMessageReceived(NetworkMessage networkMessage)
        {
            SkillStateSyncMessage message = networkMessage.ReadMessage<SkillStateSyncMessage>();
            UpsertReplicatedLevelState(message.MasterNetId, message);

            GameObject masterObject = ClientScene.FindLocalObject(message.MasterNetId);
            if (masterObject == null)
            {
                return;
            }

            CharacterMaster master = masterObject.GetComponent<CharacterMaster>();
            if (master == null)
            {
                return;
            }

            CharacterBody body = master.GetBody();
            if (body != null)
            {
                body.MarkAllStatsDirty();
            }
        }

        private void UpsertReplicatedLevelState(NetworkInstanceId masterNetId, SkillStateSyncMessage message)
        {
            if (masterNetId == NetworkInstanceId.Invalid)
            {
                return;
            }

            if (!_progression.ReplicatedStates.TryGetValue(masterNetId, out ReplicatedLevelState state))
            {
                state = new ReplicatedLevelState();
                _progression.ReplicatedStates[masterNetId] = state;
            }

            state.Levels[(int)SkillSlotKind.Primary] = Math.Max(0, message.PrimaryLevel);
            state.Levels[(int)SkillSlotKind.Secondary] = Math.Max(0, message.SecondaryLevel);
            state.Levels[(int)SkillSlotKind.Utility] = Math.Max(0, message.UtilityLevel);
            state.Levels[(int)SkillSlotKind.Special] = Math.Max(0, message.SpecialLevel);
            state.Progress[(int)SkillSlotKind.Primary] = message.PrimaryProgress;
            state.Progress[(int)SkillSlotKind.Secondary] = message.SecondaryProgress;
            state.Progress[(int)SkillSlotKind.Utility] = message.UtilityProgress;
            state.Progress[(int)SkillSlotKind.Special] = message.SpecialProgress;
            state.FlowActive = message.FlowActive;
        }

        private sealed class SkillActivationNotifyMessage : MessageBase
        {
            internal NetworkInstanceId MasterNetId;
            internal int SlotIndex;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(MasterNetId);
                writer.Write(SlotIndex);
            }

            public override void Deserialize(NetworkReader reader)
            {
                MasterNetId = reader.ReadNetworkId();
                SlotIndex = reader.ReadInt32();
            }
        }

        private sealed class SkillStateSyncMessage : MessageBase
        {
            internal NetworkInstanceId MasterNetId;
            internal int PrimaryLevel;
            internal int SecondaryLevel;
            internal int UtilityLevel;
            internal int SpecialLevel;
            internal float PrimaryProgress;
            internal float SecondaryProgress;
            internal float UtilityProgress;
            internal float SpecialProgress;
            internal bool FlowActive;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(MasterNetId);
                writer.Write(Math.Max(0, PrimaryLevel));
                writer.Write(Math.Max(0, SecondaryLevel));
                writer.Write(Math.Max(0, UtilityLevel));
                writer.Write(Math.Max(0, SpecialLevel));
                writer.Write(Mathf.Clamp01(PrimaryProgress));
                writer.Write(Mathf.Clamp01(SecondaryProgress));
                writer.Write(Mathf.Clamp01(UtilityProgress));
                writer.Write(Mathf.Clamp01(SpecialProgress));
                writer.Write(FlowActive);
            }

            public override void Deserialize(NetworkReader reader)
            {
                MasterNetId = reader.ReadNetworkId();
                PrimaryLevel = reader.ReadInt32();
                SecondaryLevel = reader.ReadInt32();
                UtilityLevel = reader.ReadInt32();
                SpecialLevel = reader.ReadInt32();
                PrimaryProgress = reader.ReadSingle();
                SecondaryProgress = reader.ReadSingle();
                UtilityProgress = reader.ReadSingle();
                SpecialProgress = reader.ReadSingle();
                FlowActive = reader.ReadBoolean();
            }
        }
    }
}
