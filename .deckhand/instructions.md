# Story Shuffle: project notes

Jellyfin 12 plugin in C# (.NET 8). User-facing text is English. `CONTRIBUTING.md` is the
contributor guide (build, tests, adding a series, releasing); these notes only add what a
sandboxed run needs on top of it.

## Checks (run all before pushing)

1. `dotnet build -c Release src/Jellyfin.Plugin.StoryShuffle`: must build without errors.
2. `dotnet run -c Release --project tests/StoryShuffle.Tests`: must print `ALL n CHECKS PASSED`
   (exit code 0). It loads the Release DLL from step 1, so always rebuild first.

CI (`.github/workflows/ci.yml`) runs these same two checks on every pull request.

## Rules

- No NuGet: `nuget.config` clears every feed. Never add a `PackageReference`; use the SDK
  and the shared frameworks only.
- `ref/` holds compile-only stubs of Jellyfin 12 types. Add only the members the plugin
  uses, with Jellyfin's exact signatures. `tests/realref/` holds Jellyfin 12.4's real
  source: copy from upstream, don't invent.
- New behaviour gets a `Check(...)` in `tests/StoryShuffle.Tests/Program.cs`.
- Built-in series rules live in `src/Jellyfin.Plugin.StoryShuffle/BuiltInRules/`, one file per
  series: follow "Adding a series" in `CONTRIBUTING.md`. Keep the README's "How episodes get
  linked" and "Settings" in sync with what the code does.
- The settings page (`Configuration/configPage.html`) runs inside Jellyfin's dashboard,
  which isn't available here. For UI changes, open the HTML file on its own in the browser
  for a screenshot and say so in the pull request.
