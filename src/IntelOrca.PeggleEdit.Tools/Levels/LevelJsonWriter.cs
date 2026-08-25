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
using System.Drawing;
using IntelOrca.PeggleEdit.Tools.Levels.Children;

namespace IntelOrca.PeggleEdit.Tools.Levels
{
    /// <summary>
    /// Writes a level as JSON in an engine-neutral schema.
    /// </summary>
    /// <remarks>
    /// This deliberately does not reuse the reflection-driven approach in
    /// <see cref="LevelXMLWriter"/>. That writer only emits properties carrying an
    /// [EntryProperty] attribute, and Polygon, Rod, Teleport, Emitter, Movement and
    /// all four generator types carry none — so they would silently export as empty
    /// shells. Every field below is written explicitly so that the schema is stable
    /// and adding a field is a deliberate act rather than a side effect of reflection.
    /// </remarks>
    public class LevelJsonWriter
    {
        /// <summary>
        /// Schema version. Increment on any breaking change and keep a migration
        /// path on the consuming side.
        /// </summary>
        public const int FormatVersion = 1;

        /// <summary>The playfield Peggle levels are authored against.</summary>
        public const int BoardWidth = 800;
        public const int BoardHeight = 600;

        private readonly Level _level;

        /// <summary>
        /// When true, generators are run so the output contains concrete pegs rather
        /// than generator parameters. This is almost always what a consuming engine
        /// wants: it avoids reimplementing bezier and radial placement maths.
        /// The generator definitions are still emitted under "generators" for
        /// round-tripping.
        /// </summary>
        public bool ExpandGenerators { get; set; } = true;

        /// <summary>
        /// Relative filename written into the "images" block for the background, e.g.
        /// "peggleland.png". Set by the exporter after it decides where the PNG lands.
        /// Null omits the entry.
        /// </summary>
        public string BackgroundFileName { get; set; }

        /// <summary>Relative filename for the thumbnail. Null omits the entry.</summary>
        public string ThumbnailFileName { get; set; }

        public bool Indent { get; set; } = true;

        public LevelJsonWriter(Level level)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
        }

        public string GetJson()
        {
            var w = new JsonTextWriter { Indent = Indent };

            w.StartObject();
            w.Write("format", FormatVersion);
            w.Write("generator", "PeggleEdit");
            w.Write("name", _level.Info.Name ?? string.Empty);
            w.Write("filename", _level.Info.Filename ?? string.Empty);
            w.Write("aceScore", _level.Info.AceScore);
            w.Write("minStage", _level.Info.MinStage);
            w.WriteInlineNumbers("board", BoardWidth, BoardHeight);

            if (BackgroundFileName != null || ThumbnailFileName != null)
            {
                w.StartObject("images");
                w.Write("background", BackgroundFileName);
                w.Write("thumbnail", ThumbnailFileName);
                w.EndObject();
            }

            var entries = GetEntriesForExport(out var expanded);
            w.Write("generatorsExpanded", expanded);

            // Movements are shared by reference: an entry points at a movement id
            // rather than inlining it, because MovementLink lets several entries
            // ride the same path.
            var movements = CollectMovements(entries);
            WriteMovements(w, movements);

            WritePegs(w, entries, movements);
            WriteBricks(w, entries, movements);
            WriteGeometry(w, entries);
            WriteTeleports(w, entries);
            WriteGenerators(w, entries);

            w.EndObject();
            return w.ToString();
        }

        /// <summary>
        /// Produces the entry list to export. When expanding, generators are run on a
        /// deep copy so the document the user has open is never mutated —
        /// IEntryFunction.Execute() adds pegs to the level and removes the generator.
        /// </summary>
        private List<LevelEntry> GetEntriesForExport(out bool expanded)
        {
            expanded = false;

            if (!ExpandGenerators)
                return new List<LevelEntry>(_level.Entries);

            var scratch = new Level();
            scratch.Info = _level.Info;

            foreach (LevelEntry entry in _level.Entries)
            {
                var clone = entry.Clone() as LevelEntry;
                if (clone == null)
                    continue;

                clone.Level = scratch;
                scratch.Entries.Add(clone);
            }

            // Execute() mutates the collection it belongs to, so iterate a snapshot.
            foreach (var entry in new List<LevelEntry>(scratch.Entries))
            {
                if (entry is IEntryFunction fn)
                {
                    try
                    {
                        fn.Execute();
                        expanded = true;
                    }
                    catch (Exception)
                    {
                        // A malformed generator should not abort the whole export.
                        // It stays in the list and is emitted under "generators".
                    }
                }
            }

            return new List<LevelEntry>(scratch.Entries);
        }

