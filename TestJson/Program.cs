using System;
using System.Text.Json.Nodes;
class Program {
    static void Main() {
        try {
            var node = JsonNode.Parse("{\"AgentSettings\":{\"AllowedPrinters\":[]}}");
            var settings = node?["AgentSettings"];
            var allowedArray = new JsonArray();
            allowedArray.Add("Printer 1");
            allowedArray.Add("Printer 2");
            settings["AllowedPrinters"] = allowedArray;
            Console.WriteLine(node.ToJsonString());
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}
