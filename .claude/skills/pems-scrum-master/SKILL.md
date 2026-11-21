---
name: pems-scrum-master
description: Act as PEMS scrum master: run the work block, keep the sprint board honest (statuses, assignees, labels, qa-pending), remove blockers, run the sprint review. Use on any go-ahead, at sprint start or end, or when the board looks wrong.
---

# PEMS scrum master

- **Work block** (guideline 00 section 0, skill `pems-work-block`): pull main; audit the whole open backlog with one verdict per issue (go / modify / split / merge / cancel with the owner's yes / blocked); batch related issues, a blocker travels with what it blocks; state the plan in the first message.
- **No finished issue waits**: an issue whose Done-when is met and whose tests are green is merged and In Review at once, grouped with its neighbours (owner decision 2026-11-19); the board must not show finished work as In Progress.
- **Claim the batch** the moment it is approved: every issue of the batch (10 to 30) In Progress, assigned, in the sprint, with a comment naming the branch, before any code; unreached issues go back to To Do with a reason (guideline 00 section 0.3).
- **Hand over lean** (batch): In Review with the minimum really needed tests, the rest as unchecked QA lines; at the end of the batch merge nearly repeated tests (guideline 05, 07 section 2a).
- **Statuses**: In Progress only with an assignee and a branch; In Review only after the merge into `main`, with label `qa-pending` and the QA checklist (browser checks are unchecked lines); Done only after QA. If an issue will not move, check the transition screen and add the missing comment or label (precedent SCRUM-437).
- **Board checks** (queries in guideline 00 section 5d): issues without parent, title or labels; unassigned issues in the sprint (assign the area owner or the creator); duplicates; stale In Progress.
- **Sprint review**: guideline 07 sections 7a and 7b; Bugs found go into the active sprint.
- **Impediments** you cannot solve (credentials, hosting, GitHub API outages): say what and who, continue with unblocked work; do not poll.
- Shared account: branch and commit carry the real person suffix (`-hamid`, `-saber`); stage specific files only; never stash, reset or switch branches in a shared folder.
