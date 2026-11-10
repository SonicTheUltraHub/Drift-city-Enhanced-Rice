# Enhanced Rice (Offline Aprimorado)

An improved offline mode for the **Drift City 2016 US client**, built on top of the Rice server emulator
(C#, .NET Framework 4.8). The city feels alive with simulated players (bots) who race, trade, join your party and
crew, chat, and play the game modes with you.

Portuguese guide and the full change history: `LEIA-ME.md`.

**⬇️ Download the ready-to-use installer:** [RaGEZONE — Drift City Offline - Enhanced Rice](https://forum.ragezone.com/threads/%F0%9F%8F%81-drift-city-offline-enhanced-rice-v1.1277531/)

> **Credits:** based on **Rice** by **amPerl** (used and expanded with the author's permission).
> **Game files are not included in this repository** (the modified `DriftCity.exe`, `.agt` packs and `Patch` files
> under `Cliente/`, and `Instalador/modificacoes/*.completo`). The source builds without them; a ready-to-use installer
> is available on the [RaGEZONE thread](https://forum.ragezone.com/threads/%F0%9F%8F%81-drift-city-offline-enhanced-rice-v1.1277531/).

## What you need
- Your own Drift City (Alpha / US) folder with `DriftCity.exe`, `Data` and `Data_US`.
- Windows with .NET Framework 4.8.
- Visual Studio (2017 or newer) to build the project.

## Build and install
1. Open `Rice.sln` and build it. The **Instalador** project is the startup project and builds the Rice server along with it.
2. When the build finishes, the folder `Pacote` contains only `Install Enhanced Rice.exe` (the server is inside it).
3. Put `Install Enhanced Rice.exe` next to `DriftCity.exe` (or type the game folder when it asks) and run it.
4. Choose the language (English or Portuguese), then `1) Install / update`.
   - The installer checks every game file (md5) and shows what it will change.
   - Game data files get **only the modifications** (diffs) applied over your own files. `DriftCity.exe` is replaced by
     the one that opens without the launcher. Everything it replaces goes to `backup_instalador\<date>`.
   - It also installs the server (`Rice` folder), `Start.bat`, `Menu.bat` and the readme files.
   - It also fixes the cars and the car shop for **your** client: any Drift City client (for example the openDC one) gets
     the cars it has in the shop, ordered V1 to V9 with the special cars after V9, with our V, stats and prices. Cars
     without a model are left out. It asks first: you can also leave the client's car shop as it is (the bots then use
     the cars of that shop). `3) Fix the cars and the car shop for this client` does only this part.
   - `2) Undo the last installation` restores the backup.

## Playing
- Run `Start.bat`. It opens the server and the game.
- At login:
  - `admin` enters the **admin account** (RiceAdmin: high level, billionaire, a single police car fully upped with
    Turbo Start and Drift Booster);
  - any other name creates a **new account** right away (not admin), starting from zero.
- Type `!!help` in the game chat to see all the commands.

## Menu (`Menu.bat`)
Close the server before changing anything.
1. **Saves**: accounts and racers (create, rename, delete, level, mito, car V).
2. **Bots**: name, level, car, V, personality, city, skill, mito; recreate all bots.
3. **Crews**: name, description, minimum level.
4. **Server settings**: every option with an explanation (stored in `Rice\server.json`).
5. **Save backup**: zip and restore `save`, `mundo`, `mapas` and `server.json`.
6. Open the saves folder.
7. **Special event**: Automatic (default), Christmas, Valentine, Halloween or none.
8. **Mods**: install, turn on/off, reset or uninstall the Crew Ladder mod.
9. **Language**: English (default) or Portuguese.
G. **Game graphics**: resolution, fullscreen, anti-aliasing, shadows, view distance, detail and any item of `Data\Init\goption.ini`.

## Special events
The event turns on the client's own event look. In **Automatic** mode each event lasts the whole month:
- **October**: Halloween.
- **December**: Christmas (snow, Christmas tree, Santa outfit, the Christmas song looping in the lobby).
- **February**: Valentine.

Bots talk about the current event in the chat, each one in their own style, and answer when you say "merry christmas",
"happy halloween" and so on. Easter has no look in the client (only the bunny delivery mission).

## Mods
**Crew Ladder** (`Menu.bat > 8) Mods`): crews per city, one-on-one challenges in the Battle Zone, bosses with prizes and
the Most Wanted (Blacklist, boss Razor). Details in `Rice\res\mods\README_MODS.md`.

## Main features
- Bots living in the cities and the Driver Dome: they drive, park, go to shops, race, auction, trade and chat.
- Party with bots (`!!party <name>`): they follow you, help in missions, deliveries, patrols, HUV chases, Rush Time,
  Undercity and Neo City, and evolve with you.
- Battle Zone with bots: Beginner/Pro arena rules, bots join your room as players and race as AI, team races, the
  Complete Bonus Reward and race cards.
- Bot crews, friends, trades (parts, cars in boxes, visual parts), live auction, Part Shop (Modify, Convert,
  Combine), Drift Shop (V upgrade, stickers), licenses, crafting (Skill Books), level and combo rewards.
- Neo City solo and with bots (real weapon damage, HUVs fight back).
- Organized save folders: `save/<account>/<racer>`, `mundo` (world) and `mapas` (maps).

## Language
The server, the in-game messages, the menu and the installer are in **English** by default. To use Portuguese:
`Menu.bat > 9) Language`, choose Portuguese in the installer, or set `"Language": "pt"` in `Rice\server.json`.
Text the server does not know yet stays in Portuguese.

## Credits
- **Rice** server emulator by **amPerl** (used with permission).
- Packet formats from the openDC emulator documentation (github.com/Xerender13/openDC).
