using System;
using System.Windows;

namespace VAL.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        var window = new Window
        {
            Title = "VAL",
            Width = 800,
            Height = 600
        };

        app.Run(window);
    }
}
