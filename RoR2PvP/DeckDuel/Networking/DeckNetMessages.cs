using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace DeckDuel.Networking
{
    public static class DeckNetMessages
    {
        public static void Register()
        {
            NetworkingAPI.RegisterMessageType<DeckSubmitMessage>();
            NetworkingAPI.RegisterMessageType<DeckResultMessage>();
        }
    }

    /// <summary>
    /// Client → Host: submit a deck for validation.
    /// </summary>
    public class DeckSubmitMessage : INetMessage
    {
        private const uint FORMAT_MAGIC = 0xDECC0001;

        private byte[] _deckData;
        private uint _senderNetId;

        public DeckSubmitMessage() { }

        public DeckSubmitMessage(Models.Deck deck)
        {
            _deckData = deck.Serialize();
            // Include sender's NetworkUser netId so the host can match this deck to the correct player
            var networkUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            _senderNetId = networkUser?.netId.Value ?? 0;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(FORMAT_MAGIC);
            writer.Write(_senderNetId);
            writer.WriteBytesAndSize(_deckData, _deckData.Length);
        }

        public void Deserialize(NetworkReader reader)
        {
            uint firstWord = reader.ReadUInt32();
            if (firstWord == FORMAT_MAGIC)
            {
                // New format: magic + senderNetId + bytesAndSize
                _senderNetId = reader.ReadUInt32();
                _deckData = reader.ReadBytesAndSize();
            }
            else
            {
                // Backward compat: old format where firstWord is the data length
                _senderNetId = 0;
                _deckData = reader.ReadBytes((int)firstWord);
                Log.Warning("DeckSubmitMessage: old format detected (no sender ID). Both players should use the same DLL version.");
            }
        }

        public void OnReceived()
        {
            if (!NetworkServer.active)
                return;

            // If old format (no sender ID), infer from the first non-local NetworkUser
            if (_senderNetId == 0)
            {
                foreach (var nu in NetworkUser.readOnlyInstancesList)
                {
                    if (nu.isLocalPlayer) continue;
                    _senderNetId = nu.netId.Value;
                    Log.Warning($"Inferred sender netId={_senderNetId} from first non-local NetworkUser (old DLL compat).");
                    break;
                }
            }

            var deck = Models.Deck.Deserialize(_deckData);
            var result = Models.DeckValidator.Validate(deck);

            if (result.IsValid)
            {
                DeckDuelPlugin.Instance.MatchStateMachine.OnDeckReceived(deck, _senderNetId);
                Log.Info($"Deck received and validated successfully from netId={_senderNetId}.");
            }
            else
            {
                Log.Warning($"Deck rejected: {result.Reason}");
                // Send rejection back to all clients (in v1, only 2 players)
                new DeckResultMessage(false, result.Reason).Send(NetworkDestination.Clients);
            }
        }
    }

    /// <summary>
    /// Host → Client: deck validation result.
    /// </summary>
    public class DeckResultMessage : INetMessage
    {
        private bool _approved;
        private string _reason;

        public DeckResultMessage() { }

        public DeckResultMessage(bool approved, string reason = "")
        {
            _approved = approved;
            _reason = reason ?? string.Empty;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(_approved);
            writer.Write(_reason);
        }

        public void Deserialize(NetworkReader reader)
        {
            _approved = reader.ReadBoolean();
            _reason = reader.ReadString();
        }

        public void OnReceived()
        {
            if (_approved)
            {
                Log.Info("Deck approved by host.");
                DeckDuelPlugin.Instance.DeckBuilderUI?.OnDeckApproved();
            }
            else
            {
                Log.Warning($"Deck rejected by host: {_reason}");
                DeckDuelPlugin.Instance.DeckBuilderUI?.OnDeckRejected(_reason);
            }
        }
    }
}
