using System.Collections.Concurrent;
using System.Linq;
using Microsoft.AspNetCore.SignalR;

namespace ExamplePrintHub.Hubs;

public class PrintHub : Hub
{
    // AgentName -> ConnectionId
    public static readonly ConcurrentDictionary<string, string> ConnectedAgents = new();
    
    // UI Connections -> ConnectionId
    public static readonly ConcurrentDictionary<string, bool> UiClients = new();

    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Remove agent if disconnected
        var agent = ConnectedAgents.FirstOrDefault(x => x.Value == Context.ConnectionId);
        if (agent.Key != null)
        {
            ConnectedAgents.TryRemove(agent.Key, out _);
            // Notify UI clients
            await Clients.Clients(UiClients.Keys).SendAsync("AgentsUpdated", ConnectedAgents.Keys);
        }
        
        UiClients.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    // --- Methods called by PrintAgent ---

    public async Task RegisterAgent(string agentName)
    {
        ConnectedAgents[agentName] = Context.ConnectionId;
        Console.WriteLine($"Agent Registered: {agentName} ({Context.ConnectionId})");
        
        // Notify UI clients about the new agent list
        await Clients.Clients(UiClients.Keys).SendAsync("AgentsUpdated", ConnectedAgents.Keys);
    }

    public async Task SendPrintersList(string agentName, string correlationId, List<string> printers)
    {
        Console.WriteLine($"Received printers from {agentName} (CorrelationId: {correlationId}): {printers.Count} printers.");
        
        // Forward the printer list to the UI client that requested it (correlationId is used as the UI connection ID)
        await Clients.Client(correlationId).SendAsync("ReceivePrinters", agentName, printers);
    }

    public async Task ReportPrintStatus(string logId, string callerId, bool isSuccess, string message, string documentName)
    {
        Console.WriteLine($"Print status from Agent - LogId: {logId}, Success: {isSuccess}, Message: {message}");
        
        // Forward the status to the UI client that requested it (callerId is the UI connection ID)
        await Clients.Client(callerId).SendAsync("PrintStatusUpdated", logId, isSuccess, message, documentName);
    }

    // --- Methods called by UI (Vue Client) ---

    public async Task RegisterUiClient()
    {
        UiClients[Context.ConnectionId] = true;
        Console.WriteLine($"UI Client connected: {Context.ConnectionId}");
        
        // Send the current list of agents to the new UI client
        await Clients.Caller.SendAsync("AgentsUpdated", ConnectedAgents.Keys);
    }

    public async Task RequestPrinters(string agentName)
    {
        if (ConnectedAgents.TryGetValue(agentName, out var agentConnectionId))
        {
            // Use UI's connection ID as correlationId so we know where to send the response
            string correlationId = Context.ConnectionId; 
            await Clients.Client(agentConnectionId).SendAsync("GetPrinters", correlationId);
        }
        else
        {
            await Clients.Caller.SendAsync("PrintError", $"Agent '{agentName}' is not connected.");
        }
    }

    public async Task SendPrintJob(string agentName, string printerName, string data, string documentName)
    {
        if (ConnectedAgents.TryGetValue(agentName, out var agentConnectionId))
        {
            string logId = Guid.NewGuid().ToString();
            string callerId = Context.ConnectionId; 
            
            await Clients.Client(agentConnectionId).SendAsync("PrintCommand", logId, callerId, printerName, data, documentName);
        }
        else
        {
            await Clients.Caller.SendAsync("PrintError", $"Agent '{agentName}' is not connected.");
        }
    }
}
