using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace XFLCSMS.Controllers
{
    /// <summary>
    /// Pages that belong to no role: the error page and the "not found" page.
    /// (The old scaffold pages of this controller - a second brokerage CRUD and a file upload test - are gone;
    /// the real ones live in AdminController.)
    /// </summary>
    [IgnoreAntiforgeryToken] // these pages are also shown for failed POST requests (re-executed with the original method)
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return RedirectToAction("Login", "RegisterLogin");
        }

        /// <summary>Unhandled exceptions in production end here (see Program.cs).</summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        /// <summary>Empty 404 / 403 / ... responses are replaced by this page (see Program.cs).</summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Status(int id)
        {
            ViewBag.StatusCode = id;
            if (id >= 400 && id <= 599)
            {
                Response.StatusCode = id; // also when the address is opened directly
            }

            return View("Status");
        }
    }
}
