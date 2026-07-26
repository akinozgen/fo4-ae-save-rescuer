# Changelog

## 2.0.0

The tool now rewrites the save instead of working around it. The 1.0 approach - generating empty
placeholder plugins so the game would find every name the save listed - opened saves but left the
session broken: saves written afterwards would not load. It has been removed entirely.

* Full save (`.fos`) parser and writer. An untouched save is rebuilt byte for byte, which is what
  makes it safe to change one part of it and leave the rest alone.
* Surgery: missing plugins come out of the save's plugin lists, form IDs are renumbered to the new
  indices or cleared when their plugin is gone, and change records referring to cleared slots are
  dropped. Form ID slot positions never move, because other sections index that array by position.
* Every result is verified before it is offered: section layout, a complete walk of the change
  records, no reference to a cleared slot, no plugin index past the end of the list.
* Rescued saves are written as new files in free manual slots, with the in-game name prefixed
  (`[RESCUED]` by default) so they are recognisable in the load menu. Old-style file names are
  normalised, including the Survival marker.
* Vanilla mode strips every plugin outside the base game and its DLC, for load orders that cannot
  be reconstructed at all.
* Backup vault: originals are copied outside the game's folder before anything is written, listed
  with the character they belong to, and can be restored in place or elsewhere. Identical content
  is stored once.
* Save list reads a folder of saves without loading them whole, and shows what each is missing
  along with a plain-language read on how the rescue is likely to go, based on the save's weight.
* Tag mode writes a copy whose load-menu name carries a tag with the contents untouched - useful
  for telling an original apart from a rescued one.
* Batch: select several saves and work through them in one pass.
* Interface reworked around the save list: search, character and status filters, the save's own
  screenshot, per-plugin state, and the game launcher (script extender first when installed).

## 1.0.0

First release. Superseded by 2.0.0; the placeholder-plugin approach it was built on does not hold
up in practice and is no longer part of the tool.
