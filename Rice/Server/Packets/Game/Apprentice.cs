using System;
using System.Linq;
using Rice.Game;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Tutorial do proprio jogo (Apprentice), conforme a documentacao do emulador openDC:
    // corredor novo com o Birdie RTI de treino (tipo 95, SLOT 3<<16) -> Driver Dome -> corrida de treino do Battle Zone
    // (local, o jogo controla) -> 0x5d2 CompleteApprentice (u32 resultado, tempo, carro escolhido, cor)
    // -> 0x5d3 (u32 0, tempo, id do carro de treino, 0, CarInfo do carro novo, i32 premio, i64 mito).
    // O cliente troca o carro sozinho; aqui o carro escolhido entra na garagem e o de treino sai.
    public static class Apprentice
    {
        [RicePacket(0x5d2, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Complete(RicePacket packet)
        {
            var buf = packet.Buffer;
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || buf.Length < 16) return;
            uint result = BitConverter.ToUInt32(buf, 0), elapsed = BitConverter.ToUInt32(buf, 4);
            int carType = BitConverter.ToInt32(buf, 8), color = BitConverter.ToInt32(buf, 12);
            var old = ch.Vehicle != null && ch.Vehicle.IsTraining ? ch.Vehicle : ch.Garage.FirstOrDefault(v => v.IsTraining);
            Log.WriteLine("Tutorial: fim (resultado {0}, {1} ms), carro escolhido {2} cor {3:X}.", result, elapsed, carType, color);
            if (old == null) { Log.WriteError("Tutorial: o corredor nao tem o carro de treino."); return; }

            VehicleListEntry entry;
            if (result != 0 || !VehicleTable.Vehicles.TryGetValue(carType, out entry) || entry.Level != 1 || Vehicle.IsTrainingType(carType))
            {
                Log.WriteError("Tutorial: carro inicial " + carType + " recusado.");
                return;
            }
            Vehicle car;
            Item key;
            if (!ch.GrantVehicle(carType, out car, out key, color, 1))
            {
                Log.WriteError("Tutorial: nao consegui dar o carro " + carType + ".");
                return;
            }
            if (!ch.Garage.Contains(car)) ch.Garage.Add(car);
            ch.SelectCar(car.CarID);
            int oldId = old.CarID;
            if (!ch.RemoveVehicle(oldId)) Log.WriteError("Tutorial: nao consegui tirar o carro de treino.");

            var ack = new RicePacket(0x5d3);
            ack.Writer.Write(0u);
            ack.Writer.Write(elapsed);
            ack.Writer.Write((uint) oldId);
            ack.Writer.Write(0u);
            ack.Writer.Write(car.GetInfo());
            ack.Writer.Write(0);
            ack.Writer.Write(ch.Mito);
            packet.Sender.Send(ack);
            ch.FlushModInfo(packet.Sender);
            // licenca do seu carro (Rookie): sem isso o minimapa fica lendo licenca vazia (-7000 sem parar)
            Licenses.SendLogin(packet.Sender, ch);
            Log.WriteLine("Tutorial concluido por {0}: carro de treino {1} -> carro {2} ({3}).", ch.Name, oldId, car.CarID, carType);
            Chat.SendSystemMessage(packet.Sender, "Tutorial concluido! Seu carro inicial ja esta na garagem. Use !!help para ver os comandos.");
        }
    }
}
