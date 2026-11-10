using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Server.Structures.Resources;

namespace Rice.Server
{
    public static class Resources
    {
        public static void Initialize(Config config)
        {
            QuestTable.Load("res/QuestClient.json");
            ItemTable.Load("res/ItemClient.json", "res/UseItemClient.json");
            VehicleTable.Load("res/VehicleUpgrade.json", "res/VehicleList.json");
            AssistTable.Load("res/AssistClient.json");

            Log.WriteLine("Loaded resources.");

            // res antigo (2016) ao lado do Rice.exe: carros com o V e o preco antigos (GSIX coop era V9; na loja nova e V2)
            VehicleListEntry gsix;
            if (VehicleTable.Vehicles.TryGetValue(244, out gsix) && gsix != null && gsix.Level == 9)
                Log.WriteError("ATENCAO: a pasta res ao lado do Rice.exe esta desatualizada (VehicleList.json antigo). Copie os .json da pasta Rice\\res do projeto para ela.");
        }
    }
}
