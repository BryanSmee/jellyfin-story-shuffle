# Story Shuffle: project notes

Jellyfin 12 plugin in C# (.NET 8). User-facing text is English.

[CONTRIBUTING.md](../CONTRIBUTING.md) is the contributor guide: read it before working. It
has the two checks to run before pushing ("Build and test", the same ones CI runs), the rules
for `nuget.config`, `ref/` and `tests/realref/`, adding a series to `BuiltInRules/` ("Adding a
series") and the rules for code changes ("Code changes"). These notes only add what a
sandboxed run needs on top of it:

- The settings page (`Configuration/configPage.html`) runs inside Jellyfin's dashboard,
  which isn't available here. For UI changes, open the HTML file on its own in the browser
  for a screenshot and say so in the pull request.
- A new series can't be checked with *Preview* on a real library here: say so in the pull
  request, so the maintainer runs that step.
