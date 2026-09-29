using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public static class SmartCollectionService
    {
        public static IReadOnlyList<PhotoInfo> Filter(IEnumerable<PhotoInfo> photos, SmartCollectionDefinition definition)
        {
            if (photos == null) return Array.Empty<PhotoInfo>();

            IEnumerable<PhotoInfo> q = photos;
            if (definition.Year.HasValue)
                q = q.Where(p => p.DateTaken?.Year == definition.Year.Value);
            if (definition.YearFrom.HasValue)
                q = q.Where(p => p.DateTaken?.Year >= definition.YearFrom.Value);
            if (definition.YearTo.HasValue)
                q = q.Where(p => p.DateTaken?.Year <= definition.YearTo.Value);
            if (!string.IsNullOrWhiteSpace(definition.CameraMake))
                q = q.Where(p => Contains(p.CameraMake, definition.CameraMake));
            if (!string.IsNullOrWhiteSpace(definition.CameraModel))
                q = q.Where(p => Contains(p.CameraModel, definition.CameraModel));
            if (!string.IsNullOrWhiteSpace(definition.LensModel))
                q = q.Where(p => Contains(p.LensModel, definition.LensModel));
            if (!string.IsNullOrWhiteSpace(definition.Extension))
                q = q.Where(p => string.Equals(Path.GetExtension(p.FilePath), NormalizeExtension(definition.Extension), StringComparison.OrdinalIgnoreCase));
            if (definition.HasGps.HasValue)
                q = q.Where(p => (p.Latitude.HasValue && p.Longitude.HasValue) == definition.HasGps.Value);
            if (definition.HasExifDate.HasValue)
                q = q.Where(p => p.DateTaken.HasValue == definition.HasExifDate.Value);
            if (definition.MinimumWidth.HasValue)
                q = q.Where(p => p.PixelWidth.GetValueOrDefault() >= definition.MinimumWidth.Value);
            if (definition.MinimumHeight.HasValue)
                q = q.Where(p => p.PixelHeight.GetValueOrDefault() >= definition.MinimumHeight.Value);
            if (definition.MinimumMegapixels.HasValue)
                q = q.Where(p => p.Megapixels.GetValueOrDefault() >= definition.MinimumMegapixels.Value);
            if (definition.IsoMinimum.HasValue)
                q = q.Where(p => p.Iso.GetValueOrDefault() >= definition.IsoMinimum.Value);
            if (definition.IsoMaximum.HasValue)
                q = q.Where(p => p.Iso.GetValueOrDefault() <= definition.IsoMaximum.Value);
            if (definition.RatingMinimum.HasValue)
                q = q.Where(p => p.Rating.GetValueOrDefault() >= definition.RatingMinimum.Value);
            if (definition.FocalLengthMinimum.HasValue)
                q = q.Where(p => p.FocalLengthMm.GetValueOrDefault() >= definition.FocalLengthMinimum.Value);
            if (definition.FocalLengthMaximum.HasValue)
                q = q.Where(p => p.FocalLengthMm.GetValueOrDefault() <= definition.FocalLengthMaximum.Value);
            if (!string.IsNullOrWhiteSpace(definition.DirectoryContains))
                q = q.Where(p => Contains(p.Directory, definition.DirectoryContains));
            if (!string.IsNullOrWhiteSpace(definition.LocationContains))
                q = q.Where(p => Contains(p.LocationString, definition.LocationContains));

            return q.ToList();
        }

        public static bool Matches(PhotoInfo photo, SmartCollectionDefinition definition)
            => Filter(new[] { photo }, definition).Count != 0;

        private static bool Contains(string? source, string? value) =>
            !string.IsNullOrWhiteSpace(source) &&
            !string.IsNullOrWhiteSpace(value) &&
            source.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeExtension(string extension)
        {
            var value = extension.Trim();
            return value.StartsWith('.') ? value : "." + value;
        }
    }
}
