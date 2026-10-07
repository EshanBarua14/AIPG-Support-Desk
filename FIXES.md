# XFL CSMS - fixes

What was wrong and what was changed. File names are relative to `XFLCSMS/`.

## 1. Could not build / start on another machine

| Problem | Fix |
|---|---|
| `Controllers/IssueController.cs` had `using NuGet.Protocol;` (unused). It only compiled when a scaffolding package happened to bring NuGet DLLs along. | Removed. |
| The connection string was hard-coded in `Data/DataContext.cs` to the original developer PC (`DESKTOP-21UHJJD\SQLEXPRESS`). | Moved to `appsettings.json` > `ConnectionStrings:DefaultConnection`, wired up in `Program.cs`. |
| A fresh database could not be used: no administrator exists, and registration needs a brokerage house + branch that only an administrator can create. | `Data/DbInitializer.cs`: applies migrations at start-up and, when the Users table is empty, creates the first admin from `SeedAdmin` (see `appsettings.Development.json`). |
| `CSMS.bak` comes from SQL Server 2022; it cannot be restored on SQL Server 2019. | Documented in `read me.txt` (use 2022, or start with an empty database). |

## 2. Bugs that damaged data or gave wrong results

- **Edit Ticket (Admin / Support Manager / Support Engineer) reset the ticket on every save.** The status drop-down was bound to a field the controller never filled, so it always showed "Open": saving any edit re-opened closed tickets and wiped Closed On / Closed By. Assigned On was erased (Admin, Engineer) or re-stamped (Manager) and Approved On re-stamped on every save. Now the stored status and assignee are pre-selected and the dates only change when the assignment or status really changes (`Services/TicketService.cs`).
- **Admin > Edit User demoted staff.** The department drop-down always showed "Maker", so saving a Support Engineer/Manager (e.g. to disable him) turned him into a Maker. "Full Name" showed the user name.
- **Admin > User List > Delete deleted a ticket.** The button called `DeleteTicket` with the *user* id. There is now a real `DeleteUser` (refuses your own account and users who have tickets).
- **Deleting something that is in use logged the admin out.** The foreign-key error went into a catch block that cleared the session; the row also disappeared from the page although nothing was deleted. Deletes now answer "409 - cannot be deleted because ..." and the page shows that message.
- **Every `catch` signed the user out.** About 120 catch blocks removed the session and redirected to the login page on *any* exception, which hid the real error. They now log the exception and show an error page (`CsmsController.HandleError`).
- **Raising a ticket as Support Engineer / Support Manager did nothing.** Their forms posted to the Maker controller (-> login page). Their own actions also crashed on empty details/comments and saved the ticket without a status.
- **Ticket date, number, owner and brokerage house came from editable form fields.** The date was text formatted by the browser's locale (wrong or `0001-01-01` on non-US PCs); the number was reserved when the form was *opened*, so two users could get the same number. All four are now set on the server when the ticket is saved.
- **Attachments.** Two uploads with the same file name overwrote each other; the client file name was used as the path; any file type was accepted; downloads were always sent as `application/pdf`; and the absolute path stored in the database made every download fail after the database was restored on another machine. Files are now stored under a generated name, limited to document/image types, served with the right content type, and looked up in this installation's `wwwroot/Uplods` when the stored path does not exist.
- **Reports.** Maker search threw a NullReferenceException (wrote to `SESearch`); the Support Manager page called a misspelt URL (`/SupportManeger/Search`) and posted to the Admin controller; the Support Engineer Search button was dead (`$ is not defined`); an empty filter crashed; the "To" date excluded that day and a single date was ignored; to-do reports counted status "Completed" although the application stores "Done".
- **To-dos** took the owner from a hidden field and let a user open/update another user's to-do.
- **View Branch / profile branch name** were looked up by brokerage id instead of branch id.
- **Missing pages:** "My Profile" for Support Engineer and Support Manager (404), Maker "Change Password" menu link (empty action), `EditSupportType` was a 400-line copy of the AdminLTE demo page with dead links.
- **Paging:** an empty list showed page 0 of 0 with serial -9; a page number past the end showed an empty table.
- **Forgot password** with a user name tried to send the mail *to the user name*. **Registration** with a failing mail server ended in an error page and left an account that could never be verified; it is now rolled back with a message.

