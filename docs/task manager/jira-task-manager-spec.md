# Task Manager Module - Jira-Equivalent UX Specification (for Claude Code)

Source: direct inspection of a live Jira Software (Scrum, team-managed style) project "PEMS" (key SCRUM) in the browser, 2026-10-15.
Goal: the PEMS Task Manager must look and behave like this Jira project. Target stack: the existing PEMS ASP.NET (MVC/Razor + EF Core + SQL Server) solution; follow existing conventions (service layer, [RequirePermission], AuditLog, migrations only with approval).

Observed vs. not observed: everything below marked (OBSERVED) was seen on screen. Items marked (INFERRED) are standard Jira behavior that was not opened in this session (Filter popup and Timeline were still loading skeletons when captured; verify them live before building).

---

## 1. Information architecture

Project (called "Space") page has a fixed top tab bar, in this order (OBSERVED):
Summary | Backlog | Board | Development | Timeline | Docs | Forms | "+" (add view).

Global top bar (OBSERVED): sidebar toggle, app switcher, app logo, a wide Search box (center), blue primary "+ Create" button, then Upgrade, notifications bell, help, settings gear, avatar.

Breadcrumb on project pages: "Spaces" (small, grey) above the project header: [project icon] **PEMS** [team button] [...]. Right side of header: share, automation (lightning), feedback, fullscreen.

Minimum scope for PEMS v1: Backlog, Board, Work item page, Create dialog, All-work list with filters (JQL-lite), Timeline. Summary, Development, Docs, Forms may be placeholders.

## 2. Data model (map to EF entities)

WorkItem
- Key: `{PROJECTKEY}-{n}` auto-increment per project (e.g. SCRUM-32). Unique, immutable.
- Type: Story (green bookmark icon, default), Task, Bug, Epic, Subtask. (Story observed; others INFERRED.)
- Summary (single line, title), Description (rich text), Status, Priority, Assignee, Reporter, Parent (epic or parent item), Due date, Start date, Labels (many), Team, Sprint, StoryPointEstimate, Created, Updated.
- Defaults seen on Create: Type=Story, Assignee="Automatic" (resolves to the creator/project default), Priority=Medium. Reporter = creator.
- Status categories: To Do, In Progress, Done. Workflow statuses seen: **To Do, In Progress, In Review, Done**. "In Review" belongs to the In Progress category. A filter "Status Category != Done" is the default on the All-work list.
- Related: Subtasks (child items), Linked work items (typed links: blocks / is blocked by / relates to / duplicates - INFERRED), Comments, History (field-change log), Work log, Attachments, Watchers (eye icon with count), Development links (commits/PRs).
- Sprint: Name ("SCRUM Sprint 0"), Start/End dates, State (future / active / closed), Goal. Only one active sprint at a time. Sprint can be created, started ("Start sprint" is disabled until the sprint has items and dates), completed ("Complete sprint").
- Workflow: list of statuses + allowed transitions; "Add status", "Edit status", "View workflow" are offered from the status dropdown.

All edits write a History row (field, old, new, user, time) - reuse PEMS AuditLog conventions.

## 3. Backlog view (OBSERVED)

Layout: vertical stack of collapsible panels on a light-grey rounded container, each with a chevron, a select-all checkbox, a bold title, then meta on the right.
1. Active/future sprint panels, newest sprint listed in order (e.g. "SCRUM Sprint 0 24 Jul - 7 Aug (0 work items)", "SCRUM Sprint 1 [Add dates] (0 work items)").
   - Right side: three small count pills (grey = To Do, blue = In Progress, green = Done), then a button: "Complete sprint" (active) or "Start sprint" (future, disabled when empty), then a "..." menu.
   - Empty state: dashed box "There's nothing in this sprint" / "Plan a sprint by dragging work items into it, or by dragging the sprint footer."
   - Footer row: "+ Create" (inline create).
2. A divider row with the estimate summary: "0 of 0 work items visible | Estimate: 0 of 0".
3. "Backlog (N work items)" panel with the same count pills, a small icon button (epic/plan helper) and a "Create sprint" button. A dismissible promo banner row ("Create a whiteboard to plan your work [Try]") appears first - not needed in PEMS.
4. Rows (one per item), left to right: checkbox, type icon, key (grey, e.g. SCRUM-32), summary (truncated with ellipsis), epic chip (if any), status dropdown (small outlined select, e.g. "To Do v"), story points chip ("-" when empty, grey rounded), priority icon (orange "=" for Medium), assignee avatar (grey placeholder when unassigned).
   - Row hover: highlight; click opens the item in a right-side panel (or full page via key link); drag handle to reorder or drag into a sprint. Multi-select checkboxes enable bulk actions (a toast "Bulk action complete - You've edited 4 work items." was seen).
   - Inline status change from the row dropdown.
