using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace ProjektAFI.Hubs
{
    public class GameHub : Hub
    {
        private readonly IHttpClientFactory _httpClientFactory;
       

        private static ConcurrentDictionary<string, List<string>> _lobbies = new();
        private static ConcurrentDictionary<string, string> _connectionIdToPlayerName = new();

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
            // Skicka gissningen till alla i lobbyn
            await Clients.Group(lobbyId).SendAsync("ReceiveChatMessage", playerName, guess);
        }
        public async Task RequestWord(string lobbyId)
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync("https://localhost:7179/words/random");

            if (response.IsSuccessStatusCode)
            {
                var word = await response.Content.ReadAsStringAsync();
                await Clients.Group(lobbyId).SendAsync("ReceiveWord", word);
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

                // Skicka ordet till "ritaren" (spelaren med rollen "Ritare")
                var drawer = players[0];
                var guesser = players[1];

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