## 3. Security

- Many actions had no session check at all (report searches, attachment download/delete, and the old scaffolding controllers `Home`, `Issue`, `DataTable`, `ServerDT` - including file upload and ticket delete). All role controllers now inherit `CsmsController`, which requires the role's session; the old controllers are admin-only (`Infrastructure/SessionAuthorizeAttribute.cs`).
- Signing in as a second user in the same browser kept the first user's role. The session is cleared on sign-in and sign-out, and no longer contains the password hash/salt.
- A Maker could open, edit and download any ticket by changing the id in the URL, view any profile, and Change Password trusted a posted user id.
- Ticket details are printed as raw HTML -> stored XSS. They now go through an allow-list sanitizer (`Services/HtmlSanitizer.cs`).
- `wwwroot/Uplods` was readable by URL without signing in; direct access is blocked, downloads go through `DownloadAttachment`.
- Sign-in answered "User not found" / "Password is incorrect" as bare error pages; it now shows one neutral message on the login page.

## 4. Validation

- Registration: confirm password and the terms checkbox were only checked in JavaScript; branch must belong to the chosen organisation; duplicate e-mail / user name shown next to the field.
- Reset password: length and confirmation are enforced.
- Admin create/update forms (brokerage house, branch, support type/category/sub-category, affected section) never checked `ModelState`; empty values went to the database and failed. They now re-display the form with the messages. Brokerage name and acronym must be unique (ticket numbers are built from the acronym).
  Note: the existing rules on those models (letters only; 4-100 characters for support types/categories) are now really enforced on the server.

## 5. Pages / scripts

- Register page loaded a stylesheet from `C:\Users\Lenovo\...`; dashboards used `~plugins/...` (missing slash).
- Links to AdminLTE demo pages (`index2.html`, `examples/...`) removed.
- Script errors: copied validation script on the Edit Ticket pages, `DataTable`/`CKEDITOR`/`bsCustomFileInput` used on pages that do not load them, AdminLTE demo `dashboard.js`, stray `">` printed on the Maker dashboard, Show/Hide buttons on Change Password.
- `Views/Shared/Error.cshtml` used the admin layout and crashed for everybody who was not an admin.

## 6. Not changed - please review

- **`appsettings.json` contains a Gmail app password and it is in the git history.** Revoke it and keep the new one in user-secrets / an environment variable.
- .NET 6 is out of support (since Nov 2024). The project still targets `net6.0` with the same packages.
- ~~Pages load jQuery/Bootstrap/DataTables/CKEditor from public CDNs.~~ Done in section 8: nothing is loaded from the internet any more.
- Passwords are a single salted HMAC-SHA512 (kept so existing accounts keep working).
- ~~The AJAX delete calls and some POST forms carry no anti-forgery token.~~ Done in section 8.
- ~~The old scaffolding controllers/views were kept.~~ Removed in section 8.
- **Correction:** earlier versions of this file said `api/UserControllers/*` is public. It is not reachable at all: the API class is nested inside `UserController`, and ASP.NET Core does not treat nested classes as controllers (every `api/...` address answers 404). `Controllers/UserController.cs` is dead code and can be deleted.

## 7. Second pass: menu, wording, and more bugs

**"Customize XFL CSMS" panel removed.** It was AdminLTE's demo script (`dist/js/demo.js`) plus the grid button in the top bar; both are gone from every layout and page.

