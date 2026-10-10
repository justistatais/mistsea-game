using System;
using System.Runtime.InteropServices;
using Godot;

public partial class Smoke : Node
{
    public override void _Ready()
    {
        var version = Engine.GetVersionInfo();
        if ((int)version["major"] != 4 || (int)version["minor"] != 7 || (int)version["patch"] != 2)
            throw new InvalidOperationException("The runtime differs from the locked Godot 4.7.2 editor.");
        GD.Print("MISTSEA_CX002_CSHARP_OK ", version["string"], " ", RuntimeInformation.FrameworkDescription,
            " ", RuntimeInformation.ProcessArchitecture);
        GetTree().Quit();
    }
}