        private Dictionary<Movement, int> CollectMovements(List<LevelEntry> entries)
        {
            var map = new Dictionary<Movement, int>();
            foreach (var entry in entries)
            {
                var m = entry.MovementLink?.Movement;
                while (m != null && !map.ContainsKey(m))
                {
                    map.Add(m, map.Count);
                    m = m.MovementLink?.Movement;
                }
            }
            return map;
        }

        private void WriteMovements(JsonTextWriter w, Dictionary<Movement, int> movements)
        {
            w.StartArray("movements");
            foreach (var kvp in movements)
            {
                var m = kvp.Key;
                w.StartObject();
                w.Write("id", kvp.Value);
                w.Write("type", m.Type.ToString());
                w.WriteInlineNumbers("anchor", m.AnchorPointX, m.AnchorPointY);
                w.WriteInlineNumbers("object", m.ObjectX, m.ObjectY);
                w.Write("radius", m.Radius1);
                w.Write("speed", m.Speed);
                w.Write("phase", m.Phase);
                w.Write("offset", m.Offset);
                w.Write("rotation", m.Rotation);
                w.Write("movementAngle", m.MovementAngle);
                w.Write("maxAngle", m.MaxAngle);
                w.Write("reverse", m.Reverse);
                w.Write("pause1", m.Pause1);
                w.Write("pause2", m.Pause2);
                w.Write("postDelayPhase", m.PostDelayPhase);
                w.WriteInlineNumbers("subOffset", m.SubMovementOffsetX, m.SubMovementOffsetY);

                // A linked movement composes on top of this one (e.g. a peg orbiting a
                // point that is itself sliding). Emit the parent's id, not a copy.
                if (m.MovementLink?.Movement != null &&
                    movements.TryGetValue(m.MovementLink.Movement, out int parentId))
                {
                    w.Write("linkedTo", parentId);
                }

                w.EndObject();
            }
            w.EndArray();
        }

        private void WritePegs(JsonTextWriter w, List<LevelEntry> entries, Dictionary<Movement, int> movements)
        {
            w.StartArray("pegs");
            foreach (var entry in entries)
            {
                if (!(entry is Circle circle))
                    continue;

                w.StartObject();
                w.WriteInlineNumbers("pos", circle.X, circle.Y);
                w.Write("radius", circle.Radius);

                // CanBeOrange marks a peg as eligible to be selected as a goal peg.
                // Peggle picks the actual orange set at runtime; the level only says
                // which pegs are candidates.
                var pegInfo = circle.PegInfo;
                w.Write("canBeGoal", pegInfo != null && pegInfo.CanBeOrange);
                if (pegInfo != null && pegInfo.QuickDisappear)
                    w.Write("quickDisappear", true);

                WriteCommonEntryFields(w, entry, movements);
                w.EndObject();
            }
            w.EndArray();
        }

        private void WriteBricks(JsonTextWriter w, List<LevelEntry> entries, Dictionary<Movement, int> movements)
        {
            w.StartArray("bricks");
            foreach (var entry in entries)
            {
                if (!(entry is Brick brick))
                    continue;

                w.StartObject();
                w.WriteInlineNumbers("pos", brick.X, brick.Y);
                w.Write("width", brick.Width);
                w.Write("length", brick.Length);
                w.Write("rotation", brick.Rotation);
                w.Write("curved", brick.Curved);

                if (brick.Curved)
                {
                    w.Write("innerRadius", brick.InnerRadius);
                    w.Write("outerRadius", brick.OuterRadius);
                    w.Write("sectorAngle", brick.SectorAngle);
                    w.Write("curvePoints", brick.CurvePoints);
                }

                w.WriteInlineNumbers("leftSide", brick.LeftSidePosition.X, brick.LeftSidePosition.Y);
                w.WriteInlineNumbers("rightSide", brick.RightSidePosition.X, brick.RightSidePosition.Y);
                w.Write("leftSideAngle", brick.LeftSideAngle);
                w.Write("rightSideAngle", brick.RightSideAngle);

                var pegInfo = brick.PegInfo;
                w.Write("canBeGoal", pegInfo != null && pegInfo.CanBeOrange);

                WriteCommonEntryFields(w, entry, movements);
                w.EndObject();
            }
            w.EndArray();
        }

        /// <summary>
        /// Polygons and rods are collision geometry rather than scoring pegs, so they
        /// go in their own bucket.
        /// </summary>
        private void WriteGeometry(JsonTextWriter w, List<LevelEntry> entries)
        {
            w.StartArray("geometry");
            foreach (var entry in entries)
            {
                if (entry is Polygon polygon)
                {
                    w.StartObject();
                    w.Write("kind", "polygon");
                    w.WriteInlineNumbers("pos", polygon.X, polygon.Y);
                    w.StartArray("points");
                    foreach (PointF p in polygon.Points ?? new PointF[0])
                        w.WriteInlineNumbers(null, p.X, p.Y);
                    w.EndArray();
                    w.Write("bouncy", polygon.Bouncy);
                    w.Write("rolly", polygon.Rolly);
                    w.EndObject();
                }
                else if (entry is Rod rod)
                {
                    w.StartObject();
                    w.Write("kind", "rod");
                    w.WriteInlineNumbers("a", rod.PointA.X, rod.PointA.Y);
                    w.WriteInlineNumbers("b", rod.PointB.X, rod.PointB.Y);
                    w.EndObject();
                }
            }
            w.EndArray();
        }

