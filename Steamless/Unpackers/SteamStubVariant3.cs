/**
 * Steamless Steam DRM Remover
 * (c) 2015-2016 atom0s [atom0s@live.com]
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see http://www.gnu.org/licenses/
 */

namespace Steamless.Unpackers
{
    using Classes;
    using Steamless.Unpackers.Variant3;
    using System;

    /// <summary>
    /// Steam Stub DRM Unpacker (Variant #3)
    /// 
    /// Special thanks to Cyanic (aka Golem_x86) for his assistance.
    /// </summary>
    [SteamStubUnpacker(
        Author = "atom0s", Name = "SteamStub Variant #3",
        Pattern = "E8 00 00 00 00 50 53 51 52 56 57 55 8B 44 24 1C 2D 05 00 00 00 8B CC 83 E4 F0 51 51 51 50")]
    public class SteamStubVariant3 : SteamStubUnpacker
    {
        /// <summary>
        /// Processes the given file in attempt to remove the DRM protection.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public override bool Process(Pe32File file)
        {
            Program.Output("File is packed with SteamStub Variant #3!", ConsoleOutputType.Success);
            Program.Output("Determining what variant version was used...", ConsoleOutputType.Info);

            // Try and determine the packer version used..
            var bindSection = file.GetSectionData(".bind");
            var offset = Pe32Helpers.FindPattern(bindSection, "55 8B EC 81 EC ?? ?? ?? ?? 53 ?? ?? ?? ?? ?? 68");
            var drmHeaderSize = 0;

            if (offset == 0)
            {
                // Try again with the last two instructions flipped..
                offset = Pe32Helpers.FindPattern(bindSection, "55 8B EC 81 EC ?? ?? ?? ?? 53 ?? ?? ?? ?? ?? 8D 83");
                if (offset == 0)
                {
                    // Todo: Manually attempt each version of the variant 3 unpackers..
                    Program.Output("Could not determine the variant version to unpack with!", ConsoleOutputType.Error);
                    return false;
                }
                else
                {
                    // Obtain the DRM header size..
                    drmHeaderSize = BitConverter.ToInt32(bindSection, (int)offset + 22);
                }
            }
            else
            {
                // Obtain the DRM header size..
                drmHeaderSize = BitConverter.ToInt32(bindSection, (int)offset + 16);
            }


            // Attempt to handle the given DRM header size..
            switch (drmHeaderSize)
            {
                //
                // Variant Version v3.0.0 (?)
                //
                case 0xB0: // Older version of v3.0.0
                case 0xD0: // Newer version of v3.0.0
                    {
                        var unpacker = new Variant3_0();
                        return unpacker.Process(file);
                    }

                //
                // Variant Version v3.0.1 (?)
                //
                case 0xF0:
                    {
                        var unpacker = new Variant3_1();
                        return unpacker.Process(file);
                    }
            }

            return false;
        }
    }
}