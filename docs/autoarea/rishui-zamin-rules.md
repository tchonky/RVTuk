# Rishui Zamin area robot — input rules reference

> The complete, compiled rulebook for what the national area-calculation robot
> (**רכיב אוטומטי לחישוב שטחים** in רישוי זמין) accepts as input. One rule per line, each
> tagged with its provenance. The research trail behind every rule lives in
> [`rishui-zamin-notes.md`](rishui-zamin-notes.md); this file is the distilled reference the
> generator (`RVTuk.Core.AreaSubmission`) is built against. Compiled 2026-07-02; updated
> 2026-07-03 (Oct-2025 official code table incl. 117-119, total-area method list,
> Jerusalem/Tel-Aviv municipal robots researched, marker form now selectable).
>
> **Provenance tags:**
> `[OFFICIAL]` Planning Administration spec (2018 v2.0) / FAQ / regulations / gov.il robot
> pages (checked 2026-07-03) ·
> `[SAMPLE]` reverse-engineered from the three real submission sets in
> `tests/Examples Autoarea/` ·
> `[TEKENPLUS]` measured from a tekenplus-generated file (`tests/output/Export_example.dxf`) ·
> `[ACAD]` established by AutoCAD 2025/2026 `DXFIN` acceptance testing ·
> `[RVTUK]` our implementation's choice where the robot allows several ·
> `[OPEN]` unresolved — see §13.

---

## 1. Submission package

