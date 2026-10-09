using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;

namespace XFLCSMS.Controllers
{
    /// <summary>
    /// Short addresses that work for every role: "/go/article/12", "/go/ticket/345". Every role has its own area
    /// (/Admin/..., /Maker/...), so a link written by a support engineer into a reply would not open for the
    /// person of the brokerage house who reads it. These addresses lead to the same page in the area of whoever
    /// is signed in; that page decides, as always, whether this person may see it. Nothing is shown from here.
    /// </summary>
    public class GoController : Controller
    {
        [HttpGet("/go/article/{id:int}")]
        public IActionResult Article(int id) => To("Article", id);

        [HttpGet("/go/ticket/{id:int}")]
        public IActionResult Ticket(int id) => To("TicketView", id);

        private IActionResult To(string action, int id)
        {
            foreach (var role in Rbac.AllRoles)
            {
                if (!string.IsNullOrEmpty(HttpContext.Session.GetString(Rbac.SessionKey(role))))
                {
                    return RedirectToAction(action, Rbac.Controller(role), new { id });
                }
            }

            return RedirectToAction("Login", "RegisterLogin");
        }
    }
}