5. Bottom of each panel: "+ Create" inline row: input "Describe what needs to be done." with a type icon selector (checkbox-style icon + chevron) on the left, due-date calendar icon, assignee avatar picker, and a "Create (Enter)" button. Enter creates, input stays for rapid entry.

Toolbar above the panels (OBSERVED): "Search backlog" box; two avatar chips (filter by assignee, quick filters); "Filter" button (opens a two-pane popover: left = filter categories, right = options - INFERRED contents: Assignee, Type, Epic, Label, Status, Priority); right side icons: Insights/chart, **View settings**, "..." more.

View settings panel (OBSERVED, right drawer titled "View settings", close X):
- "Epic panel" toggle (shortcut E, off by default)
- "Empty sprints" toggle (on)
- Density: Default | Compact (radio with small thumbnails)
- Fields toggles (all on): Work type, Work item key, Epic, Status, Priority, Assignee (and more below, e.g. Labels/Story points - scroll to verify).

## 4. Board view (OBSERVED)

- Same tab bar and toolbar: "Search board", assignee avatar chips, "Filter", "Group" (swimlane grouping), blue **"Complete sprint"** primary button, then icons: refresh/sync, insights chart, view settings, "...".
- Columns (horizontal, each a light-grey rounded lane, equal width ~280px): **To Do 0 | In Progress 0 | In Review 0 | Done 0**. Header = column name + item count. Horizontal scroll when wider than viewport.
- Empty state in the first column: icon + "No work items - Create a work item to get started. Work will appear here." Every column has a dashed "+" box for inline create.
- Cards (INFERRED, standard): summary, type icon, key, priority icon, assignee avatar, story points; drag & drop between columns changes Status (only to statuses allowed by the workflow); drop on Done sets resolution.
- Board shows only items in the active sprint.

## 5. Create dialog (OBSERVED)

Opened by the global blue "+ Create" button; centered modal ~800px wide, rounded, with dim overlay.
- Header row: project chip ([icon] SCRUM) | type chip ([bookmark] Story, dropdown to change type); right: "minimize to side panel", "expand", close X.
- Big borderless **Summary** field (placeholder "Summary"); below it a rich-text **Description** area (placeholder "Add a description or type / for actions" - slash commands supported).
- Chip row of optional fields: Assignee ("Automatic" with avatar), Priority ("Medium" with orange =), Parent, Due date, Labels. More fields via expand (Sprint, Team, Start date, Story points, Reporter, etc.).
- Footer: left feedback icon; right "Create another" checkbox (keeps dialog open and resets the form; a green "N work items created" confirmation shows after each create), primary blue "Create" button.
- Keyboard: **Ctrl+Enter submits** (verified). Closing with unsaved text prompts "Your changes won't be saved - Go back / Discard".
- Caution observed: the description editor auto-converts text emoticons ("B)" became an emoji). PEMS editor should not do emoji autoconvert of plain text.

## 6. Work item page / detail (OBSERVED)

Two-column page (also usable as a right side panel from backlog/board):
Left (main):
- Breadcrumb: Spaces / [icon] PEMS / [Add epic] / [type icon] SCRUM-32.
- Large editable title (inline edit on click).
- Small toolbar below title: "+" (add: subtask, link, attachment, apps) and "..." menu.
- **Description** section (collapsible, chevron): rich text, click to edit.
- **Subtasks** section: "Add subtask" inline.
- **Linked work items** section: "Add linked work item".
- (Optional) Confluence/Docs content block - not needed.
- **Activity** section: tabs pill group [All | Comments | History | Work log] (Comments selected by default), sort toggle (Newest first), avatar + "Add a comment..." box with quick-reply chips ("Looks good!", "Need help?", "This is blocked...", "Can you clarify...?", "This is on track"); hint "Pro tip: press M to comment".
Right (sidebar, sticky):
- Top icon row: lock/visibility, watcher eye with count (1), share, "..." actions.
- **Status** button (e.g. "To Do v") with dropdown listing allowed transitions colored as lozenges: In Progress (blue), In Review (blue), Done (green), then "Add status", "Edit status", "View workflow". Next to it: "Open in coding tool" and "Automation" icon buttons.
- **Details** card (collapsible, gear to configure visible fields), label above value, each value inline-editable: Assignee, Priority, Parent, Due date, Labels, Team, Start date, Sprint, Story point estimate, Reporter.
- **Development** (commits/PRs) and **Automation** (rule executions) cards.
- Footer meta: "Created 11 minutes ago", "Updated 6 minutes ago" (relative times, tooltip with exact), "Configure".

