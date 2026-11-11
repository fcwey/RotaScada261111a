using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace WebAPI.Hubs
{
    [Authorize] // Sadece giriş yapmış kullanıcılar bağlanabilir
    public class AlarmHub : Hub
    {
        // Bağlantıyı alarm abonelerinin grubuna ekler.
        public async Task SubscribeToAlarms()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "AlarmSubscribers");
        }
    }
}