using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace CNCRemasterMegaDumper
{
    /// <summary>
    /// 
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct FileDescriptor
    {
        public ushort Flags;
        public uint CRCValue;
        public int Index;
        public uint FileSize;
        public uint DataOffset;
        public ushort NameIndex;

        public static readonly uint StructureSize = (uint)Marshal.SizeOf(typeof(FileDescriptor));
    } // public struct FileDescriptor

    /// <summary>
    /// 
    /// </summary>
    class Program
    {
        static string[] fileNameTable = new string[0];
        static Dictionary<string, FileDescriptor> descriptorTable = new Dictionary<string, FileDescriptor>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="dest"></param>
        /// <param name="action"></param>
        static void PumpStreams(Stream source, Stream dest, Action<int, Stream> action = null)
        {
            byte[] buffer = new byte[64 * 1024];

            int numRead = source.Read(buffer, 0, buffer.Length);
            while (numRead != 0)
            {
                dest.Write(buffer, 0, numRead);
                if (action != null)
                    action(numRead, dest);
                numRead = source.Read(buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Main(string[] args)
        {
            if (args.Length < 0)
                return -1;

            if (!File.Exists(args[0]))
                return -2;

            uint offset = 0U;
            uint descriptorCount = 0U, totalFiles = 0U;
            uint fileNameTableSize = 0u, fileDataTableSize = 0u;

            string fileName = args[0];

            // open file and map it to a block in memory
            MemoryMappedFile file = MemoryMappedFile.CreateFromFile(new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.None), fileName, 0L, MemoryMappedFileAccess.Read, HandleInheritability.None, false);

            // read total length
            using (BinaryReader reader = new BinaryReader(file.CreateViewStream(offset, 4)))
            {
                uint length = reader.ReadUInt32();
                if (length == 4294967295U || length == 2415919103U)
                    offset += 8U;
            }

            // read length metadata
            offset += 4U;
            using (BinaryReader reader = new BinaryReader(file.CreateViewStream(offset, 12)))
            {
                descriptorCount = reader.ReadUInt32();
                totalFiles = reader.ReadUInt32();

                fileNameTableSize = reader.ReadUInt32();
                fileDataTableSize = descriptorCount * FileDescriptor.StructureSize;
            }

            // read the file path table
            offset += 12U;
            using (BinaryReader reader = new BinaryReader(file.CreateViewStream(offset, fileNameTableSize)))
            {
                fileNameTable = new string[totalFiles];
                for (uint fileIdx = 0u; fileIdx < totalFiles; fileIdx++)
                {
                    ushort len = reader.ReadUInt16();
                    fileNameTable[fileIdx] = new string(reader.ReadChars(len));
                }
            }

            // read the file descriptors table
            offset += fileNameTableSize;
            using (MemoryMappedViewAccessor mappedAccessor = file.CreateViewAccessor(offset, fileDataTableSize))
            {
                for (uint fileIdx = 0u; fileIdx < descriptorCount; fileIdx++)
                {
                    // use the memory mapped data to build the file descriptor
                    FileDescriptor subFileData;
                    mappedAccessor.Read<FileDescriptor>(fileIdx * FileDescriptor.StructureSize, out subFileData);

                    descriptorTable[fileNameTable[subFileData.NameIndex]] = subFileData;
                }
            }

            // iterate through all files and extract
            foreach (KeyValuePair<string, FileDescriptor> kvp in descriptorTable)
            {
                string filename = Path.GetFileName(kvp.Key);
                string path = Path.GetDirectoryName(kvp.Key);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                // check that the archive contains the given file
                FileDescriptor subFileData;
                if (!descriptorTable.TryGetValue(kvp.Key, out subFileData))
                    continue;

                using (Stream stream = file.CreateViewStream(subFileData.DataOffset, subFileData.FileSize))
                {
                    using (Stream outStream = File.Open(path + Path.DirectorySeparatorChar + filename, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    {
                        Console.Write(string.Format(">> Extracting {0}...\n", kvp.Key));
                        long totalBytes = 0L;
                        PumpStreams(stream, outStream, (int bytes, Stream dest) =>
                        {
                            totalBytes += bytes;
                            Console.Write("\rwriting {0} bytes.", totalBytes);
                        });
                        Console.Write("\n");

                        outStream.Flush();
                        outStream.Close();
                    }

                    stream.Close();
                }
            }

            file.Dispose();
            return 0;
        }
    } // class Program
} // namespace CNCRemasterMegaDumper
