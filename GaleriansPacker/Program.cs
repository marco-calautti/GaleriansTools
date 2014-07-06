using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace GaleriansPacker
{
    class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("Galerians (PSX) CDB UnPacker (C) Phoenix - SadNES cITy Translations\nUsage: {0} -pack[cdb]/-unpack[cdb] archive_file files_directory", System.AppDomain.CurrentDomain.FriendlyName);
        }
        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                PrintUsage();
                return;
            }
            try
            {
                switch (args[0])
                {
                    case "-packcdb":
                        Pack(args[1], args[2],true);
                        break;
                    case "-pack":
                        Pack(args[1], args[2],false);
                        break;
                    case "-unpackcdb":
                        Unpack(args[1], args[2],true);
                        break;
                    case "-unpack":
                        Unpack(args[1], args[2], false);
                        break;
                    default:
                        Console.WriteLine("Wrong command: {0}", args[0]);
                        break;
                }
                
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.Message);
                return;
            }

            Console.WriteLine("Done.");
        }

        private static void Unpack(string cdbFile, string directory, bool cdb)
        {
            BufferedStream stream = new BufferedStream(File.OpenRead(cdbFile));
            BinaryReader reader = new BinaryReader(stream);

            byte[] buffer = new byte[0x800];

            UInt32 filesCount = reader.ReadUInt32();

            Directory.CreateDirectory(directory);
            for (int i = 0; i < filesCount; i++)
            {
                UInt32 sector, size;

                sector = cdb? reader.ReadUInt16() : reader.ReadUInt32();
                size = cdb? reader.ReadUInt16() : reader.ReadUInt32();
                

                uint remaining = 0;
                if (!cdb)
                {
                    remaining = size - (size / 0x800) * 0x800;
                    size = size / 0x800;
                }
                long lastPos = reader.BaseStream.Position;

                reader.BaseStream.Position = cdb? sector * 0x800 : sector;

                FileStream outStream=File.Open(Path.Combine(directory,i.ToString("D4")+".dat"), FileMode.Create);
                for (int j = 0; j < size; j++)
                {
                    reader.Read(buffer, 0, buffer.Length);
                    outStream.Write(buffer, 0, buffer.Length);
                }
                for (int j = 0; j < remaining; j++)
                    outStream.WriteByte(reader.ReadByte());

                outStream.Close();

                reader.BaseStream.Position = lastPos;
            }

            reader.Close();
        }


        private static void Pack(string cdbFile, string directory,bool cdb)
        {
            if (cdb)
                PackCDB(cdbFile, directory);
            else
                Pack(cdbFile, directory);
        }

        private static void Pack(string archive, string directory)
        {
            string[] fileNames = Directory.GetFiles(directory);

            Array.Sort<string>(fileNames);

            BinaryWriter outStream = new BinaryWriter(new BufferedStream(File.Open(archive, FileMode.Create)));

            UInt32 headerSize = 4 + (UInt32)fileNames.Length * 8;
            for(int i=0;i<headerSize;i++)
                outStream.Write((byte)0);

            UInt32 curOffset = headerSize;

            UInt32[] sizes = new UInt32[fileNames.Length];


            for (int i = 0; i < fileNames.Length; i++)
            {
                byte[] bytes = File.ReadAllBytes(fileNames[i]);
                sizes[i] = (UInt32)bytes.Length;
                outStream.Write(bytes, 0, bytes.Length);
                int remainder = (4 - (bytes.Length % 4)) % 4;
                for (int j = 0; j < remainder; j++)
                    outStream.Write((byte)0);
            }

            outStream.BaseStream.Position = 0;
            outStream.Write((UInt32)fileNames.Length);

            foreach (UInt32 size in sizes)
            {
                outStream.Write(curOffset);
                outStream.Write(size);
                curOffset += size;
                UInt32 remainder = (4 - (size % 4)) % 4;
                curOffset += remainder;
            }

            outStream.Close();
        }
        private static void PackCDB(string cdbFile, string directory)
        {
            string[] fileNames = Directory.GetFiles(directory);

            Array.Sort<string>(fileNames);

            BinaryWriter outStream = new BinaryWriter(new BufferedStream(File.Open(cdbFile, FileMode.Create)));

            int headerSize = 4 + fileNames.Length * 4;
            int tot = headerSize % 0x800 == 0 ? headerSize : headerSize + 0x800 - (headerSize % 0x800);
            for (int i = 0; i < tot; i++)
                outStream.Write((byte)0);

            int baseSector = headerSize % 0x800 == 0 ? headerSize / 0x800 : headerSize / 0x800 + 1;

            int[] sizes = new int[fileNames.Length];


            for (int i = 0; i < fileNames.Length; i++)
            {
                byte[] bytes = File.ReadAllBytes(fileNames[i]);
                sizes[i] = bytes.Length % 0x800 == 0 ? bytes.Length / 0x800 : bytes.Length / 0x800 + 1;
                outStream.Write(bytes, 0, bytes.Length);
                int remainder = (0x800 - (bytes.Length % 0x800)) % 0x800;
                for (int j = 0; j < remainder; j++)
                    outStream.Write((byte)0);
            }

            outStream.BaseStream.Position = 0;
            outStream.Write((UInt32)fileNames.Length);
            foreach (UInt16 size in sizes)
            {
                outStream.Write((UInt16)baseSector);
                outStream.Write((UInt16)size);
                baseSector += size;
            }

            outStream.Close();
        }
    }
}
