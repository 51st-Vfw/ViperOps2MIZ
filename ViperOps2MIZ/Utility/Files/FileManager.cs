// ********************************************************************************************************************
//
// FileManager.cs : basic file management operations
//
// Copyright(C) 2025 ilominar/raven
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
// implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If not, see
// <https://www.gnu.org/licenses/>.
//
// ********************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;

namespace ViperOps2MIZ.Utility.Files
{
    /// <summary>
    /// TODO: document
    /// </summary>
    public sealed class FileManager
    {
        // ------------------------------------------------------------------------------------------------------------
        //
        // file management
        //
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// copies file from source to destination, overwriting destination if it already exists.
        /// </summary>
        public static void CopyFile(string src, string dest)
        {
            File.Copy(src, dest, overwrite: true);
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // zip file handling
        //
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// return the contents of the text file at the specified path within a zip file at the specified path. callers
        /// should protect this with a try/catch.
        /// </summary>
        public static string ReadFileFromZip(string zipPath, string filePath)
        {
            ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Read)
                                 ?? throw new Exception($"Unable to open Zip archive {zipPath}");
            ZipArchiveEntry entry = archive.GetEntry(filePath)
                                    ?? throw new Exception($"Unable to get entry {filePath} from Zip archive {zipPath}");
            string data = new StreamReader(entry.Open()).ReadToEnd();
            archive.Dispose();

            return data;
        }

        /// <summary>
        /// return the contents of the text file at the specified path within a zip file at the specified path. callers
        /// should protect this with a try/catch.
        /// </summary>
        public static void WriteDataToZip(string zipPath, string filePath, string data)
        {
            ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Update)
                                 ?? throw new Exception($"Unable to open Zip archive {zipPath}");

            ZipArchiveEntry entryOld = archive?.GetEntry(filePath)
                                       ?? throw new Exception($"Unable to get entry {filePath} from Zip archive {zipPath}");
            entryOld.Delete();

            ZipArchiveEntry entryNew = archive.CreateEntry(filePath)
                            ?? throw new Exception($"Unable to create entry {filePath} from Zip archive {zipPath}");
            StreamWriter writer = new(entryNew.Open());
            List<string> lines = [.. data.Split("\n") ];
            foreach (string line in lines)
                writer.Write($"{line}\n");
            writer.Close();

            archive.Dispose();
        }
    }
}
