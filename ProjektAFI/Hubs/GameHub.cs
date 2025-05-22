using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace ProjektAFI.Hubs
{
    public class GameHub : Hub
    {
        private static ConcurrentDictionary<string, List<string>> _lobbies = new();
        private static ConcurrentDictionary<string, string> _connectionIdToPlayerName = new();

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
        }

        public async Task StartGame(string lobbyId)
        {
            if (_lobbies.TryGetValue(lobbyId, out var players) && players.Count == 2)
            {
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