**Sidebar menu rebuilt** as one shared partial, `Views/Shared/_SidebarMenu.cshtml`, used by all four layouts and all four dashboards (eight hand-copied menus before, each slightly different).
- Order: Dashboard / **Tickets** (Create, list, Assigned to Me, Unassigned, Closed, Ticket Report) / **To-Do** (List, All, Completed, Report, Team To-Dos, Team Report) / **Administration** (Users, Brokerage Houses, Branches, Support Types, Categories, Sub-Categories, Affected Sections).
- The Ticket Report page had no menu entry at all (only a button at the bottom of the dashboard).
- The To-Do sub-menu only opened in the Maker layout; the admin's second sub-menu reused the same element id and never opened.
- The current page is highlighted. The search box above the menu did nothing (the AdminLTE widget never started); it now filters the menu.

**Spelling and labels** (visible text only - URLs, action names and stored values such as `Dashbord`, `SupportManegar`, `"Support Maneger"`, `"Inprogress"` are unchanged so links and existing data keep working):
- Brocarage -> Brokerage, Catagory/Caragory -> Category, Manegar/Maneger -> Manager, Dashbord -> Dashboard, "Phon Number", "Emai", "Sowing 1 to 10 form 27 items" -> "Showing 1 to 10 of 27 items", "one step a way", "Total Inque Tickets", "Todo" -> "To-Do", etc.
- Raw property names used as labels/headers (`TNumber`, `UserId`, `SupportCatagoryId`, `BrokerageHouseName`, `SType`, ...) replaced by readable text; models got `[Display(Name = ...)]`, so validation messages read "The Support Type field is required." instead of "The SType field is required."
- Status values are shown as "In Progress", "In Queue", "Closed" (`Services/DisplayText.cs`).
- The Edit Ticket save button was labelled "Upload"; it is "Save Changes".

**Bugs found by driving the pages in a browser**
- Admin ticket lists: **Delete did not work** - the script read `data-issue-id`, which the link never had, and called `/Admin/DeleteTicket/null`.
- After a search or page-size change the table is replaced by AJAX and the Delete buttons lost their handlers (plain link to a DELETE-only URL). Handlers are now delegated.
- The search box listened to `input` and `change`: every search ran twice and the second one redrew the table while Delete was being clicked.
- Searching from page 2+ kept the old page number and showed an empty table; the highlighted page number always linked to page 1.
- **Print Results killed the report page**: it replaced the whole `<body>` and pasted the HTML back, destroying all event handlers, so Search was dead after printing. It now prints the result block through a print style.
- Report pages passed the wrong model to the result partial (`Html.Partial(..., Model.Issues)`); also removes the MVC1000 build warnings.
- Pie chart legend showed "NaN%" on a database without tickets.
- The file picker refused .docx/.xlsx although the server accepts them.

**Still open:** `dotnet build` reports NU1902 for MailKit 4.3.0 (moderate advisory). Updating the package needs a NuGet restore, which could not be done or tested here.

## 8. Third pass: new interface, AdminLTE removed

**What went:** AdminLTE, Bootstrap 4 and 5, three jQuery versions, DataTables, CKEditor, Chart.js, Font Awesome, Ionicons, the Google font and every CDN link. `wwwroot` (without the uploads) shrank from about 106 MB to about 0.3 MB. The application now works without internet access.

**What came instead** (no framework, everything in the project):

| File | Purpose |
|---|---|
| `wwwroot/css/app.css` | all styles; colours, radii and fonts are variables at the top (light and dark theme) |
| `wwwroot/js/app.js` | all behaviour, switched on with `data-*` attributes (list search/sort/paging, confirm dialogs, editor, file picker, reports, CSV export) |
| `wwwroot/fonts/*` | IBM Plex Sans / Sans Condensed / Mono (SIL Open Font License, see `OFL.txt`) |
| `wwwroot/img/icons.svg` | icon sprite (Lucide icons, ISC licence); used through `<icon name="..." />` |
| `Infrastructure/IconTagHelper.cs`, `Infrastructure/Ui.cs` | the `<icon>` tag and the shared display helpers (role, dates, ticket chip, status / priority pills) |
| `Views/Shared/_Layout.cshtml`, `_Nav.cshtml`, `_AuthLayout.cshtml` | one page frame for the four roles, one for the pages before sign-in |

