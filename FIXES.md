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
- Pages load jQuery/Bootstrap/DataTables/CKEditor from public CDNs, so they need internet access.
- Passwords are a single salted HMAC-SHA512 (kept so existing accounts keep working).
- The AJAX delete calls and some POST forms carry no anti-forgery token.
- The old scaffolding controllers/views were kept (admin-only now); `api/UserControllers/*` is still public.

## New files

`Controllers/CsmsController.cs`, `Infrastructure/SessionAuthorizeAttribute.cs`, `Services/TicketService.cs`,
`Services/PasswordHasher.cs`, `Services/HtmlSanitizer.cs`, `Data/DbInitializer.cs`,
`Views/SupportEngineer/Profile.cshtml`, `Views/SupportManegar/Profile.cshtml`
