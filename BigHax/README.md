# BigHax runtime diagnostics

Diagnostics default to disabled. For a local investigation create
`BigHax.Diagnostics.json` under the BigHax ModsLocal folder:

`%USERPROFILE%/AppData/LocalLow/Hovgaard Games/Big Ambitions/ModsLocal/BigHax/`

```json
{
  "debug": true,
  "employee": true,
  "happiness": true,
  "ui": true
}
```

The configuration is read once when the mod loads. Restart the game after
changing it. Remove the file or set `debug` to `false` to disable diagnostics.
The other optional sub-flags are `sleep`, `businessDirt`, and `traffic`;
leave them disabled for the employee/Happiness reports.

Look for `[Mod:BigHax]` in `Player.log`. The startup line records the game
assembly module identifier and active sub-flags. Employee logs record the
resolved GetContact signature, load/hire/candidate/daily cleanup counts and
satisfaction ranges. UI logs record the configured hotkey, library selection,
fallback reason, panel construction and failures.

Happiness protection hooks the game's actual `UpdateHappiness` calculation while
its cheat is enabled. This covers hourly recalculations and modifier additions,
removals and activity boosts, even when Mono inlines the property's setter.
Modifier data and expiry continue to use the game's own routines. Disabling the
cheat restores the original method and immediately recalculates from those
modifiers. The supported Build 3682 method body is checked before hooking;
unknown bodies warn and retain the hourly fallback.

Happiness diagnostics observe full-menu and app-panel lifecycle events and
hourly events. Up to 24 protected recalculations also include caller stacks,
before/final values and modifier data. No Happiness polling is used. The
functional hook is independent of the diagnostic flags.

For in-game verification:

1. Open BigHax settings with the configured hotkey (default F5); close/reopen.
2. Enable demand removal and maximum employee satisfaction; check existing
   employees, a new candidate/hire, a day change, and a save reload. Demand
   messages should disappear; unrelated messages/notifications should remain.
3. Enable the Happiness option; open/close Persona and other panels displaying
   Happiness, and let several hours pass without opening either menu. Try
   sleeping and an activity that adds/removes a modifier. Happiness should stay
   at 100%. Disable the cheat: the normal calculated value should return.
   Re-enable it and reload the save; protection should remain active. Preserve
   that session's Player.log if any step fails.

Run `TestEmployeeDemandCompatibility.ps1` from PowerShell with .NET SDK 10 to
exercise the actual employee service against both supported GetContact API
shapes and an unsupported future API, without launching Unity or the game.
In-game verification remains necessary for actual UI and game event behavior.

Run TestHappinessProtection.ps1 with .NET SDK 10 to check the installed game's
recalculation fingerprint and the service's enable/disable, modifier expiry,
save rebind and unload behavior. The native detour transport is substituted in
the fixture; its actual Mono runtime behavior still requires an in-game test.
