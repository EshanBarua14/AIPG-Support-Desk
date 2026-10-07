# XFL CSMS - who does what

Customer Support Management System of Xpert Fintech Ltd. Staff of brokerage houses raise support
tickets; the XFL support team assigns, works on and closes them.

## 1. Getting an account

1. **Register** (`/RegisterLogin/Register`): name, email, phone, brokerage house, branch, employee ID, user name, password.
2. A **token** (6 characters) arrives by email. Enter it on **Activate your account**.
3. **Sign in** with user name or email.
4. A new account is a **Maker**. An administrator changes that under **Users > Edit**.

Forgot the password: **Forgot your password?** emails a reset token (valid 24 hours).
A session ends after 10 minutes without a request.

## 2. Which role a user gets

Decided at sign-in, in this order (`RegisterLoginController.Login`):

| Setting on the user | Role | Area |
|---|---|---|
| Administrator = Yes (`UCatagory`) | Administrator | `/Admin/...` |
| XFL staff (`UType`) + position "Support Manager" (`Department` = `Support Maneger`) | Support Manager | `/SupportManegar/...` |
| XFL staff (`UType`) + position "Support Engineer" | Support Engineer | `/SupportEngineer/...` |
| anything else | Maker | `/Maker/...` |

A user who opens an address of another role is sent back to the own dashboard.

## 3. Life of a ticket

```
Maker creates ticket            status Open, nobody assigned     number = HOUSE_0000001
        |
Staff opens "Edit or assign"    picks the engineer               stamps Assigned on, Approved by/on
        |
Engineer works on it            status In queue / In progress
        |
Staff sets status Closed        stamps Closed by/on              ticket leaves the open lists
```

- Setting the engineer back to "Unassigned" clears the assignment and approval stamps.
- Changing a closed ticket back to another status clears the closing stamps (there is no button for it on a closed ticket; see section 6).
- Every save stamps "Last edited" with the editor's name.

## 4. What each role can do

| | Maker | Support Engineer | Support Manager | Administrator |
|---|---|---|---|---|
| Dashboard | own tickets | all tickets + per house | all tickets + per house | all tickets + per house |
| Create ticket | yes | yes | yes | no |
| See tickets | own only | all | all | all |
| Lists | My / Unassigned / Closed | All / Assigned to me / Unassigned / Closed | All / Unassigned / Closed | All / Unassigned / Closed |
| Edit title, details, comments, priority, files | own tickets | any ticket | any ticket | any ticket |
| Assign engineer, set status | no | yes | yes | yes |
| Delete ticket | no | no | no | yes (open tickets) |
| Ticket report | own tickets: priority, status, dates | tickets assigned to / closed by self: house, priority, dates | all: house, priority, status, closed by, dates | same as manager |
| To-do list (personal) | yes | yes | yes | yes |
| Team to-dos + team to-do report | no | no | no | yes |
| Users: change role, disable, delete | no | no | no | yes |
| Master data (houses, branches, support types, categories, sub-categories, affected sections) | no | no | no | yes |
| Profile, change password | yes | yes | yes | yes |

### Maker (brokerage house staff)
1. **Create ticket**: title and priority are required; details (formatted text), comments, support type / category / sub-category / affected section and files are optional. Allowed files: txt, doc, docx, pdf, jpg, jpeg, png, xls, xlsx, csv.
2. The ticket gets the next number of the house and shows up under **My tickets** and **Unassigned**.
3. Follow it on the ticket page: who it is assigned to, the status, the progress steps.
4. While it is open: **Edit ticket** to change the text or priority, add or remove files.
5. **Ticket report**: filter own tickets, print or export to CSV.

### Support Engineer
1. **Unassigned** shows what is waiting; **Assigned to me** shows the own open work (counter in the menu).
2. **Edit or assign** on a ticket: choose the engineer, set status and priority, add comments and files.
3. Set status **Closed** when done.
4. **Ticket report** covers tickets assigned to or closed by the engineer.

### Support Manager
Same pages as the engineer without "Assigned to me". The ticket report covers all tickets and can be filtered by the engineer who closed them.

### Administrator
1. Everything the manager can do with tickets, plus **Delete** (open tickets; the files are removed as well).
2. **Users**: see every account with role and state, change role settings, disable an account, delete an account that has raised no tickets.
3. **Master data**: brokerage houses (name + acronym for ticket numbers), branches, and the four lists offered on the ticket form. An entry that is in use cannot be deleted.
4. **Team to-dos** and **Team to-do report**: the to-dos of all users.
5. First administrator on an empty database: user `admin`, password from `SeedAdmin` in `appsettings.Development.json`.

## 5. To-dos

A personal list per user, not linked to tickets. Add a line, tick it to mark it done, or edit it to rename or cancel.
Statuses: In progress, Done, Canceled. **To-do report** filters by status and date.

## 6. Where the system stands

**Working** (checked in the test runs, see FIXES.md): registration, activation, sign-in, password reset and change;
creating, editing, assigning, closing and deleting tickets; attachments; lists with search, sorting and paging;
ticket and to-do reports with print and CSV; to-dos; user and master-data administration.

**Not there yet**

| Gap | Effect |
|---|---|
| No email when a ticket is created, assigned or closed | people have to look at the lists |
| One "Comments" field that every edit overwrites; no conversation, no history | nobody can see who said or changed what |
| The engineer is stored as a name, not as a user | renaming a user or two users with the same name break "Assigned to me" and the reports |
| No "reopen" button on a closed ticket | a closed ticket can only be changed through the edit address |
| No due date, SLA or escalation | old tickets are only visible as "oldest first" on the dashboard |
| A maker sees only the own tickets | colleagues of the same branch or house cannot follow each other's tickets |
| A user with position "Support Engineer" who is not XFL staff appears in the engineer list but signs in as Maker | a ticket can be assigned to somebody who cannot see it |
| Lists and reports load all tickets and filter in memory | fine for thousands of tickets, slow for hundreds of thousands |

**Security and operations**

| Item | Action |
|---|---|
| The Gmail app password is in `appsettings.json` and in the git history | revoke it, keep the new one outside the repository |
| Activation and reset tokens are 6 hex characters and there is no limit on attempts; no lock-out after wrong passwords | add an attempt limit |
| .NET 6 is out of support; MailKit 4.3.0 has a published advisory | move to a supported .NET and update MailKit |
| Uploaded files are in the repository (`wwwroot/Uplods`) | remove them from git, keep the folder |
| `Controllers/UserController.cs` is dead code (its API class is never registered) | delete it |
| No automated tests in the repository | the checks so far were run from outside |
