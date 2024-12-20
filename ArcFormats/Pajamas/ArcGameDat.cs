//! \file       ArcGamedat.cs
//! \date       Fri May 01 03:12:04 2015
//! \brief      Pajamas Adventure System resource archive.
//
// Copyright (C) 2015 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.Pajamas
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string Tag { get { return "GAMEDAT"; } }
        public override string Description { get { return "Pajamas Adventure System resource archive"; } }
        public override uint Signature { get { return 0x454d4147; } } // 'GAME'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return true; } }

        public DatOpener()
        {
            Extensions = new string[] { "dat", "pak" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.AsciiEqual(0, "GAMEDAT PAC"))
                return null;
            int version = file.View.ReadByte(0x0b);
            if ('K' == version)
                version = 1;
            else if ('2' == version)
                version = 2;
            else
                return null;
            int count = file.View.ReadInt32(0x0c);
            if (count <= 0 || count > 0xfffff)
                return null;
            int name_length = 1 == version ? 16 : 32;

            int name_offset = 0x10;
            int index_offset = name_offset + name_length * count;
            int base_offset = index_offset + 8 * count;
            if ((uint)base_offset > file.View.Reserve(0, (uint)base_offset))
                return null;
            var dir = new List<Entry>(count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString(name_offset, (uint)name_length);
                var entry = FormatCatalog.Instance.Create<Entry>(name);
                entry.Offset = base_offset + file.View.ReadUInt32(index_offset);
                entry.Size = file.View.ReadUInt32(index_offset + 4);
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                dir.Add(entry);
                name_offset += name_length;
                index_offset += 8;
            }
            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            if (!entry.Name.EndsWith("textdata.bin", StringComparison.InvariantCultureIgnoreCase))
                return arc.File.CreateStream(entry.Offset, entry.Size);
            var data = arc.File.View.ReadBytes(entry.Offset, entry.Size);
            // encrypted PJADV
            if (0x95 == data[0] && 0x6B == data[1] && 0x3C == data[2]
                && 0x9D == data[3] && 0x63 == data[4])
            {
                byte key = 0xC5;
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] ^= key;
                    key += 0x5C;
                }
            }
            return new BinMemoryStream(data, entry.Name);
        }

        public override void Create(Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            int file_count = list.Count();
            if (null != callback)
                callback(file_count + 2, null, null);
            //int callback_count = 0;
            using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
            {
                var encoding = Encodings.cp932;
                byte[] name_buf = new byte[0x40];
                Encoding.ASCII.GetBytes("GAMEDAT PAC", 0, 11, name_buf, 0);
                writer.Write(name_buf, 0, 11);

                var arc_options = GetOptions<ArcOptions>(options);
                int version = arc_options.Version;
                if (version == 1)
                {
                    writer.Write('K');
                }
                else
                {
                    writer.Write('2');
                }
                int count = list.Count();
                writer.Write(count);
                int name_length = 1 == version ? 16 : 32;

                int name_offset = 0x10;
                int index_offset = name_offset + name_length * count;
                int base_offset = index_offset + 8 * count;

                // name section
                foreach (var entry in list)
                {
                    string name = Path.GetFileName(entry.Name);
                    try
                    {
                        int size = encoding.GetBytes(name, 0, name.Length, name_buf, 0);
                        for (int i = size; i < name_length; ++i)
                            name_buf[i] = 0;
                    }
                    catch (Exception X)
                    {
                        throw new InvalidFileName(entry.Name, X);
                    }
                    writer.Write(name_buf, 0, name_length);
                }

                // data section
                writer.BaseStream.Position = base_offset;
                foreach (var entry in list)
                {
                    entry.Offset = base_offset;
                    using (var input = File.OpenRead(entry.Name))
                    {
                        var size = input.Length;
                        if (size > uint.MaxValue || base_offset + size > uint.MaxValue)
                            throw new FileSizeException();
                        base_offset += (int)size;
                        entry.Size = (uint)size;
                        if (!entry.Name.EndsWith("textdata.bin", StringComparison.InvariantCultureIgnoreCase))
                        {
                            input.CopyTo(output);
                        }
                        else
                        {
                            byte[] data = new byte[entry.Size];
                            input.Read(data, 0, (int)entry.Size);
                            // check encrypted PJADV
                            if (0x95 == data[0] && 0x6B == data[1] && 0x3C == data[2] &&
                                0x9D == data[3] && 0x63 == data[4])
                            {
                                // encrypted
                            }
                            else
                            {
                                // unencrypted
                                byte key = 0xC5;
                                for (int i = 0; i < data.Length; ++i)
                                {
                                    data[i] ^= key;
                                    key += 0x5C;
                                }
                            }
                            writer.Write(data, 0, data.Length);
                        }
                    }
                }

                // index section
                writer.BaseStream.Position = index_offset;
                base_offset = index_offset + 8 * count;
                foreach (var entry in list)
                {
                    writer.BaseStream.Position = index_offset;
                    writer.Write((uint)(entry.Offset -base_offset));
                    writer.Write(entry.Size);
                    index_offset += 0x8;
                }
            }
        }

        public override object GetCreationWidget()
        {
            return new GUI.CreateGameDatWidget();
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new ArcOptions { Version = Properties.Settings.Default.GameDatVersion };
        }
    }

    public class ArcOptions : ResourceOptions
    {
        public int Version { get; set; }
    }
}
