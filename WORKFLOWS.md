# Xpert CSMS - who does what

Customer Support Management System of Xpert Fintech Ltd. (version 3.3). Staff of brokerage houses raise
support tickets; the XFL support team assigns them, works on them and closes them. Everybody is told what
happens to their tickets: on the page while they work, by e-mail, and by SMS.

## 1. The five roles

A person has exactly one role. It is set on the account (**Users > Edit**) and decides which area of the site
the person lands in.

| Role | Who | Area | Sees |
|---|---|---|---|
| **Platform admin** | administrator of XFL | `/Admin/...` | everything, for every brokerage house |
| **Support manager** | XFL staff | `/SupportManegar/...` | every ticket; assigns the engineers |
| **Support engineer** | XFL staff | `/SupportEngineer/...` | every ticket; works on the ones assigned to him |
| **House admin** | administrator of one brokerage house | `/HouseAdmin/...` | the tickets, people and branches of that house |
| **House user** | staff of a brokerage house | `/Maker/...` | the tickets he raised himself |

Somebody who opens an address of another area is sent back to the own dashboard. Somebody who opens a page
his role has no permission for gets "Your role does not allow this" (403).

### What each role may do

This is the default. A platform admin changes it under **System > Roles & permissions** (section 7).

| Permission | Platform admin | Support manager | Support engineer | House admin | House user |
|---|:-:|:-:|:-:|:-:|:-:|
| See all tickets | yes | yes | yes | - | - |
| See the tickets of their house | - | - | - | yes | - |
| Raise tickets | - | yes | yes | yes | yes |
| Edit the tickets of their house (colleagues' tickets) | - | - | - | yes | - |
| Assign tickets | yes | yes | no | - | - |
| Take tickets, give them back | - | - | yes | - | - |
| Work on any ticket (status, comments) | yes | yes | no | - | - |
| Deploy, close and reopen | yes | yes | yes | - | - |
| Delete tickets | yes | no | - | - | - |
| Workload page | yes | yes | no | - | - |
| Service report | yes | yes | no | yes (own house) | - |
| Manage canned replies | yes | yes | no | - | - |
| Service targets | yes | no | - | - | - |
| Manage all accounts | always | no | - | - | - |
| Manage the accounts of their house | - | - | - | yes | - |
| Manage the branches of their house | - | - | - | yes | - |
| Master data | yes | no | - | - | - |
| Team to-dos | yes | no | - | - | - |
| Whole audit trail | yes | no | - | - | - |
| Audit trail of tickets | no | yes | no | - | - |
| Audit trail of their house | - | - | - | yes | - |
| System health | yes | no | - | - | - |
| Notification settings and demo data | yes | - | - | - | - |
| Change the permissions | always | - | - | - | - |

"-" means the role can never be given that permission. Three things are fixed on purpose:

- **House roles never reach outside their house.** Everything that works across houses can only go to XFL roles.
- **A house user never sees more than his own tickets.** Anybody can register for a brokerage house and activate
  the account with the token from his own e-mail, so "house user" proves nothing about a person. People who
  should see the whole house are made house admin by an administrator.
- **The platform admin always keeps the accounts and the permissions page**, so a wrong setting can be repaired.
  The system settings (mail server, SMS gateway, demo data) stay with the platform admin too: whoever sets the
  mail server can read password-reset e-mails.

Everybody always may: see and edit the own open tickets, keep a personal to-do list, see the own notifications,
change the own password.

## 2. Getting an account

Before anybody of a brokerage house can start, a platform admin creates the **house** and at least one **branch**
(the house list flags a house without a branch: nobody can register for it).

**Way 1 - the person registers**
1. **Register** (`/RegisterLogin/Register`): name, e-mail, phone, brokerage house, branch, employee ID, user name, password.
2. A **token** (8 letters and digits) arrives by e-mail. Enter it together with the e-mail address on **Activate your account**.
3. No e-mail? The account waits as **Not verified**. The platform admin and the admin of that house see it: a
   notice under the bell, and the number of waiting accounts in the menu. They press **Activate** under **Users**
   - after the person has confirmed that the registration is theirs (anybody can register under any name).
4. **Sign in** with user name or e-mail. A registered account is a **house user**.

**Way 2 - an administrator creates the account**
**Users > New user**: same details plus the role. The account is active at once; the administrator passes the
user name and password on (the owner gets an e-mail with the user name, never with the password). At the first
sign-in the owner has to choose an own password before anything else.
A platform admin creates any account; a house admin creates house users and house admins of the own house.

Forgot the password: **Forgot your password?** e-mails a reset token (32 characters, valid 24 hours). The page
answers the same for every name, so it does not tell who is registered. If the mail does not arrive, an
administrator sets a new password under **Users > Edit**; the owner then has to replace it at the next sign-in.

**Passwords.** A new password has 10 or more characters with at least one letter and one digit, does not contain
the user name or the e-mail address and is not a common one. Passwords that existed before version 3.2 stay valid.

**Wrong attempts.** Five wrong passwords or activation tokens in a row lock the account for 15 minutes; during
that time the right password is refused too. The lock ends by itself, with **Users > Edit > Unlock now**, with a
new password from an administrator, or with a reset by e-mail. A name that has no account gets exactly the same
answers. Thirty failed attempts from one network address in ten minutes make the site refuse that address for a
while. The numbers are settings ("SignIn" in appsettings.json).

A sign-in ends after 10 minutes in which the person did nothing (an open page that only listens for notifications
does not count as doing something). It also ends at the next click when an administrator disables the account,
changes its role or house, deletes it, or when its password changes.

## 3. Life of a ticket

A ticket has one of eight statuses. Lists, the menu, the board, the dashboard and the reports all use them.

```
Unassigned --assign--> Assigned --> In progress --> Waiting for review --> Done --> Deployed --> Closed
                                       ^   |
                                       +-- Pending   (work has stopped: waiting for somebody else)
```

| Status | Means | Who sets it |
|---|---|---|
| **Unassigned** | raised, no engineer yet | the system: every new ticket, and every ticket that loses its engineer |
| **Assigned** | an engineer has it and has not started | the system, when the ticket gets an engineer |
| **In progress** | the engineer is working on it | the engineer of the ticket, or anybody with "work on any ticket" |
| **Pending** | work has stopped, waiting for information | same |
| **Waiting for review** | finished by the engineer, waits to be checked | same |
| **Done** | checked and accepted, not live yet | same |
| **Deployed** | the fix or change is live | same, with "deploy, close and reopen" |
| **Closed** | nothing more to do | same, with "deploy, close and reopen" |

Rules the system keeps:
- A ticket is **Unassigned exactly while it has no engineer**. A working status needs an engineer; taking the
  engineer away makes the ticket Unassigned again.
- The statuses can be set in any order (a ticket can go straight from In progress to Done). The ticket page
  marks the usual next step.
- **Closed** stamps who and when, and the people of the house can no longer edit the ticket. Reopening (any other
  status on a closed ticket) needs "deploy, close and reopen" and clears the stamp.
- Every step is written to the **history** on the ticket page (who, in which role, when, from what to what) and
  to the audit trail.

### Conversation
Every ticket has a conversation on its page (it replaced the single "Comments" field, which every edit overwrote).
- **Reply**: everybody who may open the ticket writes there: formatted text and files. An entry keeps its author,
  role and time and cannot be changed or removed afterwards.
- **Internal note** (XFL staff only): a tick under the reply box. The brokerage house never sees the note, its
  files, or any trace of it in the history of the ticket or in the activity list of the house.
- **Canned replies**: XFL staff choose a ready-made text above the reply box and adapt it. `{name}` becomes the
  first name of the person who raised the ticket, `{ticket}` the ticket number, `{me}` the writer. The texts are
  managed under **Tickets > Canned replies** (permission "manage canned replies").
- A reply does not change the status. Replies are possible on closed tickets too; they do not reopen it.
- The "first message" of the create form opens the conversation.

### Service targets
A ticket gets two target times when it is raised, from **System > Service targets**: the first response and the
solution, per priority (defaults: High 0.5 h / 4 h, Medium 2 h / 18 h, Low 4 h / 45 h of working time).
- **Working time**: the working days and hours and the days off entered on that page (default Sunday to Thursday,
  09:00 to 18:00). A priority can count round the clock instead.
- **First response** is the first reply of XFL staff that the house can read, or the engineer setting the ticket
  to In progress - whichever comes first. Assigning a ticket and internal notes do not count.
- **Solved** is the first time the ticket reaches Done, Deployed or Closed. While it is **Pending** the clock for
  the solution stands still, and the target moves later by that time. Reopening makes it unsolved again.
- Lists show how an open ticket stands ("Reply due in 20 min", "Due in 3 h", "Overdue 2 d", "On hold"); the
  ticket page shows both targets. **Overdue** in the menu lists the open tickets past a target.
- The engineer of a ticket is told when 75 % of the time to a target is used; the people who assign tickets when
  it has passed. Each message goes out once per ticket and target.
- A ticket keeps the targets it got. Changing the priority recalculates them; changing the settings affects
  tickets raised afterwards. Tickets raised before version 3.3 have no targets.

### Rating
When a ticket is closed, the person who raised it is asked on its page how the support was: 1 (bad) to 5 (very
good) and an optional sentence, once. XFL staff see the rating on the ticket; the engineer and the people who
assign tickets are told.

### Assignment
- Tickets are assigned to **support engineers** (active XFL accounts with that role).
- **Assign / Reassign** (permission "assign tickets"): from the lists, the ticket page, the workload page or the
  edit form. The dialog shows how many open tickets each engineer has.
- **Take** (permission "take tickets"): an engineer takes an unassigned ticket; **Give back** returns it.
- **Workload**: open tickets per engineer (in progress, pending, waiting for review, to close, high priority,
  oldest), and the tickets that wait for an engineer.

### Where to find tickets
- **All tickets / House tickets / My tickets**: the main list. Status chips above it narrow it to one status;
  search, sorting, page size and paging keep the status.
- Menu **Tickets > By status**: one entry per status with the number of tickets.
- **Board**: one column per status, most urgent first (closed: the last 30 days). An engineer can switch to "Mine".
- **Assigned to me** (engineers), **Unassigned**, **Closed**.
- **Dashboard**: see below.
- **Ticket report**: filter by house, product, priority, status, closed by, dates; print or export to CSV. The
  result comes with charts (raised and closed over time, by priority, by product, by house, time to close);
  **Table** turns them into tables. Every role reports on the tickets it may see.
- The search of every list also finds a ticket by the name or short name of its product.
- **Service report** (permission "service report"): for the last 7, 30, 90 days or 12 months: how long the first
  response and the solution took (the time within which half of the tickets were done, in working time), how many
  targets were met, ratings, tickets raised and solved per day, open tickets by age, and the same per priority,
  product, engineer and brokerage house. A house admin sees the own house.

### The dashboard
Every role has one, over the tickets it may see. It is made of parts:

| Part | Shows |
|---|---|
| Queue | open tickets now, unassigned, high priority, closed, total; one bar with a segment per status (each number opens its list) |
| Raised and closed | for the chosen period: raised, closed, how long half of the closed tickets took, what is still open - each compared with the period before - and a line chart per day (week, month) |
| By product, By priority | the tickets raised in the period, split into not closed and closed |
| Time to close | the tickets closed in the period, in five groups from "under 1 day" to "over 4 weeks" |
| Open tickets per engineer | now (roles with "workload") |
| Periods at a glance | today, 7 days, 30 days, 12 months as a table |
| Waiting for an engineer, By brokerage house | XFL staff |
| Latest tickets | the newest six |

- **Period** (7 days, 30 days, 90 days, 12 months) sits above everything it changes; the queue is always "now".
- Every chart answers the pointer and the keyboard (focus it, then the arrow keys) with its numbers, and has
  **Table** for the same numbers as a table.
- **Customize**: hide a part, move it (buttons or drag), make it wide or narrow; **Reset the layout** puts
  everything back. Period and arrangement are remembered per person in the browser that was used.

### The side menu
Groups and sub menus fold with a click on their name. The button at the top left collapses the whole menu to a
rail of icons and back; the rail opens over the page while the pointer or the keyboard focus is in it. On a
phone the same button opens and closes the menu. All of it is remembered per browser.

### Raising and editing
1. **Create ticket**: title and priority are required; details (formatted text), comments, product, support
   type / category / sub-category / affected section and files are optional. Choosing the **product** narrows
   the four lists to the entries of that product plus the ones for all products; an entry of the product
   replaces one for all products that has the same name. The server keeps only entries that fit the product
   (without a chosen product, the product of the chosen type counts). The ticket page, the lists (short name)
   and the report show the product. Allowed files: txt, doc, docx, pdf, jpg,
   jpeg, png, xls, xlsx, csv. The ticket gets the next number of the house (`ABC_0000001`).
2. People of the house edit title, details, comments, priority and files while the ticket is open: a house
   user his own tickets, a house admin every ticket of the house.
3. XFL staff also set the status and (with "assign tickets") the engineer on the edit form. Only fields that
   were really changed on the form are applied, so a form that sat open does not undo what a colleague did.

## 4. Notifications

Three channels: **in the application** (a message at the bottom of every open page, at once, and the list
under the bell), **e-mail**, **SMS**. The person who does something is never told about it.

| Event | Who is told | App | E-mail | SMS |
|---|---|:-:|:-:|:-:|
| Ticket raised | XFL staff who assign tickets, and the admins of that house | on | on | off |
| Ticket assigned | the engineer who got it, and the person who raised it | on | on | on |
| Ticket unassigned | the engineer who had it, and XFL staff who assign tickets | on | off | off |
| Status changed | the person who raised it and its engineer | on | off | off |
| Ticket deployed or closed | the person who raised it and its engineer | on | on | on |
| Ticket edited | the person who raised it and its engineer | on | off | off |
| Reply in the conversation | the person who raised it and its engineer; without an engineer: XFL staff who assign tickets | on | on | off |
| Internal note | the engineer of the ticket (XFL staff only) | on | off | off |
| Support was rated | the engineer and XFL staff who assign tickets | on | off | off |
| Service target is due soon | the engineer; without one: XFL staff who assign tickets | on | off | off |
| Service target was missed | XFL staff who assign tickets, and the engineer | on | on | off |
| Registration waiting for activation | the administrators who can activate it | on | off | off |
| Account activated | the owner of the account | off | on | on |
| Account created by an administrator | the owner | off | on | off |
| Password set by an administrator | the owner | off | on | on |

These are the defaults; a platform admin changes every cell under **System > Notification settings**. SMS as a
whole is off until a gateway is entered there.

- **Bell** (top bar): number of unread notices; a click lists the newest; **Mark all read**; a notice opens the
  ticket or account it is about.
- **Notifications** page (link in the bell): the whole list, and **Preferences**: each person switches the three
  channels on or off for himself.
- E-mail and SMS are written to a queue in the same step as the change they report and sent a moment later by
  a background sender. A slow or broken mail server never holds up a page. A message that fails is tried again
  after 1, 5 and 30 minutes; then it is listed as failed with the reason, and can be sent again with one button.
- An e-mail carries a button that opens the ticket, once the **address of the site** is saved in the settings.
- Passwords are never part of a notification.

## 5. The house admin

- **Users**: the house users and house admins of the own house: create, activate a waiting registration, edit,
  change the role (house user / house admin), set a new password, disable, delete (an account without tickets).
- **Branches**: add, rename, remove (a branch without accounts).
- **Activity**: the audit trail of the house: its tickets, accounts, branches and sign-ins.
- Tickets: every ticket of the house, with who raised it; may edit them while they are open.

## 6. To-dos

A personal list per user, not linked to tickets. Add a line, tick it to mark it done, or edit it to rename or
cancel. Statuses: In progress, Done, Canceled. **To-do report** filters by status and date. With "team to-dos":
the lists of all users, editing included, and the report over them.

## 7. The platform admin

**Administration**
- **Users**: every account with house, role and state; create, activate, edit (name, e-mail, phone, house while
  the account has no tickets, branch, role, active / disabled), set a password, delete.
- **Master data**: brokerage houses (name + acronym for ticket numbers), branches, products, support types,
  categories, sub-categories, affected sections. An entry that is in use cannot be deleted.
- **Products**: what brokerage houses get support for. Each product has a name, a short name (shown in lists
  and the report), a description and **Active**.
  - The page of a product lists its support types, categories, sub-categories and affected sections; add,
    rename and delete them there. The same entries are on the four pages under **Support lists**, with a
    Product column; an entry with "All products" is offered whatever the product.
  - Names: 2 to 100 characters, letters, digits, spaces and `. , & / ( ) + ' -`. The same name can exist once
    per product and once for all products.
  - An **inactive** product is no longer offered on the ticket form (nor are its entries); its tickets stay.
  - Deleting a product deletes its entries with it. A product that tickets were raised for cannot be deleted:
    make it inactive. An entry that tickets use can be opened to all products, but does not move to a
    product while tickets of another product use it.
  - A new installation starts with five products and their types and categories (`Data/ProductSeed.cs`). They
    are added once; what is renamed or deleted afterwards stays that way.

**System**
- **Audit trail**: who did what and when: sign-ins (also failed ones, and accounts locked by them), accounts, tickets, master data, settings.
  Filter by date, kind, house, text; export to CSV. Lines are only ever added.
- **System health**: database, e-mail, notifications (open pages listening, queue, failed messages, SMS), file
  storage, security settings, the support queue, facts about the installation, the latest warnings and errors.
  `/health` answers `Healthy` or `Unhealthy` for monitoring tools, without sign-in.
- **Service targets**: on / off, the two times per priority, working days and hours, days off, when to warn.
- **Roles & permissions**: the table of section 1 as switches. A change is in force as soon as it is saved, also
  for people who are signed in; it is written to the audit trail; **Reset to defaults** puts everything back.
  A locked box cannot be changed, a dash cannot be given.
- **Notification settings**: the three channel switches; the table "which event uses which channel"; the address
  of the site for links in e-mails; the **mail server** (host, port, encryption, user, password, sender) with
  **Send test e-mail**; the **SMS gateway** with **Send test SMS**; the list of what was sent, with the reason
  for every failure.
  - The mail server entered here is used for everything, also registration tokens and password resets. While
    nothing is entered, the values of `appsettings.json` are used.
  - Passwords and keys are stored encrypted and never shown again. An empty field keeps what is stored - unless
    the server, user name or gateway address changes: then the secret has to be entered again.
  - The SMS gateway works with any provider that takes a web request: enter the address and the fields from the
    provider's API description, with `{to}`, `{message}`, `{sender}`, `{key}` where the number, the text, the
    sender name and the key belong.
- **Demo data**: see section 8.

A support manager can be given master data, team to-dos, delete tickets, the whole audit trail, system health
and "manage all accounts" on the permissions page. With "manage all accounts" he manages everybody except
platform admins, and cannot make anybody platform admin.

## 8. Demo data

**System > Demo data > Load demo data** fills the system so every page can be tried with content:
3 brokerage houses with 7 branches, 15 accounts of every role (one of them waiting for activation), 64 tickets
in every status with their history, to-dos, and notices under the bell.

- All demo accounts share one password. It is made up new for every load and shown on the demo data page.
- User names start with `demo.` (`demo.manager`, `demo.engineer1`, `demo.demoa.admin`, `demo.demoa.user1`, ...),
  houses with `Demo`. Their addresses end in `demo.invalid`: no e-mail and no SMS is ever sent to them.
- The demo manager and engineers are working XFL accounts: they see real tickets too. Next to existing tickets
  the page warns about this and wants a tick before it loads.
- **Remove demo data** deletes exactly what was loaded, plus the tickets demo accounts raised meanwhile. Real
  tickets given to a demo engineer become unassigned. A demo account or house that was changed into a real one
  (renamed, real e-mail address, real accounts or tickets inside) is kept, and the page says so.

## 9. Where the system stands

**Working** (checked in the test runs, see FIXES.md): everything described above.

**Not there yet**

| Gap | Effect |
|---|---|
| Product, support type and category are set when the ticket is raised and cannot be changed afterwards | a ticket raised under the wrong product keeps it; the report then counts it there |
| The arrangement of the dashboard is kept in the browser, not in the account | on another computer the dashboard starts in its standard arrangement |
| Support is reached through the web form only | no ticket by e-mail, no reply by e-mail, no chat |
| No knowledge base | every question becomes a ticket; nothing suggests an answer while a ticket is typed |
| No rules (auto-assign, auto-close after Deployed, reminders on Pending) | a manager assigns by hand; old Deployed tickets stay open until somebody closes them |
| One escalation step for a missed target (the people who assign tickets) | no second step to the platform admin after a further delay |
| No "send the token again" on the activation page | when the mail is lost, an administrator activates |
| Lists and reports load all visible tickets and filter in memory | fine for thousands of tickets, slow for hundreds of thousands |
| Two people saving the same ticket at the same moment: the last one wins for title, details and comments | rare; status and engineer are protected (section 3) |
| Notifications are pushed from the memory of one process | run one instance of the application; a web farm would need a shared channel |
| The text of notifications is fixed (English) | which event, which channel and who gets it are settings; the wording is not |

**Security and operations**

Built in since version 3.2: passwords stored with PBKDF2 (210,000 rounds; older ones are converted at the owner's next
sign-in), the limits on wrong attempts above, a content security policy and the other browser protection headers on
every page, upload limits with a check of the file content, and "choose your own password first" for passwords an
administrator or the settings file supplied. **System > System health** shows the state of each.

Still to do by hand or later:

| Item | Action |
|---|---|
| The Gmail app password is in `appsettings.json` and in the git history | revoke it; enter the new one under Notification settings (stored encrypted in the database) and remove it from the file |
| Sign-in is user name and password only | add a second factor (code by e-mail or authenticator app) before institutions ask for it |
| Limits per network address are kept in memory | fine for one instance; they start again after a restart |
| The folder `App_Data/keys` holds the keys for sign-in cookies and stored secrets | the account the site runs under must be able to write there; keep it out of version control and out of "delete extra files" deployments. After a move to another server, enter the mail password and SMS key again |
| Start SQL Server before the application | database updates are applied at start-up only; if the database was not reachable then, restart the application |
| .NET 6 is out of support; MailKit 4.3.0 has a published advisory | move to .NET 10 (.NET 8 support ends in November 2026) and update MailKit |
| Uploaded files are in the repository (`wwwroot/Uplods`) | remove them from git, keep the folder |
| `Controllers/UserController.cs` is dead code (its API class is never registered) | delete it |
| No automated tests in the repository | the checks so far were run from outside |
