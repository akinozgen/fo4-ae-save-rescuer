# Changelog

## 1.0.0

First release.

* Full Fallout 4 save (`.fos`) parser and writer. An untouched save is rebuilt byte for byte,
  which is what makes it safe to change one part of it and leave the rest alone.
* Rescue: missing plugins are removed from the save's plugin lists, form IDs are renumbered to
  their new indices or cleared when their plugin is gone, and change records referring to cleared
  slots are dropped. Slot positions in the form ID array never move, because other sections of the
  save index that array by position.
* Every result is verified before it is offered: section layout, a complete walk of the change
  records, no reference to a cleared slot, no plugin index past the end of the list.
* Rescued saves are written as new files in free manual slots, with the in-game name prefixed
  (`[RESCUED]` by default) so they are recognisable in the load menu. Old-style file names are
  normalised, including the Survival marker.
* Vanilla mode strips every plugin outside the base game and its DLC, for load orders that cannot
  be reconstructed at all.
* Backup vault: originals are copied outside the game's folder before anything is written, listed
  with the character they belong to, and restored in place or elsewhere on demand. Identical
  content is stored once.
* Save list reads a folder of saves without loading them whole, showing what each one is missing
  and a plain-language read on how a rescue is likely to go, based on the save's weight.
* Tag mode writes a copy whose load-menu name carries a tag with the contents untouched - useful
  for telling an original apart from a rescued one.
* Batch: select several saves and work through them in one pass.
* Game launcher, with the script extender preferred when it is installed.
* Original save files are never written to.
