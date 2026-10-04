using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using System.Security.Cryptography;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Common;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Todos;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    public class SupportEngineerController : CsmsController
    {
        private readonly DataContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        protected override string SessionKey => "SEData";

        public SupportEngineerController(DataContext context, IWebHostEnvironment webHostEnvironment, TicketService tickets)
            : base(context, tickets)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }
        public int EditIssueId = 0;


        public async Task<IActionResult> Dashbord()
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                // Total Ticket
                int TotalInTicket = _context.Issues.Count();
                int TotalInclosed = _context.Issues.Count(i => i.IStatus == "Close");
                int TotalInQueue = TotalInTicket - TotalInclosed;
                //Today
                int TodayTotal = _context.Issues.Count(i => i.TDate.Date == DateTime.Now.Date);
                int TodayClose = _context.Issues.Count(i => (i.TDate.Date == DateTime.Now.Date) && (i.IStatus == "Close"));
                int TodayQueue = TodayTotal - TodayClose;
                //Week
                DateTime lastWeekStartDate = DateTime.Now.Date.AddDays(-7);
                int LastWeekTotal = _context.Issues.Count(i => i.TDate.Date >= lastWeekStartDate && i.TDate.Date <= DateTime.Now.Date);
                int LastWeekClosed = _context.Issues.Count(i => i.TDate.Date >= lastWeekStartDate && i.TDate.Date <= DateTime.Now.Date && i.IStatus == "Close");
                int lastWeekQueue = LastWeekTotal - LastWeekClosed;

                // Last month's total issues
                DateTime lastMonthStartDate = DateTime.Now.Date.AddMonths(-1);
                int LastMonthTotal = _context.Issues.Count(i => i.TDate.Date >= lastMonthStartDate && i.TDate.Date <= DateTime.Now.Date);
                int LastMonthClosed = _context.Issues.Count(i => i.TDate.Date >= lastMonthStartDate && i.TDate.Date <= DateTime.Now.Date && i.IStatus == "Close");
                int LastMonthQueue = LastMonthTotal - LastMonthClosed;

                // Last year's total issues
                DateTime lastYearStartDate = DateTime.Now.Date.AddYears(-1);
                int LastYearTotal = _context.Issues.Count(i => i.TDate.Date >= lastYearStartDate && i.TDate.Date <= DateTime.Now.Date);
                int LastYearClosed = _context.Issues.Count(i => i.TDate.Date >= lastYearStartDate && i.TDate.Date <= DateTime.Now.Date && i.IStatus == "Close");
                int LastYearQueue = LastYearTotal - LastYearClosed;

                ///for Brocarage Chart
                ///
                var brokerages = _context.Brokerages.Include(b => b.Issues).ToList();

                var chartData = new
                {
                    labels = brokerages.Select(b => b.BrokerageHouseName),
                    datasets = new[]
                    {
                new
                {
                    label = "Total Tickets",
                    data = brokerages.Select(b => b.Issues.Count),
                    backgroundColor = "rgba(75, 192, 192, 0.2)",
                    borderColor = "rgba(75, 192, 192, 1)",
                    borderWidth = 1
                },
                new
                {
                    label = "Close Tickets",
                    data = brokerages.Select(b => b.Issues.Count(I=>I.IStatus=="Close")),
                    backgroundColor = "rgba(0, 255, 0, 0.2)",
                    borderColor = "rgba(0, 255, 0, 1)",
                    borderWidth = 1
                },
                new
                {
                    label = "Inqueue Tickets",
                    data = brokerages.Select(b => b.Issues.Count(I=>I.IStatus!="Close")),
                    backgroundColor = "rgba(75, 192, 192, 0.2)",
                    borderColor = "rgba(255, 206, 86, 1)",
                    borderWidth = 1
                },
                // Repeat the structure for "In Queue" and "Closed Tickets" using relevant data
            }
                };




                var TicketCount = new Dashboard
                {
                    TotalTicket = TotalInTicket,
                    TotalClosed = TotalInclosed,
                    TotalQueue = TotalInQueue,
                    TodayTotalTicket = TodayTotal,
                    TodayTotalClosed = TodayClose,
                    TodayTotalQueue = TodayQueue,
                    WeeklyTotalTicket = LastWeekTotal,
                    WeeklyTotalClosed = LastWeekClosed,
                    WeeklyTotalQueue = lastWeekQueue,
                    MonthlyTotalTicket = LastMonthTotal,
                    MonthlyTotalClosed = LastMonthClosed,
                    MonthlyTotalQueue = LastMonthQueue,
                    YearlyTotalTicket = LastYearTotal,
                    YearlyTotalClosed = LastYearClosed,
                    YearlyTotalQueue = LastYearQueue,
                    ChartData = chartData


                };
                return View(TicketCount);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        public async Task<IActionResult> AllTicketList(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;

                var TicketList = _context.Issues.OrderByDescending(i => i.IssueId).ToList();

                if (!string.IsNullOrEmpty(searchString))
                {


                    TicketList = TicketList.Where(e =>
                                            e.TNumber.ToLower().Contains(searchString.ToLower()) ||
                                            e.ITitle?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.Priority.ToLower().Contains(searchString.ToLower()) || // Convert Priority to string for searching
                                            e.IStatus?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.AssignOn?.ToString().ToLower().Contains(searchString.ToLower()) == true ||
                                            e.TDate.ToString().ToLower().Contains(searchString.ToLower()) ||
                                            e.AssignBy?.ToLower().Contains(searchString.ToLower()) == true).ToList();

                }

                switch (sortField)
                {
                    case "TNumber":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() :
                            TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() :
                            TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() :
                            TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() :
                            TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() :
                            TicketList.OrderByDescending(item => item.IStatus).ToList();
                        break;

                    // Add cases for other fields as needed
                    default:
                        // Default sorting if no valid sort field provided
                        TicketList = TicketList.OrderByDescending(item => item.IssueId).ToList();
                        break;
                }

                int tot_records = TicketList.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;


                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<IssueTable> IssueList = TicketList.Skip(skip_records).Take(take_records).ToList();
                return View(IssueList);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> AssignedTicketList(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;

                List<IssueTable> TicketList = _context.Issues.Where(i => i.AssignBy == LogSesson.FullName &&(i.IStatus !="Close"))
                    .OrderByDescending(i => i.IssueId).ToList();

                if (!string.IsNullOrEmpty(searchString))
                {


                    TicketList = TicketList.Where(e =>
                                            e.TNumber.ToLower().Contains(searchString.ToLower()) ||
                                            e.ITitle?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.Priority.ToLower().Contains(searchString.ToLower()) || // Convert Priority to string for searching
                                            e.IStatus?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.AssignOn?.ToString().ToLower().Contains(searchString.ToLower()) == true ||
                                            e.TDate.ToString().ToLower().Contains(searchString.ToLower()) ||
                                            e.AssignBy?.ToLower().Contains(searchString.ToLower()) == true).ToList();

                }

                switch (sortField)
                {
                    case "TNumber":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() :
                            TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() :
                            TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() :
                            TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() :
                            TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() :
                            TicketList.OrderByDescending(item => item.IStatus).ToList();
                        break;

                    // Add cases for other fields as needed
                    default:
                        // Default sorting if no valid sort field provided
                        TicketList = TicketList.OrderByDescending(item => item.IssueId).ToList();
                        break;
                }

                int tot_records = TicketList.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;


                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<IssueTable> IssueList = TicketList.Skip(skip_records).Take(take_records).ToList();
                return View(IssueList);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
        public async Task<IActionResult> UnassignedTicketList(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                List<IssueTable> TicketList = _context.Issues.OrderByDescending(i => i.IssueId)
                    .Where(i => i.AssignOn == null && i.AssignBy == null).ToList();
                if (!string.IsNullOrEmpty(searchString))
                {


                    TicketList = TicketList.Where(e =>
                                            e.TNumber.ToLower().Contains(searchString.ToLower()) ||
                                            e.ITitle?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.Priority.ToLower().Contains(searchString.ToLower()) || // Convert Priority to string for searching
                                            e.IStatus?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.AssignOn?.ToString().ToLower().Contains(searchString.ToLower()) == true ||
                                            e.TDate.ToString().ToLower().Contains(searchString.ToLower()) ||
                                            e.AssignBy?.ToLower().Contains(searchString.ToLower()) == true).ToList();

                }

                switch (sortField)
                {
                    case "TNumber":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() :
                            TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() :
                            TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() :
                            TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() :
                            TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() :
                            TicketList.OrderByDescending(item => item.IStatus).ToList();
                        break;

                    // Add cases for other fields as needed
                    default:
                        // Default sorting if no valid sort field provided
                        TicketList = TicketList.OrderByDescending(item => item.IssueId).ToList();
                        break;
                }

                int tot_records = TicketList.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;


                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<IssueTable> IssueList = TicketList.Skip(skip_records).Take(take_records).ToList();
                return View(IssueList);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        public async Task<IActionResult> ClosedTicketList(int page, int rowperpage, string? searchString = null, string?
            sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                List<IssueTable> TicketList = _context.Issues.OrderByDescending(i => i.IssueId).Where(i => i.IStatus == "Close").ToList();
                if (!string.IsNullOrEmpty(searchString))
                {


                    TicketList = TicketList.Where(e =>
                                            e.TNumber.ToLower().Contains(searchString.ToLower()) ||
                                            e.ITitle?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.Priority.ToLower().Contains(searchString.ToLower()) || // Convert Priority to string for searching
                                            e.IStatus?.ToLower().Contains(searchString.ToLower()) == true ||
                                            e.AssignOn?.ToString().ToLower().Contains(searchString.ToLower()) == true ||
                                            e.TDate.ToString().ToLower().Contains(searchString.ToLower()) ||
                                            e.AssignBy?.ToLower().Contains(searchString.ToLower()) == true).ToList();

                }

                switch (sortField)
                {
                    case "TNumber":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.TNumber).ToList() :
                            TicketList.OrderByDescending(item => item.TNumber).ToList();
                        break;
                    case "Tickets":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.ITitle).ToList() :
                            TicketList.OrderByDescending(item => item.ITitle).ToList();
                        break;
                    case "Approval Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.AssignBy).ToList() :
                            TicketList.OrderByDescending(item => item.AssignBy).ToList();
                        break;
                    case "Priority":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.Priority).ToList() :
                            TicketList.OrderByDescending(item => item.Priority).ToList();
                        break;
                    case "Status":
                        TicketList = sortAscending ? TicketList.OrderBy(item => item.IStatus).ToList() :
                            TicketList.OrderByDescending(item => item.IStatus).ToList();
                        break;

                    // Add cases for other fields as needed
                    default:
                        // Default sorting if no valid sort field provided
                        TicketList = TicketList.OrderByDescending(item => item.IssueId).ToList();
                        break;
                }

                int tot_records = TicketList.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;


                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<IssueTable> IssueList = TicketList.Skip(skip_records).Take(take_records).ToList();
                return View(IssueList);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }
        public async Task<IActionResult> TicketView(int? id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var issueWithAttachments = _context.Issues
                    .Include(i => i.attachment)  // Include attachments in the query
                    .FirstOrDefault(i => i.IssueId == id);

                if (issueWithAttachments == null || !CanAccessIssue(issueWithAttachments))
                {
                    return NotFound();
                }

                var makerView = new MakerView
                {
                    IssueId = issueWithAttachments.IssueId,
                    CreatedOn = issueWithAttachments.TDate,
                    CreatedBy = UserName(issueWithAttachments.UserId),
                    AssgnOn = issueWithAttachments.AssignOn,
                    AssgnBy = issueWithAttachments.AssignBy,
                    SupportType = SupportTypeName(issueWithAttachments.SupportTypeId),
                    SupportCatagory = SupportCatagoryName(issueWithAttachments.SupportCatagoryId),
                    SupportSubCatagory = SupportSubCatagoryName(issueWithAttachments.SupportSubCatagoryId),
                    AffectedSection = AffectedSectionName(issueWithAttachments.AffectedSectionId),
                    TicketDetails = issueWithAttachments.Details,
                    Command = issueWithAttachments.Comments,
                    TicketStatus = issueWithAttachments.IStatus,
                    IStatus = issueWithAttachments.IStatus,
                    Priority = issueWithAttachments.Priority,
                    Attachments = issueWithAttachments.attachment,
                    ApproveOn = issueWithAttachments.ApproveOn,
                    ApproveBy = issueWithAttachments.ApproveBy,
                    IssueTitle = issueWithAttachments.ITitle


                };

                return View(makerView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> EditTicket(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var issueWithAttachments = _context.Issues
                    .Include(i => i.attachment)  // Include attachments in the query
                    .FirstOrDefault(i => i.IssueId == id);
                var SupportEng = _context.Users.Where(i => i.Department == "Support Engineer").ToList();

                if (issueWithAttachments == null || !CanAccessIssue(issueWithAttachments))
                {
                    return NotFound();
                }
                EditIssueId = issueWithAttachments.IssueId;

                var EditView = new MakerView
                {
                    IssueId = issueWithAttachments.IssueId,
                    CreatedOn = issueWithAttachments.TDate,
                    CreatedBy = UserName(issueWithAttachments.UserId),
                    AssgnOn = issueWithAttachments.AssignOn,
                    AssgnBy = issueWithAttachments.AssignBy,
                    SupportType = SupportTypeName(issueWithAttachments.SupportTypeId),
                    SupportCatagory = SupportCatagoryName(issueWithAttachments.SupportCatagoryId),
                    SupportSubCatagory = SupportSubCatagoryName(issueWithAttachments.SupportSubCatagoryId),
                    AffectedSection = AffectedSectionName(issueWithAttachments.AffectedSectionId),
                    TicketDetails = issueWithAttachments.Details,
                    Command = issueWithAttachments.Comments,
                    TicketStatus = issueWithAttachments.IStatus,
                    IStatus = issueWithAttachments.IStatus,
                    Attachments = issueWithAttachments.attachment,
                    IssueTitle = issueWithAttachments.ITitle,
                    Priority = issueWithAttachments.Priority,
                    SupportEngineers = SupportEng,
                    ApproveOn = issueWithAttachments.ApproveOn,
                    ApproveBy = issueWithAttachments.ApproveBy,

                };
                return View(EditView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTicketttt(MakerView makerView, List<IFormFile> files)
        {
            try
            {
                var editor = CurrentUser!;
                var issue = await _context.Issues.FirstOrDefaultAsync(a => a.IssueId == makerView.IssueId);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound("Edit is not done");
                }

                Tickets.ApplyStaffEdit(issue, makerView, editor, canApprove: true);
                await _context.SaveChangesAsync();

                var rejected = await Tickets.SaveAttachmentsAsync(issue.IssueId, files);
                if (rejected.Count > 0)
                {
                    TempData["ErrorMessage"] = "The ticket was saved, but these files were not attached (file type not allowed): "
                        + string.Join(", ", rejected);
                }

                return RedirectToAction("AllTicketList", "SupportEngineer");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> IssueRaiseFrom()
        {
            try
            {
                // Retrieve the JSON string from the session and deserialize it
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var brocarage = await _context.Brokerages.ToListAsync();
                string[] acronymsArray = new string[brocarage.Count];
                //Dictionary<string, int> nameCount = new Dictionary<string, int>();
                int i = 0;
                foreach (var item in brocarage)
                {
                    string acronyme = item.BrokerageHouseAcronym;
                    acronymsArray[i] = acronyme;
                    i++;

                }

                var issueLoginInfo = new IssueLoginInfo
                {
                    UserId = LogSesson.Id,
                    BrocarageHouseName = CreateHouseName(LogSesson.BrokerageHouseName),
                    BranchName = CreateBranchName(LogSesson.Branch),
                    TicketID = GenerateTicketId(LogSesson.BrokerageHouseName)
                };

                var viewModel = new IssueViewModel
                {
                    SupportTypes = _context.SupportTypes.ToList(),
                    SupportCatagories = _context.SupportCatagories.ToList(),
                    SupportSubCatagories = _context.SupportSubCatagories.ToList(),
                    AffectedSections = _context.AffectedSectionss.ToList(),
                    Brokerages = _context.Brokerages.ToList(),
                    Branchhs = _context.Branchhs.ToList(),
                    LoginInfo = issueLoginInfo

                };
                return View(viewModel);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssueRaiseFrom(IssueViewModel issueViewModel, List<IFormFile> files)
        {
            try
            {
                // Owner, brokerage house, ticket number and date are decided on the server (see TicketService).
                var result = await Tickets.CreateAsync(CurrentUser!, issueViewModel?.issueFrom, files);
                if (result.Issue == null)
                {
                    TempData["ErrorMessage"] = result.Error;
                    return RedirectToAction("IssueRaiseFrom");
                }

                if (result.RejectedFiles.Count > 0)
                {
                    TempData["ErrorMessage"] = "Ticket " + result.Issue.TNumber + " was created, but these files were not attached (file type not allowed): "
                        + string.Join(", ", result.RejectedFiles);
                }
                else
                {
                    TempData["SuccessMessage"] = "Ticket " + result.Issue.TNumber + " was created.";
                }

                return RedirectToAction("AllTicketList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private string UserName(int id)
        {
            var user = _context.Users.Where(i => i.Id == id).FirstOrDefault();
            return user?.FullName ?? string.Empty;
        }
        private string SupportTypeName(int? id)
        {
            var user = _context.SupportTypes.Where(i => i.SupportTypeId == id).FirstOrDefault();
            if (user == null)
            {
                return String.Empty;
            }
            return user.SType;
        }





        [HttpGet]
        public async Task<IActionResult> Reports()
        {
            try
            {


                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var supportEngineer = _context.Users.Where(i => i.Department == "Support Engineer").Select(user => user.FullName).ToList();
                var BrocarageHouse = _context.Brokerages.ToList();


                var reportview = new ReportView
                {
                    EmployeeNames = supportEngineer,
                    brocarages = BrocarageHouse,


                };

                return View(reportview);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [HttpGet]
        public async Task<IActionResult> Search(ReportView reportView)
        {
            // the report page can be opened/searched with an empty filter
            reportView.SESearch ??= new SESearch();
            var jsonStringFromSession = HttpContext.Session.GetString("SEData");
            User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
            if (reportView.SESearch.ToDate >= DateTime.Now)
            {
                reportView.SESearch.ToDate = DateTime.Now;
            }

            var searchResults = await _context.Issues.ToListAsync();
            searchResults = searchResults.Where(x => (x.ClosedBy == LogSesson.FullName)||(x.AssignBy==LogSesson.FullName)).ToList();

            if (reportView.SESearch.BrokerageId.HasValue)
                searchResults = searchResults.Where(x => x.BrokerageId == reportView.SESearch.BrokerageId).ToList();
            if (!String.IsNullOrEmpty(reportView.SESearch.Priority))
                searchResults = searchResults.Where(x => x.Priority.Contains(reportView.SESearch.Priority)).ToList();
                
            // both ends of the range are inclusive, and either end may be left empty
            if (reportView.SESearch.FromDate != null)
                searchResults = searchResults.Where(x => x.TDate.Date >= reportView.SESearch.FromDate.Value.Date).ToList();
            if (reportView.SESearch.ToDate != null)
                searchResults = searchResults.Where(x => x.TDate.Date <= reportView.SESearch.ToDate.Value.Date).ToList();

            string Brocaragename = (reportView.SESearch.BrokerageId > 0) ? GetBrocarageHouseName(reportView.SESearch.BrokerageId) : "All";
            string EmployeeNamee = LogSesson.FullName;



            int TotalTickett = searchResults.Count();
            int TotalOpenTickett = searchResults.Where(x => x.IStatus == "Open").Count();
            int TotalCloseTickett = searchResults.Where(x => x.IStatus == "Close").Count();
            int TotalInquee = TotalTickett - TotalOpenTickett - TotalCloseTickett;


            HeaderInfo SectionInfo = new HeaderInfo
            {
                BrokerageHouseName = Brocaragename,
                EmployeeName = EmployeeNamee,
                TotalTicket = TotalTickett,
                TotalOpenTicket = TotalOpenTickett,
                TotalCloseTicket = TotalCloseTickett,
                TotalInque = TotalInquee,
                ReportName = "Support Engineer"


            };

            // Populate the Issues property of the ReportView model with search results
            var reportViewWithSearchResults = new ReportView
            {
                Issues = searchResults,
                HeaderInfo = SectionInfo,

            };

            // Return the partial view with the populated reportView model
            return PartialView("_SearchResults", reportViewWithSearchResults);
        }


        [HttpGet]
        public async Task<IActionResult> ViewTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;

                var todos = await _context.Todos
                            .Where(item => item.UserId == LogSesson.Id && item.Status == "In progress")
                            .OrderByDescending(item => item.Id)
                            .ToListAsync();
                //List<Todo> todoss = todos;
                if (!string.IsNullOrEmpty(searchString))
                {
                    todos = todos.Where(todo =>
                                            todo.Todoname.ToLower().Contains(searchString.ToLower()) ||
                                            todo.Status?.ToLower().Contains(searchString.ToLower()) == true ||
                                            todo.CreatedOn.ToString().ToLower().Contains(searchString.ToLower())
                                     ).ToList();
                }

                switch (sortField)
                {
                    case "Task":
                        todos = sortAscending ? todos.OrderBy(item => item.Todoname).ToList() :
                            todos.OrderByDescending(item => item.Todoname).ToList();
                        break;
                    case "Status":
                        todos = sortAscending ? todos.OrderBy(item => item.Status).ToList() :
                            todos.OrderByDescending(item => item.Status).ToList();
                        break;
                    case "CreatedOn":
                        todos = sortAscending ? todos.OrderBy(item => item.CreatedOn).ToList() :
                            todos.OrderByDescending(item => item.CreatedOn).ToList();
                        break;
                    default:
                        // Default sorting if no valid sort field provided (sort by Id by default)
                        todos = todos.OrderByDescending(item => item.Id).ToList();
                        break;
                }


                int tot_records = todos.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;

                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<Todo> todoss = todos.Skip(skip_records).Take(take_records).OrderByDescending(item => item.Id).ToList();



                TodoviewModel todoviewModel = new TodoviewModel()
                {
                    Todos = todoss,
                    Todo = null,

                };

                // Return the view with the list of Todo items
                return View(todoviewModel);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }


        [HttpGet]
        public async Task<IActionResult> AllTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                // Fetch all Todo items from the database
                var todos = await _context.Todos.Where(item => item.UserId == LogSesson.Id).ToListAsync();
                //List<Todo> todoss = todos;

                if (!string.IsNullOrEmpty(searchString))
                {
                    todos = todos.Where(todo =>
                                            todo.Todoname.ToLower().Contains(searchString.ToLower()) ||
                                            todo.Status?.ToLower().Contains(searchString.ToLower()) == true ||
                                            todo.CreatedOn.ToString().ToLower().Contains(searchString.ToLower())
                                     ).ToList();
                }

                switch (sortField)
                {
                    case "Task":
                        todos = sortAscending ? todos.OrderBy(item => item.Todoname).ToList() :
                            todos.OrderByDescending(item => item.Todoname).ToList();
                        break;
                    case "Status":
                        todos = sortAscending ? todos.OrderBy(item => item.Status).ToList() :
                            todos.OrderByDescending(item => item.Status).ToList();
                        break;
                    case "CreatedOn":
                        todos = sortAscending ? todos.OrderBy(item => item.CreatedOn).ToList() :
                            todos.OrderByDescending(item => item.CreatedOn).ToList();
                        break;
                    default:
                        // Default sorting if no valid sort field provided (sort by Id by default)
                        todos = todos.OrderByDescending(item => item.Id).ToList();
                        break;
                }


                int tot_records = todos.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;

                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<Todo> todoss = todos.Skip(skip_records).Take(take_records).ToList();



                TodoviewModel todoviewModel = new TodoviewModel()
                {
                    Todos = todoss,
                    Todo = null,

                };

                // Return the view with the list of Todo items
                return View(todoviewModel);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        [HttpGet]
        public async Task<IActionResult> CompletedTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {

            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                // Fetch all Todo items from the database
                var todos = await _context.Todos.Where(item => item.UserId == LogSesson.Id && item.Status == "Done").ToListAsync();
                //List<Todo> todoss = todos;
                if (!string.IsNullOrEmpty(searchString))
                {
                    todos = todos.Where(todo =>
                                            todo.Todoname.ToLower().Contains(searchString.ToLower()) ||
                                            todo.Status?.ToLower().Contains(searchString.ToLower()) == true ||
                                            todo.CreatedOn.ToString().ToLower().Contains(searchString.ToLower())
                                     ).ToList();
                }

                switch (sortField)
                {
                    case "Task":
                        todos = sortAscending ? todos.OrderBy(item => item.Todoname).ToList() :
                            todos.OrderByDescending(item => item.Todoname).ToList();
                        break;
                    case "Status":
                        todos = sortAscending ? todos.OrderBy(item => item.Status).ToList() :
                            todos.OrderByDescending(item => item.Status).ToList();
                        break;
                    case "CreatedOn":
                        todos = sortAscending ? todos.OrderBy(item => item.CreatedOn).ToList() :
                            todos.OrderByDescending(item => item.CreatedOn).ToList();
                        break;
                    default:
                        // Default sorting if no valid sort field provided (sort by Id by default)
                        todos = todos.OrderByDescending(item => item.Id).ToList();
                        break;
                }


                int tot_records = todos.Count;
                int pagesize = rowperpage > 0 ? rowperpage : 10;
                int number_of_button = 4;

                Pager P = new Pager(tot_records, page, pagesize, number_of_button, searchString);
                ViewBag.pager = P;
                int skip_records = (Math.Max(P.CurrentPage, 1) - 1) * pagesize;
                int take_records = pagesize;
                List<Todo> todoss = todos.Skip(skip_records).Take(take_records).ToList();



                TodoviewModel todoviewModel = new TodoviewModel()
                {
                    Todos = todoss,
                    Todo = null,

                };

                // Return the view with the list of Todo items
                return View(todoviewModel);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }


        [HttpPost]
        public async Task<IActionResult> AddTodo(TodoviewModel newTodo)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (string.IsNullOrWhiteSpace(newTodo?.Todo?.Todoname))
                {
                    TempData["ErrorMessage"] = "Please enter a to-do before adding it.";
                    return RedirectToAction("ViewTodo");
                }

                Todo todo = new Todo()
                {
                    CreatedOn = DateTime.Now,
                    Todoname = newTodo.Todo.Todoname,
                    Status = "In progress",
                    UserId = LogSesson.Id,
                    BrokerageId = LogSesson.BrokerageHouseName,
                };

                await _context.Todos.AddAsync(todo);
                await _context.SaveChangesAsync();
                return RedirectToAction("ViewTodo");

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }

        }

        [HttpGet]
        public async Task<IActionResult> GetTodo(int id)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                var todo = await _context.Todos.FindAsync(id);
                if (todo == null || todo.UserId != LogSesson.Id)
                {
                    return NotFound();
                }
                var statusOptions = new List<SelectListItem>
                {
                    new SelectListItem("In progress", "In progress"),
                    new SelectListItem("Done", "Done"),
                    new SelectListItem("Canceled", "Canceled")
                };

                ViewBag.StatusOptions = statusOptions;

                return View(todo);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }

        [HttpPost]
        public async Task<IActionResult> Updatetodo(Todo model)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);

                var existingTodo = await _context.Todos.FindAsync(model.Id);
                if (existingTodo == null || existingTodo.UserId != LogSesson.Id)
                {
                    return NotFound();
                }

                if (!string.IsNullOrWhiteSpace(model.Todoname))
                {
                    existingTodo.Todoname = model.Todoname;
                }

                if (!string.IsNullOrWhiteSpace(model.Status))
                {
                    existingTodo.Status = model.Status;
                }

                _context.Todos.Update(existingTodo);
                await _context.SaveChangesAsync();

                return RedirectToAction("ViewTodo");

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }


        [HttpGet]
        public async Task<IActionResult> TodoReports()
        {
            try
            {


                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var reportView = new TodoReportView
                {
                    Todos = null,
                    ListofEmployee = null,
                    search = null,
                    TodoHederInfo = null,
                    brokerages = null,

                };


                return View(reportView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpGet]
        public async Task<IActionResult> TodoSearch(TodoReportView reportView)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString("SEData");
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                reportView.search ??= new XFLCSMS.Models.Todos.TodoSearch();
                if (reportView.search != null)
                {
                    // Check if FromDate is not null and is greater than or equal to the current date
                    if (reportView.search.FromDate.HasValue && reportView.search.FromDate >= DateTime.Now)
                    {
                        reportView.search.FromDate = DateTime.Now;
                    }

                    // Check if ToDate is not null and is greater than or equal to the current date
                    if (reportView.search.ToDate.HasValue && reportView.search.ToDate >= DateTime.Now)
                    {
                        reportView.search.ToDate = DateTime.Now;
                    }
                }

                var searchResults = _context.Todos
                    .Where(item => item.UserId == LogSesson.Id)
                    .OrderByDescending(item => item.Id)
                    .ToList();


                if (!String.IsNullOrEmpty(reportView.search.Status))
                    searchResults = searchResults.Where(x => x.Status != null && x.Status.Contains(reportView.search.Status)).ToList();

                // both ends of the range are inclusive, and either end may be left empty
                if (reportView.search.FromDate != null)
                    searchResults = searchResults.Where(x => x.CreatedOn.Date >= reportView.search.FromDate.Value.Date).ToList();
                if (reportView.search.ToDate != null)
                    searchResults = searchResults.Where(x => x.CreatedOn.Date <= reportView.search.ToDate.Value.Date).ToList();

                string Brocaragename = GetBrocarageHouseName(LogSesson.BrokerageHouseName);
                string EmployeeNamee = LogSesson.FullName;



                int TotalTodo = searchResults.Count();
                int TotalInprogressTodo = searchResults.Where(x => x.Status == "In Progress").Count();
                int TotalCompletedTodoo = searchResults.Where(x => x.Status == "Done").Count();
                int TotalCancledTodo = TotalTodo - TotalInprogressTodo - TotalCompletedTodoo;


                TodoHederInfo SectionInfo = new TodoHederInfo
                {
                    BrokerageHouseName = Brocaragename,
                    EmployeeName = EmployeeNamee,
                    TotalTodo = TotalTodo,
                    TotalInprogress = TotalInprogressTodo,
                    TotalCanseled = TotalCancledTodo,
                    TotalCompleteTodo = TotalCompletedTodoo,
                    ReportName = "Support Engineer"


                };

                // Populate the Issues property of the ReportView model with search results
                var reportViewWithSearchResults = new TodoReportView
                {
                    Todos = searchResults,
                    TodoHederInfo = SectionInfo,

                };

                // Return the partial view with the populated reportView model
                return PartialView("_todoSearchResult", reportViewWithSearchResults);

            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }


        }






        private string GetBrocarageHouseName(int? id)
        {
            var Brocarage = _context.Brokerages.ToList();
            foreach (var item in Brocarage)
            {
                if (item.BrokerageId == id)
                {
                    return item.BrokerageHouseName;
                }
            }
            return null;
        }
        private string SupportCatagoryName(int? id)
        {
            var user = _context.SupportCatagories.Where(i => i.SupportCatagoryId == id).FirstOrDefault();
            if (user == null)
            {
                return String.Empty;
            }

            return user.SCatagory;
        }

        private string SupportSubCatagoryName(int? id)
        {
            var user = _context.SupportSubCatagories.Where(i => i.SupportSubCatagoryId == id).FirstOrDefault();
            if (user == null)
            {
                return String.Empty;
            }
            return user.SubCatagory;
        }

        private string AffectedSectionName(int? id)
        {
            var user = _context.AffectedSectionss.Where(i => i.AffectedSectionId == id).FirstOrDefault();
            if (user == null)
            {
                return String.Empty;
            }
            return user.ASection;
        }
        private string CreateBranchName(int id)
        {
            var Brocaragessss = _context.Branchhs.ToList();
            foreach (var item in Brocaragessss)
            {
                if (item.BranchId == id)
                {
                    string BranchName = item.BranchName;
                    return BranchName;
                }

            }
            return String.Empty;

        }

        private string CreateHouseName(int id)
        {
            var Brocaragessss = _context.Brokerages.ToList();
            foreach (var item in Brocaragessss)
            {
                if (item.BrokerageId == id)
                {
                    string HouseName = item.BrokerageHouseName;
                    return HouseName;
                }

            }
            return String.Empty;

        }

        private int CreateBrocarageID(string brocarageName)
        {
            var Brocaragessss = _context.Brokerages.ToList();
            foreach (var item in Brocaragessss)
            {
                if (item.BrokerageHouseName == brocarageName)
                {
                    return item.BrokerageId;
                }

            }
            return 0;

        }

        private string GenerateTicketId(int id)
        {
            // Shown on the form as a preview only; the real number is taken when the ticket is saved.
            return Tickets.NextTicketNumber(id) ?? string.Empty;
        }



    }
}
