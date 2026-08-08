using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

class Program
{
    static async Task Main()
    {
        var conn = new HubConnectionBuilder().WithUrl("http://localhost:5200/printhub").Build();
        await conn.StartAsync();
        Console.WriteLine("Connected.");
        
        try
        {
            string data = new string('A', 110000000); // larger than 100MB
            await conn.InvokeAsync("SendPrintJob", Environment.MachineName, "Printer", data, "doc");
            Console.WriteLine("Sent!");
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e.Message);
        }
        
        await Task.Delay(1000);
    }
}
