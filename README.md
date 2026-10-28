# Story Shuffle for Jellyfin 12

Makes the normal **Shuffle** button on a series or season keep multi-part stories together. A random story is picked, then every part plays in order, then the next random story starts.

No new buttons and nothing to change in your apps. The plugin answers the "give me random episodes of this series" request that the shuffle button sends, so it works in the web client and the Android app. It also works in most other apps that ask the server for the shuffle.

## Install

### From Jellyfin's catalog (recommended)

1. **Dashboard → Plugins → Repositories**: add a repository named `Story Shuffle` with the URL
   `https://bryansmee.github.io/jellyfin-story-shuffle/manifest.json`.
2. **Catalog**: install *Story Shuffle*, then restart Jellyfin. New versions show up there as updates.
3. On each device, reload the web client once. On Android, close and reopen the app.

### By hand

1. Stop Jellyfin.
2. Download `StoryShuffle_<version>.zip` from the [latest release](https://github.com/BryanSmee/jellyfin-story-shuffle/releases/latest) and unzip it into Jellyfin's `plugins` folder, in a folder of its own: you end up with `plugins/StoryShuffle_<version>/Jellyfin.Plugin.StoryShuffle.dll` and `meta.json` next to it.
   - Docker: `/config/plugins/`
   - Linux package: `/var/lib/jellyfin/plugins/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\`
3. Start Jellyfin. **Dashboard → Plugins** should list *Story Shuffle* as Active, with that version.
4. On each device, reload the web client once. On Android, close and reopen the app.

Uninstall by deleting the folder and restarting.

## How episodes get linked

For each series, in this order:

1. **Story lists.** Built-in lists for Doctor Who (2005 onwards, including Flux), Doctor Who (2023 onwards) and South Park. You can add your own in the settings. Each episode in a list is found by its TheTVDB id first, so the lists work whatever your metadata language and however your provider numbers the seasons. Both the TheTVDB plugin and Jellyfin's built-in TMDB provider store that id. An episode without it is found by its English title.

   A built-in list finds its series by TVDB, TMDB or IMDb id, never by name, so the 2005 list leaves Doctor Who (1963) and (2023) alone. A series with none of its TVDB, TMDB or IMDb ids (not identified yet) gets no built-in list, and neither does any series when the plugin can't read the series' ids from Jellyfin: the *Preview* says so. The 2024–25 seasons are in both Doctor Who lists, because libraries scraped before TVDB split off the 2023 series have them as seasons 14 and 15 of the 2005 one.
2. **Titles.** "Part 1 / Part 2", "(1) / (2)", "Episode I / II" and similar on neighbouring episodes. This also covers classic Doctor Who serials named like "Spearhead from Space (1)…(4)". The same markers work in French, German, Spanish, Italian, Dutch and Portuguese, accents or not, all at once:

   | Language   | Words before the number            | Numbers in words           | "of"  |
   | ---------- | ---------------------------------- | -------------------------- | ----- |
   | English    | part, pt., episode, ep., chapter   | one … ten                  | of    |
   | French     | partie, épisode, chapitre          | un/une, deux … dix         | sur   |
   | German     | Teil, Folge, Kapitel               | eins, zwei … zehn          | von   |
   | Spanish    | parte, episodio, capítulo          | uno/una, dos … diez        | de    |
   | Italian    | parte, episodio, capitolo          | uno/una, due … dieci       | di    |
   | Dutch      | deel, aflevering, hoofdstuk        | één, twee … tien           | van   |
   | Portuguese | parte, episódio, capítulo          | um/uma, dois/duas … dez    | de    |

   So "Le Jour du Docteur (Partie 1) / (Partie 2)", "Die Flut – Teil eins / Teil zwei" and "El final, parte 1 / parte 2" link. Digits and Roman numerals work with any of these words. Keywords and number words mix across languages ("Hot Shots! Part Deux" is part 2). A number word only counts after one of the keywords, except English ones in brackets ("The End of Time (Two)"): "Uno" or "Le Casse (Un)" link nothing.
3. **Descriptions.** "Part two of two", "Conclusion of the two-part…", "To be continued", "Continued from last week"… and in the other languages:

   | Language   | Links to the next episode                                        | Links to the previous episode                                                   |
   | ---------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------- |
   | French     | "À suivre" (as the last sentence), "Suite au prochain épisode"             | "Suite et fin", "Deuxième / Seconde / Dernière partie…", "Suite de l'épisode précédent" |
   | German     | "Fortsetzung folgt"                                              | "(Der) zweite / letzte Teil…", "Fortsetzung der vorherigen Folge"               |
   | Spanish    | "Continuará" (as the last sentence), "(Continuará)"                        | "Segunda / Última parte…", "Continuación del episodio anterior"                 |
   | Italian    | "Continua…" (as the last sentence), "(continua)", "Il seguito alla / nella prossima puntata" | "Seconda / Ultima parte…", "Seguito della puntata precedente"         |
   | Dutch      | "Wordt vervolgd" (as the last sentence)                                    | "Tweede / Laatste deel…", "Vervolg op de vorige aflevering"                     |
   | Portuguese | "(Continua)", "Continua no próximo episódio"                     | "Segunda / Última parte…", "Continuação do episódio anterior"                   |

   "Part 2 of 2" and "Part 1/2" work with the words of the titles table ("Teil 2 von 2", "partie 1 sur 2"). "Second part…" phrases only count at the start of the description. This is off for Doctor Who and South Park, because their lists are already exact.

   To add a language, see [Adding a language](CONTRIBUTING.md#adding-a-language) in CONTRIBUTING.md.

Stories you watched in the last 14 days go to the back of the queue. Change the number in the settings, or set 0 to turn it off.

## Settings

**Dashboard → Plugins → Story Shuffle**

- **Check a series.** Pick a series and press *Preview* to see exactly which episodes will play together and why. Use it after adding a show.
- **Log every shuffle in detail.** Off by default. Every shuffle the plugin takes over always writes one line to the server log (**Dashboard → Logs**), like `Story Shuffle: Doctor Who, 180 episodes → 112 stories, returned 100`. With this on, that line also gives the series' Jellyfin and provider ids, the rules used, how many recently watched stories went to the back and how long it took, step by step, and a shuffle request the plugin leaves to Jellyfin says why (plugin turned off, *Also handle shuffle requests from other apps* off, no `parentId`, not episodes only, Jellyfin's answer not usable). It also says so for an episode list the plugin would have taken over but that wasn't asked for in random order (no `sortBy=Random`), in case an app shuffles some other way, so expect a line each time a season or episode list is opened. Without it those details are logged at Debug level, which Jellyfin hides unless you raise it in `logging.default.json`. Invalid JSON in *Your series rules* writes one warning with the error when you save. The log never holds API keys or tokens: of the request, only its path, cleaned and cut short.
- **Your series rules.** A JSON list that extends the built-in ones. Episodes can be given by title, as `S06E01`, or by provider id like `tvdb:12345` or `tmdb:67890` (Jellyfin's provider names: `tvdb`, `tmdb`, `imdb`, any case). Separate alternatives with `|`: `"tvdb:12345 | tmdb:67890 | The Empty Child"` uses the first one the episode has, ids before titles. Because `|` separates alternatives, a title can't contain one.

```json
[
  { "series": ["Star Trek: The Next Generation"],
    "groups": [["The Best of Both Worlds (1)", "The Best of Both Worlds (2)"]] },

  { "series": ["South Park"], "seasonsAsStory": [20] },

  { "series": ["Some Show"], "neverGroup": ["Title wrongly linked"], "exclude": ["Clip Show"] }
]
```

Every field: `series` (names, provider ids like `"tvdb:78804"` or `"tmdb:57243"`, or Jellyfin ids; a name without a year like `"Doctor Who"` matches every year, a name with one like `"Doctor Who (2005)"` only the series from that year: the year in the series' name in Jellyfin or, when it has none, its production year, so use a provider id when those differ), `groups`, `seasonsAsStory`, `neverGroup`, `exclude` (episodes given as above), `titleHeuristic`, `overviewHeuristic`, `excludeSpecials`, `enabled` (false = normal shuffle for that show), `replaceBuiltIn`.

## Limits

- The web and Android apps load 100 episodes per shuffle. That's Jellyfin's own limit; the plugin fills it with whole stories.
- Apps that download the whole episode list and shuffle it on the device won't be affected. Infuse is one of these.
- Early 1960s Doctor Who episodes with individual titles (e.g. "The Cave of Skulls") aren't detected. Add them as a rule if you have them.

## Contributing

Wrong links, a series to add, a pull request: see [CONTRIBUTING.md](CONTRIBUTING.md), which also covers building, testing and releasing.
