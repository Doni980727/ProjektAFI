using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace ProjektAFI.Hubs
{
    public class GameHub : Hub
    {
        private readonly IHttpClientFactory _httpClientFactory;
       

        private static ConcurrentDictionary<string, List<string>> _lobbies = new();
        private static ConcurrentDictionary<string, string> _connectionIdToPlayerName = new();
        private static Dictionary<string, DateTime> _lobbyStartTimes = new();
        private static Dictionary<string, string> _lobbyWords = new();
        private static Dictionary<string, (string Drawer, string Guesser)> _lobbyRoles = new();
        private static Dictionary<string, int> _playerScores = new(); // namn → poäng


        public GameHub(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task JoinLobby(string lobbyId, string playerName)
        {
            _connectionIdToPlayerName[Context.ConnectionId] = playerName;

            _lobbies.AddOrUpdate(lobbyId,
                new List<string> { playerName },
                (key, existingPlayers) =>
                {
                    if (!existingPlayers.Contains(playerName))
                        existingPlayers.Add(playerName);
                    return existingPlayers;
                });

            await Groups.AddToGroupAsync(Context.ConnectionId, lobbyId);
            await Clients.Group(lobbyId).SendAsync("UpdatePlayerList", _lobbies[lobbyId]);

            if (_lobbies.TryGetValue(lobbyId, out var players) && players.Count == 2)
            {
                await Clients.Group(lobbyId).SendAsync("StartTimer", 30); // Starta 30 sekunder
            }

        }
        public async Task SendGuess(string lobbyId, string playerName, string guess)
        {
            await Clients.Group(lobbyId).SendAsync("ReceiveChatMessage", playerName, guess);

            if (!_lobbyWords.TryGetValue(lobbyId, out var correctWord))
                return;

            if (!string.Equals(guess, correctWord, StringComparison.OrdinalIgnoreCase))
                return;

            var timeTaken = (DateTime.UtcNow - _lobbyStartTimes[lobbyId]).TotalSeconds;
            var maxTime = 30.0;
            var guesserScore = Math.Max(0, (int)((maxTime - timeTaken) * 10));
            var drawerScore = Math.Max(0, (int)(timeTaken * 5));

            if (_lobbyRoles.TryGetValue(lobbyId, out var roles))
            {
                var drawer = roles.Drawer;
                var guesser = roles.Guesser;

                _playerScores[drawer] = _playerScores.GetValueOrDefault(drawer, 0) + drawerScore;
                _playerScores[guesser] = _playerScores.GetValueOrDefault(guesser, 0) + guesserScore;

                await Clients.Group(lobbyId).SendAsync("CorrectGuess", guesser, correctWord, guesserScore, drawerScore);
                await Clients.Group(lobbyId).SendAsync("UpdateScores", _playerScores);

                // 🔄 Växla roller
                _lobbyRoles[lobbyId] = (guesser, drawer);

                await Task.Delay(2000); // kort paus

                // 🧽 Rensa canvasen för ny runda
                await Clients.Group(lobbyId).SendAsync("ReceiveClear");

                // 🧠 Skicka nya roller till spelarna
                foreach (var connection in _connectionIdToPlayerName)
                {
                    if (_lobbies[lobbyId].Contains(connection.Value))
                    {
                        var newRole = connection.Value == _lobbyRoles[lobbyId].Drawer ? "Ritare" : "Gissare";
                        await Clients.Client(connection.Key).SendAsync("SetRole", newRole);
                    }
                }

                // 🆕 Skicka nytt ord till ritare
                await RequestWord(lobbyId);
            }
        }



        public async Task RequestWord(string lobbyId)
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync("https://localhost:7179/words/random");

            if (response.IsSuccessStatusCode)
            {
                var word = await response.Content.ReadAsStringAsync();

                _lobbyWords[lobbyId] = word;
                _lobbyStartTimes[lobbyId] = DateTime.UtcNow;

                if (_lobbyRoles.TryGetValue(lobbyId, out var roles))
                {
                    var drawerConnection = _connectionIdToPlayerName
                        .FirstOrDefault(kvp => kvp.Value == roles.Drawer).Key;

                    if (drawerConnection != null)
                    {
                        await Clients.Client(drawerConnection).SendAsync("ReceiveWord", word);
                    }
                }

                await Clients.Group(lobbyId).SendAsync("StartTimer", 30);
            }
            else
            {
                await Clients.Group(lobbyId).SendAsync("ReceiveWord", "Kunde inte hämta ord");
            }
        }




        public async Task StartGame(string lobbyId)
{
    if (_lobbies.TryGetValue(lobbyId, out var players) && players.Count == 2)
    {
        var drawer = players[0];
        var guesser = players[1];

        _lobbyRoles[lobbyId] = (drawer, guesser);

        foreach (var connection in _connectionIdToPlayerName)
        {
            if (players.Contains(connection.Value))
            {
                var role = connection.Value == drawer ? "Ritare" : "Gissare";

                await Clients.Client(connection.Key).SendAsync("NavigateToGame", new
                {
                    Role = role,
                    LobbyId = lobbyId,
                    PlayerName = connection.Value
                });
            }
        }
    }
}



        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var lobby in _lobbies)
            {
                if (lobby.Value.Remove(_connectionIdToPlayerName[Context.ConnectionId]))
                {
                    await Clients.Group(lobby.Key).SendAsync("UpdatePlayerList", lobby.Value);
                    break;
                }
            }

            _connectionIdToPlayerName.TryRemove(Context.ConnectionId, out _);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendDrawData(string lobbyId, float startX, float startY, float x, float y)
        {
            await Clients.OthersInGroup(lobbyId).SendAsync("ReceiveDrawData", startX, startY, x, y);
        }

        public async Task SendClear(string lobbyId)
        {
            await Clients.OthersInGroup(lobbyId).SendAsync("ReceiveClear");
        }
    }
}
