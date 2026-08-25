// This file is part of PeggleEdit.
// Copyright Ted John 2010 - 2011. http://tedtycoon.co.uk
//
// PeggleEdit is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// PeggleEdit is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with PeggleEdit. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using IntelOrca.PeggleEdit.Tools.Pack;

namespace IntelOrca.PeggleEdit.Tools.Levels
{
    /// <summary>
    /// Writes a level as a JSON file plus its background and thumbnail as PNGs
    /// alongside it.
    /// </summary>
    /// <remarks>
    /// Peggle stores level art as JPEG 2000 (.jp2) inside the .pak archive.
    /// PakImage.Image already decodes that transparently via CSJ2K, so the only work
    /// here is re-encoding to PNG, which every engine can load. Note that PakImage
    /// swallows decode failures and returns null, so a missing image is normal and
    /// must not abort the export.
    /// </remarks>
    public static class LevelJsonExporter
    {
        public class Result
        {
            public string JsonPath { get; set; }
            public List<string> ImagePaths { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
        }

        /// <summary>
        /// Exports <paramref name="level"/> to <paramref name="jsonPath"/>.
        /// Images are written into the same directory using the JSON file's base name.
        /// </summary>
        public static Result Export(Level level, string jsonPath, bool exportImages = true, bool expandGenerators = true)
        {
            if (level == null)
                throw new ArgumentNullException(nameof(level));
            if (string.IsNullOrEmpty(jsonPath))
                throw new ArgumentException("A destination path is required.", nameof(jsonPath));

            var result = new Result { JsonPath = jsonPath };

            string directory = Path.GetDirectoryName(Path.GetFullPath(jsonPath));
            string baseName = Path.GetFileNameWithoutExtension(jsonPath);

            var writer = new LevelJsonWriter(level) { ExpandGenerators = expandGenerators };

            if (exportImages)
            {
                writer.BackgroundFileName = TrySaveImage(
                    level.Background, directory, baseName, result);

                writer.ThumbnailFileName = TrySaveImage(
                    level.Thumbnail, directory, baseName + "_thumb", result);
            }

            // The JSON is written last so that it never references a PNG that failed
            // to save.
            File.WriteAllText(jsonPath, writer.GetJson(), Encoding.UTF8);
            return result;
        }

        /// <summary>
        /// Decodes and saves one image as PNG. Returns the relative filename to record
        /// in the JSON, or null if there was nothing to save.
        /// </summary>
        private static string TrySaveImage(PakImage source, string directory, string baseName, Result result)
        {
            if (source == null)
                return null;

            try
            {
                // This triggers the .jp2 decode. It returns null rather than throwing
                // when CSJ2K cannot handle the file.
                var image = source.Image;
                if (image == null)
                {
                    result.Warnings.Add(
                        $"Could not decode '{source.FileName}'. If it is a .jp2, the Peggle " +
                        "installation path may need registering via J2K.RegisterPegglePath.");
                    return null;
                }

                string fileName = baseName + ".png";
                string fullPath = Path.Combine(directory, fileName);
                image.Save(fullPath, ImageFormat.Png);
                result.ImagePaths.Add(fullPath);
                return fileName;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to export '{source.FileName}': {ex.Message}");
                return null;
            }
        }
    }
}
