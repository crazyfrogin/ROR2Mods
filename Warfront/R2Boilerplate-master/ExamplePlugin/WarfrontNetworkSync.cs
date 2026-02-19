using RoR2.Networking;
using UnityEngine.Networking;

namespace WarfrontDirector
{
    internal static class WarfrontNetworkSync
    {
        internal const short MessageType = 8061;

        internal static void Register(NetworkClient client)
        {
            if (client == null)
            {
                return;
            }

            client.RegisterHandler(MessageType, OnMessageReceived);
        }

        private static void OnMessageReceived(NetworkMessage networkMessage)
        {
            if (networkMessage == null)
            {
                return;
            }

            var message = networkMessage.ReadMessage<WarfrontStateMessage>();
            WarfrontDirectorController.ApplyRemoteSnapshot(message);
        }
    }

    internal sealed class WarfrontStateMessage : MessageBase
    {
        internal bool Active;
        internal byte Phase;
        internal byte DominantRole;
        internal byte Doctrine;
        internal float Intensity;
        internal float ContestDelta;
        internal float ChargeFraction;
        internal bool AssaultActive;
        internal bool BreachActive;
        internal bool MercyActive;
        internal byte LoneWolfPressure;
        internal float WindowTimeRemaining;
        internal byte ActiveCommanderCount;
        internal byte CommanderTypeMask;
        internal byte WarningOne;
        internal byte WarningTwo;
        internal byte Anomaly;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Active);
            writer.Write(Phase);
            writer.Write(DominantRole);
            writer.Write(Doctrine);
            writer.Write(Intensity);
            writer.Write(ContestDelta);
            writer.Write(ChargeFraction);
            writer.Write(AssaultActive);
            writer.Write(BreachActive);
            writer.Write(MercyActive);
            writer.Write(LoneWolfPressure);
            writer.Write(WindowTimeRemaining);
            writer.Write(ActiveCommanderCount);
            writer.Write(CommanderTypeMask);
            writer.Write(WarningOne);
            writer.Write(WarningTwo);
            writer.Write(Anomaly);
        }

        public override void Deserialize(NetworkReader reader)
        {
            Active = reader.ReadBoolean();
            Phase = reader.ReadByte();
            DominantRole = reader.ReadByte();
            Doctrine = reader.ReadByte();
            Intensity = reader.ReadSingle();
            ContestDelta = reader.ReadSingle();
            ChargeFraction = reader.ReadSingle();
            AssaultActive = reader.ReadBoolean();
            BreachActive = reader.ReadBoolean();
            MercyActive = reader.ReadBoolean();
            LoneWolfPressure = reader.ReadByte();
            WindowTimeRemaining = reader.ReadSingle();
            ActiveCommanderCount = reader.ReadByte();
            CommanderTypeMask = reader.ReadByte();
            WarningOne = reader.ReadByte();
            WarningTwo = reader.ReadByte();
            Anomaly = reader.ReadByte();
        }
    }
}
