using System;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Battle Zone: modo espectador (OBS). Primeira etapa: so registra o que o jogo manda quando voce aperta para
    // assistir (na lista de salas ou o botao OBS dentro da sala), para montar as respostas (447 OBSState etc.).
    //   473 SecureJoinOBS, 475 SwitchToOBS, 476 SwitchToPlayer, 477 SecureSwitchToPlayer, 478 JoinOBS, 480 LeaveOBS,
    //   822 ObserveMode, 916 ObserverSupport. O jogo responde com 447 OBSState / 823 ObserverCount.
    public static class Observe
    {
        static void Dump(RicePacket packet, string name)
        {
            Log.WriteLine("Espectador: {0} ({1} bytes): {2}", name, packet.Buffer.Length,
                BitConverter.ToString(packet.Buffer, 0, Math.Min(packet.Buffer.Length, 96)));
        }

        [RicePacket(473, RiceServer.ServerType.Game)]
        public static void SecureJoinObs(RicePacket packet) { Dump(packet, "SecureJoinOBS 473"); }
        [RicePacket(475, RiceServer.ServerType.Game)]
        public static void SwitchToObs(RicePacket packet) { Dump(packet, "SwitchToOBS 475"); }
        [RicePacket(476, RiceServer.ServerType.Game)]
        public static void SwitchToPlayer(RicePacket packet) { Dump(packet, "SwitchToPlayer 476"); }
        [RicePacket(477, RiceServer.ServerType.Game)]
        public static void SecureSwitchToPlayer(RicePacket packet) { Dump(packet, "SecureSwitchToPlayer 477"); }
        [RicePacket(478, RiceServer.ServerType.Game)]
        public static void JoinObs(RicePacket packet) { Dump(packet, "JoinOBS 478"); }
        [RicePacket(480, RiceServer.ServerType.Game)]
        public static void LeaveObs(RicePacket packet) { Dump(packet, "LeaveOBS 480"); }
        [RicePacket(822, RiceServer.ServerType.Game | RiceServer.ServerType.Area)]
        public static void ObserveMode(RicePacket packet) { Dump(packet, "ObserveMode 822"); }
        [RicePacket(916, RiceServer.ServerType.Game)]
        public static void ObserverSupport(RicePacket packet) { Dump(packet, "ObserverSupport 916"); }
    }
}
