# Setup: game, modding, testing, recording

## 1. You need to own GTA V on PC
You can't build, test or record the mod without the game. The judges will install it on their own copy, but **you** have to record footage of it running.

Ways to get it:
- **Check your Epic Games library first.** GTA V Premium Edition was free on Epic in May 2020. Epic is installed on this PC, so log in and look.
- Check the **Rockstar Games Launcher** library (it's installed here, but no game is).
- Buy it on Steam, Epic or Rockstar. It's often heavily discounted.
- Or **team up** with someone who owns it. Teams are allowed, and the prize goes to the team lead.
- ❌ **Steam Families / Family Sharing won't work.** GTA V needs a Rockstar account and launcher, so Steam excludes it from sharing.
- ❌ Don't log into a friend's Steam account. It breaks Steam's rules and puts their account at risk.

**This PC:** RTX 4070 Laptop, Ryzen 9 7940HS, 16 GB RAM. That's plenty.
**Install it on D:** (261 GB free). C: has only about 13 GB free, and GTA V needs about 110 GB.

## 2. Which edition: Legacy vs Enhanced
- Use **GTA V Legacy** if you can. It's where ScriptHookV, ScriptHookVDotNet and almost every mod are most mature.
- Enhanced (2025) support exists but is newer. Check the ScriptHookV page for the build you have.

## 3. Story Mode only
- "Single-player" = **Story Mode** (missions + free roam). That's exactly what the bounty requires.
- Launch with **BattlEye off** (an option in the Rockstar Launcher / launch settings). Mods won't work with it on, and turning it off also blocks GTA Online, which is what we want.
- ScriptHookV also switches itself off if you try to go Online.
- Tip: back up your story save, or start a new save, for testing.

## 4. Modding install (once the game is in)
1. **ScriptHookV** (by Alexander Blade): copy `ScriptHookV.dll` + `dinput8.dll` into the GTA V folder
2. **ScriptHookVDotNet 3** (nightly): copy its files into the GTA V folder and create a `scripts\` folder
3. Run `build-release.ps1`, then copy the `scripts\` folder from `release\EntangledHeat-<ver>\` into the GTA V folder and add your API key to `scripts\EntangledHeat.ini` (full steps in `docs/INSTALL.md`)
4. In game, press `F4` for the SHVDN console (it shows script errors) and `Insert` to reload scripts without restarting

Only download these from their official pages or gta5-mods.com.

## 5. How we verify the gameplay
- **Without the game (now):** I build and test the Atlas side as a standalone console app: real API calls, output formats, timing. Most of the risky work can be done this way.
- **In the game (you):** you play it. The mod writes `scripts\EntangledHeat.log` and shows an on-screen HUD, and you send me the log, screenshots or short clips. I can't run or see GTA myself, so this loop is how we debug.
- **Test pass before submitting:** a clean install with only our mod, a 20-minute session, no crashes or stutter, and the API key removed to check the error message.
- **How Moth verifies it:** they install and play it, and may check your **Atlas account** to confirm the engines were really called.

## 6. Recording footage
- **NVIDIA App / ShadowPlay** (Alt+Z) or **OBS Studio**: 1080p60
- Show the HUD ("Collapsed by Atlas · job #…") on screen, plus a short split screen of the job log
- Use royalty-free or original music only, and mute in-game radio (it's licensed music)
- No Rockstar logos on the thumbnail
