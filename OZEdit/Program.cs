using MapPublishingApp;
using System;

namespace OZEdit
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("First argument is input folder path, second is output mapname.w3x");
            MapPublisher.PackMap(args[0], args[1]);
        }
    }
}
