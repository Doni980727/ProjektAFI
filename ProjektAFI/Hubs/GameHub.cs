using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace ProjektAFI.Hubs
{
    public class GameHub : Hub
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private static ConcurrentDictionary<string, Dictionary<string, int>> _lobbyDrawCounts = new();

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

                await Task.Delay(2000); // kort paus

                await BytRollerOchStartaNyRunda(lobbyId);
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

        public async Task TimeUp(string lobbyId)
        {
            if (_lobbyRoles.TryGetValue(lobbyId, out var roles))
            {
                var drawer = roles.Drawer;
                var guesser = roles.Guesser;
                var word = _lobbyWords.ContainsKey(lobbyId) ? _lobbyWords[lobbyId] : "(okänt)";

                // 0 poäng till båda
                _playerScores[drawer] = _playerScores.GetValueOrDefault(drawer, 0);
                _playerScores[guesser] = _playerScores.GetValueOrDefault(guesser, 0);

                await Clients.Group(lobbyId).SendAsync("CorrectGuess", "(ingen)", word, 0, 0);
                await Clients.Group(lobbyId).SendAsync("UpdateScores", _playerScores);

                // Byt roller
                _lobbyRoles[lobbyId] = (guesser, drawer);

                // Rensa canvas
                await Clients.Group(lobbyId).SendAsync("ReceiveClear");

                // Starta ny runda
                await Task.Delay(2000); // liten paus

                foreach (var connection in _connectionIdToPlayerName)
                {
                    if (_lobbies[lobbyId].Contains(connection.Value))
                    {
                        var newRole = connection.Value == _lobbyRoles[lobbyId].Drawer ? "Ritare" : "Gissare";
                        await Clients.Client(connection.Key).SendAsync("SetRole", newRole);
                    }
                }

                await RequestWord(lobbyId);
            }
        }


        private async Task BytRollerOchStartaNyRunda(string lobbyId)
        {
            if (_lobbyRoles.TryGetValue(lobbyId, out var roles) &&
                _lobbies.TryGetValue(lobbyId, out var players) &&
                players.Count == 2)
            {
                // Öka räkningen för den som just ritade
                if (_lobbyDrawCounts.TryGetValue(lobbyId, out var drawCounts))
                {
                    if (drawCounts.ContainsKey(roles.Drawer))
                        drawCounts[roles.Drawer]++;
                }

                // Kontrollera om båda har ritat 5 gånger -----------------------------------------------------------------------HÄR ÄNDRAR DU ANTALET OMGÅNGAR----------------------------------------
                var done = _lobbyDrawCounts[lobbyId].Values.All(count => count >= 3);
                if (done)
                {
                    // Skicka resultat
                    await Clients.Group(lobbyId).SendAsync("GameOver", _playerScores);

                    // Du kan även navigera till en resultatsida via klienten
                    return;
                }

                // Växla roller
                _lobbyRoles[lobbyId] = (roles.Guesser, roles.Drawer);

                foreach (var connection in _connectionIdToPlayerName)
                {
                    if (players.Contains(connection.Value))
                    {
                        var newRole = connection.Value == roles.Guesser ? "Ritare" : "Gissare";
                        await Clients.Client(connection.Key).SendAsync("SetRole", newRole);
                    }
                }

                // Rensa canvas
                await Clients.Group(lobbyId).SendAsync("ReceiveClear");

                await Task.Delay(500);
                await RequestWord(lobbyId);
            }
        }



        public async Task StartGame(string lobbyId)
        {
            if (_lobbies.TryGetValue(lobbyId, out var players) && players.Count == 2)
            {
                var drawer = players[0];
                var guesser = players[1];

                _lobbyRoles[lobbyId] = (drawer, guesser);

                _lobbyDrawCounts[lobbyId] = new Dictionary<string, int>
                {
                    { drawer, 0 },
                    { guesser, 0 }
                };

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
