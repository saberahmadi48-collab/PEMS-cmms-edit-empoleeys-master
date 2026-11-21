# Capability Checklist

| # | Area | Capability | Reference | PEMS today | PEMS rows | Target | Parity |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | RBAC and authorization | One engine for roles/groups/rules/users plus row-level relevance | Titan #1 #5 | Partial - AccessControlService, RoleAssignment | #178 #180 | Better | Open |
| 2 | RBAC and authorization | Editable permission matrix on unit and user forms | Titan #3 | Missing | #60 | Equal | Open |
| 3 | RBAC and authorization | Bulk permission operations (clear/add owner) | Titan #2 | Missing | #60 | Equal | Open |
| 4 | Sessions and security | Session registry, split TTLs, force-logout | Titan #67 #104 | Missing (plain cookie) | #182 | Better | Open |
| 5 | Sessions and security | Superadmin TOTP | Titan #67 | FIDO2 (stronger) | #241 | Better | Open |
| 6 | Sessions and security | AES-GCM field encryption with a machine key | Titan #64 | Missing | #188 | Equal | Open |
| 7 | Licensing | Offline machine-locked Ed25519 licence | Titan #69 #131 | Missing | #101 #98 | Better (install seal + anti-clone) | Open |
| 8 | Licensing | Model/module-level licence | Titan #70 #168 | Missing | #113 | Equal | Open |
| 9 | Licensing | Zip upload, fingerprint display, automatic restart | Titan #140 #141 #146 | Missing | #108 | Equal | Open |
| 10 | Licensing | Web-based superowner issuing tool | Titan #145 | Missing | #106 | Equal | Open |
| 11 | Licensing | Signed update package and Update page | Titan #249 | Missing | #170 | Equal | Open |
| 12 | Licensing | Code protection (bytecode) | Titan #71 | Missing | #118 | Equal | Open |
| 13 | Dynamic platform | Registry of app/model/field/status | Titan Overview | Missing | #10 | Better | Open |
| 14 | Dynamic platform | Generic CRUD/Export/Import engine | Titan #54 #13 | Partial - 11 fixed imports | #12 #25 #210 | Equal | Open |
| 15 | Dynamic platform | Unified formula/validation engine | Titan #10 #11 #12 | Missing | #15 #46 | Equal | Open |
| 16 | Dynamic platform | Status/workflow with a flow editor | Titan #28 | Runtime exists; no UI | #67 | Better (BPMS) | Open |
| 17 | Dynamic platform | Reference values and translatable labels | Titan #138 #190 #230 | Missing | #21 #24 | Equal | Open |
| 18 | UI | List/tree/gallery/accordion/calendar views | Titan #23 #179 | Fixed lists only | #33 | Better | Open |
| 19 | UI | AssociatedList (child relation) with linked view | Titan #23 | Partial | #22 #32 | Equal | Open |
| 20 | UI | Form row-width editor, structured list sizes | Titan #32 #43 | Missing | #44 #57 | Equal | Open |
| 21 | UI | Weighted search and command palette | Titan #48 #199 | Partial - GlobalSearch | #43 | Equal | Open |
| 22 | UI | Jalali calendar support | Titan #179 | Missing | #206 | Equal | Open |
| 23 | Reporting | Server-side export with RBAC | Titan #54 #62 | Partial - QuestPDF | #210 | Equal | Open |
| 24 | Reporting | Dashboard with formulas, cross-filter, PDF | Titan #101 | Fixed | #58 | Equal | Open |
| 25 | Documents | EDMS: document, version, transmittal, numbering | Titan #79 #159 #160 | Stronger | #244 #91 | Better | Open |
| 26 | CMMS | Work orders, PBS, child resources | Titan #85 #86 | Stronger | #89 | Better | Open |
| 27 | Human resources | Personnel registry | Titan #90 #91 | Partial | #231 | Equal | Open |
| 28 | Brand | Database-driven Brand app with public read | Titan #98 #241 | Missing | #129 | Equal | Open |
| 29 | Support | Diagnostics, tickets, error log, audited support session | Titan #92 #93 #94 #95 | Partial - logs | #174 #235 | Equal | Open |
| 30 | Deployment | One-click installer, installer/updater/migrator split | Titan #76 #150 #161 | docker-compose only | #167 #170 | Better | Open |
| 31 | Usability | Unified loading primitives | Titan #121 #122 #123 | Missing | #31 | Equal | Open |
| 32 | Collaboration | Notifications | Titan #105 | Exists (Notification, SignalR) | #61 | Equal | Open |
| 33 | Collaboration | Record lock and live updates | Titan #104 | Missing | #18 | Equal | Open |
| 34 | Business apps | Risk (COSO), procurement, sales, CRM | Titan #106 #107 #224 #225 | Procurement partial | #233 #162 | Equal | Open |
| 35 | AI | Offline assistant | Titan #211 #212 | Local AI exists | #216 #248 | Better | Open |
| 36 | Hosting | PHP-fronted mode / MySQL | Titan #147 #148 | N/A - .NET + SQL Server | - | N/A by design | Open |

|  | Rule: capabilities PEMS must offer, sourced from the reference platform (see the Reference column). When every row is Parity/Better/N/A and the acceptance tests pass, the owner decides whether PEMS replaces it. |
| --- | --- |