**Views: 125 -> 72 files.** The role folders held four copies of most pages. Pages that are the same for every role now exist once in `Views/Shared` (the view engine looks there when `Views/<Controller>/` has no file), and what differs per role is decided by `Ui.Role(...)`: `Dashbord`, `TicketView`, `EditTicket`, `IssueRaiseFrom`, the ticket lists (`_TicketList`), the to-do lists (`_TodoList`), `GetTodo`, `Reports`, `TodoReports`, `Profile`, `ChangePassword`. The 24 master-data pages follow one pattern.

**New in the interface**
- Light and dark theme (follows the system, switch in the top bar, remembered per browser). Works on a phone: the menu becomes a drawer, the ticket list becomes cards.
- Ticket lists: search, page size, sorting and paging without reloading the page; the address keeps the state, so reload and Back work. Tabs for All / Assigned to me / Unassigned / Closed. Counters in the menu for unassigned tickets and (engineers) "Assigned to me".
- Dashboard: open tickets with the open/closed share, unassigned / high-priority / assigned-to-me counts, raised-and-closed per period as a table (today, week, month and year no longer share one chart axis), tickets waiting for an engineer (oldest first), load per brokerage house, latest tickets.
- Ticket page: number, status, priority, progress steps (raised, assigned, in progress, closed), details shown as formatted text, who did what and when.
- Create / edit ticket: small built-in text editor, drag-and-drop file picker that lists the chosen files and refuses other file types before sending, priority as three buttons.
- To-dos: one click marks a to-do as done; the admin's team list shows the owner.
- Reports: run in the page, print (only the report is printed) and export to CSV.
- Users: role and account state in the list; the edit page shows which role the three settings add up to.
- Delete asks in a dialog that names the item; the answer of the server (for example "used by existing tickets") is shown as a message.
- A missing page or ticket shows a "does not exist" page instead of an empty tab.

