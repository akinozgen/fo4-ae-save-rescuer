# Changelog

## 1.0.0

First release.

* Reads Fallout 4 save files (`.fos`): character details, embedded screenshot, and both the
  regular and light plugin tables written by next-gen builds.
* Diagnoses a save against the current setup: missing plugins, plugins present but not enabled,
  existing placeholders, base-game masters.
* Creates empty placeholder plugins for missing entries and enables the load-order lines the
  save expects, so the save opens without the missing-content warning.
* One-step undo: removes generated placeholders and restores the backed-up load order.
* Auto-detects the game folder, load order file and saves folder; Mod Organizer 2 profiles are
  listed as load-order candidates. All paths can be overridden.
* Refuses to modify anything while Fallout 4 is running.
* Save files are never written to.
