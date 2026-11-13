using Rice.Server.Core;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Models = Rice.Server.Database.Models;

namespace Rice.Game
{
    public class Vehicle
    {
        public uint AuctionCount;
        public uint Color;
        public int CarID;
        public uint CarType;
        public uint Grade;
        public float Mitron;
        public float Kms;
        public uint SlotType;
        public long OwnerCid;
        // cor do vidro (Drift Shop > Tint) do carro: 0x82RRGGBB (VisualShop.TintOf, ligado no RiceServer.Start)
        public static Func<long, int, uint> TintFor;

        public VehicleUpgradeEntry VehicleUpgradeEntry;
        public VehicleListEntry VehicleListEntry;

        private Vehicle(int id, Models.Vehicle dbVehicle = null, int grade = 1)
        {
            int lookupId = id;
            int lookupGrade = grade;

            if (dbVehicle != null)
            {
                AuctionCount = (uint)dbVehicle.AuctionCount;
                Color = (uint)dbVehicle.Color;
                OwnerCid = dbVehicle.CID;
                CarID = dbVehicle.CarID;
                CarType = (uint)dbVehicle.CarType;
                Grade = (uint)dbVehicle.Grade;
                Mitron = dbVehicle.Mitron;
                Kms = dbVehicle.Kms;
                // carro de treino do tutorial: metade de cima do SLOT = 3 (o cliente reconhece assim)
                SlotType = IsTrainingType(dbVehicle.CarType) ? (3u << 16) : 0u;

                lookupId = dbVehicle.CarType;
                lookupGrade = dbVehicle.Grade;
            }

            VehicleUpgradeEntry = VehicleTable.GetVehicleUpgrade(lookupId, lookupGrade);
            VehicleListEntry = VehicleTable.Vehicles[lookupId];
        }

        public static bool IsTrainingType(int carType)
        {
            return RiceServer.Config.ApprenticeTutorial && carType == RiceServer.Config.ApprenticeCarType;
        }

        public bool IsTraining => (SlotType >> 16) == 3;

        public static Vehicle FromDB(Models.Vehicle dbVehicle) => new Vehicle(dbVehicle.CarID, dbVehicle, dbVehicle.Grade);

        public static Vehicle CreateEntry(ulong CID, int carSort, int color = 0, int grade = 1)
        {
            using (var rc = Database.GetContext())
            {
                VehicleUpgradeEntry upgradeEntry;
                try
                {
                    upgradeEntry = VehicleTable.GetVehicleUpgrade(carSort, grade);
                }
                catch (Exception ex)
                {
                    Log.WriteError($"Failed to get vehicle {carSort} grade {grade} for Vehicle.CreateEntry: {ex.Message}");
                    return null;
                }

                var lastVehicle = rc.Vehicles.Where(i => i.CID == (long)CID).OrderByDescending(i => i.CarID).FirstOrDefault();

                var newVehicle = new Models.Vehicle
                {
                    CID = (long)CID,
                    CarID = (lastVehicle?.CarID ?? 0) + 1,
                    CarType = carSort,
                    Grade = grade,
                    AuctionCount = 0,
                    Color = color,
                    Mitron = ScaledCapacity(upgradeEntry) // carro novo sai com o tanque cheio
                };

                rc.Vehicles.Add(newVehicle);
                rc.SaveChanges();

                return new Vehicle(newVehicle.CarType, newVehicle);
            }
        }

        // Tamanho do tanque enviado ao jogo (tabela x multiplicador do server.json).
        public static float ScaledCapacity(VehicleUpgradeEntry entry)
        {
            int factor = RiceServer.Config.FuelCapacityMultiplier;
            if (factor < 1)
                factor = 1;
            float scaled = entry.MitronCapacity * factor;
            int min = RiceServer.Config.FuelMinCapacity;
            return scaled < min ? min : scaled;
        }

        uint TintOf()
        {
            try { return TintFor != null && OwnerCid != 0 ? TintFor(OwnerCid, CarID) : 0u; }
            catch (Exception) { return 0u; }
        }

        public CarInfo GetInfo()
        {
            return new CarInfo
            {
                AuctionOn = false,
                CarUnit = new CarUnit
                {
                    AuctionCnt = AuctionCount,
                    BaseColor = 0,
                    CarID = CarID,
                    CarType = CarType,
                    Grade = Grade,
                    Mitron = Mitron,
                    Kmh = Kms,
                    SlotType = SlotType
                },
                Color = Color,
                Color2 = TintOf(),
                MitronCapacity = ScaledCapacity(VehicleUpgradeEntry),
                MitronEfficiency = VehicleUpgradeEntry.MitronEfficiency * RiceServer.Config.FuelEfficiencyMultiplier
            };
        }
    }
}
