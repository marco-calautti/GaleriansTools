using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GaleriansCompression;

namespace GaleriansDecomp
{
    class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("Galerians (PSX) File Compressor (C) Phoenix - SadNES cITy Translations\nUsage: {0} inputfile outputfile", System.AppDomain.CurrentDomain.FriendlyName);
        }
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                PrintUsage();
                return;
            }
            try
            {

                Console.WriteLine("Compressing file {0} to file {1}", args[0], args[1]);
                Console.WriteLine("Please be patient, it may be (very) slow...");
                CompressionUtil.Compress(args[0], args[1]);

            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                return;
            }

            Console.WriteLine("Done.");
        }
    }
}