**Bugs and gaps fixed on the way**
- **Anti-forgery on every POST / DELETE** (`Program.cs`): another website could make a signed-in admin's browser delete tickets or change roles. A page that is out of date now gets a message instead of an empty "400".
- **Sign-out is a POST.** As a link, any page (or an image address inside a ticket) could sign users out.
- **Ticket details are cleaned when saved as well as when shown, and the cleaner was rewritten** (`Services/HtmlSanitizer.cs`): the text is now printed inside the page instead of an editor frame, so classes, positioning and sizing styles are removed, tags are balanced (an unclosed link could turn the rest of the page into a link), images must carry their own data (an image address made the reader's browser call that address), links must be web or mail addresses. The old cleaner used regular expressions that took minutes on deliberately broken input of 100 KB; the new one is a single pass.
- Signed-in pages are sent with `Cache-Control: no-store` (replaces the script that pushed the browser history forward and the scripts that blocked F12 / right click).
- The four dashboards, and the eight "ticket to view model" blocks, were separate copies; they are one method each now (`CsmsController.BuildDashboardAsync`, `ToMakerView`).
- "Unassigned" lists showed tickets that were closed without ever being assigned.
- To-do lists: the chosen sort order was undone inside each page (re-sorted by id). Paging lost the sort order on all lists.
- To-do report: the admin filter offered "Closed" and "Canceled" with wrong values (`Done` / `Completed`), the engineer filter sent `In Progress` while `In progress` is stored, and the engineer report counted "in progress" as 0 for the same reason.
- After saving or creating a ticket the user lands on the ticket (with a confirmation) instead of a list.
- After a password change the sign-in page says so. The sign-in page sends a signed-in user to the dashboard.
- Removed: `IssueController`, `DataTableController`, `ServerDTController` and the scaffold actions of `HomeController` with their views (old prototypes, admin-only, not in any menu), the unused `jquery.datatables` package reference and the stale `<None Include="Views\...">` entries in the project file.

**Checked with:** 250 server checks (`requests`), 80 browser checks in headless Chromium (every page of every role on desktop and phone width: no script errors, no failed or external requests, labelled controls; then the main flows clicked through), on a stand-in database. Not checked: real e-mail delivery, a real SQL Server, browsers other than Chromium.

## 9. Fourth pass: brokerage house users can get in and work

The ticket side for brokerage house users (role Maker) already worked. What could stop them was everything before it:

| Blocker | Before | Now |
|---|---|---|
| The token e-mail does not arrive (mail server down, wrong mail password, spam filter) | registration was rolled back with "could not send"; nobody could register while mail was broken, and an administrator could do nothing about it | the account is kept as **Not verified**, the person is told to ask XFL support, the administrator sees the number of waiting accounts in the menu and presses **Activate** (`AdminController.ActivateUser`) |
| Only self-registration | an administrator could not create an account at all | **Users > New user** (`CreateUser`): active at once, any role |
| Lost password and the reset mail does not arrive | no way back in | **Users > Edit > Set new password** (`SetUserPassword`) |
| A house without a branch | registration impossible for that house, nothing said so | the house list shows the number of branches and flags "No branch" with a link to add one; creating a house says so; the registration and new-user forms say whom to ask |
| The application answers on `localhost` only | nobody on another computer could open it | launch profile **lan** (`dotnet run --launch-profile lan`: http on port 5100 on all network cards, Production mode); steps in `read me.txt` section 6 |

Also changed
- **A change by the administrator works at the next click** (`CsmsController.OnActionExecuting`): the session kept a copy of the user from sign-in and its time-out restarts with every request, so a disabled or deleted user, or one whose role or password was changed, stayed signed in for as long as they kept clicking. Each request now compares status, role and a fingerprint of the password hash with the database; a mismatch ends the session with a note on the sign-in page.
- Only XFL staff can hold the position Support Engineer / Support Manager (`UpdateUser`, `CreateUser`, the assignee list on tickets). A brokerage house user stored as "Support Engineer" used to be offered as assignee although he signs in as Maker and cannot see the ticket.
- The user list shows the brokerage house; the user page shows "Not verified" (it said "Active").
- A user name can no longer contain a space or `@`, and user name and e-mail are checked against each other: both are accepted in the sign-in box. Length limits on name, e-mail and phone at registration.
- Activate asks first and says that anybody can register under any name. New accounts, activations and passwords set by an administrator are written to the application log.

**Checked with:** 349 server checks and 108 browser checks (both ways in, clicked through from the registration form to a closed ticket, on desktop and phone width), plus a run in Production mode through the network address of the machine. Not checked: real e-mail delivery, a real SQL Server, another physical computer, browsers other than Chromium.

## 10. Fifth pass: five roles, assignment, audit trail, system health

- **Roles and permissions in one place** (`Infrastructure/Rbac.cs`): platform admin, support manager, support engineer, house admin (new), house user. Every action that is not for everybody carries `[RequirePermission(...)]`; it is checked before the action runs (`CsmsController.OnActionExecuting`) and answers 403 "Your role does not allow this". Menus and buttons ask the same table, so a button is there exactly when its action is allowed.
- **House admin** (`HouseAdminController`, area `/HouseAdmin`): the tickets of the house, its accounts (create, activate, edit, password, disable, delete), its branches, its part of the audit trail. Never anything of another house.
- **Assignment by account** (`Issues.AssignedToId`): assign / reassign / unassign with a dialog that shows each engineer's load; engineers take a ticket and give it back; workload page. Tickets assigned by name before this still work. Renaming an engineer keeps his tickets.
- **Audit trail** (`AuditLogs`, `Services/AuditService.cs`): sign-ins, accounts, tickets, master data, written in the same transaction as the change. Pages per role (everything / ticket lines / the own house), filter, CSV export, and the history on every ticket page.
- **System health** page and `/health` endpoint.
- Database: migration `AuditTrailAndAssignee` (one new table, one new nullable column, indexes; applied at start-up).

## 11. Sixth pass: statuses, notifications, permissions from the page, folding menu, demo data

**Ticket statuses** (`Services/TicketStatus.cs`, rules in `TicketService.SetStatus` / `Assign`)
- Eight statuses: Unassigned, Assigned, In progress, Pending, Waiting for review, Done, Deployed, Closed. The stored values of the old four did not change (`Open`, `Inqueue`, `Inprogress`, `Close`).
- A ticket is Unassigned exactly while it has no engineer; assigning makes it Assigned; a working status needs an engineer; Deployed, Closed and reopening need the permission "deploy, close and reopen".
- Status buttons on the ticket page (one click, closing asks first), seven progress steps, status chips above the lists, menu **By status** with counts, a **board** with one column per status, tickets per status on the dashboard and in the report.
- At start-up, tickets written by older versions are brought in line once (`DbInitializer.NormaliseStatuses`): other spellings ("In Progress", "Closed"), "Open" with an engineer, a working status without one. One line in the audit trail says how many.
- The edit form only applies the status and engineer the user really changed on it. Before, a form that sat open while a colleague took the ticket silently unassigned it again on save.

**Notifications** (`Services/Notify/`, `CsmsController.Notifications.cs`)
- In the application: a message appears on every open page of the person within a second or two (Server-Sent Events, one stream per visible tab), plus a bell with the unread count, a panel and a list page. What happens while no page is listening is shown on the next page, once.
- E-mail and SMS: rows in `NotificationDeliveries`, written in the transaction of the change, sent by a background worker with retries (1, 5, 30 minutes), listed with the reason when they fail, "send again" button. A message that went out is never sent twice, also not across a restart. While a server does not answer, the other channel is not held up.
- Settings page: channel switches, event x channel table, mail server, SMS gateway (any HTTP provider, template with `{to} {message} {sender} {key}`), test buttons, site address for links.
- The mail server entered on the page is used for registration and password-reset mail too; `appsettings.json` remains the fallback.
- Every person can switch channels off for himself.

**Permissions from the page** (`CsmsController.System.cs`, stored in `AppSettings`)
- The grant table is a page of switches, in force at once, audited, with reset. `Rbac.Grantable` limits what a role can ever hold; `Rbac.IsFixed` what the platform admin can never lose.
- So that a grant is real, the pages that were built into the admin area moved to the shared base: master data (24 actions), team to-dos, system health, delete ticket. Their views moved from `Views/Admin` to `Views/Shared`.
- The four role controllers lost about 3,400 lines of copied code (5,900 to 2,500): ticket lists, ticket page, edit, report and to-do editing now exist once (`CsmsController.Tickets.cs`, `CsmsController.TeamTodos.cs`). Side effects that are fixes: the admin's report and lists respect what the admin may see; "Closed by" in the report offers everybody who closed a ticket; a to-do of another person can be edited exactly with "team to-dos".
- Wording on pages follows the permissions, not the role name ("My tickets" for anybody who sees only his own).

**Menu**: groups and the sub menus "By status" and "Support lists" fold (click or keyboard); remembered per browser; the group of the open page never folds away. Hiding the whole menu works as before.

**Demo data** (`Services/DemoDataService.cs`): load and remove from **System > Demo data**; see WORKFLOWS.md section 8.

**Found by an independent review of this pass and fixed before delivery**
- Demo accounts had a fixed password that anybody could know. Now a random one per load, shown on the page; loading next to real tickets needs an explicit tick; removing checks that each row still is demo data.
- A house user could have been given "see the tickets of the house". Since anybody can register for any house, that permission can now go to the house admin only.
- The system settings could have been given to a support manager, who could then point the mail server at his own and read password-reset mail. They stay with the platform admin; and a stored mail password or SMS key is never sent to a changed server: it has to be entered again.
- A ticket title containing `{key}` could have pulled the SMS key into the text sent to the person who raised the ticket (JSON gateways). The template is now filled in one pass.
- Links in e-mails were built from the host name of the request, which a visitor can choose. They now come from the site address saved in the settings (no link until it is saved).
- Password-reset tokens were 6 characters and the only thing the reset page asks for. Now 32.
- An open page doubled the idle time before sign-out. The 10 minutes now count from the last thing the person did.
- Status chips were all grey; the bell badge was hard to read in the dark theme; pages shown again after a refused form had lost the menu numbers and the bell.
- `/health` used an overload that does not exist in .NET 6 (it would not have compiled).

**Database**: migration `NotificationsAndSettings` (four new tables: `AppSettings`, `Notifications`, `NotificationDeliveries`, `NotificationPreferences`; nothing existing is changed; applied at start-up).

**Checked with:** 1,149 checks in seven suites on a stand-in database: 895 server checks (351 from the earlier passes, 267 for roles, 277 for this pass), 247 browser checks in headless Chromium (every page of every role on desktop and phone width; the five roles; and for this pass 83: folding menu, status buttons, messages appearing on the open page of another signed-in person, permissions, settings, demo data), and 7 for the sign-out after idle time. Also: an empty database from the first administrator through demo data and back (37 checks), and Production mode over the network address (12). E-mails were received by a test mail server, SMS by a test gateway. Two independent reviews of the code; their findings are listed above. Not checked: a real SQL Server (the migration script was read: four `CREATE TABLE`, two indexes), a build with the .NET 6 reference assemblies (built here with SDK 8 against the EF Core 6 assemblies, language version 10), a real mail server and a real SMS provider, browsers other than Chromium.

## New files

`Controllers/CsmsController.cs`, `Infrastructure/SessionAuthorizeAttribute.cs`, `Services/TicketService.cs`,
`Services/PasswordHasher.cs`, `Services/HtmlSanitizer.cs`, `Data/DbInitializer.cs`,
`Views/SupportEngineer/Profile.cshtml`, `Views/SupportManegar/Profile.cshtml`,
`Views/Shared/_SidebarMenu.cshtml`, `Services/DisplayText.cs`

Third pass: `Infrastructure/Ui.cs`, `Infrastructure/IconTagHelper.cs`, `Infrastructure/AntiforgeryFailureFilter.cs`, the shared views listed in section 8, `wwwroot/css/app.css`, `wwwroot/js/app.js`, `WORKFLOWS.md`.
(`Views/Shared/_SidebarMenu.cshtml` became `_Nav.cshtml`; the two per-role `Profile.cshtml` copies are gone again.)

Fourth pass: `Models/Admin/NewUser.cs`, `Views/Admin/CreateUser.cshtml`.

Fifth pass: `Infrastructure/Rbac.cs`, `Infrastructure/RecentLog.cs`, `Controllers/CsmsController.Assignment.cs`, `.Users.cs`, `.Audit.cs`, `Controllers/HouseAdminController.cs`, `Services/AuditService.cs`, `Services/SystemHealthService.cs`, `Models/Audit/*`, `Views/HouseAdmin/Organization.cshtml`, `Views/Shared/Workload.cshtml`, `AuditTrail.cshtml`, `SystemHealth.cshtml`, `_AssignDialog.cshtml`, `_RoleChoice.cshtml`, migration `20261007092004_AuditTrailAndAssignee`.

Sixth pass: `Services/TicketStatus.cs`, `Services/SettingsStore.cs`, `Services/DemoDataService.cs`, `Services/Notify/*` (events, service, hub, worker, SMS sender), `Services/EmailService/MailSettings.cs`, `Models/Notify/*`, `Models/Settings/*`, `Controllers/CsmsController.Tickets.cs`, `.MasterData.cs`, `.TeamTodos.cs`, `.Notifications.cs`, `.System.cs`, `Views/Shared/TicketBoard.cshtml`, `Permissions.cshtml`, `NotificationSettings.cshtml`, `Notifications.cshtml`, `_NotificationItems.cshtml`, `DemoData.cshtml`, migration `20261007120446_NotificationsAndSettings`.
