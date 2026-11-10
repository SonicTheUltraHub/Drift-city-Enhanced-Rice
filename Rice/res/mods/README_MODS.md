# Mods

## Crew Ladder (`mod_escada_de_crews.json`)

Off by default. To install, turn on or turn off: `Menu.bat` > `8) Mods`. The server console shows
"Mod on: Crew Ladder" when it is on. (Under the hood: on = `mod_escada_de_crews.json` in the `Rice.exe` folder;
off = the same file ending in `.desligado`, keeping your changes.)

What the mod does:
- Crews per city (Rookies/MoonFlow, MitoKings/KoiGhost, CrasRacers/CrasElite, OrosLegends, LegendsDC), 10 members each,
  each one on its side of the city; bots do not recruit.
- Ladder of one-on-one challenges in the Battle Zone (stay near a member of the current crew for 10 s), bosses with
  prizes (car, mito, +1 garage floor), donations from the defeated, duo race with a bot from your crew.
- Most Wanted (Blacklist, boss Razor) after LegendsDC.
- You need a crew to challenge (crew from level 1); crew slots (3, +3 per boss); a higher level bot refuses to join;
  a friend without a crew joins yours; half of the members' mito goes to the leader; `!!escada` shows the progress.

The numbers in the file (time, prize, slots, crew sides) can be changed in the `mod_escada_de_crews.json` in the
`Rice.exe` folder; the menu has an option to reset them to default.
Turning it on/off rebuilds the bots' crews (classic <-> ladder); bots already in your crew stay in it.
