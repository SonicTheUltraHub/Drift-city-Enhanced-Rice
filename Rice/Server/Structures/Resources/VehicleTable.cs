using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Rice.Server.Structures.Resources
{
    public static class VehicleTable
    {
        public static Dictionary<int, List<VehicleUpgradeEntry>> VehicleUpgrades = new Dictionary<int, List<VehicleUpgradeEntry>>();
        public static Dictionary<int, VehicleListEntry> Vehicles = new Dictionary<int, VehicleListEntry>();

        public static void Load(string vehicleupgradepath, string vehiclelistpath)
        {
            if (!File.Exists(vehicleupgradepath))
            {
                Log.WriteError("Could not find VehicleUpgrade at " + vehicleupgradepath);
                return;
            }

            if (!File.Exists(vehiclelistpath))
            {
                Log.WriteError("Could not find VehicleList at " + vehiclelistpath);
                return;
            }

            string vehicleUpgradeJson = File.ReadAllText(vehicleupgradepath);
            VehicleUpgrades = JsonConvert.DeserializeObject<Dictionary<int, List<VehicleUpgradeEntry>>>(vehicleUpgradeJson);

            string vehicleListJson = File.ReadAllText(vehiclelistpath);
            Vehicles = JsonConvert.DeserializeObject<Dictionary<int, VehicleListEntry>>(vehicleListJson);

            foreach (var carId in VehicleUpgrades.Keys)
                foreach (var upgrade in VehicleUpgrades[carId])
                    upgrade.CarID = carId;
        }

        public static VehicleUpgradeEntry GetVehicleUpgrade(int sort, int grade)
        {
            if (grade < 1 || grade > 9)
                throw new Exception($"Invalid vehicle grade {grade} specified");

            if (!VehicleUpgrades.ContainsKey(sort))
                throw new Exception($"No vehicle with sort {sort} found");

            var entry = VehicleUpgrades[sort][grade - 1];

            if (!entry.IsValidGrade())
                throw new Exception($"Grade {grade} is not valid for vehicle {sort}");

            return entry;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class VehicleUpgradeEntry
    {
        public int CarID { get; set; }

        [JsonProperty("price")] public int Price;
        [JsonProperty("sell")] public int SellPrice;

        [JsonProperty("game_ui_name")] public string Name;

        [JsonProperty("gradelvl")] public int GradeLevel;
        [JsonProperty("gradetype")] public string GradeType;

        [JsonProperty("upgrademito")] public int UpgradeCost;

        [JsonProperty("capacity")] public float MitronCapacity;
        [JsonProperty("efficiency")] public float MitronEfficiency;

        [JsonProperty("accel")] public int Acceleration;
        [JsonProperty("boost")] public int Boost;
        [JsonProperty("crash")] public int Durability;
        [JsonProperty("speed")] public int Speed;

        public bool IsValidGrade() => Name.Length > 0;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class VehicleListEntry
    {
        [JsonProperty("unique_id")] public int CarID { get; set; }

        [JsonProperty("sellable")] public int Sellable;
        [JsonProperty("lvl")] public int Level;

        [JsonProperty("req_cond")] public string Condition;

        [JsonProperty("file_name")] public string FileName;

        [JsonProperty("ui_name")] public string UiName;
        [JsonProperty("speed")] public int Speed;
        [JsonProperty("accel")] public int Accel;
        [JsonProperty("crash")] public int Crash;
        [JsonProperty("boost")] public int BoostStat;

        // Categorias especiais pelo sufixo do nome: "Shadow ST", "Shadow BT", "Cielo Xmas", "Fervor MINI".
        // "SMALL" = carros de cupom: continuam como estao.
        public bool IsCouponOnly() => string.Equals(Condition, "SMALL", StringComparison.OrdinalIgnoreCase);

        public bool IsSpecialCategory()
        {
            if (string.IsNullOrEmpty(UiName)) return false;
            string n = UiName.Trim().ToUpperInvariant();
            return n.EndsWith(" ST") || n.EndsWith(" BT") || n.EndsWith(" XMAS") || n.EndsWith(" MINI");
        }

        public bool InDealership() => Sellable == 1;
        public bool HasCondition() => Condition != "N/A";

        // doesn't look like there's a more reliable way
        public string GetKeyId() => FileName.Split('.')[0];
    }
}