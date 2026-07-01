# רישוי זמין — חישוב שטחים ואחוזי בנייה: research notes for the AutoArea package generator

> Scoping notes for a tool that generates the file package submitted to the national
> area-calculation robot (**רכיב אוטומטי לחישוב שטחים**) inside **רישוי זמין (Rishui Zamin)**.
>
> **Accuracy convention used throughout:** anything marked **[OFFICIAL]** is taken from the
> Planning Administration technical spec, the official FAQ, or the regulations (URLs in
> §6). Anything marked **[UNCERTAIN]** or **[NEEDS SAMPLE]** is *not* fully pinned down by a
> public document and must be confirmed against the user's own sample DXF/DAT/DWFX files.
> **No DXF layer name, block/attribute tag, or DAT byte layout below was invented** — every
> concrete name/code is quoted from the official spec PDF; where the spec is silent that is
> stated explicitly.

---

## 1. Process overview

**What it is. [OFFICIAL]**
"חישוב שטחים ואחוזי בנייה" is the area / building-percentage calculation that accompanies a
building-permit request (בקשה להיתר). In Rishui Zamin this is produced by a **national
automatic area-calculation component ("robot")** operated by the Planning Administration
(מינהל התכנון). The permit editor (עורך הבקשה — architect/engineer) uploads drawing files;
the robot **computes the areas, colours (צובע) the area polygons, and draws the area table
(טבלת שטחים)**, returning a finished product a few minutes later.

**Who submits, to whom. [OFFICIAL]**
- The **עורך הבקשה** uploads files in the personal area of Rishui Zamin, under the
  **"חישוב שטחים"** tab. Flow: open a new project, type the **plot area (שטח המגרש)** and
  the **requested scale (קנ"מ, usually 1:100)**, upload the files, send for processing, then
  download the product. Drafts / partial plans / single floors can be sent freely with no
  limit and no charge.
- The robot's **output (a DWFX product)** is what ultimately goes into the permit request /
  main plan submitted to the **local committee (ועדה מקומית)**.

**Where it fits.** The robot replaces the manual area-calculation sheets of the old paper
"גרמושקה". Committees that mandate the robot **cannot accept the old-format calculation**, and
committees are obliged to accept the new format.

**Important carve-out. [OFFICIAL]** The FAQ states that **Jerusalem and Tel-Aviv local
committees use a *different* area robot** — for those, the editor must follow the municipal
site. "In the future all committees will connect to the national robot of the Planning
Administration." → **If the target committee is TLV or Jerusalem, the file package and codes
below may not apply.** This must be confirmed per project.

### Required file package — verify against the user's belief

The user believed the package is: a DWF of sheet images + a DXF of area geometry & tags + a
DAT data file. The official spec is slightly different:

**[OFFICIAL] The ZIP submitted to the robot contains exactly two files, in one of two forms:**

- **Default / primary form (2 files):**
  1. **`.dwfx`** — the architectural submission spec (מפרט ההגשה האדריכלי), i.e. the floor
     layouts (תנוחת הקומות), **without** the area-calculation layers.
  2. **`.dwg`** — the area-calculation layers, which **embeds the `.dwfx` main plan as an
     attached xref** (`attached xref`).
- **Alternative form (3 files):** a ZIP containing **`.dwfx` + `.dxf` + `.dat`**.

Packaging rules [OFFICIAL]: files sit **directly in the ZIP (no subfolder)**; file names may
use letters/digits, **no spaces**; the path to the `.dwfx` must be **relative or no-path**.

**Discrepancy vs. the user's belief:**
- The user's "DWF" is really **DWFX** (DWF is a fallback that must be re-saved to DWFX via
  Autodesk *Design Review* or exported directly by *TrueView*). DWFX carries the **sheet
  images / floor layouts**, matching the user's description.
