# Story Shuffle for Jellyfin 12

Makes the normal **Shuffle** button on a series or season keep multi-part stories together. A random story is picked, then every part plays in order, then the next random story starts.

No new buttons and nothing to change in your apps. The plugin answers the "give me random episodes of this series" request that the shuffle button sends, so it works in the web client and the Android app. It also works in most other apps that ask the server for the shuffle.

## Install

1. Stop Jellyfin.
2. Unzip `StoryShuffle_1.0.0.0.zip` into Jellyfin's `plugins` folder, so you end up with `plugins/StoryShuffle_1.0.0.0/Jellyfin.Plugin.StoryShuffle.dll` and `meta.json` next to it.
   - Docker: `/config/plugins/`
   - Linux package: `/var/lib/jellyfin/plugins/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\`
3. Start Jellyfin. **Dashboard → Plugins** should list *Story Shuffle* as Active.
4. On each device, reload the web client once. On Android, close and reopen the app.

Uninstall by deleting the folder and restarting.

## How episodes get linked

For each series, in this order:

1. **Story lists.** Doctor Who (2005 onwards, including Flux and the 2024–25 seasons) and South Park ship built in. You can add your own.
2. **Titles.** "Part 1 / Part 2", "(1) / (2)", "Episode I / II" and similar on neighbouring episodes. This also covers classic Doctor Who serials named like "Spearhead from Space (1)…(4)".
3. **Descriptions.** "Part two of two", "Conclusion of the two-part…", "To be continued", "Continued from last week"… This is off for Doctor Who and South Park, because their lists are already exact.

Stories you watched in the last 14 days go to the back of the queue. Change the number in the settings, or set 0 to turn it off.

## Settings

**Dashboard → Plugins → Story Shuffle**

- **Check a series.** Pick a series and press *Preview* to see exactly which episodes will play together and why. Use it after adding a show.
- **Your series rules.** A JSON list that extends the built-in ones. Episodes can be given by title or as `S06E01`:

```json
[
  { "series": ["Star Trek: The Next Generation"],
    "groups": [["The Best of Both Worlds (1)", "The Best of Both Worlds (2)"]] },

  { "series": ["South Park"], "seasonsAsStory": [20] },

  { "series": ["Some Show"], "neverGroup": ["Title wrongly linked"], "exclude": ["Clip Show"] }
]
```

Every field: `series` (names, year suffix ignored, or Jellyfin ids), `groups`, `seasonsAsStory`, `neverGroup`, `exclude`, `titleHeuristic`, `overviewHeuristic`, `excludeSpecials`, `enabled` (false = normal shuffle for that show), `replaceBuiltIn`.

## Limits

- The web and Android apps load 100 episodes per shuffle. That's Jellyfin's own limit; the plugin fills it with whole stories.
- Apps that download the whole episode list and shuffle it on the device won't be affected. Infuse is one of these.
- Early 1960s Doctor Who episodes with individual titles (e.g. "The Cave of Skulls") aren't detected. Add them as a rule if you have them.

## Building

`dotnet build -c Release` in `src/Jellyfin.Plugin.StoryShuffle`. It needs no NuGet access: the `ref/` folder holds compile-only stand-ins for the few Jellyfin types the plugin uses. `tests/` builds those types from Jellyfin 12.4's real source and runs the plugin end to end against a local web server: `dotnet run -c Release` in `tests/StoryShuffle.Tests`.
