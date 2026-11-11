namespace WebAPI.Hubs
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.SignalR;
    using System.Threading.Tasks;

    namespace WebAPI.Hubs
    {
        [Authorize] // Sadece giriş yapmış kullanıcılar bağlanabilir
        public class ScadaHub : Hub
        {
            // Bağlantıyı verilen fırının grubuna ekler.
            public async Task JoinFurnaceGroup(string furnaceId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, furnaceId);
            }

            // Bağlantıyı verilen fırının grubundan çıkarır.
            public async Task LeaveFurnaceGroup(string furnaceId)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, furnaceId);
            }
        }
    }
}