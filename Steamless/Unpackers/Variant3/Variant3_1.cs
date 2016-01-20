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

namespace Steamless.Unpackers.Variant3
{
    using Steamless.Classes;
    using Steamless.Extensions;
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;

    public class Variant3_1
    {
        /// <summary>
        /// SteamStub DRM Variant 3 Flags
        /// </summary>
        public enum DrmFlags
        {
            NoModuleVerification = 0x02,
            NoEncryption = 0x04,
            NoOwnershipCheck = 0x10,
            NoDebuggerCheck = 0x20,
            NoErrorDialog = 0x40
        }

        /// <summary>
        /// SteamStub DRM Variant 3.1 Header
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SteamStub32Var31Header
        {
            public uint XorKey; // The base xor key, if defined, to unpack the file with.
            public uint Signature; // The signature to ensure the xor decoding was successful.
            public ulong ImageBase; // The base of the image that was protected.
            public ulong AddressOfEntryPoint; // The entry point that is set from the DRM.
            public uint BindSectionOffset; // The starting offset to the .bind section data. RVA(AddressOfEntryPoint - BindSectionOffset)
            public uint Unknown0000; // [Cyanic: This field is most likely the .bind code size.]
            public ulong OriginalEntryPoint; // The original entry point of the binary before it was protected.
            public uint Unknown0001; // [Cyanic: This field is most likely an offset to a string table.]
            public uint PayloadSize; // The size of the payload data.
            public uint DRMPDllOffset; // The offset to the SteamDrmp.dll file.
            public uint DRMPDllSize; // The size of the SteamDrmp.dll file.
            public uint SteamAppId; // The Steam application id of this program.
            public uint Flags; // The DRM flags used while protecting this program.
            public uint BindSectionVirtualSize; // The .bind section virtual size.
            public uint Unknown0002; // [Cyanic: This field is most likely a hash of some sort.]
            public ulong CodeSectionVirtualAddress; // The code section virtual address.
            public ulong CodeSectionRawSize; // The code section raw size.

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x20)]
            public byte[] AES_Key; // The AES encryption key.

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
            public byte[] AES_IV; // The AES encryption IV.

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
            public byte[] CodeSectionStolenData; // The first 16 bytes of the code section stolen.

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x04)]
            public uint[] EncryptionKeys; // Encryption keys used to decrypt the SteamDrmp.dll file.

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x08)]
            public uint[] Unknown0003; // Unknown unused data.

            public ulong GetModuleHandleA_Rva; // The rva to GetModuleHandleA.
            public ulong GetModuleHandleW_Rva; // The rva to GetModuleHandleW.
            public ulong LoadLibraryA_Rva; // The rva to LoadLibraryA.
            public ulong LoadLibraryW_Rva; // The rva to LoadLibraryW.
            public ulong GetProcAddress_Rva; // The rva to GetProcAddress.
        }

        /// <summary>
        /// Processes the given file in attempt to remove the DRM protection.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public bool Process(Pe32File file)
        {
            // Store the file object for later usage..
            this.File = file;
            this.CodeSectionIndex = -1;

            // Announce the packer version being used..
            Program.Output("File is packed with SteamStub Variant #3.1!\n", ConsoleOutputType.Success);

            // Process the packed file..
            Program.Output("[!] Info: Unpacker Stage 1 - Read, decode and validate SteamStub DRM header.", ConsoleOutputType.Custom, ConsoleColor.Magenta);
            if (!this.Step1())
                return false;

            Program.Output("[!] Info: Unpacker Stage 2 - Read, decode, and process payload data.", ConsoleOutputType.Custom, ConsoleColor.Magenta);
            if (!this.Step2())
                return false;

            Program.Output("[!] Info: Unpacker Stage 3 - Read, decode, and dump SteamDrmp.dll file.", ConsoleOutputType.Custom, ConsoleColor.Magenta);
            if (!this.Step3())
                return false;

            Program.Output("[!] Info: Unpacker Stage 4 - Handle .bind section.", ConsoleOutputType.Custom, ConsoleColor.Magenta);
            if (!this.Step4())
                return false;

            Program.Output("[!] Info: Unpacker Stage 5 - Read, decrypt, and process code section.", ConsoleOutputType.Custom, ConsoleColor.Magenta);
            if (!this.Step5())
                return false;

            Program.Output("[!] Info: Unpacker Stage 6 - Rebuild unpacked file.", ConsoleOutputType.Custom, ConsoleColor.Magenta);
            if (!this.Step6())
                return false;

            Console.WriteLine();
            Program.Output("Processed the file successfully!", ConsoleOutputType.Success);
            return true;
        }

        /// <summary>
        /// Step #1
        /// 
        /// Reads, decodes, and validates the SteamStub DRM header.
        /// </summary>
        /// <returns></returns>
        private bool Step1()
        {
            // Obtain the DRM header data..
            var fileOffset = this.File.GetFileOffsetFromRva(this.File.NtHeaders.OptionalHeader.AddressOfEntryPoint);
            var headerData = new byte[0xF0];
            Array.Copy(this.File.FileData, (int)(fileOffset - 0xF0), headerData, 0, 0xF0);

            // Xor decode the header data..
            this.XorKey = SteamXor(ref headerData, 0xF0);
            this.StubHeader = Pe32Helpers.GetStructure<SteamStub32Var31Header>(headerData);

            // Validate the header signature..
            return (this.StubHeader.Signature == 0xC0DEC0DF);
        }

        /// <summary>
        /// Step #2
        /// 
        /// Reads, decodes, and processes the payload data.
        /// </summary>
        /// <returns></returns>
        private bool Step2()
        {
            // Obtain the payload address and size..
            var payloadAddr = this.File.GetFileOffsetFromRva(this.File.NtHeaders.OptionalHeader.AddressOfEntryPoint - this.StubHeader.BindSectionOffset);
            var payloadSize = (this.StubHeader.PayloadSize + 0x0F) & 0xFFFFFFF0;

            // Do nothing if there is no payload..
            if (payloadSize == 0)
                return true;

            Program.Output("  --> Payload found within program!", ConsoleOutputType.Info);

            // Obtain and decode the payload..
            var payload = new byte[payloadSize];
            Array.Copy(this.File.FileData, payloadAddr, payload, 0, payloadSize);
            this.XorKey = SteamXor(ref payload, payloadSize, this.XorKey);

            // Todo: Do something with the payload here..
            return true;
        }

        /// <summary>
        /// Step #3
        /// 
        /// Reads, decodes, and dumps the SteamDrmp.dll file.
        /// </summary>
        /// <returns></returns>
        private bool Step3()
        {
            // Ensure there is a dll to process..
            if (this.StubHeader.DRMPDllSize == 0)
            {
                Program.Output("  --> Program does not appear to have a SteamDrmp.dll file!", ConsoleOutputType.Info);
                return true;
            }

            Program.Output("  --> SteamDrmp.dll found within program!", ConsoleOutputType.Info);

            try
            {
                // Obtain the SteamDrmp.dll file address and data..
                var drmpAddr = this.File.GetFileOffsetFromRva(this.File.NtHeaders.OptionalHeader.AddressOfEntryPoint - this.StubHeader.BindSectionOffset + this.StubHeader.DRMPDllOffset);
                var drmpData = new byte[this.StubHeader.DRMPDllSize];
                Array.Copy(this.File.FileData, drmpAddr, drmpData, 0, drmpData.Length);

                // Decrypt the data (xtea decryption)..
                SteamDrmpDecryptPass1(ref drmpData, this.StubHeader.DRMPDllSize, this.StubHeader.EncryptionKeys);

                // Save the SteamDrmp.dll file..
                var basePath = Path.GetDirectoryName(this.File.FilePath) ?? string.Empty;
                System.IO.File.WriteAllBytes(Path.Combine(basePath, "SteamDrmp.dll"), drmpData);

                Program.Output("  --> SteamDrmp.dll saved to disk!", ConsoleOutputType.Success);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Step #4
        /// 
        /// Remove the .bind section if requested, find the code section.
        /// </summary>
        /// <returns></returns>
        private bool Step4()
        {
            // Remove the .bind section if it is not being kept..
            if (!Program.HasArgument("--keepbind"))
            {
                // Obtain the .bind section..
                var bindSection = this.File.GetSection(".bind");
                if (!bindSection.IsValid)
                    return false;

                // Remove the section..
                this.File.RemoveSection(bindSection);

                // Decrease the header section count..
                var ntHeaders = this.File.NtHeaders;
                ntHeaders.FileHeader.NumberOfSections--;
                this.File.NtHeaders = ntHeaders;

                Program.Output("  --> .bind section was removed!", ConsoleOutputType.Info);
            }
            else
                Program.Output("  --> .bind section was kept!", ConsoleOutputType.Info);

            // Skip finding the code section if we are not encrypted..
            if ((this.StubHeader.Flags & (uint)DrmFlags.NoEncryption) == (uint)DrmFlags.NoEncryption)
                return true;

            // Find the code section..
            var codeSection = this.File.GetOwnerSection(this.StubHeader.CodeSectionVirtualAddress);
            if (codeSection.PointerToRawData == 0 || codeSection.SizeOfRawData == 0)
                return false;

            // Obtain the code section index..
            this.CodeSectionIndex = this.File.GetSectionIndex(codeSection);

            return true;
        }

        /// <summary>
        /// Step #5
        /// 
        /// Reads, decrypts, and processes the code section.
        /// </summary>
        /// <returns></returns>
        private bool Step5()
        {
            // Do nothing if the code section is not encrypted..
            if ((this.StubHeader.Flags & (uint)DrmFlags.NoEncryption) == (uint)DrmFlags.NoEncryption)
            {
                Program.Output("  --> Code section not encrypted!", ConsoleOutputType.Info);
                return true;
            }

            try
            {
                // Obtain the code section..
                var codeSection = this.File.Sections[this.CodeSectionIndex];
                Program.Output($"  --> '{codeSection.SectionName}' linked as main code section!", ConsoleOutputType.Info);
                Program.Output($"  --> '{codeSection.SectionName}' section encrypted!", ConsoleOutputType.Info);

                // Obtain the code section data..
                var codeSectionData = new byte[codeSection.SizeOfRawData + this.StubHeader.CodeSectionStolenData.Length];
                Array.Copy(this.StubHeader.CodeSectionStolenData, 0, codeSectionData, 0, this.StubHeader.CodeSectionStolenData.Length);
                Array.Copy(this.File.FileData, this.File.GetFileOffsetFromRva(codeSection.VirtualAddress), codeSectionData, this.StubHeader.CodeSectionStolenData.Length, codeSection.SizeOfRawData);

                // Create the AES decryption helper..
                var aes = new AesHelper(this.StubHeader.AES_Key, this.StubHeader.AES_IV);
                aes.RebuildIv(this.StubHeader.AES_IV);

                // Decrypt the code section data..
                var data = aes.Decrypt(codeSectionData, CipherMode.CBC, PaddingMode.None);
                if (data == null)
                    return false;

                // Set the override data..
                this.CodeSectionData = data;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Step #6
        /// 
        /// Rebuild the unpacked file.
        /// </summary>
        /// <returns></returns>
        private bool Step6()
        {
            FileStream fStream = null;

            try
            {
                // Rebuild the sections of the file..
                this.File.RebuildSections();

                // Open the unpacked file for writing..
                var unpackedPath = this.File.FilePath + ".unpacked.exe";
                fStream = new FileStream(unpackedPath, FileMode.Create, FileAccess.ReadWrite);

                // Write the dos header..
                fStream.WriteBytes(Pe32Helpers.GetStructureBytes(this.File.DosHeader));

                // Write the dos stub..
                if (this.File.DosStubSize > 0)
                    fStream.WriteBytes(this.File.DosStubData);

                // Update the entry point of the file..
                var ntHeaders = this.File.NtHeaders;
                ntHeaders.OptionalHeader.AddressOfEntryPoint = (uint)this.StubHeader.OriginalEntryPoint;
                this.File.NtHeaders = ntHeaders;

                // Write the Nt headers..
                fStream.WriteBytes(Pe32Helpers.GetStructureBytes(ntHeaders));

                // Write the sections to the file..
                for (var x = 0; x < this.File.Sections.Count; x++)
                {
                    var section = this.File.Sections[x];
                    var sectionData = this.File.SectionData[x];

                    // Write the section header to the file..
                    fStream.WriteBytes(Pe32Helpers.GetStructureBytes(section));

                    var sectionOffset = fStream.Position;
                    fStream.Position = section.PointerToRawData;

                    var sectionIndex = this.File.Sections.IndexOf(section);
                    if (sectionIndex == this.CodeSectionIndex)
                        fStream.WriteBytes(this.CodeSectionData ?? sectionData);
                    else
                        fStream.WriteBytes(sectionData);

                    // Reset the file offset..
                    fStream.Position = sectionOffset;
                }

                // Write the overlay data if available..
                fStream.Position = fStream.Length;
                if (this.File.OverlayData != null)
                    fStream.WriteBytes(this.File.OverlayData);

                Program.Output("  --> Unpacked file saved to disk!", ConsoleOutputType.Success);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                fStream?.Dispose();
            }
        }

        /// <summary>
        /// Xor decrypts the given data starting with the given key, if any.
        /// 
        /// @note    If no key is given (0) then the first key is read from the first
        ///          4 bytes inside of the data given.
        /// </summary>
        /// <param name="data">The data to xor decode.</param>
        /// <param name="size">The size of the data to decode.</param>
        /// <param name="key">The starting xor key to decode with.</param>
        /// <returns></returns>
        private static uint SteamXor(ref byte[] data, uint size, uint key = 0)
        {
            var offset = (uint)0;

            // Read the first key as the base xor key if we had none given..
            if (key == 0)
            {
                offset += 4;
                key = BitConverter.ToUInt32(data, 0);
            }

            // Decode the data..
            for (var x = offset; x < size; x += 4)
            {
                var val = BitConverter.ToUInt32(data, (int)x);
                Array.Copy(BitConverter.GetBytes(val ^ key), 0, data, x, 4);

                key = val;
            }

            return key;
        }

        /// <summary>
        /// The second pass of decryption for the SteamDRMP.dll file.
        /// 
        /// @note    The encryption method here is known as XTEA.
        /// </summary>
        /// <param name="res">The result value buffer to write our returns to.</param>
        /// <param name="keys">The keys used for the decryption.</param>
        /// <param name="v1">The first value to decrypt from.</param>
        /// <param name="v2">The second value to decrypt from.</param>
        /// <param name="n">The number of passes to crypt the data with.</param>
        private static void SteamDrmpDecryptPass2(ref uint[] res, uint[] keys, uint v1, uint v2, uint n = 32)
        {
            const uint delta = 0x9E3779B9;
            const uint mask = 0xFFFFFFFF;
            var sum = (delta * n) & mask;

            for (var x = 0; x < n; x++)
            {
                v2 = (v2 - (((v1 << 4 ^ v1 >> 5) + v1) ^ (sum + keys[sum >> 11 & 3]))) & mask;
                sum = (sum - delta) & mask;
                v1 = (v1 - (((v2 << 4 ^ v2 >> 5) + v2) ^ (sum + keys[sum & 3]))) & mask;
            }

            res[0] = v1;
            res[1] = v2;
        }

        /// <summary>
        /// The first pass of the decryption for the SteamDRMP.dll file.
        /// 
        /// @note    The encryption method here is known as XTEA. It is modded to include
        ///          some basic xor'ing.
        /// </summary>
        /// <param name="data">The data to decrypt.</param>
        /// <param name="size">The size of the data to decrypt.</param>
        /// <param name="keys">The keys used for the decryption.</param>
        private static void SteamDrmpDecryptPass1(ref byte[] data, uint size, uint[] keys)
        {
            var v1 = (uint)0x55555555;
            var v2 = (uint)0x55555555;

            for (var x = 0; x < size; x += 8)
            {
                var d1 = BitConverter.ToUInt32(data, x + 0);
                var d2 = BitConverter.ToUInt32(data, x + 4);

                var res = new uint[2];
                SteamDrmpDecryptPass2(ref res, keys, d1, d2);

                Array.Copy(BitConverter.GetBytes(res[0] ^ v1), 0, data, x + 0, 4);
                Array.Copy(BitConverter.GetBytes(res[1] ^ v2), 0, data, x + 4, 4);

                v1 = d1;
                v2 = d2;
            }
        }

        /// <summary>
        /// Gets or sets the file being processed.
        /// </summary>
        public Pe32File File { get; set; }

        /// <summary>
        /// Gets or sets the current Xor key being used against the file data.
        /// </summary>
        public uint XorKey { get; set; }

        /// <summary>
        /// Gets or sets the DRM stub header.
        /// </summary>
        public SteamStub32Var31Header StubHeader { get; set; }

        /// <summary>
        /// Gets or sets the code section index.
        /// </summary>
        public int CodeSectionIndex { get; set; }

        /// <summary>
        /// Gets or sets the decrypted code section data.
        /// </summary>
        public byte[] CodeSectionData { get; set; }
    }
}