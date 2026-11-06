# Chart conversion

PenguinTools conversion behavior and compatibility rules. See [CLI usage](cli.md) for commands and configuration, [format references](formats/README.md) for file syntax, and [architecture](architecture.md#charts) for implementation structure.

## Formats and versions

| Input | Available outputs |
| ----- | ----------------- |
| MGXC, UGC, SUS | MGXC, UGC, C2S |
| C2S | MGXC, UGC |

Editor output has three capability targets, selected with `chart convert --editor-target` or `ChartConvertRequest.EditorTarget`:

| Target | Native output | Intended reader |
| ------ | ------------- | --------------- |
| `legacy` | MGXC v2 / UGC v8 | Margrete 1.8 |
| `standard` | MGXC v4 / UGC v9 | Margrete 1.9 |
| `extended` (default) | MGXC v4 / UGC v9 with supported extensions | Compatible extended editor |

The target does not select an input parser and does not change C2S output. Features that a selected standard or legacy reader cannot reproduce fail with `diag.mgcrit.editor_target_feature`, before the destination is replaced. These include 0/x meters, mixed SLIDE segment visibility and unsupported AIR properties. Legacy output also rejects NCL and out-of-field coordinates; legacy UGC rejects AIR-CRUSH rotation. A custom metronome count requires UGC for these two targets because their native MGXC fields do not express it. Supported curves are sampled for standard/legacy output when needed, with `diag.warn.editor_curves_sampled`; pure legacy Bezier handles can stay editable in legacy MGXC.

MGXC versions above 4 and UGC versions above 9 fail with `diag.error.chart_version_too_new`; [option batches](cli.md#partial-option-batches) can continue with the supported inputs.

The format references describe the source specifications. Implementation coverage is determined by the parsers, writers, and their tests.

Use binary MGXC only. Do not use text MGXC: PenguinTools does not support it, and Margrete has deprecated it.

When saving MGXC or UGC into another directory, relative audio, jacket, background and field-image paths are rewritten to keep referencing the same media files. Absolute paths and same-directory references are retained. Music extraction instead writes references to the media extracted beside the new chart.

## Input compatibility

MGXC v1–4 and UGC v8–9 use a single conversion entry point. Readers resolve record layouts from the file version and individual extensions from their markers, without an Original/Extended application-version selection. A file version is not an editor version: different editors can write the same format version.

UGC v9 always uses musical-bar positions. For v8, explicit `PenguinTools dialect` comments select fixed-width or musical-bar pairs; recognized older exporter comments and fixed-origin metadata provide the legacy fallback. Unmarked files use official semantics. Conflicting coordinate markers fail instead of silently changing timing. The fixed-zero origin and the representation of UGC bar/tick pairs are separate properties.

UGC output explicitly marks musical-bar coordinates so extended readers do not interpret fixed-zero origin metadata as fixed-width v8 positions. Zero-length meters retain their bar numbers and zero duration in native MGXC/UGC fields. A terminal zero-length meter uses an offset from that bar rather than dividing by its length. These meters require an editor that supports the extension; round-trip metadata alone cannot make an unsupported meter work in an older editor.

Optional segment-line, metronome-count, curve-range and AIR height-basis extensions are read independently. Missing fields retain native defaults; malformed fields are errors. Mixed SLIDE visibility is retained in `xns1` MGXC fields or `MGR_NOLINE_SEGMENTS_V1` UGC comments. Native metronome counts override older saved counts without enabling a disabled metronome. Curve metadata is applied only to matching note identities, and unchanged locked curves retain their editable source snapshots when saving MGXC.

Older extended UGC height fields may use a private-use character for the leading base36 digit. The unified reader decodes that existing extension independently of the version's lane encoding; trailing height digits must remain base36. Invalid characters fail at the source line instead of becoming height -1. This permits exact C2S conversion of an existing extended height, but does not enlarge the standard output formats' height ranges below.

MGXC heights use `HSCL` (default 1) to convert to internal tenths. MGXC v4 limits rescaled heights to 0–360 when `HSCL` differs from 10, matching Margrete; its usual `HSCL=10` representation retains larger heights. Nonpositive, nonfinite, or unrepresentable scales fail with `diag.mgcrit.invalid_height_scale`.

The MGXC v4 `xh32` field explicitly identifies the height layout: metadata type 1, value 0 means the native unsigned 16-bit height followed by two reserved bytes; value 1 means a signed 32-bit height. Unknown or conflicting values fail. Without this field, only the recognized older `MGR_ORIGIN=ZERO_V1` extension selects signed heights; `xfld` and nonzero reserved bytes do not. Standard output writes `xh32=0`, and extended output writes `xh32=1`.

C2S `ASO`, `HHD`, and `HHX` objects are not supported. They are skipped with `diag.c2s.unsupported_note_type` warnings; conversion continues with the remaining objects.

UGC output uses `HIPRECISION` (internal heights 0–1295); standard/legacy MGXC output supports internal heights 0–65535, while extended MGXC uses the signed 32-bit range. Out-of-range heights stop export with `diag.mgcrit.unrepresentable_height` instead of silently clipping the playable path. UGC validates the sampled curve, so a higher Bezier handle is allowed when the emitted path fits the range. Round-trip metadata cannot make an unrepresentable height readable by the target player.

UGC `SPDDEF`/`SPDFLD` relative note-speed definitions are retained when writing UGC and in MGXC metadata. Their playback effects are not reproduced by MGXC or C2S output; conversion reports `diag.warn.ugc_relative_speed_not_reproduced` when a non-default definition applies to a note. Unused definitions and lossless UGC saves do not produce this warning.

## Timing and coordinate origins

Editor BPM and speed input/output preserves native float32 rounding at timing and judgement thresholds. UGC numbers first use the native double interpretation. Editor output retains original higher precision values in metadata validated separately for each numeric group; editing a group's native values prevents its restoration.

Numeric values outside the supported range or precision are adjusted with `diag.warn.number_adjusted`, which reports the original and replacement values. Representable speeds, including 10000 and 100000, are preserved. Only when a speed cannot be represented, an excessive magnitude falls back to ±100000, `NaN` to 1, and a tiny nonzero value to ±1e-28. Keeping the sign avoids C2S treating a zero speed modifier differently. Unrepresentable large positive BPM falls back to 100000; other unrepresentable BPM uses 150. Unrepresentable audio offsets and preview positions use 0. Adjusted C2S source records are normalized before retaining round-trip metadata so the original unrepresentable values cannot reappear on export. Malformed numeric text remains an error.

C2S files without `CLK_DEF` keep the metronome disabled during conversion.

For an MGXC/UGC chart using the original relative origin with `sofs`/`SOFFSET` enabled, audio export inserts one measure of silence. Its length uses the initial time signature, independently of the manual audio offset.

Audio preview start and stop receive that measure only for the relative origin. Fixed-zero-origin charts retain their preview positions. The audio cache distinguishes these origins even when their final audio offsets match.

C2S conversion shifts notes, long-note endpoints, speed intervals, and later tempo/meter events by that measure; the initial tempo and meter stay at tick zero. Speed intervals starting at zero remain active during the silence and extend to the shifted endpoint. Conversion diagnostic times refer to the source chart, before this shift; short-segment warnings are determined from the rounded C2S endpoints.

C2S imports retain their coordinates even after edits invalidate saved snapshots. The C2S coordinate origin is retained in MGXC/UGC round-trip metadata.

Standard and extended editor output use an explicitly marked fixed origin. A relative-origin source is translated once: notes and positive event positions advance by the initial measure, positive beat and image-break bar numbers advance by one, and initial events remain at zero. The derived audio delay is folded into the output's manual offset so its actual audio alignment stays equal; the source object and file are untouched. Existing fixed-origin files are not translated again. Opaque legacy-origin data without a validated translation contract is rejected during this migration. The original opening-measure preference is retained separately for round trips.

For example, at an initial BPM of 120 with `SOFFSET` enabled, a 4/4 chart receives 2 seconds of opening silence; a 3/4 chart receives 1.5 seconds. Notes and later timing events move by the corresponding measure. The initial BPM and meter remain at the start, and the manual audio offset is applied independently.

Overlapping C2S SLP/SFL/STP intervals become TIL speeds of `1 + Σ(speed − 1)` at each interval boundary. SLA placement includes generated AIR-CRUSH rungs as well as explicit note endpoints. C2S output assigns higher timeline IDs to nested objects when needed, copying their complete scroll curves without changing editor timelines. Conversion warns only when the resulting scroll curve differs, such as coincident objects with incompatible curves.

Editor input retains speed events after a timeline's last note and on unused timelines. In particular, trailing resets remain editable and preserve finite C2S intervals over repeated MGXC/UGC saves; an actual speed edit still invalidates the corresponding saved interval.

Overlapping DCM intervals use the first active nonzero speed in start-time order, retaining statement order for equal starts. Editor SPDMOD events follow that effective speed; unedited round trips retain the original intervals. Native editor `SPDMOD 0` can stop a note's scrolling, whereas C2S ignores `DCM 0`. C2S conversion reports `diag.warn.zero_note_speed_not_reproduced` when that zero modifier affects a note on a defined, nonzero TIL segment; MGXC and UGC saves preserve it without this warning.

UMIGURI ignores SPDMOD on a timeline without a TIL definition. Margrete can create this state by assigning notes to another timeline without adding scroll-speed events to that timeline, then setting a note-speed modifier. For MGXC and UGC input, C2S conversion reports `diag.warn.editor_undefined_timeline_speed` if a nonzero, nonunit modifier would become active in C2S and change a note's scrolling.

## Approximation and round trips

MGXC retains Bezier handles; UGC and C2S expand supported curves. Legacy Bezier controls retain their interpretation in MGXC, but UGC/C2S export fails with `diag.mgcrit.unsupported_legacy_curve` until their older sampling behavior can be reproduced accurately. Legacy AIR-CRUSH with explicit judgement points survives MGXC/UGC saves; C2S export fails with `diag.mgcrit.unsupported_legacy_crash` rather than replacing its judgement count with zero.

Explicit legacy AIR-CRUSH judgement points cannot be combined with axis rotation or a nonzero automatic interval; such inputs and exports fail with `diag.mgcrit.legacy_crash_attributes`. C2S source statistics are retained only if every gameplay record was interpreted without alteration, including unknown note records; skipped records cannot retain a stale total.

Unsupported geometry, AIR colors, and meter changes are approximated with `diag.warn.editor_approximation`; original values are retained in checksum-validated metadata. Without segment-visibility extensions, SLIDE line visibility follows the start. Recognized mixed-segment extensions preserve each segment's visibility.

Legacy source `COLOR` records survive migration to checked MGXC/UGC metadata only while the imported AIR attributes, complete paths and ground-note attachments remain unchanged. Modern AIR color projections have a separate path check so a native container rewrite or MGXC/UGC export does not discard them merely because the container checksum changed. Presentation edits preserve these colors; AIR or path edits invalidate their restoration so old metadata cannot overwrite the edited chart.

C2S output always uses resolution 384. Coordinates outside that integer grid use the existing nearest-tick rounding rule; automatic input selection does not increase C2S resolution or change rounding. Exact sub-tick equality is therefore not possible for every editor coordinate.

Native Bezier clipping can produce zero-width nonjudging SLIDE/AIR-SLIDE controls. UGC v9 and extended MGXC preserve these controls; C2S accepts their connected continuations while keeping zero and one-column positions distinct. Zero-width heads, ordinary notes, AIR attachments and judgement endpoints fail with `diag.mgcrit.invalid_note_width`. Standard MGXC and legacy output reject existing zero-width controls because native editable nodes cannot preserve them. Standard UGC reports `diag.warn.editor_clipped_control_save`: original Margrete can import the controls, but reopening its saved MGXC can widen them to one column. This compatibility is not a custom zero-width note feature or a guarantee of game playback support.

Changing line visibility within one editor SLIDE preserves each emitted `NCL`/`SLD` marker, but standard C2S connects paths only when their line types match. Each change can create another head and alter intermediate judgements. Conversion reports `diag.warn.c2s_segmented_slide_judgements`; C2S statistics continue to describe the emitted C2S paths. An extended player that joins mixed line types can therefore show a different total.

Some source C2S files declare internally consistent judge totals that the generic calculator cannot reproduce. Their source statements are retained in round-trip metadata unless input parsing skipped unsupported objects or adjusted numeric values. The writer preserves these totals only when every final gameplay record and judge-related header is unchanged, without timing quantization, and restoring source order preserves note paths, AIR attachments and same-tick event precedence. It then restores the original statement order and reports `diag.warn.c2s_source_judgements_retained`. Gameplay edits invalidate this fallback, recalculate totals, and report `diag.warn.c2s_source_judgements_edited`. This protects an unchanged round trip and does not implement the missing game counting rule.

For AIR-HOLD and AIR-SLIDE, Invert is set on the AIR arrow and applies to the entire long note. MGXC input uses the leading AIR record's inversion, and MGXC, UGC, and C2S output retain it for the whole note. The long-note record does not provide an independent body-color setting.

AIR-HOLD input preserves the documented short color field (`I` for inverted), including the short form written by the original editor. For high-precision UGC v4-v8, the explicit legacy padding forms `0I` and `0N` are also accepted. UGC v9 uses only the short color position; coordinate-origin metadata does not change color parsing. Old high-precision readers incorrectly skip one character, so re-importing their own short-form export can lose inversion. PenguinTools retains the export's intended color and writes `II` for inverted Legacy UGC so both old and current readers retain it. Passing an original short-form export through PenguinTools before native re-import avoids that old reader defect.

The older editor's straight-height-eight AIR-SLIDE-to-AIR-HOLD option is an application export setting, not an MGXC chart field. PenguinTools preserves the stored object type and does not infer that setting. Known high-precision UGC exports from this option contain `Hxy28N/I` heads and full AIR-SLIDE child fields. The reader normalizes these only after checking the complete constant-geometry path, retaining its color, timing and node attributes and reporting `diag.warn.ugc_legacy_air_hold_normalized`. Judging children and a final nonjudging control are supported. Incomplete, mixed-encoding or inconsistent blocks, intermediate nonjudging controls and legacy Bezier handles fail with `diag.mgcrit.ugc_legacy_air_hold`; normalizing them could change judgement counts. This recovery does not change ordinary AIR-SLIDE records.

UGC supports a nonjudging AIR-HOLD endpoint, but native readers can treat intermediate AIR-HOLD control points as judgement steps, and editor exports can discard them. UGC export retains those records and reports `diag.warn.ugc_air_hold_controls` because native playback or re-export can change continuous judgements. Ordinary intermediate AIR-ACTION steps and nonjudging endpoints are unaffected; MGXC and C2S retain intermediate controls.

Original Margrete UGC readers turn a final nonjudging ground SLIDE control into a judging endpoint. Standard and legacy UGC output reject that path with `diag.mgcrit.editor_target_feature`; use MGXC to retain the endpoint. Ground SLIDE endpoints use the native control attribute, independently of the AIR long-note end attribute.

Original UGC readers also discard coincident nodes in one SLIDE/AIR-SLIDE path, even when lane, width or height differs. Standard and legacy UGC output require strictly increasing node times and reject such paths before replacing the destination. MGXC preserves the distinct nodes. Quantizing a sampled curve to C2S can create coincident controls; they remain separate records rather than being merged or moved to another tick.

Saved C2S record order is restored only when it preserves paths and AIR relationships. Apart from the guarded unchanged-source fallback above, C2S judgement totals are recalculated from the final emitted records, including accepted AIR attachments, `PROGJUDGE_AER`, and tutorial stop exclusions. Compressed round-trip metadata has a 64 MiB decoded-size limit.

Standard and legacy UGC output reject copyright plus round-trip information above 65535 UTF-8 bytes with `diag.mgcrit.ugc_metadata_too_long`. Original editors copy this value into an unsigned 16-bit MGXC string field and can corrupt an oversized value when saving. Use PenguinTools MGXC output to retain large round-trip payloads in bookmarks. The extended reader moves those payloads into bookmarks before saving, but the user's copyright text itself still has the 65535-byte limit. MGXC narrow strings retain the full unsigned length range; inline numeric fields remain signed. Oversized native text fields fail with `diag.mgcrit.mgxc_string_too_long`. Negative wide-string or block lengths and truncated fields fail with a diagnostic instead of allocating from an invalid length.

AIR-CRUSH axes `N/Y/Z` map to C2S `DEF/AxisY/AxisZ`. The separate C2S `Trace` style uses default X-axis lines in MGXC/UGC, with the original attribute restored from validated metadata. Colors follow palette appearance, including `PNK → Y` and `VLT → B`.

For overlapping UGC attachments at the same position, complete ground-note blocks are ordered so AIR-HOLD precedes AIR-SLIDE, which precedes ordinary upward AIR. This matches the native reader's attachment passes and prevents long AIR notes from taking another note's arrow. Conflicting block-order requirements stop UGC export with `diag.mgcrit.ugc_air_attachment_order`.

AIR attached to an intermediate ground SLIDE node cannot be preserved by the supported native editor formats: MGXC omits that attachment, and UGC readers can attach it to another path. Both outputs reject it with `diag.mgcrit.editor_interior_air` before replacing the destination. A saved C2S snapshot is not evidence that the native file retained its attachment.

MGXC stores EXTAP roles in validated metadata without repurposing native height fields. Note edits invalidate roles in the same timing group; title and artist edits preserve them.

In UGC output, PenguinTools retains original C2S roles in validated metadata. Title and author edits preserve restoration; note edits invalidate the affected group, including AIR attachments and EX coverage. Timing changes invalidate all note snapshots. Meters and SLA regions have separate checks, so unrelated note edits can preserve them.

AIR, SLA and SLP snapshots require matching validation information. Missing or mismatched keys do not authorize restoring old records over the current notes or events. Writers check this before creating new checksums, so saving an edited file cannot make a stale snapshot valid again.

Two examples illustrate which information survives edits:

- After C2S → MGXC, moving a note and converting back to C2S retains the edited position without inserting an additional opening measure, provided the origin metadata remains present.
- After C2S → UGC, unchanged speed events can restore their saved C2S intervals. Editing a speed or reordering events at the same position invalidates the matching snapshot, so conversion uses the edited events.
