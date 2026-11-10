using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Rice.Server.Core
{
    public class RiceListener
    {
#if DEBUG
        private static Dictionary<ushort, string> debugNameDatabase;
#endif

        private Dictionary<ushort, Action<RicePacket>> parsers;
        private List<ushort> checkInParsers; 
        private List<RiceClient> clients;

        private int port;
        private TcpListener listener;
        private bool exchangeRequired;

        public string Name;

        public RiceListener(string name, int port, bool exchangeRequired = true)
        {
#if DEBUG
            if (debugNameDatabase == null)
            {
                debugNameDatabase = new Dictionary<ushort, string>();

                // res/packets.lst e opcional (so deixa o log com nomes); sem ele o servidor sobe normal
                string pf = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "packets.lst");
                if (!File.Exists(pf)) pf = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Packets.txt");
                string[] src = File.Exists(pf) ? File.ReadAllLines(pf) : new string[0];
                foreach (var line in src)
                {
                    if (line.Length <= 3) continue;
                    string[] lineSplit = line.Split(':');
                    ushort id;
                    if (lineSplit.Length < 2 || !ushort.TryParse(lineSplit[0].Trim(), out id)) continue;
                    debugNameDatabase[id] = lineSplit[1].Trim();
                }
            }
#endif
            Name = name;
            parsers = new Dictionary<ushort, Action<RicePacket>>();
            checkInParsers = new List<ushort>();
            clients = new List<RiceClient>();

            this.port = port;
            this.listener = new TcpListener(IPAddress.Any, port);
            this.exchangeRequired = exchangeRequired;
        }

        public void Start()
        {
            listener.Start();
            listener.BeginAcceptTcpClient(onAccept, this.listener);

            //Log.WriteLine("Started listening on {0}", port);
        }

        private void onAccept(IAsyncResult result)
        {
            var tcpClient = listener.EndAcceptTcpClient(result);
            var riceClient = new RiceClient(tcpClient, this, exchangeRequired);

            Log.WriteLine("Accepted client from {0} on {1}", tcpClient.Client.RemoteEndPoint, port);

            clients.Add(riceClient);
            listener.BeginAcceptTcpClient(onAccept, this.listener);
        }

        public void SetParser(ushort id, Action<RicePacket> parser, bool needsCheckIn)
        {
            //Log.WriteLine("Added parser for packet {0} on {1}.", id, port);
            parsers[id] = parser;
            if (needsCheckIn)
                checkInParsers.Add(id);
        }

        public void Parse(RicePacket packet)
        {
            //Log.WriteLine("{0} parsing {1}.", port, packet.ID);

            if (parsers.ContainsKey(packet.ID))
            {
                if (checkInParsers.Contains(packet.ID) && packet.Sender.Player == null)
                {
                    Log.WriteLine("Autokilling {0} sender on {1}: CheckIn requirement not met.");
                    packet.Sender.Kill("Forbidden");
                    return;
                }

                parsers[packet.ID](packet);
            }
            else if (quietPackets.Contains(packet.ID) && RiceServer.Config.QuietSyncPackets)
            {
                // pacotes de sincronia que o jogo manda varias vezes por segundo (mira das torres e das armas na Neo
                // City, posicao do carro, replay da corrida): sozinho nao precisam de resposta e escrever cada um no
                // console deixava o servidor atrasado (delay nos tiros)
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
#if DEBUG
                if (debugNameDatabase.ContainsKey(packet.ID))
                    Log.WriteLine("Received {2} (id {0}, {0:X}) on {1}.", packet.ID, port, debugNameDatabase[packet.ID]);
                else
                    Log.WriteLine("Received unknown packet (id {0}, {0:X}{4}) on {1}, {2} bytes: {3}", packet.ID, port, packet.Buffer.Length, HexPreview(packet.Buffer), PacketNameSuffix(packet.ID));
#else
                Log.WriteLine("Received unknown packet (id {0}, {0:X}{4}) on {1}, {2} bytes: {3}", packet.ID, port, packet.Buffer.Length, HexPreview(packet.Buffer), PacketNameSuffix(packet.ID));
#endif
                Console.ForegroundColor = ConsoleColor.Gray;
            }
        }

        static readonly HashSet<int> quietPackets = new HashSet<int> { 209, 555, 556, 725 };

        // Nomes dos pacotes (da lista Packets.txt do pacote original), so para deixar o log legivel.
        private static readonly Dictionary<int, string> packetNames = new Dictionary<int, string>
        {
            {3, "GlobalTime"},
            {4, "Latency"},
            {5, "Log"},
            {6, "ServerPing"},
            {7, "NullPing"},
            {10, "UserAuth"},
            {20, "UserAuth"},
            {21, "UserAuth2"},
            {24, "ServerMessage"},
            {41, "CheckInLobby"},
            {43, "CheckOutLobby"},
            {44, "ClientAuth"},
            {45, "LobbyChat"},
            {46, "LobbyTime"},
            {60, "UserInfo"},
            {64, "GetImage"},
            {67, "PushImageData"},
            {71, "StickerUpload"},
            {73, "StickerList"},
            {75, "StickerSmallImage"},
            {76, "StickerReportAttach"},
            {77, "StickerSearch"},
            {80, "CheckCharName"},
            {82, "CreateChar"},
            {84, "DeleteChar"},
            {85, "BuyCar"},
            {87, "SellCar"},
            {89, "SelectCar"},
            {91, "UpgradeCar_Thread"},
            {93, "StickerNameCheck"},
            {95, "DeleteStickerCatalog"},
            {100, "ChannelList"},
            {102, "ChannelInfo"},
            {120, "CheckInGame"},
            {122, "CheckOutGame"},
            {123, "LoadChar_Thread"},
            {125, "JoinChannel"},
            {127, "LeaveChannel"},
            {129, "LoadDummyChar"},
            {130, "GameStream"},
            {133, "MailSet"},
            {134, "GetMail"},
            {135, "CheckEvent"},
            {140, "OpenChat"},
            {141, "CloseChat"},
            {142, "JoinChat"},
            {144, "LeaveChat"},
            {146, "ChatMsg"},
            {148, "Whisper"},
            {152, "RecommendFriend"},
            {157, "ReqCouponItem"},
            {160, "CarPos"},
            {162, "SaveCarPos"},
            {171, "DormantAccept"},
            {180, "ChaseBegin"},
            {183, "ChaseEnd"},
            {185, "ChaseProgress"},
            {187, "ChaseHit"},
            {189, "ChaseRequest"},
            {190, "HuvReq"},
            {192, "HuvHit"},
            {194, "HuvParam"},
            {196, "HuvTimeBonus"},
            {200, "BattleReq"},
            {203, "BattleEnd"},
            {220, "RegisterChannelAdmin"},
            {228, "PartyPreCheck"},
            {230, "FriendList"},
            {232, "FriendAddByName"},
            {235, "FriendDel"},
            {239, "FriendRequest"},
            {240, "PartyInvite"},
            {241, "PartyReject"},
            {242, "PartyJoin"},
            {244, "PartyLeave"},
            {245, "PartyBanish"},
            {246, "PartyApply"},
            {247, "PartyAccept"},
            {250, "UnderCityStage"},
            {252, "UnderCitySuggest"},
            {253, "UnderCityAgree"},
            {254, "UnderCityReject"},
            {257, "ExitUnderCity"},
            {260, "TutorialClear"},
            {262, "QuestStart"},
            {264, "QuestComplete"},
            {266, "QuestReward"},
            {268, "QuestFail"},
            {270, "QuestGiveUp"},
            {272, "MyQuestList"},
            {274, "QuestGoalPlace"},
            {300, "JoinArea"},
            {302, "LeaveArea"},
            {321, "ArbeitList"},
            {323, "ArbeitStart"},
            {325, "ArbeitReward"},
            {329, "ArbeitGiveUp"},
            {331, "ArbeitGoalPlace"},
            {335, "ArbeitReady"},
            {337, "ArbeitGo"},
            {339, "ArbeitRankList"},
            {400, "ItemList"},
            {403, "DropItem"},
            {405, "BuyItem"},
            {407, "SellItem"},
            {409, "EquipItem"},
            {411, "UnEquipItem"},
            {413, "UpgradeItem"},
            {415, "ItemUnion"},
            {417, "ItemUse"},
            {419, "ItemUseEx"},
            {422, "PackItem"},
            {424, "UnPackItem"},
            {426, "ModifyItem"},
            {428, "CombiItem"},
            {430, "UpgradeItemDouble"},
            {432, "ReplenishItem"},
            {434, "CheckStat"},
            {435, "RepairItem"},
            {440, "RegisterRoomObserver"},
            {442, "UnregisterRoomObserver"},
            {443, "RoomDirectJoin"},
            {444, "RoomSecureJoin"},
            {445, "RoomIdDirectJoin"},
            {450, "InviteeList"},
            {457, "GetRoomInfo"},
            {460, "RoomCreate"},
            {463, "RoomList"},
            {465, "RoomInvite"},
            {468, "RoomJoin"},
            {469, "RoomLeave"},
            {470, "RoomBanish"},
            {471, "ChannelRoomJoin"},
            {472, "ChannelRoomSecureJoin"},
            {473, "SecureJoinOBS"},
            {475, "SwitchToOBS"},
            {476, "SwitchToPlayer"},
            {477, "SecureSwitchToPlayer"},
            {478, "JoinOBS"},
            {480, "LeaveOBS"},
            {485, "PvpReadyState"},
            {486, "PvpStartReq"},
            {488, "PvpLoadingStatus"},
            {489, "PvpLoadComplete"},
            {491, "PvpEnd"},
            {493, "PvpGate"},
            {500, "TradeOpen"},
            {503, "TradeProposeAck"},
            {504, "TradeItemOn"},
            {506, "TradeItemOff"},
            {508, "TradeLock"},
            {510, "TradeClose"},
            {512, "TradeComplete"},
            {520, "SetGameMode"},
            {540, "UDPTimeSync"},
            {541, "MoveVehicle"},
            {544, "JumpFreeTrafficVehicle"},
            {546, "MoveVehiclePlus"},
            {547, "MoveFreeTrafficVehicle"},
            {549, "JumpVehicle"},
            {550, "RemoveVehicle"},
            {562, "EnterArea"},
            {564, "ExitArea"},
            {566, "EnterGroup"},
            {567, "ExitGroup"},
            {568, "SetAreaMap"},
            {569, "BlockList"},
            {570, "ClearAreaMap"},
            {571, "AreaChat"},
            {572, "GroupCombo"},
            {573, "RequestCar"},
            {575, "GroupComboReset"},
            {576, "ReplaceCar"},
            {580, "MyAuctionList"},
            {582, "AuctionOn"},
            {584, "AuctionCancel"},
            {586, "AuctionComplete"},
            {588, "AuctionList"},
            {592, "AuctionBuy"},
            {595, "AuctionSearch"},
            {620, "RoomModify"},
            {623, "RoomChangeTeam"},
            {660, "GameCharInfo"},
            {662, "GameCharInfoEx"},
            {681, "UDPSync"},
            {682, "AreaStatus"},
            {684, "AllAreaStatus"},
            {700, "UDPCastTraffic"},
            {702, "UDPCastTCS"},
            {704, "UDPFlushCast"},
            {705, "RegisterAgent"},
            {707, "UDPCastTCSSignal"},
            {710, "GroupTCS"},
            {711, "GroupTCSSignal"},
            {712, "GroupUpdate"},
            {720, "ObjectUpdate"},
            {721, "DriveInfoUpdate"},
            {723, "FuelChargeReq"},
            {740, "TrafficItemEvent"},
            {780, "AreaList"},
            {782, "FirstPosition"},
            {784, "GetDateTime"},
            {786, "QuestPosition"},
            {788, "MyCityPosition"},
            {801, "PlayerInfoReq"},
            {805, "SetVisualItem"},
            {821, "SilentMove"},
            {822, "ObserveMode"},
            {830, "SetTeamMark"},
            {832, "GetTeamMark"},
            {834, "SetTeamUrl"},
            {840, "MyTeamInfo"},
            {842, "CreateTeam"},
            {844, "CloseTeam"},
            {848, "LeaveTeam"},
            {850, "DelegateTeamMaster"},
            {851, "CheckTeamName"},
            {853, "CancelCloseTeam"},
            {855, "TeamMembers"},
            {858, "CharTeamInfo"},
            {860, "CBattleInfo"},
            {862, "GetCBattleTime"},
            {868, "CheckTeamUrl"},
            {870, "JoinTeamApplyByName"},
            {871, "JoinTeamApply"},
            {872, "JoinTeamConfirm"},
            {873, "LeaveTeamRequest"},
            {874, "LeaveTeamConfirm"},
            {875, "BanishTeamMember"},
            {876, "RejectTeamApply"},
            {880, "CBattleRoomCreate"},
            {881, "CBattleRoomJoin"},
            {882, "EnterCBattleChannel"},
            {883, "ExitCBattleChannel"},
            {884, "AssignTeamRank"},
            {900, "CBattleRoomReady"},
            {903, "CBattleInstance"},
            {906, "CBattleSet"},
            {907, "CBattleJoinOBS"},
            {908, "CBattleItems"},
            {910, "CBattleGetSetting"},
            {916, "ObserverSupport"},
            {990, "InstantRequest"},
            {992, "InstantStart"},
            {994, "InstantComplete"},
            {999, "InstantGiveUp"},
            {1001, "InstantGoalPlace"},
            {1010, "UpdateCouponForVar"},
            {1011, "GetCouponForVar"},
            {1013, "GetCouponStampList"},
            {1015, "MarkCoupon"},
            {1017, "OpenCoupon"},
            {1019, "GetCoupon"},
            {1080, "ImageRequest"},
            {1082, "ImageSave"},
            {1110, "EnterRushZone"},
            {1112, "ExitRushZone"},
            {1114, "RushHitPoint"},
            {1118, "NotifyRushHuv"},
            {1122, "CastRushEvent"},
            {1123, "RushObjectUpdate"},
            {1125, "RequestSwitchRushHuv"},
            {1151, "Mita500Get"},
            {1154, "Mita500GetBuff"},
            {1162, "DayMissionGetReward"},
            {1163, "DayMileageGetReward"},
            {1170, "DayEventGetItem"},
            {1172, "DayEventComplete"},
            {1200, "VisualItemList"},
            {1203, "BuyVisualItem_Thread"},
            {1205, "EquipVisualItem"},
            {1207, "UnEquipVisualItem"},
            {1209, "VisualItemUse"},
            {1211, "DropVisualItem"},
            {1300, "BuyHistoryList"},
            {1302, "SendVisualItem_Thread"},
            {1304, "GetVisualItem_Thread"},
            {1306, "IsValidCharName"},
            {1311, "TestPlayerAnswer"},
            {1321, "LapTimeReady"},
            {1323, "LapTimeGate"},
            {1324, "LapTimeRecord"},
            {1350, "MyStickerList"},
            {1352, "AttachSticker"},
            {1354, "DetachSticker"},
            {1360, "EndingUserRecord"},
            {1400, "GetMyHancoin_Thread"},
            {1410, "NotifyDelCharacter"},
            {1411, "NotifyDelCharacterAck"},
            {1412, "NotifyServerIp"},
            {1413, "ReportHack"},
            {1500, "ItemExpireCmd"},
            {1700, "MyAuctionCarList"},
            {1702, "AuctionCarOn"},
            {1704, "AuctionCarCancel"},
            {1706, "AuctionCarComplete"},
            {1708, "AuctionCarList"},
            {1710, "AuctionCarBuy"},
            {1713, "AuctionCarSearch"},
            {1900, "SelectGuild"},
            {2000, "UpdateQuickSlot"},
            {3000, "UCPatrolStart"},
            {3002, "UCPatrolGiveUp"},
            {3004, "UCPatrolGoalPlace"},
            {3010, "UCBossPatrolStart"},
            {3012, "UCBossPatrolGiveUp"},
            {3014, "UCBossPatrolGoalPlace"},
            {3200, "CityLeave"}
        };

        private static string PacketNameSuffix(int id)
        {
            string name;
            return packetNames.TryGetValue(id, out name) ? " " + name : "";
        }

        private static string HexPreview(byte[] buffer)
        {
            const int max = 96;   // Neo City etc.: mais bytes no console para descobrir os pacotes
            if (buffer == null || buffer.Length == 0)
                return "(vazio)";
            if (buffer.Length <= max)
                return BitConverter.ToString(buffer);
            return BitConverter.ToString(buffer, 0, max) + "...";
        }

        public RiceClient[] GetClients()
        {
            return clients.ToArray();
        }

        public void Broadcast(RicePacket packet, RiceClient exclude = null)
        {
            foreach (var client in GetClients())
            {
                if (exclude == null || client != exclude)
                    client.Send(packet);
            }
        }
    }
}