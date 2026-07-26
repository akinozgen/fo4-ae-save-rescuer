# Fallout 4 Save Rescuer

Bring back a Fallout 4 save that no longer loads because mods it was built with are gone -
by rewriting the save so it does not ask for them any more.

![screenshot](docs/screenshot.png)

## The problem

A save records every plugin that was active when it was written, and the objects, actors and
world changes those plugins owned. When some of those plugins are missing, the game is supposed
to warn you and let you continue. On current builds that path is unreliable: the warning appears,
the interface starts misbehaving, and you end up anywhere but in your game. Even when the save
does open, the next save written over it often will not load at all.

Reinstalling the exact mod list is the textbook answer. It is also frequently impossible - mods
get deleted from the internet, versions move on, and load orders from years ago cannot be
reproduced.

## The solution

Take the missing content out of the save itself. Three things have to happen together:

* the missing names are removed from the save's plugin lists, which renumbers the ones that stay;
* every form ID in the save is rewritten to its new plugin index, or cleared if its plugin is
  gone - **without moving any slot**, because other sections of the save index that array by
  position;
* every change record that referred to a cleared slot is dropped.

The result is a save that depends on nothing but the plugins you still have. Your character,
level, location, quest state and everything owned by surviving plugins come with you. Objects
that belonged to the missing mods do not - they were the problem.

The original file is never written to. The rescued save is written as a new file in a free manual
slot, and its in-game name is prefixed (`[RESCUED]` by default) so you can tell it apart in the
load menu.

## Usage

1. Close the game.
2. Start `FO4SaveRescuer.exe`. Your saves are listed with what each one is missing.
3. Pick a save - or drop a `.fos` file onto the window. The panel on the right shows the
   character, the plugins the save refers to, and how the rescue is likely to go.
4. Press **Rescue this save**, review the plan, then **Perform rescue**.
5. Launch the game and load the save with the `[RESCUED]` prefix.

Select several saves at once to work through a batch. **Backups** lists the copies taken before
each rescue and puts any of them back.

### Options

| Option | What it does |
| --- | --- |
| In-game name prefix | Text put in front of the load-menu name. Empty leaves the name alone. |
| Remove every mod, not just the missing ones | Produces a save that needs only the base game and its DLC. Use it when the load order cannot be trusted at all. |
| Keep a copy of each original first | Copies the untouched save into the backup folder before writing. |
| First slot number | Rescued saves are written as `Save<N>` starting here, clear of the numbers the game hands out. |

### Folders

The game's `Data` folder and your save folder are detected on first run; override them under
**Settings**. Backups live under `%LOCALAPPDATA%\FO4SaveRescuer` on purpose - the save folder in
`Documents\My Games` is synced to the cloud and rotated by the game, which is no place to keep
the only copy of something.

## What to expect

Whether a rescued save holds up depends on how heavy the save is, not on how many plugins were
missing. From measured runs:

* saves up to roughly 100,000 change forms came back reliably, including one that was missing
  255 plugins and one written in Survival mode;
* a save with 196,000 change forms (49 MB) loaded, played, and then rejected the next save
  written over it. The interface flags saves in that range before you start.

The number the tool shows in the **Change forms** column is the one that matters.

## Requirements

* Windows, .NET 9 (the published build is self-contained)
* No script extender, no in-game component, nothing installed into the game folder

## Building from source

```
dotnet build
dotnet test
dotnet publish src/SaveRescuer.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Layout:

```
src/SaveRescuer.Core    save format, diagnosis, surgery, backup vault
src/SaveRescuer.App     WPF interface
tests/SaveRescuer.Tests unit tests on synthetic saves - no game data needed
```

The parser round-trips an untouched save byte for byte; that test is the foundation everything
else rests on.

## Releases

Pushing a tag such as `v2.0.0` builds and publishes a self-contained Windows executable through
GitHub Actions; see `.github/workflows/release.yml`.

## Limitations

* Content from missing mods cannot be recovered. This makes the save playable, not complete.
* If your character is standing in a location a missing mod added, the game may struggle to place
  them. Rescue an earlier save, or move somewhere vanilla from the console before saving again.
* Papyrus script data is left untouched. Script instances belonging to removed mods stay in the
  file as orphans; the game discards them on load, but a dedicated save editor is the tool for
  cleaning those out.
* Very heavy saves may still fail after the operation - see *What to expect*.

## License

MIT - see [LICENSE](LICENSE).
