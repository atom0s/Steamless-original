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

namespace Steamless
{
    using Classes;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    internal class Program
    {
        /// <summary>
        /// Application entry point.
        /// </summary>
        /// <param name="args"></param>
        private static void Main(string[] args)
        {
            // Override the assembly resolve event for this application..
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;

            PrintHeader();

            // Parse the command line arguments..
            Arguments = new List<string>();
            Arguments.AddRange(Environment.GetCommandLineArgs());

            if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
            {
                PrintHelp();
            }
            else
            {
                // Load the given file and ensure it is valid..
                var file = new Pe32File(args[0]);
                if (!file.Parse() || file.IsFile64Bit() || !file.HasSection(".bind"))
                {
                    Output("Steamless cannot process the given file.", ConsoleOutputType.Error);
                    if (file.IsFile64Bit())
                        Output("  --> This verison of Steamless does not support x64 files!", ConsoleOutputType.Error);
                    if (!file.HasSection(".bind"))
                        Output("  --> The given file does not have a .bind section!", ConsoleOutputType.Error);

                    Console.WriteLine("\nPress any key to exit...");
                    Console.Read();
                    return;
                }

                // Build a list of known packers from within our local source..
                var unpackers = (from t in Assembly.GetExecutingAssembly().GetTypes()
                                 from a in t.GetCustomAttributes(typeof(SteamStubUnpackerAttribute), false)
                                 select t).ToList();

                // Display a list of found unpacker engines..
                Output("Found the following unpackers (internally):", ConsoleOutputType.Info);
                foreach (var attr in unpackers.Select(unpacker => (SteamStubUnpackerAttribute)unpacker.GetCustomAttributes(typeof(SteamStubUnpackerAttribute)).FirstOrDefault()))
                    Output($" >> Unpacker: {attr?.Name} - by: {attr?.Author}", ConsoleOutputType.Custom, ConsoleColor.Yellow);
                Console.WriteLine();

                // Attempt to process the packed file..
                Func<bool> processed = () =>
                    {
                        // Obtain the .bind section data..
                        var bindSectionData = file.GetSectionData(".bind");

                        // Attempt to process the file..
                        return (from unpacker in unpackers
                                let attr = (SteamStubUnpackerAttribute)unpacker.GetCustomAttributes(typeof(SteamStubUnpackerAttribute)).FirstOrDefault()
                                where attr != null
                                where Pe32Helpers.FindPattern(bindSectionData, attr.Pattern) != 0
                                select Activator.CreateInstance(unpacker) as SteamStubUnpacker).Select(stubUnpacker => stubUnpacker.Process(file)).FirstOrDefault();
                    };

                if (!processed())
                {
                    Console.WriteLine();
                    Output("Steamless failed to process the file.", ConsoleOutputType.Error);
                }
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.Read();
        }

        /// <summary>
        /// Resolves embedded resources to not load from disk.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private static Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Obtain the DLL name from the assembly..
            var dllName = args.Name.Contains(",") ? args.Name.Substring(0, args.Name.IndexOf(",", StringComparison.InvariantCultureIgnoreCase)) : args.Name.Replace(".dll", "");
            if (dllName.ToLower().EndsWith(".resources"))
                return null;

            // Build full path to the possible embedded resource..
            var fullName = $"{Assembly.GetExecutingAssembly().EntryPoint.DeclaringType?.Namespace}.Embedded.{new AssemblyName(args.Name).Name}.dll";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fullName))
            {
                // Return null if we are not valid..
                if (stream == null)
                    return null;

                // Read and load the embedded resource..
                var data = new byte[stream.Length];
                stream.Read(data, 0, (int)stream.Length);
                return Assembly.Load(data);
            }
        }

        /// <summary>
        /// Prints the header of this application.
        /// </summary>
        private static void PrintHeader()
        {
            var color = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n>> Steamless.NET v{((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyFileVersionAttribute), false)).Version}\n");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("(c) 2015-2016 atom0s [atom0s@live.com]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("For more info, visit http://atom0s.com/");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Special thanks to Cyanic for his research/help.");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================\n");

            Console.ForegroundColor = color;
        }

        /// <summary>
        /// Prints the help information on how to use this application.
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine($"{AppDomain.CurrentDomain.FriendlyName} [file] [options]\n");
            Console.WriteLine("Options:");
            Console.WriteLine("  --keepbind\t\tKeeps the .bind section inside of the unpacked file.");
        }

        /// <summary>
        /// Outputs a message to the console with the given color.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="outType"></param>
        /// <param name="color"></param>
        public static void Output(string message, ConsoleOutputType outType, ConsoleColor color = ConsoleColor.White)
        {
            // Store the original foreground color..
            var c = Console.ForegroundColor;

            // Prepare the new message build..
            var msg = "";

            // Set the color based on our message type..
            switch (outType)
            {
                case ConsoleOutputType.Info:
                    Console.ForegroundColor = ConsoleColor.White;
                    msg += $"[!] {message}";
                    break;
                case ConsoleOutputType.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    msg += $"[!] {message}";
                    break;
                case ConsoleOutputType.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    msg += $"[!] {message}";
                    break;
                case ConsoleOutputType.Success:
                    Console.ForegroundColor = ConsoleColor.Green;
                    msg += $"[!] {message}";
                    break;
                case ConsoleOutputType.Custom:
                    Console.ForegroundColor = color;
                    msg += message;
                    break;
            }

            // Print the message..
            Console.WriteLine(msg);

            // Restore the foreground color..
            Console.ForegroundColor = c;
        }

        /// <summary>
        /// Determines if the application was passed the given argument.
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        public static bool HasArgument(string arg) => Arguments != null && Arguments.Contains(arg.ToLower());

        /// <summary>
        /// Gets or sets the list of arguments passed to this application on load.
        /// </summary>
        public static List<string> Arguments { get; set; }
    }
}