- The **DXF path is the *alternative*, not the default.** The default second file is a **DWG**
  (with the DWFX xref'd in). The DXF is the DWG's schema exported to DXF — same layer/block
  schema (§5).
- The **DAT** only appears in the 3-file alternative. Its purpose/contents are **not described
  in the official spec** (see §5, §7). Secondary sources describe it loosely as "a text file
  containing the scale (קנ"מ)". **[UNCERTAIN — NEEDS SAMPLE]**

So: user is broadly right that the machine package is DWFX + DXF + DAT, but the *official
default* is DWFX + DWG, and DAT is undocumented.

---

## 2. Project-level info required

Split between (a) what the editor types into the web UI at submission, and (b) what is carried
inside the drawing blocks.

**Typed into the Rishui Zamin UI at submission [OFFICIAL]:**
- **שטח המגרש** — plot/parcel area.
- **קנ"מ מבוקש** — requested output scale (typically **1:100**). Drawing itself must be **1:1
  in centimetres**.

**Carried per-floor inside the `RZ_FLOOR_SYM` block [OFFICIAL] (see §5):**
- `BUILDING_NO` — building/structure number (default 1).
- `FLOOR` — floor/level name.
- `LEVEL_ELEVATION` — relative level above the determining entrance level (מפלס הכניסה
  הקובעת).
- `IS_UNDERGROUND` — whether the level is underground (1/0).

**[UNCERTAIN] Not found in the robot spec:** the spec is a *drawing-file* spec, so
project identity such as **גוש/חלקה (block/parcel), address, committee/region, תב"ע (plan)
numbers, permitted vs proposed building rights, unit/floor totals, אחוזי בנייה** are **not**
attributes in the DXF blocks. Those live in the broader Rishui Zamin permit request form
(the בקשה להיתר itself) and/or the surveyor's file (קובץ מדידה), not in the area-robot input.
The robot **computes** total areas / building-% *from the geometry*; it does not ask the
editor to type them. → Confirm the full permit-request field list separately from the
area-robot spec if the tool must populate those too. **[NEEDS SAMPLE / further research]**

---

## 3. Per-area info & classification (the usage taxonomy)

**[OFFICIAL — quoted verbatim from the spec's "טבלת סוגי שימושים" (usage-type table).]**
Each area polygon carries a numeric **usage code** in the `USAGE_TYPE` field. The codes are
grouped:

### שטחים עיקריים — Primary areas
| Code | Use (HE) | Use (EN, approx.) |
|-----:|----------|-------------------|
| 1  | מגורים | Residential |
| 2  | מסחר והסעדה | Commerce & catering |
| 4  | תעשייה ומלאכה | Industry & crafts |
| 6  | חקלאות | Agriculture |
| 7  | משרדים ותעשיות עתירות ידע | Offices & knowledge industries |
| 8  | מלונאות ובתי אירוח אחרים | Hotels & other lodging |
| 9  | נופש וספורט | Leisure & sport |
| 10 | מבני ציבור ודת | Public & religious buildings |
| 11 | פנאי ותרבות | Recreation & culture |
| 12 | מוסדות חינוך | Education institutions |
| 13 | מוסדות בריאות | Health institutions |
| 14 | מבני חרום וכליאה | Emergency & detention |
| 15 | מבני תחבורה, מבני דרך ותדלוק | Transport / road / fuelling |
| 16 | מבנים טכניים, תשתיות ושמירה | Technical / infrastructure / guarding |
| 30 | מרפסת | Balcony (primary) |
| 31 | אחסנה | Storage (primary) |
| 32 | מצללה | Pergola (primary) |
| 33 | חניה | Parking (primary) |

*(Note: codes 3 and 5 are not listed in the table as extracted.)*

### שטחי שירות — Service areas
| Code | Use (HE) | Use (EN, approx.) |
|-----:|----------|-------------------|
| 101 | מרחב מוגן דירתי – שטח רצפה | Residential protected space (MMD) – floor area |
| 102 | מרחב מוגן דירתי – שטח קירות | Residential protected space (MMD) – wall area |
| 103 | מרחב מוגן קומתי / מוסדי / מקלט / מבנה שמירה | Floor/institutional protected space / shelter / guard structure |
| 104 | מעלית | Elevator |
| 105 | מבואות וחדרי מדרגות | Lobbies & stairwells |
| 106 | קומת עמודים מפולשת ומקמרות | Open pilotis floor & vaults |
| 107 | מעברים לכלל הציבור | Public passages |
| 108 | מערכות טכניות ומבני שירות | Technical systems & service structures |
| 109 | חדרי שירות משותפים | Shared service rooms |
| 110 | מרפסת | Balcony (service) |
| 111 | מרתף | Basement |
| 112 | חניה | Parking (service) |
| 113 | עובי קירות | Wall thickness |
| 114 | בליטות, גגונים וקירוי | Protrusions, canopies & roofing |
| 115 | אחסנה | Storage (service) |
| 116 | מבני שמירה | Guard structures |
| 130 | אחר מתוקף תכנית | Other, per the plan |

### אחר — Other (NOT included in the floor's area summary)
| Code | Use (HE) | Use (EN, approx.) |
|-----:|----------|-------------------|
| 250 | מרפסת זיזית | Cantilevered balcony |
| 252 | שטח מרוצף לא מקורה | Paved, uncovered area |
| 255 | מצללה | Pergola |
| 256 | בריכת שחיה | Swimming pool |
| 257 | בליטות, גגונים וקירוי | Protrusions, canopies & roofing |

### ללא צביעה — Not coloured (process markers)
| Code | Use (HE) | Use (EN, approx.) |
|-----:|----------|-------------------|
| 300 | הורדה | Lowering / removal |
| 301 | הריסה ופירוק | Demolition & dismantling |
| 302 | חפירה | Excavation |

**Auto-coloured hues with no usage code [OFFICIAL]** (the robot colours these itself):
"שטח עיקרי כפי שקיים בהיתר", "שטח שירות כפי שקיים בהיתר", "שטחים אחרים קיימים בהיתר",
and **תכסית (perimeter only)**.

**[NOTE — two code lists exist.]** The spec above is for the **primary/service separation
method**. Secondary/FAQ material states there is a **different code list for the "total area"
(שטח כולל) method** (see §4). The extracted PDF only contains the separation-method table.
→ If the tool must support שטח-כולל plans, the second code list **[NEEDS SOURCE/SAMPLE]**.

**Identifying data each area polygon carries** — see the `RZ_AREA_SYM` block in §5
(`USAGE_TYPE`, `USAGE_TYPE_OLD`, `AREA`, `ASSET`).

---

## 4. Area-calculation rules (from the regulations)

Authoritative source: **תקנות התכנון והבניה (חישוב שטחים ואחוזי בניה בתכניות ובהיתרים),
התשנ"ב-1992** (nevo / Wikisource). Article numbers below are as reported from Wikisource and
should be re-verified against the canonical nevo text before being relied on in code.

**Primary vs service. [OFFICIAL — regulations]**
- **שטח למטרה עיקרית (primary)** — space serving the plan's principal uses directly
  (residential, commerce, industry, offices, hotels, agriculture, public buildings, etc.).
- **שטח שירות (service)** — ancillary space: shelters/protected space, technical systems,
  storage, parking, stairwells/lobbies, open pilotis, and public passages (with width/other
  conditions, e.g. passages over ~1.5 m).

**Measurement rule. [OFFICIAL — regulations]** Area is the area enclosed by a **horizontal
cross-section**, taken (per the regs) at a defined height above floor level. Space **beneath
external and internal walls is included**; a wall shared between two uses is **split 50/50**
between them. → i.e. gross/external-wall measurement, not net internal.

**Inclusions/treatment [OFFICIAL — regulations, but verify exact article]:**
- **Balconies (מרפסות):** counted as part of the floor area when integral to the unit; note
  the robot taxonomy distinguishes enclosed/integral balcony (30/110) from **cantilevered
  balcony (250, "other", excluded from the floor summary)**.
- **Parking (חניה):** service area (112 / 33).
- **Storage (אחסנה):** service area (115 / 31).
- **Common stairs/lobbies/passages:** service areas (105, 107).
- **Wall thickness (עובי קירות):** carried explicitly as service code 113.

**Building percentage (אחוזי בנייה). [OFFICIAL — regulations]** Building rights are expressed
as percentages of the plot area; a plan may set separate percentages for primary vs service.

**Two methods for determining rights. [OFFICIAL]**
1. **Separation method** — plan sets distinct areas/percentages for **primary vs service**
   (the classic method; matches the code table in §3).
2. **Total-area method (שטח כולל)** — introduced by a **December 2023 amendment**; the plan
   sets a single undifferentiated total building area with **no split** into primary/service.
   Newer plans may use this.

**[CAVEAT — moving target]** A further amendment **(תיקון), התשפ"ה-2025** and Planning
Administration circular **10/25** reportedly change area/building-% calculation. The 2018
robot spec predates these. → **Verify which regulation version the target committee currently
enforces**, and whether the robot's code table has been revised since 2018.

---

## 5. File formats — what each file contains

### 5a. `.dwfx` — architectural submission spec [OFFICIAL]
- Contains the **floor layouts (תנוחת הקומות)** / sheet images that serve as the **background**
  for the area calculation. This is the human-readable drawing product.
- **No hatch/area fills** allowed **except cut elements (wall thickness)**.
- Produced by most CAD/BIM tools; if a tool can't export DWFX directly, export DWF then
  re-save to DWFX (Design Review) or use TrueView.
- Drawing must be **1:1, centimetres**; import DWFX into a fresh drawing at 1:1 (no scale
  change) or the scale drifts.

### 5b. `.dwg` (default) / `.dxf` (alternative) — area-calculation layers [OFFICIAL]
Same schema either way; the area-calc file is authored in DWG or DXF **over the DWFX
background** (DWFX attached as xref in the DWG). **Layer/block schema, verbatim from the
spec:**

**Layers & their attached blocks:**
| Purpose (HE) | Layer | Attached block | Mandatory? |
|--------------|-------|----------------|-----------|
| הגדרת שטחים (area definition) | `RZ_AREA` (and any `RZ_AREA*`) | `RZ_AREA_SYM` | layer optional |
| הגדרת מפלס (floor definition) | `RZ_FLOOR` | `RZ_FLOOR_SYM` | yes |
| מסגרת הפקה (production frame) | `RZ_FRAME` | `RZ_FRAME_SYM` | **yes** |
| תכסית (land coverage / footprint) | `RZ_LANDCOVER` | none | optional |
| עיגון גיאוגרפי (geographic anchor) | `RZ_ANCHOR` | `RZ_ANCHOR_SYM` | optional |

**`RZ_AREA` — area polygons.** One closed polygon per area, each containing **exactly one**
`RZ_AREA_SYM` block; **no polygon overlap**; polygons may be any closed shape (incl. curved).
Extra layers starting `RZ_AREA*` are allowed (e.g. to separate primary vs service).

**`RZ_AREA_SYM` block attributes (TAG names, verbatim):**
| Attribute (HE) | TAG | Type | Notes |
|----------------|-----|------|-------|
| קוד שימוש מוצע (proposed usage) | `USAGE_TYPE` | number (value table) | see §3 codes |
| קוד שימוש קיים בהיתר (existing usage in permit) | `USAGE_TYPE_OLD` | number (value table) | |
| שטח כפי שקיים בהיתר (area as in permit) | `AREA` | number (manual) | shown with `*`, **not** recomputed by robot |
| מספר יח"ד/יחידה (dwelling-unit / unit no.) | `ASSET` | number (manual) | not mandatory at this stage |

Rule: **at least one** of `USAGE_TYPE` / `USAGE_TYPE_OLD` must have a value. Proposed area →
`USAGE_TYPE` only; existing-in-permit → `USAGE_TYPE_OLD`; change of use → both. Demolition →
demolition code in `USAGE_TYPE` + existing code in `USAGE_TYPE_OLD`.

**`RZ_FLOOR` — floor boundary polygons.** One closed polygon per floor, one `RZ_FLOOR_SYM`
each, no overlap; a floor polygon **contains** its area polygons and **is contained by** a
production-frame polygon.

**`RZ_FLOOR_SYM` block attributes (TAG names, verbatim):**
| Attribute (HE) | TAG | Type | Mandatory | Notes |
|----------------|-----|------|-----------|-------|
| מספר מבנה/בניין | `BUILDING_NO` | number | yes | default 1 |
| שם המפלס | `FLOOR` | text | yes | comma-separated list for typical floors |
| רום מפלס יחסי (rel. to determining entrance) | `LEVEL_ELEVATION` | text | yes | e.g. `+3.00,+6.00,+9.00` |
| האם המפלס תת-קרקעי | `IS_UNDERGROUND` | number | yes | 1 = yes, 0 = no (default 0) |

For typical floors: `FLOOR` and `LEVEL_ELEVATION` take comma-separated lists — **the count in
both fields must match.**

**`RZ_FRAME` — production frame.** **Mandatory.** At least one closed **rectangle** enclosing
everything to be calculated; one `RZ_FRAME_SYM` each; no overlap; contains the floor polygons;
must not cross a floor polygon. The **main frame (PAGE_NO = 1) must be the right-most** in the
drawing. Max printed size (by scale): **height 910 mm, length 15,000 mm**; first sheet
(`page_no=1`) max **910 × 14,500 mm**. Long drawings may be split into several frames.

**`RZ_FRAME_SYM` block attribute:** `PAGE_NO` — sheet number, integer, mandatory.

**`RZ_LANDCOVER` — coverage/footprint.** Optional. Closed polygon per relevant level, **no
block**, no overlap, contained in the floor polygon; the robot uses only its perimeter.

**`RZ_ANCHOR` — geographic anchor.** Optional layer; block `RZ_ANCHOR_SYM` is a **point**;
**exactly one anchor inside each floor boundary** if used. Drawings must sit in a **positive,
right-angle coordinate grid (m/cm/mm) within Israel's national bounds.**

### 5c. `.dat` — data file (alternative 3-file form) [UNCERTAIN / NEEDS SAMPLE]
- The official robot spec **lists** the `.dat` only as part of the alternative
  `dwfx + dxf + dat` ZIP and **does not describe its contents, fields, or byte layout**.
- Secondary/community sources describe it loosely as a **text file carrying the scale (קנ"מ)**
  (and, by inference, the units / placement needed to register the DXF against the DWFX
  sheets, i.e. the role the xref plays in the DWG form). **This is not officially confirmed.**
- **Do NOT assume any DAT field structure.** Its exact format must come from the user's sample
  `.dat` file. **[NEEDS SAMPLE]**

### 5d. Output product [OFFICIAL]
The robot returns a **DWFX** product combining the DWFX background + computed & coloured areas
+ the area table. The permit request / main plan (תכנית ראשית) submitted to the committee is a
single multi-sheet DWFX.

---

## 6. Sources (official / authoritative)

Official (Planning Administration / gov.il / legislation):
- **Technical spec — "מפרט טכני לרכיב אוטומטי לחישוב שטחים", 18 Jan 2018, v2.0** (the primary
  source for §3 and §5): https://www.gov.il/BlobFolder/generalpage/rz_calculation_areas/he/rz_RZCalcAreaSpec.pdf
- **Official FAQ — "שאלות ותשובות מהדרכת עורכי בקשה על רובוט חישוב שטחים"** (process, files,
  scale, TLV/Jerusalem carve-out): https://www.gov.il/BlobFolder/generalpage/iplan_doc2/he/faq_calculations_area_mail.pdf
- Area-calculation landing page (מינהל התכנון): https://www.gov.il/he/pages/rz_calculation_areas_1
- FAQ landing page: https://www.gov.il/he/departments/faq/faq_calc_area
- Preparing files in Revit (gov.il; blocked to automated fetch, browse manually): https://www.gov.il/he/pages/files_revit
- Guidelines for determining rights in total area (שטח כולל): https://www.gov.il/he/pages/guidelines_determining_total_area
- **תקנות … (חישוב שטחים ואחוזי בניה בתכניות ובהיתרים), התשנ"ב-1992** — nevo: https://www.nevo.co.il/law_html/law00/74663.htm
- Same regulations — Wikisource (used for §4 definitions): https://he.wikisource.org/wiki/תקנות_התכנון_והבניה_(חישוב_שטחים_ואחוזי_בניה_בתכניות_ובהיתרים)
- **תקנות … (בקשה להיתר, תנאיו ואגרות)** — Wikisource: https://he.wikisource.org/wiki/תקנות_התכנון_והבניה_(בקשה_להיתר,_תנאיו_ואגרות)
- 2025 amendment / circular 10/25 (context for the moving-target caveat): https://www.hs-law.org/מהפכה-בחישוב-שטחים-ואחוזי-בניה-בתכניו/

Secondary / community (context only, NOT authoritative for formats):
- Municipal DWF submission guide (Shomron): https://www.shomron.org.il/מדריך-להגשת-מסמכים-ותכניות-dwf-באתר-הוועדה-לתכנון-ובנייה/
- Hod-HaSharon area-calc training deck (PDF): https://hithadshut.hod-hasharon.muni.il/wp-content/uploads/2024/04/5-מדריך-לחישוב-שטחים-רובוט.pdf
- RVM "Auto Area Calculation (AAC) for Revit" (commercial add-in, comparable scope): https://rvm.co.il/פתרונות-cad/אפליקציות-ויישומים/567-auto-area-calculation-aac-for-revit
- Archijob forum thread (practitioner discussion): https://www.archijob.co.il/archijob_forums/printmessage.asp?id=93191

---

## 7. Open questions / what the sample files must confirm

1. **DAT format [BIGGEST GAP].** No official field/byte spec exists. The user's sample `.dat`
   must reveal: is it plain text? What fields (scale? units? sheet/frame registration? plot
   area?)? How does it relate the DXF to the DWFX when no xref is embedded? **Do not invent.**
2. **DXF vs DWG in practice.** Confirm from a sample DXF that the layer/block/attribute schema
   (§5b) transfers 1:1 into DXF, and how the DWFX background is referenced when using the
   DXF+DAT form (xref path? relative? or is the DWFX simply the separate ZIP member?).
3. **Block/attribute internals.** The spec gives TAG names but not the DXF-level encoding
   (block definition, ATTDEF/ATTRIB structure, insertion point conventions, layer colours/
   ACI). Sample DXF needed to pin exact geometry (are polygons `LWPOLYLINE`? how is the block
   anchored inside the polygon?). **[NEEDS SAMPLE]**
4. **Second code list for the "total area" (שטח כולל) method** — not in the 2018 PDF. Needs a
   current source or sample.
5. **Regulation version currently enforced** — 1992 vs Dec-2023 amendment vs 2025 amendment /
   circular 10/25. Which applies to the target committees, and did the robot's usage-code
   table change after 2018?
6. **Codes 3 and 5** are absent from the extracted primary table — confirm whether they exist
   (extraction gap) or are intentionally unused.
7. **Target committee.** Confirm it uses the **national robot** (schema above) and **not** the
   separate **Tel-Aviv / Jerusalem** robots, which have their own formats.
8. **Project-identity fields** (גוש/חלקה, address, תב"ע, rights) live in the permit request
   form, not the robot input — confirm the full list separately if the tool must fill them.
9. **Version drift.** The spec is dated 2018 / v2.0. Confirm no newer robot spec version
   supersedes it (layer names, tags, or size limits may have changed).
