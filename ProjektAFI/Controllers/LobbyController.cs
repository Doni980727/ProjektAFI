using Microsoft.AspNetCore.Mvc;

namespace ProjektAFI.Controllers
{
    public class LobbyController : Controller
    {
        [HttpGet]
        public IActionResult HostLobby()
        {
            ViewBag.PageTitle = "Starta lobby";
            ViewBag.FormTitle = "Ange ditt namn för att skapa en lobby";
            ViewBag.FormAction = Url.Action("CreateLobby", "Lobby");
            ViewBag.SubmitText = "Skapa lobby";
            return View("EnterName");
        }

        [HttpGet]
        public IActionResult CreateLobby(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return RedirectToAction("HostLobby");

            var lobbyId = Guid.NewGuid().ToString();
            var invitationUrl = Url.Action("JoinLobby", "Lobby", new { lobbyId }, Request.Scheme);

            ViewBag.LobbyId = lobbyId;
            ViewBag.InvitationUrl = invitationUrl;
            ViewBag.PlayerName = playerName;
            return View("HostLobby");
        }

        public IActionResult JoinLobby(string lobbyId, string? playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                ViewBag.LobbyId = lobbyId;
                ViewBag.PageTitle = "Gå med i lobby";
                ViewBag.FormTitle = "Ange ditt namn för att gå med i lobbyn";
                ViewBag.FormAction = Url.Action("JoinLobby", "Lobby");
                ViewBag.SubmitText = "Gå med";
                return View("EnterName");
            }

            ViewBag.LobbyId = lobbyId;
            ViewBag.PlayerName = playerName;
            return View();
        }

        public IActionResult Game(string lobbyId, string playerName, string role)
        {
            ViewBag.LobbyId = lobbyId;
            ViewBag.PlayerName = playerName;
            ViewBag.Role = role;
            return View();
        }

    }
}