Keyboard shortcuts seen/standard: M = comment, E = epic panel (backlog), C = create (INFERRED), "/" = search (INFERRED).

## 7. All work / search page (OBSERVED)

URL pattern /issues. Title "All work". Top right: "Search all apps", Apps, Share, "...".
Filter bar: toggle **Basic | JQL**; text search; filter chips with operators: `Space = PEMS`, `Assignee = Unassigned`, Type v, Status v, `Status Category != Done` (removable x), "More filters v", "Clear filters", "Save filter".
Results table: select-all checkbox; resizable columns Work (key+summary), Assignee, Reporter, Priority, Status ... plus a column picker icon at the far right; horizontal scroll; empty state "There are no work items here yet - You either don't have any work items or your existing ones don't match your current filters."
Bulk actions via row checkboxes (edit, move, delete); toast "Bulk action complete".
JQL mode: free-text query like `project = PEMS AND status != Done ORDER BY priority DESC` (implement a small subset: =, !=, AND, OR, IN, ORDER BY, text ~).

## 8. Timeline (roadmap) view (INFERRED - verify)

Left tree: epics with child items (indent) and expand/collapse; right: monthly/weekly grid with horizontal bars (start to due date), drag bar ends to change dates, today marker, zoom (weeks / months / quarters), filters, "Create epic" at bottom.

## 9. Visual design tokens (OBSERVED, approximate)

- Font: system sans (Inter/Segoe-like), body 14px; page titles 24-28px semibold; item title on detail page ~28px semibold.
- Primary: blue (#1868DB / #0C66E4 family) for Create, Complete sprint, links, selected tab underline (2px blue under active tab).
- Surfaces: white page; panels/lanes light grey (#F7F8F9 / #F1F2F4); borders 1px #DFE1E6 / dashed for empty drop zones; radius 8px on panels, 3-4px on chips and inputs.
- Lozenges: To Do = grey, In Progress/In Review = blue, Done = green (bold small caps text in Jira's classic style; here soft pills).
- Priority icons: Highest/High red chevrons up, Medium orange "=", Low blue chevron down, Lowest.
- Type icons: Story = green bookmark, Task = blue check square, Bug = red circle, Epic = purple lightning, Subtask = blue branch.
- Count pills in sprint headers: small rounded squares - grey / blue / green with numbers.
- Density: row height ~40px (default), ~32px (compact). Hover state light-grey row background. Focus ring blue 2px.
- Avatars: 24px circle; unassigned = grey circle with person silhouette.
- Toasts: bottom-left card with green check, title + message, X to close.

## 10. Behaviors to implement (acceptance checklist)

1. Create item via global Create modal and via inline "+ Create" in backlog/sprint/board column; Ctrl+Enter submits; "Create another" works; keys auto-increment.
2. Backlog drag-and-drop: reorder (rank), move to/from sprints; multi-select bulk move/assign/status.
3. Sprint lifecycle: create, add dates, start (requires >=1 item and dates), complete (prompt moves unfinished items to next sprint or backlog), only one active.
4. Board: columns from workflow, drag to transition, swimlane "Group" (None, Assignee, Epic, Subtask), quick filters, search.
5. Item page: inline edit for every field, status transitions menu, subtasks, links (blocks / is blocked by), comments with @mentions and quick chips, History tab listing every field change, Work log, watchers, share link.
6. Search/filters: Basic filter chips + JQL subset + saved filters; column chooser; CSV export (INFERRED).
7. Relative timestamps, optimistic UI updates, toast confirmations, "Your changes won't be saved" guard on dirty forms.
8. Permissions: use existing PEMS [RequirePermission] with separate Browse / Create / Edit / Transition / Assign / Comment / Delete permissions; every mutation writes AuditLog.
9. No emoji autoconversion in the description editor; rich text sanitized server-side.
10. Persian/RTL: support RTL layout and Jalali dates as a switchable option (PEMS users are Persian-speaking); keep keys (PEMS-123) LTR.

## 11. Suggested build order

1. Entities + migration (WorkItem, Sprint, WorkflowStatus, Transition, Comment, Link, WorkLog, History) - needs migration approval.
2. Service layer (no data access in controllers - see ARCH-001) with permission checks and audit.
3. Backlog page, then Create modal, then Work item page, then Board, then All-work filters, then Timeline.
4. Tests: service-level (transitions, sprint rules, key generation concurrency) on an isolated test database (see TEST-001 guard).