        private void WriteTeleports(JsonTextWriter w, List<LevelEntry> entries)
        {
            w.StartArray("teleports");
            foreach (var entry in entries)
            {
                if (!(entry is Teleport teleport))
                    continue;

                w.StartObject();
                w.WriteInlineNumbers("pos", teleport.X, teleport.Y);
                w.WriteInlineNumbers("size", teleport.Width, teleport.Height);
                w.WriteInlineNumbers("destination", teleport.Destination.X, teleport.Destination.Y);
                w.EndObject();
            }
            w.EndArray();
        }

        /// <summary>
        /// Emitted for round-tripping and for engines that would rather resolve the
        /// curves themselves. When ExpandGenerators is on, the concrete pegs these
        /// produce are already present in "pegs" and this array will normally be empty.
        /// </summary>
        private void WriteGenerators(JsonTextWriter w, List<LevelEntry> entries)
        {
            w.StartArray("generators");
            foreach (var entry in entries)
            {
                if (entry is CurveGenerator curve)
                {
                    w.StartObject();
                    w.Write("kind", entry is BrickCurveGenerator ? "brickCurve" : "pegCurve");
                    w.WriteInlineNumbers("pos", curve.X, curve.Y);
                    w.Write("interval", curve.Interval);

                    var path = curve.BezierPath;
                    if (path != null)
                    {
                        w.Write("svg", path.Svg);
                        w.StartArray("points");
                        for (int i = 0; i < path.NumPoints; i++)
                        {
                            PointF p = path.GetPosition(i);
                            w.StartObject();
                            w.WriteInlineNumbers("pos", p.X, p.Y);
                            w.Write("kind", path.PointKinds[i].ToString());
                            w.EndObject();
                        }
                        w.EndArray();
                    }
                    w.EndObject();
                }
                else if (entry is PegGenerator pegGen)
                {
                    w.StartObject();
                    w.Write("kind", "pegRing");
                    w.WriteInlineNumbers("pos", pegGen.X, pegGen.Y);
                    w.WriteInlineNumbers("radius", pegGen.RadiusX, pegGen.RadiusY);
                    w.Write("count", pegGen.NumberOfPegs);
                    w.Write("maxCount", pegGen.MaxNumberOfPegs);
                    w.Write("angularOffset", pegGen.AngularOffset);
                    w.EndObject();
                }
                else if (entry is BrickGenerator brickGen)
                {
                    w.StartObject();
                    w.Write("kind", "brickRing");
                    w.WriteInlineNumbers("pos", brickGen.X, brickGen.Y);
                    w.Write("innerRadius", brickGen.InnerRadius);
                    w.Write("outerRadius", brickGen.OuterRadius);
                    w.Write("brickWidth", brickGen.BrickWidth);
                    w.Write("brickRadius", brickGen.BrickRadius);
                    w.Write("sectorAngles", brickGen.SectorAngles);
                    w.Write("count", brickGen.NumberOfBricks);
                    w.Write("maxCount", brickGen.MaxNumberOfBricks);
                    w.Write("angularOffset", brickGen.AngularOffset);
                    w.EndObject();
                }
            }
            w.EndArray();
        }

        /// <summary>
        /// Physics and rendering flags shared by every entry type. Defaults are
        /// omitted to keep files readable; a consumer should assume collision and
        /// visibility are on unless told otherwise.
        /// </summary>
        private void WriteCommonEntryFields(JsonTextWriter w, LevelEntry entry, Dictionary<Movement, int> movements)
        {
            if (!entry.Collision)
                w.Write("collision", false);
            if (!entry.Visible)
                w.Write("visible", false);
            if (entry.Bouncy != 0f)
                w.Write("bouncy", entry.Bouncy);
            if (entry.Rolly != 0f)
                w.Write("rolly", entry.Rolly);
            if (entry.MaxBounceVelocity != 0f)
                w.Write("maxBounceVelocity", entry.MaxBounceVelocity);
            if (entry.Background)
                w.Write("background", true);
            if (entry.Foreground)
                w.Write("foreground", true);
            if (!string.IsNullOrEmpty(entry.ID))
                w.Write("id", entry.ID);

            var movement = entry.MovementLink?.Movement;
            if (movement != null && movements.TryGetValue(movement, out int movementId))
                w.Write("movement", movementId);
        }
    }
}
