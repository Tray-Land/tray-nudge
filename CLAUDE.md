@AGENTS.md

## Tray app conventions

This is a tray-only WinUI 3 app built from the `winui-tray-app` skill scaffold on top of `winapp new`. Load that skill before changing app lifetime, the flyout, the tray icon, polling, settings, or secrets.

- Launch with `winapp run` (or `dotnet run`); never register the package by hand. Use `winapp run --clean` to test first-run behavior.
- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The flyout (`Views/TrayFlyoutWindow`) is hidden on dismiss and closed after a minute hidden. Anything it starts, it must stop in `FlyoutPage.OnHidden` / `Dispose`.
- The app makes no network calls and declares no `internetClient` capability; the scaffold's poller, connectivity, and secrets services were removed. If a feature ever needs the network, bring `ForegroundPoller` back from the skill scaffold rather than polling ad hoc.
- The one background job is `Services/ReminderService`: a single timer set for the next nudge (no polling), capped at 30 minutes and midnight. Reminders are the app's purpose, which is the documented reason it runs while hidden. Keep it that cheap.
- Habit data lives in `habits.json` (LocalFolder) through `Services/HabitStore`; small preferences live in `SettingsService`. All mutations happen on the UI thread and raise `HabitStore.Changed`.
- Streak, reminder, and wording rules live in `Core/` (WinRT-free) and are covered by `TrayNudge.Tests` (`dotnet test TrayNudge.Tests`), which compiles `Core/` directly. Change behavior there, with a test.
- Voice: warm, plain, and never scolding. A missed day is a "pause", never "failed" or "broken", and nothing is red. All progress wording goes through `Core/Encouragement.cs`; a test rejects harsh words and exclamation marks.
- Celebrations go through `Controls/Motion` so they respect the Celebrations setting and Windows' animation setting. Never start a XAML storyboard on an element that isn't loaded (it throws).
- The repo is MIT licensed (`LICENSE`). When the app's features are working, write a descriptive `README.md` (what it does, screenshot, install, setup/keys, privacy, build from source, license); the `winui-tray-app` skill lists the sections.
- Put pure decision logic (parsing, policies, formatting) in classes with no WinRT dependency, so a plain `net10.0` test project can cover it.
