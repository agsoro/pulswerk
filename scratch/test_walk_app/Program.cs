using System;
using System.IO.BACnet;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Drivers.BACnet;

var transport = new BacnetIpUdpProtocolTransport(port: 0, useExclusivePort: true);
var client = new BacnetClient(transport);
client.Start();

// Resolve address of device 10
Console.WriteLine("Resolving address for device 10...");
var adr = BacnetDriver.ResolveAddress(client, "127.0.0.1", 47808, 10, 2000);
Console.WriteLine($"Resolved: {adr}");

// Walk hierarchy
Console.WriteLine("Walking hierarchy...");
var tree = BacnetHierarchy.Walk(client, adr, 10);
Console.WriteLine($"Walk complete. Roots: {tree.Roots.Count}");

foreach (var root in tree.Roots)
{
    PrintNode(root, 0);
}

void PrintNode(DezikoNode node, int indent)
{
    string prefix = node.IsView ? "[FOLDER]" : "[POINT] ";
    Console.WriteLine($"{new string(' ', indent * 2)}- {prefix} {node.FriendlyName} ({node.ObjectId}) children={node.Children.Count}");
    foreach (var child in node.Children)
    {
        PrintNode(child, indent + 1);
    }
}
