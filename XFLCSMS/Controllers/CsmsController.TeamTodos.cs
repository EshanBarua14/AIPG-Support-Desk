using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Common;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;
using XFLCSMS.Models.Todos;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // Editing one to-do (every role), and the to-dos of all users with the report over them
    // (every role that holds Permission.TeamTodos).
    public abstract partial class CsmsController
    {
        // ---- one to-do ----------------------------------------------------------------------------
        // Everybody edits his own to-dos; a role with "team to-dos" also those of others.

        private bool MayEdit(Todo todo)
        {
            return todo.UserId == CurrentUser!.Id || Can(Permission.TeamTodos);
        }

        [HttpGet]
        public async Task<IActionResult> GetTodo(int id)
        {
            try
            {
                var todo = await Db.Todos.FindAsync(id);
                if (todo == null || !MayEdit(todo))
                {
                    return NotFound();
                }

                ViewBag.StatusOptions = new List<SelectListItem>
                {
                    new SelectListItem("In progress", "In progress"),
                    new SelectListItem("Done", "Done"),
                    new SelectListItem("Canceled", "Canceled")
                };
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
                var todo = await Db.Todos.FindAsync(model.Id);
                if (todo == null || !MayEdit(todo))
                {
                    return NotFound();
                }

                if (!string.IsNullOrWhiteSpace(model.Todoname))
                {
                    todo.Todoname = model.Todoname;
                }

                if (!string.IsNullOrWhiteSpace(model.Status) && new[] { "In progress", "Done", "Canceled" }.Contains(model.Status))
                {
                    todo.Status = model.Status;
                }

                await Db.SaveChangesAsync();
                // back to the list the to-do was opened from
                return RedirectToAction(todo.UserId == CurrentUser!.Id ? "ViewTodo" : "ViewAllTodo");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.TeamTodos)]
        [HttpGet]
        public async Task<IActionResult> ViewAllTodo(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            try
            {
                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;
                if (page <= 0) { page = 1; }

                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;
                // Fetch all Todo items from the database
                //var todos = await Db.Todos.Where(item => item.UserId == LogSesson.Id && item.Status == "In progress").ToListAsync();
                // the team list shows who each to-do belongs to
                var todos = await Db.Todos
                            .Include(item => item.User)
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


        [RequirePermission(Permission.TeamTodos)]
        [HttpGet]
        public async Task<IActionResult> AllTodoReports()
        {
            try
            {


                var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
                User LogSesson = JsonConvert.DeserializeObject<User>(jsonStringFromSession);
                ViewBag.Profile = LogSesson;

                var supportEngineer = Db.Users.ToList();
                var BrocarageHouse = Db.Brokerages.ToList();

                var reportView = new TodoReportView
                {
                    Todos = null,
                    ListofEmployee = supportEngineer,
                    search = null,
                    TodoHederInfo = null,
                    brokerages = BrocarageHouse,

                };


                return View(reportView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }


        [RequirePermission(Permission.TeamTodos)]
        [HttpGet]
        public async Task<IActionResult> AllTodoSearch(TodoReportView reportView)
        {
            try { 
            var jsonStringFromSession = HttpContext.Session.GetString(SessionKey);
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

            var searchResults = await Db.Todos.Include(item => item.User).ToListAsync();

            // both ends of the range are inclusive, and either end may be left empty
            if (reportView.search.FromDate != null)
                searchResults = searchResults.Where(x => x.CreatedOn.Date >= reportView.search.FromDate.Value.Date).ToList();
            if (reportView.search.ToDate != null)
                searchResults = searchResults.Where(x => x.CreatedOn.Date <= reportView.search.ToDate.Value.Date).ToList();

            if (!String.IsNullOrEmpty(reportView.search.Status))
                searchResults = searchResults.Where(x => x.Status != null && x.Status.Contains(reportView.search.Status)).ToList();

            if (reportView.search.BrocarageHouseName.HasValue)
                searchResults = searchResults.Where(x => x.BrokerageId == reportView.search.BrocarageHouseName).ToList();
            if (reportView.search.EmployeeName.HasValue)
                searchResults = searchResults.Where(x => x.UserId==reportView.search.EmployeeName).ToList();



            string Brocaragename = reportView.search.BrocarageHouseName !=null ?HouseName(reportView.search.BrocarageHouseName) :"All";
            string EmployeeNamee = (reportView.search.EmployeeName != null) ? PersonName(reportView.search.EmployeeName) : "All";




            int TotalTodo = searchResults.Count();
            int TotalInprogressTodo = searchResults.Where(x => x.Status == "In progress").Count();
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
                ReportName = Rbac.Label(MyRole)


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
    }
}