| # | Rule | Source |
|---|------|--------|
| 1.1 | The upload is a **ZIP** containing exactly one of two forms: **(a)** `.dwfx` + `.dwg` (DWG has the DWFX attached as xref) — the official default; **(b)** `.dwfx` + `.dxf` + `.dat` — the alternative form. | [OFFICIAL] |
| 1.2 | All real-world tools observed (incl. tekenplus) emit the **3-file form: `.dwfx` + `.dxf` + `.dat`**, all three sharing one base name. This is what RVTuk emits. | [SAMPLE] |
| 1.3 | Files sit **directly in the ZIP** (no subfolder); names use letters/digits with **no spaces**; any path to the `.dwfx` must be relative or absent. | [OFFICIAL] |
| 1.4 | Submission UI inputs typed by the editor (not in the files): **plot area (שטח המגרש)** and **requested scale (קנ"מ)**, usually 1:100. | [OFFICIAL] |

## 2. DXF file — format level

| # | Rule | Source |
|---|------|--------|
| 2.1 | Both **ASCII DXF and binary DXF** are accepted. Observed versions: AC1015 (binary, tekenplus and one sample) and AC1032 (ASCII, RVTuk). | [SAMPLE] [TEKENPLUS] |
| 2.2 | Drawing units are **centimetres at 1:1** real-world scale (a 4 m wall is 400 units). | [OFFICIAL] |
| 2.3 | Geometry must sit in a **positive, right-angle coordinate grid** within Israel's national bounds (relevant when anchoring; in practice keep all coordinates ≥ 0). | [OFFICIAL] |
| 2.4 | For AC1015+ files, **every entity needs a unique group-5 handle**, and `$HANDSEED` must exceed every handle used — AutoCAD (and hence any ODA-based pipeline) rejects the file otherwise: *"Handle missing … Invalid or incomplete DXF input — drawing discarded"*. | [ACAD] |
| 2.5 | The file must contain an **OBJECTS section with the root NamedObject dictionary** (plus the LAYOUT objects the `*Model_Space`/`*Paper_Space` BLOCK_RECORDs point to). Missing ⇒ *"File lacks the NamedObject dictionary"* rejection. | [ACAD] |
| 2.6 | Hebrew text: AC1032 ASCII DXF carries raw **UTF-8** (verified round-trip); AC1015 files use `\U+XXXX` escapes. Either is read correctly. | [ACAD] [TEKENPLUS] |
| 2.7 | Entity colours are **ByLayer (62 = 256)**; the robot assigns colours from `USAGE_TYPE` itself. No hatches/fills in the calc file. | [SAMPLE] [OFFICIAL] |

## 3. Layers

| Layer | Purpose | Mandatory | Source |
|-------|---------|-----------|--------|
| `RZ_FRAME` | production frame (sheet outline) | **yes** | [OFFICIAL] |
| `RZ_FLOOR` | floor boundary polygons | yes | [OFFICIAL] |
| `RZ_AREA` (+ any `RZ_AREA*`) | area polygons | layer optional, but this is the payload | [OFFICIAL] |
| `RZ_LANDCOVER` | coverage/footprint perimeter, no marker | optional (absent in all samples) | [OFFICIAL] |
| `RZ_ANCHOR` | geographic anchor | optional | [OFFICIAL] |

## 4. Geometry & topology

| # | Rule | Source |
|---|------|--------|
| 4.1 | Every frame/floor/area polygon is a **closed `LWPOLYLINE`** (group 70 = 1, group 90 = vertex count) on its layer. Any closed shape is allowed for floors/areas (incl. curved); samples reach 30+ vertices. | [OFFICIAL] [SAMPLE] |
| 4.2 | **Containment hierarchy:** frame ⊃ floor polygons ⊃ area polygons. A frame must not cross a floor polygon; floors must not overlap each other; areas must not overlap each other. | [OFFICIAL] |
| 4.3 | **`RZ_FRAME` is a rectangle only**, bottom-left at (0,0) in every observed file. It represents the **physical sheet**: e.g. 40000 × 9000 cm = 4.00 m × 0.90 m of paper at 1:100 (tekenplus), 27000 × 9000 (Garmoshka). | [OFFICIAL] [SAMPLE] [TEKENPLUS] |
| 4.4 | Frame print-size caps at output scale: **height ≤ 910 mm; length ≤ 15,000 mm; the PAGE_NO=1 sheet ≤ 14,500 mm**. Long submissions split into several frames. | [OFFICIAL] |
| 4.5 | Multi-frame drawings read **right-to-left: the main frame (PAGE_NO = 1) must be the right-most**. | [OFFICIAL] |
| 4.6 | Floors are laid out side-by-side inside the frame (the old garmoshka strip); tekenplus orders them **ascending elevation right-to-left**. Exact gaps/margins are free — only containment and non-overlap are enforced. RVTuk uses the arrangement of the Revit sheet's viewports verbatim. | [TEKENPLUS] [RVTUK] |
| 4.7 | Each polygon carries **exactly one marker** (block insert or TEXT, §5) whose anchor point lies **strictly inside** the polygon — the robot matches marker→polygon by point-in-polygon. Beware: a concave polygon's centroid/vertex-average can fall outside. | [OFFICIAL] [TEKENPLUS] |
| 4.8 | `RZ_LANDCOVER`: closed polygon per relevant level, contained in the floor polygon, **no marker**; only its perimeter is used. | [OFFICIAL] |
| 4.9 | `RZ_ANCHOR`: if used, **exactly one anchor point inside each floor boundary**. | [OFFICIAL] |

## 5. Markers — two accepted encodings

The tag/value payload (§6) is identical in both; only the carrier differs. RVTuk emits
**either form**, chosen by the "Marker format" radio in the Area Calc Config pane —
**Official** (Form A, the default) or **Old** (Form B); both outputs pass the AutoCAD 2025
`DXFIN` acceptance test (verified 2026-07-03). `[RVTUK]` `[ACAD]`

**Form A — block inserts** (official spec wording; Garmoshka sample; the encoding the
current official guide still teaches, incl. a downloadable block set): an `INSERT` of
`RZ_FRAME_SYM` / `RZ_FLOOR_SYM` / `RZ_AREA_SYM` / `RZ_ANCHOR_SYM_RUNTIME` with
attributes-follow flag (66 = 1), one `ATTRIB` per tag, closed by `SEQEND`; the block's
insertion point is the in-polygon anchor. Attribute metrics mirror the block ATTDEFs
(frame/floor tags: height 2.667, right-justified; area/anchor tags: height 1.0,
middle-justified; `AREA`, `ASSET`, `COORD_X`, `COORD_Y` carry the invisible flag 70 = 1).
ATTRIBs sit on layer `0` owned by their INSERT; the sample insets `RZ_FRAME_SYM`/
`RZ_FLOOR_SYM` 5 units off the box's top-right corner. Note the anchor block is really named
`RZ_ANCHOR_SYM_RUNTIME`, not the spec's `RZ_ANCHOR_SYM`. `[OFFICIAL]` `[SAMPLE]`

**Form B — plain TEXT** (tekenplus; RVTuk's "Old" option): one left/baseline
`TEXT` entity per polygon, **on the polygon's own layer**, content = tag/value pairs joined
with `&&&` (`USAGE_TYPE=108&&&USAGE_TYPE_OLD=&&&AREA=&&&ASSET=`). Measured constants:
text height **2.0**; frame label anchored **10 units** inside the frame's top-right corner;
floor label **25 units** inside its box's top-right corner; area label at an interior point.
Glyph overflow past the polygon is harmless — only the anchor matters. `[TEKENPLUS]` `[RVTUK]`

## 6. Tags and value formats

**Frame (`PAGE_NO`)** `[OFFICIAL]`

| Tag | Value | Rules |
|-----|-------|-------|
| `PAGE_NO` | integer as text | mandatory; sheet number; 1 = main (right-most) frame |

**Floor (`RZ_FLOOR_SYM` / floor TEXT)** `[OFFICIAL]`, formats `[TEKENPLUS]`

| Tag | Value | Rules |
|-----|-------|-------|
| `BUILDING_NO` | integer as text | mandatory, default 1; multi-building projects give each building its own number — the robot then emits a per-building area table plus a request-wide summary |
| `FLOOR` | text | mandatory; level name; comma-separated list for typical floors |
| `LEVEL_ELEVATION` | metres, **two decimals** (`-4.90`, `0.00`, `4.00`) | mandatory; relative to the determining entrance level (מפלס הכניסה הקובעת); comma-list must match `FLOOR`'s count |
| `IS_UNDERGROUND` | `0` / `1` | mandatory; default 0 |

**Area (`RZ_AREA_SYM` / area TEXT)** `[OFFICIAL]`

| Tag | Value | Rules |
|-----|-------|-------|
| `USAGE_TYPE` | usage code (§7) as text | **proposed** usage — official prompt: "קוד שימוש מוצע" |
| `USAGE_TYPE_OLD` | usage code as text, or empty | usage **as existing in the current permit** ("קוד שימוש כפי שקיים בהיתר") — empty for new work; never a mirror of `USAGE_TYPE` |
| `AREA` | m² as text, or empty | area as stated in the old permit ("שטח כפי שקיים בהיתר מ"ר") — manual/historical, marked `*` in output, **not recomputed**; empty in every real sample |
| `ASSET` | unit number as text, or empty | dwelling-unit / unit number ("מס' יח"ד/יחידה"); optional at this stage |

Combination rules `[OFFICIAL]`:
- **At least one** of `USAGE_TYPE` / `USAGE_TYPE_OLD` must have a value.
- New/proposed area → `USAGE_TYPE` only. Existing-in-permit → `USAGE_TYPE_OLD` only.
- **Change of use** → both filled (different codes).
- **Demolition** → process code (300–302) in `USAGE_TYPE` + the demolished usage in `USAGE_TYPE_OLD`.

**Anchor (`RZ_ANCHOR_SYM_RUNTIME`)** `[SAMPLE]` — optional; undocumented in the spec

| Tag | Value | Rules |
|-----|-------|-------|
| `COORD_X`, `COORD_Y` | ITM (Israeli New Grid) metres as text | georeferences the anchor's drawing-space position |

## 7. Usage code catalog `[OFFICIAL — "טבלת סוגי שימושים", 20.10.2025 edition]`

Source: `usage_codes_and_printing_guide.pdf` on the gov.il robot page (updated 24.02.2026,
fetched 2026-07-03). Supersedes the 2018 spec table: codes **117/118/119** were added after
amendment 163 to the Planning and Building Law (22.07.2025, extended ממ"ד areas).

**שטחים עיקריים — primary:** 1 מגורים · 2 מסחר והסעדה · 4 תעשייה ומלאכה · 6 חקלאות ·
7 משרדים ותעשיות עתירות ידע · 8 מלונאות ובתי אירוח אחרים · 9 נופש וספורט · 10 מבני ציבור ודת ·
11 פנאי ותרבות · 12 מוסדות חינוך · 13 מוסדות בריאות · 14 מבני חרום וכליאה ·
15 מבני תחבורה, מבני דרך ותדלוק · 16 מבנים טכניים, תשתיות ושמירה · 30 מרפסת · 31 אחסנה ·
32 מצללה · 33 חניה

**שטחי שירות — service:** 101 מרחב מוגן דירתי – שטח רצפה · 102 מרחב מוגן דירתי – שטח קירות ·
**117 מרחב מוגן דירתי – מבואה בתוספת ממ"ד · 118 חדר רטוב לממ"ד · 119 הרחבת ממ"ד** ·
103 מרחב מוגן קומתי/מוסדי/מקלט/מבנה שמירה · 104 מעלית · 105 מבואות וחדרי מדרגות ·
106 קומת עמודים מפולשת ומקמרות · 107 מעברים לכלל הציבור · 108 מערכות טכניות ומבני שירות ·
109 חדרי שירות משותפים · 110 מרפסת · 111 מרתף · 112 חניה · 113 עובי קירות ·
114 בליטות, גגונים וקירוי · 115 אחסנה · 116 מבני שמירה · 130 אחר מתוקף תכנית

**אחר — other (excluded from the floor summary):** 250 מרפסת זיזית · 252 שטח מרוצף לא
מקורה · 255 מצללה · 256 בריכת שחיה · 257 בליטות, גגונים וקירוי

**ללא צביעה — process markers:** 300 הורדה · 301 הריסה ופירוק · 302 חפירה

ממ"ד mechanics `[OFFICIAL]`: floor ≤ 9 m² of code 101 counts as service, the excess moves to
primary automatically; walls (102) are all service; a ממ"ד wet room ≤ 3 m² is code 118 and a
further ≤ 3 m² extension is code 119 (amendment 163). A מרתף-ממ"ד whose excess should stay
service is split into polygons < 9 m² each. Elevator (104) is service at its lowest level and
הורדה (300) at every other level.

Caveats: codes 3, 5, 120–129, 251, 253–254 are absent from the Oct-2025 official table too —
the gaps are intentional, not an extraction artifact. Several Hebrew names repeat across
kinds (מרפסת 30/110/250, אחסנה 31/115, חניה 33/112, מצללה 32/255, בליטות… 114/257) —
**name→code resolution is ambiguous; the code is the source of truth.**

### 7b. Total-area method (שטח כולל) code list `[OFFICIAL — rz_usage_codes_total_area.pdf]`

Since the 2023 amendment of תקנה 9, new-construction permits may be calculated בשטח כולל.
File preparation is **identical**; only the codes differ, and the robot detects the method
from the codes used — **mixing codes from both lists fails the calculation**. The list
(fetched 2026-07-03): covered 401–416 mirror primary 1–16, plus 432 מצללה סטייה ביחס למותר;
service 501/502/518/519/503–516 mirror 101/102/118/119/103–116, plus 530 אחר; uncovered
650 מרפסת זיזית · 652 שטח מרוצף · 655 מצללה · 656 בריכת שחיה · 657 בליטות גגונים קירוי ·
658 מרפסת גג · 659 גג טכני · 660 תאים פוטו-וולטאים · 661 חצר אנגלית; removals 700/701/702
mirror 300/301/302. RVTuk's catalog/key schedules cover the separation method only.

## 8. DAT file `[SAMPLE — byte-identical in all three sets]`

A 14-byte ASCII text file, one tab-separated key/value line, bare LF:

```
DWFX_SCALE→10⏎        (bytes: 44 57 46 58 5F 53 43 41 4C 45 09 31 30 0A)
```

`DWFX_SCALE` relates DXF drawing units to the DWFX sheet; value `10` in every 1:100 sample.
Its meaning for other scales is unconfirmed (`[OPEN]` — verify before submitting a non-1:100
job).

## 9. DWFX file `[OFFICIAL]` `[SAMPLE]`

The plotted sheet background (floor layouts, תנוחת הקומות) the robot draws over: a standard
Autodesk **OPC/XPS ePlot package** (ZIP, `FixedPage.fpage` vector markup). 1:1 centimetres,
no area fills except cut elements. Export from CAD (DWF → re-save as DWFX via Design
Review/TrueView if needed); not hand-generated. The robot's **output** is also a DWFX (the
coloured product with the area table).

## 10. Municipality scope — Jerusalem and Tel-Aviv `[OFFICIAL]` (researched 2026-07-03)

The rules above are the **national robot's**. The Planning Administration's own robot FAQ
states verbatim: *"בוועדות המקומיות ירושלים ותל-אביב משתמשים ברובוט חישוב שטחים אחר. אנא
פנו לאתר העירוני להדרכה. בעתיד יתחברו כל הועדות לרובוט הארצי של מינהל התכנון"* — both
committees run a **different area robot**, municipal guidance applies, and the declared
end-state is that every committee eventually joins the national robot.

What the municipal sites actually publish (checked 2026-07-03):

- **Tel-Aviv–Yafo** — fully independent online permitting system
  (`rishuybniya.tel-aviv.gov.il`, smart-card authenticated, predates רישוי זמין). The
  submission package differs structurally from the national one: the main plan (תכנית
  ראשית, "מוצג 100") is uploaded as **DWF**, and the area-calculation file ("מוצג 150") as
  **DWG** — no DXF/DAT/ZIP form. General drawing constraints match the national ones
  (layers exactly on the main-plan outline, positive right-angle coordinate grid). The
  detailed layer/block/code spec is only taught inside their gated training module
  ("רישוי מקוון | חישוב שטחים", a Storyline course at www5.tel-aviv.gov.il/RISHUYM) and the
  system itself; no public PDF equivalent of the national spec exists.

- **Jerusalem** — independent online system (`jerrishuybniyaonline.jerusalem.muni.il`,
  login-gated). Public guidance is a set of official training videos (הגשה מקוונת, הכנת
  תכנית שטחים ×2, הכנת הרמוניקה) linked from the municipal building-permit page; like
  Tel-Aviv, no public technical spec of the file format is published.

**Impact on RVTuk:** none code-wise — the exporter targets the national robot only, and the
municipal formats are unpublished (login-gated), so nothing municipality-specific can or
should be encoded in the DXF. A "Municipality type" project parameter merely selects the
workflow; Jerusalem/Tel-Aviv submissions follow the city system with the city's own tooling.
Revisit only if a client project actually needs a J'lem/TLV submission (then obtain the spec
from inside the municipal system) or when those committees migrate to the national robot.

## 11. What RVTuk validates before export `[RVTUK]`

Blocking: missing/invalid usage code (neither `USAGE_TYPE` nor `USAGE_TYPE_OLD` resolves to
a catalog code, or a provided code is unknown) · zero/negative area · missing/degenerate
boundary (< 3 points) · missing output folder / base name / scale / building number.
Warnings: area missing number or name · geometry outside the sheet frame · sheet exceeding
the 910 mm / 14,500 mm robot caps.

## 12. RVTuk emission choices `[RVTUK]`

Marker form selectable in the Config pane — **Official** = Form A block/ATTRIB (default),
**Old** = Form B TEXT (`AreaSubmissionConfig.MarkerForm`); both AutoCAD-accepted 2026-07-03 ·
AC1032 ASCII, UTF-8 · HEADER/TABLES/BLOCKS preamble (which carries the `RZ_*_SYM` block
definitions Form A references) and OBJECTS postamble captured verbatim from Garmoshka.dxf,
entity handles allocated from 0xB8 with `$HANDSEED` rewritten; Form A ATTRIBs owned by their
INSERT, per-tag heights/justification/invisible flags mirroring the ATTDEFs · frame = Revit
title-block size × scale at (0,0), geometry sheet-relative and untranslated · interior-point
algorithm (centroid, scanline fallback) for area anchors; `RZ_*_SYM` frame/floor inserts
inset 5 units off the box corner · `LEVEL_ELEVATION` from the Revit level, metres F2 ·
`IS_UNDERGROUND` = 1 iff elevation < −0.005 m.

**Area parameters** (bound by the "Usage Keys" button — `UsageKeyScheduleBuilder`): five
shared text instance parameters on Areas — `RZ_USAGE_TYPE`, `RZ_USAGE_TYPE_OLD`, `RZ_AREA`,
`RZ_ASSET`, `RZ_BUILDING_NO` — plus two Area **key schedules** filled from the catalog:
"New Usage" (Key Name = "«code» - «name»", drives `RZ_USAGE_TYPE` + the area's system Name)
and "Old Usage" (same keys minus process codes 300-302, drives `RZ_USAGE_TYPE_OLD` only).
Key rows are named via the `REF_TABLE_ELEM_NAME` ("Key Name") parameter — Area-class key
rows reject the `Element.Name` setter. The extractor reads `RZ_USAGE_TYPE`/`RZ_USAGE_TYPE_OLD`
first and falls back to the office-template parameters "Usage Type" / "Usage Type Prev"
(names accepted only when catalog-valid); `RZ_AREA` → `AREA` tag and `RZ_ASSET` → `ASSET`
tag per area (config Asset as fallback). `RZ_BUILDING_NO` is bound but not yet wired into
the floor tag (multi-building floor-splitting is future work; config Building number rules).

## 13. Open questions `[OPEN]`

1. `DWFX_SCALE` semantics at scales other than 1:100.
2. DXF↔DWFX registration: presumed shared coordinates + `DWFX_SCALE` only; unverified.
3. Whether the robot itself is stricter or looser than AutoCAD's DXF reader (§2.4–2.5 are
   proven necessary for AutoCAD-based tooling; the robot may add checks of its own — final
   confirmation only comes from a live submission). Note the robot accepts **free draft
   submissions** ("אפשר להיכנס באופן חופשי ולשלוח טיוטות… ללא תשלום" `[OFFICIAL]` FAQ) — a
   real end-to-end test costs nothing.
4. The Jerusalem/Tel-Aviv municipal robots' technical file specs (§10) — login-gated,
   unobtained.

Resolved 2026-07-03 (previously open): the שטח-כולל usage-code list is now published — see
§7b · the 2018 code table did change — 117/118/119 added (amendment 163, Oct-2025 table) ·
codes 3/5 are confirmed absent from the current official table (intentional gap, not an
extraction error).
